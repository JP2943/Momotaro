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
    /// P3-12：<see cref="EnemyTestFieldController"/> の実行時ライフサイクルを検証する。編成適用で Scene 全体の有効な
    /// <see cref="EnemyActor"/> 数が仕様値と一致し、切替で前の敵が残らず（破棄が遅延しても即時 非アクティブ化）、Clear で 0 体、数フレーム
    /// 動作させても Unexpected Error／Exception が出ないことを、実 <see cref="EnemyActor"/> コンポーネントで確認する（Scene 実総数を数える）。
    /// 完成 Prefab そのものの体数一致は <c>EnemyTestFieldControllerTests</c>（EditMode）で担保する。
    /// </summary>
    public sealed class EnemyTestFieldPlayTests
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

        private GameObject MakeRealEnemyTemplate()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 40);

            var go = new GameObject("EnemyTemplate");
            _spawned.Add(go);
            go.AddComponent<BoxCollider>();
            var actor = go.AddComponent<EnemyActor>(); // 実 EnemyActor。
            SetField(actor, "_archetype", arch);
            go.SetActive(false); // テンプレートは無効化（有効数に数えない）。生成コピーは Controller が有効化する。
            return go;
        }

        private EnemyTestFieldController MakeController()
        {
            GameObject template = MakeRealEnemyTemplate();
            var go = new GameObject("Controller");
            _spawned.Add(go);
            var c = go.AddComponent<EnemyTestFieldController>();
            c.ConfigurePrefabs(template, template, template);
            return c;
        }

        private static int ActiveEnemies()
        {
            return Object.FindObjectsByType<EnemyActor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        }

        [UnityTest]
        public IEnumerator Formations_SceneWideCounts_NoResidual_NoException()
        {
            EnemyTestFieldController c = MakeController();
            yield return null;
            Assert.AreEqual(0, ActiveEnemies(), "初期は 0 体。");

            (EnemyTestFormation formation, int expected)[] cases =
            {
                (EnemyTestFormation.Melee1, 1),
                (EnemyTestFormation.Ranged1, 1),
                (EnemyTestFormation.Elite1, 1),
                (EnemyTestFormation.Group3, 3),
                (EnemyTestFormation.Melee6, 6),
                (EnemyTestFormation.Mixed6, 6),
                (EnemyTestFormation.Max8, 8),
            };

            foreach ((EnemyTestFormation formation, int expected) in cases)
            {
                c.Apply(formation);
                // 旧敵は即時 非アクティブ化されるため、切替直後でも有効数は新編成のみ。
                Assert.AreEqual(expected, ActiveEnemies(), formation + " 直後の有効数。");
                for (int i = 0; i < 2; i++) yield return null; // 遅延破棄を消化。
                Assert.AreEqual(expected, ActiveEnemies(), formation + " 数フレーム後の有効数（残留なし）。");
            }

            c.Clear();
            yield return null;
            Assert.AreEqual(0, ActiveEnemies(), "Clear 後は 0 体。");

            LogAssert.NoUnexpectedReceived();
        }
    }
}
