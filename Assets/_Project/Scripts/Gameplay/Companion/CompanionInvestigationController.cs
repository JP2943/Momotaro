using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>依頼の実行形態。</summary>
    public enum InvestigationMode
    {
        None = 0,

        /// <summary>通常表示の戦闘 Actor を探索へ引き渡す（平常時。v1.0 §8.3 の「探索開始」行）。</summary>
        Body = 1,

        /// <summary>
        /// 戦闘 Actor を持たない探索用の表示代理で調べる（Down／Away／離脱 CD 中の加入済み犬丸。v1.0 §5.2）。
        /// 戦闘 Actor の HP・Down・復帰時計・CD には触らない。
        /// </summary>
        Proxy = 2,
    }

    /// <summary>
    /// 依頼の完了確定・中断を受ける側（探索の調停役）。駆動は「調査時間が満了した」「中断した」を報告するだけで、
    /// 記録・一回性・通知は調停役が確定する（v1.0 §6.3 の順序を 1 か所に置く）。
    /// </summary>
    public interface IInvestigationOutcomeSink
    {
        /// <summary>完了確定を試みる（依頼の有効性を再検査 → 記録 → 成功終端化 → 通知）。失敗なら理由。</summary>
        bool TryConfirmCompletion(
            CompanionInvestigationController driver, InvestigationRequest request, out InvestigationInterruptReason failure);

        /// <summary>未完了のまま中断した。</summary>
        void OnInterrupted(
            CompanionInvestigationController driver, InvestigationRequest request, InvestigationInterruptReason reason);
    }

    /// <summary>
    /// 仲間 1 体の探索駆動（P4-07A。v1.0 §5・§6・§8、c8c0ddf §5）。<b>自分では地点を探さない。</b>
    /// 依頼は主人公の Interact から調停役（<see cref="InvestigationCoordinator"/>）が 1 件だけ渡す。
    ///
    /// 2 つの実行形態を持つ。
    /// <list type="bullet">
    /// <item><description><b>本体</b>：平常時は戦闘 Actor そのものが調べに行く。行動の所有権
    /// （<see cref="CompanionActionOwner.Investigate"/>）と移動の所有権を取り、状態を <see cref="CompanionState.Investigate"/> にする。
    /// 探索中は自動 Follow／Chase／Attack／Guard／Evade を<b>拒否</b>する（許可表）。実命中・戦闘開始は同期的に
    /// 所有権を奪い（<see cref="OnActionInterrupted"/>）、その同じ呼び出しの中で依頼を中断・解放する。</description></item>
    /// <item><description><b>表示代理</b>：Down／Away／復帰待ちの加入済み犬丸は、戦闘 Actor に触れず、代理の座標だけを
    /// この駆動が進める。HP・復帰時計・CD は変えない。自然復帰の時刻が来ても調査は続く（§9）。</description></item>
    /// </list>
    ///
    /// 時間は外部注入（<see cref="TickInvestigation"/>）。Pause は凍結、会話・イベントは中断（§6.4）。
    /// 依頼で使う数値は受付時の Snapshot（<see cref="InvestigationRequest.Settings"/>）だけを読む（E21）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionInvestigationController : MonoBehaviour, ICompanionActionParticipant
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("状態要求の唯一の窓口（未設定なら自動取得）。")]
        [SerializeField] private CompanionStateArbiter _states;

        [Tooltip("移動と向きの書き手（未設定なら自動取得）。")]
        [SerializeField] private CompanionMovementArbiter _arbiter;

        private IObstacleProbe _probe;
        private IInvestigationOutcomeSink _sink;
        private IInteractActor _player;
        private InvestigationRequest _request;
        private InvestigationRun _run;
        private CompanionActionHandle _action;
        private int _generation;

        /// <summary>いまの実行形態（依頼が無ければ None）。</summary>
        public InvestigationMode Mode { get; private set; }

        /// <summary>依頼を実行中か（受付〜引き渡し完了）。</summary>
        public bool IsBusy => _request != null;

        /// <summary>現在の依頼（無ければ null）。</summary>
        public InvestigationRequest CurrentRequest => _request;

        /// <summary>進行状態。</summary>
        public InvestigationPhase Phase => _run != null ? _run.Phase : InvestigationPhase.Idle;

        /// <summary>調査の進み（0〜1。表示用）。</summary>
        public float Progress => _run != null ? _run.InvestigationProgress : 0f;

        /// <summary>実行世代（依頼を受け取るたびに増える。古い依頼の完了を弾く。E12）。</summary>
        public int Generation => _generation;

        /// <summary>この仲間の StableId（Data の Id。地点の RequiredCompanion と照合する）。</summary>
        public StableId CompanionId => _actor != null && _actor.Data != null ? _actor.Data.Id : default;

        /// <summary>
        /// この駆動が動かす Body（Editor の検査用。未注入なら同じ GameObject の Actor。状態は変えない）。
        /// 別 GameObject の駆動が同じ Body を指すと二重 Tick になるため、Validator が同一 Body の駆動数をこれで数える（E24）。
        /// </summary>
        public CompanionActor BoundActor => _actor != null ? _actor : GetComponent<CompanionActor>();

        /// <summary>表示代理が出ているか（Presentation が読む）。</summary>
        public bool ProxyVisible { get; private set; }

        /// <summary>表示代理の位置（Presentation はこれを描くだけ。§5.2）。</summary>
        public Vector3 ProxyPosition { get; private set; }

        /// <summary>表示代理の向き。</summary>
        public Vector3 ProxyFacing { get; private set; } = Vector3.forward;

        /// <summary>完了した依頼の数（テスト・診断用）。</summary>
        public int CompletedCount { get; private set; }

        /// <summary>中断した依頼の数（テスト・診断用）。</summary>
        public int InterruptedCount { get; private set; }

        /// <summary>直近の中断理由（テスト・診断用）。</summary>
        public InvestigationInterruptReason LastInterruptReason { get; private set; }

        /// <summary>Actor 等を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionActor actor, CompanionStateArbiter states = null, CompanionMovementArbiter arbiter = null)
        {
            if (actor != null)
            {
                _actor = actor;
            }

            if (states != null)
            {
                _states = states;
            }

            if (arbiter != null)
            {
                _arbiter = arbiter;
            }
        }

        /// <summary>障害物判定を差し替える（テストで Fake を注入する。未設定なら壁レイヤーへの物理判定）。</summary>
        public void SetObstacleProbe(IObstacleProbe probe)
        {
            _probe = probe;
        }

        /// <summary>
        /// いま依頼を受けられるか。受けられるなら実行形態を返す。加入資格・戦闘中かどうかは調停役が先に見る。
        /// ここは「戦闘 Actor の状態として始められるか」だけ（v1.0 §8.3「探索開始」行と §5.2）。
        /// </summary>
        public InvestigationRejectReason CanAccept(out InvestigationMode mode)
        {
            mode = InvestigationMode.None;
            EnsureRefs();

            if (_actor == null)
            {
                return InvestigationRejectReason.NotWired;
            }

            if (IsBusy)
            {
                return InvestigationRejectReason.CompanionBusy; // 複数依頼の列は持たない（§4.3）。
            }

            CompanionState state = _actor.State;
            if (state == CompanionState.Down || state == CompanionState.Away || state == CompanionState.Recovering)
            {
                mode = InvestigationMode.Proxy; // 戦闘上の生存状態へ戻さずに調べる（§5.2、§8.3 補足）。
                return InvestigationRejectReason.None;
            }

            if (CompanionActionRules.Evaluate(CompanionActionKind.Investigate, state) != CompanionActionVerdict.Allowed)
            {
                return InvestigationRejectReason.CompanionNotReady; // 攻撃・防御・ひるみ・イベント中は始めない。
            }

            mode = InvestigationMode.Body;
            return InvestigationRejectReason.None;
        }

        /// <summary>
        /// 依頼を受け取って開始する。受け付けたら true。受け付けなかった理由は <paramref name="reason"/>。
        /// 開始と同時に移動の所有権を取る（本体）か、代理の位置を決める（表示代理）。
        /// </summary>
        public bool TryBegin(
            IInvestigationPoint point, int requestId, IInvestigationOutcomeSink sink, IInteractActor player,
            out InvestigationRequest request, out InvestigationRejectReason reason)
        {
            request = null;
            reason = CanAccept(out InvestigationMode mode);
            if (reason != InvestigationRejectReason.None)
            {
                return false;
            }

            if (point == null || sink == null || player == null)
            {
                reason = InvestigationRejectReason.NotWired;
                return false;
            }

            if (!point.IsAvailable || !point.Settings.IsUsable)
            {
                reason = InvestigationRejectReason.PointUnavailable;
                return false;
            }

            var candidate = new InvestigationRequest(requestId, _generation + 1, point, CompanionId);

            if (mode == InvestigationMode.Body)
            {
                // 行動の所有権を先に取る（取れなければ始めない）。可否は許可表が決める。
                if (_states == null || !_states.TryStartAction(
                        CompanionActionOwner.Investigate, CompanionActionKind.Investigate, CompanionState.Investigate,
                        CompanionStateChangeReason.InvestigationStarted, out _action))
                {
                    reason = InvestigationRejectReason.CompanionNotReady;
                    return false;
                }

                ProxyVisible = false;
            }
            else
            {
                // 表示代理：主人公付近の安全な出現位置（倒れている本体の位置。退場中なら主人公の位置）から出る。
                ProxyPosition = _actor.State == CompanionState.Away ? player.Position : _actor.WorldPosition;
                ProxyFacing = Face(candidate.ApproachPosition - ProxyPosition);
                ProxyVisible = true;
            }

            _generation++;
            _request = candidate;
            request = candidate;
            _run = new InvestigationRun(candidate);
            _sink = sink;
            _player = player;
            Mode = mode;
            _run.Begin();

            // 開始したその Tick から移動の所有権を握る（本体）。先に取らないと同じフレームに追従が歩き出す。
            Advance(0f);
            return true;
        }

        /// <summary>探索を 1 Tick 進める（Update から呼ばれるが、テストは決定的に直接呼べる）。</summary>
        public void TickInvestigation(float deltaTime)
        {
            if (_request == null)
            {
                return;
            }

            CompanionActivity activity = CompanionActivityProvider.Activity;

            // 会話・イベント：中断して表示と参照を解放する（§6.4）。
            if (activity.DiscardOngoing)
            {
                Abort(InvestigationInterruptReason.EventStarted);
                return;
            }

            // Pause：進行・入力を凍結。所有権と依頼は保持し、Resume で同じ依頼を続ける（§6.4、E13）。
            if (!activity.ClocksRun)
            {
                return;
            }

            // 戦闘開始・Wave 幕間：直ちに中断（§6.4）。完了確定と同じ Tick に競合したら戦闘開始が勝つ（E10）。
            if (!activity.CanInvestigate)
            {
                Abort(InvestigationInterruptReason.CombatStarted);
                return;
            }

            // 地点が消えた・退場した・加入資格が消えた（後者は調停役の完了再検査でも見る）。
            if (!_request.Succeeded && !_request.PointIsAlive)
            {
                Abort(InvestigationInterruptReason.PointLost);
                return;
            }

            if (Mode == InvestigationMode.Body && (_actor == null || _actor.IsAway))
            {
                Abort(InvestigationInterruptReason.Disabled);
                return;
            }

            Advance(deltaTime);
        }

        /// <summary>
        /// 明示的な戦闘開始要求（P4-08R の試遊開始操作）。依頼を同期的に中断し、表示と所有権を解放する。
        /// 呼び出し側はこのあとで敵生成／攻撃許可へ進む（v1.0 §7.1「探索中断 → 代理の解放 → 敵生成」）。
        /// </summary>
        public void NotifyCombatStart()
        {
            if (_request != null)
            {
                Abort(InvestigationInterruptReason.CombatStarted);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// 本体で調べている最中に行動を奪われた（戦闘本体への実命中・主人公被弾からの守護評価・Down・退場）。
        /// <b>同じ呼び出しの中で</b>依頼を中断し、移動の所有権を返す。状態は奪った側のもので、ここでは触らない
        /// （まだ Investigate のままなら追従へ戻す）。表示代理で調べている間は本体の行動を持っていないので、ここへは来ない。
        /// </remarks>
        public void OnActionInterrupted(in CompanionActionHandle lost)
        {
            if (_request == null || Mode != InvestigationMode.Body)
            {
                return;
            }

            if (!lost.IsValid || lost.Owner != CompanionActionOwner.Investigate || lost.RunId != _action.RunId)
            {
                return;
            }

            _action = default; // 券はもう無効。Release／TryComplete で古い券を使わない。
            Abort(InvestigationInterruptReason.CompanionHit);
        }

        // ---- 内部 ----

        private void Advance(float deltaTime)
        {
            Vector3 self = Mode == InvestigationMode.Proxy ? ProxyPosition : _actor.WorldPosition;
            Vector3 playerPosition = _player != null ? _player.Position : self;

            InvestigationStep step = _run.Tick(deltaTime, self, playerPosition, out InvestigationInterruptReason reason);

            switch (step)
            {
                case InvestigationStep.Interrupted:
                    Abort(reason);
                    return;

                case InvestigationStep.ReadyToComplete:
                    Complete();
                    return;

                case InvestigationStep.Finished:
                    Finish();
                    return;
            }

            // 進行中：形態ごとの移動。
            switch (_run.Phase)
            {
                case InvestigationPhase.Moving:
                    MoveToward(_request.ApproachPosition, deltaTime, out bool blocked);
                    if (blocked)
                    {
                        Abort(InvestigationInterruptReason.Blocked);
                    }

                    break;

                case InvestigationPhase.Investigating:
                    HoldFacing(_request.ApproachFacing);
                    break;

                case InvestigationPhase.Returning:
                    // 表示代理の帰還（本体は帰還段を持たず、完了と同時に追従へ戻る）。
                    Vector3 home = Home();
                    MoveToward(home, deltaTime, out _);
                    if (FormationSlot.HorizontalDistance(ProxyPosition, home) <= _request.Settings.ArrivalDistance)
                    {
                        Finish();
                    }

                    break;
            }
        }

        private void MoveToward(Vector3 target, float deltaTime, out bool blocked)
        {
            blocked = false;
            float speed = _actor != null && _actor.Data != null ? _actor.Data.MoveSpeed : 0f;

            if (Mode == InvestigationMode.Body)
            {
                _arbiter?.Submit(
                    CompanionMovementOwner.Investigate,
                    CompanionMoveRequest.MoveFacing(target, speed, _request.Settings.ArrivalDistance, target - _actor.WorldPosition));
                return;
            }

            // 表示代理：直線で進める。遮られたら壁抜けせず中断する（§6.2）。
            Vector3 from = ProxyPosition;
            Vector3 delta = target - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 1e-4f)
            {
                return;
            }

            float stepLength = Mathf.Min(distance, speed * Mathf.Max(0f, deltaTime));
            Vector3 next = from + delta / distance * stepLength;
            if (stepLength > 0f && _probe != null && !_probe.IsClear(from, next))
            {
                blocked = true;
                return;
            }

            ProxyPosition = next;
            ProxyFacing = Face(delta);
        }

        private void HoldFacing(Vector3 facing)
        {
            if (Mode == InvestigationMode.Body)
            {
                _arbiter?.Submit(CompanionMovementOwner.Investigate, CompanionMoveRequest.StopFacing(facing));
                return;
            }

            ProxyFacing = Face(facing);
        }

        private Vector3 Home()
        {
            if (_actor != null && !_actor.IsAway)
            {
                return _actor.WorldPosition;
            }

            return _player != null ? _player.Position : ProxyPosition;
        }

        /// <summary>調査時間が満了した。調停役に完了確定を頼み、成功なら帰還へ、失敗なら未完了のまま中断する。</summary>
        private void Complete()
        {
            InvestigationRequest request = _request;
            InvestigationInterruptReason failure = InvestigationInterruptReason.CompletionRejected;
            if (_sink == null || !_sink.TryConfirmCompletion(this, request, out failure))
            {
                Abort(failure == InvestigationInterruptReason.None ? InvestigationInterruptReason.CompletionRejected : failure);
                return;
            }

            CompletedCount++;

            if (Mode == InvestigationMode.Body)
            {
                // 本体は帰還段を持たない。行動を正常終了して追従へ戻し、以後は通常どおり再判断する。
                Finish();
                return;
            }

            _run.EnterReturning();
        }

        /// <summary>未完了のまま中断する（成功後の帰還中は理由通知を出さずに撤収するだけ。§6.3）。</summary>
        private void Abort(InvestigationInterruptReason reason)
        {
            InvestigationRequest request = _request;
            if (request == null)
            {
                return;
            }

            bool wasSucceeded = request.Succeeded;
            request.MarkInterrupted(reason);
            if (!wasSucceeded)
            {
                InterruptedCount++;
                LastInterruptReason = reason;
            }

            IInvestigationOutcomeSink sink = _sink;
            ReleaseAll();

            if (!wasSucceeded)
            {
                sink?.OnInterrupted(this, request, reason);
            }
        }

        /// <summary>正常に引き渡しを終える。</summary>
        private void Finish()
        {
            ReleaseAll();
        }

        /// <summary>所有権・表示・参照をすべて解放する（成功・中断・無効化で共通。冪等）。</summary>
        private void ReleaseAll()
        {
            if (Mode == InvestigationMode.Body)
            {
                _arbiter?.Release(CompanionMovementOwner.Investigate);

                if (_states != null)
                {
                    if (_states.IsCurrent(_action))
                    {
                        _states.TryComplete(_action, CompanionState.Follow, CompanionStateChangeReason.InvestigationFinished);
                    }
                    else if (_actor != null && _actor.State == CompanionState.Investigate)
                    {
                        // 券は奪われたが状態が Investigate のまま（実命中で所有権だけ解放された場合）。追従へ戻す。
                        _states.ForceRecover(CompanionState.Follow, CompanionStateChangeReason.InvestigationFinished);
                    }
                }
            }

            _action = default;
            _request = null;
            _run = null;
            _sink = null;
            _player = null;
            Mode = InvestigationMode.None;
            ProxyVisible = false;
        }

        private static Vector3 Face(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        }

        private void EnsureRefs()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }

            if (_states == null)
            {
                _states = GetComponent<CompanionStateArbiter>();
            }

            if (_arbiter == null)
            {
                _arbiter = GetComponent<CompanionMovementArbiter>();
            }

            if (_probe == null)
            {
                _probe = new PhysicsObstacleProbe();
            }

            if (isActiveAndEnabled)
            {
                _states?.RegisterParticipant(CompanionActionOwner.Investigate, this); // 冪等。EditMode は OnEnable を呼ばない。
            }
        }

        private void Update()
        {
            EnsureRefs();
            TickInvestigation(Time.deltaTime);
        }

        private void OnEnable()
        {
            EnsureRefs();
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱・Retry：終了処理を 1 回行い、すべて解放する（§6.4）。
            if (_request != null)
            {
                Abort(InvestigationInterruptReason.Disabled);
            }

            _states?.UnregisterParticipant(this);
        }
    }
}
