using Momotaro.Gameplay.Enemy.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-04：攻撃選択 <see cref="EnemyAttackSelector"/> の使用不可除外（距離/角度/Cooldown）・連続使用 50% 減・1 種例外・
    /// 同点 tie-break を検証する（§6.2）。純粋・再現可能。
    /// </summary>
    public sealed class EnemyAttackSelectorTests
    {
        private static AttackOption[] Two()
        {
            return new[]
            {
                new AttackOption(useRange: 3f, useAngle: 60f, baseScore: 10f),
                new AttackOption(useRange: 3f, useAngle: 60f, baseScore: 10f),
            };
        }

        [Test]
        public void ExcludesOutOfRangeAndAngleAndCooldown()
        {
            var opts = new[]
            {
                new AttackOption(2f, 60f, 10f),  // 距離外にする
                new AttackOption(5f, 30f, 10f),  // 角度外にする
                new AttackOption(5f, 60f, 10f),  // Cooldown 中にする
            };
            var cd = new[] { 0f, 0f, 1f };
            int idx = EnemyAttackSelector.Evaluate(distance: 4f, angleToTarget: 45f, opts, cd, lastUsedIndex: -1,
                tieBreak: null, out float[] scores);
            Assert.AreEqual(-1, idx, "全候補が使用不可なら選択なし。");
            Assert.AreEqual(float.NegativeInfinity, scores[0], "距離外は除外。");
            Assert.AreEqual(float.NegativeInfinity, scores[1], "角度外は除外。");
            Assert.AreEqual(float.NegativeInfinity, scores[2], "Cooldown 中は除外。");
        }

        [Test]
        public void ConsecutiveUse_HalvesScore_WhenMultipleUsable()
        {
            var opts = Two();
            var cd = new[] { 0f, 0f };
            int idx = EnemyAttackSelector.Evaluate(1f, 0f, opts, cd, lastUsedIndex: 0, tieBreak: null, out float[] scores);
            Assert.AreEqual(5f, scores[0], 1e-4f, "直前使用は 50% 減。");
            Assert.AreEqual(10f, scores[1], 1e-4f, "他候補は据え置き。");
            Assert.AreEqual(1, idx, "減点されていない候補が選ばれる。");
        }

        [Test]
        public void SingleUsable_NoConsecutivePenalty()
        {
            // 候補は 2 つだが、1 つは角度外で使用不可 → 使用可能は 1 種のみ → 連続減点しない（試作敵の例外）。
            var opts = new[]
            {
                new AttackOption(3f, 60f, 10f),
                new AttackOption(3f, 10f, 10f), // 角度外
            };
            var cd = new[] { 0f, 0f };
            int idx = EnemyAttackSelector.Evaluate(1f, 45f, opts, cd, lastUsedIndex: 0, tieBreak: null, out float[] scores);
            Assert.AreEqual(0, idx);
            Assert.AreEqual(10f, scores[0], 1e-4f, "使用可能が 1 種なら連続でも減点しない。");
        }

        [Test]
        public void Tie_UsesTieBreak()
        {
            var opts = Two();
            var cd = new[] { 0f, 0f };
            // 同点（両方 10）。tie-break が index 1（topの2番目）を返す。
            int idx = EnemyAttackSelector.Evaluate(1f, 0f, opts, cd, lastUsedIndex: -1, tieBreak: _ => 1, out _);
            Assert.AreEqual(1, idx, "同点は tie-break で決める。");
        }
    }
}
