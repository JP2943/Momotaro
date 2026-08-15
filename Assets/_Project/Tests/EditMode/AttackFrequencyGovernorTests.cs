using Momotaro.Gameplay.Enemy.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-09：ガード不能の頻度上限（§9.3「全選択の20%以下」）を <see cref="AttackFrequencyGovernor"/> の選択履歴管理で検証する。
    /// 上限側：多数回の選択でガード不能割合が上限（20%）を超えない（唯一の候補でも解禁前は不許可のまま）。下限側：解禁され
    /// たびに選ぶ運用で 0%（全く使われない）にならない。Score 乗算では保証できない「割合そのものの固定」を担保する。
    /// </summary>
    public sealed class AttackFrequencyGovernorTests
    {
        [Test]
        public void MinGap_DerivedFromRatio()
        {
            Assert.AreEqual(4, new AttackFrequencyGovernor(0.2f).MinGap, "20% → 5 回に 1 回（min-gap=4）。");
            Assert.AreEqual(9, new AttackFrequencyGovernor(0.1f).MinGap, "10% → 10 回に 1 回。");
        }

        [Test]
        public void CappedShare_NeverExceedsRatio_AtEverySelection()
        {
            var gov = new AttackFrequencyGovernor(0.2f);

            // 「解禁されたら必ずガード不能を選ぶ（かつ常に使用可能）」最悪ケースでも、序盤を含む各選択後で割合が上限を超えない。
            for (int i = 0; i < 1000; i++)
            {
                bool pickUnblockable = gov.CappedEligible; // 解禁され次第ガード不能を選ぶ。
                gov.RecordSelection(pickUnblockable);

                float ratio = (float)gov.CappedSelections / gov.TotalSelections;
                Assert.LessOrEqual(ratio, 0.2f + 1e-4f,
                    "各選択後にガード不能割合が 20% を超えない（選択 " + gov.TotalSelections + " 回時点）。");
            }

            Assert.Greater(gov.CappedSelections, 0, "十分な回数の後は 0%（全く使われない）にならない。");
        }

        [Test]
        public void FirstFewSelections_AreNotUnblockable()
        {
            var gov = new AttackFrequencyGovernor(0.2f);
            // 最初の min-gap 回は未解禁 → ガード不能にならない（序盤で 100%/50%/33%/25% に跳ねない）。
            for (int i = 0; i < gov.MinGap; i++)
            {
                Assert.IsFalse(gov.CappedEligible, "序盤 " + i + " 回目は未解禁。");
                gov.RecordSelection(false);
            }

            Assert.IsTrue(gov.CappedEligible, "他攻撃を min-gap 回挟んだ後に解禁。");
        }

        [Test]
        public void NotEligible_UntilMinGapOfOtherSelections()
        {
            var gov = new AttackFrequencyGovernor(0.2f);
            gov.RecordSelection(true);                 // ガード不能を 1 回選択 → 間隔リセット。
            Assert.IsFalse(gov.CappedEligible, "直後は未解禁。");

            for (int i = 0; i < 3; i++)
            {
                gov.RecordSelection(false);
                Assert.IsFalse(gov.CappedEligible, "他攻撃 " + (i + 1) + " 回では未解禁（min-gap=4）。");
            }

            gov.RecordSelection(false); // 4 回目の他攻撃。
            Assert.IsTrue(gov.CappedEligible, "他攻撃 4 回で解禁。");
        }

        [Test]
        public void NotEligible_AtStart()
        {
            var gov = new AttackFrequencyGovernor(0.2f);
            Assert.IsFalse(gov.CappedEligible, "開始直後は未解禁（最初の攻撃をガード不能にしない）。");
        }
    }
}
