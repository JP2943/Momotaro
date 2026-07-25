namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 攻撃 1 段内のフェーズ（Phase2 P2-03B）。予備→判定→後隙の順に進む。
    /// Hitbox が有効なのは <see cref="Active"/> のみ。
    /// </summary>
    public enum AttackPhase
    {
        /// <summary>非攻撃。</summary>
        None = 0,

        /// <summary>予備動作（踏み込み・判定前）。</summary>
        Startup = 1,

        /// <summary>判定（Hitbox 有効）。</summary>
        Active = 2,

        /// <summary>後隙。</summary>
        Recovery = 3,
    }
}
