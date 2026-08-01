using System;
using System.Collections.Generic;

namespace Momotaro.Gameplay.Enemy.Combat
{
    /// <summary>
    /// 攻撃候補 1 件の評価用パラメータ（Phase3 §6.2）。使用可否（距離・角度・Cooldown）と基礎 Score を持つ。
    /// 周囲敵数・味方位置・自 HP・Slot・画面内可否などの因子は将来の拡張点（P3-07/09）で加算する。
    /// </summary>
    public readonly struct AttackOption
    {
        public float UseRange { get; }
        public float UseAngle { get; }
        public float BaseScore { get; }

        public AttackOption(float useRange, float useAngle, float baseScore)
        {
            UseRange = useRange;
            UseAngle = useAngle;
            BaseScore = baseScore;
        }
    }

    /// <summary>
    /// 敵攻撃の選択（Phase3 §6.2）。使用不可条件（Cooldown 中・距離外・角度外）は Score を下げるのではなく候補から除外し、
    /// 同一攻撃の連続使用は Score 50% 減（使用可能候補が 1 種のときは例外＝減らさない）。同点はSeed注入の tie-break で決める。
    /// Score 内訳を <see cref="Evaluate"/> の out 配列で返し Debug 表示に使える。純粋・再現可能。
    /// </summary>
    public static class EnemyAttackSelector
    {
        /// <summary>
        /// 候補を評価し、選ばれた index（無ければ -1）を返す。<paramref name="scores"/> は各候補の Score
        /// （除外は <see cref="float.NegativeInfinity"/>）。<paramref name="tieBreak"/> は [0,count) を返す乱数源（null で先頭）。
        /// </summary>
        public static int Evaluate(
            float distance,
            float angleToTarget,
            IReadOnlyList<AttackOption> options,
            IReadOnlyList<float> cooldownRemaining,
            int lastUsedIndex,
            Func<int, int> tieBreak,
            out float[] scores)
        {
            int n = options.Count;
            scores = new float[n];
            int usableCount = 0;

            for (int i = 0; i < n; i++)
            {
                AttackOption o = options[i];
                bool cool = cooldownRemaining == null || i >= cooldownRemaining.Count || cooldownRemaining[i] <= 0f;
                bool usable = cool && distance <= o.UseRange && angleToTarget <= o.UseAngle;
                if (usable)
                {
                    scores[i] = o.BaseScore;
                    usableCount++;
                }
                else
                {
                    scores[i] = float.NegativeInfinity; // 候補除外。
                }
            }

            if (usableCount == 0)
            {
                return -1;
            }

            // 連続使用の 50% 減（使用可能候補が 2 種以上のときのみ）。1 種しか撃てない試作敵は例外。
            if (usableCount > 1 && lastUsedIndex >= 0 && lastUsedIndex < n && scores[lastUsedIndex] > float.NegativeInfinity)
            {
                scores[lastUsedIndex] *= 0.5f;
            }

            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (scores[i] > best)
                {
                    best = scores[i];
                }
            }

            // 最高 Score の候補を集め、同点なら tie-break。
            int topCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (scores[i] == best)
                {
                    topCount++;
                }
            }

            if (topCount == 1)
            {
                for (int i = 0; i < n; i++)
                {
                    if (scores[i] == best)
                    {
                        return i;
                    }
                }
            }

            int pick = tieBreak != null ? tieBreak(topCount) : 0;
            if (pick < 0)
            {
                pick = 0;
            }

            if (pick >= topCount)
            {
                pick = topCount - 1;
            }

            int seen = 0;
            for (int i = 0; i < n; i++)
            {
                if (scores[i] == best)
                {
                    if (seen == pick)
                    {
                        return i;
                    }

                    seen++;
                }
            }

            return -1;
        }
    }
}
