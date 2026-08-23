using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Player;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-08A：主人公の被弾解決（<see cref="PlayerVitalsHolder.ReceiveHit"/>）が反応種別ごとに正しい外部変位／強制ひるみを要求することを検証する。
    /// Damage＝ヒットバック（AttackDirection・距離・時間）、通常 Guard＝ガードバック、JG＝押し出しなし＋近接攻撃者へ強制ひるみ、
    /// 飛び道具の JG＝射手をひるませない、Evade＝反応なし。実際の <c>GetComponentInParent</c> 取得経路（Motor／攻撃者）で確認する。
    /// </summary>
    public sealed class PlayerReactionTests
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

        private sealed class FakeGuardJustEvade : MonoBehaviour, IGuardState, IJustGuardState, IEvadeState
        {
            public bool Guarding;
            public Vector3 Fwd = Vector3.forward;
            public bool CanJG;
            public bool Invincible;
            public bool IsGuarding => Guarding;
            public Vector3 GuardForward => Fwd;
            public bool CanJustGuard => CanJG;
            public bool IsInvincible => Invincible;
            public void NotifyJustGuardSuccess() { CanJG = false; }
        }

        private sealed class FakeReactionMotor : MonoBehaviour, IReactionMotor
        {
            public int PushCount;
            public int ClearCount;
            public Vector3 LastDir;
            public float LastDistance;
            public float LastSeconds;
            public void PushReaction(Vector3 direction, float distance, float seconds)
            {
                PushCount++; LastDir = direction; LastDistance = distance; LastSeconds = seconds;
            }
            public void ClearReaction() { ClearCount++; }
        }

        // 攻撃者：ICombatActor（hit.Attacker）＋ IForcedFlinchReceiver（強制ひるみ記録）＋ IDamageable（JG 反射の受け先）。
        private sealed class FakeAttacker : MonoBehaviour, ICombatActor, IForcedFlinchReceiver, IDamageable
        {
            public int FlinchCount;
            public float LastFlinchSeconds;
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
            public int DamageableId => GetInstanceID();
            public void ReceiveHit(in HitInfo hit) { }
            public void ForceFlinch(float seconds) { FlinchCount++; LastFlinchSeconds = seconds; }
        }

        private PlayerData MakePlayerData(int maxHp, float defense, int maxStamina)
        {
            var d = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(d);
            SetField(d, "_maxHp", maxHp);
            SetField(d, "_defense", defense);
            SetField(d, "_maxStamina", maxStamina);
            return d;
        }

        private (PlayerVitalsHolder holder, FakeGuardJustEvade fake, FakeReactionMotor motor) MakePlayer(
            bool guarding = false, bool canJustGuard = false, bool invincible = false)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var fake = go.AddComponent<FakeGuardJustEvade>();
            fake.Guarding = guarding;
            fake.CanJG = canJustGuard;
            fake.Invincible = invincible;
            var motor = go.AddComponent<FakeReactionMotor>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", MakePlayerData(100, 20f, 100));
            go.SetActive(true);
            return (holder, fake, motor);
        }

        private FakeAttacker MakeAttacker()
        {
            var go = new GameObject("Attacker");
            _spawned.Add(go);
            return go.AddComponent<FakeAttacker>();
        }

        private static HitInfo Hit(ICombatActor attacker, IDamageable target, Vector3 attackDir,
            HitReaction reaction, bool guardable = true, bool justGuardable = true, int id = 1)
        {
            return new HitInfo(attacker, target, attackDir, Vector3.zero, new HitDamage(10f, 0f, 0f),
                    10f, 20f, guardable, justGuardable, HitId.Single(id))
                .WithReaction(reaction);
        }

        [Test]
        public void Damage_RequestsHitback_AlongAttackDirection_WithDistanceAndTime()
        {
            var (holder, _, motor) = MakePlayer(guarding: false, canJustGuard: false);
            var atk = MakeAttacker();
            var dir = new Vector3(0f, 0f, -1f);
            var reaction = new HitReaction(0.16f, 0.12f, 0.12f, isProjectile: false);

            holder.ReceiveHit(Hit(atk, holder, dir, reaction));

            Assert.AreEqual(1, motor.PushCount, "Damage でヒットバックを 1 回要求。");
            Assert.AreEqual(dir, motor.LastDir, "AttackDirection へ押し出す。");
            Assert.AreEqual(0.16f, motor.LastDistance, 1e-4f, "距離は命中の HitbackDistance。");
            Assert.AreEqual(0.12f, motor.LastSeconds, 1e-4f, "時間は HitbackSeconds。");
        }

        [Test]
        public void Guard_RequestsGuardback_NotHitback()
        {
            var (holder, _, motor) = MakePlayer(guarding: true, canJustGuard: false);
            var atk = MakeAttacker();
            var reaction = new HitReaction(0.16f, 0.12f, 0.12f, isProjectile: false);

            holder.ReceiveHit(Hit(atk, holder, -Vector3.forward, reaction));

            Assert.AreEqual(1, motor.PushCount, "通常ガードでガードバックを 1 回要求。");
            Assert.AreEqual(0.12f, motor.LastDistance, 1e-4f, "距離は GuardbackDistance（ヒットバックより小）。");
            Assert.AreEqual(0.12f, motor.LastSeconds, 1e-4f, "時間はヒットバック時間を流用。");
        }

        [Test]
        public void JustGuard_NoPush_AndForcesMeleeAttackerFlinch()
        {
            var (holder, _, motor) = MakePlayer(guarding: true, canJustGuard: true);
            var atk = MakeAttacker();
            var reaction = new HitReaction(0.16f, 0.12f, 0.12f, isProjectile: false);

            holder.ReceiveHit(Hit(atk, holder, -Vector3.forward, reaction));

            Assert.AreEqual(0, motor.PushCount, "JG は踏み止まり（ガードバック 0・押し出しなし）。");
            Assert.AreEqual(1, atk.FlinchCount, "近接攻撃者へ強制ひるみを付与。");
            Assert.AreEqual(0.35f, atk.LastFlinchSeconds, 1e-4f, "0.30〜0.40 の中央 0.35 秒。");
        }

        [Test]
        public void JustGuard_Projectile_DoesNotFlinchArcher()
        {
            var (holder, _, motor) = MakePlayer(guarding: true, canJustGuard: true);
            var atk = MakeAttacker();
            var reaction = new HitReaction(0.16f, 0.12f, 0.12f, isProjectile: true);

            holder.ReceiveHit(Hit(atk, holder, -Vector3.forward, reaction));

            Assert.AreEqual(0, atk.FlinchCount, "飛び道具の JG では射手本人をひるませない。");
            Assert.AreEqual(0, motor.PushCount, "JG は押し出しなし。");
        }

        [Test]
        public void Evade_StepInvincible_NoReaction()
        {
            var (holder, _, motor) = MakePlayer(guarding: false, canJustGuard: false, invincible: true);
            var atk = MakeAttacker();
            var reaction = new HitReaction(0.16f, 0.12f, 0.12f, isProjectile: false);

            holder.ReceiveHit(Hit(atk, holder, -Vector3.forward, reaction));

            Assert.AreEqual(0, motor.PushCount, "回避（無敵）では反応を起こさない。");
            Assert.AreEqual(0, atk.FlinchCount);
        }
    }
}
