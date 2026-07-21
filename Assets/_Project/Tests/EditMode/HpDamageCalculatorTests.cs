using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-04：HP ダメージ計算（仕様書 §6.1 / Table 11・12・9）の検証。基準ダメージ 8/9/13、最低 10%（防御補正下限）、
    /// 背後 ×1.1、四捨五入、乱数なし（同条件同値）、計算順（乗算合成＋防御下限）を確認する。
    /// </summary>
    public sealed class HpDamageCalculatorTests
    {
        // 主人公：攻撃力 100 / 対象防御 20（Table 12）。
        private const float Power = 100f;
        private const float Defense = 20f;

        [Test]
        public void BaseDamage_ThreeStages_Are_8_9_13()
        {
            Assert.AreEqual(8, HpDamageCalculator.Compute(Power, 1.0f, Defense), "1 段目 ≒ 8。");
            Assert.AreEqual(9, HpDamageCalculator.Compute(Power, 1.1f, Defense), "2 段目 ≒ 9。");
            Assert.AreEqual(13, HpDamageCalculator.Compute(Power, 1.5f, Defense), "3 段目 ≒ 13（12.5 は四捨五入で 13）。");
        }

        [Test]
        public void BackAttack_MultipliesHpBy1_1()
        {
            int front = HpDamageCalculator.Compute(Power, 1.0f, Defense);
            int back = HpDamageCalculator.Compute(Power, 1.0f, Defense, backMultiplier: HpDamageCalculator.BackMultiplier);
            Assert.AreEqual(8, front);
            Assert.AreEqual(9, back, "背後は ×1.1（10×1.1×0.833 ≒ 9.17 → 9）。");
            Assert.Greater(back, front);
        }

        [Test]
        public void DefenseCorrection_HasFloorOfTenPercent()
        {
            // 極端な防御でも下限 0.1（防御適用前の 10%）。
            Assert.AreEqual(HpDamageCalculator.MinDefenseCorrection, HpDamageCalculator.DefenseCorrection(1_000_000f), 1e-6f);
            // 基礎 10 × 0.1 = 1。0 にはならない。
            Assert.AreEqual(1, HpDamageCalculator.Compute(Power, 1.0f, 1_000_000f), "最低でも防御前の 10%。");
        }

        [Test]
        public void NoRandomness_SameInputsSameResult()
        {
            int a = HpDamageCalculator.Compute(Power, 1.5f, Defense);
            int b = HpDamageCalculator.Compute(Power, 1.5f, Defense);
            Assert.AreEqual(a, b);
            Assert.AreEqual(13, a);
        }

        [Test]
        public void Multipliers_ComposeMultiplicatively()
        {
            // 攻撃者補正 ×2、スタン ×1.25 が乗算合成される。
            int expected = HpDamageCalculator.Compute(Power, 1.0f, Defense, attackerMultiplier: 2f, stunMultiplier: 1.25f);
            // 10 × 2 × 0.8333 × 1.25 = 20.83 → 21
            Assert.AreEqual(21, expected);
        }

        [Test]
        public void SplitContributionAndResolve_MatchesFullCompute()
        {
            float contribution = HpDamageCalculator.AttackContribution(Power, 1.5f);
            int split = HpDamageCalculator.ResolveFinal(contribution, Defense);
            Assert.AreEqual(HpDamageCalculator.Compute(Power, 1.5f, Defense), split, "攻撃側寄与＋対象側解決＝フル計算。");
        }

        [Test]
        public void ZeroAttackPower_YieldsZero()
        {
            Assert.AreEqual(0, HpDamageCalculator.Compute(0f, 1.5f, Defense));
        }

        [Test]
        public void RoundHalfUp_At12_5_Gives13()
        {
            // 3 段目 15 × 0.8333… = 12.5 ちょうど → 13（四捨五入）。
            Assert.AreEqual(13, HpDamageCalculator.ResolveFinal(15f, Defense));
        }
    }
}
