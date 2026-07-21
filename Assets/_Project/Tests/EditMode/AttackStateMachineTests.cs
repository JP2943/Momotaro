using System.Collections.Generic;
using Momotaro.Gameplay.Player;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-02/P2-03B：状態機械の攻撃状態は外部の攻撃中フラグで駆動される。優先度（攻撃 ＞ ガード ＞ 移動/Idle）と
    /// 無効化・Reset を検証する。攻撃の時間・段・連鎖は <see cref="AttackComboMachineTests"/> が担う。
    /// </summary>
    public sealed class AttackStateMachineTests
    {
        [Test]
        public void Attacking_EntersAttackState()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(enabled: true, isMoving: false, guarding: false, attacking: true);
            Assert.AreEqual(PlayerState.Attack, sm.Current);
        }

        [Test]
        public void Attack_OutranksGuardAndMove()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, isMoving: true, guarding: true, attacking: true);
            Assert.AreEqual(PlayerState.Attack, sm.Current, "攻撃は移動・ガードより優先。");
        }

        [Test]
        public void AttackEnds_ReturnsToMoveOrIdle()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, true);
            Assert.AreEqual(PlayerState.Attack, sm.Current);

            sm.Tick(true, isMoving: true, guarding: false, attacking: false);
            Assert.AreEqual(PlayerState.Move, sm.Current);

            sm.Tick(true, false, false, false);
            Assert.AreEqual(PlayerState.Idle, sm.Current);
        }

        [Test]
        public void Disabled_ForcesIdle_EvenWhenAttacking()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, true);
            sm.Tick(enabled: false, isMoving: true, guarding: true, attacking: true);
            Assert.AreEqual(PlayerState.Idle, sm.Current);
        }

        [Test]
        public void EnteringAttack_RaisesStateChangedOnce()
        {
            var sm = new PlayerStateMachine();
            var changes = new List<PlayerState>();
            sm.StateChanged += changes.Add;

            sm.Tick(true, false, false, true); // Idle -> Attack
            sm.Tick(true, false, false, true); // Attack -> Attack（通知なし）

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(PlayerState.Attack, changes[0]);
        }

        [Test]
        public void Reset_ReturnsToIdle()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, true);
            sm.Reset();
            Assert.AreEqual(PlayerState.Idle, sm.Current);
        }

        [Test]
        public void LegacyThreeArgTick_NeverAttacks()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, true, false);
            Assert.AreEqual(PlayerState.Move, sm.Current);
        }
    }
}
