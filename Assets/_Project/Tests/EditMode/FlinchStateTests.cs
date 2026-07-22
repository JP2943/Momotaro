using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-05：ひるみ蓄積状態（蓄積・耐性でひるみ・1.5s 保持で一括0・再蓄積で更新・ひるみ0.5s・免疫0.5s）を検証する。
    /// </summary>
    public sealed class FlinchStateTests
    {
        private static FlinchState Make() => new FlinchState(60f); // 耐性60、保持1.5s、ひるみ0.5s、免疫0.5s

        [Test]
        public void Accumulates_WithoutFlinch_BelowResistance()
        {
            var f = Make();
            float added = f.AddFlinch(20f);
            Assert.AreEqual(20f, added);
            Assert.AreEqual(20f, f.Accumulation, 1e-4f);
            Assert.IsFalse(f.IsFlinching);
        }

        [Test]
        public void ReachingResistance_TriggersFlinch_AndResetsAccumulation()
        {
            var f = Make();
            f.AddFlinch(20f);
            f.AddFlinch(20f);
            f.AddFlinch(20f); // 60 >= 60
            Assert.IsTrue(f.IsFlinching);
            Assert.AreEqual(0f, f.Accumulation, "発生時に蓄積 0。");
        }

        [Test]
        public void DuringFlinch_NoAccumulation()
        {
            var f = Make();
            f.AddFlinch(60f); // flinch
            Assert.IsTrue(f.IsFlinching);
            float added = f.AddFlinch(50f);
            Assert.AreEqual(0f, added, "ひるみ中は蓄積しない。");
            Assert.AreEqual(0f, f.Accumulation);
        }

        [Test]
        public void HoldExpires_ResetsAccumulationAtOnce()
        {
            var f = Make();
            f.AddFlinch(30f);
            f.Tick(1.6f); // 1.5s 経過
            Assert.AreEqual(0f, f.Accumulation, "保持切れで一括 0。");
        }

        [Test]
        public void ReHit_RefreshesHoldTimer()
        {
            var f = Make();
            f.AddFlinch(20f);
            f.Tick(1.0f);
            f.AddFlinch(20f); // 再蓄積 → 保持タイマー更新（accum 40）
            f.Tick(1.0f);
            Assert.AreEqual(40f, f.Accumulation, 1e-4f, "再蓄積で保持が更新され消えない。");
        }

        [Test]
        public void FlinchEnds_ThenImmunityBlocksNewFlinch()
        {
            var f = Make();
            f.AddFlinch(60f); // flinch
            f.Tick(0.5f);     // ひるみ終了 → 免疫
            Assert.IsFalse(f.IsFlinching);
            Assert.IsTrue(f.InImmunity);

            f.AddFlinch(60f); // 免疫中は蓄積しても発生しない
            Assert.IsFalse(f.IsFlinching, "免疫中は新たなひるみを無効化。");

            f.Tick(0.5f);     // 免疫終了
            Assert.IsFalse(f.InImmunity);
            f.AddFlinch(1f);  // 蓄積は 60 超なので発生
            Assert.IsTrue(f.IsFlinching);
        }
    }
}
