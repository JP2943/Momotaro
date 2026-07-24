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
        public void StepDuringCharge_FullyCancelsCharge_NoResume()
        {
            var (c, _, _, input) = MakeController();
            input.SetSpecialAttack(true);
            Tick(c); // charging
            Assert.IsTrue(c.IsSpecialCharging);

            input.SetStep(true);
            Tick(c); // ステップ開始 → チャージ完全キャンセル（経過0）

            Assert.IsFalse(c.IsSpecialCharging, "ステップでチャージは即時キャンセル。");
            var special = GetPrivate(c, "_special");
            var isActive = (bool)special.GetType().GetProperty("IsActive").GetValue(special);
            Assert.IsFalse(isActive, "SpecialChargeState は非アクティブ（経過0へ）。ステップ後も再開しない。");
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
        public void HitDuringCharge_CancelsCharge()
        {
            var (c, _, holder, input) = MakeController(withVitals: true);
            input.SetSpecialAttack(true);
            Tick(c);
            Assert.IsTrue(c.IsSpecialCharging);

            // 通常被弾（実ダメージ）→ 必殺技チャージ中断。
            holder.ReceiveHit(new HitInfo(null, holder, -Vector3.forward, Vector3.zero, new HitDamage(10f, 0f, 0f),
                true, true, HitId.Single(1)));

            Assert.IsFalse(c.IsSpecialCharging, "被弾でチャージ中断。");
        }
    }
}
