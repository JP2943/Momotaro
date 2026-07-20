using System.Collections.Generic;
using Momotaro.Gameplay.Player;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-02：状態機械の攻撃状態（優先度・継続時間・Hold 非連続・遮断/Reset での解除）を検証する。
    /// </summary>
    public sealed class AttackStateMachineTests
    {
        private const float Dur = 0.40f;

        [Test]
        public void AttackRequest_EntersAttackState()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(enabled: true, isMoving: false, guarding: false, attackRequested: true, deltaTime: 0f, attackDuration: Dur);

            Assert.AreEqual(PlayerState.Attack, sm.Current);
            Assert.IsTrue(sm.IsAttacking);
        }

        [Test]
        public void Attack_OutranksGuardAndMove()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, isMoving: true, guarding: true, attackRequested: true, deltaTime: 0f, attackDuration: Dur);
            Assert.AreEqual(PlayerState.Attack, sm.Current, "攻撃は移動・ガードより優先。");
        }

        [Test]
        public void Attack_HoldsForDuration_ThenReturnsToMoveOrIdle()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, true, 0f, Dur);          // 開始 remaining=0.40
            sm.Tick(true, false, false, false, 0.20f, Dur);      // remaining=0.20
            Assert.AreEqual(PlayerState.Attack, sm.Current, "継続時間内は Attack。");

            sm.Tick(true, false, false, false, 0.25f, Dur);      // 累計 0.45 > 0.40
            Assert.AreEqual(PlayerState.Idle, sm.Current, "継続時間経過で通常状態へ。");
            Assert.IsFalse(sm.IsAttacking);
        }

        [Test]
        public void AttackRequest_DuringAttack_IsIgnored_NoRestart()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, true, 0f, Dur);          // remaining=0.40
            sm.Tick(true, false, false, true, 0.20f, Dur);       // 攻撃中の再要求は無視。remaining=0.20
            sm.Tick(true, false, false, false, 0.25f, Dur);      // 累計 0.45 → 終了
            Assert.AreEqual(PlayerState.Idle, sm.Current, "攻撃中の再要求で再スタートしない。");
        }

        [Test]
        public void AfterAttack_ReturnsToMove_WhenMoving()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, true, 0f, Dur);
            sm.Tick(true, true, false, false, Dur, Dur);         // 終了と同時に移動入力
            Assert.AreEqual(PlayerState.Move, sm.Current);
        }

        [Test]
        public void Disabled_DuringAttack_ForcesIdle_AndClearsTimer()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, true, 0f, Dur);
            Assert.IsTrue(sm.IsAttacking);

            sm.Tick(enabled: false, isMoving: true, guarding: true, attackRequested: true, deltaTime: 0f, attackDuration: Dur);
            Assert.AreEqual(PlayerState.Idle, sm.Current);
            Assert.IsFalse(sm.IsAttacking, "無効化で攻撃タイマーも解除。");
        }

        [Test]
        public void Reset_ClearsAttackState()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, true, 0f, Dur);
            sm.Reset();
            Assert.AreEqual(PlayerState.Idle, sm.Current);
            Assert.IsFalse(sm.IsAttacking);
        }

        [Test]
        public void EnteringAttack_RaisesStateChangedOnce()
        {
            var sm = new PlayerStateMachine();
            var changes = new List<PlayerState>();
            sm.StateChanged += changes.Add;

            sm.Tick(true, false, false, true, 0f, Dur);          // Idle -> Attack
            sm.Tick(true, false, false, false, 0.10f, Dur);      // Attack -> Attack（通知なし）

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(PlayerState.Attack, changes[0]);
        }

        [Test]
        public void LegacyTick_NeverAttacks()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, true, false);   // 3 引数版
            Assert.AreEqual(PlayerState.Move, sm.Current);
            Assert.IsFalse(sm.IsAttacking);
        }

        [Test]
        public void ZeroDuration_RequestDoesNotEnterAttack()
        {
            var sm = new PlayerStateMachine();
            sm.Tick(true, false, false, attackRequested: true, deltaTime: 0f, attackDuration: 0f);
            Assert.AreEqual(PlayerState.Idle, sm.Current, "継続時間 0 では攻撃状態にならない。");
        }
    }
}
