using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Data.World;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Transfer;
using Momotaro.Infrastructure.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// エリア遷移を実行する常駐コンポーネント（P5-03b。仕様書 v1.1 §6.2）。
    ///
    /// <b>判断は Gameplay の <see cref="AreaTransitionCoordinator"/>、実行はここ</b>という分け方にしてある。
    /// 受付条件・世代・タイムアウト・復旧の回数といった「規則」は純粋クラス側にあり EditMode で検証済み
    /// （E08〜E10）。ここがやるのは Unity への橋渡しだけ：Scene の非同期ロード、暗転、GameMode の切替。
    ///
    /// <b>世代の確認を飛ばさない。</b> Coroutine は遷移をまたいで生き残りうるので、
    /// 各段の通知は必ず自分の <c>TransitionId</c> を添えて出し、調停役が一致を確かめる。
    /// 一致しなければ何も起きない（古い Coroutine が新しい遷移を壊さない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTransitionService : MonoBehaviour, IGameService
    {
        private readonly GameplayClockGate _clock = new GameplayClockGate();

        private AreaCatalog _catalog;
        private AreaTransitionCoordinator _coordinator;
        private IAreaTransitionConditions _conditions;
        private Coroutine _running;
        private AreaTransferSnapshot _pendingTransfer;
        private bool _hasPendingTransfer;

        /// <summary>
        /// いま生きているロード操作。<b>1 つしか持たない。</b>
        ///
        /// Unity の非同期ロードはキャンセルできないので、監視を諦めても操作自体は走り続ける（§6.3）。
        /// 終端していない操作の上に新しいロードを重ねると、どちらが最後に Scene を置き換えるか決まらない。
        /// 「古い操作が終端するまで新たなロードを開始しない」をここ 1 か所で守る。
        /// </summary>
        private IAreaLoadOperation _liveOperation;

        /// <inheritdoc />
        public string ServiceName => "AreaTransition";

        /// <summary>調停役（診断・テスト用）。未初期化なら null。</summary>
        public AreaTransitionCoordinator Coordinator => _coordinator;

        /// <summary>Gameplay 時計のゲート（診断・テスト用）。</summary>
        public GameplayClockGate Clock => _clock;

        /// <summary>解決済みのカタログ（診断・テスト用）。</summary>
        public AreaCatalog Catalog => _catalog;

        /// <summary>直近の遷移が完了した回数（診断・テスト用）。</summary>
        public int CompletedCount => _coordinator != null ? _coordinator.CompletedCount : 0;

        /// <summary>
        /// 到着側が値を読む（§6.2 手順 7）。<b>消費はしない。</b>
        ///
        /// 以前は読んだ時点で捨てていたが、到着側の適用に失敗すると復旧用の値が
        /// 取り出せなくなる（GPT レビュー R1）。遷移が成功するか復旧が終わるまで保持し、
        /// <see cref="ClearPendingTransfer"/> で明示的に捨てる。
        /// </summary>
        public bool TryPeekPendingTransfer(out AreaTransferSnapshot snapshot)
        {
            snapshot = _pendingTransfer;
            return _hasPendingTransfer;
        }

        /// <summary>運んでいた値を捨てる（遷移の成功時・復旧の終了時）。</summary>
        public void ClearPendingTransfer()
        {
            _pendingTransfer = default;
            _hasPendingTransfer = false;
        }

        /// <summary>
        /// ロード監視の上限（unscaled 秒。§6.3 の初期値は 30）。
        /// <b>いつ設定しても効く</b>（調停役が既にあれば伝える）。テストは短くして復旧経路を実時間で通す。
        /// </summary>
        public float TimeoutSeconds
        {
            get => _timeoutSeconds;
            set
            {
                _timeoutSeconds = value;
                if (_coordinator != null)
                {
                    _coordinator.TimeoutSeconds = value;
                }
            }
        }

        private float _timeoutSeconds = AreaTransitionCoordinator.DefaultTimeoutSeconds;

        /// <summary>
        /// 到着側の準備完了を待つ上限（unscaled 秒）。超えたら Bind 失敗として復旧へ回す（§6.3 の 3 行目）。
        /// テストは短くして失敗経路を実時間で通す。
        /// </summary>
        public float BindTimeoutSeconds { get; set; } = 10f;

        /// <summary>
        /// 復旧ロードの監視上限（unscaled 秒）。
        ///
        /// <b>復旧にも監視が要る</b>（GPT レビュー R2 の指摘 3）。ここが無界だと、復旧先の Scene が
        /// 返ってこないときに暗転のまま永久に待ち、理由も戻り道も出ない。
        /// 超えたら終端失敗として理由を残し、Launcher へ戻る操作を提示する（§6.3 の最終行）。
        /// ただし<b>ここで新しいロードは始めない</b>。生きている操作が終端するまで戻り操作も待たせる。
        /// </summary>
        public float RecoveryTimeoutSeconds { get; set; } = AreaTransitionCoordinator.DefaultTimeoutSeconds;

        /// <summary>
        /// Scene の読込実装（GPT レビュー R1）。<b>本番もテストも同じ経路を通る。</b>
        /// 既定は Unity の非同期ロード。テストは実装ごと差し替えて、開始失敗・遅延完了を同じ流れで検査する。
        /// </summary>
        public IAreaSceneLoader Loader { get; set; } = new UnitySceneLoader();

        /// <inheritdoc />
        public ServiceInitResult Initialize()
        {
            GameplayClockProvider.Current = _clock;
            return ServiceInitResult.Ok("Area transition ready.");
        }

        /// <summary>
        /// カタログと受付条件を注入する。Area の初期化担当が、その Scene の条件源を差し直す。
        /// カタログは Session と同じく作り直さない（同じ参照なら調停役も作り直さない）。
        /// </summary>
        public bool Bind(AreaCatalogData catalogData, IAreaTransitionConditions conditions)
        {
            if (_catalog == null)
            {
                if (!AreaCatalog.TryBuild(catalogData, out AreaCatalog built, out var errors))
                {
                    foreach (string e in errors)
                    {
                        GameLog.Error(LogCategory.Scene, "Area catalog error: " + e);
                    }

                    return false;
                }

                _catalog = built;
            }

            _conditions = conditions;
            if (_coordinator == null)
            {
                _coordinator = new AreaTransitionCoordinator(
                    _catalog, new ConditionsRelay(this), _clock, _timeoutSeconds);
            }

            return true;
        }

        /// <summary>
        /// 遷移を要求する（§6.1／§6.2）。受理できなければ理由を返し、その場に留まる。
        /// </summary>
        public AreaTransitionDecision TryTravel(StableId areaId, StableId entryId)
        {
            if (_coordinator == null)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
            }

            var request = new AreaTransitionRequest(areaId, entryId);
            AreaTransitionDecision decision = _coordinator.TryRequest(request);
            if (!decision.Accepted)
            {
                return decision;
            }

            // 新しい遷移が始まったので、前回の終端失敗の表示は畳む。
            HasTerminalFailure = false;
            TerminalFailureReason = null;

            // 受理した。活動を閉じ、GameMode を Loading へ（§6.2 手順 3）。
            CloseCurrentArea();
            GameModeProvider.Current?.ChangeMode(GameMode.Loading);

            // 手順 4（進行中の行動を同期停止）と 5（中断後の値を採取）は Port が 1 か所でまとめて行う。
            CaptureActors();

            _running = StartCoroutine(TravelRoutine(request, decision.TransitionId));
            return decision;
        }

        private IEnumerator TravelRoutine(AreaTransitionRequest request, int transitionId)
        {
            if (!_catalog.TryGetEntry(request.AreaId, request.EntryId, out AreaEntryInfo entry))
            {
                FailAndRestoreOldArea(transitionId);
                yield break;
            }

            AreaPendingArrival.Set(transitionId, request.AreaId, request.EntryId);

            if (!TryStartLoad(entry.ScenePath, out IAreaLoadOperation watched))
            {
                // 前のロードがまだ終端していない。重ねずに終端失敗にする（§6.3）。
                AreaPendingArrival.Clear();
                FailTerminal(transitionId, "前のロードが終端していないため、目的地のロードを開始できませんでした。");
                yield break;
            }

            if (!_coordinator.NotifyLoadStarted(transitionId, watched))
            {
                // 世代が進んでいた。この Coroutine はもう自分のものではない。
                yield break;
            }

            // 開始に失敗した＝旧 Scene はまだ生きている。その場に留まって活動を再開する（§6.3 の 2 行目）。
            if (watched.HasError)
            {
                AreaPendingArrival.Clear();
                FailAndRestoreOldArea(transitionId);
                yield break;
            }

            while (!watched.IsDone)
            {
                _coordinator.TickUnscaled(Time.unscaledDeltaTime);
                yield return null;
            }

            // タイムアウトしていたら、遅れて完了した目的地を活動させない（§6.3）。
            // 古い操作が終端したので、保留していた復旧をここで 1 回だけ始める。
            _coordinator.TickUnscaled(Time.unscaledDeltaTime);
            if (_coordinator.TimedOut)
            {
                if (_coordinator.TryConsumeRecovery())
                {
                    yield return RecoverToOrigin(transitionId);
                }
                else
                {
                    FailTerminal(transitionId, "ロードが監視上限を超え、復旧も開始できませんでした。");
                }

                yield break;
            }

            if (watched.HasError)
            {
                if (_coordinator.TryBeginRecovery(transitionId))
                {
                    yield return RecoverToOrigin(transitionId);
                }
                else
                {
                    FailTerminal(transitionId, "目的地のロードが失敗し、復旧も開始できませんでした。");
                }

                yield break;
            }

            // sceneLoaded だけで Ready 扱いにしない（§6.2 手順 7）。Bind と復元は到着側の初期化担当が行う。
            if (!_coordinator.NotifyBinding(transitionId))
            {
                yield break;
            }

            // 到着側の AreaInitializer が Ready を確定するまで待つ。
            float waited = 0f;
            while (!AreaPendingArrival.IsPreparedFor(transitionId) && waited < BindTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!AreaPendingArrival.IsPreparedFor(transitionId))
            {
                // 旧 Scene は破棄済み。元 Area を 1 回だけ再ロードして復旧する（§6.3 の 3 行目）。
                if (_coordinator.TryBeginRecovery(transitionId))
                {
                    yield return RecoverToOrigin(transitionId);
                }
                else
                {
                    FailTerminal(transitionId, "到着側の準備が完了せず、復旧も開始できませんでした。");
                }

                yield break;
            }

            if (!_coordinator.NotifyReady(transitionId))
            {
                yield break;
            }

            // ---- ここで初めて活動を許可する（GPT レビュー R2 の指摘 1） ----
            //
            // 到着側は「準備できた」と言うだけで、活動してよいかは判断しない。
            // 世代・対象・タイムアウトを確認した所有者＝ここが許可を出す。
            // 到着側が自分で Ready にしてしまうと、監視がタイムアウトしたあとに遅れて届いた
            // 目的地が、そのまま操作可能になってしまう。
            AreaContext arrived = FindCurrentContext();
            if (arrived == null)
            {
                FailTerminal(transitionId, "到着先に AreaContext がありません。");
                yield break;
            }

            arrived.Activate();
            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);

            // 後始末は世代一致でだけ行う。通知の中で次の遷移が始まっていたら、
            // この世代の Release は成立せず、共有領域も触らない。
            bool released = _coordinator.Release(transitionId);
            if (released)
            {
                ClearPendingTransfer();
                AreaPendingArrival.Clear();
                _running = null;
            }

            // 完了を通知する。<b>後始末をすべて終えてから出す</b>（§6.2 末尾）。
            //
            // 購読者はこの中から次の遷移を要求してよい。通知を先に出すと、
            // 排他がまだ Ready のままなので要求が「遷移中」で弾かれるし、
            // 要求が通ったとしても、戻ってきた先の後始末が新しい遷移の
            // 到着要求（AreaPendingArrival）を消してしまう。順序がそのまま正しさになる。
            if (released)
            {
                ArrivalCompleted?.Invoke(request.AreaId);
            }
        }

        /// <summary>
        /// 到着して活動が許可された直後に 1 度だけ発火する（§6.2 手順 9）。
        /// <b>購読者はこの中から次の遷移を要求してよい。</b>後始末は世代一致でのみ行うので、
        /// 新しい遷移が始まっていれば旧世代の後始末は成立しない。
        /// </summary>
        public event System.Action<StableId> ArrivalCompleted;

        /// <summary>
        /// 旧 Scene が破棄されたあとの失敗から、<b>元 Area を 1 回だけ再ロードして復旧する</b>（§6.3 の 3 行目）。
        ///
        /// 運んでいた Actor 値はまだ捨てていないので、復旧先でもそのまま復元できる。
        /// 復旧先も失敗したら Error 表示に留め、無限再試行しない（§6.3 の最終行）。
        /// </summary>
        private IEnumerator RecoverToOrigin(int transitionId)
        {
            // 認可（復旧を 1 回だけ行う判断）は呼び出し側が済ませている。
            // ここで再度 TryBeginRecovery を呼ぶと、タイムアウト経路では
            // すでに消費済みのため弾かれてしまう（実際に踏んだ）。
            if (!_hasPendingTransfer
                || !_catalog.TryGetEntry(
                    _pendingTransfer.OriginAreaId, _pendingTransfer.OriginEntryId, out AreaEntryInfo origin))
            {
                FailTerminal(transitionId, "復旧元のエリア・入口を解決できませんでした。");
                yield break;
            }

            GameLog.Warning(LogCategory.Scene,
                "Recovering to the origin area: " + _pendingTransfer.OriginAreaId.Value);

            AreaPendingArrival.Set(transitionId, _pendingTransfer.OriginAreaId, _pendingTransfer.OriginEntryId);

            if (!TryStartLoad(origin.ScenePath, out IAreaLoadOperation recovery))
            {
                // 失敗したロードがまだ終端していない。重ねて読み込まない（§6.3）。
                FailTerminal(transitionId, "前のロードが終端していないため、復旧ロードを開始できませんでした。");
                yield break;
            }

            // 復旧にも監視を置く。無界に待つと、戻れないまま暗転が続く（GPT レビュー R2 の指摘 3）。
            float watchedSeconds = 0f;
            while (!recovery.IsDone && watchedSeconds < RecoveryTimeoutSeconds)
            {
                watchedSeconds += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!recovery.IsDone)
            {
                // 監視は切れたが操作は生きている。捨てず、終端するまで Launcher へも戻さない。
                FailTerminal(transitionId,
                    "復旧ロードが " + RecoveryTimeoutSeconds.ToString("0.##") + " 秒以内に完了しませんでした。");
                yield break;
            }

            if (recovery.HasError)
            {
                FailTerminal(transitionId, "復旧ロードが失敗しました。");
                yield break;
            }

            float waited = 0f;
            while (!AreaPendingArrival.IsPreparedFor(transitionId) && waited < BindTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!AreaPendingArrival.IsPreparedFor(transitionId))
            {
                FailTerminal(transitionId, "復旧先の初期化が完了しませんでした。");
                yield break;
            }

            // 復旧できた。元の場所で活動を再開する（許可はここが出す）。
            AreaContext recovered = FindCurrentContext();
            if (recovered == null)
            {
                FailTerminal(transitionId, "復旧先に AreaContext がありません。");
                yield break;
            }

            recovered.Activate();
            _coordinator.NotifyFailed(transitionId, oldSceneUsable: true);
            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            ClearPendingTransfer();
            AreaPendingArrival.Clear();
            RecoveredCount++;
            _running = null;
        }

        /// <summary>
        /// 復旧もできない（§6.3 の最終行）。<b>凍結したまま黙って放置しない。</b>
        ///
        /// Gameplay 時計は止めたままにする（壊れた状態で世界を動かさない）が、
        /// 理由を残して <see cref="TerminalFailed"/> で知らせ、
        /// <see cref="TryReturnToLauncher"/> という戻り道を用意する。
        /// 以前は理由も戻り道も無く、暗転のまま何もできなかった（GPT レビュー R2 の指摘 3）。
        ///
        /// <b>自動で再試行はしない。</b>戻るかどうかはプレイヤーが決める（§6.3「無限再試行しない」）。
        /// </summary>
        private void FailTerminal(int transitionId, string reason)
        {
            AreaPendingArrival.Clear();
            _coordinator.NotifyFailed(transitionId, oldSceneUsable: false);
            _running = null;

            HasTerminalFailure = true;
            TerminalFailureReason = reason;
            TerminalFailureCount++;

            GameLog.Error(LogCategory.Scene, "Area transition failed terminally: " + reason);
            TerminalFailed?.Invoke(reason);
        }

        /// <summary>終端失敗の状態にあるか（Error 表示の条件。§6.3 の最終行）。</summary>
        public bool HasTerminalFailure { get; private set; }

        /// <summary>終端失敗の理由（表示・診断用）。失敗していなければ null。</summary>
        public string TerminalFailureReason { get; private set; }

        /// <summary>終端失敗に至った回数（診断・テスト用）。</summary>
        public int TerminalFailureCount { get; private set; }

        /// <summary>終端失敗を知らせる（表示側が購読して Error UI を出す）。</summary>
        public event System.Action<string> TerminalFailed;

        /// <summary>重複ロードを断った回数（診断・テスト用）。0 でない＝重ね掛けを防いだということ。</summary>
        public int DuplicateLoadBlockedCount { get; private set; }

        /// <summary>Launcher へ戻した回数（診断・テスト用）。</summary>
        public int ReturnedToLauncherCount { get; private set; }

        /// <summary>差されていないときの戻り先（P5 の統合起動 Scene。§13.1）。</summary>
        public const string DefaultLauncherScenePath =
            "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_ExplorationTrial.unity";

        /// <summary>
        /// 戻る Scene のパス（§6.3「既存 Launcher へ戻る操作を提示」）。
        /// 起動役が自分の Scene のパスを差す。差されていなければ P5 の統合起動 Scene を使う。
        /// </summary>
        public string LauncherScenePath { get; set; } = DefaultLauncherScenePath;

        /// <summary>
        /// いま Launcher へ戻れるか。
        ///
        /// <b>生きているロード操作が終端するまでは戻れない。</b> 戻り操作も Scene のロードなので、
        /// 終端していない操作の上に重ねれば同じ事故になる
        /// （§6.3「古い操作が終端するまで新たなロードを開始しない」）。
        /// 表示側はこれが false の間、戻る操作を押せない状態にする。
        /// </summary>
        public bool CanReturnToLauncher =>
            HasTerminalFailure
            && !string.IsNullOrEmpty(LauncherScenePath)
            && (_liveOperation == null || _liveOperation.IsDone);

        /// <summary>
        /// 既存 Launcher へ戻す（§6.3 の最終行）。<b>自動では呼ばない。</b>
        /// 戻れない状態（終端失敗していない・生きているロードがある）では何もせず false を返す。
        /// </summary>
        public bool TryReturnToLauncher()
        {
            if (!HasTerminalFailure || string.IsNullOrEmpty(LauncherScenePath))
            {
                return false;
            }

            if (!TryStartLoad(LauncherScenePath, out _))
            {
                return false;
            }

            // 戻るので、運んでいた Actor 値と到着要求は捨てる。世界状態（Session）はそのまま残す。
            ClearPendingTransfer();
            AreaPendingArrival.Clear();

            HasTerminalFailure = false;
            TerminalFailureReason = null;

            // 受理の時点で止めた時計を戻す。止めたままだと、戻った先でも何も動かない。
            _clock.Thaw();
            GameModeProvider.Current?.ChangeMode(GameMode.Loading);
            ReturnedToLauncherCount++;
            return true;
        }

        /// <summary>
        /// ロード操作の唯一の入口（§6.3）。<b>前の操作が終端していなければ新しく始めない。</b>
        /// 断った回数は <see cref="DuplicateLoadBlockedCount"/> に数える。
        /// </summary>
        private bool TryStartLoad(string scenePath, out IAreaLoadOperation operation)
        {
            if (_liveOperation != null && !_liveOperation.IsDone)
            {
                DuplicateLoadBlockedCount++;
                operation = null;
                return false;
            }

            operation = (Loader ?? new UnitySceneLoader()).Load(scenePath);
            _liveOperation = operation;
            return true;
        }

        /// <summary>復旧ロードで元の場所へ戻れた回数（診断・テスト用）。</summary>
        public int RecoveredCount { get; private set; }

        /// <summary>
        /// 遷移に失敗し、<b>旧 Scene がまだ生きている</b>ときに元の活動を再開する（§6.3 の 2 行目）。
        ///
        /// 受理の時点で活動を閉じ、Gameplay 時計も止めている（手順 3）。
        /// 失敗したまま放置すると「その場に留まるが移動も CD 進行もできない」状態になるので、
        /// AreaReady・モードだけでなく<b>時計も戻す</b>（GPT レビュー R1）。
        /// 中断済みの調査は自動再開しない。
        /// </summary>
        private void FailAndRestoreOldArea(int transitionId)
        {
            AreaContext context = FindCurrentContext();
            bool usable = context != null;

            _coordinator.NotifyFailed(transitionId, oldSceneUsable: usable);

            if (usable)
            {
                context.ReopenAfterFailedTransition();
                GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            }

            ClearPendingTransfer();
            AreaPendingArrival.Clear();
            _running = null;
        }

        /// <summary>
        /// 旧 Scene の Actor 値を採取する（§6.2 手順 4 → 5）。
        /// Port が無い Scene（Actor を載せていない構成）では何も運ばない。
        /// </summary>
        private void CaptureActors()
        {
            var port = Object.FindFirstObjectByType<AreaActorTransferPort>();
            if (port == null)
            {
                _hasPendingTransfer = false;
                return;
            }

            AreaContext origin = FindCurrentContext();
            StableId originArea = origin != null ? origin.AreaId : default;
            StableId originEntry = origin != null ? origin.EntryId : default;

            _pendingTransfer = port.Capture(CompanionIds.Inumaru, originArea, originEntry);
            _hasPendingTransfer = true;
        }

        private void CloseCurrentArea()
        {
            AreaContext context = FindCurrentContext();
            if (context != null)
            {
                context.CloseForTransition();
            }
        }

        private static AreaContext FindCurrentContext()
        {
            // Scene に 1 つだけの前提（§5.1）。常駐側からは明示参照が持てないのでここだけ探索する。
            return Object.FindFirstObjectByType<AreaContext>();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(GameplayClockProvider.Current, _clock))
            {
                // 自分が差したものだけを外す（所有者一致。§5.2「Provider の解除は所有者一致で行う」）。
                GameplayClockProvider.Current = null;
            }
        }

        /// <summary>差し替え可能な条件源へ橋渡しする（Scene ごとに条件源が入れ替わるため）。</summary>
        private sealed class ConditionsRelay : IAreaTransitionConditions
        {
            private readonly AreaTransitionService _owner;

            public ConditionsRelay(AreaTransitionService owner)
            {
                _owner = owner;
            }

            private IAreaTransitionConditions Source => _owner._conditions;

            public bool IsAreaReady => Source != null && Source.IsAreaReady;
            public GameMode Mode => Source != null ? Source.Mode : GameMode.Loading;
            public bool IsPlayerAlive => Source != null && Source.IsPlayerAlive;
            public bool IsPlayerBusy => Source == null || Source.IsPlayerBusy;
            public bool IsEncounterActive => Source != null && Source.IsEncounterActive;
        }
    }

    /// <summary>
    /// 到着要求の受け渡し（§6.2 手順 7）。遷移サービスが所有し、Scene をまたいで参照される。
    ///
    /// <b>世代を持つ。</b> 以前は無条件に完了へできたため、古い Scene の初期化担当が
    /// 新しい遷移を完了させられた（GPT レビュー R1 の中位指摘）。
    /// 到着側は自分が処理しているエリア・入口と世代が一致するときだけ完了を記録できる。
    ///
    /// 持つのは ID と世代だけ。Actor の値は遷移サービスが保持し、世界状態は Session が持つ。
    /// </summary>
    public static class AreaPendingArrival
    {
        /// <summary>この要求の実行世代。0 は「要求なし」。</summary>
        public static int TransitionId { get; private set; }

        /// <summary>到着先のエリア。</summary>
        public static StableId AreaId { get; private set; }

        /// <summary>到着先の入口。</summary>
        public static StableId EntryId { get; private set; }

        /// <summary>要求が入っているか。</summary>
        public static bool HasPending => TransitionId != 0;

        private static bool _prepared;

        /// <summary>遷移の開始時に設定する。</summary>
        public static void Set(int transitionId, StableId areaId, StableId entryId)
        {
            TransitionId = transitionId;
            AreaId = areaId;
            EntryId = entryId;
            _prepared = false;
        }

        /// <summary>
        /// 到着側が<b>準備完了</b>を報告する。エリア・入口・世代がすべて一致するときだけ効く。
        ///
        /// 渡す世代は、到着側が<b>初期化を始めた時点で受け取ったトークン</b>であること。
        /// ここで <see cref="TransitionId"/> を読み直すと自分自身との比較になり、照合の意味が無くなる
        /// （GPT レビュー R2 の指摘 4）。
        ///
        /// これは「準備できた」の報告であって、活動の許可ではない。許可は所有者が出す。
        /// </summary>
        public static bool TryMarkPrepared(int transitionId, StableId areaId, StableId entryId)
        {
            if (transitionId == 0 || transitionId != TransitionId
                || !areaId.Equals(AreaId) || !entryId.Equals(EntryId))
            {
                MismatchedCompletionCount++;
                return false;
            }

            _prepared = true;
            return true;
        }

        /// <summary>指定の世代について準備できているか。</summary>
        public static bool IsPreparedFor(int transitionId) =>
            _prepared && transitionId != 0 && transitionId == TransitionId;

        /// <summary>一致しない完了要求を無視した回数（診断・テスト用）。</summary>
        public static int MismatchedCompletionCount { get; private set; }

        /// <summary>片付ける（成功・失敗・新規開始・テストの後始末）。</summary>
        public static void Clear()
        {
            TransitionId = 0;
            AreaId = default;
            EntryId = default;
            _prepared = false;
        }

        /// <summary>診断カウンタを戻す（テストの後始末）。</summary>
        public static void ResetDiagnostics()
        {
            MismatchedCompletionCount = 0;
        }
    }

    /// <summary>
    /// 既定の Scene 読込（P5-03b 修正）。Unity の非同期ロードを <see cref="IAreaLoadOperation"/> として返す。
    /// <b>開始に失敗しても例外を投げず</b>、エラーを持つ操作を返して同じ経路で観測させる。
    /// </summary>
    public sealed class UnitySceneLoader : IAreaSceneLoader
    {
        /// <inheritdoc />
        public IAreaLoadOperation Load(string scenePath)
        {
            try
            {
                return new AreaSceneLoadOperation(SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single));
            }
            catch (System.Exception e)
            {
                GameLog.Error(LogCategory.Scene, "Failed to start scene load '" + scenePath + "': " + e.Message);
                return new AreaSceneLoadOperation(null);
            }
        }
    }
}
