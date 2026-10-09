namespace Momotaro.Gameplay.Progression
{
    /// <summary>
    /// 取得済み成長から作り直した効果の合計（P6B 01。仕様 §3）。<b>不変値</b>で、基礎値＋取得済み効果から毎回作る。
    /// 既存の値へ加算を繰り返さない。倍率は加算どうしを足し合わせる（0.10＋0.10＝1.20。1.10×1.10 にはしない）。
    /// </summary>
    public readonly struct GrowthEffects
    {
        public GrowthEffects(int maxHpBonus, float attackHpMultiplierBonus, int maxStaminaBonus,
            float normalPoiseMultiplierBonus, int kibidangoHealBonus, int kibidangoCapacityBonus)
        {
            MaxHpBonus = maxHpBonus < 0 ? 0 : maxHpBonus;
            AttackHpMultiplierBonus = Sanitize(attackHpMultiplierBonus);
            MaxStaminaBonus = maxStaminaBonus < 0 ? 0 : maxStaminaBonus;
            NormalPoiseMultiplierBonus = Sanitize(normalPoiseMultiplierBonus);
            KibidangoHealBonus = kibidangoHealBonus < 0 ? 0 : kibidangoHealBonus;
            KibidangoCapacityBonus = kibidangoCapacityBonus < 0 ? 0 : kibidangoCapacityBonus;
        }

        /// <summary>効果なし。</summary>
        public static GrowthEffects None => default;

        /// <summary>最大 HP への加算。</summary>
        public int MaxHpBonus { get; }

        /// <summary>刀の HP ダメージ倍率への加算の合計。</summary>
        public float AttackHpMultiplierBonus { get; }

        /// <summary>最大スタミナへの加算。</summary>
        public int MaxStaminaBonus { get; }

        /// <summary>通常攻撃の体幹倍率への加算の合計。</summary>
        public float NormalPoiseMultiplierBonus { get; }

        /// <summary>きびだんご回復量への加算。</summary>
        public int KibidangoHealBonus { get; }

        /// <summary>きびだんご最大数への加算。</summary>
        public int KibidangoCapacityBonus { get; }

        /// <summary>刀の HP ダメージ倍率（1＋加算の合計）。通常各段と必殺に 1 回だけ掛ける。</summary>
        public float AttackHpMultiplier => 1f + AttackHpMultiplierBonus;

        /// <summary>通常攻撃の体幹倍率（1＋加算の合計）。JG・必殺には掛けない。</summary>
        public float NormalPoiseMultiplier => 1f + NormalPoiseMultiplierBonus;

        /// <summary>1 ノード分を足した新しい合計を返す（自身は変えない）。</summary>
        public GrowthEffects Plus(int maxHp, float attack, int stamina, float poise, int heal, int capacity)
        {
            return new GrowthEffects(MaxHpBonus + maxHp, AttackHpMultiplierBonus + attack, MaxStaminaBonus + stamina,
                NormalPoiseMultiplierBonus + poise, KibidangoHealBonus + heal, KibidangoCapacityBonus + capacity);
        }

        /// <inheritdoc />
        public override string ToString() =>
            "HP+" + MaxHpBonus + " 刀x" + AttackHpMultiplier.ToString("0.00") + " スタミナ+" + MaxStaminaBonus
            + " 体幹x" + NormalPoiseMultiplier.ToString("0.00") + " 回復+" + KibidangoHealBonus
            + " 最大数+" + KibidangoCapacityBonus;

        private static float Sanitize(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
    }
}
