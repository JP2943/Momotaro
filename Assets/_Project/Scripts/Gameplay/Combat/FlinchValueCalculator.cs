namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// ひるませ値の純粋計算（Phase2 P2-05。仕様書 §3.10 / §3.12）。ひるませ値 = 攻撃値 × 攻撃者専用補正 ×
    /// 対象の被蓄積倍率。背後・攻撃中などの状況補正は掛けない（HP・体幹と異なる点）。攻撃力・防御の影響も受けない。
    /// </summary>
    public static class FlinchValueCalculator
    {
        /// <summary>ひるませ値を求める（状況補正なし）。</summary>
        public static float Compute(float baseFlinch, float attackerMultiplier, float targetFlinchMultiplier)
        {
            return baseFlinch * attackerMultiplier * targetFlinchMultiplier;
        }
    }
}
