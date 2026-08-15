using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-08 受入修正：敵の矢が Phase 2 の <b>実主人公被弾経路</b>（<see cref="PlayerVitalsHolder"/>）を、近接攻撃と共通の契約で通ることを
    /// 検証する。無防備＝HP ダメージ、Guard＝ガード結果、Just Guard＝HP 無傷＋発射者体幹返却、Step 無敵＝ダメージ無効を、近接
    /// （EnemyMeleeCombatTests）と同じ Fixture（FakeDefenses＋実 PlayerVitalsHolder＋EnemyHitFactory）で確認する。期待値は Phase 2 準拠。
    /// 実 Collider・実 Overlap で矢を飛ばし、被弾側の解決結果を確認する（Step は決定的化のため FixedUpdate を無効化して駆動）。
    /// </summary>
    public sealed class EnemyProjectilePlayerCombatTests
    {
        private readonly List<GameObject> _spawnedGo = new List<GameObject>();
        private readonly List<UnityEngine.Object> _spawnedObj = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject g in _spawnedGo) { if (g != null) UnityEngine.Object.DestroyImmediate(g); }
            foreach (UnityEngine.Object o in _spawnedObj) { if (o != null) UnityEngine.Object.DestroyImmediate(o); }
            _spawnedGo.Clear();
            _spawnedObj.Clear();
        }

        private static void SetField(object t, string n, object v)
        {
            Type ty = t.GetType(); FieldInfo f = null;
            while (ty != null && f == null) { f = ty.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); ty = ty.BaseType; }
            Assert.IsNotNull(f, "field not found: " + n); f.SetValue(t, v);
        }

        // Phase 2 の主人公防御状態を注入する Fake（EnemyMeleeCombatTests と同型）。
        private sealed class FakeDefenses : MonoBehaviour, IGuardState, IJustGuardState, IEvadeState
        {
            public bool Guarding;
            public bool CanJG;
            public bool Invincible;
            public Vector3 Fwd = Vector3.forward;
            public bool IsGuarding => Guarding;
            public Vector3 GuardForward => Fwd;
            public bool CanJustGuard => CanJG;
            public void NotifyJustGuardSuccess() => CanJG = false;
            public bool IsInvincible => Invincible;
        }

        private sealed class Recorder : IHitResultListener
        {
            public HitResult Last;
            public bool Got;
            public void OnHitResult(in HitResult r) { Got = true; Last = r; }
        }

        private (PlayerVitalsHolder holder, FakeDefenses def, Recorder rec) MakePlayer(Vector3 pos)
        {
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawnedObj.Add(data);
            SetField(data, "_maxHp", 100);
            SetField(data, "_defense", 0f);

            var go = new GameObject("Player");
            _spawnedGo.Add(go);
            go.transform.position = pos;
            go.SetActive(false);
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            var def = go.AddComponent<FakeDefenses>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", data);
            go.SetActive(true);
            var rec = new Recorder();
            holder.Results.AddListener(rec);
            return (holder, def, rec);
        }

        private EnemyActor MakeEnemyOwner(float poiseMax)
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawnedObj.Add(arch);
            SetField(arch, "_maxHp", 100);
            SetField(arch, "_attackPower", 40f);
            SetField(arch, "_poiseMax", poiseMax);

            var go = new GameObject("EnemyOwner");
            _spawnedGo.Add(go);
            go.transform.position = new Vector3(0, 1, 5);
            go.SetActive(false);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            go.SetActive(true);
            return actor;
        }

        private EnemyAttackData MakeShotData()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawnedObj.Add(d);
            SetField(d, "_attackClass", EnemyAttackClass.Projectile);
            SetField(d, "_hpMultiplier", 1.0f);
            SetField(d, "_poiseDamage", 10f);
            SetField(d, "_flinchPower", 30f);
            SetField(d, "_guardStaminaCost", 12f);
            SetField(d, "_justGuardPoiseReturn", 18f);
            SetField(d, "_guardable", true);
            SetField(d, "_justGuardable", true);
            SetField(d, "_steppable", true);
            SetField(d, "_projectileSpeed", 10f);
            SetField(d, "_projectileMaxDistance", 20f);
            SetField(d, "_projectileLifetimeSeconds", 3f);
            return d;
        }

        // 矢を主人公の正面から飛ばす（進行方向 -Z、主人公 GuardForward=+Z で正対＝近接テストと同じ前方条件）。
        private EnemyProjectile FireAtPlayer(EnemyActor owner, Vector3 playerPos)
        {
            var go = new GameObject("Arrow");
            _spawnedGo.Add(go);
            var proj = go.AddComponent<EnemyProjectile>();
            proj.enabled = false;
            Vector3 origin = playerPos + new Vector3(0, 0, 2f); // 主人公の +Z 側から
            proj.Initialize(EnemyAttackSnapshot.From(MakeShotData()), origin, new Vector3(0, 0, -1f), owner, 40f, HitId.Single(1));
            for (int i = 0; i < 6 && proj.IsLive; i++)
            {
                proj.Step(0.1f);
            }

            return proj;
        }

        [Test]
        public void Projectile_NormalHit_DamagesPlayerHp()
        {
            var (holder, def, rec) = MakePlayer(new Vector3(0, 1, 0));
            EnemyActor owner = MakeEnemyOwner(60f);

            EnemyProjectile proj = FireAtPlayer(owner, holder.transform.position);

            Assert.IsTrue(rec.Got, "矢が主人公の被弾経路へ到達する。");
            Assert.AreEqual(HitResultKind.Damage, rec.Last.Kind, "無防備は Damage 結果（近接と共通）。");
            Assert.Less(holder.Vitals.Health.Current, holder.Vitals.Health.Max, "HP が減る。");
            Assert.IsFalse(proj.IsLive, "命中で矢は消滅（1 発 1Hit）。");
        }

        [Test]
        public void Projectile_Guard_ResultsInGuard_NoHpLoss()
        {
            var (holder, def, rec) = MakePlayer(new Vector3(0, 1, 0));
            def.Guarding = true;
            EnemyActor owner = MakeEnemyOwner(60f);
            int hp0 = holder.Vitals.Health.Current;

            FireAtPlayer(owner, holder.transform.position);

            Assert.AreEqual(HitResultKind.Guard, rec.Last.Kind, "正面ガードは Guard 結果（近接と共通）。");
            Assert.AreEqual(hp0, holder.Vitals.Health.Current, "ガードで HP ダメージ 0。");
        }

        [Test]
        public void Projectile_JustGuard_NoHpDamage_ReturnsOwnerPoise()
        {
            var (holder, def, rec) = MakePlayer(new Vector3(0, 1, 0));
            def.CanJG = true;
            EnemyActor owner = MakeEnemyOwner(60f);
            float ownerPoise0 = owner.CurrentPoise;
            int hp0 = holder.Vitals.Health.Current;

            FireAtPlayer(owner, holder.transform.position);

            Assert.AreEqual(HitResultKind.JustGuard, rec.Last.Kind, "JG 成立（近接と共通）。");
            Assert.AreEqual(hp0, holder.Vitals.Health.Current, "JG は HP ダメージ無し。");
            Assert.AreEqual(ownerPoise0 - 18f, owner.CurrentPoise, 1e-3f, "発射者へ体幹 18 返却（§9.1／Phase 2 経由）。");
        }

        [Test]
        public void Projectile_StepInvincible_NoDamage_AndConsumed()
        {
            var (holder, def, rec) = MakePlayer(new Vector3(0, 1, 0));
            def.Invincible = true;
            EnemyActor owner = MakeEnemyOwner(60f);
            int hp0 = holder.Vitals.Health.Current;

            EnemyProjectile proj = FireAtPlayer(owner, holder.transform.position);

            Assert.AreEqual(HitResultKind.Evade, rec.Last.Kind, "Step 無敵は Evade（命中無効。近接と共通）。");
            Assert.AreEqual(hp0, holder.Vitals.Health.Current, "無敵中はダメージ無し。");
            Assert.IsFalse(proj.IsLive, "無敵対象に接触した矢は消滅する（固定規則：接触で消費）。");
        }
    }
}
