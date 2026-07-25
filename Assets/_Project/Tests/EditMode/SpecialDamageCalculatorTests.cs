using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-10：必殺技の威力（<see cref="SpecialDamageCalculator"/>）を検証する。技倍率 7.0、防御一部無視、スタン中は固有 1.5 のみ
    /// （1.25 とは乗算しない）。攻撃力・防御・スタンの各条件で決定的な値を確認する。
    /// </summary>
    public sealed class SpecialDamageCalculatorTests
    {
        [Test]
        public void EffectiveDefense_HalvedByRatio()
        {
            Assert.AreEqual(50f, SpecialDamageCalculator.EffectiveDefense(100f, 0.5f), 1e-4f);
            Assert.AreEqual(100f, SpecialDamageCalculator.EffectiveDefense(100f, 0f), 1e-4f);
            Assert.AreEqual(0f, SpecialDamageCalculator.EffectiveDefense(100f, 1f), 1e-4f);
        }

        [Test]
        public void Hp_NoDefense_NoStun_SevenTimes()
        {
            // 100 × 7.0 × 0.1 = 70、防御0 → 70。
            Assert.AreEqual(70, SpecialDamageCalculator.ResolveHp(100f, 7.0f, 0f, 0f, false, 1.5f));
        }

        [Test]
        public void Hp_StunUsesSpecialMultiplier_NotMultipliedWith125()
        {
            // 70 × 1.5 = 105（固有）。1.25 と乗算していれば 70×1.875=131 になる → 105 であることを確認。
            int special = SpecialDamageCalculator.ResolveHp(100f, 7.0f, 0f, 0f, true, 1.5f);
            Assert.AreEqual(105, special, "スタン中は固有 1.5 のみ。");
            Assert.AreNotEqual(131, special, "1.25×1.5（=1.875）ではない（非乗算）。");
        }

        [Test]
        public void Hp_DefenseIgnore_RaisesDamage()
        {
            // 防御100・無視0 → 70×(100/200)=35。無視0.5 → 実効防御50 → 70×(100/150)=46.67 → 47。
            Assert.AreEqual(35, SpecialDamageCalculator.ResolveHp(100f, 7.0f, 100f, 0f, false, 1.5f));
            Assert.AreEqual(47, SpecialDamageCalculator.ResolveHp(100f, 7.0f, 100f, 0.5f, false, 1.5f));
        }
    }
}
