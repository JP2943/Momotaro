using Momotaro.Gameplay.Modes;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// エリア遷移の受付・世代管理・失敗復旧を担う純粋な調停役（P5-03b。仕様書 v1.1 §6）。
    /// UnityEngine に依存しないので EditMode で決定的に検証できる。実際の Scene ロードは
    /// Infrastructure が <see cref="IAreaLoadOperation"/> を渡して行う（§12 の層規則）。
    ///
    /// <b>世代（TransitionId）がこの型の要。</b> 遷移は「完了通知 → その中から次の遷移要求」という再入が普通に起きる。
    /// 実行世代を持たないと、<b>古い完了通知や旧 finally が新しい遷移の排他を解除してしまう</b>（§6.2 末尾）。
    /// そこで受理のたびに世代を 1 つ進め、すべての通知と解除は世代一致を確認してから効かせる。
    /// 一致しない通知は<b>無視して数える</b>だけで、状態は動かさない。
    ///
    /// <b>タイムアウトは「諦め」ではない。</b> Unity の非同期ロードはキャンセルできないので、
    /// 監視が切れても操作自体は生きている。タイムアウトしたら停止表示へ移るが、同じ操作を観測し続け、
    /// 遅れて完了したら<b>目的地を活動させずに復旧を 1 回だけ</b>始める（§6.3）。
    /// 排他はここで解除しない。解除すると、生きている古いロードの上に新しいロードが乗る。
    /// </summary>
    public sealed class AreaTransitionCoordinator
    {
        /// <summary>ロード監視の既定上限（unscaled 秒。§6.3）。</summary>
        public const float DefaultTimeoutSeconds = 30f;

        private readonly AreaCatalog _catalog;
        private readonly IAreaTransitionConditions _conditions;
        private readonly GameplayClockGate _clock;
        private float _timeoutSeconds;

        private IAreaLoadOperation _operation;
        private float _elapsedUnscaled;
        private bool _recoveryStarted;

        public AreaTransitionCoordinator(
            AreaCatalog catalog,
            IAreaTransitionConditions conditions,
            GameplayClockGate clock,
            float timeoutSeconds = DefaultTimeoutSeconds)
        {
            _catalog = catalog;
            _conditions = conditions;
            _clock = clock;
            TimeoutSeconds = timeoutSeconds;
        }

        /// <summary>
        /// ロード監視の上限（unscaled 秒。§6.3 の初期値は 30）。
        ///
        /// <b>途中で差し替えてよい。</b> 差し替えは次の判定から効き、経過時間は巻き戻さない。
        /// 「作る前に設定しないと効かない」形だと、設定と生成の順序が入れ替わったことに
        /// 誰も気づけないまま既定値で動く（実際にテストの並びで踏んだ）。0 以下は既定値として扱う。
        /// </summary>
        public float TimeoutSeconds
        {
            get => _timeoutSeconds;
            set => _timeoutSeconds = value > 0f ? value : DefaultTimeoutSeconds;
        }

        /// <summary>現在の段階。</summary>
        public AreaTransitionPhase Phase { get; private set; } = AreaTransitionPhase.Idle;

        /// <summary>現在の実行世代。受理のたびに 1 つ進む。0 は「遷移していない」。</summary>
        public int CurrentTransitionId { get; private set; }

        /// <summary>遷移中か。</summary>
        public bool IsTransitioning => Phase != AreaTransitionPhase.Idle && Phase != AreaTransitionPhase.Failed;

        /// <summary>監視がタイムアウトしたか（§6.3）。排他は解除していない。</summary>
        public bool TimedOut { get; private set; }

        /// <summary>復旧を開始した回数。<b>1 を超えてはいけない</b>（§6.3「無限再試行しない」）。</summary>
        public int RecoveryCount { get; private set; }

        /// <summary>世代不一致で無視した通知の数（診断・テスト用）。</summary>
        public int StaleNotificationCount { get; private set; }

        /// <summary>完了まで到達した遷移の数（診断・テスト用）。</summary>
        public int CompletedCount { get; private set; }

        /// <summary>ロード操作を開始した回数。タイムアウトしても増えてはいけない（§6.3「重複ロードなし」）。</summary>
        public int LoadStartCount { get; private set; }

        /// <summary>
        /// 遷移を要求する（§6.1）。受理したら世代を進め、Gameplay 時計を止める。
        ///
        /// 判定の順序は「目的地 → 状態 → 排他」。目的地が解決できない要求は<b>受理前に落とす</b>ので、
        /// その場に留まり進行を変更しない（§6.3 の 1 行目）。
        /// </summary>
        public AreaTransitionDecision TryRequest(in AreaTransitionRequest request)
        {
            // 排他を最初に見る（§6.2 手順 2「外部通知より先に再入を拒否する」）。
            //
            // 受理した時点で活動を閉じる＝ AreaReady が false になるので、条件を先に見ると
            // 再入の理由が NotReady になってしまい、呼び出し側が原因を取り違える。
            // 「もう遷移している」の方が具体的で行動可能な理由なので、こちらを先に返す。
            if (IsTransitioning)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.AlreadyTransitioning);
            }

            if (_catalog == null || !_catalog.TryGetEntry(request.AreaId, request.EntryId, out _))
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.UnknownDestination);
            }

            if (_conditions == null)
            {
                // 条件が分からない＝安全側。未配線で遷移させない。
                return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
            }

            // 死亡再開だけは別の受付条件を使う（§9.1 手順 4／§6.1 末尾）。
            //
            // 通常の移動は「探索中・生存・行動していない」を求めるが、再開はその 3 つが
            // <b>すべて成り立たないときにだけ</b>行う操作である。同じ条件表を当てると、
            // 再開が自分の前提で拒否されて永久に死んだままになる。
            // 代わりに「GameOver であること」を求め、戦闘が残っていないことだけ引き続き見る。
            if (request.IsRespawn)
            {
                // <b>AreaReady は見ない。</b> 死亡を受理した時点で活動を閉じている（§9.1 手順 1）ので、
                // 通常の移動と同じ「準備できているか」を求めると、閉じた自分の状態を理由に
                // 再開が断られ、死んだまま動けなくなる（実際に踏んだ）。
                if (_conditions.Mode != GameMode.GameOver)
                {
                    return AreaTransitionDecision.Reject(AreaTransitionRejection.WrongMode);
                }

                if (_conditions.IsEncounterActive)
                {
                    return AreaTransitionDecision.Reject(AreaTransitionRejection.EncounterActive);
                }
            }
            else
            {
                if (!_conditions.IsAreaReady)
                {
                    return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
                }

                if (_conditions.Mode != GameMode.Exploration)
                {
                    return AreaTransitionDecision.Reject(AreaTransitionRejection.WrongMode);
                }

                if (!_conditions.IsPlayerAlive)
                {
                    return AreaTransitionDecision.Reject(AreaTransitionRejection.PlayerDefeated);
                }

                // 戦闘開始が移動より先（§8.3 の競合表）。主人公の行動中より先に見る。
                if (_conditions.IsEncounterActive)
                {
                    return AreaTransitionDecision.Reject(AreaTransitionRejection.EncounterActive);
                }

                if (_conditions.IsPlayerBusy)
                {
                    return AreaTransitionDecision.Reject(AreaTransitionRejection.PlayerBusy);
                }
            }

            CurrentTransitionId++;
            Phase = AreaTransitionPhase.Preparing;
            TimedOut = false;
            _recoveryStarted = false;
            _recoveryConsumed = false;
            _elapsedUnscaled = 0f;
            _operation = null;
            _clock?.Freeze();

            return AreaTransitionDecision.Accept(CurrentTransitionId);
        }

        /// <summary>ロードを開始したことを通知する。世代が一致しなければ何もしない。</summary>
        public bool NotifyLoadStarted(int transitionId, IAreaLoadOperation operation)
        {
            if (!IsCurrent(transitionId) || Phase != AreaTransitionPhase.Preparing)
            {
                StaleNotificationCount++;
                return false;
            }

            _operation = operation;
            _elapsedUnscaled = 0f;
            Phase = AreaTransitionPhase.Loading;
            LoadStartCount++;
            return true;
        }

        /// <summary>
        /// ロードが終わり Bind へ入ったことを通知する。<c>sceneLoaded</c> だけで Ready 扱いにしない（§6.2 手順 7）。
        ///
        /// <b>監視がタイムアウトしていたら受け付けない。</b> 遅れて完了した目的地を通常どおり
        /// 活動させてはいけない（§6.3「遅れてロード完了しても目的地を活動させず、保留した復旧だけを 1 回開始する」）。
        /// タイムアウト後は <see cref="RecoveryPending"/> を見て復旧へ分岐する。
        /// </summary>
        public bool NotifyBinding(int transitionId)
        {
            if (!IsCurrent(transitionId) || Phase != AreaTransitionPhase.Loading || TimedOut)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaTransitionPhase.Binding;
            return true;
        }

        /// <summary>配置・カメラ・HUD の確認まで終わったことを通知する。</summary>
        public bool NotifyReady(int transitionId)
        {
            if (!IsCurrent(transitionId) || Phase != AreaTransitionPhase.Binding)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaTransitionPhase.Ready;
            return true;
        }

        /// <summary>
        /// 遷移を完了して排他を解く（§6.2 手順 9）。
        /// <b>世代が一致しないと解除しない。</b> 前の遷移の finally が新しい遷移の排他を解除するのを防ぐ。
        /// </summary>
        public bool Release(int transitionId)
        {
            if (!IsCurrent(transitionId) || Phase != AreaTransitionPhase.Ready)
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaTransitionPhase.Idle;
            _operation = null;
            CompletedCount++;
            _clock?.Thaw();
            return true;
        }

        /// <summary>
        /// 遷移に失敗したことを確定する（§6.3）。
        ///
        /// <b>旧 Scene が使えるかどうかで扱いが変わる。</b>
        /// 使えるなら「その場に留まって元の活動を再開する」ので Gameplay 時計を<b>戻す</b>。
        /// 戻さないと、留まってはいるが移動も CD 進行もできない状態になる（GPT レビュー R1 で指摘された）。
        /// 使えないなら復旧ロードが要るので、止めたままにする（壊れた状態で動かさない）。
        /// </summary>
        /// <param name="transitionId">失敗した遷移の世代。</param>
        /// <param name="oldSceneUsable">旧 Scene が生きていて、そのまま活動を再開できるか。</param>
        public bool NotifyFailed(int transitionId, bool oldSceneUsable)
        {
            if (!IsCurrent(transitionId))
            {
                StaleNotificationCount++;
                return false;
            }

            Phase = AreaTransitionPhase.Failed;
            _operation = null;

            if (oldSceneUsable)
            {
                _clock?.Thaw();
            }

            return true;
        }

        /// <summary>
        /// 監視を進める（unscaled 秒。§6.3「暗転と Error UI は unscaled 時間で動かし、Gameplay 時計・CD は進めない」）。
        ///
        /// タイムアウトしても<b>排他を解除せず、同じ操作を観測し続ける</b>。
        /// 遅れて完了したら復旧を 1 回だけ開始する。正常系に固定の待ち時間を足さない。
        /// </summary>
        public void TickUnscaled(float unscaledDelta)
        {
            if (Phase != AreaTransitionPhase.Loading || unscaledDelta <= 0f)
            {
                return;
            }

            if (!TimedOut)
            {
                _elapsedUnscaled += unscaledDelta;
                if (_elapsedUnscaled >= _timeoutSeconds)
                {
                    TimedOut = true;
                }
            }

            if (!TimedOut || _operation == null || !_operation.IsDone || _recoveryStarted)
            {
                return;
            }

            // 古い操作が終端した。ここで初めて復旧を 1 回だけ始める。
            _recoveryStarted = true;
            RecoveryCount++;
        }

        /// <summary>復旧を開始してよい状態か（診断・テスト用）。</summary>
        public bool RecoveryPending => TimedOut && _recoveryStarted && !_recoveryConsumed;

        private bool _recoveryConsumed;

        /// <summary>
        /// 保留していた復旧を実行側が受け取る。<b>1 回だけ true を返す</b>（§6.3「無限再試行しない」）。
        /// サービスはこれが true のときだけ復旧ロードを始める。
        /// </summary>
        public bool TryConsumeRecovery()
        {
            if (!RecoveryPending)
            {
                return false;
            }

            _recoveryConsumed = true;
            return true;
        }

        /// <summary>
        /// 旧 Scene が破棄されたあとの失敗から、復旧ロードを 1 回だけ始めてよいか問う（§6.3 の 3 行目）。
        /// タイムアウト由来の復旧と同じ回数上限を共有する。
        /// </summary>
        public bool TryBeginRecovery(int transitionId)
        {
            if (!IsCurrent(transitionId) || _recoveryStarted)
            {
                return false;
            }

            _recoveryStarted = true;
            _recoveryConsumed = true;
            RecoveryCount++;
            return true;
        }

        private bool IsCurrent(int transitionId) =>
            transitionId != 0 && transitionId == CurrentTransitionId;
    }
}
