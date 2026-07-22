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
        public void Recovery_StartsAfterDelay_At8PercentPerSecond()
        {
            var p = Make();
            p.ApplyPoiseDamage(20f); // 80
            p.Tick(3.5f); // 遅延超過（この Tick では回復しない）
            p.Tick(1.0f); // 回復開始：8%/s × 100 × 1s = 8
            Assert.AreEqual(88f, p.Current, 0.5f, "遅延後 毎秒 8 回復。");
        }

        [Test]
        public void JustGuard_DelaysRecoveryLonger()
        {
            var p = Make();
            p.ApplyPoiseDamage(20f, isJustGuard: true); // 80、回復開始 4s
            p.Tick(3.5f);
            p.Tick(1.0f); // 累計 4.5 だが JG 遅延で未回復
            Assert.AreEqual(80f, p.Current, 1e-4f, "JG 後は回復開始が遅い。");

            p.Tick(1.0f); // ここで回復開始
            Assert.Greater(p.Current, 80f);
        }
    }
}
