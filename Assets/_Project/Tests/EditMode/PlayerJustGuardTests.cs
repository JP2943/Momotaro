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
    /// 先に評価され、成立時はスタミナ非消費・HP0・攻撃者の体幹へ反射（JG 反射は回復待機 4 秒）。前方外・ガード不能・受付窓外では
    /// JG は成立せず通常ガード／貫通へ移行する。実際の <c>GetComponentInParent&lt;IJustGuardState&gt;()</c> 取得経路で確認する。
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

        private static object GetField(object target, string name)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + name);
            return f.GetValue(target);
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

        // JG 反射の逆方向 Hit を記録する検証用の攻撃者（IDamageable + ICombatActor）。
        private sealed class RecordingAttacker : MonoBehaviour, IDamageable, ICombatActor
        {
            public bool Got;
            public HitInfo Last;
            public int DamageableId => GetInstanceID();
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
            public void ReceiveHit(in HitInfo hit) { Got = true; Last = hit; }
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

        // JG 体幹反射の既定は「通常攻撃 = 20」（仕様書 §3.3）。
        private static HitInfo JgHit(ICombatActor attacker, IDamageable target, Vector3 attackDir,
            float jgPoise = 20f, bool guardable = true, bool justGuardable = true, int id = 1)
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

            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward)); // 前方・受付窓内・既定 20

            Assert.AreEqual(HitResultKind.JustGuard, rec.Received[0].Kind, "JG 成立。");
            Assert.AreEqual(0f, rec.Received[0].AppliedDamage.Hp, "HP ダメージ 0。");
            Assert.AreEqual(stamina0, holder.Vitals.Stamina.Current, "JG はスタミナを消費しない。");
            Assert.AreEqual(80f, attacker.CurrentPoise, 1e-3f, "攻撃者の体幹へ既定 20 反射（100→80）。");
            Assert.AreEqual(1, fake.SuccessCount, "成功通知（猶予付与・窓クローズ）が呼ばれる。");
        }

        [TestCase(15f, 85f)]
        [TestCase(20f, 80f)]
        [TestCase(30f, 70f)]
        [TestCase(40f, 60f)]
        public void JustGuard_ReflectsConfigurablePoise(float jgPoise, float expectedRemaining)
        {
            var (holder, _, rec) = MakePlayer(canJustGuard: true);
            var attacker = MakeAttacker(poiseMax: 100);

            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward, jgPoise: jgPoise));

            Assert.AreEqual(HitResultKind.JustGuard, rec.Received[0].Kind);
            Assert.AreEqual(expectedRemaining, attacker.CurrentPoise, 1e-3f, "軽15/通常20/強30/ボス大技40 を設定可能。");
        }

        [Test]
        public void JustGuardCounter_UsesFourSecondRecoveryDelay_OnTarget()
        {
            var (holder, _, _) = MakePlayer(canJustGuard: true);
            var attacker = MakeAttacker(poiseMax: 100);

            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward)); // JG 反射

            var poise = (PoiseState)GetField(attacker, "_poise");
            Assert.AreEqual(4f, poise.RecoveryDelayRemaining, 1e-3f, "JG 由来の体幹ダメージは回復待機 4 秒。");
        }

        [Test]
        public void JustGuardCounter_ReverseHit_CarriesFlag_AndIsUnblockable()
        {
            var (holder, _, rec) = MakePlayer(canJustGuard: true);
            var go = new GameObject("RecAtk");
            _spawned.Add(go);
            var atk = go.AddComponent<RecordingAttacker>();

            holder.ReceiveHit(JgHit(atk, holder, -Vector3.forward, jgPoise: 20f));

            Assert.AreEqual(HitResultKind.JustGuard, rec.Received[0].Kind);
            Assert.IsTrue(atk.Got, "攻撃者へ反射 Hit が返る。");
            Assert.IsTrue(atk.Last.IsJustGuardCounter, "反射 Hit は JG カウンターとしてマークされる（4 秒回復に接続）。");
            Assert.AreEqual(20f, atk.Last.Damage.Poise, 1e-3f, "体幹のみ 20 を反射。");
            Assert.AreEqual(0f, atk.Last.Damage.Hp, "反射は HP を与えない。");
            Assert.IsFalse(atk.Last.Guardable, "反射は再ガード不可。");
            Assert.IsFalse(atk.Last.JustGuardable, "反射は再 JG 不可。");
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
        public void UnguardableAndUnjustGuardable_Pierces()
        {
            var (holder, _, rec) = MakePlayer(maxHp: 100, defense: 20f, guarding: true, canJustGuard: true);
            var attacker = MakeAttacker();

            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward, guardable: false, justGuardable: false));

            Assert.AreEqual(HitResultKind.Damage, rec.Received[0].Kind, "不能攻撃は JG も通常ガードも不可。");
            Assert.AreEqual(92, holder.Vitals.Health.Current);
        }

        [Test]
        public void JustGuardableFalse_NoReflect_EvenIfWindowOpen()
        {
            var (holder, _, rec) = MakePlayer(maxStamina: 100, guarding: true, canJustGuard: true);
            var attacker = MakeAttacker(poiseMax: 100);

            // JustGuardable=false（ガード可）：JG は成立せず反射しない。通常ガードへ。
            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward, justGuardable: false));

            Assert.AreNotEqual(HitResultKind.JustGuard, rec.Received[0].Kind, "JustGuardable=false では JG 不成立。");
            Assert.AreEqual(100f, attacker.CurrentPoise, 1e-3f, "反射は発生しない（攻撃者体幹は不変）。");
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
        public void JustGuard_AtZeroStamina_Succeeds_NoConsume_NoBreak_ReflectsNormally()
        {
            var (holder, _, rec) = MakePlayer(maxStamina: 100, canJustGuard: true);
            var attacker = MakeAttacker(poiseMax: 100);

            // スタミナを実際に 0 へ（ブレイクを伴わない形で。JG がスタミナ非依存であることの検証）。
            var stamina = (StaminaState)GetField(holder, "_stamina");
            SetField(stamina, "_current", 0f);
            Assert.IsFalse(holder.IsGuardBroken, "前提：0 だがブレイクしていない状態。");

            holder.ReceiveHit(JgHit(attacker, holder, -Vector3.forward));

            Assert.AreEqual(HitResultKind.JustGuard, rec.Received[0].Kind, "スタミナ 0 でも JG 成立。");
            Assert.AreEqual(0, holder.Vitals.Stamina.Current, "消費 0（0 のまま）。");
            Assert.IsFalse(holder.IsGuardBroken, "JG はブレイクへ移行しない。");
            Assert.AreEqual(80f, attacker.CurrentPoise, 1e-3f, "反射は通常どおり発生（100→80）。");
        }
    }
}
