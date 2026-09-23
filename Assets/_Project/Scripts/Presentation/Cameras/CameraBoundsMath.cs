using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// 斜め見下ろしカメラの<b>床面での見える範囲</b>と、領域に収める基準位置を求める（P5-06。仕様書 v1.1 §11）。
    ///
    /// <b>Z 幅を orthographicSize と同じにしない。</b> これが §11 の要点。
    /// 真上から見ているなら画面の縦は床の奥行きとそのまま対応するが、俯角が付くと
    /// 画面の縦 1 に対して床は <c>1 / sin(俯角)</c> だけ伸びる。45 度なら約 1.41 倍、
    /// 30 度なら 2 倍。ここを取り違えると、部屋の端で床の外（仮背景）が見える。
    ///
    /// <b>横は素直に <c>orthographicSize × aspect</c>。</b> カメラの横軸は床と平行なので伸びない。
    /// だから 16:9・4:3・21:9 で変わるのは横だけで、縦の伸びは俯角だけで決まる。
    /// </summary>
    public static class CameraBoundsMath
    {
        /// <summary>俯角の下限（度）。これより浅いと床面との交点が発散する。</summary>
        public const float MinPitchDegrees = 5f;

        /// <summary>
        /// 床面で見える範囲の<b>半分の大きさ</b>を返す（x = 横, y = 奥行）。
        /// </summary>
        /// <param name="orthographicSize">Orthographic の縦半分。</param>
        /// <param name="aspect">横 / 縦。</param>
        /// <param name="pitchDegrees">俯角（真上が 90 度）。</param>
        public static Vector2 HalfFootprint(float orthographicSize, float aspect, float pitchDegrees)
        {
            float size = Mathf.Max(0f, orthographicSize);
            float safeAspect = aspect > 0f ? aspect : 1f;
            float pitch = Mathf.Max(MinPitchDegrees, Mathf.Abs(pitchDegrees));
            float sin = Mathf.Sin(pitch * Mathf.Deg2Rad);

            return new Vector2(size * safeAspect, size / sin);
        }

        /// <summary>
        /// 見たい位置を、領域からはみ出さない基準位置へ直す（§11）。
        ///
        /// <b>部屋が見える範囲より小さい軸は中央へ固定する。</b> 無理に clamp すると
        /// min が max を超えて、どちらを採るかで画面が跳ねる。§11 が「無理な min/max clamp や
        /// 自動ズームをしない。外側は仮背景で埋める」と決めているのはこのため。
        /// </summary>
        /// <param name="desiredFocus">追従したい床上の位置（主人公）。</param>
        /// <param name="region">収めたい領域。</param>
        /// <param name="halfFootprint"><see cref="HalfFootprint"/> の結果。</param>
        public static Vector3 ClampFocus(
            Vector3 desiredFocus, in CameraRegionDefinition region, Vector2 halfFootprint)
        {
            Vector2 min = region.Min;
            Vector2 max = region.Max;

            float x = ClampAxis(desiredFocus.x, min.x, max.x, halfFootprint.x, region.Center.x);
            float z = ClampAxis(desiredFocus.z, min.y, max.y, halfFootprint.y, region.Center.y);

            return new Vector3(x, desiredFocus.y, z);
        }

        /// <summary>1 軸ぶんの収め方。入りきらない軸は中央固定。</summary>
        private static float ClampAxis(float value, float min, float max, float half, float center)
        {
            float low = min + half;
            float high = max - half;

            // 部屋の方が狭い＝どう動かしても外が見える。中央で固定する（揺らさない）。
            if (low > high)
            {
                return center;
            }

            return Mathf.Clamp(value, low, high);
        }
    }
}
