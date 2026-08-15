using Momotaro.Gameplay.Enemy.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-09：ガード不能の頻度抑制（§9.3「全選択の20%以下」）を <see cref="EnemyAttackSelector"/> の頻度スケールで検証する。
    /// スケール&lt;1 の攻撃は Score が下がり、他候補があるとき相対的に選ばれにくくなる。連続使用抑制（同一攻撃 50% 減）も維持する。
    /// </summary>
    public sealed class AttackFrequencyScaleTests
    {
        [Test]
        public void FrequencyScale_ReducesScore_DeprioritizesUnblockable()
        {
            var options = new[]
            {
                new AttackOption(3f, 90f, 10f, frequencyScale: 1f),    // 0: 通常
                new AttackOption(3f, 90f, 10f, frequencyScale: 0.35f), // 1: ガード不能（抑制）
            };
            var cd = new[] { 0f, 0f };

            int idx = EnemyAttackSelector.Evaluate(1f, 0f, options, cd, lastUsedIndex: -1, tieBreak: null, out float[] scores);

            Assert.AreEqual(0, idx, "他候補があればガード不能でなく通常を選ぶ。");
            Assert.AreEqual(10f, scores[0], 1e-4f);
            Assert.AreEqual(3.5f, scores[1], 1e-4f, "頻度スケール 0.35 で Score が下がる。");
        }

        [Test]
        public void FrequencyScale_StillSelectable_WhenOnlyCandidate()
        {
            var options = new[]
            {
                new AttackOption(3f, 90f, 10f, frequencyScale: 0.35f), // ガード不能のみが射程内
                new AttackOption(1f, 90f, 10f, frequencyScale: 1f),    // 射程外
            };
            var cd = new[] { 0f, 0f };

            int idx = EnemyAttackSelector.Evaluate(2f, 0f, options, cd, lastUsedIndex: -1, tieBreak: null, out _);
            Assert.AreEqual(0, idx, "唯一の候補なら抑制中でも選ばれる。");
        }
    }
}
