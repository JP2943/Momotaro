using System.Text;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Perception;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 敵 AI デバッグ表示の 1 行を組み立てる純粋ヘルパ（Phase3 P3-11。§「Development 限定で State、Target、Threat、選択 Attack、Score、
    /// Slot、LOS、活動範囲を切替表示」）。無効時は文字列を一切構築せず null を返す（＝Debug OFF で余計な文字列確保をしない契約を純粋に保証）。
    /// 表示専用で Gameplay を分岐しない。数値は読み取り値をそのまま整形するだけ。
    /// </summary>
    public static class EnemyDebugReadout
    {
        /// <summary>
        /// 表示行を作る。<paramref name="enabled"/> が false なら即 null（何も確保しない）。有効時は State／Target／Threat／
        /// 選択 Attack／Score／Slot／LOS／活動範囲を 1 行へ整形する。
        /// </summary>
        public static string Build(
            bool enabled,
            EnemyState state,
            int targetId,
            float threat,
            EnemyAttackClass attackClass,
            bool attacking,
            float score,
            bool holdsSlot,
            PerceptionPhase los,
            float activityRadius)
        {
            if (!enabled)
            {
                return null; // Debug OFF：文字列確保なし。
            }

            var sb = new StringBuilder(96);
            sb.Append("State=").Append(state);
            sb.Append(" Tgt=").Append(targetId);
            sb.Append(" Thr=").Append(threat.ToString("0.0"));
            sb.Append(" Atk=").Append(attacking ? attackClass.ToString() : "-");
            sb.Append(" Score=").Append(score.ToString("0.0"));
            sb.Append(" Slot=").Append(holdsSlot ? "1" : "0");
            sb.Append(" LOS=").Append(los);
            sb.Append(" R=").Append(activityRadius.ToString("0"));
            return sb.ToString();
        }
    }
}
