using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-05：体幹ダメージ計算（固定系統・状況補正は非乗算で高い方）と、ひるませ値（状況補正なし）を検証する。
    /// </summary>
    public sealed class PoiseDamageCalculatorTests
    {
        [Test]
        public void ThreeStagePoise_AreFixed_8_8_15()
        {
            // 攻撃力・防御の影響なし。段の体幹値がそのまま（状況補正・倍率 1.0）。
            Assert.AreEqual(8f, PoiseDamageCalculator.Compute(8f, 1f, 1f, 1f));
            Assert.AreEqual(8f, PoiseDamageCalculator.Compute(8f, 1f, 1f, 1f));
            Assert.AreEqual(15f, PoiseDamageCalculator.Compute(15f, 1f, 1f, 1f));
        }

        [Test]
        public void Situational_BackOrActive_Is1_5()
        {
            Assert.AreEqual(1.5f, PoiseDamageCalculator.SituationalMultiplier(true, false), "背後 ×1.5。");
            Assert.AreEqual(1.5f, PoiseDamageCalculator.SituationalMultiplier(false, true), "攻撃中 ×1.5。");
            Assert.AreEqual(1f, PoiseDamageCalculator.SituationalMultiplier(false, false), "どちらも無ければ 1.0。");
        }

        [Test]
        public void Situational_BackAndActive_DoesNotMultiply()
        {
            // 背後 ×1.5 と 攻撃中 ×1.5 が同時でも乗算せず、高い方 1.5 のみ（2.25 にならない）。
            Assert.AreEqual(1.5f, PoiseDamageCalculator.SituationalMultiplier(true, true), "重複なし（乗算しない）。");
        }

        [Test]
        public void Compute_AppliesSituationalAndTargetMultiplier()
        {
            // 背後で 8 × 1.5 = 12。
            float back = PoiseDamageCalculator.SituationalMultiplier(true, false);
            Assert.AreEqual(12f, PoiseDamageCalculator.Compute(8f, back, 1f, 1f));
            // 対象被体幹倍率 1.5 → 8 × 1.5 = 12。
            Assert.AreEqual(12f, PoiseDamageCalculator.Compute(8f, 1f, 1f, 1.5f));
        }

        [Test]
        public void Flinch_HasNoSituationalCorrection()
        {
            // ひるませは背後・攻撃中の補正を持たない（値のみ）。
            Assert.AreEqual(20f, FlinchValueCalculator.Compute(20f, 1f, 1f));
            Assert.AreEqual(25f, FlinchValueCalculator.Compute(25f, 1f, 1f));
            // 攻撃者/対象倍率のみ作用。
            Assert.AreEqual(26f, FlinchValueCalculator.Compute(20f, 1.3f, 1f), 1e-4f);
        }
    }
}
