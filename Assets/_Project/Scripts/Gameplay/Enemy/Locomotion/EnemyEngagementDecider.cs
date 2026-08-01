using Momotaro.Gameplay.Enemy.Perception;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Locomotion
{
    /// <summary>交戦判定の入力（Phase3 §5）。認識・自他位置・活動範囲・現在モードなど、すべて外部から与える。</summary>
    public readonly struct EngagementInput
    {
        public PerceptionPhase Phase { get; }
        public bool HasTarget { get; }
        public Vector3 TargetPos { get; }
        public Vector3 SelfPos { get; }
        public Vector3 HomePos { get; }
        public float ActivityRadius { get; }
        public float StopDistance { get; }
        public float TooCloseDistance { get; }
        public float ArriveEpsilon { get; }
        public EnemyEngagementMode CurrentMode { get; }
        public float ReturnWaitRemaining { get; }
        public float ReturnWaitSeconds { get; }
        public float DeltaTime { get; }

        public EngagementInput(PerceptionPhase phase, bool hasTarget, Vector3 targetPos, Vector3 selfPos, Vector3 homePos,
            float activityRadius, float stopDistance, float tooCloseDistance, float arriveEpsilon,
            EnemyEngagementMode currentMode, float returnWaitRemaining, float returnWaitSeconds, float deltaTime)
        {
            Phase = phase;
            HasTarget = hasTarget;
            TargetPos = targetPos;
            SelfPos = selfPos;
            HomePos = homePos;
            ActivityRadius = activityRadius;
            StopDistance = stopDistance;
            TooCloseDistance = tooCloseDistance;
            ArriveEpsilon = arriveEpsilon;
            CurrentMode = currentMode;
            ReturnWaitRemaining = returnWaitRemaining;
            ReturnWaitSeconds = returnWaitSeconds;
            DeltaTime = deltaTime;
        }
    }

    /// <summary>交戦判定の出力（Phase3 §5）。次モード・公開状態・移動目標・間合い理由・帰還待ち・認識抑制。</summary>
    public readonly struct EngagementOutput
    {
        public EnemyEngagementMode Mode { get; }
        public EnemyState State { get; }
        public bool HasMoveTarget { get; }
        public Vector3 MoveTarget { get; }
        public RepositionReason RepositionReason { get; }
        public float ReturnWaitRemaining { get; }
        public bool SuppressPerception { get; }

        public EngagementOutput(EnemyEngagementMode mode, EnemyState state, bool hasMoveTarget, Vector3 moveTarget,
            RepositionReason repositionReason, float returnWaitRemaining, bool suppressPerception)
        {
            Mode = mode;
            State = state;
            HasMoveTarget = hasMoveTarget;
            MoveTarget = moveTarget;
            RepositionReason = repositionReason;
            ReturnWaitRemaining = returnWaitRemaining;
            SuppressPerception = suppressPerception;
        }
    }

    /// <summary>
    /// 追跡・間合い・帰還の純粋な判定機（Phase3 §5）。範囲超過は新規交戦せず帰還、帰還中は再認識を抑制、初期位置到達で
    /// 待機して通常へ復帰する。範囲内では Alert で追跡→停止帯保持→過近後退、Suspicious は最終確認位置を調査する。
    /// Unity 非依存で EditMode 再現可能（移動の実行と物理・被弾処理は MonoBehaviour 側の責務）。
    /// </summary>
    public static class EnemyEngagementDecider
    {
        public static EngagementOutput Decide(in EngagementInput i)
        {
            // 帰還後待機：カウントダウンし、明けたら通常（Idle）へ復帰し認識を再開する。
            if (i.CurrentMode == EnemyEngagementMode.ReturnWait)
            {
                float rem = i.ReturnWaitRemaining - i.DeltaTime;
                if (rem <= 0f)
                {
                    return new EngagementOutput(EnemyEngagementMode.Idle, EnemyState.Idle, false, Vector3.zero,
                        RepositionReason.None, 0f, suppressPerception: false);
                }

                return new EngagementOutput(EnemyEngagementMode.ReturnWait, EnemyState.Return, false, Vector3.zero,
                    RepositionReason.None, rem, suppressPerception: true);
            }

            bool outside = VisionCheck.PlanarDistance(i.SelfPos, i.HomePos) > i.ActivityRadius;

            // 帰還：既に帰還中、または新たに範囲を超過したら初期位置へ。到達で待機へ。帰還中は再認識しない。
            if (i.CurrentMode == EnemyEngagementMode.Return || outside)
            {
                float distHome = VisionCheck.PlanarDistance(i.SelfPos, i.HomePos);
                if (distHome <= i.ArriveEpsilon)
                {
                    return new EngagementOutput(EnemyEngagementMode.ReturnWait, EnemyState.Return, false, Vector3.zero,
                        RepositionReason.None, i.ReturnWaitSeconds, suppressPerception: true);
                }

                return new EngagementOutput(EnemyEngagementMode.Return, EnemyState.Return, true, i.HomePos,
                    RepositionReason.None, 0f, suppressPerception: true);
            }

            // 範囲内：警戒中は追跡・保持・後退。
            if (i.Phase == PerceptionPhase.Alert && i.HasTarget)
            {
                float d = VisionCheck.PlanarDistance(i.SelfPos, i.TargetPos);
                if (d > i.StopDistance)
                {
                    return new EngagementOutput(EnemyEngagementMode.Chase, EnemyState.Chase, true, i.TargetPos,
                        RepositionReason.None, 0f, false);
                }

                if (d < i.TooCloseDistance)
                {
                    Vector3 back = ApproachCalculator.BackAwayTarget(i.SelfPos, i.TargetPos, i.StopDistance);
                    return new EngagementOutput(EnemyEngagementMode.Reposition, EnemyState.Reposition, true, back,
                        RepositionReason.TooClose, 0f, false);
                }

                // 停止帯：攻撃帯で保持（攻撃は P3-04）。
                return new EngagementOutput(EnemyEngagementMode.Hold, EnemyState.Alert, false, Vector3.zero,
                    RepositionReason.None, 0f, false);
            }

            // 不審：最終確認位置を調査（直進で近づき、着いたら諦めて待機）。
            if (i.Phase == PerceptionPhase.Suspicious && i.HasTarget)
            {
                float d = VisionCheck.PlanarDistance(i.SelfPos, i.TargetPos);
                if (d > i.ArriveEpsilon)
                {
                    return new EngagementOutput(EnemyEngagementMode.Investigate, EnemyState.Suspicious, true, i.TargetPos,
                        RepositionReason.None, 0f, false);
                }

                return new EngagementOutput(EnemyEngagementMode.Idle, EnemyState.Idle, false, Vector3.zero,
                    RepositionReason.None, 0f, false);
            }

            // 未認識・対象なし：初期位置から離れていれば帰還、そうでなければ待機。
            if (VisionCheck.PlanarDistance(i.SelfPos, i.HomePos) > i.ArriveEpsilon)
            {
                return new EngagementOutput(EnemyEngagementMode.Return, EnemyState.Return, true, i.HomePos,
                    RepositionReason.None, 0f, suppressPerception: true);
            }

            return new EngagementOutput(EnemyEngagementMode.Idle, EnemyState.Idle, false, Vector3.zero,
                RepositionReason.None, 0f, false);
        }
    }
}
