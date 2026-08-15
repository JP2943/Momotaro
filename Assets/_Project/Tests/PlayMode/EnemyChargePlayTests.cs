using System.Collections;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-09：強敵の突進（§9.3）。<see cref="EnemyMotor.SetCharge"/> で狙い方向へ前進し、壁（Enemy↔Default 衝突）で停止して貫通しない
    /// ことを実 Rigidbody・実 Collision で検証する。突進速度は移動速度と独立（charge speed）。
    /// </summary>
    public sealed class EnemyChargePlayTests
    {
        private readonly System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject g in _spawned) { if (g != null) Object.Destroy(g); }
            _spawned.Clear();
        }

        private static void SetField(object t, string n, object v)
        {
            System.Type ty = t.GetType(); FieldInfo f = null;
            while (ty != null && f == null) { f = ty.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); ty = ty.BaseType; }
            Assert.IsNotNull(f, "field not found: " + n); f.SetValue(t, v);
        }

        private EnemyMotor BuildChargerAt(Vector3 pos)
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            SetField(arch, "_maxHp", 200);
            SetField(arch, "_moveSpeed", 3f);

            var go = new GameObject("Charger");
            _spawned.Add(go);
            go.transform.position = pos;
            go.SetActive(false);
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            col.center = new Vector3(0, 0.5f, 0);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            var motor = go.AddComponent<EnemyMotor>();
            go.SetActive(true);
            return motor;
        }

        private GameObject BuildWallAt(Vector3 pos)
        {
            var go = new GameObject("Wall");
            _spawned.Add(go);
            go.transform.position = pos;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(4f, 2f, 1f);
            return go;
        }

        [UnityTest]
        public IEnumerator Charge_MovesForward_WhenNoObstacle()
        {
            EnemyMotor motor = BuildChargerAt(Vector3.zero);
            motor.SetCharge(new Vector3(0, 0, 20f), 8f);
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            Assert.Greater(motor.transform.position.z, 2f, "突進で前進する。");
        }

        [UnityTest]
        public IEnumerator Charge_StopsAtWall_NoPenetration()
        {
            EnemyMotor motor = BuildChargerAt(Vector3.zero);
            BuildWallAt(new Vector3(0, 0.5f, 3f)); // 壁前面 z=2.5
            motor.SetCharge(new Vector3(0, 0, 20f), 8f);
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

            float z = motor.transform.position.z;
            Assert.Greater(z, 0.5f, "壁まで前進する。");
            Assert.Less(z, 2.6f, "壁で停止し貫通しない（前面 z=2.5 を超えない）。");
        }
    }
}
