using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// 受け付けた 1 件の調査依頼（P4-07A。v1.0 §6.1「依頼 ID・対象地点 ID・実行世代番号を保持」）。
    /// 受付時に地点の設定・位置を写し取り、以後は SO 原本も地点も読み直さない（E21）。
    /// 地点そのものへの参照は「まだ生きているか」の再検査（§6.3）にだけ使う。
    /// </summary>
    public sealed class InvestigationRequest
    {
        public int RequestId { get; }

        /// <summary>実行世代。仲間側の駆動が依頼を受け取るたびに増え、古い依頼からの完了を弾く（E12）。</summary>
        public int Generation { get; }

        public StableId PointId { get; }
        public StableId CompanionId { get; }
        public StableId DiscoveryId { get; }
        public InvestigationSettings Settings { get; }

        /// <summary>
        /// この依頼で使う移動速度（受付時の Snapshot。R3-04）。往路も表示代理の帰還もこれだけを使う。
        ///
        /// 地点の設定（<see cref="Settings"/>）と同じ理由で固定する。実行中に Data の移動速度を書き換えると、
        /// 走っている依頼の到着時刻・移動タイムアウトとの関係が途中で変わってしまう。
        /// 新しい値は<b>次の依頼</b>から効く（E21「実行中の SO 編集：現行依頼は不変」）。
        /// </summary>
        public float MoveSpeed { get; }

        public Vector3 PointPosition { get; }
        public Vector3 ApproachPosition { get; }
        public Vector3 ApproachFacing { get; }

        /// <summary>再検査用の地点参照（破棄されていれば「無い」扱い）。</summary>
        public IInvestigationPoint Point { get; }

        /// <summary>成功が確定したか（帰還中の中断で取り消さない。§6.3）。</summary>
        public bool Succeeded { get; private set; }

        /// <summary>終端に達したか（成功・中断のどちらでも）。</summary>
        public bool Terminated { get; private set; }

        /// <summary>終端の理由（成功なら None）。</summary>
        public InvestigationInterruptReason TerminalReason { get; private set; }

        public InvestigationRequest(
            int requestId, int generation, IInvestigationPoint point, StableId companionId, float moveSpeed = 0f)
        {
            RequestId = requestId;
            Generation = generation;
            Point = point;
            PointId = point.PointId;
            CompanionId = companionId;
            DiscoveryId = point.DiscoveryId;
            Settings = point.Settings;
            MoveSpeed = moveSpeed > 0f ? moveSpeed : 0f;
            PointPosition = point.Position;
            ApproachPosition = point.ApproachPosition;
            Vector3 facing = point.ApproachFacing;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f)
            {
                facing = PointPosition - ApproachPosition;
                facing.y = 0f;
            }

            ApproachFacing = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;
        }

        /// <summary>地点がまだ生きて有効か（Disable／破棄なら false）。</summary>
        public bool PointIsAlive
        {
            get
            {
                if (Point == null || (Point is Object o && o == null))
                {
                    return false;
                }

                return Point.IsAvailable;
            }
        }

        internal void MarkSucceeded()
        {
            Succeeded = true;
            Terminated = true;
            TerminalReason = InvestigationInterruptReason.None;
        }

        internal void MarkInterrupted(InvestigationInterruptReason reason)
        {
            if (Succeeded)
            {
                return; // 成功後の中断は成功を取り消さない。
            }

            Terminated = true;
            TerminalReason = reason;
        }
    }

    /// <summary>依頼の進行を 1 段進めた結果。</summary>
    public enum InvestigationStep
    {
        /// <summary>まだ進行中。</summary>
        Continue = 0,

        /// <summary>調査時間が満了した。呼び出し側が完了確定（再検査・記録・通知）を行う。</summary>
        ReadyToComplete = 1,

        /// <summary>帰還が終わった（表示の引き渡し完了）。</summary>
        Finished = 2,

        /// <summary>中断条件を検出した（理由は out 引数）。</summary>
        Interrupted = 3,
    }

    /// <summary>
    /// 1 件の依頼の進行（Moving → Investigating → 完了確定 → Returning）を時間注入で進める純粋ロジック（v1.0 §6.1）。
    /// 位置は外から与える（本体なら実 Actor の位置、表示代理なら代理の位置）。Unity 依存なし。
    /// </summary>
    public sealed class InvestigationRun
    {
        private readonly InvestigationRequest _request;
        private float _phaseElapsed;

        public InvestigationPhase Phase { get; private set; } = InvestigationPhase.Idle;

        /// <summary>現在の段での経過秒（テスト・表示用）。</summary>
        public float PhaseElapsed => _phaseElapsed;

        /// <summary>調査の進み（0〜1。表示用）。</summary>
        public float InvestigationProgress =>
            Phase == InvestigationPhase.Investigating && _request.Settings.InvestigationSeconds > 0f
                ? Mathf.Clamp01(_phaseElapsed / _request.Settings.InvestigationSeconds)
                : 0f;

        public InvestigationRequest Request => _request;

        public InvestigationRun(InvestigationRequest request)
        {
            _request = request;
        }

        public void Begin()
        {
            Phase = InvestigationPhase.Moving;
            _phaseElapsed = 0f;
        }

        /// <summary>
        /// 1 Tick 進める。<paramref name="currentPosition"/> は調べに行く側の位置、<paramref name="playerPosition"/> は主人公。
        /// 継続範囲・到着・時間上限をここで判定する。完了確定そのものは行わない（<see cref="InvestigationStep.ReadyToComplete"/> を返す）。
        /// </summary>
        public InvestigationStep Tick(
            float deltaTime, Vector3 currentPosition, Vector3 playerPosition, out InvestigationInterruptReason reason)
        {
            reason = InvestigationInterruptReason.None;
            float dt = deltaTime < 0f ? 0f : deltaTime;
            InvestigationSettings s = _request.Settings;

            switch (Phase)
            {
                case InvestigationPhase.Moving:
                    if (PlayerLeft(playerPosition, s))
                    {
                        reason = InvestigationInterruptReason.PlayerLeftRange;
                        return InvestigationStep.Interrupted;
                    }

                    if (FormationSlot.HorizontalDistance(currentPosition, _request.ApproachPosition) <= s.ArrivalDistance)
                    {
                        Phase = InvestigationPhase.Investigating;
                        _phaseElapsed = 0f;
                        return InvestigationStep.Continue;
                    }

                    _phaseElapsed += dt;
                    if (_phaseElapsed > s.MoveTimeoutSeconds)
                    {
                        reason = InvestigationInterruptReason.MoveTimeout;
                        return InvestigationStep.Interrupted;
                    }

                    return InvestigationStep.Continue;

                case InvestigationPhase.Investigating:
                    if (PlayerLeft(playerPosition, s))
                    {
                        reason = InvestigationInterruptReason.PlayerLeftRange;
                        return InvestigationStep.Interrupted;
                    }

                    _phaseElapsed += dt;
                    return _phaseElapsed >= s.InvestigationSeconds
                        ? InvestigationStep.ReadyToComplete
                        : InvestigationStep.Continue;

                case InvestigationPhase.Returning:
                    _phaseElapsed += dt;
                    return _phaseElapsed >= s.ReturnVisualTimeoutSeconds
                        ? InvestigationStep.Finished
                        : InvestigationStep.Continue;

                default:
                    return InvestigationStep.Finished;
            }
        }

        /// <summary>完了確定が済んだ（記録と通知が終わった）ので帰還へ移る。</summary>
        public void EnterReturning()
        {
            Phase = InvestigationPhase.Returning;
            _phaseElapsed = 0f;
        }

        /// <summary>帰還が終わった（到着など時間以外の理由）。</summary>
        public void Finish()
        {
            Phase = InvestigationPhase.Idle;
            _phaseElapsed = 0f;
        }

        private bool PlayerLeft(Vector3 playerPosition, in InvestigationSettings s)
        {
            return s.ContinueRange > 0f
                && FormationSlot.HorizontalDistance(playerPosition, _request.PointPosition) > s.ContinueRange;
        }
    }
}
