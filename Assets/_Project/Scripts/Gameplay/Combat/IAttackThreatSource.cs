using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 攻撃側が「観測可能な危険」を晒す契約（Phase3 P3-11 / P3-10。§9「入力を直接読まない／観測可能な危険に反応」）。敵の危険観測
    /// （<see cref="Momotaro.Gameplay.Enemy.Defense.PhysicsEnemyDangerSense"/>）は、プレイヤーの入力そのものではなく、現在の攻撃状態から
    /// 「攻撃中か」「ガード不能（必殺技）か」「攻撃方向」を読む。これにより、ガード不能な危険は回避で、通常の危険はガードで対処し分けられる
    /// （ガード専用敵が必殺技へ無効なガードを構える不具合、両能力持ちが必殺技を見て回避できない不具合を解消する）。
    /// <see cref="ICombatActivityState"/> が「体幹補正の対象か」だけを表すのに対し、本契約は防御 AI が必要とする危険の質を表す。
    /// </summary>
    public interface IAttackThreatSource
    {
        /// <summary>いま攻撃の予備動作／判定中か（＝観測可能な危険を出しているか）。</summary>
        bool IsThreateningAttack { get; }

        /// <summary>その攻撃がガード不能（必殺技など、通常ガードで受けられない）か。true なら受け手は回避を優先すべき。</summary>
        bool IsUnblockableThreat { get; }

        /// <summary>攻撃の向き（危険源の前方。XZ）。危険の方向判定の補助に使う。</summary>
        Vector3 ThreatForward { get; }
    }
}
