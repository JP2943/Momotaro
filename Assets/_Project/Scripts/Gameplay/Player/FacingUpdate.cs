using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// 向き更新の純粋ロジック（Phase1 P1-07）。ロック中は現在方向を保持し（ガード中の向き固定）、
    /// 非ロック時は <see cref="FacingResolver"/> で決定する。
    /// </summary>
    public static class FacingUpdate
    {
        /// <summary>ロック状態を考慮して次の向きを返す。</summary>
        public static FacingDirection Resolve(bool locked, Vector2 input, FacingDirection current, float deadzone)
        {
            return locked ? current : FacingResolver.Resolve(input, current, deadzone);
        }
    }
}
