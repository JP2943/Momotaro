namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 体幹（ポイズ）ダメージの純粋計算（Phase2 P2-05。仕様書 §3.11 / Table 9）。体幹は固定系統で、攻撃力・防御力の
    /// 影響を受けない。状況補正（背後・敵の予備/判定中）は乗算せず高い方だけを適用する（Table 9「重複なし」）。
    /// ひるませ値には状況補正を掛けない（<see cref="FlinchValueCalculator"/>）。
    /// </summary>
    public static class PoiseDamageCalculator
    {
        /// <summary>背後攻撃の体幹倍率（Table 9：×1.5）。</summary>
        public const float BackMultiplier = 1.5f;

        /// <summary>対象の予備動作/判定中の体幹倍率（Table 9：×1.5）。</summary>
        public const float TargetActiveMultiplier = 1.5f;

        /// <summary>
        /// 状況補正倍率。背後・攻撃中はいずれも成立時に倍率へ加わるが、乗算せず「高い方」だけを採用する。
        /// どちらも成立しなければ 1.0。
        /// </summary>
        public static float SituationalMultiplier(bool isBackHit, bool targetActing)
        {
            float m = 1f;
            if (isBackHit && BackMultiplier > m)
            {
                m = BackMultiplier;
            }

            if (targetActing && TargetActiveMultiplier > m)
            {
                m = TargetActiveMultiplier;
            }

            return m;
        }

        /// <summary>
        /// 体幹ダメージ = 技の体幹値 × 状況補正 × 攻撃者補正 × 対象の体幹ダメージ倍率（攻撃力・防御力の影響なし）。
        /// </summary>
        public static float Compute(float basePoise, float situationalMultiplier, float attackerMultiplier, float targetPoiseMultiplier)
        {
            return basePoise * situationalMultiplier * attackerMultiplier * targetPoiseMultiplier;
        }
    }
}
