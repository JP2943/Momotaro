using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-03B：段ごとの Swing Token（<see cref="HitId"/>）＋ <see cref="MultiHitTracker"/> による命中規則を検証する。
    /// 同一段では同一対象へ 1 回だけ、複数対象へは各 1 回、段が変われば新 Token で再命中できる
    /// （PlayerStateController の Hitbox 判定ロジックと同じ規則）。
    /// </summary>
    public sealed class AttackComboHitTests
    {
        private sealed class FakeTarget : IDamageable
        {
            public FakeTarget(int id) { DamageableId = id; }
            public int DamageableId { get; }
            public int Received { get; private set; }
            public void ReceiveHit(in HitInfo hit) { Received++; }
        }

        // 駆動側と同じく「Hitbox 内の対象へ 1 段 1 回だけ HitInfo を届ける」処理を模す。
        private static void DeliverOverlap(MultiHitTracker tracker, HitId swing, FakeTarget[] overlapped)
        {
            foreach (FakeTarget t in overlapped)
            {
                if (tracker.TryRegisterHit(swing, t))
                {
                    HitInfo hit = HitBuilder.FromSnapshot(default, null, t, UnityEngine.Vector3.forward,
                        UnityEngine.Vector3.zero, new HitDamage(0f, 8f, 20f), swing);
                    t.ReceiveHit(hit);
                }
            }
        }

        [Test]
        public void SameStage_HitsEachTargetOnce_AcrossMultipleActiveFrames()
        {
            var allocator = new HitInstanceAllocator();
            var tracker = new MultiHitTracker();
            var a = new FakeTarget(1);
            var b = new FakeTarget(2);

            HitId swing = allocator.NextSingle(); // 段開始で 1 回採番
            // 判定中に複数フレーム重なっても各対象 1 回。
            DeliverOverlap(tracker, swing, new[] { a, b });
            DeliverOverlap(tracker, swing, new[] { a, b });
            DeliverOverlap(tracker, swing, new[] { a });

            Assert.AreEqual(1, a.Received, "同一段・同一対象は 1 回。");
            Assert.AreEqual(1, b.Received, "別対象にもそれぞれ 1 回。");
        }

        [Test]
        public void NewStage_NewSwingToken_AllowsHitAgain()
        {
            var allocator = new HitInstanceAllocator();
            var tracker = new MultiHitTracker();
            var a = new FakeTarget(1);

            HitId swing1 = allocator.NextSingle();
            DeliverOverlap(tracker, swing1, new[] { a });
            Assert.AreEqual(1, a.Received);

            // 段送りで新 Swing Token（駆動側は StageJustStarted で採番し直す）。
            HitId swing2 = allocator.NextSingle();
            DeliverOverlap(tracker, swing2, new[] { a });
            Assert.AreEqual(2, a.Received, "段が変われば同一対象へ再命中。");
            Assert.AreNotEqual(swing1, swing2);
        }

        [Test]
        public void TrackerClear_OnComboEnd_ResetsHitHistory()
        {
            var tracker = new MultiHitTracker();
            var a = new FakeTarget(1);
            var swing = HitId.Single(5);

            Assert.IsTrue(tracker.TryRegisterHit(swing, a));
            tracker.Clear(); // コンボ終了/中断で履歴クリア
            Assert.IsTrue(tracker.TryRegisterHit(swing, a), "Clear 後は再登録できる。");
        }
    }
}
