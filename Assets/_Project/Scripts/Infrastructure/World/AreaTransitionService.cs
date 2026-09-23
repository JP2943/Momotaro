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
        /// 到着側が値を受け取る（§6.2 手順 7）。<b>1 度だけ</b>取り出せる。
        /// Scene をまたぐ static を増やさないため、遷移の所有者であるこのサービスが保持する。
        /// </summary>
        public bool TryConsumePendingTransfer(out AreaTransferSnapshot snapshot)
        {
            snapshot = _pendingTransfer;
            bool had = _hasPendingTransfer;
            _pendingTransfer = default;
            _hasPendingTransfer = false;
            return had;
        }

        /// <summary>テストが不正な目的地を注入するための差し替え口（P12）。null なら通常のロード。</summary>
        public System.Func<string, AsyncOperation> LoadOverride { get; set; }

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
                _coordinator = new AreaTransitionCoordinator(_catalog, new ConditionsRelay(this), _clock);
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

            AreaPendingArrival.Set(request.AreaId, request.EntryId);

            AsyncOperation op = LoadOverride != null
                ? LoadOverride(entry.ScenePath)
                : SceneManager.LoadSceneAsync(entry.ScenePath, LoadSceneMode.Single);

            var watched = new AreaSceneLoadOperation(op);
            if (!_coordinator.NotifyLoadStarted(transitionId, watched))
            {
                // 世代が進んでいた。この Coroutine はもう自分のものではない。
                yield break;
            }

            while (!watched.IsDone)
            {
                _coordinator.TickUnscaled(Time.unscaledDeltaTime);
                yield return null;
            }

            if (watched.HasError)
            {
                AreaPendingArrival.Clear();
                FailAndRestoreOldArea(transitionId);
                yield break;
            }

            // sceneLoaded だけで Ready 扱いにしない（§6.2 手順 7）。Bind と復元は到着側の初期化担当が行う。
            if (!_coordinator.NotifyBinding(transitionId))
            {
                yield break;
            }

            // 到着側の AreaInitializer が Ready を確定するまで待つ。
            float waited = 0f;
            while (!AreaPendingArrival.Completed && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!AreaPendingArrival.Completed)
            {
                // 旧 Scene は破棄済みなので、ここで戻す先は無い（§6.3 の 3 行目は復旧ロードの担当）。
                _coordinator.NotifyFailed(transitionId);
                yield break;
            }

            if (!_coordinator.NotifyReady(transitionId))
            {
                yield break;
            }

            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            _coordinator.Release(transitionId);
            _running = null;
        }

        /// <summary>
        /// 遷移に失敗したとき、<b>旧 Scene がまだ生きていれば元の活動を再開する</b>（§6.3 の 2 行目）。
        ///
        /// 受理の時点で活動を閉じている（手順 3）ので、失敗したまま放置すると
        /// 「その場に留まるが何も操作できない」状態になる。実 Scene の PlayMode（P12）で踏んだ。
        /// 中断済みの調査は自動再開しない。
        /// </summary>
        private void FailAndRestoreOldArea(int transitionId)
        {
            _coordinator.NotifyFailed(transitionId);

            AreaContext context = FindCurrentContext();
            if (context != null)
            {
                context.ReopenAfterFailedTransition();
                GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            }
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
    /// 遷移で「次にどの入口へ到着するか」を運ぶだけの小さな受け渡し（§6.2 手順 7）。
    ///
    /// Scene をまたぐので static にせざるを得ないが、<b>持つのは ID 2 つと完了フラグだけ</b>。
    /// Actor の値は <c>AreaTransferSnapshot</c>、世界状態は Session が持つ。ここを万能の置き場にしない。
    /// </summary>
    public static class AreaPendingArrival
    {
        /// <summary>到着先のエリア。</summary>
        public static StableId AreaId { get; private set; }

        /// <summary>到着先の入口。</summary>
        public static StableId EntryId { get; private set; }

        /// <summary>要求が入っているか。</summary>
        public static bool HasPending { get; private set; }

        /// <summary>到着側の初期化が Ready を確定したか。</summary>
        public static bool Completed { get; private set; }

        /// <summary>遷移の開始時に設定する。</summary>
        public static void Set(StableId areaId, StableId entryId)
        {
            AreaId = areaId;
            EntryId = entryId;
            HasPending = true;
            Completed = false;
        }

        /// <summary>到着側が Ready を確定したときに呼ぶ。</summary>
        public static void MarkCompleted()
        {
            Completed = true;
        }

        /// <summary>片付ける（失敗・新規開始・テストの後始末）。</summary>
        public static void Clear()
        {
            AreaId = default;
            EntryId = default;
            HasPending = false;
            Completed = false;
        }
    }
}
