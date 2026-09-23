using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Companion;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// Interact の候補選択（P5-04。仕様書 v1.1 §7.1 の候補条件と順位）。
    ///
    /// <b>純粋な規則だけを持つ。</b> Scene も入力も触らないので EditMode で境界条件を決定的に検査できる
    /// （E11・E12）。窓口（<see cref="AreaInteractionController"/>）は集める・実行するだけ。
    ///
    /// 順位は §7.1 のとおり：
    /// <list type="number">
    /// <item><description>同じ Area・Floor、対象が有効、水平距離が受付距離以内。</description></item>
    /// <item><description>主人公から <c>InteractionAnchor</c> まで遮蔽物がない（壁越しは除外）。</description></item>
    /// <item><description>水平距離の小さいもの。</description></item>
    /// <item><description>同距離なら StableId の辞書順で固定。</description></item>
    /// </list>
    ///
    /// <b>向きは見ない。</b> §7.1 は「向きの厳密な円錐条件は P5 で追加しない」と決めている。
    /// P4 の調査選択が距離 → 前方 → ID の順で並べるのとはここが違い、
    /// <b>前方順位と ID 順位が逆転する配置で結果が変わる</b>（E30 が見ているのはこの差）。
    /// </summary>
    public static class AreaInteractionSelector
    {
        /// <summary>受付距離の既定値（§7.1 の InteractionRadius 初期値）。</summary>
        public const float DefaultInteractionRadius = 1.6f;

        /// <summary>同距離とみなす差（m）。P4 の調査選択と同じ値を使う。</summary>
        public const float DistanceEpsilon = 1e-3f;

        /// <summary>
        /// 候補を 1 つ選ぶ。選べなければ <paramref name="reason"/> に理由を入れて false。
        /// </summary>
        /// <param name="candidates">登録中の対象（窓口が registry から写したもの）。</param>
        /// <param name="playerPosition">主人公の基準位置。</param>
        /// <param name="areaId">いま居るエリア。</param>
        /// <param name="floorId">いま居る Floor。</param>
        /// <param name="defaultRadius">窓口の既定の受付距離。</param>
        /// <param name="probe">遮蔽判定（null なら遮蔽を見ない）。</param>
        public static bool TrySelect(
            IReadOnlyList<IAreaInteractable> candidates,
            Vector3 playerPosition,
            StableId areaId,
            int floorId,
            float defaultRadius,
            IObstacleProbe probe,
            out IAreaInteractable chosen,
            out AreaInteractionRejection reason)
        {
            chosen = null;
            reason = AreaInteractionRejection.NoTargetInRange;

            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            float fallbackRadius = defaultRadius > 0f ? defaultRadius : DefaultInteractionRadius;

            IAreaInteractable best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                IAreaInteractable item = candidates[i];
                if (item == null || IsDestroyed(item))
                {
                    continue;
                }

                if (!item.IsAvailable || !item.AreaId.Equals(areaId) || item.FloorId != floorId)
                {
                    continue;
                }

                // 対象固有の受付距離が短ければそちらを使う（§7.1）。長くはできない。
                float radius = item.InteractionRadius > 0f
                    ? Mathf.Min(fallbackRadius, item.InteractionRadius)
                    : fallbackRadius;

                float distance = FormationSlot.HorizontalDistance(playerPosition, item.InteractionAnchor);
                if (distance > radius)
                {
                    continue;
                }

                // 遮蔽は距離で絞ってから見る（壁判定は物理を触るので、候補を減らしてから呼ぶ）。
                if (probe != null && !probe.IsClear(playerPosition, item.InteractionAnchor))
                {
                    continue;
                }

                if (best == null || IsBetter(distance, item, bestDistance, best))
                {
                    best = item;
                    bestDistance = distance;
                }
            }

            if (best == null)
            {
                return false;
            }

            chosen = best;
            reason = AreaInteractionRejection.None;
            return true;
        }

        /// <summary>近い方が勝つ。同距離なら StableId の辞書順で決める（毎回同じ結果になる）。</summary>
        private static bool IsBetter(
            float distance, IAreaInteractable item, float bestDistance, IAreaInteractable best)
        {
            if (distance < bestDistance - DistanceEpsilon)
            {
                return true;
            }

            if (distance > bestDistance + DistanceEpsilon)
            {
                return false;
            }

            return string.CompareOrdinal(item.InteractableId.Value, best.InteractableId.Value) < 0;
        }

        /// <summary>破棄済みの MonoBehaviour が登録に残っていても落ちないようにする。</summary>
        private static bool IsDestroyed(IAreaInteractable item) =>
            item is UnityEngine.Object o && o == null;
    }
}
