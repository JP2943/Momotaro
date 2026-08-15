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
    /// P3.5-01：被弾（<see cref="PlayerState.Hurt"/>）が実際の <see cref="PlayerStateController"/> 更新経路で成立することを検証する。
    /// 実ダメージで全入力より優先して Hurt へ入り、攻撃・ガード・移動・ステップ・必殺技チャージを中断し、硬直中は入力を無視し、
    /// 硬直終了後は入力状況へ自然復帰する。HP0 は Hurt に入らない（Defeated 優先の準備境界）。GuardBreak 中の被弾は Hurt へ移り、
    /// 残存 Break 時間を破棄して GuardBreak へ戻らない（仕様書 §2.3/§3.1/§3.3）。
    /// </summary>
    public sealed class PlayerHurtStateTests
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

        private static void SetPrivate(object target, string field, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + field);
            f.SetValue(target, value);
        }

        private static void Tick(PlayerStateController controller) => UpdateMethod.Invoke(controller, null);

        private PlayerAttackComboData MakeCombo()
        {
            var combo = ScriptableObject.CreateInstance<PlayerAttackComboData>();
            _spawned.Add(combo);
            var a = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(a);
            // EditMode の大きな Time.deltaTime で先行入力 Buffer や段が同一 Tick 内に落ちないよう秒オーダーへスケール（GuardBreak テストと同方針）。
            SetPrivate(a, "_startupSeconds", 1.0f);
            SetPrivate(a, "_activeSeconds", 2.0f);
            SetPrivate(a, "_recoverySeconds", 2.0f);
            SetPrivate(combo, "_stages", new[] { a });
            SetPrivate(combo, "_bufferSeconds", 5.0f);
            return combo;
        }

        private SpecialAttackData MakeSpecialData(float charge = 5.0f, float hold = 0.75f)
        {
            var d = ScriptableObject.CreateInstance<SpecialAttackData>();
            _spawned.Add(d);
            SetPrivate(d, "_chargeSeconds", charge);
            SetPrivate(d, "_maxHoldSeconds", hold);
            return d;
        }

        private PlayerData MakePlayerData(int maxHp, int maxStamina)
        {
            var d = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(d);
            SetPrivate(d, "_maxHp", maxHp);
            SetPrivate(d, "_defense", 0f);
            SetPrivate(d, "_maxStamina", maxStamina);
            return d;
        }

        private sealed class Setup
        {
            public PlayerStateController Controller;
            public PlayerVitalsHolder Holder;
            public PlayerHitReaction Reaction;
            public PlayerFacing Facing;
            public PlayerMotor Motor;
            public PlayerInputState Input;
        }

        private Setup MakeSetup(int maxHp = 100, int maxStamina = 100)
        {
            var go = new GameObject("HurtTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var motor = go.AddComponent<PlayerMotor>();
            var reaction = go.AddComponent<PlayerHitReaction>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetPrivate(holder, "_data", MakePlayerData(maxHp, maxStamina));
            var controller = go.AddComponent<PlayerStateController>();
            SetPrivate(controller, "_facing", facing);
            SetPrivate(controller, "_motor", motor);
            SetPrivate(controller, "_attackCombo", MakeCombo());
            SetPrivate(controller, "_specialData", MakeSpecialData());

            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            return new Setup { Controller = controller, Holder = holder, Reaction = reaction, Facing = facing, Motor = motor, Input = input };
        }

        private static HitInfo Damaging(IDamageable target, float preDefenseHp, int id = 1)
        {
            return new HitInfo(null, target, -Vector3.forward, Vector3.zero, new HitDamage(preDefenseHp, 0f, 0f),
                true, true, HitId.Single(id));
        }

        [Test]
        public void RealDamage_EntersHurt_OverAllInputs()
        {
            var s = MakeSetup();
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));

            // 攻撃・ガード・移動すべて要求しても被弾硬直が優先。
            s.Input.SetAttack(true);
            s.Input.SetGuard(true);
            s.Input.SetMove(new Vector2(1f, 0f));
            Tick(s.Controller);

            Assert.AreEqual(PlayerState.Hurt, s.Controller.Current, "被弾硬直は全入力より優先。");
            Assert.IsTrue(s.Motor.MovementSuppressed, "硬直中は移動凍結。");
            Assert.IsTrue(s.Facing.IsLocked, "硬直中は向きを固定して被弾直前 Facing を保持。");
        }

        [Test]
        public void DuringHurt_InputsIgnored_NoBufferRetained()
        {
            var s = MakeSetup();
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));
            s.Input.SetAttack(true);
            Tick(s.Controller);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Hurt, s.Controller.Current, "硬直中は入力無視。");

            // 硬直終了。押しっぱなしの攻撃は残留 Buffer で発火しない。
            s.Reaction.Tick(0.30f);
            s.Input.SetMove(Vector2.zero);
            s.Input.SetGuard(false);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Idle, s.Controller.Current, "終了後、残留 Buffer による攻撃は起きない。");
        }

        [Test]
        public void AttackInProgress_InterruptedByHurt()
        {
            var s = MakeSetup();
            s.Input.SetAttack(true);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Attack, s.Controller.Current, "まず攻撃に入る。");

            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));
            s.Input.SetAttack(false);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Hurt, s.Controller.Current, "攻撃を中断して Hurt。");
        }

        [Test]
        public void StepInProgress_InterruptedByHurt()
        {
            var s = MakeSetup();
            s.Input.SetStep(true);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Step, s.Controller.Current, "後方ステップ開始。");

            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));
            s.Input.SetStep(false);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Hurt, s.Controller.Current, "ステップを中断して Hurt。");
        }

        [Test]
        public void SpecialChargeInProgress_InterruptedByHurt()
        {
            var s = MakeSetup();
            s.Input.SetSpecialAttack(true);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.SpecialCharge, s.Controller.Current, "長押しでチャージ開始。");

            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Hurt, s.Controller.Current, "必殺技チャージを中断して Hurt。");
            Assert.IsFalse(s.Controller.IsSpecialCharging, "チャージは中断済み。");
        }

        [Test]
        public void AfterHurtEnds_ReturnsToInputState()
        {
            var s = MakeSetup();
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Hurt, s.Controller.Current);

            s.Reaction.Tick(0.30f); // 硬直終了（無敵は継続）
            s.Input.SetMove(new Vector2(1f, 0f));
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Move, s.Controller.Current, "硬直終了後は入力状況へ復帰。");
            Assert.IsFalse(s.Facing.IsLocked, "硬直終了で向きロック解除。");
        }

        [Test]
        public void LethalDamage_DoesNotEnterHurt()
        {
            var s = MakeSetup(maxHp: 5);
            s.Holder.ReceiveHit(Damaging(s.Holder, 100f)); // HP0
            Tick(s.Controller);

            Assert.AreNotEqual(PlayerState.Hurt, s.Controller.Current, "HP0 は Hurt に入らない（Defeated 優先の準備境界）。");
            Assert.IsFalse(s.Reaction.IsHurt);
        }

        [Test]
        public void GuardBreakThenDamage_EntersHurt_ThenDoesNotReturnToBreak()
        {
            var s = MakeSetup(maxStamina: 20);
            s.Holder.ConsumeStamina(20f); // ガードブレイク
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.GuardBreak, s.Controller.Current);

            s.Holder.ReceiveHit(Damaging(s.Holder, 10f)); // ブレイク中被弾 → Hurt
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Hurt, s.Controller.Current, "ブレイク中の実ダメージで Hurt へ。");
            Assert.IsFalse(s.Holder.IsGuardBroken, "残存 Break は破棄。");

            s.Reaction.Tick(0.30f);
            s.Input.SetMove(Vector2.zero);
            Tick(s.Controller);
            Assert.AreNotEqual(PlayerState.GuardBreak, s.Controller.Current, "Hurt 終了後に GuardBreak へ戻らない。");
        }
    }
}
