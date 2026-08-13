using Momotaro.Gameplay.Enemy;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-12：<see cref="EnemyTestFieldController"/> が実際の完成敵 Prefab から編成どおりの敵を生成し、Scene 全体の有効な
    /// <see cref="EnemyActor"/> 数が仕様値と一致すること、切替で前の敵が残らないこと、Clear で 0 体になること、生成敵のルート Y=0 を検証する。
    /// EditMode で AssetDatabase から実 Prefab を読み込み、決定的に確認する（合成敵ではなく完成 Prefab を使用）。
    /// </summary>
    public sealed class EnemyTestFieldControllerTests
    {
        private const string Melee = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";
        private const string Ranged = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Ranged_Prototype.prefab";
        private const string Elite = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        private EnemyTestFieldController _controller;

        [SetUp]
        public void SetUp()
        {
            GameObject melee = AssetDatabase.LoadAssetAtPath<GameObject>(Melee);
            GameObject ranged = AssetDatabase.LoadAssetAtPath<GameObject>(Ranged);
            GameObject elite = AssetDatabase.LoadAssetAtPath<GameObject>(Elite);
            Assert.IsNotNull(melee); Assert.IsNotNull(ranged); Assert.IsNotNull(elite);

            var go = new GameObject("TestFieldController");
            _controller = go.AddComponent<EnemyTestFieldController>();
            _controller.ConfigurePrefabs(melee, ranged, elite);
        }

        [TearDown]
        public void TearDown()
        {
            if (_controller != null)
            {
                Object.DestroyImmediate(_controller.gameObject); // OnDisable→Clear で生成敵も破棄。
            }
        }

        private static int ActiveEnemies()
        {
            return Object.FindObjectsByType<EnemyActor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        }

        [Test]
        public void Formations_ProduceExactSceneWideActiveEnemyCount()
        {
            Assert.AreEqual(0, ActiveEnemies(), "初期は 0 体。");

            Assert.AreEqual(1, _controller.Apply(EnemyTestFormation.Melee1));
            Assert.AreEqual(1, ActiveEnemies(), "近接1。");

            Assert.AreEqual(1, _controller.Apply(EnemyTestFormation.Ranged1));
            Assert.AreEqual(1, ActiveEnemies(), "遠距離1（前の敵は残らない）。");

            Assert.AreEqual(1, _controller.Apply(EnemyTestFormation.Elite1));
            Assert.AreEqual(1, ActiveEnemies(), "強敵1。");

            Assert.AreEqual(3, _controller.Apply(EnemyTestFormation.Group3));
            Assert.AreEqual(3, ActiveEnemies(), "3体混成。");

            Assert.AreEqual(6, _controller.Apply(EnemyTestFormation.Melee6));
            Assert.AreEqual(6, ActiveEnemies(), "近接6。");

            Assert.AreEqual(6, _controller.Apply(EnemyTestFormation.Mixed6));
            Assert.AreEqual(6, ActiveEnemies(), "混成6。");

            Assert.AreEqual(8, _controller.Apply(EnemyTestFormation.Max8));
            Assert.AreEqual(8, ActiveEnemies(), "最大8。");

            _controller.Clear();
            Assert.AreEqual(0, ActiveEnemies(), "Clear で 0 体。");
        }

        [Test]
        public void SpawnedEnemies_HaveRootYZero()
        {
            _controller.Apply(EnemyTestFormation.Group3);
            foreach (EnemyActor a in Object.FindObjectsByType<EnemyActor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Assert.AreEqual(0f, a.transform.position.y, 1e-4f, "生成敵のルート Y=0。");
            }
        }
    }
}
