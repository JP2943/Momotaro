using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-05 受入修正：敵の物理ルートが接地を保つことを実 Rigidbody／実 Collider で検証する。EnemyMotor が全回転＋Y 位置を
    /// 固定するため、押し出し（別 Collider との重なり）や上向き速度指示があっても Y は動かず（浮き上がらず）、地面高さの Hitbox
    /// （OverlapBox）が移動中の敵を取りこぼさない。物理は手動シミュレーション（<see cref="SimulationMode.Script"/>＋
    /// <see cref="Physics.Simulate"/>）で決定的に確認し、simulationMode・レイヤー衝突状態を復元する。
    /// </summary>
    public sealed class EnemyGroundingPlayTests
    {
        private SimulationMode _prevMode;
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            CombatLayers.EnsureCollisionPolicy();
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

        private static EnemyArchetypeData MakeArchetype()
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            SetField(a, "_maxHp", 40);
            SetField(a, "_defense", 0f);
            SetField(a, "_poiseMax", 30f);
            SetField(a, "_flinchResistance", 20f);
            SetField(a, "_stunSeconds", 3f);
            return a;
        }

        // Prefab と同じ接地構成（root 原点=地面、Collider 0..1、EnemyMotor が constraints=116 を設定）。
        private GameObject BuildGroundedEnemy(Vector3 pos)
        {
            var go = new GameObject("GroundedEnemy");
            _spawned.Add(go);
            go.transform.position = pos;
            go.SetActive(false);

            go.AddComponent<Rigidbody>();
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            col.center = new Vector3(0f, 0.5f, 0f);

            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", MakeArchetype());
            go.AddComponent<EnemyMotor>(); // Awake で全回転＋Y 位置を固定する。

            go.SetActive(true);
            return go;
        }

        private GameObject BuildBlock(int layer, Vector3 pos)
        {
            var go = new GameObject("Block") { layer = layer };
            _spawned.Add(go);
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            go.transform.position = pos;
            return go;
        }

        [UnityTest]
        public IEnumerator Motor_AppliesGroundedConstraints_OnAwake()
        {
            GameObject enemy = BuildGroundedEnemy(Vector3.zero);
            yield return null;

            var rb = enemy.GetComponent<Rigidbody>();
            RigidbodyConstraints expected = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            Assert.AreEqual(expected, rb.constraints, "EnemyMotor が全回転＋Y 位置固定（=116）を適用する。");
            Assert.IsFalse(rb.useGravity, "重力は使わない。");
        }

        [UnityTest]
        public IEnumerator Y_StaysGrounded_DespiteUpwardVelocityAndDepenetration()
        {
            GameObject enemy = BuildGroundedEnemy(Vector3.zero);
            var rb = enemy.GetComponent<Rigidbody>();
            yield return null;

            // 重なり Collider（Default/壁）で押し出しを誘発しつつ、上向き速度を毎ステップ指示する。
            BuildBlock(CombatLayers.WallLayer, new Vector3(0.3f, 0.5f, 0f));

            for (int i = 0; i < 60; i++)
            {
                rb.linearVelocity = new Vector3(1f, 5f, 0f); // わざと +Y を混ぜる。
                Physics.Simulate(0.02f);
            }

            Assert.LessOrEqual(Mathf.Abs(rb.position.y), 0.01f,
                "Y 位置固定により、押し出し・上向き速度があっても敵は浮き上がらない。");
        }

        [UnityTest]
        public IEnumerator GroundLevelHitbox_CatchesMovingEnemy()
        {
            GameObject enemy = BuildGroundedEnemy(Vector3.zero);
            var rb = enemy.GetComponent<Rigidbody>();
            var actor = enemy.GetComponent<EnemyActor>();
            yield return null;

            // 敵を +X へ移動させる（Enemy↔ 何もぶつからない空間）。
            for (int i = 0; i < 15; i++)
            {
                rb.linearVelocity = new Vector3(3f, 0f, 0f);
                Physics.Simulate(0.02f);
            }

            Assert.Greater(rb.position.x, 0.3f, "敵は移動している。");
            Assert.LessOrEqual(Mathf.Abs(rb.position.y), 0.01f, "移動中も接地を保つ。");

            // 地面高さ（center y=0.5, half 0.5）の Hitbox が現在位置の敵を捉える。
            Physics.SyncTransforms();
            Vector3 boxCenter = new Vector3(rb.position.x, 0.5f, rb.position.z);
            var buffer = new Collider[16];
            int count = Physics.OverlapBoxNonAlloc(boxCenter, new Vector3(0.5f, 0.5f, 0.5f), buffer,
                Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

            IDamageable found = null;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] == null)
                {
                    continue;
                }

                var d = buffer[i].GetComponentInParent<IDamageable>();
                if (d != null)
                {
                    found = d;
                    break;
                }
            }

            Assert.IsNotNull(found, "地面高さの Hitbox が移動中の敵を検出（浮き上がりによる空振りが起きない）。");
            Assert.AreSame(actor, found, "検出対象は EnemyActor。");
        }
    }
}
