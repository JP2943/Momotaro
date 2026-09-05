using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-01：仲間状態機（<see cref="CompanionStateMachine"/>）と優先度（<see cref="CompanionStatePriority"/>）、
    /// 通知チャネル（<see cref="CompanionStateChannel"/>）を検証する。被弾由来の割り込み（Stagger／Down）、
    /// 仲間固有の 2 規則（Down は終端ではない／退場はどこからでも成立）、不正遷移を黙って無視しないこと、
    /// 同一署名の不正を 1 回だけ記録することを固定する。純粋 C# のため決定的に検証できる。
    /// </summary>
    public sealed class CompanionStateMachineTests
    {
        private readonly List<CompanionStateChanged> _changes = new List<CompanionStateChanged>();
        private readonly List<string> _illegal = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _changes.Clear();
            _illegal.Clear();
        }

        private CompanionStateMachine Make(CompanionState initial = CompanionState.Idle)
        {
            return new CompanionStateMachine(42, initial, c => _changes.Add(c), m => _illegal.Add(m));
        }

        // ---- 初期化 ----

        [Test]
        public void Initial_StateAndReason()
        {
            CompanionStateMachine m = Make(CompanionState.Away);

            Assert.AreEqual(CompanionState.Away, m.Current);
            Assert.AreEqual(CompanionStateChangeReason.Spawned, m.LastReason);
            Assert.AreEqual(0, _changes.Count, "生成では通知しない。");
        }

        [Test]
        public void Reset_AppliesWithoutPriorityChecks()
        {
            CompanionStateMachine m = Make(CompanionState.Down);

            m.Reset(CompanionState.Follow);

            Assert.AreEqual(CompanionState.Follow, m.Current, "Reset は Down からでも通る（加入・再配置）。");
            Assert.AreEqual(CompanionStateChangeReason.Spawned, m.LastReason);
            Assert.AreEqual(1, _changes.Count);
            Assert.AreEqual(CompanionState.Down, _changes[0].Previous);
            Assert.AreEqual(42, _changes[0].ActorId);
        }

        // ---- 被弾由来の割り込み ----

        [Test]
        public void ForceHitState_Stagger_InterruptsNormalStates()
        {
            CompanionStateMachine m = Make(CompanionState.AttackActive);

            Assert.IsTrue(m.ForceHitState(CompanionState.Stagger, CompanionStateChangeReason.Staggered));
            Assert.AreEqual(CompanionState.Stagger, m.Current);
            Assert.AreEqual(1, _changes.Count);
        }

        [Test]
        public void ForceHitState_Down_UpgradesFromStagger()
        {
            CompanionStateMachine m = Make(CompanionState.Stagger);

            Assert.IsTrue(m.ForceHitState(CompanionState.Down, CompanionStateChangeReason.Defeated));
            Assert.AreEqual(CompanionState.Down, m.Current);
        }

        [Test]
        public void ForceHitState_Stagger_DoesNotDowngradeFromDown()
        {
            CompanionStateMachine m = Make(CompanionState.Down);

            Assert.IsFalse(m.ForceHitState(CompanionState.Stagger, CompanionStateChangeReason.Staggered));
            Assert.AreEqual(CompanionState.Down, m.Current);
            Assert.AreEqual(0, _changes.Count, "ダウングレードは不正ではなく無視（記録もしない）。");
            Assert.AreEqual(0, m.IllegalTransitionCount);
        }

        [Test]
        public void ForceHitState_SameState_IsIgnored()
        {
            CompanionStateMachine m = Make(CompanionState.Stagger);

            Assert.IsFalse(m.ForceHitState(CompanionState.Stagger, CompanionStateChangeReason.Staggered));
            Assert.AreEqual(0, _changes.Count);
        }

        [Test]
        public void ForceHitState_WhileAway_IsIgnored()
        {
            CompanionStateMachine m = Make(CompanionState.Away);

            Assert.IsFalse(m.ForceHitState(CompanionState.Stagger, CompanionStateChangeReason.Staggered));
            Assert.IsFalse(m.ForceHitState(CompanionState.Down, CompanionStateChangeReason.Defeated));
            Assert.AreEqual(CompanionState.Away, m.Current, "退場中は場に居ないため被弾状態を持たない。");
        }

        [Test]
        public void ForceHitState_RejectsNonHitStates()
        {
            CompanionStateMachine m = Make(CompanionState.Follow);

            Assert.IsFalse(m.ForceHitState(CompanionState.Guard, CompanionStateChangeReason.DefensiveAction));
            Assert.AreEqual(CompanionState.Follow, m.Current);
        }

        // ---- 任意遷移 ----

        [Test]
        public void TryTransition_SameState_IsNoOp()
        {
            CompanionStateMachine m = Make(CompanionState.Follow);

            Assert.IsFalse(m.TryTransition(CompanionState.Follow, CompanionStateChangeReason.FollowResumed));
            Assert.AreEqual(0, _changes.Count);
            Assert.AreEqual(0, m.IllegalTransitionCount);
        }

        [Test]
        public void TryTransition_NormalFlow_IsAllowed()
        {
            CompanionStateMachine m = Make(CompanionState.Follow);

            Assert.IsTrue(m.TryTransition(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget));
            Assert.IsTrue(m.TryTransition(CompanionState.AttackPrepare, CompanionStateChangeReason.AttackStarted));
            Assert.IsTrue(m.TryTransition(CompanionState.AttackActive, CompanionStateChangeReason.AttackAdvanced));
            Assert.IsTrue(m.TryTransition(CompanionState.AttackRecovery, CompanionStateChangeReason.AttackFinished));
            Assert.IsTrue(m.TryTransition(CompanionState.Follow, CompanionStateChangeReason.LostTarget));
            Assert.AreEqual(5, _changes.Count);
            Assert.AreEqual(0, m.IllegalTransitionCount);
        }

        [Test]
        public void Down_IsNotTerminal_RecoveredIsAllowed()
        {
            CompanionStateMachine m = Make(CompanionState.Down);

            Assert.IsTrue(m.TryTransition(CompanionState.Recovering, CompanionStateChangeReason.Recovered),
                "仲間は撃破されても復帰する（敵と違い Down は終端ではない）。");
            Assert.IsTrue(m.TryTransition(CompanionState.Follow, CompanionStateChangeReason.FollowResumed));
        }

        [Test]
        public void Down_ArbitraryExit_IsIllegal()
        {
            CompanionStateMachine m = Make(CompanionState.Down);

            Assert.IsFalse(m.TryTransition(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget));
            Assert.AreEqual(CompanionState.Down, m.Current);
            Assert.AreEqual(1, m.IllegalTransitionCount, "黙って無視せず記録する。");
            Assert.AreEqual(1, _illegal.Count);
            StringAssert.Contains("illegal transition", _illegal[0]);
        }

        [Test]
        public void Stagger_ArbitraryExit_IsIllegal()
        {
            CompanionStateMachine m = Make(CompanionState.Stagger);

            Assert.IsFalse(m.TryTransition(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget));
            Assert.AreEqual(CompanionState.Stagger, m.Current);
            Assert.AreEqual(1, m.IllegalTransitionCount);
        }

        [Test]
        public void Stagger_Recovered_IsAllowed()
        {
            CompanionStateMachine m = Make(CompanionState.Stagger);

            Assert.IsTrue(m.TryTransition(CompanionState.Follow, CompanionStateChangeReason.Recovered));
            Assert.AreEqual(0, m.IllegalTransitionCount);
        }

        [Test]
        public void Leave_IsAllowedFromAnyState()
        {
            foreach (CompanionState from in new[]
            {
                CompanionState.Idle, CompanionState.Follow, CompanionState.AttackActive,
                CompanionState.Protect, CompanionState.Stagger, CompanionState.Down,
            })
            {
                CompanionStateMachine m = Make(from);

                Assert.IsTrue(m.TryTransition(CompanionState.Away, CompanionStateChangeReason.Left),
                    from + " からの退場は成立する（残留を作らない）。");
                Assert.AreEqual(CompanionState.Away, m.Current);
                Assert.AreEqual(0, m.IllegalTransitionCount, from + " からの退場は不正扱いにしない。");
            }
        }

        [Test]
        public void Away_WithoutLeaveReason_FollowsNormalRules()
        {
            CompanionStateMachine m = Make(CompanionState.Down);

            // 退場以外の理由で Away へ行こうとした場合は、Down の離脱規則に従って弾かれる。
            Assert.IsFalse(m.TryTransition(CompanionState.Away, CompanionStateChangeReason.OrderedByPlayer));
            Assert.AreEqual(CompanionState.Down, m.Current);
            Assert.AreEqual(1, m.IllegalTransitionCount);
        }

        [Test]
        public void IllegalTransition_WithSameSignature_IsRecordedOnce()
        {
            CompanionStateMachine m = Make(CompanionState.Down);

            m.TryTransition(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget);
            m.TryTransition(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget);
            m.TryTransition(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget);

            Assert.AreEqual(1, m.IllegalTransitionCount, "同一署名は 1 回だけ記録する（Console の氾濫を防ぐ）。");
            Assert.AreEqual(1, _illegal.Count);
        }

        // ---- 優先度 ----

        [Test]
        public void Priority_OrdersLeaveAboveDown_AndProtectAboveAttack()
        {
            Assert.Greater(CompanionStatePriority.Rank(CompanionState.Away),
                CompanionStatePriority.Rank(CompanionState.Down), "退場はダウンより上（残留を作らない）。");
            Assert.Greater(CompanionStatePriority.Rank(CompanionState.Down),
                CompanionStatePriority.Rank(CompanionState.Stagger));
            Assert.Greater(CompanionStatePriority.Rank(CompanionState.Protect),
                CompanionStatePriority.Rank(CompanionState.AttackActive), "かばうは自分の攻撃より優先する。");
            Assert.Greater(CompanionStatePriority.Rank(CompanionState.Chase),
                CompanionStatePriority.Rank(CompanionState.Follow));
            Assert.Greater(CompanionStatePriority.Rank(CompanionState.Follow),
                CompanionStatePriority.Rank(CompanionState.Idle));
        }

        [Test]
        public void Priority_CanInterrupt_IsStrict()
        {
            Assert.IsTrue(CompanionStatePriority.CanInterrupt(CompanionState.Down, CompanionState.AttackActive));
            Assert.IsFalse(CompanionStatePriority.CanInterrupt(CompanionState.Follow, CompanionState.Chase));
            Assert.IsFalse(CompanionStatePriority.CanInterrupt(CompanionState.Guard, CompanionState.Evade),
                "同順は割り込めない（呼び出し側の理由で扱う）。");
        }

        [Test]
        public void Priority_ForcedByHit_IsStaggerAndDownOnly()
        {
            Assert.IsTrue(CompanionStatePriority.IsForcedByHit(CompanionState.Stagger));
            Assert.IsTrue(CompanionStatePriority.IsForcedByHit(CompanionState.Down));
            Assert.IsFalse(CompanionStatePriority.IsForcedByHit(CompanionState.Protect));
            Assert.IsFalse(CompanionStatePriority.IsForcedByHit(CompanionState.Away),
                "仲間にスタンは無く、退場は被弾由来ではない。");
        }

        // ---- 通知チャネル ----

        private sealed class Recorder : ICompanionStateListener
        {
            public readonly List<CompanionStateChanged> Received = new List<CompanionStateChanged>();
            public void OnCompanionStateChanged(in CompanionStateChanged change) => Received.Add(change);
        }

        [Test]
        public void Channel_PublishesToListeners_WithoutDuplicates()
        {
            var channel = new CompanionStateChannel();
            var a = new Recorder();
            channel.AddListener(a);
            channel.AddListener(a); // 重複登録は無視。
            channel.AddListener(null);

            Assert.AreEqual(1, channel.ListenerCount);

            channel.Publish(new CompanionStateChanged(1, CompanionState.Idle, CompanionState.Follow,
                CompanionStateChangeReason.FollowResumed));

            Assert.AreEqual(1, a.Received.Count);
            Assert.AreEqual(CompanionState.Follow, a.Received[0].Current);
        }

        [Test]
        public void Channel_RemoveListener_StopsDelivery()
        {
            var channel = new CompanionStateChannel();
            var a = new Recorder();
            channel.AddListener(a);
            channel.RemoveListener(a);

            channel.Publish(new CompanionStateChanged(1, CompanionState.Idle, CompanionState.Follow,
                CompanionStateChangeReason.FollowResumed));

            Assert.AreEqual(0, a.Received.Count);
            Assert.AreEqual(0, channel.ListenerCount);
        }

        [Test]
        public void Channel_IsSafeWithoutListeners()
        {
            var channel = new CompanionStateChannel();

            Assert.DoesNotThrow(() => channel.Publish(new CompanionStateChanged(1, CompanionState.Idle,
                CompanionState.Follow, CompanionStateChangeReason.FollowResumed)));
        }
    }
}
