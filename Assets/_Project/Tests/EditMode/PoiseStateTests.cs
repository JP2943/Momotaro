using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-05：体幹・スタン状態（回復開始/速度、JG 遅延、体幹0でスタン、スタン終了で全回復＋軽減、スタン中 HP×1.25）を検証する。
    /// </summary>
    public sealed class PoiseStateTests
    {
        private static PoiseState Make() => new PoiseState(100f); // 回復3s/8%、JG4s、スタン3s、終了後3s 50%減

        [Test]
        public void ApplyPoiseDamage_ReducesAndReturnsActual()
        {
            var p = Make();
            float applied = p.ApplyPoiseDamage(15f);
            Assert.AreEqual(85f, p.Current, 1e-4f);
            Assert.AreEqual(15f, applied, 1e-4f);
        }

        [Test]
        public void PoiseZero_TriggersStun_WithHpMultiplier()
        {
            var p = Make();
            Assert.AreEqual(1f, p.StunHpMultiplier, "非スタン時は 1.0。");
            p.ApplyPoiseDamage(100f);
            Assert.AreEqual(0f, p.Current);
            Assert.IsTrue(p.IsStunned);
            Assert.AreEqual(1.25f, p.StunHpMultiplier, "スタン中は HP ×1.25。");
        }

        [Test]
        public void Stun_NoRecovery_ThenFullRecoverAndReductionOnEnd()
        {
            var p = Make();
            p.ApplyPoiseDamage(100f); // stun 3s
            p.Tick(1f);
            Assert.IsTrue(p.IsStunned, "スタン中。");
            Assert.AreEqual(0f, p.Current, "スタン中は回復しない。");

            p.Tick(2.5f); // 累計 3.5 > 3 → スタン終了
            Assert.IsFalse(p.IsStunned);
            Assert.AreEqual(100f, p.Current, 1e-4f, "スタン終了時に全回復。");
            Assert.IsTrue(p.InPostStunReduction, "終了後は軽減期間。");
        }

        [Test]
        public void PostStunReduction_HalvesPoiseDamage()
        {
            var p = Make();
            p.ApplyPoiseDamage(100f);
            p.Tick(3.1f); // スタン終了 → 軽減期間
            Assert.IsTrue(p.InPostStunReduction);

            float applied = p.ApplyPoiseDamage(20f); // 50%減 → 10
            Assert.AreEqual(10f, applied, 1e-4f, "軽減期間は体幹ダメージ 50%減。");
            Assert.AreEqual(90f, p.Current, 1e-4f);
        }

        [Test]
        public void Recovery_ExcessAfterDelay_RecoversInSameTick()
        {
            var p = Make();
            p.ApplyPoiseDamage(20f); // 80
            // 遅延3秒を消化した残り0.5秒が回復へ：8%/s × 100 × 0.5s = 4 → 84（受入修正の例）。
            p.Tick(3.5f);
            Assert.AreEqual(84f, p.Current, 0.1f, "遅延超過分が同じ Tick で回復へ使われる。");
        }

        [Test]
        public void JustGuard_DelaysRecoveryLonger()
        {
            var jg = Make();
            jg.ApplyPoiseDamage(20f, isJustGuard: true); // 80、JG 回復開始 4s
            jg.Tick(3.5f);
            Assert.AreEqual(80f, jg.Current, 1e-4f, "JG 遅延(4s)未満では回復しない。");

            // 通常遅延(3s)なら同じ 3.5s で余剰0.5s回復して 84。JG は回復が遅い。
            var normal = Make();
            normal.ApplyPoiseDamage(20f);
            normal.Tick(3.5f);
            Assert.Greater(normal.Current, jg.Current, "JG は通常より回復開始が遅い。");
        }
    }
}
