using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 戦闘に参加する主体（主人公・仲間・敵が共通実装する想定）の最小契約（P2-01）。
    /// 攻撃側・被弾側の同定と、World XZ 平面上の位置・前方向を提供する。
    /// 具体的な MonoBehaviour 実装は本 Phase では作らない（依頼 §10）。
    /// </summary>
    public interface ICombatActor
    {
        /// <summary>陣営（最小拡張点。敵対関係の解決は後続 Phase）。</summary>
        CombatFaction Faction { get; }

        /// <summary>
        /// フロア／階層の識別子（最小拡張点）。既存の FloorId 仕様・コードが無いため、
        /// P2-01 では既定 0 を返すだけの拡張点とし、フロア分離ロジックは実装しない（依頼 §9）。
        /// </summary>
        int FloorId { get; }

        /// <summary>World 空間での位置。命中方向・背後判定は XZ 平面で評価する。</summary>
        Vector3 WorldPosition { get; }

        /// <summary>前方向（XZ 平面）。背後判定の基準に用いる。</summary>
        Vector3 Forward { get; }
    }
}
