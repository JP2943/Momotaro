namespace Momotaro.Core.World
{
    /// <summary>
    /// XZ 平面の 4 方向（P5-02。仕様書 v1.1 §3.1「各 Entry は到着位置・4 方向の向きを持つ」）。
    ///
    /// Data 層が Entry の向きを持つため、Gameplay の <c>FacingDirection</c> は使えない
    /// （層は Core ← Data ← Gameplay で、Data から Gameplay を参照できない）。
    /// 4 方向に限るのは、P5 のカメラが固定俯角・4 方向表示だから（§11）。
    /// </summary>
    public enum CardinalDirection
    {
        /// <summary>+Z。</summary>
        North = 0,

        /// <summary>+X。</summary>
        East = 1,

        /// <summary>-Z。</summary>
        South = 2,

        /// <summary>-X。</summary>
        West = 3,
    }
}
