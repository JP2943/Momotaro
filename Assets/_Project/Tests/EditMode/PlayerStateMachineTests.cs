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
    }
}
