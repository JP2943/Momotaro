using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 隊列位置の解決（P4-02）。主人公の位置と論理前方から、仲間 1 体ぶんの「立つべき場所」を World XZ 平面で求める純粋関数。
    ///
    /// 主人公の後方に V 字で並べる：0 番が後方やや左、1 番が後方やや右、2 番がさらに後方中央。3 体（犬・猿・雉）を想定し、
    /// それ以上の番号は同じ並びを繰り返して後方へ下げる（決定的で、体数が増えても破綻しない）。
    /// 高さ（Y）は主人公に合わせる（本作は XZ 平面のゲームで、接地は Motor 側の責務）。
    /// </summary>
    public static class FormationSlot
    {
        /// <summary>並びの定義（後方距離倍率, 横方向倍率）。倍率は <c>spacing</c> に掛ける。</summary>
        private static readonly (float back, float side)[] Layout =
        {
            (1.0f, -0.6f), // 0：後方やや左（前衛＝犬丸の既定）
            (1.0f, +0.6f), // 1：後方やや右
            (1.8f, 0.0f),  // 2：さらに後方中央
        };

        /// <summary>並びの定義数（3 体ぶん）。これを超える番号は繰り返しつつ後方へ下げる。</summary>
        public static int LayoutCount => Layout.Length;

        /// <summary>方向ベクトルを有効とみなす最小の二乗長（これ未満は前方不定として +Z を用いる）。</summary>
        public const float ForwardEpsilonSqr = 1e-6f;

        /// <summary>
        /// 隊列位置を求める。<paramref name="leaderForward"/> は XZ へ射影して正規化し、不定なら +Z とみなす。
        /// <paramref name="slotIndex"/> の負値は 0 として扱う。<paramref name="spacing"/> の負値は 0 として扱う
        /// （＝主人公と同じ位置。呼び出し側の設定ミスで飛んでいかない）。
        /// </summary>
        public static Vector3 Resolve(Vector3 leaderPosition, Vector3 leaderForward, int slotIndex, float spacing)
        {
            Vector3 forward = Flatten(leaderForward);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x); // XZ 平面での右手方向。

            int index = slotIndex < 0 ? 0 : slotIndex;
            int cycle = index / Layout.Length;          // 4 体目以降は 1 周ぶん後方へ下げる。
            (float back, float side) = Layout[index % Layout.Length];

            float unit = spacing < 0f ? 0f : spacing;
            float backDistance = (back + cycle) * unit;
            float sideDistance = side * unit;

            return leaderPosition - (forward * backDistance) + (right * sideDistance);
        }

        /// <summary>XZ 平面での距離（高さの差は無視する）。</summary>
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>XZ へ射影して正規化する。不定なら +Z。</summary>
        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < ForwardEpsilonSqr ? Vector3.forward : direction.normalized;
        }
    }
}
