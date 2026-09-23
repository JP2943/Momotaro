using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// カメラの領域（P5-06。仕様書 v1.1 §11）。XZ の<b>軸平行矩形</b>と RegionId、優先度を持つ。
    ///
    /// 回転する領域は持たない。斜めの部屋を作りたくなっても、矩形の組み合わせで表す。
    /// 回転を許すと「どちらの領域に居るか」の判定も、範囲の clamp も一気に面倒になり、
    /// 見た目の得より保守の損が大きい。
    /// </summary>
    public readonly struct CameraRegionDefinition
    {
        public CameraRegionDefinition(StableId regionId, int priority, Vector2 center, Vector2 size)
        {
            RegionId = regionId;
            Priority = priority;
            Center = center;
            Size = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
        }

        /// <summary>領域の安定 ID（同優先度のときの順位にも使う）。</summary>
        public StableId RegionId { get; }

        /// <summary>優先度。重なったときは大きい方が勝つ。</summary>
        public int Priority { get; }

        /// <summary>XZ 平面での中心（x, z）。</summary>
        public Vector2 Center { get; }

        /// <summary>XZ 平面での大きさ（幅, 奥行）。</summary>
        public Vector2 Size { get; }

        /// <summary>この領域の最小 (x, z)。</summary>
        public Vector2 Min => Center - Size * 0.5f;

        /// <summary>この領域の最大 (x, z)。</summary>
        public Vector2 Max => Center + Size * 0.5f;

        /// <summary>配線されているか（大きさが 0 の領域は使わない）。</summary>
        public bool IsValid => Size.x > 0f && Size.y > 0f && !RegionId.IsEmpty;

        /// <summary>XZ の点がこの領域に入っているか（境界は含む）。</summary>
        public bool Contains(Vector3 worldPosition)
        {
            Vector2 min = Min;
            Vector2 max = Max;
            return worldPosition.x >= min.x && worldPosition.x <= max.x
                && worldPosition.z >= min.y && worldPosition.z <= max.y;
        }
    }

    /// <summary>
    /// 主人公が属するカメラ領域を決める（P5-06。仕様書 v1.1 §11）。
    ///
    /// <b>重なりの決着を固定する。</b> §11 は「重複時は Priority 降順 → RegionId 辞書順」と決めている。
    /// 面積や距離で決めると、境界をまたぐたびに揺れて、どちらが選ばれるか作り手にも読めなくなる。
    /// どの領域にも入らなければ、呼び出し側が Area の既定領域を使う。
    /// </summary>
    public static class CameraRegionSelector
    {
        /// <summary>属する領域を 1 つ選ぶ。どこにも入らなければ false。</summary>
        public static bool TrySelect(
            IReadOnlyList<CameraRegionDefinition> regions,
            Vector3 playerPosition,
            out CameraRegionDefinition chosen)
        {
            chosen = default;

            if (regions == null)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < regions.Count; i++)
            {
                CameraRegionDefinition region = regions[i];
                if (!region.IsValid || !region.Contains(playerPosition))
                {
                    continue;
                }

                if (!found || IsBetter(region, chosen))
                {
                    chosen = region;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>優先度が高い方。同じなら RegionId の辞書順で早い方。</summary>
        private static bool IsBetter(in CameraRegionDefinition candidate, in CameraRegionDefinition best)
        {
            if (candidate.Priority != best.Priority)
            {
                return candidate.Priority > best.Priority;
            }

            return string.CompareOrdinal(candidate.RegionId.Value, best.RegionId.Value) < 0;
        }
    }
}
