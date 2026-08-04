using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Screen
{
    /// <summary>
    /// 画面端警告の純粋計算（Phase3 P3-08 受入修正。§9.2）。発射者の Viewport 射影から画面端方向・クランプ位置・おおよその距離を求める。
    /// カメラ背面（Viewport z&lt;0）でも中心対称に反転して安定に方向を得る。Camera API へは依存せず（Viewport 点を入力に取る）EditMode で
    /// 決定的に検証できる。表示は <see cref="EnemyEdgeWarningView"/> 側（Presentation）が本計算を用いて行う。
    /// </summary>
    public static class EdgeWarningMath
    {
        /// <summary>
        /// Viewport 点（x,y は 0..1 が画面内、z はカメラ前後）から Screen 座標へ変換する。z&lt;0（背面）は中心対称に反転して
        /// 「発射者のいる向き」を安定に表す。
        /// </summary>
        public static Vector2 ScreenPointFromViewport(Vector3 viewport, float width, float height)
        {
            float cx = viewport.x - 0.5f;
            float cy = viewport.y - 0.5f;
            if (viewport.z < 0f)
            {
                cx = -cx; // 背面は反転（左右・上下が入れ替わるのを補正）。
                cy = -cy;
            }

            return new Vector2((cx + 0.5f) * width, (cy + 0.5f) * height);
        }

        /// <summary>Screen 座標を画面内側マージンへクランプする（画面端に張り付かせる）。</summary>
        public static Vector2 ClampInside(Vector2 p, float width, float height, float margin)
        {
            float m = margin < 0f ? 0f : margin;
            return new Vector2(
                Mathf.Clamp(p.x, m, width - m),
                Mathf.Clamp(p.y, m, height - m));
        }

        /// <summary>画面中心から <paramref name="screenPoint"/> への正規化方向（発射者のいる向き）。中心一致時は上向き。</summary>
        public static Vector2 DirectionFromCenter(Vector2 screenPoint, float width, float height)
        {
            Vector2 d = new Vector2(screenPoint.x - width * 0.5f, screenPoint.y - height * 0.5f);
            return d.sqrMagnitude < 1e-6f ? Vector2.up : d.normalized;
        }

        /// <summary>発射者までのおおよその距離（対象＝主人公からの距離。表示用）。</summary>
        public static float ApproxDistance(Vector3 sourceWorld, Vector3 targetWorld)
        {
            return Vector3.Distance(sourceWorld, targetWorld);
        }
    }
}
