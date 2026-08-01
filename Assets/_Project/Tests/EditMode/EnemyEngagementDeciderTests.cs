using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-03：追跡・間合い・帰還の純粋判定 <see cref="EnemyEngagementDecider"/> を検証する（§5）。範囲超過→帰還、帰還中の
    /// 再認識抑制、到達→待機→復帰、Alert の追跡/保持/後退、Suspicious の調査を確認する。再現可能。
    /// </summary>
    public sealed class EnemyEngagementDeciderTests
    {
        private const float Radius = 12f;
        private const float Stop = 1.6f;
        private const float TooClose = 0.96f;
        private const float Eps = 0.35f;
        private const float ReturnWait = 1f;
        private static readonly Vector3 Home = Vector3.zero;

        private static EngagementInput In(PerceptionPhase phase, bool hasTarget, Vector3 targetPos, Vector3 selfPos,
            EnemyEngagementMode mode, float returnWaitRemaining = 0f, float dt = 0.1f)
        {
            return new EngagementInput(phase, hasTarget, targetPos, selfPos, Home, Radius, Stop, TooClose, Eps,
                mode, returnWaitRemaining, ReturnWait, dt);
        }

        [Test]
        public void OutsideActivity_TriggersReturn_SuppressesPerception()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Alert, true, new Vector3(21f, 0, 0),
                new Vector3(20f, 0, 0), EnemyEngagementMode.Chase));
            Assert.AreEqual(EnemyEngagementMode.Return, o.Mode);
            Assert.AreEqual(EnemyState.Return, o.State);
            Assert.IsTrue(o.SuppressPerception, "帰還中は再認識しない。");
            Assert.IsTrue(o.HasMoveTarget);
            Assert.AreEqual(Home, o.MoveTarget, "初期位置へ向かう。");
        }

        [Test]
        public void Return_ArrivesHome_EntersReturnWait()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Unaware, false, Vector3.zero,
                new Vector3(0.1f, 0, 0), EnemyEngagementMode.Return));
            Assert.AreEqual(EnemyEngagementMode.ReturnWait, o.Mode);
            Assert.AreEqual(ReturnWait, o.ReturnWaitRemaining, 1e-4f, "到達で待機開始。");
            Assert.IsTrue(o.SuppressPerception);
        }

        [Test]
        public void ReturnWait_CountsDown_ThenResumesIdle()
        {
            var mid = EnemyEngagementDecider.Decide(In(PerceptionPhase.Unaware, false, Vector3.zero, Vector3.zero,
                EnemyEngagementMode.ReturnWait, returnWaitRemaining: 1f, dt: 0.3f));
            Assert.AreEqual(EnemyEngagementMode.ReturnWait, mid.Mode);
            Assert.AreEqual(0.7f, mid.ReturnWaitRemaining, 1e-4f);
            Assert.IsTrue(mid.SuppressPerception, "待機中も認識抑制。");

            var done = EnemyEngagementDecider.Decide(In(PerceptionPhase.Unaware, false, Vector3.zero, Vector3.zero,
                EnemyEngagementMode.ReturnWait, returnWaitRemaining: 0.2f, dt: 0.3f));
            Assert.AreEqual(EnemyEngagementMode.Idle, done.Mode, "待機明けで通常へ。");
            Assert.AreEqual(EnemyState.Idle, done.State);
            Assert.IsFalse(done.SuppressPerception, "認識を再開する。");
        }

        [Test]
        public void Alert_FarTarget_Chases()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Alert, true, new Vector3(5f, 0, 0),
                Vector3.zero, EnemyEngagementMode.Idle));
            Assert.AreEqual(EnemyEngagementMode.Chase, o.Mode);
            Assert.AreEqual(EnemyState.Chase, o.State);
            Assert.IsTrue(o.HasMoveTarget);
            Assert.AreEqual(new Vector3(5f, 0, 0), o.MoveTarget);
        }

        [Test]
        public void Alert_InStopBand_Holds_NoMove()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Alert, true, new Vector3(1.2f, 0, 0),
                Vector3.zero, EnemyEngagementMode.Chase));
            Assert.AreEqual(EnemyEngagementMode.Hold, o.Mode);
            Assert.AreEqual(EnemyState.Alert, o.State, "攻撃帯では Alert 保持（攻撃は P3-04）。");
            Assert.IsFalse(o.HasMoveTarget, "停止帯では移動しない。");
        }

        [Test]
        public void Alert_TooClose_Repositions_BackAway()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Alert, true, new Vector3(0.5f, 0, 0),
                new Vector3(1f, 0, 0), EnemyEngagementMode.Hold));
            Assert.AreEqual(EnemyEngagementMode.Reposition, o.Mode);
            Assert.AreEqual(EnemyState.Reposition, o.State);
            Assert.AreEqual(RepositionReason.TooClose, o.RepositionReason);
            Assert.IsTrue(o.HasMoveTarget, "後退目標を持つ。");
        }

        [Test]
        public void Suspicious_Investigates_LastKnown()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Suspicious, true, new Vector3(4f, 0, 0),
                Vector3.zero, EnemyEngagementMode.Idle));
            Assert.AreEqual(EnemyEngagementMode.Investigate, o.Mode);
            Assert.AreEqual(EnemyState.Suspicious, o.State);
            Assert.IsTrue(o.HasMoveTarget);
            Assert.AreEqual(new Vector3(4f, 0, 0), o.MoveTarget, "最終確認位置へ向かう。");
        }

        [Test]
        public void Suspicious_ReachedLastKnown_GivesUpToIdle()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Suspicious, true, new Vector3(0.1f, 0, 0),
                Vector3.zero, EnemyEngagementMode.Investigate));
            Assert.AreEqual(EnemyEngagementMode.Idle, o.Mode);
            Assert.AreEqual(EnemyState.Idle, o.State);
        }

        [Test]
        public void Unaware_AwayFromHome_ReturnsHome()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Unaware, false, Vector3.zero,
                new Vector3(5f, 0, 0), EnemyEngagementMode.Idle));
            Assert.AreEqual(EnemyEngagementMode.Return, o.Mode, "見失って初期位置から離れていれば帰還。");
            Assert.IsTrue(o.SuppressPerception);
        }

        [Test]
        public void Unaware_AtHome_Idles()
        {
            var o = EnemyEngagementDecider.Decide(In(PerceptionPhase.Unaware, false, Vector3.zero,
                new Vector3(0.1f, 0, 0), EnemyEngagementMode.Idle));
            Assert.AreEqual(EnemyEngagementMode.Idle, o.Mode);
            Assert.AreEqual(EnemyState.Idle, o.State);
            Assert.IsFalse(o.SuppressPerception);
        }
    }
}
