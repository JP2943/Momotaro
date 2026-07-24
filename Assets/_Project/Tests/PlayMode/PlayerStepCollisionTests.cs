using System.Collections;
using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P2-09：ステップ（および移動一般）の物理的挙動を実 Physics で検証する。敵は Enemy レイヤーへ置き Player（Default）と衝突しない
    /// ため通過でき、壁（Default）は Player と衝突するため停止する。手動シミュレーション（<see cref="Physics.Simulate"/>）で決定的に確認する。
    /// </summary>
    public sealed class PlayerStepCollisionTests
    {
        private SimulationMode _prevMode;
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script; // 手動ステップ
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _prevMode;
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _spawned.Clear();
        }

        private Rigidbody MakePlayer()
        {
            var go = new GameObject("Player") { layer = CombatLayers.PlayerLayer };
            _spawned.Add(go);
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.5f;
            col.height = 2f;
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.transform.position = Vector3.zero;
            return rb;
        }

        private GameObject MakeBlock(int layer, Vector3 pos)
        {
            var go = new GameObject("Block") { layer = layer };
            _spawned.Add(go);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(1f, 2f, 6f); // 前方(+X)へ十分な壁面
            go.transform.position = pos; // 面は x = pos.x - 0.5
            return go;
        }

        private static void Advance(Rigidbody rb, Vector3 velocity, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                rb.linearVelocity = velocity; // ステップ移動相当（毎ステップ速度を維持）
                Physics.Simulate(0.02f);
            }
        }

        [UnityTest]
        public IEnumerator Player_PassesThroughEnemy()
        {
            int enemy = CombatLayers.EnemyLayer;
            if (enemy < 0)
            {
                Assert.Ignore("Enemy レイヤーが未定義。TagManager を確認。");
            }

            var rb = MakePlayer();
            MakeBlock(enemy, new Vector3(2f, 0f, 0f)); // Enemy レイヤーの敵ボディ
            Physics.IgnoreLayerCollision(CombatLayers.PlayerLayer, enemy, true);

            Advance(rb, new Vector3(4f, 0f, 0f), 100); // 2 秒相当

            Assert.Greater(rb.transform.position.x, 3f, "敵をすり抜けて前進できる。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Player_StopsAtWall()
        {
            var rb = MakePlayer();
            MakeBlock(CombatLayers.PlayerLayer, new Vector3(2f, 0f, 0f)); // 壁（Default）。面は x=1.5

            Advance(rb, new Vector3(4f, 0f, 0f), 100);

            Assert.Less(rb.transform.position.x, 1.6f, "壁で停止する（面 x=1.5・半径0.5 で中心 ~1.0）。");
            yield return null;
        }
    }
}
