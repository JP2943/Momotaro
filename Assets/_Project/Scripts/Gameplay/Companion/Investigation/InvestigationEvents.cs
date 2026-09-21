using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>依頼を受け付けなかった理由（v1.0 §4.3 の受付条件。理由を識別できる通知＝§3.1-4）。</summary>
    public enum InvestigationRejectReason
    {
        None = 0,

        /// <summary>供給元（加入資格・記録・仲間・主人公）が未配線。Validator で検出する対象。</summary>
        NotWired = 1,

        /// <summary>Pause／会話／イベント／Loading／結果画面：新規入力を受け付けない。</summary>
        InputClosed = 2,

        /// <summary>戦闘中・Wave 幕間・開始待ちの Encounter。予約もしない。</summary>
        InCombat = 3,

        /// <summary>主人公が攻撃・Step・Hurt 等の実行中（通常の移動／待機からだけ始める）。</summary>
        PlayerBusy = 4,

        /// <summary>範囲内に地点が無い。</summary>
        NoPointInRange = 5,

        /// <summary>最寄りの地点は既に調査済み。発見通知を再発行しない。</summary>
        AlreadyInvestigated = 6,

        /// <summary>地点が要求する仲間が未加入（ヒントのみ表示）。</summary>
        CompanionNotRecruited = 7,

        /// <summary>その仲間は別の調査を実行中（現在の依頼を維持。列は持たない）。</summary>
        CompanionBusy = 8,

        /// <summary>壁越しなどで調査位置へ到達できない。</summary>
        Unreachable = 9,

        /// <summary>地点が無効・参照欠落・設定不正。</summary>
        PointUnavailable = 10,

        /// <summary>同一依頼の再送（同じ RequestId の結果を再利用し、二重起動しない）。</summary>
        DuplicateRequest = 11,

        /// <summary>仲間の戦闘 Actor が探索を始められない状態（攻撃・防御・ひるみ中）。</summary>
        CompanionNotReady = 12,
    }

    /// <summary>未完了の依頼を中断した理由（v1.0 §6.4）。</summary>
    public enum InvestigationInterruptReason
    {
        None = 0,

        /// <summary>戦闘開始・Wave 幕間へ入った（明示の戦闘開始要求を含む）。</summary>
        CombatStarted = 1,

        /// <summary>
        /// 実命中で解放された。仲間の戦闘本体への直撃と、主人公への命中の<b>どちらも</b>この理由になる（R3-02）。
        /// 主人公側は防がれた命中（Guard／JG／有効 Step／無敵）でも解放する。届いたこと自体は変わらないため。
        /// </summary>
        CompanionHit = 2,

        /// <summary>主人公が地点から継続範囲外へ離れた。</summary>
        PlayerLeftRange = 3,

        /// <summary>地点が Disable／破棄された。</summary>
        PointLost = 4,

        /// <summary>加入資格が消えた。</summary>
        RecruitLost = 5,

        /// <summary>移動時間の上限を超えた。</summary>
        MoveTimeout = 6,

        /// <summary>移動中に遮られた（壁抜けで成功させない）。</summary>
        Blocked = 7,

        /// <summary>会話・イベントへ入った。</summary>
        EventStarted = 8,

        /// <summary>無効化・Scene 離脱・Retry。</summary>
        Disabled = 9,

        /// <summary>完了確定時の再検査に失敗した（地点・資格・戦闘状態のいずれか）。</summary>
        CompletionRejected = 10,
    }

    /// <summary>依頼の進行状態（v1.0 §6.1）。完了確定は同期処理で、待機状態ではない。</summary>
    public enum InvestigationPhase
    {
        Idle = 0,
        Moving = 1,
        Investigating = 2,
        Returning = 3,
    }

    /// <summary>受付成功の通知。</summary>
    public readonly struct InvestigationAccepted
    {
        public int RequestId { get; }
        public StableId PointId { get; }
        public StableId CompanionId { get; }
        public Vector3 Position { get; }

        public InvestigationAccepted(int requestId, StableId pointId, StableId companionId, Vector3 position)
        {
            RequestId = requestId;
            PointId = pointId;
            CompanionId = companionId;
            Position = position;
        }
    }

    /// <summary>受付拒否の通知（理由付き。地点が定まらない拒否では PointId は空）。</summary>
    public readonly struct InvestigationRejected
    {
        public StableId PointId { get; }
        public InvestigationRejectReason Reason { get; }

        /// <summary>表示用の短文（未加入ヒント・調査済み文言。地点が無ければ空）。</summary>
        public string Text { get; }

        public InvestigationRejected(StableId pointId, InvestigationRejectReason reason, string text)
        {
            PointId = pointId;
            Reason = reason;
            Text = text ?? string.Empty;
        }
    }

    /// <summary>
    /// 完了通知（v1.0 §6.3「PointId / RequestId / CompanionId / DiscoveryId / Position 程度の不変データ」）。
    /// SO 原本は渡さない。
    /// </summary>
    public readonly struct InvestigationCompleted
    {
        public StableId PointId { get; }
        public int RequestId { get; }
        public StableId CompanionId { get; }
        public StableId DiscoveryId { get; }
        public Vector3 Position { get; }

        public InvestigationCompleted(
            StableId pointId, int requestId, StableId companionId, StableId discoveryId, Vector3 position)
        {
            PointId = pointId;
            RequestId = requestId;
            CompanionId = companionId;
            DiscoveryId = discoveryId;
            Position = position;
        }
    }

    /// <summary>未完了のまま中断した通知（成功後の帰還中断では発行しない。§6.3）。</summary>
    public readonly struct InvestigationInterrupted
    {
        public int RequestId { get; }
        public StableId PointId { get; }
        public InvestigationInterruptReason Reason { get; }

        public InvestigationInterrupted(int requestId, StableId pointId, InvestigationInterruptReason reason)
        {
            RequestId = requestId;
            PointId = pointId;
            Reason = reason;
        }
    }

    /// <summary>探索通知の購読者（HUD・地点マーカー・後続の効果受け手）。</summary>
    public interface IInvestigationListener
    {
        void OnInvestigationAccepted(in InvestigationAccepted accepted);
        void OnInvestigationRejected(in InvestigationRejected rejected);
        void OnInvestigationCompleted(in InvestigationCompleted completed);
        void OnInvestigationInterrupted(in InvestigationInterrupted interrupted);
    }

    /// <summary>
    /// 探索の型付き通知チャネル（v1.0 §11「HitResultKind を増やさず専用の型付き通知」）。
    /// 購読者の数を完了の回数に数えない（§6.3）。通知先が 0 でも調査記録は正常に残る。
    /// </summary>
    public sealed class InvestigationChannel
    {
        private readonly List<IInvestigationListener> _listeners = new List<IInvestigationListener>();

        public int ListenerCount => _listeners.Count;

        public void AddListener(IInvestigationListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        public void RemoveListener(IInvestigationListener listener)
        {
            if (listener != null)
            {
                _listeners.Remove(listener);
            }
        }

        public void PublishAccepted(in InvestigationAccepted e)
        {
            IInvestigationListener[] snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].OnInvestigationAccepted(e);
        }

        public void PublishRejected(in InvestigationRejected e)
        {
            IInvestigationListener[] snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].OnInvestigationRejected(e);
        }

        public void PublishCompleted(in InvestigationCompleted e)
        {
            IInvestigationListener[] snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].OnInvestigationCompleted(e);
        }

        public void PublishInterrupted(in InvestigationInterrupted e)
        {
            IInvestigationListener[] snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].OnInvestigationInterrupted(e);
        }

        /// <summary>通知の途中で購読が変わっても壊れないよう、写しを回す。</summary>
        private IInvestigationListener[] Snapshot()
        {
            return _listeners.Count == 0 ? System.Array.Empty<IInvestigationListener>() : _listeners.ToArray();
        }
    }
}
