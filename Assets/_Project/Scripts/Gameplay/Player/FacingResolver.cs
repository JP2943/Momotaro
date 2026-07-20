using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// 移動入力から表示方向（<see cref="FacingDirection"/>）を決める純粋ロジック（Phase1 P1-04）。
    /// Deadzone 未満の微小入力では現在の方向を保持し、震えを防ぐ。斜めは優勢軸で決定し、
    /// 完全同値（|x| == |y|）のときは横（Right/Left）を優先する固定規則とする。
    /// </summary>
    public static class FacingResolver
    {
        /// <summary>
        /// 入力・現在方向・Deadzone から次の表示方向を返す。
        /// </summary>
        /// <param name="input">移動入力（Deadzone 前の生値でよい）。</param>
        /// <param name="current">現在の表示方向（無入力時に保持する）。</param>
        /// <param name="deadzone">この大きさ未満の入力は無視する（0〜1 想定）。</param>
        public static FacingDirection Resolve(Vector2 input, FacingDirection current, float deadzone)
        {
            if (input.sqrMagnitude < deadzone * deadzone)
            {
                return current;
            }

            float absX = Mathf.Abs(input.x);
            float absY = Mathf.Abs(input.y);

            // 同値を含め横優先（|x| >= |y| なら横軸）。
            if (absX >= absY)
            {
                return input.x >= 0f ? FacingDirection.Right : FacingDirection.Left;
            }

            return input.y >= 0f ? FacingDirection.Up : FacingDirection.Down;
        }
    }
}
