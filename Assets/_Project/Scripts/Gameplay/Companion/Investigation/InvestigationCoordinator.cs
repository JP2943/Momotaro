using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>1 回の依頼の結果（入力仲介と表示が読む）。</summary>
    public readonly struct InvestigationRequestResult
    {
        public bool Accepted { get; }
        public int RequestId { get; }
        public InvestigationRejectReason Reason { get; }
        public IInvestigationPoint Point { get; }

        public InvestigationRequestResult(bool accepted, int requestId, InvestigationRejectReason reason, IInvestigationPoint point)
        {
            Accepted = accepted;
            RequestId = requestId;
            Reason = reason;
            Point = point;
        }
    }

    /// <summary>
    /// 探索依頼の調停役（P4-07A。v1.0 §4・§6.3・§12「探索地点・依頼・調査記録」）。Scene に 1 つ置く。
    ///
    /// 主人公の Interact 1 回につき <see cref="TryRequest"/> を 1 回呼ぶ（入力仲介は Infrastructure）。ここで受付条件（§4.3）を
    /// 順に検査し、対象地点を 1 件に決め（<see cref="InvestigationTargetSelector"/>）、地点が要求する仲間の駆動へ依頼を渡す。
    /// 完了確定は <see cref="TryConfirmCompletion"/> で「依頼の有効性を再検査 → Scene 記録へ登録 → 成功終端化 → 完了通知」の
    /// 順に固定する（§6.3）。同一地点の再入・通知購読側からの再要求でも成功は 1 回。
    ///
    /// 供給元（主人公・加入資格・記録・仲間の駆動）はすべて Scene の明示参照で注入する（万能 static にしない）。
    /// 欠けていれば未配線として拒否し、Validator が検出する（§5.1）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvestigationCoordinator : MonoBehaviour, IInvestigationOutcomeSink, IInvestigationReachability,
        IIncomingHitObserver
    {
        [Tooltip("主人公（位置・向き・いま Interact を実行できる状態か）。")]
        [SerializeField] private PlayerStateController _player;

        [Tooltip("加入資格の供給元。")]
        [SerializeField] private CompanionRosterContext _roster;

        [Tooltip("Scene 単位の調査済み記録。")]
        [SerializeField] private InvestigationRecordHolder _record;

        [Tooltip("依頼を受けられる仲間の探索駆動（地点の RequiredCompanion と StableId で照合する）。")]
        [SerializeField] private CompanionInvestigationController[] _companions = System.Array.Empty<CompanionInvestigationController>();

        private readonly List<IInvestigationPoint> _buffer = new List<IInvestigationPoint>();
        private IObstacleProbe _probe;
        private IInteractActor _playerOverride;
        private int _nextRequestId;
        private IIncomingHitSource _hitSource;       // 主人公の被弾入口（実命中で探索を解放するための購読先。R3-02）。
        private bool _hitSourceOverridden;           // テストが明示注入した（自動解決で上書きしない）。

        /// <summary>依頼を出す側（Scene の主人公、またはテストが注入した Fake）。</summary>
        private IInteractActor Interactor => _playerOverride ?? (_player != null ? _player : null);

        /// <summary>探索の型付き通知（HUD・マーカー・後続の効果受け手が購読する）。</summary>
        public InvestigationChannel Events { get; } = new InvestigationChannel();

        /// <summary>直近に発行した依頼 ID（0 なら未発行）。</summary>
        public int LastRequestId { get; private set; }

        /// <summary>直近の拒否理由（None なら受理）。テスト・診断用。</summary>
        public InvestigationRejectReason LastRejectReason { get; private set; }

        /// <summary>直近に選ばれた（または拒否の文脈になった）地点。</summary>
        public IInvestigationPoint LastPoint { get; private set; }

        /// <summary>供給元がすべて配線されているか（Scene 検査・診断用）。</summary>
        public bool IsWired =>
            Interactor != null && _roster != null && _record != null && _companions != null && _companions.Length > 0;

        /// <summary>主人公（Scene 検査用）。</summary>
        public PlayerStateController Player => _player;

        /// <summary>加入資格の供給元（Scene 検査用）。</summary>
        public CompanionRosterContext Roster => _roster;

        /// <summary>記録の保持先（Scene 検査用）。</summary>
        public InvestigationRecordHolder RecordHolder => _record;

        /// <summary>配線された仲間の駆動（Scene 検査用）。</summary>
        public IReadOnlyList<CompanionInvestigationController> Companions => _companions;

        /// <summary>いずれかの仲間が依頼を実行中か。</summary>
        public bool AnyBusy
        {
            get
            {
                for (int i = 0; i < _companions.Length; i++)
                {
                    if (_companions[i] != null && _companions[i].IsBusy)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>供給元を注入する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(
            PlayerStateController player, CompanionRosterContext roster, InvestigationRecordHolder record,
            params CompanionInvestigationController[] companions)
        {
            if (player != null)
            {
                _player = player;
            }

            if (roster != null)
            {
                _roster = roster;
            }

            if (record != null)
            {
                _record = record;
            }

            if (companions != null && companions.Length > 0)
            {
                _companions = companions;
            }

            SubscribeToPlayerHits(); // 主人公が後から差し替わった構成でも購読を張り直す。
        }

        /// <summary>依頼を出す側を差し替える（テストが Fake の主人公を注入する。null で Scene の主人公へ戻す）。</summary>
        public void SetInteractor(IInteractActor interactor)
        {
            _playerOverride = interactor;
        }

        /// <summary>障害物判定を差し替える（テスト用。未設定なら壁レイヤーへの物理判定）。</summary>
        public void SetObstacleProbe(IObstacleProbe probe)
        {
            _probe = probe;
            for (int i = 0; i < _companions.Length; i++)
            {
                _companions[i]?.SetObstacleProbe(probe);
            }
        }

        /// <summary>
        /// 依頼を出さずに「いま Interact したらどうなるか」を見る（「調べる」表示のため）。副作用なし。
        /// </summary>
        public InvestigationRejectReason Peek(out IInvestigationPoint point)
        {
            return Evaluate(out point, out _);
        }

        /// <summary>
        /// Interact 1 回に対する依頼。押下 1 回で 1 依頼（連続実行しない）。受理・拒否のどちらも通知を 1 回出す。
        /// </summary>
        public InvestigationRequestResult TryRequest()
        {
            InvestigationRejectReason reason = Evaluate(out IInvestigationPoint point, out CompanionInvestigationController driver);
            LastPoint = point;

            if (reason != InvestigationRejectReason.None)
            {
                return Reject(point, reason);
            }

            int requestId = _nextRequestId + 1;
            if (!driver.TryBegin(point, requestId, this, Interactor, out InvestigationRequest request, out reason))
            {
                return Reject(point, reason);
            }

            _nextRequestId = requestId;
            LastRequestId = requestId;
            LastRejectReason = InvestigationRejectReason.None;
            Events.PublishAccepted(new InvestigationAccepted(requestId, request.PointId, request.CompanionId, request.PointPosition));
            return new InvestigationRequestResult(true, requestId, InvestigationRejectReason.None, point);
        }

        /// <summary>
        /// 明示的な戦闘開始（P4-08R）。実行中の依頼をすべて同期的に中断し、表示代理と所有権を解放する。
        /// 呼び出し側はこのあとで敵生成／攻撃許可へ進む（§7.1）。
        /// </summary>
        public void InterruptAllForCombat()
        {
            for (int i = 0; i < _companions.Length; i++)
            {
                _companions[i]?.NotifyCombatStart();
            }
        }

        /// <summary>
        /// 主人公の被弾入口を明示注入する（テスト・特殊な Scene 構成。null で自動解決へ戻す）。
        /// </summary>
        public void SetPlayerHitSource(IIncomingHitSource source)
        {
            UnsubscribeFromPlayerHits();
            _hitSourceOverridden = source != null;
            _hitSource = source;
            if (_hitSource != null && isActiveAndEnabled)
            {
                _hitSource.AddIncomingHitObserver(this);
            }
        }

        /// <summary>主人公の被弾入口を購読しているか（Scene 検査・診断用）。</summary>
        public bool IsSubscribedToPlayerHits => _hitSource != null;

        /// <inheritdoc />
        /// <remarks>
        /// 主人公へ実命中が届いた（解決の前。R3-02）。実行中の依頼を<b>本体・表示代理の別なく</b>同期解放する。
        /// 守護の資格・クールダウン・主人公側の防御分岐には依存しない。ここから戻ったあとで主人公の
        /// 無敵／Step／JG／ガード／守護／通常 Damage の既存解決が続く（＝解放は必ず先に終わっている）。
        /// </remarks>
        public void OnIncomingHit(in HitInfo hit)
        {
            InterruptAllForRealHit();
        }

        /// <summary>実命中による探索中断を全員へ配る（実命中の入口から呼ぶ。冪等）。</summary>
        public void InterruptAllForRealHit()
        {
            for (int i = 0; i < _companions.Length; i++)
            {
                _companions[i]?.NotifyRealHit();
            }
        }

        /// <inheritdoc />
        public bool TryConfirmCompletion(
            CompanionInvestigationController driver, InvestigationRequest request, out InvestigationInterruptReason failure)
        {
            failure = InvestigationInterruptReason.None;

            // 1. 依頼の有効性を再検査（古い世代・地点消失・資格消失・戦闘開始）。
            if (driver == null || request == null || !ReferenceEquals(driver.CurrentRequest, request)
                || request.Generation != driver.Generation || request.Terminated)
            {
                failure = InvestigationInterruptReason.CompletionRejected; // 中断済みの古い依頼からの完了は無視（E12）。
                return false;
            }

            if (!request.PointIsAlive)
            {
                failure = InvestigationInterruptReason.PointLost;
                return false;
            }

            if (_roster == null || !_roster.IsRecruited(request.CompanionId))
            {
                failure = InvestigationInterruptReason.RecruitLost;
                return false;
            }

            CompanionActivity activity = CompanionActivityProvider.Activity;
            if (!activity.CanInvestigate)
            {
                failure = InvestigationInterruptReason.CombatStarted; // 同じ Tick の競合は戦闘開始が優先（E10）。
                return false;
            }

            // 2. Scene 記録へ調査済みとして登録（同一地点の完了は 1 回だけ）。
            if (_record == null || !_record.TryMarkInvestigated(request.PointId))
            {
                failure = InvestigationInterruptReason.CompletionRejected;
                return false;
            }

            // 3. 内部依頼を成功終端化 → 4. 完了通知（購読者の数は完了回数に数えない。未配線でも記録は残る）。
            request.MarkSucceeded();
            Events.PublishCompleted(new InvestigationCompleted(
                request.PointId, request.RequestId, request.CompanionId, request.DiscoveryId, request.PointPosition));
            return true;
        }

        /// <inheritdoc />
        public void OnInterrupted(
            CompanionInvestigationController driver, InvestigationRequest request, InvestigationInterruptReason reason)
        {
            if (request == null)
            {
                return;
            }

            Events.PublishInterrupted(new InvestigationInterrupted(request.RequestId, request.PointId, reason));
        }

        /// <inheritdoc />
        /// <remarks>主人公付近の出現位置から調査位置まで、直線で遮られていないこと（§6.2）。</remarks>
        public bool IsReachable(IInvestigationPoint point)
        {
            IInteractActor player = Interactor;
            if (point == null || player == null)
            {
                return false;
            }

            EnsureProbe();
            return _probe.IsClear(player.Position, point.ApproachPosition);
        }

        // ---- 内部 ----

        /// <summary>受付条件（§4.3）を順に検査する。受理できるなら None と対象・駆動を返す。</summary>
        private InvestigationRejectReason Evaluate(out IInvestigationPoint point, out CompanionInvestigationController driver)
        {
            point = null;
            driver = null;

            if (!IsWired)
            {
                return InvestigationRejectReason.NotWired;
            }

            CompanionActivity activity = CompanionActivityProvider.Activity;
            if (!activity.ClocksRun || activity.DiscardOngoing)
            {
                return InvestigationRejectReason.InputClosed; // Pause／会話／イベント／Loading／未配線は新規入力を受け付けない。
            }

            if (!activity.CanInvestigate)
            {
                return InvestigationRejectReason.InCombat; // 戦闘中・Wave 幕間・開始待ち・結果画面。予約しない。
            }

            IInteractActor player = Interactor;
            if (!player.CanInteract)
            {
                return InvestigationRejectReason.PlayerBusy; // 攻撃・Step・Hurt・ガード中は受けない（先行入力予約もしない）。
            }

            InvestigationPointRegistry.CopyTo(_buffer);
            if (!InvestigationTargetSelector.TrySelect(
                    _buffer, player.Position, player.Forward, _record.Record, this,
                    out point, out InvestigationRejectReason selectReason, out IInvestigationPoint context))
            {
                point = context;
                return selectReason;
            }

            if (!_roster.IsRecruited(point.RequiredCompanion))
            {
                return InvestigationRejectReason.CompanionNotRecruited; // ヒントのみ。調査済みにしない。
            }

            driver = FindDriver(point.RequiredCompanion);
            if (driver == null)
            {
                return InvestigationRejectReason.NotWired;
            }

            InvestigationRejectReason accept = driver.CanAccept(out _);
            return accept;
        }

        private InvestigationRequestResult Reject(IInvestigationPoint point, InvestigationRejectReason reason)
        {
            LastRejectReason = reason;
            Events.PublishRejected(new InvestigationRejected(point != null ? point.PointId : default, reason, RejectText(point, reason)));
            return new InvestigationRequestResult(false, 0, reason, point);
        }

        private static string RejectText(IInvestigationPoint point, InvestigationRejectReason reason)
        {
            if (point == null)
            {
                return string.Empty;
            }

            switch (reason)
            {
                case InvestigationRejectReason.CompanionNotRecruited: return point.MissingCompanionHint;
                case InvestigationRejectReason.AlreadyInvestigated: return point.CompletedText;
                default: return string.Empty;
            }
        }

        private CompanionInvestigationController FindDriver(StableId companionId)
        {
            for (int i = 0; i < _companions.Length; i++)
            {
                CompanionInvestigationController c = _companions[i];
                if (c != null && c.CompanionId.Equals(companionId))
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>
        /// 主人公の被弾入口を解決して購読する（R3-02）。参照は既に配線されている主人公から辿るだけで、
        /// <c>Find*</c> も新しい直列化項目も増やさない。守護を持たない仲間・主人公でも成立する。
        /// </summary>
        private void SubscribeToPlayerHits()
        {
            if (_hitSourceOverridden || !isActiveAndEnabled)
            {
                return;
            }

            IIncomingHitSource resolved = _player != null ? _player.GetComponentInParent<IIncomingHitSource>() : null;
            if (ReferenceEquals(resolved, _hitSource))
            {
                if (_hitSource != null)
                {
                    _hitSource.AddIncomingHitObserver(this); // 冪等。OnEnable の再入でも二重にならない。
                }

                return;
            }

            UnsubscribeFromPlayerHits();
            _hitSource = resolved;
            _hitSource?.AddIncomingHitObserver(this);
        }

        private void UnsubscribeFromPlayerHits()
        {
            _hitSource?.RemoveIncomingHitObserver(this);
            _hitSource = null;
        }

        private void OnEnable()
        {
            if (_hitSourceOverridden)
            {
                _hitSource?.AddIncomingHitObserver(this);
                return;
            }

            SubscribeToPlayerHits();
        }

        private void OnDisable()
        {
            // 購読は OnEnable／OnDisable 対称（§2.3 後始末）。明示注入は保持し、購読だけ外す。
            _hitSource?.RemoveIncomingHitObserver(this);
            if (!_hitSourceOverridden)
            {
                _hitSource = null;
            }
        }

        private void EnsureProbe()
        {
            if (_probe == null)
            {
                _probe = new PhysicsObstacleProbe();
                for (int i = 0; i < _companions.Length; i++)
                {
                    _companions[i]?.SetObstacleProbe(_probe);
                }
            }
        }
    }
}
