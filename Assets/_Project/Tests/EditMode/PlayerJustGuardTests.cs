using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-08：主人公の被弾経路（<see cref="PlayerVitalsHolder.ReceiveHit"/>）でのジャストガードを検証する。JG は通常ガードより
    /// 先に評価され、成立時はスタミナ非消費・HP0・攻撃者の体幹へ反射。前方 180°外・ガード不能・受付窓外では JG は成立せず、
    /// 通常ガード／貫通へ移行する。実際の <c>GetComponentInParent&lt;IJustGuardState&gt;()</c> 取得経路で確認する。
    /// </summary>
    public sealed class PlayerJustGuardTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
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

        private sealed class FakeGuardJust : MonoBehaviour, IGuardState, IJustGuardState
        {
            public bool Guarding;
            public Vector3 Fwd = Vector3.forward;
            public bool CanJG;
            public int SuccessCount;
            public bool IsGuarding => Guarding;
            public Vector3 GuardForward => Fwd;
            public bool CanJustGuard => CanJG;
            public void NotifyJustGuardSuccess() { SuccessCount++; CanJG = false; }
        }

        private sealed class Recorder : IHitResultListener
        {
            public readonly List<HitResult> Received = new List<HitResult>();
            public void OnHitResult(in HitResult result) => Received.Add(result);
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

        private (PlayerVitalsHolder holder, FakeGuardJust fake, Recorder rec) MakePlayer(
            int maxHp = 100, float defense = 20f, int maxStamina = 100, bool guarding = false, bool canJustGuard = true)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var fake = go.AddComponent<FakeGuardJust>();
            fake.Guarding = guarding;
            fake.CanJG = canJustGuard;
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", MakePlayerData(maxHp, defense, maxStamina));
            go.SetActive(true);
            var rec = new Recorder();
            holder.Results.AddListener(rec);
            return (holder, fake, rec);
        }

        private CombatDummy MakeAttacker(int poiseMax = 100)
        {
            var e = ScriptableObject.CreateInstance<EnemyData>();
            _spawned.Add(e);
            SetField(e, "_maxHp", 100);
            SetField(e, "_defense", 0f);
            SetField(e, "_poiseMax", (float)poiseMax);
            SetField(e, "_flinchResistance", 60f);

            var go = new GameObject("Attacker");
            _spawned.Add(go);
            go.SetActive(false);
            var dummy = go.AddComponent<CombatDummy>();
            SetField(dummy, "_data", e);
            go.SetActive(true);
            return dummy;
        }

        private static HitInfo JgHit(CombatDummy attacker, IDamageable target, Vector3 attackDir,
            float jgPoise = 50f, bool guardable = true, bool justGuardable = true, int id = 1)
        {
            return new HitInfo(attacker, target, attackDir, Vector3.zero, new HitDamage(10f, 0f, 0f),
                10f, jgPoise, guardable, justGuardable, HitId.Single(id));
        }

        [Test]
        public void FrontJustGuard_NoStaminaConsumed_ReflectsPoiseToAttacker()
        {
            var (holder, fake, rec) = MakePlayer(maxStamina: 100, canJustGuard: true);
            var attacker = MakeAttacker(poiseMax: 100);
            int stamina0 = holder.Vitals.Stamina.Current;

            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward)); // 前方・受付窓内

            Assert.AreEqual(HitResultKind.JustGuard, rec.Received[0].Kind, "JG 成立。");
            Assert.AreEqual(0f, rec.Received[0].AppliedDamage.Hp, "HP ダメージ 0。");
            Assert.AreEqual(stamina0, holder.Vitals.Stamina.Current, "JG はスタミナを消費しない。");
            Assert.AreEqual(50f, attacker.CurrentPoise, 1e-3f, "攻撃者の体幹へ 50 反射。");
            Assert.AreEqual(1, fake.SuccessCount, "成功通知（猶予付与・窓クローズ）が呼ばれる。");
        }

        [Test]
        public void JustGuard_TakesPriorityOverNormalGuard()
        {
            var (holder, _, rec) = MakePlayer(maxStamina: 100, guarding: true, canJustGuard: true);
            var attacker = MakeAttacker();

            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward));

            Assert.AreEqual(HitResultKind.JustGuard, rec.Received[0].Kind, "ガード中でも JG を優先。");
            Assert.AreEqual(100, holder.Vitals.Stamina.Current, "JG なのでスタミナ消費なし。");
        }

        [Test]
        public void WindowClosed_FallsBackToNormalGuard()
        {
            var (holder, _, rec) = MakePlayer(maxStamina: 100, guarding: true, canJustGuard: false);
            var attacker = MakeAttacker();

            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward));

            Assert.AreEqual(HitResultKind.Guard, rec.Received[0].Kind, "受付窓外は通常ガードへ移行。");
            Assert.AreEqual(90, holder.Vitals.Stamina.Current, "通常ガードは固定スタミナ 10 を消費。");
        }

        [Test]
        public void UnguardableAttack_NoJustGuard()
        {
            var (holder, _, rec) = MakePlayer(maxHp: 100, defense: 20f, guarding: true, canJustGuard: true);
            var attacker = MakeAttacker();

            // ガード不能・JG 不能攻撃：JG も通常ガードも不可 → 貫通。
            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward, guardable: false, justGuardable: false));

            Assert.AreEqual(HitResultKind.Damage, rec.Received[0].Kind, "不能攻撃は JG も成立しない。");
            Assert.AreEqual(92, holder.Vitals.Health.Current);
        }

        [Test]
        public void BackAttack_NoJustGuard_Pierces()
        {
            var (holder, _, rec) = MakePlayer(maxHp: 100, defense: 20f, canJustGuard: true);
            var attacker = MakeAttacker();

            holder.ReceiveHit(JgHit(attacker, holder, Vector3.forward)); // 背後（前方外）

            Assert.AreEqual(HitResultKind.Damage, rec.Received[0].Kind, "背後は JG 不可。");
            Assert.AreEqual(92, holder.Vitals.Health.Current);
        }

        [Test]
        public void JustGuard_WorksAtZeroStamina_NoBreak()
        {
            var (holder, _, rec) = MakePlayer(maxStamina: 1, canJustGuard: true);
            var attacker = MakeAttacker();
            // スタミナを 0 近くにしても JG は消費なしで成立し、ブレイクしない。
            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward));

            Assert.AreEqual(HitResultKind.JustGuard, rec.Received[0].Kind);
            Assert.IsFalse(holder.IsGuardBroken, "JG はスタミナ非消費でブレイクしない。");
        }
    }
}
