using System;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-01：同一攻撃の同一対象への多重ヒット防止、別攻撃ID／別段／別対象での再命中可否を検証する。
    /// </summary>
    public sealed class MultiHitTrackerTests
    {
        private sealed class FakeTarget : IDamageable
        {
            public FakeTarget(int id)
            {
                DamageableId = id;
            }

            public int DamageableId { get; }

            public void ReceiveHit(in HitInfo hit)
            {
                // P2-01 では受付契約のみ。数値適用はしない。
            }
        }

        [Test]
        public void SameHitId_SameTarget_BlocksSecondHit()
        {
            var tracker = new MultiHitTracker();
            var target = new FakeTarget(1);
            var hitId = HitId.Single(10);

            Assert.IsTrue(tracker.TryRegisterHit(hitId, target), "初回は通る。");
            Assert.IsFalse(tracker.TryRegisterHit(hitId, target), "同一攻撃・同一対象の 2 回目は弾く。");
            Assert.AreEqual(1, tracker.Count);
        }

        [Test]
        public void DifferentInstanceId_SameTarget_AllowsHitAgain()
        {
            var tracker = new MultiHitTracker();
            var target = new FakeTarget(1);

            Assert.IsTrue(tracker.TryRegisterHit(HitId.Single(10), target));
            Assert.IsTrue(tracker.TryRegisterHit(HitId.Single(11), target), "別の攻撃発動なら再度命中できる。");
        }

        [Test]
        public void DifferentStage_SameTarget_AllowsHitAgain()
        {
            var tracker = new MultiHitTracker();
            var target = new FakeTarget(1);

            Assert.IsTrue(tracker.TryRegisterHit(new HitId(10, 0), target));
            Assert.IsTrue(tracker.TryRegisterHit(new HitId(10, 1), target), "多段（別段）なら同一対象へ再度命中できる。");
        }

        [Test]
        public void SameHitId_DifferentTargets_EachHitOnce()
        {
            var tracker = new MultiHitTracker();
            var a = new FakeTarget(1);
            var b = new FakeTarget(2);
            var hitId = HitId.Single(10);

            Assert.IsTrue(tracker.TryRegisterHit(hitId, a), "対象 A に命中。");
            Assert.IsTrue(tracker.TryRegisterHit(hitId, b), "対象 B にも命中できる。");
            Assert.IsFalse(tracker.TryRegisterHit(hitId, a), "A への 2 回目は弾く。");
        }

        [Test]
        public void HasHit_ReflectsRegistration_WithoutSideEffect()
        {
            var tracker = new MultiHitTracker();
            var target = new FakeTarget(3);
            var hitId = HitId.Single(5);

            Assert.IsFalse(tracker.HasHit(hitId, target));
            Assert.IsTrue(tracker.TryRegisterHit(hitId, target));
            Assert.IsTrue(tracker.HasHit(hitId, target));
            Assert.AreEqual(1, tracker.Count, "HasHit は登録件数を変えない。");
        }

        [Test]
        public void Clear_ResetsRegistrations()
        {
            var tracker = new MultiHitTracker();
            var target = new FakeTarget(1);
            var hitId = HitId.Single(10);

            tracker.TryRegisterHit(hitId, target);
            tracker.Clear();

            Assert.AreEqual(0, tracker.Count);
            Assert.IsTrue(tracker.TryRegisterHit(hitId, target), "Clear 後は再登録できる。");
        }

        [Test]
        public void NullTarget_Throws()
        {
            var tracker = new MultiHitTracker();
            Assert.Throws<ArgumentNullException>(() => tracker.TryRegisterHit(HitId.Single(1), null));
            Assert.Throws<ArgumentNullException>(() => tracker.HasHit(HitId.Single(1), null));
        }
    }
}
