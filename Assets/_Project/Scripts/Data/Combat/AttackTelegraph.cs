namespace Momotaro.Data.Combat
{
    /// <summary>攻撃の予兆種別（仕様書 6.7 / 6.8）。</summary>
    public enum AttackTelegraph
    {
        /// <summary>通常。</summary>
        Normal = 0,

        /// <summary>強攻撃。</summary>
        Heavy = 1,

        /// <summary>ガード不能。</summary>
        Unblockable = 2,

        /// <summary>範囲。</summary>
        AreaOfEffect = 3,

        /// <summary>投射。</summary>
        Projectile = 4,
    }
}
