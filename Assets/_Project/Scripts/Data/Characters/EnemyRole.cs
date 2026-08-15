namespace Momotaro.Data.Characters
{
    /// <summary>
    /// 敵の役割（Phase3 §3.1）。検証用の 3 種（近接・遠距離・強敵）を最小集合として定義する。
    /// 本編敵・ボスは Phase 3 対象外のため、拡張点として <see cref="Boss"/> のみ予約し実処理は作らない。
    /// </summary>
    public enum EnemyRole
    {
        /// <summary>近接（基本対応確認）。</summary>
        Melee = 0,

        /// <summary>遠距離（接近・弾対応）。</summary>
        Ranged = 1,

        /// <summary>強敵（複数予兆の識別）。</summary>
        Elite = 2,

        /// <summary>ボス（Phase 3 では拡張点のみ。実処理なし）。</summary>
        Boss = 3,
    }
}
