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
    public sealed class AreaTransitionService : MonoBehaviour, IGameService, IAreaRespawnTravel
    {
        private readonly GameplayClockGate _clock = new GameplayClockGate();

        /// <summary>いま飛んでいる遷移が死亡再開のものなら、その世代（0 は通常の移動）。</summary>
        private int _respawnTransitionId;

        /// <summary>
        /// いま走っている死亡再開が、どの再開要求に対するものか（§9.1。GPT レビュー R6 の指摘 1）。
        /// <b>Scene 側の実行役ではなく常駐のここが持つ。</b> 到着側 Scene が壊れて実行役ごと
        /// 失われても、成功・失敗をこの ID で確定できる。
        /// </summary>
        private int _respawnRequestId;

        private AreaCatalog _catalog;

        /// <summary>接続一覧（P5.5 §3.1）。P5 の構成では null のまま。</summary>
        private AreaConnectionCatalog _connections;
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

            // 再開の遷移役を常駐で公開する（GPT レビュー R7 の指摘 1）。
            // 到着側 Scene の初期化が落ちても、再試行の実行経路はここから取り直せる。
            CampaignRespawnTravelProvider.Current = this;
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
                    _catalog, ConditionsRelaySource, _clock, _timeoutSeconds);
            }

            return true;
        }

        /// <summary>
        /// 接続一覧を注入する（P5.5 §3.1）。
        ///
        /// <b>null は「接続を持たない構成」</b>——P5 の Area はこちらで、
        /// 出入口は従来どおり行き先の Area／入口を直接指す。必須にすると
        /// 既存の Scene が一斉に動かなくなる。
        ///
        /// カタログと同じく<b>一度作ったら作り直さない</b>。走っている遷移が受理時に固定した
        /// 接続を見ている最中に差し替わると、途中で値が変わる（§3.1 末尾）。
        /// </summary>
        public bool BindConnections(AreaConnectionData connectionData)
        {
            if (connectionData == null || _connections != null)
            {
                return true;
            }

            if (!AreaConnectionCatalog.TryBuild(connectionData, out AreaConnectionCatalog built,
                    out System.Collections.Generic.List<string> errors))
            {
                foreach (string e in errors)
                {
                    GameLog.Error(LogCategory.Scene, "Area connection error: " + e);
                }

                return false;
            }

            _connections = built;
            return true;
        }

        /// <summary>接続一覧（無ければ null。§3.1）。</summary>
        public AreaConnectionCatalog Connections => _connections;

        private AreaSlideTransitionRunner _slideRunner;
        private ConditionsRelay _conditionsRelay;

        /// <summary>
        /// スライド遷移の実行役（P5.5 §6.2。工程 P55-04b）。<b>初回参照で作る。</b>
        ///
        /// <b>同じ常駐が Fade と Slide の両方を持つ</b>——§8 が「同じ遷移排他・世代・Scene 操作管理を
        /// 共有し、別サービスが同時ロードを発行しない」と定めているので、別の常駐にはできない。
        /// </summary>
        public AreaSlideTransitionRunner Slide => _slideRunner ??= new AreaSlideTransitionRunner(this);

        /// <summary>
        /// P5.5 の追加読込・撤去の実装（<b>テストが差し替える</b>）。
        /// P5 の <see cref="Loader"/>（Single 読込）とは別の口である（§2）。
        /// </summary>
        public IAreaSceneHost SlideSceneHost { get; set; } = new UnityAreaSceneHost();

        /// <summary>スライドで到着を確定した回数（診断・テスト用）。</summary>
        public int SlideCommittedCount => _slideRunner != null ? _slideRunner.CommittedCount : 0;

        /// <summary>スライドを出発側へ戻した回数（診断・テスト用）。</summary>
        public int SlideRolledBackCount => _slideRunner != null ? _slideRunner.RolledBackCount : 0;

        /// <summary>
        /// 到着が確定した回数（Fade と Slide の合計。診断・テスト用）。
        /// <b>経路を問わず「着いた回数」</b>を見たいテストはこちらを使う。
        /// </summary>
        public int ArrivalCount => CompletedCount + SlideCommittedCount;

        /// <summary>差し替え可能な条件源への橋（スライドの受付条件も同じものを見る）。</summary>
        internal IAreaTransitionConditions Conditions => ConditionsRelaySource;

        private ConditionsRelay ConditionsRelaySource => _conditionsRelay ??= new ConditionsRelay(this);

        /// <summary>スライドの実行役が自分の Coroutine を回すための口。</summary>
        internal void StartSlideRoutine(IEnumerator routine)
        {
            StartCoroutine(routine);
        }

        /// <summary>
        /// 到着を進行へ記録する（§6.2 手順 9／§4.1「Commit 時に行う」）。
        ///
        /// <b>Area の初期化担当ではなく、活動を許可した所有者が記録する。</b>
        /// 初期化の時点で記録すると、隔離された Prepared や、タイムアウト後に遅れて着いた Scene が
        /// 「訪問済み」を残してしまう（§11 の E06）。
        /// </summary>
        internal void NoteArrival(StableId areaId)
        {
            GameSessionProvider.Current?.MarkVisited(areaId);
        }

        /// <summary>
        /// 受付条件の窓口を差し直す（スライド経路の Commit／Rollback から呼ぶ）。
        ///
        /// <b>戻したときに差し直さないと、二度と遷移できなくなる。</b>
        /// 到着側の初期化担当は Prepared の時点で <see cref="Bind"/> を通るので、
        /// そこで窓口は到着 Area のものへ変わる。そのまま Rollback して到着 Scene を撤去すると、
        /// 窓口は<b>破棄済みの部品</b>を指したまま残る——受付条件が読めず、
        /// 以後どの遷移も「分からない＝安全側」で断られる（実際に踏んだ）。
        /// </summary>
        internal void RebindConditions(IAreaTransitionConditions conditions)
        {
            if (conditions != null)
            {
                _conditions = conditions;
            }
        }

        /// <summary>完了通知を出す（スライド経路から呼ぶ。購読者は同じ event を見る）。</summary>
        internal void RaiseArrivalCompleted(StableId areaId)
        {
            ArrivalCompleted?.Invoke(areaId);
        }

        /// <summary>
        /// 直近に受理した接続（診断・テスト用）。<b>受理できたときだけ更新する</b>——
        /// 拒否された要求で上書きすると、走っている遷移の正本が別の接続に見える。
        /// </summary>
        public AreaConnectionSnapshot LastAcceptedConnection { get; private set; }

        /// <summary>接続を指定して受理された回数（診断・テスト用）。</summary>
        public int ConnectionTravelCount { get; private set; }

        /// <summary>
        /// <b>接続を指定して</b>遷移を要求する（P5.5 §3.1／§6.1）。
        ///
        /// 受理できた時点で接続の値（見せ方・向き・所要秒・到着入口）を固定する。
        /// 走っている遷移はこの固定値だけを見る——途中で SO を読み直すと、
        /// 編集中の値が演出の最中に効いてしまう（§3.1 末尾）。
        ///
        /// <b>見せ方で経路が分かれる。</b> Slide は Additive の受け渡しとスライド演出を伴う
        /// <see cref="Slide"/> の経路へ、それ以外は従来の Single／Fade 経路へ流す
        /// （§3.1「既存未指定は Fade にして既存 Data の挙動を保持」）。
        /// </summary>
        public AreaTransitionDecision TryTravel(in AreaConnectionSnapshot connection)
        {
            AreaTransitionDecision decision = connection.IsSlide
                ? Slide.TryTravel(connection)
                : TryTravel(connection.ToAreaId, connection.EntryId);
            if (decision.Accepted)
            {
                LastAcceptedConnection = connection;
                ConnectionTravelCount++;
            }

            return decision;
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

            // <b>スライドと排他を共有する</b>（§8）。同じ常駐が両方を持つので、ここで見るだけでよい。
            if (_slideRunner != null && _slideRunner.IsTransitioning)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.AlreadyTransitioning);
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

        /// <summary>
        /// 死亡再開の遷移を要求する（P5-08。§9.1 手順 4）。
        ///
        /// 通常の移動と<b>2 つだけ違う</b>。受付条件が §9.1 のもの（GameOver から出る）になること、
        /// そして<b>Actor 値を運ばないこと</b>。§9.1 手順 6 は到着先で全回復・CD 解除を求めているので、
        /// 死ぬ直前の HP や CD を持ち越しては意味が逆になる。保留中の持ち越しも捨てる。
        /// </summary>
        public AreaTransitionDecision TryRespawnTravel(StableId areaId, StableId entryId, int respawnRequestId)
        {
            if (_coordinator == null)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
            }

            if (_slideRunner != null && _slideRunner.IsTransitioning)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.AlreadyTransitioning);
            }

            var request = new AreaTransitionRequest(areaId, entryId, isRespawn: true);
            AreaTransitionDecision decision = _coordinator.TryRequest(request);
            if (!decision.Accepted)
            {
                return decision;
            }

            HasTerminalFailure = false;
            TerminalFailureReason = null;
            _respawnTransitionId = decision.TransitionId;
            _respawnRequestId = respawnRequestId;

            CloseCurrentArea();
            GameModeProvider.Current?.ChangeMode(GameMode.Loading);

            // 死んだときの値は運ばない（手順 6）。前の移動の持ち越しが残っていたら捨てる。
            ClearPendingTransfer();

            _running = StartCoroutine(TravelRoutine(request, decision.TransitionId));
            return decision;
        }

        /// <summary>
        /// 死亡再開の遷移が失敗したとき、<b>元 Area への復旧ではなく再開画面へ戻す</b>
        /// （§9.1 末尾。GPT レビュー R6 の指摘 1）。
        ///
        /// 復旧は「運んでいた Actor 値を持ったまま元の場所へ戻る」処理だが、死亡再開は
        /// 手順 6 のために<b>持ち越しを捨ててから</b>出発する。そのため復旧は必ず
        /// 「復旧元のエリア・入口を解決できませんでした」で終端し、
        /// 再試行できるはずの失敗が Error 表示になっていた。
        /// そもそも死亡再開の「元の場所」は死んだ場所なので、戻る先として正しくない。
        /// </summary>
        /// <returns>死亡再開として処理したら true（呼び出し側は復旧へ進まない）。</returns>
        private bool FailRespawnInsteadOfRecovery(int transitionId)
        {
            if (!IsRespawnTransition(transitionId))
            {
                return false;
            }

            // 放棄として残す（遅れて読み終わった Scene に自己許可させない）。
            AreaPendingArrival.Abandon();
            _coordinator.NotifyFailed(transitionId, oldSceneUsable: false);
            _running = null;

            // Error 表示にはしない。再開画面からもう一度試せる失敗なので、戻り道は「再開する」。
            HandleRespawnFailure(transitionId);
            return true;
        }

        /// <summary>
        /// 再開地点の入口を解決する（§9.1 手順 4）。
        /// Scene 側の実行役が使えないとき、常駐の受付がここからカタログを借りる
        /// （GPT レビュー R8 の指摘 1）。
        /// </summary>
        public bool TryGetRespawnEntry(out AreaEntryInfo entry)
        {
            if (_catalog != null)
            {
                return _catalog.TryGetRespawnEntry(out entry);
            }

            entry = default;
            return false;
        }

        /// <summary>この遷移が死亡再開のものか（世代一致で見る）。</summary>
        private bool IsRespawnTransition(int transitionId) =>
            _respawnTransitionId != 0 && transitionId == _respawnTransitionId;

        /// <summary>
        /// この遷移が死亡再開のものなら、<b>同じ再開要求のまま</b>再開画面へ戻す（§9.1 末尾）。
        ///
        /// 以前は Scene の <see cref="CampaignRespawnRunner"/> を <c>FindFirstObjectByType</c> で
        /// 探して通知していた。旧 Scene を破棄したあとの失敗では<b>実行役がどこにも居ない</b>ので、
        /// 失敗が誰にも届かず、再開画面が二度と出なかった（GPT レビュー R6 の指摘 1）。
        /// 常駐の Session が持つ調停役へ、常駐のここが直接確定する。
        /// </summary>
        private bool HandleRespawnFailure(int transitionId)
        {
            if (!IsRespawnTransition(transitionId))
            {
                return false;
            }

            int requestId = _respawnRequestId;
            _respawnTransitionId = 0;
            _respawnRequestId = 0;

            // 主人公は死んだままなので、探索へは戻さない（§9.1 末尾「失敗時は再開画面から再試行可能」）。
            GameModeProvider.Current?.ChangeMode(GameMode.GameOver);

            CampaignRespawnCoordinator respawn = GameSessionProvider.Current?.Respawn;
            if (respawn != null && requestId != 0)
            {
                respawn.NotifyFailed(requestId);
            }

            RespawnFailureCount++;
            return true;
        }

        /// <summary>
        /// 死亡再開の到着を<b>成功地点で</b>確定する（§9.1 手順 7。GPT レビュー R6 の指摘 1）。
        ///
        /// 以前は到着側の <c>AreaInitializer</c> が Actor を全回復した直後に完了扱いにしていた。
        /// そのあとにも門の復元など失敗しうる段が残っており、そこで落ちると
        /// 段階はすでに Idle なので<b>失敗通知を受理できず、再開の再試行が成立しなかった</b>。
        /// 世代・到着準備・活動許可を確認したここが、許可を出したあとに確定する。
        /// </summary>
        private void CompleteRespawn(int transitionId)
        {
            if (!IsRespawnTransition(transitionId))
            {
                return;
            }

            int requestId = _respawnRequestId;
            _respawnTransitionId = 0;
            _respawnRequestId = 0;

            CampaignRespawnCoordinator respawn = GameSessionProvider.Current?.Respawn;
            if (respawn != null && requestId != 0)
            {
                respawn.NotifyArrived(requestId);
            }
        }

        /// <summary>死亡再開が失敗して再開画面へ戻った回数（診断・テスト用）。</summary>
        public int RespawnFailureCount { get; private set; }

        private IEnumerator TravelRoutine(AreaTransitionRequest request, int transitionId)
        {
            if (!_catalog.TryGetEntry(request.AreaId, request.EntryId, out AreaEntryInfo entry))
            {
                FailAndRestoreOldArea(transitionId);
                yield break;
            }

            // <b>先読みを捨ててからロードを発行する</b>（§5 末尾／§8 末尾。工程 P55-04e）。
            // 先読みを持たない構成では 1 フレームも使わない（正常系に待ちを足さない）。
            if (_slideRunner != null)
            {
                yield return _slideRunner.DiscardStagedForSingleLoad();

                // <b>終端を見届けられなければロードを発行しない</b>（GPT 受入①）。
                //
                // 以前は時間切れでも先へ進んでいた。Single 側の監視（<c>_liveOperation</c>）は
                // Additive の先読み操作を持っていないので、そこでは重なりを止められない。
                // 進めないときは<b>終端失敗</b>にして、プレイヤーに再操作させる——
                // 死亡再開なら再開画面へ戻り、もう一度 Submit できる（§9.1）。
                if (!_slideRunner.StagedDiscardCompleted)
                {
                    FailTerminal(transitionId,
                        "先読みが終端していないため、目的地のロードを開始できませんでした。"
                        + "しばらく待ってからもう一度操作してください。");
                    yield break;
                }
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
                if (FailRespawnInsteadOfRecovery(transitionId))
                {
                    yield break;
                }

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
                if (FailRespawnInsteadOfRecovery(transitionId))
                {
                    yield break;
                }

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
                // 死亡再開なら、復旧ではなく再開画面へ戻す（下記）。
                if (FailRespawnInsteadOfRecovery(transitionId))
                {
                    yield break;
                }

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
            AdoptArrivedBundle();

            AreaContext arrived = FindCurrentContext();
            if (arrived == null)
            {
                FailTerminal(transitionId, "到着先に AreaContext がありません。");
                yield break;
            }

            // Single 読込で載っている Scene は置き換わった。台帳をその場で合わせ直す。
            _slideRunner?.NotifySingleLoadCompleted();

            NoteArrival(request.AreaId);
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

                // 死亡再開の完了は<b>ここ</b>で確定する（§9.1 手順 7）。
                // 活動許可より前に確定すると、そのあとの失敗で再試行できなくなる。
                CompleteRespawn(transitionId);
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

            _slideRunner?.NotifySingleLoadCompleted();

            NoteArrival(_pendingTransfer.OriginAreaId);
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
            // 消さずに<b>放棄</b>として残す（GPT レビュー R3 の指摘 1）。
            // 消すと「要求なし＝直開き」と区別できず、キャンセルできないロードが
            // 遅れて Scene を読み終えたときに、その Scene が自分で活動を始めてしまう。
            AreaPendingArrival.Abandon();
            _coordinator.NotifyFailed(transitionId, oldSceneUsable: false);
            _running = null;

            HasTerminalFailure = true;
            TerminalFailureReason = reason;
            TerminalFailureCount++;

            GameLog.Error(LogCategory.Scene, "Area transition failed terminally: " + reason);
            TerminalFailed?.Invoke(reason);

            HandleRespawnFailure(transitionId);
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

        /// <summary>Launcher へ<b>戻り終えた</b>回数（診断・テスト用）。開始だけでは増えない。</summary>
        public int ReturnedToLauncherCount { get; private set; }

        /// <summary>Launcher へ戻れなかった回数（診断・テスト用）。</summary>
        public int ReturnToLauncherFailedCount { get; private set; }

        /// <summary>いま Launcher へ戻る途中か（表示側が操作を止めるため）。</summary>
        public bool IsReturningToLauncher => _returning;

        // Coroutine のハンドルでは持たない。即座に終わる Coroutine は StartCoroutine が
        // 返る前に走り切るので、ハンドルの代入と実際の進行がずれる（実際に踏んだ）。
        private bool _returning;

        /// <summary>
        /// 戻り先（§6.3「既存 Launcher へ戻る操作を提示」）。
        ///
        /// <b>既存の Launcher Scene に統一する</b>（GPT レビュー R3 の指摘 3）。
        /// 以前は P5 の統合起動 Scene を差していたが、あれは開くと自動で A へ進むので、
        /// 「安全に戻る」はずの操作が壊れた流れへ即座に押し戻していた。
        /// Launcher は何も自動で始めないので、戻り先として正しい。
        /// </summary>
        public const string DefaultLauncherScenePath =
            "Assets/_Project/Scenes/SCN_System_Launcher.unity";

        /// <summary>戻り先の Scene パス。差し替えはテスト・将来の構成変更のため。</summary>
        public string LauncherScenePath { get; set; } = DefaultLauncherScenePath;

        /// <summary>戻りロードの監視上限（unscaled 秒）。ここも無界に待たない。</summary>
        public float ReturnTimeoutSeconds { get; set; } = AreaTransitionCoordinator.DefaultTimeoutSeconds;

        /// <summary>
        /// いま Launcher へ戻る操作を始められるか。
        ///
        /// <b>生きているロード操作が終端するまでは始められない。</b> 戻り操作も Scene のロードなので、
        /// 終端していない操作の上に重ねれば同じ事故になる
        /// （§6.3「古い操作が終端するまで新たなロードを開始しない」）。
        /// 表示側はこれが false の間、戻る操作を押せない状態にする。
        /// </summary>
        public bool CanReturnToLauncher =>
            HasTerminalFailure
            && !IsReturningToLauncher
            && !string.IsNullOrEmpty(LauncherScenePath)
            && (_liveOperation == null || _liveOperation.IsDone)
            // <b>先読み・撤去も見る</b>（GPT 追加①）。<c>_liveOperation</c> は Single の
            // ロードしか管理していないので、これだけでは Additive の先読みを迂回できてしまう。
            && !(_slideRunner != null && _slideRunner.HasLiveSceneOperation);

        /// <summary>
        /// 既存 Launcher へ戻る操作を<b>始める</b>（§6.3 の最終行）。<b>自動では呼ばない。</b>
        ///
        /// 返り値は「戻り終えた」ではなく<b>「戻りを始めた」</b>。
        /// ロードの発行に成功しただけで戻れたことにすると、開始に失敗した操作まで成功扱いになる
        /// （GPT レビュー R3 の指摘 3）。片付け（Snapshot の破棄・時計の解凍・Error 表示の解除）は
        /// <b>ロードが完了してから</b>行う。失敗したら理由を差し替えて停止状態のまま留まる。
        /// </summary>
        public bool TryBeginReturnToLauncher()
        {
            if (!CanReturnToLauncher)
            {
                return false;
            }

            _returning = true;
            GameModeProvider.Current?.ChangeMode(GameMode.Loading);
            StartCoroutine(ReturnToLauncherRoutine());
            return true;
        }

        /// <summary>
        /// 戻りロードを完了まで見届ける。失敗したら停止状態を維持する。
        ///
        /// <b>ロードを始める前に、通常の遷移と同じ手順で Scene 操作の所有を解く</b>
        /// （GPT 追加①）。ここを飛ばすと、先読みが走ったまま Single を撃つ経路が
        /// <b>退避だけに残る</b>——入口ごとに条件を足すのではなく、同じ手順を通す。
        /// </summary>
        private IEnumerator ReturnToLauncherRoutine()
        {
            if (_slideRunner != null)
            {
                yield return _slideRunner.DiscardStagedForSingleLoad();
                if (!_slideRunner.StagedDiscardCompleted)
                {
                    _returning = false;
                    ReturnToLauncherFailedCount++;
                    TerminalFailureReason =
                        "先読み・撤去が終端していないため、Launcher へ戻るロードを開始できませんでした。"
                        + "しばらく待ってからもう一度操作してください。";
                    GameLog.Error(LogCategory.Scene, "Return to launcher blocked: " + TerminalFailureReason);
                    TerminalFailed?.Invoke(TerminalFailureReason);
                    yield break;
                }
            }

            if (!TryStartLoad(LauncherScenePath, out IAreaLoadOperation operation))
            {
                _returning = false;
                ReturnToLauncherFailedCount++;
                TerminalFailureReason = "前のロードが終端していないため、Launcher へ戻れませんでした。";
                GameLog.Error(LogCategory.Scene, "Return to launcher blocked: " + TerminalFailureReason);
                TerminalFailed?.Invoke(TerminalFailureReason);
                yield break;
            }

            float waited = 0f;
            while (!operation.IsDone && waited < ReturnTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            _returning = false;

            if (!operation.IsDone || operation.HasError)
            {
                // 戻れなかった。<b>Error 表示も時計の凍結もそのまま</b>にして、理由だけ差し替える。
                ReturnToLauncherFailedCount++;
                TerminalFailureReason = operation.IsDone
                    ? "Launcher へ戻るロードが失敗しました。"
                    : "Launcher へ戻るロードが " + ReturnTimeoutSeconds.ToString("0.##") + " 秒以内に完了しませんでした。";
                GameLog.Error(LogCategory.Scene, "Return to launcher failed: " + TerminalFailureReason);
                TerminalFailed?.Invoke(TerminalFailureReason);
                yield break;
            }

            // 戻れた。ここで初めて片付ける。世界状態（Session）はそのまま残す。
            _slideRunner?.NotifySingleLoadCompleted();
            ClearPendingTransfer();
            AreaPendingArrival.Clear();
            HasTerminalFailure = false;
            TerminalFailureReason = null;

            // 受理の時点で止めた時計を戻す。止めたままだと、戻った先でも何も動かない。
            _clock.Thaw();
            ReturnedToLauncherCount++;
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

            // 死亡再開の失敗なら、上で戻した探索モードを打ち消して再開画面へ返す。
            HandleRespawnFailure(transitionId);
        }

        /// <summary>
        /// 旧 Scene の Actor 値を採取する（§6.2 手順 4 → 5）。
        /// Port が無い Scene（Actor を載せていない構成）では何も運ばない。
        /// </summary>
        internal void CaptureActorsForTransition()
        {
            CaptureActors();
        }

        private void CaptureActors()
        {
            AreaActorTransferPort port = ResolveTransferPort();
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

        private AreaContext FindCurrentContext()
        {
            AreaRuntimeBundle bundle = CurrentBundle();
            if (bundle != null && bundle.Context != null)
            {
                return bundle.Context;
            }

            return FallbackSearch<AreaContext>();
        }

        /// <summary>出発側の Actor 採取口を引く（P5.5 §4.3）。</summary>
        private AreaActorTransferPort ResolveTransferPort()
        {
            AreaRuntimeBundle bundle = CurrentBundle();
            if (bundle != null && bundle.TransferPort != null)
            {
                return bundle.TransferPort;
            }

            return FallbackSearch<AreaActorTransferPort>();
        }

        /// <summary>
        /// いま触るべき Area の参照集合を引く（P5.5 §4.3）。
        ///
        /// 常駐が現行を指定していればそれ。していなければ、Area がちょうど 1 つのときに限りそれを使う
        /// （直開き・P3.5／P4 の単一 Area 構成）。2 つ以上載っていて指定が無いなら、
        /// ここで当て推量せず null を返す。
        /// </summary>
        private AreaRuntimeBundle CurrentBundle()
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            if (bundle != null)
            {
                return bundle;
            }

            return AreaBundleDirectory.TryGetSingle(out bundle) ? bundle : null;
        }

        /// <summary>
        /// 束から引けなかったときの互換経路（全 Scene 検索）。
        ///
        /// 束を載せていない Scene（P3.5／P4 の既存構成・最小テスト構成）を動かし続けるために残す。
        /// <b>回数を数える。</b> P5.5 の Scene でここを通ったら配線漏れなので、検査で落とせるようにする。
        /// </summary>
        private T FallbackSearch<T>() where T : Object
        {
            BundleFallbackCount++;
            return Object.FindFirstObjectByType<T>();
        }

        /// <summary>束から引けず全 Scene 検索へ落ちた回数（診断・テスト用）。</summary>
        public int BundleFallbackCount { get; private set; }

        /// <summary>
        /// 到着した Area の束を現行として採用する（P5.5 §4.3）。
        ///
        /// <b>現行の指定は常駐が持つ。</b> Scene 側に名乗らせると、先読みで載っただけの Area が
        /// 現行になってしまう。束を載せていない Scene では何もしない（互換経路が引き続き動く）。
        /// </summary>
        private void AdoptArrivedBundle()
        {
            if (AreaBundleDirectory.TryGetSingle(out AreaRuntimeBundle bundle))
            {
                CurrentAreaProvider.TrySetCurrent(this, bundle);
            }
        }

        /// <summary>
        /// 先読みの後始末を進める（P5.5 §5／§8 の 5 行目。工程 P55-04c）。
        ///
        /// <b>止められないロードの終端はここでしか観測できない。</b> 監視を諦めた遷移は
        /// 望みを取り下げて戻るだけなので、遅れて着いた Scene を撤去する者が要る。
        /// 距離による先読み（§5）も、いずれここへ乗る。
        /// </summary>
        private void Update()
        {
            _slideRunner?.Pump();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(GameplayClockProvider.Current, _clock))
            {
                // 自分が差したものだけを外す（所有者一致。§5.2「Provider の解除は所有者一致で行う」）。
                GameplayClockProvider.Current = null;
            }

            CampaignRespawnTravelProvider.ReleaseIfOwner(this);
            CurrentAreaProvider.ReleaseIfOwner(this);
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

        /// <summary>いまの状態。</summary>
        public static AreaArrivalState State { get; private set; } = AreaArrivalState.None;

        /// <summary>生きた到着要求が入っているか（<see cref="AreaArrivalState.Pending"/> のときだけ true）。</summary>
        public static bool HasPending => State == AreaArrivalState.Pending;

        /// <summary>
        /// 到着側が<b>自分で活動許可を出してよいか</b>（＝直開き）。
        ///
        /// <b>「到着トークンが無い」を直開きの証拠にしない。</b> 終端失敗のあと、
        /// キャンセルできないロードが遅れて Scene を読み終えると、そこには生きた要求が無い。
        /// トークンの有無だけで判断すると、<b>失敗して止めたはずの遷移先が自分で動き出す</b>
        /// （GPT レビュー R3 の指摘 1）。直開きを許すのは「遷移が 1 つも走っていない」ときだけ。
        /// </summary>
        public static bool SelfActivationAllowed => State == AreaArrivalState.None;

        private static bool _prepared;

        /// <summary>遷移の開始時に設定する（<see cref="AreaArrivalState.Pending"/> へ）。</summary>
        public static void Set(int transitionId, StableId areaId, StableId entryId)
        {
            TransitionId = transitionId;
            AreaId = areaId;
            EntryId = entryId;
            _prepared = false;
            State = AreaArrivalState.Pending;
        }

        /// <summary>
        /// 要求を<b>放棄する</b>（終端失敗。§6.3 の最終行）。
        ///
        /// 消さずに放棄として残す。消すと「要求なし＝直開き」と区別できなくなり、
        /// 遅れて着いた Scene が自分で活動を始めてしまう。
        /// 放棄後は準備完了の報告も受け付けない（誰も許可を出さないので、着いても動かない）。
        /// </summary>
        public static void Abandon()
        {
            _prepared = false;
            State = AreaArrivalState.Abandoned;
        }

        /// <summary>直開きを断った回数（診断・テスト用）。0 でない＝遅れて着いた Scene を止めたということ。</summary>
        public static int BlockedSelfActivationCount { get; private set; }

        /// <summary>直開きを断ったことを数える。</summary>
        public static void NoteBlockedSelfActivation()
        {
            BlockedSelfActivationCount++;
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
            if (State != AreaArrivalState.Pending
                || transitionId == 0 || transitionId != TransitionId
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
            _prepared && State == AreaArrivalState.Pending
            && transitionId != 0 && transitionId == TransitionId;

        /// <summary>一致しない完了要求を無視した回数（診断・テスト用）。</summary>
        public static int MismatchedCompletionCount { get; private set; }

        /// <summary>
        /// 片付ける（遷移の成功・旧 Scene が生きている失敗・Launcher への復帰完了・テストの後始末）。
        /// <b>終端失敗では呼ばない</b>（放棄として残す。<see cref="Abandon"/>）。
        /// </summary>
        public static void Clear()
        {
            TransitionId = 0;
            AreaId = default;
            EntryId = default;
            _prepared = false;
            State = AreaArrivalState.None;
        }

        /// <summary>診断カウンタを戻す（テストの後始末）。</summary>
        public static void ResetDiagnostics()
        {
            MismatchedCompletionCount = 0;
            BlockedSelfActivationCount = 0;
        }
    }

    /// <summary>到着要求の状態（P5-03b 修正。GPT レビュー R3 の指摘 1）。</summary>
    public enum AreaArrivalState
    {
        /// <summary>遷移していない。この Scene を開いたのは直開き。</summary>
        None = 0,

        /// <summary>遷移中で、到着を待っている。許可を出すのは遷移サービス。</summary>
        Pending = 1,

        /// <summary>終端失敗で放棄した。遅れて着いても<b>誰も許可を出さない</b>。</summary>
        Abandoned = 2,
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
