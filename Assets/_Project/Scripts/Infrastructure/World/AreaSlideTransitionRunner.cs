using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Data.World;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// スライド遷移の実行役（P5.5 仕様書 §6.2。工程 P55-04b）。
    ///
    /// <b>判断は <see cref="AreaSlideCoordinator"/>、実行はここ</b>——P5 の
    /// <see cref="AreaTransitionCoordinator"/>／<see cref="AreaTransitionService"/> と同じ分け方である。
    /// 受付・世代・Commit・Rollback の規則は純粋クラス側にあり EditMode で検証済み。
    /// ここがやるのは Unity への橋渡しだけ：Additive ロード、活動ゲートの開閉、
    /// カメラのスライド、表示代理、旧 Scene の撤去。
    ///
    /// <b>P5 の Single 経路を 1 行も変えない。</b> 既存の 64 件の必須テストはすべて
    /// <see cref="AreaTransitionService.TryTravel(StableId, StableId)"/> の
    /// <c>TravelRoutine</c> を通るので、そこへ分岐を足すのではなく<b>別経路</b>にした（§1.2 と同じ考え方）。
    ///
    /// <b>排他・Scene 操作の直列化は共有する</b>（§8「別サービスが同時ロードを発行しない」）。
    /// 常駐は 1 つ（<see cref="AreaTransitionService"/>）で、そこが Fade と Slide の
    /// どちらかしか走らせない。Scene 操作の直列化は <see cref="AreaResidencyLedger"/> が正本。
    ///
    /// <b>成功確定点は 1 か所だけ</b>（§6.2「成功確定点は 8〜9 の同期区間。そこに yield を挟まない」）。
    /// 訪問登録・活動許可・完了通知は、その区間を通ったあとにしか出さない。
    /// </summary>
    public sealed class AreaSlideTransitionRunner
    {
        /// <summary>
        /// 表示を進める 1 フレームの上限（unscaled 秒）。
        ///
        /// <b>巨大 delta で飛ばさない</b>（§7.1 末尾）。アプリが止まっていた・重いフレームが
        /// 挟まった場合の <c>unscaledDeltaTime</c> は数秒になりうる。そのまま渡すと
        /// 1 フレームで演出が終わり、通路を渡る絵が出ない。
        /// </summary>
        public const float MaxDisplayStepSeconds = 0.1f;

        /// <summary>
        /// 接続が所要秒を持たないときの既定（§7.1 の初期値 0.45 秒）。
        /// 正本は Data 側（<see cref="AreaConnectionDefinition"/>）で、ここは 0 が来たときの保険。
        /// </summary>
        public const float FallbackSlideSeconds = 0.45f;

        /// <summary>スライドを接続軸に載せる許容ずれ（m）。</summary>
        public const float AxisTolerance = 0.05f;

        private readonly AreaTransitionService _owner;
        private readonly AreaSlideCoordinator _slide = new AreaSlideCoordinator();
        private readonly AreaResidencyLedger _residency = new AreaResidencyLedger();
        private AreaPreloader _preloader;

        /// <summary>作る。<paramref name="owner"/> が常駐の遷移サービス（Provider の所有者でもある）。</summary>
        public AreaSlideTransitionRunner(AreaTransitionService owner)
        {
            _owner = owner;
        }

        /// <summary>スライド遷移の調停役（診断・テスト用）。</summary>
        public AreaSlideCoordinator Coordinator => _slide;

        /// <summary>在留台帳（診断・テスト用）。Scene 操作の直列化もここが正本。</summary>
        public AreaResidencyLedger Residency => _residency;

        /// <summary>
        /// 先読み（診断・テスト用）。<b>初回の要求で作る</b>——
        /// Scene 操作の実装（<see cref="AreaTransitionService.SlideSceneHost"/>）は
        /// テストが差し替えるので、常駐の起動時に固定してしまうと注入できない。
        /// </summary>
        public AreaPreloader Preloader => EnsurePreloader();

        /// <summary>いまスライド遷移が走っているか（§8 の排他共有に使う）。</summary>
        public bool IsTransitioning => _slide.IsTransitioning;

        /// <summary>成功を確定した回数（診断・テスト用）。</summary>
        public int CommittedCount { get; private set; }

        /// <summary>出発側へ戻した回数（診断・テスト用）。</summary>
        public int RolledBackCount { get; private set; }

        /// <summary>戻り切れなかった回数（診断・テスト用）。</summary>
        public int FailedCount { get; private set; }

        /// <summary>Commit 後に旧 Area の撤去が失敗した回数（診断・テスト用。§8 の 4 行目）。</summary>
        public int UnloadFailureCount { get; private set; }

        /// <summary>直近の失敗理由（成功なら空）。</summary>
        public string LastFailure { get; private set; } = string.Empty;

        /// <summary>直近のスライドの始点・終点（診断・テスト用）。</summary>
        public Vector3 LastSlideFrom { get; private set; }

        /// <summary>直近のスライドの終点（診断・テスト用）。</summary>
        public Vector3 LastSlideTo { get; private set; }

        /// <summary>
        /// スライドで遷移を要求する（§6.2 手順 1〜3）。
        ///
        /// <b>受理前に State を変えない</b>（手順 1 末尾）。行き先が引けない・出発側が分からない・
        /// Fade が走っている——どれも「その場に留まる」であって、活動を閉じてはいけない。
        /// </summary>
        public AreaTransitionDecision TryTravel(in AreaConnectionSnapshot connection)
        {
            AreaCatalog catalog = _owner.Catalog;
            if (catalog == null)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
            }

            // 1. 行き先の Scene と入口を解決する（受理前の不正 Data で State を変えない）。
            if (!catalog.TryGetEntry(connection.ToAreaId, connection.EntryId, out AreaEntryInfo entry)
                || string.IsNullOrEmpty(entry.ScenePath))
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.UnknownDestination);
            }

            // <b>Fade と排他を共有する</b>（§8）。別サービスではなく同じ常駐が持っているので、
            // ここで見るだけで「同時に 2 本のロードが飛ぶ」を防げる。
            if (_owner.Coordinator != null && _owner.Coordinator.IsTransitioning)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.AlreadyTransitioning);
            }

            if (!TryResolveDeparture(out AreaRuntimeBundle departure))
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
            }

            AreaTransitionDecision decision = _slide.TryRequest(connection, _owner.Conditions);
            if (!decision.Accepted)
            {
                return decision;
            }

            LastFailure = string.Empty;

            // 2. 出発 Area を止める。GameMode=Loading、Gameplay 時計を凍結する。
            //    <b>凍結はここで行う</b>——P5 の調停役は自分で凍結するが、スライドの調停役は
            //    UnityEngine に触らない純粋クラスなので時計を持っていない（§9）。
            departure.Context.CloseForTransition();
            _owner.Clock.Freeze();
            GameModeProvider.Current?.ChangeMode(GameMode.Loading);

            // 3. 中断により発生する CD を含めて Snapshot を採る（手順 4→5 は Port が 1 か所で行う）。
            _owner.CaptureActorsForTransition();

            _owner.StartSlideRoutine(SlideRoutine(connection, decision.TransitionId, departure, entry));
            return decision;
        }

        /// <summary>
        /// §6.2 手順 4〜11。<b>各段の通知は必ず自分の世代を添えて出す</b>——
        /// 一致しなければ何も起きない（古い Coroutine が新しい遷移を壊さない）。
        /// </summary>
        private IEnumerator SlideRoutine(
            AreaConnectionSnapshot connection, int transitionId,
            AreaRuntimeBundle departure, AreaEntryInfo entry)
        {
            int departureSceneHandle = departure.SceneHandle;

            if (!_slide.NotifyPreparing(transitionId))
            {
                yield break;
            }

            AreaInstanceHandle departureHandle = EnsureResidency(departure);
            if (!departureHandle.IsValid)
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "出発 Area を在留台帳へ登録できませんでした。");
                yield break;
            }

            _residency.TrySetPhase(departureHandle, AreaActivationPhase.Suspended);

            // 到着側の初期化担当が「自分宛ての到着」と分かるようにする（§5.2／§6.2 手順 7）。
            // <b>これを置かないと</b>到着側は直開きと見なして自分で活動を始め、
            // Commit を待たずに遊べてしまう。
            AreaPendingArrival.Set(transitionId, connection.ToAreaId, connection.EntryId);

            // ---- 手順 4：対応する StagedReady を取得する ----
            //
            // <b>先読みを通す。</b> 距離による先読み（§5）が同じ先を望んでいれば読み直さず
            // 引き取るだけになる（<c>AreaPreloader.Request</c> の同一先再利用）。
            // ここで独自にロードを発行すると、先読みと二重に読むことになる。
            AreaPreloader preloader = EnsurePreloader();
            preloader.ArmRetry(); // 新しい遷移操作は、抱えている失敗を 1 回だけ再試行できる（§5）。
            preloader.Request(connection.ToAreaId, entry.ScenePath);

            float waited = 0f;
            while (preloader.Phase == AreaPreloadPhase.Loading
                   || preloader.Phase == AreaPreloadPhase.Releasing)
            {
                preloader.Poll();
                if (waited >= _owner.TimeoutSeconds)
                {
                    break;
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            preloader.Poll();

            if (preloader.Phase != AreaPreloadPhase.Staged
                || !preloader.StagedArea.AreaId.Equals(connection.ToAreaId))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "目的地の先読みが完了しませんでした（" + preloader.Phase + " / "
                    + preloader.FailureReason + "）。");
                yield break;
            }

            AreaInstanceHandle destinationHandle = preloader.StagedArea;
            int destinationSceneHandle = preloader.StagedSceneHandle;

            if (!AreaBundleDirectory.TryGetByScene(destinationSceneHandle,
                    out AreaRuntimeBundle destination)
                || destination == null || destination.Context == null || destination.Root == null
                || destination.ActivityGate == null)
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "到着 Area の参照集合を引けませんでした（束・活動ゲートの配線漏れ）。");
                yield break;
            }

            // 実体ハンドルを結ぶ（§4.3）。カメラの終点計算はこのハンドルで到着 Area を引く。
            destination.BindInstance(destinationHandle);

            // ---- 手順 5：出発側を閉じ、到着側を隔離された Prepared へ ----
            //
            // <b>代理を立ててから閉じる（同じフレーム）。</b> 出発側の活動ゲートは
            // 主人公・犬丸の物体ごと非 Active にするので、先に閉じると
            // 1 フレームだけ主人公が居ない絵が出る（§7.2「一瞬の欠落を作らない」）。
            IAreaTransitionDisplay display = AreaTransitionDisplayProvider.Current;
            Vector3 playerFrom = ResolvePlayerPosition(departure);
            display?.TryBegin(departure);
            departure.ActivityGate?.Close();

            // 到着側の活動ゲートを開けて初期化を走らせる。
            //
            // <b>「開ける」は「遊べる」ではない。</b> 初期化担当（<see cref="AreaInitializer"/>）は
            // 活動ゲートの内側に居るので、閉じたままでは Snapshot の復元も入口への配置も走らない。
            // 遊べるかどうかを決めるのは <c>AreaContext.IsAreaReady</c> と Gameplay 時計で、
            // どちらもまだ閉じている。索敵・Interact は <see cref="CurrentAreaProvider"/> が
            // まだ出発側を差しているので絞り込みの外にある（§4.1「外部へ公開していない」）。
            destination.ActivityGate.Open();
            _residency.TrySetPhase(destinationHandle, AreaActivationPhase.Prepared);

            float bindWaited = 0f;
            while (!AreaPendingArrival.IsPreparedFor(transitionId)
                   && bindWaited < _owner.BindTimeoutSeconds)
            {
                bindWaited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!AreaPendingArrival.IsPreparedFor(transitionId))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "到着側の準備が " + _owner.BindTimeoutSeconds.ToString("0.##") + " 秒以内に完了しませんでした。",
                    destination);
                yield break;
            }

            // <b>描画準備完了後のフレームを一度通す</b>（§5「固定秒数の待機では準備確認を代用しない」）。
            // 準備完了の報告はロジックの完了で、Renderer が実際に 1 枚描かれたかは別。
            yield return null;

            if (!_slide.NotifyPrepared(transitionId))
            {
                yield break;
            }

            // ---- 終点を測る（§7.1「終点は到着 Actor 位置に対して到着 Area の通常追従・clamp が算出する位置」）----
            if (!destination.Root.TryGetEntryPoint(connection.EntryId, out AreaEntryPoint arrival))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "到着入口 '" + connection.EntryId.Value + "' が到着 Scene にありません。", destination);
                yield break;
            }

            IAreaCameraOwner camera = AreaCameraOwnerProvider.Current;
            if (camera == null
                || !camera.TryGetRigPosition(out Vector3 slideFrom)
                || !camera.TryComputeArrivalPoint(destinationHandle, arrival.ArrivalPosition,
                    out Vector3 slideTo))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "スライドの終点を計算できませんでした（常駐カメラ・到着 Area のカメラ領域）。",
                    destination);
                yield break;
            }

            // <b>接続軸から外れる経路は準備失敗</b>（§7.1 末尾／§7.2 末尾
            // 「表示経路を安全に作れない接続は準備失敗とする」）。
            // 無断で暗転へ切り替えて成功扱いにしない。
            if (!IsOnConnectionAxis(connection, slideFrom, slideTo))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "スライドが接続軸から外れます（" + connection.Axis + " / from=" + slideFrom
                    + " to=" + slideTo + "）。", destination);
                yield break;
            }

            LastSlideFrom = slideFrom;
            LastSlideTo = slideTo;

            // 到着側の実 Actor を隠す（§6.2 手順 6「両方の実 Actor の Renderer は隠し」）。
            // 隠さないと、通路を渡る代理と入口で待つ到着 Actor が二重に映る。
            display?.HideArrivals(destination);
            display?.SetRoute(playerFrom, arrival.ArrivalPosition);

            // ---- 手順 6：スライド ----
            if (!camera.BeginSlide(slideTo, ResolveSeconds(connection)))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "スライドを開始できませんでした（常駐カメラが未配線）。", destination);
                yield break;
            }

            if (!_slide.NotifySlideStarted(transitionId))
            {
                yield break;
            }

            while (true)
            {
                float step = Mathf.Min(Time.unscaledDeltaTime, MaxDisplayStepSeconds);
                bool running = camera.TickSlide(step);

                // カメラと<b>同じ</b>進行度を配る（§7.2）。自前の時計から作ると別の曲線になる。
                display?.SetProgress(camera.SlideEased);
                display?.TickDisplayClock(step);

                if (!running)
                {
                    break;
                }

                yield return null;
            }

            camera.EndSlide();

            // ---- 手順 7〜9：成功確定の同期区間。ここに yield を挟まない ----
            if (!_slide.TryCommit(transitionId))
            {
                // 世代が進んでいた。共有領域には触らず、代理だけ畳む。
                display?.Release();
                yield break;
            }

            // 先読みから所有を引き取る。<b>Commit まで引き取らない</b>のは、
            // ここより前の失敗で到着 Area を撤去する役を先読みに任せておくため。
            preloader.TryHandOffStaged(connection.ToAreaId, out _, out _);

            _residency.TrySetPhase(destinationHandle, AreaActivationPhase.Active);
            _residency.TrySetPhase(departureHandle, AreaActivationPhase.Retiring);

            CurrentAreaProvider.TrySetCurrent(_owner, destination);
            TrySetActiveScene(destinationSceneHandle);

            // 受付条件の窓口も到着側へ（§6.2 手順 8「受付条件」）。
            // 到着側の初期化担当が Prepared の時点で差しているが、<b>明示的に差し直す</b>——
            // 暗黙に任せると、初期化の配線が変わったときに静かに古い窓口が残る。
            _owner.RebindConditions(destination.Conditions);

            // <b>ここで初めて訪問済みを記録する</b>（手順 9／§4.1「Commit 時に行う」）。
            _owner.NoteArrival(connection.ToAreaId);

            destination.Context.Activate();
            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);

            // 実 Actor の表示を戻し、代理を除去する（手順 8）。
            display?.Release();

            // 運んだ値は使い終わった。復旧のために保持する理由がもう無い（§6.2 手順 10）。
            _owner.ClearPendingTransfer();
            AreaPendingArrival.Clear();
            _owner.Clock.Thaw();
            CommittedCount++;
            LastFailure = string.Empty;
            // ---- 同期区間ここまで ----

            // 手順 10：排他と共有情報を片付けてから、一度だけ通知する。
            if (_slide.TryConsumeArrivalAnnouncement(transitionId) && _slide.Release(transitionId))
            {
                _owner.RaiseArrivalCompleted(connection.ToAreaId);
            }

            // 手順 11：旧 Area を撤去する。<b>成功は取り消さない</b>（§8 の 4 行目）。
            yield return UnloadDeparture(departureHandle, departureSceneHandle);
        }

        /// <summary>
        /// 旧 Area を撤去する（手順 11）。
        ///
        /// <b>失敗しても到着の成功を取り消さない</b>（§8 の 4 行目）。旧 Area は
        /// 非活動・非物理・購読解除済みで隔離されたままにし、台帳の在留枠も返さない——
        /// 返すと「空きあり」と誤認して 3 枚目を読む。
        /// </summary>
        private IEnumerator UnloadDeparture(AreaInstanceHandle handle, int sceneHandle)
        {
            IAreaSceneHost host = _owner.SlideSceneHost;
            if (sceneHandle == 0 || host == null || !host.IsLoaded(sceneHandle))
            {
                // すでに載っていない（Single 起動の直後など）。枠だけ返す。
                _residency.Remove(handle);
                yield break;
            }

            if (!_residency.TryBeginSceneOperation())
            {
                UnloadFailureCount++;
                LastFailure = "他の Scene 操作が走っているため旧 Area を撤去できませんでした。";
                yield break;
            }

            IAreaSceneOperation operation = host.Unload(sceneHandle);
            while (operation != null && !operation.IsDone)
            {
                yield return null;
            }

            _residency.EndSceneOperation();

            // <b>撤去できたと決めつけない。</b> 実際に消えたかを確かめる
            // （先読みの撤去と同じ規律。GPT レビュー R10 の指摘 2）。
            if (operation == null || host.IsLoaded(sceneHandle))
            {
                UnloadFailureCount++;
                LastFailure = "旧 Area の撤去が失敗し、まだ載っています（到着は成立している）。";
                GameLog.Warning(LogCategory.Scene, "Failed to unload the departed area: " + LastFailure);
                yield break;
            }

            _residency.Remove(handle);
        }

        /// <summary>
        /// 出発側へ戻す（§8 の 2 行目「B を隔離・解放し、旧 Actor と Camera を出発位置で復帰」）。
        ///
        /// <b>Rollback は HP や徳を巻き戻す機能ではない</b>（§8 末尾）。受理後は活動を止めているので、
        /// 中断後 Snapshot と同じ値のまま旧側へ戻るだけである。探索は自動再開しない。
        /// </summary>
        private IEnumerator Rollback(
            int transitionId, AreaRuntimeBundle departure, AreaInstanceHandle departureHandle,
            string reason, AreaRuntimeBundle destination = null)
        {
            LastFailure = reason;
            GameLog.Warning(LogCategory.Scene, "Slide transition rolled back: " + reason);

            if (!_slide.TryBeginRollback(transitionId))
            {
                yield break;
            }

            // 到着側を隔離・解放する。<b>放棄ではなく消す</b>——旧 Scene は生きているので、
            // 遅れて着いた Scene が自分で活動を始める心配が無い（消さないと出発側の
            // 次の遷移が「別 Area 宛ての要求」に引っかかる）。
            AreaPendingArrival.Clear();

            // 分かっているなら<b>隔離してから</b>解放する（§8 の 2 行目「B を隔離・解放し」）。
            // 開いたまま撤去すると、消えるまでの間だけ B の登録が生きている。
            destination?.ActivityGate?.Close();

            AreaPreloader preloader = EnsurePreloader();
            preloader.ClearRequest();

            float waited = 0f;
            while ((preloader.Phase == AreaPreloadPhase.Releasing
                    || preloader.Phase == AreaPreloadPhase.Loading)
                   && waited < _owner.TimeoutSeconds)
            {
                preloader.Poll();
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            preloader.Poll();

            // 代理を畳んで実 Actor の表示を戻し、出発側の活動を戻す。
            AreaTransitionDisplayProvider.Current?.Release();

            // <b>走っているときだけ打ち切る。</b> 走っていない演出を Cancel すると
            // 前回のスライドの始点が適用され、カメラが無関係な場所へ跳ぶ。
            IAreaCameraOwner camera = AreaCameraOwnerProvider.Current;
            if (camera != null && camera.IsSliding)
            {
                camera.CancelSlide();
            }

            if (departure != null)
            {
                departure.ActivityGate?.Open();
                departure.Context?.ReopenAfterFailedTransition();

                // <b>受付条件の窓口を出発側へ戻す。</b> 到着側の初期化担当が Prepared の時点で
                // 自分の窓口を差しているので、戻さないと<b>撤去済みの部品</b>を指したまま残り、
                // 以後どの遷移も断られる（実際に踏んだ：Rollback のあと Fade も受理されなかった）。
                _owner.RebindConditions(departure.Conditions);
            }

            if (departureHandle.IsValid)
            {
                _residency.TrySetPhase(departureHandle, AreaActivationPhase.Active);
            }

            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);

            // 運んでいた値は捨てる。<b>Actor はどこへも動いていない</b>ので、
            // 生きている実 Actor が持っている値がそのまま正本である。
            _owner.ClearPendingTransfer();
            _owner.Clock.Thaw();

            if (_slide.NotifyRolledBack(transitionId))
            {
                RolledBackCount++;
            }
            else
            {
                FailedCount++;
            }
        }

        /// <summary>
        /// 出発側の参照集合を引く。常駐の指定があればそれ、無ければ Area がちょうど 1 つのときだけ。
        /// <b>引けたら常駐の指定にする</b>——2 枚目が載ると「ちょうど 1 つ」の互換経路が使えなくなる。
        /// </summary>
        private bool TryResolveDeparture(out AreaRuntimeBundle bundle)
        {
            bundle = CurrentAreaProvider.Current;
            if (bundle == null && !AreaBundleDirectory.TryGetSingle(out bundle))
            {
                return false;
            }

            if (bundle == null || bundle.Context == null)
            {
                return false;
            }

            CurrentAreaProvider.TrySetCurrent(_owner, bundle);
            return true;
        }

        /// <summary>
        /// 出発 Area を在留台帳に載せる（載っていなければ）。
        ///
        /// Single 起動・直開きで載った Area は台帳を通っていないので、実体ハンドルを持たない。
        /// <b>ここで配る。</b> 持たせないと、到着側の終点計算（実体ハンドルで引く）も
        /// 撤去（どの枠を返すか）も成立しない。
        /// </summary>
        private AreaInstanceHandle EnsureResidency(AreaRuntimeBundle bundle)
        {
            if (bundle == null || !bundle.AreaId.IsValid)
            {
                return AreaInstanceHandle.None;
            }

            AreaInstanceHandle existing = bundle.Instance;
            if (existing.IsValid && _residency.PhaseOf(existing) != AreaActivationPhase.Unloaded)
            {
                return existing;
            }

            AreaInstanceHandle handle = _residency.NextHandle(bundle.AreaId);
            if (!_residency.TryAdmitStaged(handle))
            {
                return AreaInstanceHandle.None;
            }

            _residency.TrySetPhase(handle, AreaActivationPhase.Active);
            bundle.BindInstance(handle);
            return handle;
        }

        private AreaPreloader EnsurePreloader()
        {
            return _preloader ??= new AreaPreloader(_residency, _owner.SlideSceneHost, _owner);
        }

        /// <summary>出発側の主人公の足元位置（居なければ原点）。代理の出発位置になる。</summary>
        private static Vector3 ResolvePlayerPosition(AreaRuntimeBundle bundle)
        {
            return bundle != null
                   && bundle.TryResolve(out Momotaro.Gameplay.Player.PlayerRoot player)
                   && player != null
                ? player.transform.position
                : Vector3.zero;
        }

        /// <summary>接続の所要秒（0 なら既定へ落とす）。</summary>
        private static float ResolveSeconds(in AreaConnectionSnapshot connection) =>
            connection.SlideDuration > 0f ? connection.SlideDuration : FallbackSlideSeconds;

        /// <summary>
        /// カメラの移動が接続軸だけに乗っているか（§7.1 末尾）。
        /// 東西なら Z が動かず、南北なら X が動かない。
        /// </summary>
        private static bool IsOnConnectionAxis(
            in AreaConnectionSnapshot connection, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            switch (connection.Axis)
            {
                case AreaConnectionRules.Axis.X:
                    return Mathf.Abs(delta.z) <= AxisTolerance;
                case AreaConnectionRules.Axis.Z:
                    return Mathf.Abs(delta.x) <= AxisTolerance;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 到着 Scene を active にする（§6.2 手順 8「Scene active 指定」）。
        ///
        /// 指定しないまま旧 Scene を撤去すると、active Scene が消える瞬間に
        /// 新しく作られる物体の行き先が決まらない。
        /// </summary>
        private static void TrySetActiveScene(int sceneHandle)
        {
            if (sceneHandle == 0)
            {
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.handle == sceneHandle && scene.isLoaded)
                {
                    SceneManager.SetActiveScene(scene);
                    return;
                }
            }
        }
    }
}
