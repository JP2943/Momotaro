using System.Collections.Generic;
using Momotaro.Gameplay.Player;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    public sealed class PlayerStateMachineTests
    {
        [Test]
        public void StartsIdle()
        {
            var sm = new PlayerStateMachine();
            Assert.AreEqual(PlayerState.Idle, sm.Current);
        }

        [Test]
        public void Moving_TransitionsIdleToMove()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(enabled: true, isMoving: true, guarding: false);
            Assert.AreEqual(PlayerState.Move, sm.Current);

            sm.Tick(true, false, false);
            Assert.AreEqual(PlayerState.Idle, sm.Current);
        }

        [Test]
        public void SameState_DoesNotRaiseEventAgain()
        {
            var sm = new PlayerStateMachine();
            var changes = new List<PlayerState>();
            sm.StateChanged += changes.Add;

            sm.Tick(true, true, false); // Idle -> Move (event)
            sm.Tick(true, true, false); // Move -> Move (no event)
            sm.Tick(true, true, false);

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(PlayerState.Move, changes[0]);
        }

        [Test]
        public void Disabled_ForcesIdle()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, true, false);
            Assert.AreEqual(PlayerState.Move, sm.Current);

            sm.Tick(enabled: false, isMoving: true, guarding: true);
            Assert.AreEqual(PlayerState.Idle, sm.Current, "無効時は移動/ガード入力があっても Idle");
        }

        [Test]
        public void Guard_ProducesGuardStates()
        {
            var sm = new PlayerStateMachine();

            sm.Tick(true, false, true);
            Assert.AreEqual(PlayerState.GuardIdle, sm.Current);

            sm.Tick(true, true, true);
            Assert.AreEqual(PlayerState.GuardMove, sm.Current);

            sm.Tick(true, true, false);
            Assert.AreEqual(PlayerState.Move, sm.Current, "ガード解除で通常 Move へ");
        }

        // ---- P2-07 受入修正：ガードブレイクの状態と優先度 ----

        [Test]
        public void GuardBreak_HasHighestPriority_OverAttackGuardMove()
        {
            var sm = new PlayerStateMachine();
            // 攻撃・ガード・移動すべて要求しても、ガードブレイク中は GuardBreak。
            sm.Tick(enabled: true, isMoving: true, guarding: true, attacking: true, guardBroken: true);
            Assert.AreEqual(PlayerState.GuardBreak, sm.Current, "ガードブレイクは最優先。");
        }

        [Test]
        public void GuardBreak_OverridesDisabled()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(enabled: false, isMoving: false, guarding: false, attacking: false, guardBroken: true);
            Assert.AreEqual(PlayerState.GuardBreak, sm.Current, "無効でもガードブレイクは GuardBreak。");
        }

        [Test]
        public void AfterGuardBreak_ReturnsToInputState()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, false, guardBroken: true);
            Assert.AreEqual(PlayerState.GuardBreak, sm.Current);

            sm.Tick(true, true, false, false, guardBroken: false);
            Assert.AreEqual(PlayerState.Move, sm.Current, "ブレイク終了後は入力状況へ復帰。");

            sm.Tick(true, false, true, false, guardBroken: false);
            Assert.AreEqual(PlayerState.GuardIdle, sm.Current, "ガード入力があれば GuardIdle へ。");
        }

        // ---- P2-09：ステップの状態と優先度 ----

        [Test]
        public void Step_HasPriorityOverAttackGuardMove_ButUnderGuardBreak()
        {
            var sm = new PlayerStateMachine();
            // 攻撃・ガード・移動を要求してもステップ中は Step。
            sm.Tick(enabled: true, isMoving: true, guarding: true, attacking: true, guardBroken: false, stepping: true);
            Assert.AreEqual(PlayerState.Step, sm.Current, "ステップは攻撃/ガード/移動より優先。");

            // ガードブレイクはステップより上。
            sm.Tick(true, true, true, true, guardBroken: true, stepping: true);
            Assert.AreEqual(PlayerState.GuardBreak, sm.Current, "ガードブレイクはステップより優先。");
        }

        [Test]
        public void Step_OverridesDisabled()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(enabled: false, isMoving: false, guarding: false, attacking: false, guardBroken: false, stepping: true);
            Assert.AreEqual(PlayerState.Step, sm.Current, "無効でもステップ中は Step。");
        }

        // ---- P2-10：必殺技の状態と優先度 ----

        [Test]
        public void SpecialCharge_And_Special_States()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, false, false, false, charging: true, specialAttacking: false);
            Assert.AreEqual(PlayerState.SpecialCharge, sm.Current);

            sm.Tick(true, false, false, false, false, false, charging: false, specialAttacking: true);
            Assert.AreEqual(PlayerState.Special, sm.Current);
        }

        [Test]
        public void Special_Priority_OverAttackGuard_UnderStepAndBreak()
        {
            var sm = new PlayerStateMachine();
            // 発動は攻撃/ガード/移動より上。
            sm.Tick(true, true, true, true, false, false, charging: false, specialAttacking: true);
            Assert.AreEqual(PlayerState.Special, sm.Current);

            // ステップは必殺技より上。
            sm.Tick(true, false, false, false, false, true, charging: false, specialAttacking: true);
            Assert.AreEqual(PlayerState.Step, sm.Current);

            // ガードブレイクは最上位。
            sm.Tick(true, false, false, false, true, false, charging: true, specialAttacking: false);
            Assert.AreEqual(PlayerState.GuardBreak, sm.Current);

            // チャージは攻撃より上。
            sm.Tick(true, false, false, true, false, false, charging: true, specialAttacking: false);
            Assert.AreEqual(PlayerState.SpecialCharge, sm.Current);
        }
    }
}
