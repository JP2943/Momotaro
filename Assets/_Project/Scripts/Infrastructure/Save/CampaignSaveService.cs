using System;
using System.Collections;
using System.IO;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.Save
{
    /// <summary>正常終了・タイトル復帰の結果（P6A-05。仕様 §9）。</summary>
    public enum SaveExitOutcome
    {
        /// <summary>保存が完了した（または保存対象が無い）。</summary>
        Saved = 0,

        /// <summary>保存できなかった。選択肢を示す。</summary>
        Failed = 1,

        /// <summary>待ちが打ち切られた（遷移・死亡が終わらない）。</summary>
        TimedOut = 2,
    }

    /// <summary>
    /// オートセーブの常駐ホスト（P6A-02／05。仕様 §8〜§10）。<see cref="SaveCoordinator"/> を Unity の時間と終了導線へ繋ぐ。
    ///
    /// <b>保存対象は「冒険」だけ。</b> P6 campaign の New Game／Continue が <see cref="BindAdventure"/> で結ぶまで何もしない——
    /// P3.5／P4／P5／P5.5 の試遊はこのサービスの影響を受けない。
    ///
    /// <b>採取は LateUpdate。</b> このフレームの命中・死亡・報酬・クリアの調停が終わった区切りで採る（仕様 §8）。
    /// 遷移の途中・死亡の解決中は採らずに待つ。
    ///
    /// <b>終了。</b> 通常のウィンドウ終了要求（<c>Application.wantsToQuit</c>）とゲーム内の終了導線の両方で、
    /// 最新の保存が終わるまで待つ。<c>OnApplicationQuit</c> の非同期処理には頼らない。失敗したら
    /// 「もう一度」「ゲームへ戻る」「保存せずに終了」を示す。強制終了・電源断は保証外。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampaignSaveService : MonoBehaviour, IGameService
    {
        private SaveCoordinator _coordinator;
        private AreaTransitionService _transitions;
        private GameSessionBootService _sessions;
        private bool _quitApproved;
        private bool _exiting;
        private GameMode? _modeBeforeExit;

        /// <inheritdoc />
        public string ServiceName => "CampaignSave";

        /// <summary>
        /// 保存先のディレクトリ。<b>Editor 試遊・出荷ビルド・テストで分ける</b>（仕様 §10）。
        /// テストは最初の <see cref="BindAdventure"/> より前に一時ディレクトリを差す。
        /// </summary>
        public string SaveDirectory { get; set; }

        /// <summary>書込担当を差し替える（テスト用。最初の <see cref="BindAdventure"/> より前）。</summary>
        public ISaveExecutor ExecutorOverride { get; set; }

        /// <summary>ファイル操作を差し替える（故障注入のテスト用）。</summary>
        public ISaveFileSystem FileSystemOverride { get; set; }

        /// <summary>調停役（未作成なら null）。</summary>
        public SaveCoordinator Coordinator => _coordinator;

        /// <summary>終了・タイトル復帰の保存待ちの最中か（入力を止める）。</summary>
        public bool IsExiting => _exiting;

        /// <summary>終了の保存に失敗して、選択を待っているか。</summary>
        public bool AwaitingExitChoice { get; private set; }

        /// <summary>直近の終了・タイトル復帰の結果。</summary>
        public SaveExitOutcome LastExitOutcome { get; private set; }

        /// <summary>保存待ちの上限（秒。unscaled）。</summary>
        public float ExitTimeoutSeconds { get; set; } = 15f;

        /// <summary>
        /// テストの保存先（設定されていれば <see cref="DefaultDirectory"/> より優先）。<b>常駐が立つ前から効く</b>——
        /// 常駐の起動と同じフレームにタイトル等が保存を覗いても、試遊者の保存へ触れない。テストは後始末で null に戻す。
        /// </summary>
        public static string TestDirectoryOverride { get; set; }

        /// <summary>既定の保存先（persistentDataPath の下。Editor と出荷で分ける）。</summary>
        public static string DefaultDirectory =>
            !string.IsNullOrEmpty(TestDirectoryOverride)
                ? TestDirectoryOverride
                : Path.Combine(Application.persistentDataPath, Application.isEditor ? "saves_editor" : "saves");

        /// <inheritdoc />
        public ServiceInitResult Initialize()
        {
            // 何も作らない。冒険を結んだときに作る（試遊へ影響を出さない）。
            Application.wantsToQuit += OnWantsToQuit;
            return ServiceInitResult.Ok("Campaign save service ready (no adventure yet).");
        }

        /// <summary>遷移サービスと Session の所有者を結ぶ（Bootstrap が呼ぶ）。</summary>
        public void BindServices(AreaTransitionService transitions, GameSessionBootService sessions)
        {
            _transitions = transitions;
            _sessions = sessions;
        }

        private CampaignAdventureFlow _flow;

        /// <summary>
        /// New Game／Continue の手順（常駐。タイトル Scene が入れ替わっても結果の購読が残る）。
        /// </summary>
        public CampaignAdventureFlow Flow =>
            _flow ??= (_transitions != null && _sessions != null
                ? new CampaignAdventureFlow(_transitions, _sessions, this)
                : null);

        /// <summary>
        /// 保存の調停役を用意する（無ければ作る）。保存先の排他ロックもここで取る。
        /// </summary>
        public SaveCoordinator EnsureCoordinator()
        {
            if (_coordinator != null)
            {
                return _coordinator;
            }

            string directory = string.IsNullOrEmpty(SaveDirectory) ? DefaultDirectory : SaveDirectory;
            var store = new SaveFileStore(directory, "slot0", FileSystemOverride);
            _coordinator = new SaveCoordinator(store, ExecutorOverride ?? new ThreadSaveExecutor())
            {
                CanCapture = CanCaptureNow,
                ActorSource = ResolveActorSource,
            };

            if (!store.TryAcquireLock(out string error))
            {
                GameLog.Error(LogCategory.Boot, "Save slot lock failed: " + error);
            }

            return _coordinator;
        }

        /// <summary>
        /// 冒険を結ぶ（New Game の初期化後・Continue の採用後）。
        /// <paramref name="alreadySavedRevision"/> は Load 直後ならその版、New Game なら -1。
        /// </summary>
        public void BindAdventure(GameSessionState session, CampaignCatalog campaign, long alreadySavedRevision)
        {
            EnsureCoordinator().Bind(session, campaign, alreadySavedRevision);
        }

        /// <summary>冒険を外す（タイトルへ戻ったあと等）。</summary>
        public void UnbindAdventure()
        {
            _coordinator?.Bind(null, null, -1);
        }

        // ---------------------------------------------------------------- ゲーム内メニュー（仮 UI。P6A-05）

        private bool _menuOpen;

        /// <summary>ゲーム内メニュー（タイトルへ・終了）が開いているか。</summary>
        public bool IsMenuOpen => _menuOpen;

        /// <summary>
        /// メニューを開く（冒険中・探索中だけ）。入力は UI へ切り替わり、攻撃・Interact・遷移の受付が止まる。
        /// </summary>
        public bool OpenMenu()
        {
            if (_menuOpen || _exiting || _coordinator == null || _coordinator.Session == null)
            {
                return false;
            }

            IGameModeService modes = GameModeProvider.Current;
            if (modes == null || modes.Current != GameMode.Exploration)
            {
                return false;
            }

            SuspendGameplayInput();
            _menuOpen = true;
            return true;
        }

        /// <summary>メニューを閉じてゲームへ戻る。</summary>
        public void CloseMenu()
        {
            if (!_menuOpen)
            {
                return;
            }

            _menuOpen = false;
            ResumeGameplayInput();
        }

        private readonly PadMenuNavigator _navigator = new PadMenuNavigator();

        /// <summary>どれかのパッドで Start（Menu）が押されたか。</summary>
        private static bool AnyStartPressed()
        {
            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                if (Gamepad.all[i].startButton.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }
        private bool _choiceShownLastFrame;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (_coordinator == null || _coordinator.Session == null)
            {
                return;
            }

            // 終了前の保存に失敗したときの選択（0：もう一度保存 1：ゲームへ戻る 2：保存せずに終了）。
            if (AwaitingExitChoice)
            {
                if (!_choiceShownLastFrame)
                {
                    _navigator.Reset(0);
                    _choiceShownLastFrame = true;
                }

                int choice = _navigator.Poll(3, null, out bool back);
                if (choice == 0)
                {
                    ChooseRetry(thenQuit: true);
                }
                else if (choice == 1 || back)
                {
                    ChooseBackToGame();
                }
                else if (choice == 2)
                {
                    ChooseQuitWithoutSaving();
                }

                return;
            }

            _choiceShownLastFrame = false;
            if (_exiting)
            {
                return;
            }

            if (!_menuOpen)
            {
                // お地蔵様のメニューを Esc で閉じた同じフレームでは開かない。
                CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
                bool shrineJustClosed = shrines != null && (shrines.IsMenuOpen || shrines.LastClosedFrame == Time.frameCount);
                bool open = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || AnyStartPressed();
                if (open && !shrineJustClosed && OpenMenu())
                {
                    _navigator.Reset(0);
                }

                return;
            }

            // 0：タイトルへ戻る 1：終了する 2：ゲームへ戻る
            int item = _navigator.Poll(3, null, out bool cancelled);
            bool toTitle = item == 0 || (keyboard != null && keyboard.tKey.wasPressedThisFrame);
            bool quit = item == 1 || (keyboard != null && keyboard.qKey.wasPressedThisFrame);
            bool close = item == 2 || cancelled
                || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || AnyStartPressed();
            if (toTitle)
            {
                _menuOpen = false;
                RequestReturnToTitle();
            }
            else if (quit)
            {
                _menuOpen = false;
                RequestQuit();
            }
            else if (close)
            {
                CloseMenu();
            }
        }

        private void LateUpdate()
        {
            if (_coordinator == null)
            {
                return;
            }

            // Session が別のものへ差し替わった（New Game 以外の経路）なら、古い冒険へ書き続けない。
            GameSessionState current = GameSessionProvider.Current;
            if (_coordinator.Session != null && !ReferenceEquals(current, _coordinator.Session)
                && (_sessions == null || !_sessions.HasCandidate))
            {
                _coordinator.Bind(null, null, -1);
            }

            _coordinator.Pump();
        }

        /// <summary>
        /// 採取してよいか。<b>遷移の前後をまたがない</b>・<b>死亡の解決中は採らない</b>（HP 0 の通常保存を作らない）。
        /// </summary>
        private bool CanCaptureNow()
        {
            if (_transitions != null && _transitions.IsTransitionUnsettled)
            {
                return false;
            }

            GameSessionState session = _coordinator?.Session;
            if (session != null && session.Respawn.Phase != CampaignRespawnPhase.Idle)
            {
                return false;
            }

            GameMode? mode = GameModeProvider.Current?.Current;
            if (mode == GameMode.GameOver || mode == GameMode.Loading)
            {
                return false;
            }

            AreaContext active = _transitions != null ? _transitions.ActiveContext : null;
            return active != null && active.IsAreaReady;
        }

        private bool TryResolveRespawnEntry(out AreaEntryInfo entry)
        {
            if (_transitions != null)
            {
                return _transitions.TryGetRespawnEntry(out entry);
            }

            entry = default;
            return false;
        }

        private ISaveActorSource ResolveActorSource()
        {
            return _transitions != null && _transitions.TryGetActiveTransferPort(out AreaActorTransferPort port)
                ? port
                : null;
        }

        // ---------------------------------------------------------------- 終了・タイトル復帰（P6A-05）

        /// <summary>
        /// 保存を終えてから進む（終了・タイトル復帰の共通部分）。入力を止め、遷移・死亡の確定を待ち、最新を採って書き、完了を待つ。
        /// </summary>
        public IEnumerator SaveBeforeExit(Action<SaveExitOutcome> done)
        {
            _exiting = true;
            AwaitingExitChoice = false;
            SaveCoordinator c = _coordinator;
            if (c == null || c.Session == null)
            {
                _exiting = false;
                LastExitOutcome = SaveExitOutcome.Saved;
                done?.Invoke(SaveExitOutcome.Saved);
                yield break;
            }

            float waited = 0f;

            // 死亡の解決を待つ（仕様 §8）。死んだまま終わろうとしているなら、再開を進めてから保存する——
            // 死亡確定の HP 0 を通常の保存にしない。解決できなければ成功表示をしない。
            if (c.Session.Respawn.IsAwaitingRespawn)
            {
                GameSessionState dead = c.Session;
                CampaignRespawnRequestProcedure.Execute(dead, dead.Respawn, TryResolveRespawnEntry,
                    CampaignRespawnTravelProvider.Current);
            }

            while (c.Session != null && c.Session.Respawn.Phase != CampaignRespawnPhase.Idle && waited < ExitTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            // 入力を止める（Action Map を UI へ。メニュー操作が攻撃・Interact へ流れない。遷移の受付も止まる）。
            SuspendGameplayInput();

            c.Session.Changes.RequestAutosave("exit");
            if (c.Status == SaveStatus.Failed)
            {
                c.RetryNow();
            }

            while ((c.IsDirty || c.IsWriting) && c.Status != SaveStatus.Failed && waited < ExitTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            SaveExitOutcome outcome;
            if (c.Status == SaveStatus.Failed)
            {
                outcome = SaveExitOutcome.Failed;
            }
            else if (c.IsDirty || c.IsWriting)
            {
                outcome = SaveExitOutcome.TimedOut;
            }
            else
            {
                outcome = SaveExitOutcome.Saved;
            }

            LastExitOutcome = outcome;
            AwaitingExitChoice = outcome != SaveExitOutcome.Saved;
            if (!AwaitingExitChoice)
            {
                _exiting = false;
            }

            done?.Invoke(outcome);
        }

        /// <summary>ゲーム内の「終了」。保存を終えてから終了する。</summary>
        public void RequestQuit()
        {
            if (_exiting)
            {
                return;
            }

            StartCoroutine(SaveBeforeExit(outcome =>
            {
                if (outcome == SaveExitOutcome.Saved)
                {
                    QuitNow();
                }
            }));
        }

        /// <summary>ゲーム内の「タイトルへ」。保存を終えてから Launcher へ戻る。</summary>
        public void RequestReturnToTitle()
        {
            if (_exiting)
            {
                return;
            }

            StartCoroutine(SaveBeforeExit(outcome =>
            {
                if (outcome == SaveExitOutcome.Saved)
                {
                    ReturnToTitleNow();
                }
            }));
        }

        /// <summary>失敗時の選択：もう一度保存して終える。</summary>
        public void ChooseRetry(bool thenQuit)
        {
            AwaitingExitChoice = false;
            _exiting = false;
            if (thenQuit)
            {
                RequestQuit();
            }
            else
            {
                RequestReturnToTitle();
            }
        }

        /// <summary>失敗時の選択：ゲームへ戻る（保存はまだできていない表示を残す）。</summary>
        public void ChooseBackToGame()
        {
            AwaitingExitChoice = false;
            _exiting = false;
            ResumeGameplayInput();
        }

        /// <summary>失敗時の選択：未保存分を失って終了する。</summary>
        public void ChooseQuitWithoutSaving()
        {
            AwaitingExitChoice = false;
            QuitNow();
        }

        private void QuitNow()
        {
            _quitApproved = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ReturnToTitleNow()
        {
            _exiting = false;
            _modeBeforeExit = null;
            UnbindAdventure();
            if (_transitions == null || !_transitions.TryBeginReturnToTitle())
            {
                GameLog.Warning(LogCategory.Scene, "Return to title could not be started.");
            }
        }

        private void SuspendGameplayInput()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null || modes.Current == GameMode.Paused)
            {
                return;
            }

            _modeBeforeExit = modes.Current;
            modes.ChangeMode(GameMode.Paused);
        }

        private void ResumeGameplayInput()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes != null && _modeBeforeExit.HasValue && modes.Current == GameMode.Paused)
            {
                modes.ChangeMode(_modeBeforeExit.Value);
            }

            _modeBeforeExit = null;
        }

        /// <summary>
        /// 通常のウィンドウ終了要求（仕様 §9）。未保存があれば<b>いったん断り</b>、保存を終えてから自分で終了する。
        /// </summary>
        private bool OnWantsToQuit()
        {
            if (_quitApproved || _coordinator == null || _coordinator.Session == null)
            {
                return true;
            }

            if (!_coordinator.IsDirty && !_coordinator.IsWriting)
            {
                return true;
            }

            RequestQuit();
            return false;
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            _coordinator?.Dispose();
            _coordinator = null;
        }

        // ---------------------------------------------------------------- 表示（仮 UI。P6A）

        private void OnGUI()
        {
            if (_coordinator == null || _coordinator.Session == null)
            {
                return;
            }

            _navigator.BeginScaled();
            try
            {
                if (AwaitingExitChoice)
                {
                    DrawExitChoice();
                    return;
                }

                if (_menuOpen)
                {
                    DrawMenu();
                    return;
                }

                DrawStatus();
            }
            finally
            {
                _navigator.EndScaled();
            }
        }

        private void DrawStatus()
        {
            string label;
            switch (_coordinator.Status)
            {
                case SaveStatus.Writing:
                    label = "保存中…";
                    break;
                case SaveStatus.Failed:
                    label = "保存できていません：" + _coordinator.LastError;
                    break;
                default:
                    label = null;
                    break;
            }

            if (_exiting)
            {
                label = "保存してから終了します…";
            }

            if (label == null)
            {
                return;
            }

            float w = PadMenuNavigator.VirtualWidth;
            float h = PadMenuNavigator.VirtualHeight;
            var rect = new Rect(w - 420f, h - 64f, 404f, 48f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            if (_coordinator.Status == SaveStatus.Failed && !_exiting && GUILayout.Button("もう一度", GUILayout.Width(80f)))
            {
                _coordinator.RetryNow();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawMenu()
        {
            const float width = 400f;
            const float height = 220f;
            var area = new Rect((PadMenuNavigator.VirtualWidth - width) * 0.5f,
                (PadMenuNavigator.VirtualHeight - height) * 0.5f, width, height);
            GUI.Box(area, "メニュー");
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 30f, area.width - 32f, area.height - 40f));
            GUILayout.Label("保存してから進みます。");
            if (_navigator.DrawItem(0, "タイトルへ戻る（T）"))
            {
                _menuOpen = false;
                RequestReturnToTitle();
            }

            if (_navigator.DrawItem(1, "終了する（Q）"))
            {
                _menuOpen = false;
                RequestQuit();
            }

            if (_navigator.DrawItem(2, "ゲームへ戻る（Esc）"))
            {
                CloseMenu();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(PadMenuNavigator.Hint);
            GUILayout.EndArea();
        }

        private void DrawExitChoice()
        {
            const float width = 560f;
            const float height = 260f;
            var area = new Rect((PadMenuNavigator.VirtualWidth - width) * 0.5f,
                (PadMenuNavigator.VirtualHeight - height) * 0.5f, width, height);
            GUI.Box(area, "保存できませんでした");
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 30f, area.width - 32f, area.height - 40f));
            GUILayout.Label(LastExitOutcome == SaveExitOutcome.TimedOut
                ? "保存が時間内に終わりませんでした。"
                : "理由：" + _coordinator.LastError);
            GUILayout.Label("最後に保存できたところまでしか残りません。");
            if (_navigator.DrawItem(0, "もう一度保存"))
            {
                ChooseRetry(thenQuit: true);
            }

            if (_navigator.DrawItem(1, "ゲームへ戻る"))
            {
                ChooseBackToGame();
            }

            if (_navigator.DrawItem(2, "保存せずに終了"))
            {
                ChooseQuitWithoutSaving();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(PadMenuNavigator.Hint);
            GUILayout.EndArea();
        }
    }
}
