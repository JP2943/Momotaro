using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Screen
{
    /// <summary>
    /// Viewport 境界の純粋判定（Phase3 §8.2）。Viewport 座標（0..1）とカメラ前後、Data 化した余白から画面内かを求める。
    /// 余白は境界付近の ON／OFF 振動を防ぐバッファ（余白ぶん外側まで「画面内」とみなす）。Camera 実装はこの計算へ委譲する
    /// ことで、境界規則を Unity 非依存で EditMode 検証できる。
    /// </summary>
    public static class ViewportBounds
    {
        /// <summary>
        /// Viewport 点（x,y は 0..1 が画面内）とカメラ前方フラグ、余白 <paramref name="margin01"/>（0..1）から画面内かを返す。
        /// カメラ背面（<paramref name="inFront"/>=false）は常に画面外。余白ぶん外側（[−m, 1+m]）まで画面内とみなす。
        /// </summary>
        public static bool IsInside(Vector2 viewport01, bool inFront, float margin01)
        {
            if (!inFront)
            {
                return false;
            }

            float m = margin01 > 0f ? margin01 : 0f;
            return viewport01.x >= -m && viewport01.x <= 1f + m
                && viewport01.y >= -m && viewport01.y <= 1f + m;
        }
    }
}
