using System.Collections;
using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P2-09：ステップ（および移動一般）の物理的挙動を実 Physics で検証する。主人公は Player、敵は Enemy、壁は Default。
    /// Player↔Enemy のみ衝突無効なので Player は敵を通過でき、Player↔Default・Enemy↔Default は有効なので主人公も敵も壁で停止する。
    /// 手動シミュレーション（<see cref="Physics.Simulate"/>）で決定的に確認し、変更したレイヤー衝突状態を復元する。
    /// </summary>
    public sealed class PlayerStepCollisionTests
    {
        private SimulationMode _prevMode;
        private bool _prevIgnorePlayerEnemy;
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script; // 手動ステップ
            if (CombatLayers.PlayerLayer >= 0 && CombatLayers.EnemyLayer >= 0)
            {
                _prevIgnorePlayerEnemy = Physics.GetIgnoreLayerCollision(CombatLayers.PlayerLayer, CombatLayers.EnemyLayer);
            }

            CombatLayers.EnsureCollisionPolicy();
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _prevMode;
            if (CombatLayers.PlayerLayer >= 0 && CombatLayers.EnemyLayer >= 0)
            {
                Physics.IgnoreLayerCollision(CombatLayers.PlayerLayer, CombatLayers.EnemyLayer, _prevIgnorePlayerEnemy);
            }

            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _spawned.Clear();
        }

        private Rigidbody MakeMover(int layer)
        {
            var go = new GameObject("Mover") { layer = layer };
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
            col.size = new Vector3(1f, 2f, 6f); // 面は x = pos.x - 0.5
            go.transform.position = pos;
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
            if (CombatLayers.PlayerLayer < 0 || CombatLayers.EnemyLayer < 0)
            {
                Assert.Ignore("Player/Enemy レイヤーが未定義。TagManager を確認。");
            }

            var rb = MakeMover(CombatLayers.PlayerLayer);
            MakeBlock(CombatLayers.EnemyLayer, new Vector3(2f, 0f, 0f)); // Enemy レイヤーの敵ボディ

            Advance(rb, new Vector3(4f, 0f, 0f), 100); // 2 秒相当

            Assert.Greater(rb.transform.position.x, 3f, "Player は敵をすり抜けて前進できる。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Player_StopsAtWall()
        {
            if (CombatLayers.PlayerLayer < 0)
            {
                Assert.Ignore("Player レイヤーが未定義。");
            }

            var rb = MakeMover(CombatLayers.PlayerLayer);
            MakeBlock(CombatLayers.WallLayer, new Vector3(2f, 0f, 0f)); // 壁（Default）。面 x=1.5

            Advance(rb, new Vector3(4f, 0f, 0f), 100);

            Assert.Less(rb.transform.position.x, 1.6f, "Player は壁で停止する（中心 ~1.0）。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Enemy_StopsAtWall()
        {
            if (CombatLayers.EnemyLayer < 0)
            {
                Assert.Ignore("Enemy レイヤーが未定義。");
            }

            var rb = MakeMover(CombatLayers.EnemyLayer);
            MakeBlock(CombatLayers.WallLayer, new Vector3(2f, 0f, 0f)); // 壁（Default）

            Advance(rb, new Vector3(4f, 0f, 0f), 100);

            Assert.Less(rb.transform.position.x, 1.6f, "敵も壁（Default）で停止する（Enemy↔Default は有効）。");
            yield return null;
        }
    }
}
