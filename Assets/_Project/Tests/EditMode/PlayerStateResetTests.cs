using System.Reflection;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class PlayerStateResetTests
    {
        [Test]
        public void Machine_Reset_ReturnsToIdle_AndNotifies()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, true, true); // -> GuardMove
            Assert.AreNotEqual(PlayerState.Idle, sm.Current);

            PlayerState notified = sm.Current;
            sm.StateChanged += s => notified = s;
            sm.Reset();

            Assert.AreEqual(PlayerState.Idle, sm.Current);
            Assert.AreEqual(PlayerState.Idle, notified, "Reset で Idle が通知される");
        }

        [Test]
        public void Machine_Reset_WhenAlreadyIdle_DoesNotNotify()
        {
            var sm = new PlayerStateMachine();
            int count = 0;
            sm.StateChanged += _ => count++;
            sm.Reset();
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Controller_ResetToNeutral_ClearsGuardEffectsAndState()
        {
            var go = new GameObject("PlayerStateControllerTest");
            var controller = go.AddComponent<PlayerStateController>();
            var motor = go.AddComponent<PlayerMotor>();
            var facing = go.AddComponent<PlayerFacing>();

            SetPrivate(controller, "_motor", motor);
            SetPrivate(controller, "_facing", facing);

            // ガード相当の状態を作る。
            motor.SpeedMultiplier = 0.4f;
            facing.IsLocked = true;

            controller.ResetToNeutral();

            Assert.AreEqual(1f, motor.SpeedMultiplier, "速度倍率が 1 へ戻る");
            Assert.IsFalse(facing.IsLocked, "向きロックが解除される");
            Assert.AreEqual(PlayerState.Idle, controller.Current, "状態が Idle へ戻る");

            Object.DestroyImmediate(go);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(target, value);
        }
    }
}
