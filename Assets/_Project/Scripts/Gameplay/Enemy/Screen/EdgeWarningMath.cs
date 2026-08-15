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

        /// <summary>画面端警告の 8 方向（＋中心一致の None）。Screen 座標系（+Y が上）で量子化する。</summary>
        public enum EdgeDirection8
        {
            None = 0,
            N = 1,
            NE = 2,
            E = 3,
            SE = 4,
            S = 5,
            SW = 6,
            W = 7,
            NW = 8,
        }

        /// <summary>
        /// 方向ベクトル（Screen 座標系、+Y 上）を 8 方向へ量子化する（Phase3 P3-08 受入修正）。ほぼ中心（長さ ~0）は
        /// <see cref="EdgeDirection8.None"/>。45 度セクタで判定し、境界は最近傍へ丸める。純粋・決定的。
        /// </summary>
        public static EdgeDirection8 Quantize8(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f)
            {
                return EdgeDirection8.None;
            }

            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg; // E=0, N=90, W=±180, S=-90
            if (ang < 0f)
            {
                ang += 360f; // 0..360
            }

            int sector = Mathf.RoundToInt(ang / 45f) % 8; // 0=E,1=NE,2=N,3=NW,4=W,5=SW,6=S,7=SE
            switch (sector)
            {
                case 0: return EdgeDirection8.E;
                case 1: return EdgeDirection8.NE;
                case 2: return EdgeDirection8.N;
                case 3: return EdgeDirection8.NW;
                case 4: return EdgeDirection8.W;
                case 5: return EdgeDirection8.SW;
                case 6: return EdgeDirection8.S;
                default: return EdgeDirection8.SE;
            }
        }

        /// <summary>8 方向を矢印グリフへ写す。None は中心マーカー。</summary>
        public static string Glyph(EdgeDirection8 d)
        {
            switch (d)
            {
                case EdgeDirection8.N: return "↑";
                case EdgeDirection8.NE: return "↗";
                case EdgeDirection8.E: return "→";
                case EdgeDirection8.SE: return "↘";
                case EdgeDirection8.S: return "↓";
                case EdgeDirection8.SW: return "↙";
                case EdgeDirection8.W: return "←";
                case EdgeDirection8.NW: return "↖";
                default: return "●";
            }
        }

        /// <summary>方向ベクトルから直接、警告グリフを得る（<see cref="Quantize8"/>＋<see cref="Glyph"/>）。</summary>
        public static string ArrowGlyph(Vector2 dir) => Glyph(Quantize8(dir));
    }
}
