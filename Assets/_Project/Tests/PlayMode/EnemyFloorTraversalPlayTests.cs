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
    /// P3-05 受入修正：接地面 Y=0 の実床 Collider の上で、接地敵（root Y=0／Collider 0..1／Y 位置固定）が EnemyMotor の
    /// 移動指示（Chase 相当）で水平移動でき、移動中も Y=0 を保ち、床接触だけでは <see cref="EnemyMotor.IsBlocked"/> にならない
    /// ことを検証する。壁で実際に前進できない場合にのみ IsBlocked になることも確認する（症状を隠さず、blocked 判定は残す）。
    /// 実ゲームループ（FixedUpdate＋自動物理）で駆動する（Script 手動シミュレーションは使わない）。
    /// </summary>
    public sealed class EnemyFloorTraversalPlayTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            CombatLayers.EnsureCollisionPolicy();
        }

        [TearDown]
        public void TearDown()
        {
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

        // 接地面 Y=0：上面が Y=0 になる実床 Collider（Default/壁レイヤー）。
        private void BuildFloor()
        {
            var go = new GameObject("Floor") { layer = CombatLayers.WallLayer };
            _spawned.Add(go);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(40f, 1f, 40f);
            go.transform.position = new Vector3(0f, -0.5f, 0f); // 上面 = -0.5 + 0.5 = 0。
        }

        private EnemyMotor BuildGroundedEnemy(Vector3 pos)
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
            var motor = go.AddComponent<EnemyMotor>();

            go.SetActive(true);
            motor.Configure(3.5f, 360f, 0.1f);
            return motor;
        }

        private GameObject BuildWall(Vector3 pos)
        {
            var go = new GameObject("Wall") { layer = CombatLayers.WallLayer };
            _spawned.Add(go);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(1f, 2f, 10f);
            go.transform.position = pos;
            return go;
        }

        [UnityTest]
        public IEnumerator Enemy_ChasesAcrossFloor_MaintainsYZero_NotBlocked()
        {
            if (CombatLayers.EnemyLayer < 0 || CombatLayers.WallLayer < 0)
            {
                Assert.Ignore("Enemy/Default レイヤーが未定義。");
            }

            BuildFloor();
            EnemyMotor motor = BuildGroundedEnemy(Vector3.zero);
            Transform enemy = motor.transform;
            yield return new WaitForFixedUpdate(); // Awake/Configure 反映

            motor.SetMoveTarget(new Vector3(6f, 0f, 0f)); // Chase 相当の前進指示。

            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(enemy.position.x, 1.5f, "実床の上を Chase 前進できる（床接触が水平移動を阻害しない）。");
            Assert.LessOrEqual(Mathf.Abs(enemy.position.y), 0.02f, "移動中も接地面 Y=0 を維持する。");
            Assert.IsFalse(motor.IsBlocked, "床への接触だけでは blocked にならない。");
        }

        [UnityTest]
        public IEnumerator Enemy_BlockedByWall_SetsIsBlocked()
        {
            if (CombatLayers.EnemyLayer < 0 || CombatLayers.WallLayer < 0)
            {
                Assert.Ignore("Enemy/Default レイヤーが未定義。");
            }

            BuildFloor();
            EnemyMotor motor = BuildGroundedEnemy(Vector3.zero);
            Transform enemy = motor.transform;
            BuildWall(new Vector3(1.2f, 0.5f, 0f)); // 壁面 x=0.7。敵前面 x+0.5 が当たる。
            yield return new WaitForFixedUpdate();

            motor.SetMoveTarget(new Vector3(6f, 0f, 0f)); // 壁の先へ前進を指示（実際には進めない）。

            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(enemy.position.x, 0.8f, "壁で停止し、めり込まない。");
            Assert.LessOrEqual(Mathf.Abs(enemy.position.y), 0.02f, "壁接触中も Y=0 を維持する。");
            Assert.IsTrue(motor.IsBlocked, "壁で実際に前進できない場合は IsBlocked になる（判定は残す）。");
        }
    }
}
