namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾側から見た命中方向の分類（P2-01）。背後判定は仕様書 3.1
    /// 「正面から 135 度以上離れた方向からの命中を背後攻撃とする」に対応する。
    /// 前方／側面の境界は確定仕様ではないため、判定関数の引数で外部化する（試作値は定数化）。
    /// </summary>
    public enum HitBearing
    {
        /// <summary>正面寄り。</summary>
        Front = 0,

        /// <summary>側面。</summary>
        Side = 1,

        /// <summary>背後（背後攻撃の対象）。</summary>
        Back = 2,
    }
}
