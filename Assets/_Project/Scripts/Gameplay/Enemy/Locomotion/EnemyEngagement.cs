using Momotaro.Gameplay.Enemy.Perception;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Locomotion
{
    /// <summary>
    /// 間合い調整（Reposition）の理由（Phase3 §5/§8）。P3-03 では過近を実処理し、射線不良・攻撃後待機・Slot 待ちは
    /// 共通形として用意しておく（実駆動は P3-04/07/08）。
    /// </summary>
    public enum RepositionReason
    {
        /// <summary>理由なし。</summary>
        None = 0,

        /// <summary>対象に近すぎる（後退）。</summary>
        TooClose = 1,

        /// <summary>射線不良（撃てない・見えない）。</summary>
        LineOfSightBlocked = 2,

        /// <summary>攻撃後の待機。</summary>
        PostAttackWait = 3,

        /// <summary>攻撃 Slot 待ち。</summary>
        SlotWait = 4,
    }

    /// <summary>
    /// 交戦の内部モード（Phase3 §5）。EnemyState（公開状態）とは別に、追跡・間合い・帰還の遷移を細かく保持する。
    /// </summary>
    public enum EnemyEngagementMode
    {
        /// <summary>非交戦（待機）。</summary>
        Idle = 0,

        /// <summary>最終確認位置の調査（不審）。</summary>
        Investigate = 1,

        /// <summary>追跡（対象へ接近）。</summary>
        Chase = 2,

        /// <summary>間合い保持（攻撃帯で停止）。</summary>
        Hold = 3,

        /// <summary>間合い調整（後退など）。</summary>
        Reposition = 4,

        /// <summary>帰還（初期位置へ）。</summary>
        Return = 5,

        /// <summary>帰還後待機。</summary>
        ReturnWait = 6,
    }

    /// <summary>
    /// 活動範囲（Phase3 §5）。初期位置を中心とする半径で、範囲超過判定と帰還方向を提供する（XZ 平面）。
    /// </summary>
    public readonly struct ActivityBounds
    {
        /// <summary>中心（初期位置）。</summary>
        public Vector3 Center { get; }
        /// <summary>活動半径（m）。</summary>
        public float Radius { get; }

        public ActivityBounds(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        /// <summary>中心からの XZ 平面距離。</summary>
        public float DistanceFromCenter(Vector3 position) => VisionCheck.PlanarDistance(Center, position);

        /// <summary>範囲外か（半径超過）。</summary>
        public bool IsOutside(Vector3 position) => DistanceFromCenter(position) > Radius;
    }

    /// <summary>
    /// 接近・間合いの純粋計算（Phase3 §5）。XZ 平面で目標へ向かう速度、停止帯（攻撃帯）判定、過近判定、後退目標を求める。
    /// </summary>
    public static class ApproachCalculator
    {
        /// <summary>目標へ向かう XZ 速度。停止半径以内は 0（それ以上近づかない）。</summary>
        public static Vector3 DesiredVelocity(Vector3 selfPos, Vector3 targetPos, float moveSpeed, float stopRadius)
        {
            Vector3 to = targetPos - selfPos;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist <= stopRadius || dist < 1e-5f || moveSpeed <= 0f)
            {
                return Vector3.zero;
            }

            return to / dist * moveSpeed;
        }

        /// <summary>停止帯（過近超・停止距離以内）にいるか＝攻撃帯で保持できる間合い。</summary>
        public static bool InStopBand(Vector3 selfPos, Vector3 targetPos, float stopDistance, float tooCloseDistance)
        {
            float d = VisionCheck.PlanarDistance(selfPos, targetPos);
            return d <= stopDistance && d >= tooCloseDistance;
        }

        /// <summary>過近（近すぎて後退が必要）か。</summary>
        public static bool IsTooClose(Vector3 selfPos, Vector3 targetPos, float tooCloseDistance)
        {
            return VisionCheck.PlanarDistance(selfPos, targetPos) < tooCloseDistance;
        }

        /// <summary>対象から離れる後退目標（現在地から対象の反対方向へ desiredDistance）。</summary>
        public static Vector3 BackAwayTarget(Vector3 selfPos, Vector3 targetPos, float desiredDistance)
        {
            Vector3 away = selfPos - targetPos;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f)
            {
                away = Vector3.back; // 完全重なり時の既定退避方向。
            }

            return selfPos + away.normalized * desiredDistance;
        }
    }
}
