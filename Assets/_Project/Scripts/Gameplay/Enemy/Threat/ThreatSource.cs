namespace Momotaro.Gameplay.Enemy.Threat
{
    /// <summary>
    /// ヘイト加算の由来種別（Phase3 P3-06。§7.1 加算表）。行動の「重み」は <see cref="ThreatSettings"/> が持ち、対象側の
    /// 獲得倍率（<see cref="IThreatTarget.AcquiredThreatMultiplier"/>）が別途乗じられる。将来（Phase 4）の犬の挑発・支援術・
    /// 弱体術は語彙として先に定義するだけで、実処理・実発行はこの Phase では作らない（依頼「未使用機能の実処理は作らない」）。
    /// </summary>
    public enum ThreatSource
    {
        /// <summary>HP ダメージ 1 につき +1（§7.1）。加算時に実ダメージ量を乗じる。</summary>
        HpDamage = 0,

        /// <summary>体幹ダメージ 1 につき +0.5（§7.1）。加算時に実体幹ダメージ量を乗じる。</summary>
        PoiseDamage = 1,

        /// <summary>ひるみ発生で +20（§7.1）。1 回のひるみ成立につき固定加算。</summary>
        Flinch = 2,

        /// <summary>ジャストガード成立で +30（§7.1）。攻撃者（＝敵）から見て JG した対象へ加算。</summary>
        JustGuard = 3,

        /// <summary>将来：犬の挑発 +100（§7.1。Phase 4 拡張点。本 Phase では発行しない）。</summary>
        DogTaunt = 4,

        /// <summary>将来：回復・強化術 +20（§7.1。Phase 4 拡張点。本 Phase では発行しない）。</summary>
        SupportSkill = 5,

        /// <summary>将来：敵弱体術 +40（§7.1。Phase 4 拡張点。本 Phase では発行しない）。</summary>
        DebuffSkill = 6,
    }
}
