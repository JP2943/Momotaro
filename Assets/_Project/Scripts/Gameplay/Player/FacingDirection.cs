namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// 表示上の 4 方向（Phase1 P1-04）。斜め移動でもこの 4 値のいずれかへ一意に決定する。
    /// </summary>
    public enum FacingDirection
    {
        /// <summary>手前（画面下）。既定。</summary>
        Down = 0,

        /// <summary>奥（画面上）。</summary>
        Up = 1,

        /// <summary>左。</summary>
        Left = 2,

        /// <summary>右。</summary>
        Right = 3,
    }
}
