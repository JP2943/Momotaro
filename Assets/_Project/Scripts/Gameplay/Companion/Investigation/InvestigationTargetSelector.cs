using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>到達可能性の判定（選択側は「どこから」を知らないので、判定は呼び出し側が閉じ込めて渡す）。</summary>
    public interface IInvestigationReachability
    {
        bool IsReachable(IInvestigationPoint point);
    }

    /// <summary>
    /// 1 回の Interact に対して対象地点を<b>決定的に 1 件</b>選ぶ純粋ロジック（P4-07A。v1.0 §4.2、E05）。
    ///
    /// 規則：使用可能（有効・未調査・到達可能）な地点を優先し、主人公との XZ 距離が近い順、同距離なら主人公の
    /// 前方に近い順、最後に PointId の辞書順で 1 件へ決める。範囲内に使用可能な地点が無いときは、
    /// 何が原因で無かったかを理由として返す（調査済み／未到達／範囲外）。
    /// </summary>
    public static class InvestigationTargetSelector
    {
        /// <summary>同距離とみなす差（m）。</summary>
        public const float DistanceEpsilon = 1e-3f;

        public static bool TrySelect(
            IReadOnlyList<IInvestigationPoint> points,
            Vector3 playerPosition,
            Vector3 playerForward,
            IInvestigationRecord record,
            IInvestigationReachability reachability,
            out IInvestigationPoint chosen,
            out InvestigationRejectReason reason,
            out IInvestigationPoint context)
        {
            chosen = null;
            context = null;
            reason = InvestigationRejectReason.NoPointInRange;

            if (points == null)
            {
                return false;
            }

            IInvestigationPoint best = null;
            float bestDistance = float.MaxValue;
            float bestForward = float.MinValue;

            // 使用不能だった地点のうち、最寄りのものの理由を覚えておく（「なぜ調べられないか」を返すため）。
            InvestigationRejectReason nearestUnusableReason = InvestigationRejectReason.NoPointInRange;
            float nearestUnusableDistance = float.MaxValue;
            IInvestigationPoint nearestUnusable = null;

            Vector3 forward = playerForward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 1e-6f)
            {
                forward.Normalize();
            }

            for (int i = 0; i < points.Count; i++)
            {
                IInvestigationPoint point = points[i];
                if (point == null || IsDestroyed(point))
                {
                    continue;
                }

                float distance = FormationSlot.HorizontalDistance(playerPosition, point.Position);
                InvestigationSettings settings = point.Settings;

                if (!point.IsAvailable || !settings.IsUsable)
                {
                    Consider(ref nearestUnusableReason, ref nearestUnusableDistance, ref nearestUnusable, distance, point, InvestigationRejectReason.PointUnavailable);
                    continue;
                }

                if (distance > settings.InteractRange)
                {
                    continue; // 範囲外は理由の候補にもしない（そもそも見えていない）。
                }

                if (record != null && record.IsInvestigated(point.PointId))
                {
                    Consider(ref nearestUnusableReason, ref nearestUnusableDistance, ref nearestUnusable, distance, point, InvestigationRejectReason.AlreadyInvestigated);
                    continue;
                }

                if (reachability != null && !reachability.IsReachable(point))
                {
                    Consider(ref nearestUnusableReason, ref nearestUnusableDistance, ref nearestUnusable, distance, point, InvestigationRejectReason.Unreachable);
                    continue;
                }

                Vector3 toPoint = point.Position - playerPosition;
                toPoint.y = 0f;
                float forwardness = toPoint.sqrMagnitude > 1e-6f ? Vector3.Dot(forward, toPoint.normalized) : 1f;

                if (best == null || IsBetter(distance, forwardness, point.PointId, bestDistance, bestForward, best.PointId))
                {
                    best = point;
                    bestDistance = distance;
                    bestForward = forwardness;
                }
            }

            if (best == null)
            {
                reason = nearestUnusableReason;
                context = nearestUnusable;
                return false;
            }

            chosen = best;
            reason = InvestigationRejectReason.None;
            return true;
        }

        private static void Consider(
            ref InvestigationRejectReason reason, ref float bestDistance, ref IInvestigationPoint nearest,
            float distance, IInvestigationPoint point, InvestigationRejectReason candidate)
        {
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = point;
                reason = candidate;
            }
        }

        /// <summary>距離 → 前方への近さ → PointId 辞書順（すべて決定的）。</summary>
        private static bool IsBetter(
            float distance, float forwardness, StableId id,
            float bestDistance, float bestForward, StableId bestId)
        {
            if (Mathf.Abs(distance - bestDistance) > DistanceEpsilon)
            {
                return distance < bestDistance;
            }

            if (Mathf.Abs(forwardness - bestForward) > 1e-4f)
            {
                return forwardness > bestForward;
            }

            return string.CompareOrdinal(id.Value, bestId.Value) < 0;
        }

        private static bool IsDestroyed(IInvestigationPoint point)
        {
            return point is UnityEngine.Object o && o == null;
        }
    }
}
