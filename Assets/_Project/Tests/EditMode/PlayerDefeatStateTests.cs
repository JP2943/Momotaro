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
    /// P3.5-02：死亡（<see cref="PlayerState.Defeated"/>）が実際の <see cref="PlayerStateController"/> 更新経路で成立することを検証する。
    /// 致死で全入力より優先して Defeated へ入り、攻撃・ステップ・必殺技チャージ・GuardBreak を中断し、以後恒久的に入力を無視し、
    /// 移動を凍結し向きを保持する（仕様書 §3.1/§4.1）。
    /// </summary>
    public sealed class PlayerDefeatStateTests
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

        private static void Tick(PlayerStateController c) => UpdateMethod.Invoke(c, null);

        private PlayerAttackComboData MakeCombo()
        {
            var combo = ScriptableObject.CreateInstance<PlayerAttackComboData>();
            _spawned.Add(combo);
            var a = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(a);
            SetPrivate(a, "_startupSeconds", 1.0f);
            SetPrivate(a, "_activeSeconds", 2.0f);
            SetPrivate(a, "_recoverySeconds", 2.0f);
            SetPrivate(combo, "_stages", new[] { a });
            SetPrivate(combo, "_bufferSeconds", 5.0f);
            return combo;
        }

        private SpecialAttackData MakeSpecialData()
        {
            var d = ScriptableObject.CreateInstance<SpecialAttackData>();
            _spawned.Add(d);
            SetPrivate(d, "_chargeSeconds", 5.0f);
            SetPrivate(d, "_maxHoldSeconds", 0.75f);
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
            public PlayerFacing Facing;
            public PlayerMotor Motor;
            public PlayerInputState Input;
        }

        private Setup MakeSetup(int maxHp = 100, int maxStamina = 100)
        {
            var go = new GameObject("DefeatTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var motor = go.AddComponent<PlayerMotor>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetPrivate(holder, "_data", MakePlayerData(maxHp, maxStamina));
            var controller = go.AddComponent<PlayerStateController>();
            SetPrivate(controller, "_facing", facing);
            SetPrivate(controller, "_motor", motor);
            SetPrivate(controller, "_attackCombo", MakeCombo());
            SetPrivate(controller, "_specialData", MakeSpecialData());

            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            return new Setup { Controller = controller, Holder = holder, Facing = facing, Motor = motor, Input = input };
        }

        private static void Kill(PlayerVitalsHolder holder)
        {
            holder.ReceiveHit(new HitInfo(null, holder, -Vector3.forward, Vector3.zero,
                new HitDamage(100000f, 0f, 0f), true, true, HitId.Single(99)));
        }

        [Test]
        public void LethalDamage_EntersDefeated_OverAllInputs()
        {
            var s = MakeSetup();
            Kill(s.Holder);

            s.Input.SetAttack(true);
            s.Input.SetGuard(true);
            s.Input.SetMove(new Vector2(1f, 0f));
            Tick(s.Controller);

            Assert.AreEqual(PlayerState.Defeated, s.Controller.Current, "死亡は全入力より優先。");
            Assert.IsTrue(s.Motor.MovementSuppressed, "移動凍結。");
            Assert.IsTrue(s.Facing.IsLocked, "向きを保持（Facing 更新停止）。");
        }

        [Test]
        public void AttackInProgress_InterruptedByDefeat()
        {
            var s = MakeSetup();
            s.Input.SetAttack(true);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Attack, s.Controller.Current);

            Kill(s.Holder);
            s.Input.SetAttack(false);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Defeated, s.Controller.Current, "攻撃を中断して Defeated。");
        }

        [Test]
        public void StepInProgress_InterruptedByDefeat()
        {
            var s = MakeSetup();
            s.Input.SetStep(true);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Step, s.Controller.Current);

            Kill(s.Holder);
            s.Input.SetStep(false);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Defeated, s.Controller.Current, "ステップを中断して Defeated。");
        }

        [Test]
        public void SpecialChargeInProgress_InterruptedByDefeat()
        {
            var s = MakeSetup();
            s.Input.SetSpecialAttack(true);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.SpecialCharge, s.Controller.Current);

            Kill(s.Holder);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Defeated, s.Controller.Current, "必殺技チャージを中断して Defeated。");
        }

        [Test]
        public void GuardBreakThenLethal_EntersDefeated()
        {
            var s = MakeSetup(maxStamina: 20);
            s.Holder.ConsumeStamina(20f);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.GuardBreak, s.Controller.Current);

            Kill(s.Holder);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Defeated, s.Controller.Current, "GuardBreak 中の致死でも Defeated（最優先）。");
        }

        [Test]
        public void Defeated_IsPermanent_IgnoresFurtherInput()
        {
            var s = MakeSetup();
            Kill(s.Holder);
            Tick(s.Controller);
            Assert.AreEqual(PlayerState.Defeated, s.Controller.Current);

            // 何度 Tick しても、どんな入力でも Defeated のまま（復帰しない）。
            for (int i = 0; i < 5; i++)
            {
                s.Input.SetMove(new Vector2(1f, 0f));
                s.Input.SetAttack(true);
                s.Input.SetGuard(true);
                Tick(s.Controller);
                Assert.AreEqual(PlayerState.Defeated, s.Controller.Current, "Defeated は恒久（tick " + i + "）。");
            }
        }
    }
}
