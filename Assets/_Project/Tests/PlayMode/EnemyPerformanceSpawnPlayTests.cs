using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Enemy;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-11：性能検証ハーネスが各分岐（近接6／近接4+遠2／最大8）を実際に生成し、切替で前回分を破棄することを実フレームで検証する
    /// （純粋な体数計算だけでなく、実 Instantiate まで通す）。生成した敵を数フレーム動かして参照例外が出ないことも確認する。
    /// </summary>
    public sealed class EnemyPerformanceSpawnPlayTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private static void SetField(object target, string name, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        private GameObject MakeEnemyTemplate()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 40);

            var go = new GameObject("EnemyTemplate");
            _spawned.Add(go);
            go.AddComponent<BoxCollider>();
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            return go;
        }

        private EnemyPerformanceHarness MakeHarness()
        {
            GameObject template = MakeEnemyTemplate();
            var go = new GameObject("Harness");
            _spawned.Add(go);
            var h = go.AddComponent<EnemyPerformanceHarness>();
            SetField(h, "_spawnOnStart", false); // テストが明示的に Spawn する。
            SetField(h, "_meleePrefab", template);
            SetField(h, "_rangedPrefab", template);
            SetField(h, "_elitePrefab", template);
            return h;
        }

        [UnityTest]
        public IEnumerator Spawn_EachBranch_ProducesExpectedCount_AndSwitchClears()
        {
            EnemyPerformanceHarness h = MakeHarness();
            yield return null; // Start（_spawnOnStart=false なので自動生成なし）。

            Assert.AreEqual(6, h.Spawn(EnemyPerformanceBranch.Melee6), "近接6。");
            Assert.AreEqual(6, h.SpawnedCount);
            for (int i = 0; i < 2; i++) yield return null; // 生成体を数フレーム動かす。

            Assert.AreEqual(6, h.Spawn(EnemyPerformanceBranch.Melee4Ranged2), "近接4+遠2＝6。");
            Assert.AreEqual(6, h.SpawnedCount, "切替で前回分を破棄（累積しない）。");
            for (int i = 0; i < 2; i++) yield return null;

            Assert.AreEqual(8, h.Spawn(EnemyPerformanceBranch.Max8), "最大8。");
            Assert.AreEqual(8, h.SpawnedCount);
            for (int i = 0; i < 2; i++) yield return null;

            h.Clear();
            Assert.AreEqual(0, h.SpawnedCount, "全破棄。");

            LogAssert.NoUnexpectedReceived();
        }
    }
}
