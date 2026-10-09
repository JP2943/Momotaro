namespace Momotaro.Gameplay.Player
{
    /// <summary>きびだんご使用の開始を断った理由（P6B 03。診断・テスト・HUD 用）。</summary>
    public enum ItemUseRejection
    {
        /// <summary>断っていない（開始した）。</summary>
        None = 0,

        /// <summary>この campaign に使用動作が無い・配線が無い。</summary>
        NotAvailable = 1,

        /// <summary>行動中（攻撃・タメ・ガード・回避・被弾・ブレイク・死亡・遷移・メニュー・同フレームの既存行動）。</summary>
        Busy = 2,

        /// <summary>HP が最大。</summary>
        HpFull = 3,

        /// <summary>残数 0。</summary>
        OutOfStock = 4,
    }
}
