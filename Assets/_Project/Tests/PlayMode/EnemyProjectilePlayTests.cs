using System;
using System.Collections;
using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Modes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-08：直線 Projectile を実 Collider・実 Overlap で検証（§9.2）。壁で消滅、敵 Faction は通過、主人公（敵対）へ 1 発 1Hit で命中し
    /// 消滅、寿命で破棄、発射者が消失していても攻撃者 null で例外を出さない、Pause 中は進まない。命中 HitInfo が JG 反射（発射者返却）値と
    /// 攻撃者を運ぶことも確認する。Guard／JG／Step／無敵の解決は Phase 2 の被弾側契約（近接と共通）に委ねる。決定的化のため collision は
    /// <see cref="EnemyProjectile.Step"/> シームで駆動（FixedUpdate は無効化）。Pause のみ実 FixedUpdate で確認する。
    /// </summary>
    public sealed class EnemyProjectilePlayTests
    {
        private readonly System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            GameModeProvider.Current = null;
            // 直後の別テストへ Collider を残さないよう即時破棄する（遅延 Destroy だと物理ワールドに古い壁/対象が残り、
            // 弾が前テストの Collider に当たって誤って消滅する）。
            foreach (GameObject g in _spawned) { if (g != null) UnityEngine.Object.DestroyImmediate(g); }
            _spawned.Clear();
        }

        private static void SetField(object t, string n, object v)
        {
            Type ty = t.GetType(); FieldInfo f = null;
            while (ty != null && f == null) { f = ty.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); ty = ty.BaseType; }
            Assert.IsNotNull(f, "field not found: " + n); f.SetValue(t, v);
        }

        private sealed class OwnerActor : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
        }

        private sealed class HitTarget : MonoBehaviour, IDamageable, ICombatActor
        {
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public int Received;
            public bool LastAttackerNull;
            public float LastJgPoise;
            public ICombatActor LastAttacker;
            public int DamageableId => GetInstanceID();
            public void ReceiveHit(in HitInfo hit)
            {
                Received++;
                LastAttacker = hit.Attacker;
                LastAttackerNull = hit.Attacker == null;
                LastJgPoise = hit.JustGuardPoiseDamage;
            }
            int ICombatActor.FloorId => 0;
            Vector3 ICombatActor.WorldPosition => transform.position;
            Vector3 ICombatActor.Forward => transform.forward;
        }

        private EnemyAttackData MakeShot()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            SetField(d, "_attackClass", EnemyAttackClass.Projectile);
            SetField(d, "_projectileSpeed", 10f);
            SetField(d, "_projectileMaxDistance", 15f);
            SetField(d, "_projectileLifetimeSeconds", 0.5f);
            SetField(d, "_poiseDamage", 8f);
            SetField(d, "_justGuardPoiseReturn", 18f);
            SetField(d, "_guardable", true);
            SetField(d, "_justGuardable", true);
            SetField(d, "_steppable", true);
            return d;
        }

        private EnemyProjectile MakeProjectile(ICombatActor owner, out EnemyAttackData data)
        {
            data = MakeShot();
            var go = new GameObject("Arrow");
            _spawned.Add(go);
            var proj = go.AddComponent<EnemyProjectile>();
            proj.enabled = false; // FixedUpdate を無効化し、Step で決定的に駆動する。
            proj.Initialize(EnemyAttackSnapshot.From(data), new Vector3(0, 1, 0), Vector3.forward, owner, 30f,
                HitId.Single(1));
            return proj;
        }

        private GameObject MakeCollider(string name, Vector3 pos, int layer)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            go.layer = layer;
            go.transform.position = pos;
            var c = go.AddComponent<BoxCollider>();
            c.size = Vector3.one;
            return go;
        }

        [Test]
        public void Projectile_DestroyedByWall()
        {
            MakeCollider("Wall", new Vector3(0, 1, 2f), 0); // Default(=Wall) layer, no IDamageable
            var proj = MakeProjectile(null, out _);
            bool alive = true;
            for (int i = 0; i < 5 && alive; i++) alive = proj.Step(0.1f);
            Assert.IsFalse(alive, "壁で消滅する。");
        }

        [Test]
        public void Projectile_PassesThroughEnemyFaction()
        {
            GameObject go = MakeCollider("Ally", new Vector3(0, 1, 2f), 0);
            go.AddComponent<HitTarget>().Faction = CombatFaction.Enemy;
            var proj = MakeProjectile(null, out _);
            bool alive = true;
            for (int i = 0; i < 3; i++) alive = proj.Step(0.1f); // z=3 通過
            Assert.IsTrue(alive, "敵 Faction は通過（消えない）。");
            Assert.AreEqual(0, go.GetComponent<HitTarget>().Received, "敵には命中しない。");
        }

        [Test]
        public void Projectile_HitsHostile_OnceThenDestroyed_CarriesJgReturnAndAttacker()
        {
            var ownerGo = new GameObject("Owner");
            _spawned.Add(ownerGo);
            var owner = ownerGo.AddComponent<OwnerActor>();

            GameObject go = MakeCollider("Player", new Vector3(0, 1, 2f), 0);
            var target = go.AddComponent<HitTarget>();
            var proj = MakeProjectile(owner, out _);

            bool alive = true;
            for (int i = 0; i < 3 && alive; i++) alive = proj.Step(0.1f);
            Assert.AreEqual(1, target.Received, "主人公（敵対）へ 1 発命中。");
            Assert.IsFalse(alive, "命中で消滅（1 発 1Hit）。");
            Assert.AreEqual(18f, target.LastJgPoise, 1e-4f, "JG 反射（発射者返却）値を運ぶ。");
            Assert.AreSame(owner, target.LastAttacker, "攻撃者は発射者（JG は発射者へ返る）。");
        }

        [Test]
        public void Projectile_OwnerDestroyed_NoException_AttackerNull()
        {
            var ownerGo = new GameObject("Owner");
            var owner = ownerGo.AddComponent<OwnerActor>();
            GameObject go = MakeCollider("Player", new Vector3(0, 1, 2f), 0);
            var target = go.AddComponent<HitTarget>();
            var proj = MakeProjectile(owner, out _);

            UnityEngine.Object.DestroyImmediate(ownerGo); // 発射者消失

            Assert.DoesNotThrow(() =>
            {
                bool alive = true;
                for (int i = 0; i < 3 && alive; i++) alive = proj.Step(0.1f);
            }, "発射者消失でも例外を出さない。");
            Assert.AreEqual(1, target.Received);
            Assert.IsTrue(target.LastAttackerNull, "攻撃者は null（JG 反射先なしでも安全）。");
        }

        [Test]
        public void Projectile_ExpiresByLifetime()
        {
            var proj = MakeProjectile(null, out _); // 障害物なし、lifetime 0.5s
            bool alive = true;
            for (int i = 0; i < 6 && alive; i++) alive = proj.Step(0.1f); // 0.6s
            Assert.IsFalse(alive, "寿命で破棄される。");
        }

        private sealed class FakeMode : IGameModeService
        {
            public GameMode Current { get; set; }
            public bool CanPause => true;
            public event Action<GameModeChanged> ModeChanged { add { } remove { } }
            public bool ChangeMode(GameMode next) { Current = next; return true; }
            public void AddListener(IGameModeListener l) { }
            public void RemoveListener(IGameModeListener l) { }
        }

        [UnityTest]
        public IEnumerator Projectile_DoesNotAdvance_WhilePaused()
        {
            var proj = MakeProjectile(null, out _);
            proj.enabled = true; // 実 FixedUpdate を使う。

            var mode = new FakeMode { Current = GameMode.Dialogue }; // Pause 相当（非 Gameplay）
            GameModeProvider.Current = mode;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, proj.Traveled, 1e-4f, "Pause 中は進まない。");

            mode.Current = GameMode.Combat; // 再開
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.Greater(proj.Traveled, 0f, "再開後は進む。");
        }
    }
}
