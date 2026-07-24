namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 必殺技の威力計算（Phase2 P2-10。仕様書 §3.6 / §6）。純粋・決定的。基礎威力は通常攻撃 1 段目の 700%（技倍率 7.0）で、
    /// 防御力を一部無視する（<paramref name="defenseIgnoreRatio"/> ぶん防御を減じる）。スタン中の対象へは必殺技固有の倍率
    /// （既定 1.5）だけを適用し、通常のスタン倍率 1.25 とは乗算しない（1.25 を用いず 1.5 で置き換える）。ひるませ値は攻撃データの固定値。
    /// </summary>
    public static class SpecialDamageCalculator
    {
        /// <summary>防御一部無視を反映した実効防御 = 防御 ×(1 - ignoreRatio)。ratio は 0..1 にクランプ。</summary>
        public static float EffectiveDefense(float defense, float defenseIgnoreRatio)
        {
            float r = defenseIgnoreRatio < 0f ? 0f : (defenseIgnoreRatio > 1f ? 1f : defenseIgnoreRatio);
            return defense * (1f - r);
        }

        /// <summary>
        /// 必殺技の最終 HP ダメージ。基礎（攻撃力 × 7.0 × 0.1）へ、防御一部無視後の防御補正と、スタン時の固有倍率
        /// （<paramref name="specialStunMultiplier"/>）のみを適用する。非スタン時は倍率 1.0。1.25 とは乗算しない。
        /// </summary>
        public static int ResolveHp(
            float attackPower,
            float hpMultiplier,
            float defense,
            float defenseIgnoreRatio,
            bool targetStunned,
            float specialStunMultiplier)
        {
            float contribution = HpDamageCalculator.AttackContribution(attackPower, hpMultiplier);
            float effectiveDefense = EffectiveDefense(defense, defenseIgnoreRatio);
            float stunMultiplier = targetStunned ? specialStunMultiplier : 1f;
            return HpDamageCalculator.ResolveFinal(contribution, effectiveDefense, stunMultiplier: stunMultiplier);
        }
    }
}
