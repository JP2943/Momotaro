namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 本編型死亡再開の受付と一度限りの保証を担う純粋な調停役（P5-08。仕様書 v1.1 §9.1）。
    /// UnityEngine に依存しないので EditMode で決定的に検証できる。
    ///
    /// <b>この型の要は「再開要求 ID」ひとつ。</b> 死亡を受理した時点で ID を 1 つ発行し、
    /// 読込に失敗して再試行しても<b>同じ ID のまま</b>にする。再出現周期の更新と外部への通知は
    /// この ID につき一度だけ許す（§9.1 末尾「読込失敗やボタン連打で何度も更新しない」）。
    ///
    /// ID を要求のたびに発行する形にすると、再試行のたびに周期が進んで
    /// <b>倒したはずの敵が増えていく</b>。失敗は「やり直し」であって「次の死」ではない。
    /// </summary>
    public sealed class CampaignRespawnCoordinator
    {
        private int _requestSeq;
        private int _advancedRequestId;

        /// <summary>現在の段階。</summary>
        public CampaignRespawnPhase Phase { get; private set; } = CampaignRespawnPhase.Idle;

        /// <summary>現在の再開要求 ID。0 は「死亡していない」。</summary>
        public int CurrentRequestId { get; private set; }

        /// <summary>再開操作を待っているか。</summary>
        public bool IsAwaitingRespawn =>
            Phase == CampaignRespawnPhase.Dead || Phase == CampaignRespawnPhase.Failed;

        /// <summary>死亡を受理した回数。<b>同じ死で 2 回数えない</b>（§9.1 手順 1「一度だけ受理」）。</summary>
        public int DefeatCount { get; private set; }

        /// <summary>受理した再開要求の数。</summary>
        public int AcceptedCount { get; private set; }

        /// <summary>断った再開要求の数（連打・二重押下の診断）。</summary>
        public int RejectedCount { get; private set; }

        /// <summary>再出現周期を進めた回数。<b>再開要求 1 件につき 1 を超えてはいけない</b>。</summary>
        public int AdvanceCount { get; private set; }

        /// <summary>読込失敗で再開画面へ戻った回数。</summary>
        public int FailureCount { get; private set; }

        /// <summary>再開を完了した回数。</summary>
        public int CompletedCount { get; private set; }

        /// <summary>世代不一致で無視した通知の数（診断・テスト用）。</summary>
        public int StaleNotificationCount { get; private set; }

        /// <summary>
        /// 主人公の死亡を受理する（§9.1 手順 1）。すでに死亡中なら何もしない。
        /// </summary>
        /// <returns>この呼び出しで受理したら true。</returns>
        public bool NotifyPlayerDefeated()
        {
            if (Phase != CampaignRespawnPhase.Idle)
            {
                // 追撃・複数の通知経路で二重に受理しない。GameOver 表示も二度出さない。
                StaleNotificationCount++;
                return false;
            }

            _requestSeq++;
            CurrentRequestId = _requestSeq;
            Phase = CampaignRespawnPhase.Dead;
            DefeatCount++;
            return true;
        }

        /// <summary>
        /// 再開を要求する（§9.1 手順 3）。死亡待ち・再試行待ちのときだけ受理する。
        /// 受理しても ID は変わらない。
        /// </summary>
        public RespawnDecision TryRequest()
        {
            if (Phase == CampaignRespawnPhase.Idle)
            {
                RejectedCount++;
                return RespawnDecision.Reject(RespawnRejection.NotDead);
            }

            if (Phase == CampaignRespawnPhase.Requested)
            {
                RejectedCount++;
                return RespawnDecision.Reject(RespawnRejection.AlreadyRequested);
            }

            Phase = CampaignRespawnPhase.Requested;
            AcceptedCount++;
            return RespawnDecision.Accept(CurrentRequestId);
        }

        /// <summary>
        /// この要求でまだ再出現周期を進めていなければ true を返し、以後は同じ ID で false を返す
        /// （§9.1 末尾）。呼び出し側はこれが true のときだけ
        /// <see cref="GameSessionState.AdvanceRespawnCycle"/> を呼ぶ。
        /// </summary>
        public bool TryConsumeCycleAdvance(int requestId)
        {
            if (requestId == 0 || requestId != CurrentRequestId || requestId == _advancedRequestId)
            {
                return false;
            }

            _advancedRequestId = requestId;
            AdvanceCount++;
            return true;
        }

        /// <summary>
        /// 読込・遷移に失敗したことを通知する。再開画面へ戻して再試行できるようにする（§9.1 末尾）。
        /// <b>周期は戻さない。</b> 進めた事実は残り、再試行でもう一度進めることはない。
        /// </summary>
        public bool NotifyFailed(int requestId)
        {
            if (requestId != CurrentRequestId || Phase != CampaignRespawnPhase.Requested)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = CampaignRespawnPhase.Failed;
            FailureCount++;
            return true;
        }

        /// <summary>再開地点へ到着したことを通知する（§9.1 手順 7）。</summary>
        public bool NotifyArrived(int requestId)
        {
            if (requestId != CurrentRequestId || Phase != CampaignRespawnPhase.Requested)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = CampaignRespawnPhase.Idle;
            CurrentRequestId = 0;
            CompletedCount++;
            return true;
        }
    }
}
