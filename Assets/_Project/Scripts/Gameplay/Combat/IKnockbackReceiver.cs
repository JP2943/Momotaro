using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// ノックバック（吹き飛ばし）を受ける拡張点（Phase2 P2-10。仕様書 §3.6「小型敵を大きく吹き飛ばすが、ボスは吹き飛ばさない」）。
    /// 攻撃側が必殺技命中時に呼ぶ。実際の反応（物理・のけぞり等）は受け手側の実装に委ねる。Phase 2 の検証用ダミーは仮反応でよい。
    /// ボス（大型敵）は本インターフェースを実装しないか、実装しても無効化する方針とする。
    /// </summary>
    public interface IKnockbackReceiver
    {
        /// <summary>指定方向へ <paramref name="force"/> のノックバックを受ける。0 以下や無効な場合は何もしなくてよい。</summary>
        void ReceiveKnockback(Vector3 direction, float force);
    }
}
