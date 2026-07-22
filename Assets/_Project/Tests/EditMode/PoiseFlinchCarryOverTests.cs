using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-05 受入修正：<see cref="PoiseState"/> / <see cref="FlinchState"/> の Tick で、状態境界を跨いだ余剰 deltaTime を
    /// 捨てずに同じ Tick 内の後続処理へ流すこと（大きな Tick ≒ 小分け Tick、遅延超過→同 Tick 回復、スタン終了余剰→軽減、
    /// ひるみ終了余剰→免疫）を検証する。浮動小数の許容誤差で比較する。
    /// </summary>
    public sealed class PoiseFlinchCarryOverTests
    {
        // === 体幹（Poise） ===

        [Test]
        public void Poise_LargeTick_ApproximatesSmallTicks()
        {
            var big = new PoiseState(100f);
            big.ApplyPoiseDamage(20f); // 80
            big.Tick(5.0f);            // 遅延3 → 余剰2秒回復 = 0.08×100×2 = 16 → 96

            var small = new PoiseState(100f);
            small.ApplyPoiseDamage(20f);
            for (int i = 0; i < 50; i++)
            {
                small.Tick(0.1f); // 合計 5.0 秒
            }

            Assert.AreEqual(big.Current, small.Current, 0.5f, "大きい Tick と小分け Tick が概ね一致する。");
            Assert.AreEqual(96f, big.Current, 0.5f, "遅延超過分が同 Tick で回復（80 → 96）。");
        }

        [Test]
        public void Poise_DelayExcess_RecoversInSameTick()
        {
            var p = new PoiseState(100f);
            p.ApplyPoiseDamage(20f); // 80、遅延3秒
            p.Tick(3.5f);            // 余剰0.5秒回復 = 0.08×100×0.5 = 4 → 84（仕様書の例）
            Assert.AreEqual(84f, p.Current, 0.1f);
        }

        [Test]
        public void Poise_StunEndExcess_ConsumesPostStunReduction()
        {
            var p = new PoiseState(100f);
            p.ApplyPoiseDamage(100f); // スタン 3秒
            Assert.IsTrue(p.IsStunned);

            p.Tick(3.5f); // スタン終了（余剰0.5秒）→ 全回復＋軽減3秒から 0.5 消化 → 残り2.5
            Assert.IsFalse(p.IsStunned);
            Assert.AreEqual(100f, p.Current, 1e-4f, "スタン終了で全回復。");
            Assert.IsTrue(p.InPostStunReduction, "軽減期間中。");

            // 残り約2.5秒。2.6秒進めれば軽減期間は終わる（余剰が捨てられていれば 3.0 残で終わらない）。
            p.Tick(2.6f);
            Assert.IsFalse(p.InPostStunReduction, "スタン終了時の余剰が軽減期間の消化に使われている。");
        }

        // === ひるみ（Flinch） ===

        [Test]
        public void Flinch_FlinchEndExcess_ConsumesImmunity()
        {
            var f = new FlinchState(60f); // ひるみ0.5秒、免疫0.5秒
            f.AddFlinch(60f);             // 耐性到達 → ひるみ発生
            Assert.IsTrue(f.IsFlinching);

            f.Tick(0.6f); // ひるみ終了（余剰0.1秒）→ 免疫0.5から 0.1 消化 → 残り0.4
            Assert.IsFalse(f.IsFlinching);
            Assert.IsTrue(f.InImmunity, "ひるみ終了後は免疫中。");

            // 残り約0.4秒。0.45秒進めれば免疫は終わる（余剰が捨てられていれば 0.5 残で終わらない）。
            f.Tick(0.45f);
            Assert.IsFalse(f.InImmunity, "ひるみ終了時の余剰が免疫の消化に使われている。");
        }

        [Test]
        public void Flinch_LargeTick_ApproximatesSmallTicks()
        {
            var big = new FlinchState(60f);
            big.AddFlinch(60f);
            big.Tick(0.8f); // ひるみ0.5 + 免疫0.3消化 → 免疫残0.2

            var small = new FlinchState(60f);
            small.AddFlinch(60f);
            for (int i = 0; i < 8; i++)
            {
                small.Tick(0.1f); // 合計 0.8 秒
            }

            Assert.AreEqual(big.IsFlinching, small.IsFlinching, "ひるみ状態が一致。");
            Assert.AreEqual(big.InImmunity, small.InImmunity, "免疫状態が一致。");
            Assert.IsFalse(big.IsFlinching);
            Assert.IsTrue(big.InImmunity, "0.8秒時点では免疫中。");
        }
    }
}
