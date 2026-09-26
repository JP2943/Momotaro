namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// スライド遷移トランザクションの段階（P5.5 仕様書 §6.2）。
    ///
    /// <b>成功確定点は <see cref="Committed"/> への 1 回だけ</b>（§6.2「成功確定点は 8〜9 の同期区間」）。
    /// そこより前は、何が終わっていても「まだ着いていない」。
    /// </summary>
    public enum AreaSlideTransactionPhase
    {
        /// <summary>遷移していない。</summary>
        Idle = 0,

        /// <summary>受理した。出発側を止め、Snapshot を採る段階（手順 2〜3）。</summary>
        Accepted = 1,

        /// <summary>到着側を隔離された Prepared へ持っていく段階（手順 4〜5）。</summary>
        Preparing = 2,

        /// <summary>準備が済み、スライドを始められる（手順 5 完了）。</summary>
        Ready = 3,

        /// <summary>スライド中（手順 6）。</summary>
        Sliding = 4,

        /// <summary>成功が確定した（手順 8〜9）。</summary>
        Committed = 5,

        /// <summary>失敗して出発側へ戻している最中（§8）。</summary>
        RollingBack = 6,

        /// <summary>戻り切れなかった。Error 表示に留める（§8 最終行）。</summary>
        Failed = 7,
    }

    /// <summary>
    /// スライド遷移の調停役（P5.5 仕様書 §6／§11 の E02・E03・E06・E08）。
    ///
    /// <b>判断だけを持ち、Scene API に触らない</b>（§9「純粋な遷移判断を Scene API に依存させない」）。
    /// Additive ロード・カメラ・表示代理は Infrastructure／Presentation の担当で、ここは
    /// 「受け付けてよいか」「今どの段階か」「その通知は今の世代のものか」「成功を確定してよいか」だけを決める。
    ///
    /// <b>世代（TransitionId）で古い通知を落とす。</b> P5.5 は 2 つの Area が同時に存在し、
    /// 遅れて届くロード完了・解除通知が必ず出る。世代を照合しないと、撤去済みの遷移の完了が
    /// 新しい遷移を壊す（§8「遅延完了は世代・Scene handle を照合し、訪問登録・CurrentArea 更新・
    /// 活動開始・成功通知を一切起こさない」）。
    /// </summary>
    public sealed class AreaSlideCoordinator
    {
        private AreaConnectionSnapshot _connection;
        private int _committedTransitionId;
        private int _announcedTransitionId;

        /// <summary>現在の段階。</summary>
        public AreaSlideTransactionPhase Phase { get; private set; } = AreaSlideTransactionPhase.Idle;

        /// <summary>現在の遷移の世代。0 は遷移していない。</summary>
        public int CurrentTransitionId { get; private set; }

        /// <summary>受理時に固定した接続（走っている遷移はこれだけを見る。§3.1 末尾）。</summary>
        public AreaConnectionSnapshot Connection => _connection;

        /// <summary>
        /// 遷移中か。<b>Failed は含めない</b>——戻れなかった状態で次の要求まで塞ぐと、
        /// プレイヤーに手が無くなる（P5 の調停役と同じ考え方）。
        /// </summary>
        public bool IsTransitioning =>
            Phase != AreaSlideTransactionPhase.Idle
            && Phase != AreaSlideTransactionPhase.Failed
            && Phase != AreaSlideTransactionPhase.Committed;

        /// <summary>受理した回数（診断・テスト用）。</summary>
        public int AcceptedCount { get; private set; }

        /// <summary>成功を確定した回数（診断・テスト用）。</summary>
        public int CommittedCount { get; private set; }

        /// <summary>戻した回数（診断・テスト用）。</summary>
        public int RolledBackCount { get; private set; }

        /// <summary>戻れなかった回数（診断・テスト用）。</summary>
        public int FailedCount { get; private set; }

        /// <summary>世代違い・段階違いで無視した通知の数（診断・テスト用）。</summary>
        public int StaleNotificationCount { get; private set; }

        /// <summary>直近の拒否理由（診断・テスト用）。</summary>
        public AreaTransitionRejection LastRejection { get; private set; } = AreaTransitionRejection.None;

        /// <summary>その世代が現行か。</summary>
        public bool IsCurrent(int transitionId) =>
            transitionId != 0 && transitionId == CurrentTransitionId;

        /// <summary>
        /// 遷移を要求する（§6.1）。
        ///
        /// 受付条件は <see cref="AreaTransitionAdmission"/> が正本で、ここは<b>排他だけ</b>を足す。
        /// <b>先読み中であることは拒否理由にしない</b>（§6.1 末尾）——先読みは操作を止めない仕組みで、
        /// それを理由に移動を断ると「近づくと動けなくなる」になる。
        /// </summary>
        public AreaTransitionDecision TryRequest(
            in AreaConnectionSnapshot connection, IAreaTransitionConditions conditions)
        {
            if (IsTransitioning)
            {
                LastRejection = AreaTransitionRejection.AlreadyTransitioning;
                return AreaTransitionDecision.Reject(AreaTransitionRejection.AlreadyTransitioning);
            }

            if (!connection.ConnectionId.IsValid
                || !connection.ToAreaId.IsValid || !connection.EntryId.IsValid)
            {
                LastRejection = AreaTransitionRejection.UnknownDestination;
                return AreaTransitionDecision.Reject(AreaTransitionRejection.UnknownDestination);
            }

            AreaTransitionRejection rejection = AreaTransitionAdmission.Evaluate(conditions, isRespawn: false);
            if (rejection != AreaTransitionRejection.None)
            {
                LastRejection = rejection;
                return AreaTransitionDecision.Reject(rejection);
            }

            CurrentTransitionId++;
            Phase = AreaSlideTransactionPhase.Accepted;
            _connection = connection;
            AcceptedCount++;
            LastRejection = AreaTransitionRejection.None;
            return AreaTransitionDecision.Accept(CurrentTransitionId);
        }

        /// <summary>到着側の準備を始めた（手順 4）。</summary>
        public bool NotifyPreparing(int transitionId) =>
            Advance(transitionId, AreaSlideTransactionPhase.Accepted, AreaSlideTransactionPhase.Preparing);

        /// <summary>到着側が隔離された Prepared になった（手順 5 完了）。</summary>
        public bool NotifyPrepared(int transitionId) =>
            Advance(transitionId, AreaSlideTransactionPhase.Preparing, AreaSlideTransactionPhase.Ready);

        /// <summary>スライドを始めた（手順 6）。</summary>
        public bool NotifySlideStarted(int transitionId) =>
            Advance(transitionId, AreaSlideTransactionPhase.Ready, AreaSlideTransactionPhase.Sliding);

        /// <summary>
        /// 成功を確定する（手順 8〜9）。<b>1 つの遷移につき 1 回だけ</b>。
        ///
        /// ここを通って初めて「着いた」。訪問登録・活動許可・完了通知はこれより後にしか出さない
        /// （§4.1「P5.5 では Area 初期化だけで訪問済みや活動許可を確定しない。Commit 時に行う」）。
        /// </summary>
        public bool TryCommit(int transitionId)
        {
            if (!IsCurrent(transitionId)
                || Phase != AreaSlideTransactionPhase.Sliding
                || _committedTransitionId == transitionId)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaSlideTransactionPhase.Committed;
            _committedTransitionId = transitionId;
            CommittedCount++;
            return true;
        }

        /// <summary>
        /// 到着完了の外部通知を 1 回だけ取り出す（手順 10）。
        ///
        /// <b>Commit と分けてある。</b> §6.2 は「成功確定の同期区間に yield を挟まず、外部通知は区間後に出す」
        /// と定めている。確定と通知を同じ呼び出しにすると、購読者の中で始まった次の遷移が
        /// まだ確定中の後始末に巻き込まれる。
        /// </summary>
        public bool TryConsumeArrivalAnnouncement(int transitionId)
        {
            if (transitionId == 0
                || transitionId != _committedTransitionId
                || _announcedTransitionId == transitionId)
            {
                StaleNotificationCount++;
                return false;
            }

            _announcedTransitionId = transitionId;
            return true;
        }

        /// <summary>
        /// 失敗したので出発側へ戻し始める（§8）。
        /// <b>Commit 済みの遷移は戻さない</b>——成功は取り消さない（§8 の表）。
        /// </summary>
        public bool TryBeginRollback(int transitionId)
        {
            if (!IsCurrent(transitionId) || !IsTransitioning)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaSlideTransactionPhase.RollingBack;
            return true;
        }

        /// <summary>出発側へ戻し終えた。次の要求を受けられる。</summary>
        public bool NotifyRolledBack(int transitionId)
        {
            if (!IsCurrent(transitionId) || Phase != AreaSlideTransactionPhase.RollingBack)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaSlideTransactionPhase.Idle;
            RolledBackCount++;
            return true;
        }

        /// <summary>戻り切れなかった（§8 最終行）。Error 表示に留め、次の要求は受けられる。</summary>
        public bool NotifyFailed(int transitionId)
        {
            if (!IsCurrent(transitionId) || Phase == AreaSlideTransactionPhase.Committed)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaSlideTransactionPhase.Failed;
            FailedCount++;
            return true;
        }

        /// <summary>
        /// 後始末を終えて次を受けられる状態へ戻す（手順 10 の排他解放）。
        /// <b>Commit 済みの世代でだけ成立する。</b>
        /// </summary>
        public bool Release(int transitionId)
        {
            if (transitionId == 0 || transitionId != _committedTransitionId
                || Phase != AreaSlideTransactionPhase.Committed)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaSlideTransactionPhase.Idle;
            return true;
        }

        private bool Advance(
            int transitionId, AreaSlideTransactionPhase from, AreaSlideTransactionPhase to)
        {
            if (!IsCurrent(transitionId) || Phase != from)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = to;
            return true;
        }
    }
}
