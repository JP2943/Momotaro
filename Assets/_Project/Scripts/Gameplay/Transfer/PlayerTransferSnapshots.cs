namespace Momotaro.Gameplay.Transfer
{
    /// <summary>生命値 1 本分（現在値のみ）。最大値は Data から構築済みのものを使う（§4.5）。</summary>
    public readonly struct VitalTransferSnapshot
    {
        public int Current { get; }

        public VitalTransferSnapshot(int current)
        {
            Current = current;
        }
    }

    /// <summary>
    /// スタミナ（§4.5）。<b>Break 中は通常遷移を受付拒否する</b>ので、採取時も <see cref="BreakRemaining"/> 0 を要求する。
    /// 受付条件の正本は §6.1 で、ここはその帰結（裁定 6）。制限を緩めない。
    /// </summary>
    public readonly struct StaminaTransferSnapshot
    {
        public float Current { get; }
        public float RegenDelayRemaining { get; }
        public float BreakRemaining { get; }

        public StaminaTransferSnapshot(float current, float regenDelayRemaining, float breakRemaining)
        {
            Current = current;
            RegenDelayRemaining = regenDelayRemaining;
            BreakRemaining = breakRemaining;
        }
    }

    /// <summary>
    /// 被弾リアクション（§4.5）。持ち越すのは<b>被弾後無敵の残り</b>。
    /// Hurt 硬直は §6.1 が遷移を受付拒否するため採取時 0 だが、値域検証のために欄は持つ。
    /// <b>回避行動に付随する無敵とは別物</b>で、そちらは持ち越さない（§4.5 末尾）。
    /// </summary>
    public readonly struct HitReactionTransferSnapshot
    {
        public float HurtRemaining { get; }
        public float InvincibleRemaining { get; }

        public HitReactionTransferSnapshot(float hurtRemaining, float invincibleRemaining)
        {
            HurtRemaining = hurtRemaining;
            InvincibleRemaining = invincibleRemaining;
        }
    }

    /// <summary>主人公の生存値（HP とスタミナ）を 1 組にまとめた合成 Snapshot。</summary>
    public readonly struct PlayerVitalsTransferSnapshot
    {
        public VitalTransferSnapshot Health { get; }
        public StaminaTransferSnapshot Stamina { get; }

        public PlayerVitalsTransferSnapshot(VitalTransferSnapshot health, StaminaTransferSnapshot stamina)
        {
            Health = health;
            Stamina = stamina;
        }
    }
}
