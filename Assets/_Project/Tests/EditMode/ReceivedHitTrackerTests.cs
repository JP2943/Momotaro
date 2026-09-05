using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-01：受け手側の多重ヒット排除（<see cref="ReceivedHitTracker"/>）を検証する。肩代わりによる転送は攻撃側の
    /// <see cref="MultiHitTracker"/> を経由しないため、守護者（仲間）の被弾入口で <see cref="HitId"/> 単位に 1 回だけ
    /// 受理する必要がある。直接命中と転送のどちらが先でも 2 回目を弾くこと、別段・別発動は別命中として受理すること、
    /// 記録が無制限に増えないことを固定する。
    /// </summary>
    public sealed class ReceivedHitTrackerTests
    {
        [Test]
        public void SameHitId_IsAcceptedOnlyOnce()
        {
            var tracker = new ReceivedHitTracker();
            HitId id = HitId.Single(1);

            Assert.IsTrue(tracker.TryAccept(id), "初回は受理する。");
            Assert.IsFalse(tracker.TryAccept(id), "同じ命中の 2 回目は弾く（直接命中と転送の重複）。");
            Assert.IsTrue(tracker.HasAccepted(id));
            Assert.AreEqual(1, tracker.Count);
        }

        [Test]
        public void DifferentStage_IsAcceptedSeparately()
        {
            var tracker = new ReceivedHitTracker();

            Assert.IsTrue(tracker.TryAccept(new HitId(7, 0)));
            Assert.IsTrue(tracker.TryAccept(new HitId(7, 1)), "同一発動でも段が違えば別命中。");
            Assert.IsTrue(tracker.TryAccept(new HitId(7, 2)));
            Assert.IsFalse(tracker.TryAccept(new HitId(7, 1)), "同じ段の 2 回目は弾く。");
            Assert.AreEqual(3, tracker.Count);
        }

        [Test]
        public void DifferentInstance_IsAcceptedSeparately()
        {
            var tracker = new ReceivedHitTracker();

            Assert.IsTrue(tracker.TryAccept(HitId.Single(1)));
            Assert.IsTrue(tracker.TryAccept(HitId.Single(2)), "別の攻撃発動は別命中。");
        }

        [Test]
        public void OrderOfArrival_DoesNotMatter()
        {
            // 転送 → 直接命中 の順でも、直接命中 → 転送 の順でも、受理は 1 回だけになる。
            HitId id = new HitId(3, 1);

            var transferFirst = new ReceivedHitTracker();
            Assert.IsTrue(transferFirst.TryAccept(id), "転送が先に届いた。");
            Assert.IsFalse(transferFirst.TryAccept(id), "後から届いた直接命中は弾く。");

            var directFirst = new ReceivedHitTracker();
            Assert.IsTrue(directFirst.TryAccept(id), "直接命中が先に届いた。");
            Assert.IsFalse(directFirst.TryAccept(id), "後から届いた転送は弾く。");
        }

        [Test]
        public void Clear_ResetsRecords()
        {
            var tracker = new ReceivedHitTracker();
            HitId id = HitId.Single(9);
            tracker.TryAccept(id);

            tracker.Clear();

            Assert.AreEqual(0, tracker.Count);
            Assert.IsFalse(tracker.HasAccepted(id));
            Assert.IsTrue(tracker.TryAccept(id), "初期化後は同じ命中を再び受理できる（新しい Encounter 相当）。");
        }

        [Test]
        public void Clear_IsSafeWhenEmpty()
        {
            var tracker = new ReceivedHitTracker();

            Assert.DoesNotThrow(() => tracker.Clear());
            Assert.DoesNotThrow(() => tracker.Clear());
            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void Records_DoNotGrowBeyondCapacity()
        {
            var tracker = new ReceivedHitTracker(4);

            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(tracker.TryAccept(HitId.Single(i)));
            }

            Assert.AreEqual(4, tracker.Count, "上限を超えて蓄積しない（Clear 忘れでも際限なく増えない）。");
            Assert.IsTrue(tracker.HasAccepted(HitId.Single(99)), "直近の命中は覚えている。");
            Assert.IsFalse(tracker.HasAccepted(HitId.Single(0)), "古い命中は忘れる（FIFO）。");
        }

        [Test]
        public void RecentHits_AreStillRejected_WithinCapacity()
        {
            var tracker = new ReceivedHitTracker(4);
            HitId id = HitId.Single(1);
            tracker.TryAccept(id);

            // 容量内に収まる範囲で他の命中が挟まっても、重複は弾き続ける（同一フレームの多重命中を想定）。
            tracker.TryAccept(HitId.Single(2));
            tracker.TryAccept(HitId.Single(3));

            Assert.IsFalse(tracker.TryAccept(id));
        }

        [Test]
        public void Capacity_IsClampedToAtLeastOne()
        {
            var tracker = new ReceivedHitTracker(0);

            Assert.AreEqual(1, tracker.Capacity);
            Assert.IsTrue(tracker.TryAccept(HitId.Single(1)));
            Assert.IsFalse(tracker.TryAccept(HitId.Single(1)));
        }
    }
}
