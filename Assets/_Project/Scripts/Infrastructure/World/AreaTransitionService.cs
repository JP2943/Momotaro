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
        /// 調停役を作る前に設定する。テストは短くして復旧経路を実時間で通す。
        /// </summary>
        public float TimeoutSeconds { get; set; } = AreaTransitionCoordinator.DefaultTimeoutSeconds;

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
                    _catalog, new ConditionsRelay(this), _clock, TimeoutSeconds);
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

            IAreaLoadOperation watched = (Loader ?? new UnitySceneLoader()).Load(entry.ScenePath);
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
                    FailTerminal(transitionId);
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
                    FailTerminal(transitionId);
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
            while (!AreaPendingArrival.IsCompletedFor(transitionId) && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!AreaPendingArrival.IsCompletedFor(transitionId))
            {
                // 旧 Scene は破棄済み。元 Area を 1 回だけ再ロードして復旧する（§6.3 の 3 行目）。
                if (_coordinator.TryBeginRecovery(transitionId))
                {
                    yield return RecoverToOrigin(transitionId);
                }
                else
                {
                    FailTerminal(transitionId);
                }

                yield break;
            }

            if (!_coordinator.NotifyReady(transitionId))
            {
                yield break;
            }

            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            _coordinator.Release(transitionId);
            ClearPendingTransfer();
            AreaPendingArrival.Clear();
            _running = null;
        }

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
                FailTerminal(transitionId);
                yield break;
            }

            GameLog.Warning(LogCategory.Scene,
                "Recovering to the origin area: " + _pendingTransfer.OriginAreaId.Value);

            AreaPendingArrival.Set(transitionId, _pendingTransfer.OriginAreaId, _pendingTransfer.OriginEntryId);
            IAreaLoadOperation recovery = (Loader ?? new UnitySceneLoader()).Load(origin.ScenePath);

            while (!recovery.IsDone)
            {
                yield return null;
            }

            if (recovery.HasError)
            {
                FailTerminal(transitionId);
                yield break;
            }

            float waited = 0f;
            while (!AreaPendingArrival.IsCompletedFor(transitionId) && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!AreaPendingArrival.IsCompletedFor(transitionId))
            {
                FailTerminal(transitionId);
                yield break;
            }

            // 復旧できた。元の場所で活動を再開する。
            _coordinator.NotifyFailed(transitionId, oldSceneUsable: true);
            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            ClearPendingTransfer();
            AreaPendingArrival.Clear();
            RecoveredCount++;
            _running = null;
        }

        /// <summary>復旧もできない。Error 表示に留める（§6.3 の最終行）。Gameplay 時計は止めたまま。</summary>
        private void FailTerminal(int transitionId)
        {
            AreaPendingArrival.Clear();
            _coordinator.NotifyFailed(transitionId, oldSceneUsable: false);
            _running = null;
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

        private static bool _completed;

        /// <summary>遷移の開始時に設定する。</summary>
        public static void Set(int transitionId, StableId areaId, StableId entryId)
        {
            TransitionId = transitionId;
            AreaId = areaId;
            EntryId = entryId;
            _completed = false;
        }

        /// <summary>
        /// 到着側が Ready を確定したときに呼ぶ。<b>エリア・入口・世代がすべて一致するときだけ</b>効く。
        /// 一致しない呼び出しは無視して数える（古い Scene の初期化担当が新しい遷移を完了させない）。
        /// </summary>
        public static bool TryMarkCompleted(int transitionId, StableId areaId, StableId entryId)
        {
            if (transitionId == 0 || transitionId != TransitionId
                || !areaId.Equals(AreaId) || !entryId.Equals(EntryId))
            {
                MismatchedCompletionCount++;
                return false;
            }

            _completed = true;
            return true;
        }

        /// <summary>指定の世代について完了しているか。</summary>
        public static bool IsCompletedFor(int transitionId) =>
            _completed && transitionId != 0 && transitionId == TransitionId;

        /// <summary>一致しない完了要求を無視した回数（診断・テスト用）。</summary>
        public static int MismatchedCompletionCount { get; private set; }

        /// <summary>片付ける（成功・失敗・新規開始・テストの後始末）。</summary>
        public static void Clear()
        {
            TransitionId = 0;
            AreaId = default;
            EntryId = default;
            _completed = false;
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
