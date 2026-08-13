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
    /// P3-12：検証シナリオ Launcher が各固定編成（近接1／遠距離1／強敵1／3体混成）を実際に生成し、切替で前回分を破棄することを実フレームで
    /// 検証する（明示手順から開始可能）。生成体を数フレーム動かして参照例外が出ないことも確認する。
    /// </summary>
    public sealed class EnemyScenarioLauncherPlayTests
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

        private GameObject MakeTemplate()
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

        private EnemyScenarioLauncher MakeLauncher()
        {
            GameObject template = MakeTemplate();
            var go = new GameObject("Launcher");
            _spawned.Add(go);
            var l = go.AddComponent<EnemyScenarioLauncher>();
            SetField(l, "_launchOnStart", false);
            SetField(l, "_meleePrefab", template);
            SetField(l, "_rangedPrefab", template);
            SetField(l, "_elitePrefab", template);
            return l;
        }

        [UnityTest]
        public IEnumerator Launch_EachScenario_ProducesExpectedCount_AndSwitchClears()
        {
            EnemyScenarioLauncher l = MakeLauncher();
            yield return null;

            Assert.AreEqual(1, l.Launch(EnemyScenario.Melee1), "近接1。");
            Assert.AreEqual(1, l.SpawnedCount);
            yield return null;

            Assert.AreEqual(1, l.Launch(EnemyScenario.Ranged1), "遠距離1。");
            Assert.AreEqual(1, l.SpawnedCount, "切替で前回分を破棄。");
            yield return null;

            Assert.AreEqual(1, l.Launch(EnemyScenario.Elite1), "強敵1。");
            yield return null;

            Assert.AreEqual(3, l.Launch(EnemyScenario.Group3), "3体混成（近接2＋遠距離1）。");
            Assert.AreEqual(3, l.SpawnedCount);
            for (int i = 0; i < 2; i++) yield return null;

            l.Clear();
            Assert.AreEqual(0, l.SpawnedCount, "全破棄。");

            LogAssert.NoUnexpectedReceived();
        }
    }
}
