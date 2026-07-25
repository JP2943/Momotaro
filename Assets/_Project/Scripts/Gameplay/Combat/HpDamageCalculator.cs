namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// HP ダメージの純粋計算（Phase2 P2-04。仕様書 §6.1 / Table 11）。乱数を使わず、同条件は常に同値。
    /// 計算順：基礎（攻撃力 × 技倍率 × 0.1）→ 攻撃者補正 → 背後補正 → 防御補正 → 被ダメ増減 → スタン倍率
    /// →（犬の肩代わりは Phase 4）→ 外部モード補正 → 四捨五入。防御補正は下限 0.1（防御適用前の 10%）。
    ///
    /// 攻撃側の寄与（手順 1-3）と対象側の解決（手順 4-9）を分離し、Hitbox 生成時は攻撃側、被弾側の
    /// <see cref="IDamageable"/> 実装で対象側を適用できるようにする。UnityEngine に依存しない。
    /// </summary>
    public static class HpDamageCalculator
    {
        /// <summary>技倍率へ掛ける基礎スケール（Table 11：×0.1）。</summary>
        public const float BaseScale = 0.1f;

        /// <summary>背後攻撃の HP 倍率（Table 9：×1.1）。</summary>
        public const float BackMultiplier = 1.1f;

        /// <summary>防御補正の下限（防御適用前の 10%）。</summary>
        public const float MinDefenseCorrection = 0.1f;

        /// <summary>
        /// 攻撃側の寄与（防御適用前）：基礎 × 攻撃者補正 × 背後補正（§6.1 手順 1-3）。
        /// </summary>
        public static float AttackContribution(float attackPower, float hpMultiplier, float attackerMultiplier = 1f, float backMultiplier = 1f)
        {
            return attackPower * hpMultiplier * BaseScale * attackerMultiplier * backMultiplier;
        }

        /// <summary>防御補正 = max(0.1, 100 ÷ (100 + 防御力))（Table 11）。</summary>
        public static float DefenseCorrection(float defense)
        {
            float correction = 100f / (100f + defense);
            return correction < MinDefenseCorrection ? MinDefenseCorrection : correction;
        }

        /// <summary>
        /// 対象側の解決（§6.1 手順 4-9）：攻撃寄与 × 防御補正 × 被ダメ増減 × スタン倍率 × 外部補正 を
        /// 四捨五入して整数化する。負値は 0 に丸める。計算途中は float、最終だけ整数化。
        /// </summary>
        public static int ResolveFinal(float attackContribution, float defense, float targetDamageMultiplier = 1f, float stunMultiplier = 1f, float externalMultiplier = 1f)
        {
            float damage = attackContribution
                           * DefenseCorrection(defense)
                           * targetDamageMultiplier
                           * stunMultiplier
                           * externalMultiplier;

            if (damage <= 0f)
            {
                return 0;
            }

            // 四捨五入（非負のため +0.5 の切り捨てで round-half-up）。
            return (int)(damage + 0.5f);
        }

        /// <summary>
        /// フル計算（受入検証・単体利用向け）：攻撃側寄与と対象側解決を合成して最終 HP ダメージを返す。
        /// </summary>
        public static int Compute(
            float attackPower,
            float hpMultiplier,
            float defense,
            float attackerMultiplier = 1f,
            float backMultiplier = 1f,
            float targetDamageMultiplier = 1f,
            float stunMultiplier = 1f,
            float externalMultiplier = 1f)
        {
            float contribution = AttackContribution(attackPower, hpMultiplier, attackerMultiplier, backMultiplier);
            return ResolveFinal(contribution, defense, targetDamageMultiplier, stunMultiplier, externalMultiplier);
        }
    }
}
