using System.Collections.Generic;
using Momotaro.Gameplay.Enemy.Perception;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の対象選択（P4-03）。候補の中から「今狙うべき 1 体」を決める純粋関数で、Registry も Transform も触らない
    /// （候補の収集は <see cref="CompanionTargetTracker"/> の責務）。EditMode で決定的に検証できる。
    ///
    /// 規則は 3 つ。
    /// <list type="number">
    /// <item><description><b>維持</b>：現在の対象が有効で見失い距離の内側なら、より近い敵が現れても乗り換えない
    /// （毎フレーム対象が入れ替わって攻撃が始まらない、という挙動を防ぐ）。</description></item>
    /// <item><description><b>捕捉</b>：対象が無い・失った場合、捕捉距離の内側で最も近い有効な候補を選ぶ。</description></item>
    /// <item><description><b>同距離の決定性</b>：距離が同じなら Actor 同定 ID の小さい方を選ぶ（実行ごとに結果が変わらない）。</description></item>
    /// </list>
    ///
    /// 捕捉距離 &lt; 見失い距離（ヒステリシス）にすることで、境目での乗り換え往復を防ぐ。候補は呼び出し側で
    /// 敵対・有効に絞り込んである前提だが、本関数でも <see cref="IPerceptionTarget.IsActive"/> を再確認する。
    /// </summary>
    public static class CompanionTargetSelection
    {
        /// <summary>
        /// 対象を選ぶ。選べたら true と <paramref name="selected"/> を返す。
        /// </summary>
        /// <param name="candidates">敵対・有効に絞り込んだ候補（null 可）。</param>
        /// <param name="selfPosition">仲間の現在位置。</param>
        /// <param name="current">現在の対象（無ければ null）。維持判定に用いる。</param>
        /// <param name="acquireRange">新規捕捉できる距離（m）。0 以下で無制限。</param>
        /// <param name="loseRange">現在の対象を維持できる距離（m）。0 以下で無制限。捕捉距離以上であること。</param>
        /// <param name="selected">選ばれた対象。</param>
        public static bool TrySelect(
            IReadOnlyList<IPerceptionTarget> candidates,
            Vector3 selfPosition,
            IPerceptionTarget current,
            float acquireRange,
            float loseRange,
            out IPerceptionTarget selected)
        {
            selected = null;

            // 1) 現在の対象を維持できるなら乗り換えない。
            if (IsUsable(current) && Contains(candidates, current)
                && WithinRange(selfPosition, current.Position, loseRange))
            {
                selected = current;
                return true;
            }

            // 2) 捕捉距離の内側で最も近い候補を選ぶ。
            if (candidates == null)
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                IPerceptionTarget candidate = candidates[i];
                if (!IsUsable(candidate) || !WithinRange(selfPosition, candidate.Position, acquireRange))
                {
                    continue;
                }

                float distance = FormationSlot.HorizontalDistance(selfPosition, candidate.Position);
                if (distance < bestDistance - DistanceEpsilon)
                {
                    bestDistance = distance;
                    selected = candidate;
                }
                else if (selected != null && distance <= bestDistance + DistanceEpsilon
                    && candidate.ActorId < selected.ActorId)
                {
                    // 3) 同距離は Actor ID の小さい方（実行ごとに揺れない）。
                    bestDistance = distance;
                    selected = candidate;
                }
            }

            return selected != null;
        }

        /// <summary>同距離とみなす距離差（m）。浮動小数点の揺れで選択が入れ替わらないようにする。</summary>
        public const float DistanceEpsilon = 1e-4f;

        /// <summary>対象として使えるか（存在し、破棄されておらず、有効）。</summary>
        public static bool IsUsable(IPerceptionTarget target)
        {
            if (target == null)
            {
                return false;
            }

            // interface 経由の参照は Unity の null 演算子が効かないため、破棄済み Object を明示的に弾く。
            if (target is Object unityObject && unityObject == null)
            {
                return false;
            }

            return target.IsActive;
        }

        private static bool Contains(IReadOnlyList<IPerceptionTarget> candidates, IPerceptionTarget target)
        {
            if (candidates == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (ReferenceEquals(candidates[i], target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WithinRange(Vector3 from, Vector3 to, float range)
        {
            return range <= 0f || FormationSlot.HorizontalDistance(from, to) <= range;
        }
    }
}
