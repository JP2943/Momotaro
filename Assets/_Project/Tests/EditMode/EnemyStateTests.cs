using System.Collections.Generic;
using Momotaro.Gameplay.Enemy;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-01：敵状態の優先度（<see cref="EnemyStatePriority"/>）と状態機（<see cref="EnemyStateMachine"/>）を検証する（§2.4）。
    /// 被弾由来 Down/Stunned/Stagger の割り込み順、不正遷移の 1 回記録、復帰遷移を確認する（純粋・再現可能）。
    /// </summary>
    public sealed class EnemyStateTests
    {
        [Test]
        public void Priority_OrderMatchesSpec()
        {
            // Down > Event > Stunned > Stagger > AttackActive > AttackPrepare > AttackRecovery > Guard > Return > Chase > Alert > Patrol > Idle
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Down), EnemyStatePriority.Rank(EnemyState.Event));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Event), EnemyStatePriority.Rank(EnemyState.Stunned));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Stunned), EnemyStatePriority.Rank(EnemyState.Stagger));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Stagger), EnemyStatePriority.Rank(EnemyState.AttackActive));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.AttackActive), EnemyStatePriority.Rank(EnemyState.AttackPrepare));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.AttackPrepare), EnemyStatePriority.Rank(EnemyState.AttackRecovery));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.AttackRecovery), EnemyStatePriority.Rank(EnemyState.Guard));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Guard), EnemyStatePriority.Rank(EnemyState.Return));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Return), EnemyStatePriority.Rank(EnemyState.Chase));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Chase), EnemyStatePriority.Rank(EnemyState.Alert));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Alert), EnemyStatePriority.Rank(EnemyState.Patrol));
            Assert.Greater(EnemyStatePriority.Rank(EnemyState.Patrol), EnemyStatePriority.Rank(EnemyState.Idle));
        }

        [Test]
        public void Priority_CanInterrupt_OnlyStrictlyHigher()
        {
            Assert.IsTrue(EnemyStatePriority.CanInterrupt(EnemyState.Stunned, EnemyState.AttackActive));
            Assert.IsFalse(EnemyStatePriority.CanInterrupt(EnemyState.AttackActive, EnemyState.Stunned));
            Assert.IsFalse(EnemyStatePriority.CanInterrupt(EnemyState.Chase, EnemyState.Chase), "同順は割り込み不可。");
        }

        [Test]
        public void Priority_IsForcedByHit()
        {
            Assert.IsTrue(EnemyStatePriority.IsForcedByHit(EnemyState.Down));
            Assert.IsTrue(EnemyStatePriority.IsForcedByHit(EnemyState.Stunned));
            Assert.IsTrue(EnemyStatePriority.IsForcedByHit(EnemyState.Stagger));
            Assert.IsFalse(EnemyStatePriority.IsForcedByHit(EnemyState.Chase));
        }

        [Test]
        public void Machine_ForceHitState_StunOverridesAttack_ButStaggerDoesNotDowngradeStun()
        {
            var m = new EnemyStateMachine(1, EnemyState.AttackActive);
            Assert.IsTrue(m.ForceHitState(EnemyState.Stunned, EnemyStateChangeReason.Stunned), "スタンは攻撃を割り込む。");
            Assert.AreEqual(EnemyState.Stunned, m.Current);

            Assert.IsFalse(m.ForceHitState(EnemyState.Stagger, EnemyStateChangeReason.Staggered), "スタン中にひるみへダウングレードしない。");
            Assert.AreEqual(EnemyState.Stunned, m.Current);

            Assert.IsTrue(m.ForceHitState(EnemyState.Down, EnemyStateChangeReason.Defeated), "Down は全てを割り込む。");
            Assert.AreEqual(EnemyState.Down, m.Current);
        }

        [Test]
        public void Machine_DownIsTerminal_IllegalLeaveLoggedOnce()
        {
            var logs = new List<string>();
            var m = new EnemyStateMachine(7, EnemyState.Idle, null, s => logs.Add(s));
            m.ForceHitState(EnemyState.Down, EnemyStateChangeReason.Defeated);

            // Down から通常状態への離脱は不正。複数回試みても記録は 1 回のみ。
            Assert.IsFalse(m.TryTransition(EnemyState.Chase, EnemyStateChangeReason.PerceivedTarget));
            Assert.IsFalse(m.TryTransition(EnemyState.Chase, EnemyStateChangeReason.PerceivedTarget));
            Assert.AreEqual(EnemyState.Down, m.Current, "Down のまま。");
            Assert.AreEqual(1, m.IllegalTransitionCount, "同一署名の不正は 1 回だけ記録。");
            Assert.AreEqual(1, logs.Count, "ロガーへの出力も 1 回。");

            // 復活（Spawned）でのみ離脱できる。
            Assert.IsTrue(m.TryTransition(EnemyState.Idle, EnemyStateChangeReason.Spawned));
            Assert.AreEqual(EnemyState.Idle, m.Current);
        }

        [Test]
        public void Machine_RecoverFromStun_IsLegal()
        {
            var m = new EnemyStateMachine(2, EnemyState.Chase);
            m.ForceHitState(EnemyState.Stunned, EnemyStateChangeReason.Stunned);
            Assert.IsTrue(m.TryTransition(EnemyState.Idle, EnemyStateChangeReason.Recovered), "復帰理由での離脱は正当。");
            Assert.AreEqual(EnemyState.Idle, m.Current);
        }

        [Test]
        public void Machine_SameState_IsNoOp_NoEvent()
        {
            int events = 0;
            var m = new EnemyStateMachine(3, EnemyState.Chase, _ => events++);
            Assert.IsFalse(m.TryTransition(EnemyState.Chase, EnemyStateChangeReason.PerceivedTarget));
            Assert.AreEqual(0, events, "同一状態への遷移は通知しない。");
        }

        [Test]
        public void Machine_PublishesTypedChange()
        {
            EnemyStateChanged captured = default;
            var m = new EnemyStateMachine(42, EnemyState.Idle, c => captured = c);
            m.TryTransition(EnemyState.Alert, EnemyStateChangeReason.PerceivedTarget);
            Assert.AreEqual(42, captured.ActorId);
            Assert.AreEqual(EnemyState.Idle, captured.Previous);
            Assert.AreEqual(EnemyState.Alert, captured.Current);
            Assert.AreEqual(EnemyStateChangeReason.PerceivedTarget, captured.Reason);
        }
    }
}
