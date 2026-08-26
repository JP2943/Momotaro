using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-10：必殺技チャージ・発動を実際の <see cref="PlayerStateController"/> 更新経路で検証する。長押しでチャージ（移動抑制）、
    /// 最大未満 Release は不発、保持限界超過で自動発動、ガード入力でキャンセルして JG 受付開始、通常被弾で中断。
    /// </summary>
    public sealed class PlayerSpecialTests
    {
        private static readonly MethodInfo UpdateMethod =
            typeof(PlayerStateController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            PlayerInputProvider.Current = null;
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private static void SetPrivate(object t, string f, object v)
        {
            System.Type ty = t.GetType();
            FieldInfo fi = null;
            while (ty != null && fi == null)
            {
                fi = ty.GetField(f, BindingFlags.NonPublic | BindingFlags.Instance);
                ty = ty.BaseType;
            }

            fi.SetValue(t, v);
        }

        private static object GetPrivate(object t, string f)
        {
            System.Type ty = t.GetType();
            FieldInfo fi = null;
            while (ty != null && fi == null)
            {
                fi = ty.GetField(f, BindingFlags.NonPublic | BindingFlags.Instance);
                ty = ty.BaseType;
            }

            return fi.GetValue(t);
        }

        private static void Tick(PlayerStateController c) => UpdateMethod.Invoke(c, null);

        /// <summary>必殺技の判定発生中（Active）か（内部フィールド参照。P3.5-09 のキャンセル可否テスト用）。</summary>
        private static bool InSpecialActive(PlayerStateController c) => (float)GetPrivate(c, "_specialActiveRemaining") > 0f;

        /// <summary>Active を越えて後隙に入るまで進める（発動直後から呼ぶ）。</summary>
        private static void AdvanceToRecovery(PlayerStateController c)
        {
            for (int i = 0; i < 60 && InSpecialActive(c); i++)
            {
                Tick(c);
            }
        }

        private SpecialAttackData MakeSpecialData(float charge = 2.0f, float hold = 0.75f)
        {
            var d = ScriptableObject.CreateInstance<SpecialAttackData>();
            _spawned.Add(d);
            SetPrivate(d, "_chargeSeconds", charge);
            SetPrivate(d, "_maxHoldSeconds", hold);
            return d;
        }

        private (PlayerStateController c, PlayerMotor motor, PlayerVitalsHolder holder, PlayerInputState input) MakeController(
            float charge = 2.0f, float hold = 0.75f, bool withVitals = false)
        {
            var go = new GameObject("SpecialTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var motor = go.AddComponent<PlayerMotor>();
            PlayerVitalsHolder holder = null;
            if (withVitals)
            {
                holder = go.AddComponent<PlayerVitalsHolder>();
                var pdata = ScriptableObject.CreateInstance<PlayerData>();
                _spawned.Add(pdata);
                SetPrivate(pdata, "_maxHp", 100);
                SetPrivate(pdata, "_defense", 0f);
                SetPrivate(pdata, "_maxStamina", 100);
                SetPrivate(holder, "_data", pdata);
            }

            var c = go.AddComponent<PlayerStateController>();
            SetPrivate(c, "_facing", facing);
            SetPrivate(c, "_motor", motor);
            SetPrivate(c, "_specialData", MakeSpecialData(charge, hold));

            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            return (c, motor, holder, input);
        }

        [Test]
        public void Hold_StartsCharge_SuppressesMovement()
        {
            var (c, motor, _, input) = MakeController();
            input.SetSpecialAttack(true);
            input.SetMove(new Vector2(1f, 0f));
            Tick(c);

            Assert.IsTrue(c.IsSpecialCharging, "長押しでチャージ開始。");
            Assert.AreEqual(PlayerState.SpecialCharge, c.Current);
            Assert.IsTrue(motor.MovementSuppressed, "チャージ中は移動不可。");
        }

        [Test]
        public void ReleaseBeforeMax_NotFired()
        {
            var (c, _, _, input) = MakeController(charge: 2.0f);
            input.SetSpecialAttack(true);
            Tick(c); // charging (elapsed ~dt << 2.0)
            Assert.IsTrue(c.IsSpecialCharging);

            input.SetSpecialAttack(false);
            Tick(c); // release → 最大未満 → 不発

            Assert.IsFalse(c.IsSpecialCharging, "離してチャージ終了。");
            Assert.IsFalse(c.IsSpecialAttacking, "不発（発動しない）。");
            Assert.AreNotEqual(PlayerState.Special, c.Current);
        }

        [Test]
        public void HoldLimit_AutoFires()
        {
            // 極小チャージ/保持で、保持し続けると自動発動することを確認。
            var (c, _, _, input) = MakeController(charge: 0.001f, hold: 0.001f);
            input.SetSpecialAttack(true);
            Tick(c); // Begin（elapsed 0）
            Tick(c); // elapsed=dt >> 0.002 → 自動発動

            Assert.IsTrue(c.IsSpecialAttacking, "保持限界超過で自動発動。");
            Assert.AreEqual(PlayerState.Special, c.Current);
        }

        [Test]
        public void AutoFire_NoRepeatWhileHeld_ThenReleaseRepressStartsFresh()
        {
            // 極小チャージ/保持で自動発動させ、押しっぱなしのまま発動＋後隙を越えても再発動しないことを確認する。
            var (c, _, _, input) = MakeController(charge: 0.001f, hold: 0.001f);
            input.SetSpecialAttack(true);
            Tick(c); // Begin
            Tick(c); // 自動発動
            Assert.IsTrue(c.IsSpecialAttacking);

            // 発動＋後隙（既定 0.35+0.9=1.25 秒。P3.5-09 で Active 延長）を越えるまで、必殺技ボタンを押したまま更新。
            // 保持中は再発動しない設計のため、余裕を持った反復数でも「再発動しない」検証は成立する。
            for (int i = 0; i < 150; i++)
            {
                Tick(c);
            }

            Assert.IsFalse(c.IsSpecialAttacking, "発動・後隙は終了している。");
            Assert.IsFalse(c.IsSpecialCharging, "押しっぱなしでは再チャージ・再発動しない（1 長押し 1 発動）。");

            // 一度解除して再度押すと 0 秒から新規チャージできる。
            input.SetSpecialAttack(false);
            Tick(c);
            input.SetSpecialAttack(true);
            Tick(c);

            Assert.IsTrue(c.IsSpecialCharging, "解除後の再押下で新規チャージ開始。");
            Assert.Less(c.SpecialChargeElapsed, 0.5f, "経過は 0 付近。");
        }

        [Test]
        public void GuardDuringCharge_CancelsCharge_AndOpensJustGuard()
        {
            var (c, _, _, input) = MakeController();
            input.SetSpecialAttack(true);
            Tick(c);
            Assert.IsTrue(c.IsSpecialCharging);

            input.SetGuard(true);
            Tick(c); // ガード入力でチャージ中断→ガードへ、JG 受付開始

            Assert.IsFalse(c.IsSpecialCharging, "ガードでチャージ中断。");
            Assert.AreEqual(PlayerState.GuardIdle, c.Current);
            Assert.IsTrue(c.CanJustGuard, "ガード押下から JG 受付が開く。");
        }

        [Test]
        public void StepDuringCharge_NoResumeWhileHeld_ThenReleaseRepressStartsFresh()
        {
            var (c, _, _, input) = MakeController();
            input.SetSpecialAttack(true);
            Tick(c); // charging
            Assert.IsTrue(c.IsSpecialCharging);

            // 必殺技ボタンを押したままステップ開始 → チャージ完全キャンセル。
            input.SetStep(true);
            Tick(c);
            Assert.IsFalse(c.IsSpecialCharging, "ステップでチャージは即時キャンセル。");
            input.SetStep(false); // ステップは連続させない（保持は継続）

            // ステップ終了を越えるまで更新（必殺技ボタンは押しっぱなし）。
            for (int i = 0; i < 40; i++)
            {
                Tick(c);
            }

            Assert.IsFalse(c.IsSpecialCharging, "押しっぱなしでもステップ後に再チャージしない（要解除）。");
            Assert.IsFalse(c.IsStepping, "ステップは終了している。");

            // 一度解除して再度押すと、0 秒から新規チャージできる。
            input.SetSpecialAttack(false);
            Tick(c);
            input.SetSpecialAttack(true);
            Tick(c);

            Assert.IsTrue(c.IsSpecialCharging, "解除後の再押下で新規チャージ開始。");
            Assert.Less(c.SpecialChargeElapsed, 0.5f, "経過は 0 付近（前回のチャージ時間を保持していない）。");
        }

        [Test]
        public void FiringSpecial_DoesNotMutateSpecialAttackDataSO()
        {
            var (c, _, _, input) = MakeController(charge: 0.001f, hold: 0.001f);
            var data = (SpecialAttackData)GetPrivate(c, "_specialData");
            float hp = data.HpMultiplier, ignore = data.DefenseIgnoreRatio, stun = data.StunHpMultiplier, flinch = data.FlinchPower;

            input.SetSpecialAttack(true);
            Tick(c);
            Tick(c); // 自動発動
            Assert.IsTrue(c.IsSpecialAttacking);

            Assert.AreEqual(hp, data.HpMultiplier, 1e-6f, "SO の技倍率は不変。");
            Assert.AreEqual(ignore, data.DefenseIgnoreRatio, 1e-6f);
            Assert.AreEqual(stun, data.StunHpMultiplier, 1e-6f);
            Assert.AreEqual(flinch, data.FlinchPower, 1e-6f);
        }

        [Test]
        public void HitDuringCharge_CancelsCharge_NoResumeWhileHeld()
        {
            var (c, _, holder, input) = MakeController(withVitals: true);
            input.SetSpecialAttack(true);
            Tick(c);
            Assert.IsTrue(c.IsSpecialCharging);

            // 通常被弾（実ダメージ）→ 必殺技チャージ中断。
            holder.ReceiveHit(new HitInfo(null, holder, -Vector3.forward, Vector3.zero, new HitDamage(10f, 0f, 0f),
                true, true, HitId.Single(1)));
            Assert.IsFalse(c.IsSpecialCharging, "被弾でチャージ中断。");

            for (int i = 0; i < 5; i++)
            {
                Tick(c);
            }

            Assert.IsFalse(c.IsSpecialCharging, "被弾キャンセル後、押しっぱなしでは再チャージしない（要解除）。");
        }

        [Test]
        public void GuardCancel_NoResumeWhileHeld()
        {
            var (c, _, _, input) = MakeController();
            input.SetSpecialAttack(true);
            Tick(c);
            Assert.IsTrue(c.IsSpecialCharging);

            input.SetGuard(true);
            Tick(c); // ガードでチャージ中断（要解除ロック）
            Assert.IsFalse(c.IsSpecialCharging);

            input.SetGuard(false);
            for (int i = 0; i < 5; i++)
            {
                Tick(c); // 必殺技は押しっぱなし
            }

            Assert.IsFalse(c.IsSpecialCharging, "ガードキャンセル後、押しっぱなしでは再チャージしない（要解除）。");
        }

        // === P3.5-09：必殺技の後隙キャンセル（爽快感重視。後隙はステップ／攻撃で中断可・Active 中は出し切る） ===

        /// <summary>Active を短く・後隙を長くして、EditMode の可変 deltaTime でも「後隙の途中」を決定的に作る。</summary>
        private static void ShortActiveLongRecovery(PlayerStateController c)
        {
            var data = (SpecialAttackData)GetPrivate(c, "_specialData");
            SetPrivate(data, "_activeSeconds", 0.05f);
            SetPrivate(data, "_recoverySeconds", 5.0f);
        }

        private AttackData MakeStage(float startup, float active, float recovery, float cancelStart)
        {
            var a = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(a);
            SetPrivate(a, "_startupSeconds", startup);
            SetPrivate(a, "_activeSeconds", active);
            SetPrivate(a, "_recoverySeconds", recovery);
            SetPrivate(a, "_cancelWindowStartSeconds", cancelStart);
            return a;
        }

        /// <summary>1 段のみの通常コンボ Data（後隙キャンセル先。startup を長めにして 1 Tick で攻撃状態が続くようにする）。</summary>
        private PlayerAttackComboData MakeCombo1()
        {
            var combo = ScriptableObject.CreateInstance<PlayerAttackComboData>();
            _spawned.Add(combo);
            SetPrivate(combo, "_stages", new[] { MakeStage(1.0f, 2.0f, 2.0f, 0f) });
            SetPrivate(combo, "_bufferSeconds", 0.30f);
            return combo;
        }

        [Test]
        public void StepDuringSpecialActive_IsBlocked_HitCompletes()
        {
            var (c, _, _, input) = MakeController(charge: 0.001f, hold: 0.001f);
            // Active を deltaTime より十分長くして、発動直後の 1 Tick が確実に「Active 中」になるようにする。
            var data = (SpecialAttackData)GetPrivate(c, "_specialData");
            SetPrivate(data, "_activeSeconds", 0.5f);
            SetPrivate(data, "_recoverySeconds", 5.0f);

            input.SetSpecialAttack(true);
            Tick(c); // Begin
            Tick(c); // 自動発動
            input.SetSpecialAttack(false);
            Assert.IsTrue(c.IsSpecialAttacking && InSpecialActive(c), "判定発生（Active）中。");

            // 判定発生中のステップ入力は無視し、必殺技を出し切る。
            input.SetStep(true);
            Tick(c);

            Assert.IsFalse(c.IsStepping, "Active 中はステップを開始しない（一撃を出し切る）。");
            Assert.IsTrue(c.IsSpecialAttacking, "必殺技は継続する。");
            Assert.IsTrue(InSpecialActive(c), "判定発生は継続する。");
        }

        [Test]
        public void StepDuringSpecialRecovery_CancelsRecovery_NoResume()
        {
            var (c, _, _, input) = MakeController(charge: 0.001f, hold: 0.001f);
            ShortActiveLongRecovery(c);

            input.SetSpecialAttack(true);
            Tick(c); // Begin
            Tick(c); // 自動発動
            input.SetSpecialAttack(false);
            Assert.IsTrue(c.IsSpecialAttacking);

            AdvanceToRecovery(c);
            Assert.IsTrue(c.IsSpecialAttacking, "まだ後隙（実行中）。");
            Assert.IsFalse(InSpecialActive(c), "判定発生（Active）は終了している。");

            // 後隙中のステップで必殺技を打ち切る。
            input.SetStep(true);
            Tick(c);
            Assert.IsFalse(c.IsSpecialAttacking, "ステップで後隙をキャンセル。");
            Assert.IsTrue(c.IsStepping, "ステップが開始する。");
            input.SetStep(false);

            // 凍結バグ回帰防止：ステップ後に後隙が再開しない。
            for (int i = 0; i < 60; i++)
            {
                Tick(c);
            }

            Assert.IsFalse(c.IsSpecialAttacking, "ステップ後に後隙は再開しない。");
        }

        [Test]
        public void AttackDuringSpecialRecovery_CancelsIntoCombo()
        {
            var (c, _, _, input) = MakeController(charge: 0.001f, hold: 0.001f);
            SetPrivate(c, "_attackCombo", MakeCombo1());
            ShortActiveLongRecovery(c);

            input.SetSpecialAttack(true);
            Tick(c); // Begin（EnsureRuntime が _attackCombo から _combo を構築）
            Tick(c); // 自動発動
            input.SetSpecialAttack(false);
            Assert.IsTrue(c.IsSpecialAttacking);

            AdvanceToRecovery(c);
            Assert.IsTrue(c.IsSpecialAttacking, "後隙中。");
            Assert.IsFalse(InSpecialActive(c));

            // 後隙中に攻撃入力 → 必殺技を打ち切り、同フレームで通常コンボへキャンセル。
            input.SetAttack(true);
            Tick(c);

            Assert.IsFalse(c.IsSpecialAttacking, "攻撃で後隙をキャンセル。");
            Assert.AreEqual(PlayerState.Attack, c.Current, "通常攻撃へ移行する。");
        }
    }
}
