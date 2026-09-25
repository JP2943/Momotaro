using System.Collections;
using System.Text.RegularExpressions;
using Momotaro.Core.Identification;
using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Navigation;
using Momotaro.Infrastructure.Navigation;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Transfer;
using Momotaro.Presentation.Cameras;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Diagnostics;
using Momotaro.Presentation.Combat;
using Momotaro.Presentation.Hud;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P5 の実 Scene 受入（仕様書 Momotaro_P5_Detailed_Spec_v1.1.md §15.3）。
    /// Builder が出荷する <c>SCN_Phase5_AreaA</c>／<c>SCN_Phase5_AreaB</c> を実際に読み込んで通す。
    ///
    /// 常駐サービス（BootstrapRoot：GameMode・Input・Session・遷移）は各テストで作り直し、
    /// 終わったら消す。静的状態を後続テストへ残さない（`CLAUDE.md`）。
    /// Scene は Build Settings に登録されている必要がある。未登録なら Skip ではなく<b>失敗</b>にする。
    ///
    /// 本クラスは工程ごとに増える。現時点で実装済みなのは P5-03b（P01・P12・P13）。
    /// </summary>
    public sealed class P5ExplorationPlayTests
    {
        private const string AreaAScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity";
        private const string AreaBScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaB.unity";
        private const string TrialScene =
            "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_ExplorationTrial.unity";
        private const string LauncherScene = "Assets/_Project/Scenes/SCN_System_Launcher.unity";

        /// <summary>既存試遊（P4）の Scene。本編型と進行の持ち場が別であることを見るために読む。</summary>
        private const string LegacyTrialScene = "Assets/_Project/Scenes/Tests/SCN_Phase4_CompanionTrial.unity";

        private static readonly StableId AreaA = new StableId("area_p5_a");
        private static readonly StableId AreaB = new StableId("area_p5_b");
        private static readonly StableId AreaAStart = new StableId("area_p5_a_start");
        private static readonly StableId AreaAFromB = new StableId("area_p5_a_from_b");
        private static readonly StableId AreaBFromA = new StableId("area_p5_b_from_a");

        private GameObject _bootstrap;

        [SetUp]
        public void SetUp()
        {
            // 前のテストの静的状態を持ち込まない（`CLAUDE.md` の PlayMode の落とし穴）。
            // <b>Action Map の後始末は「モードを戻す」だけでは足りない。</b>
            //
            // モードの提供点が別のテストや Scene に差し替えられていると、上の復帰は素通りする。
            // project-wide の Asset は PlayMode を抜けても状態が残るので、最後に<b>直接</b>
            // Gameplay マップを有効へ戻しておく（P5-08 で 11 件が一斉に無反応になって踏んだ）。
            RestoreGameplayActionMap();

            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            RespawnSubmitProvider.Current = null;
            InputReleaseGateProvider.Current = null;
            PerceptionTargetRegistry.Clear();
            GameplayClockProvider.Current = null;
            GameSessionProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            AreaPendingArrival.Clear();
            AreaPendingArrival.ResetDiagnostics();
            AreaInteractableRegistry.Clear();

            // 前の実行が残した仮想デバイスを、足す前に外しておく（枝番の 2 台目を作らない）。
            RemoveStrayTestDevices();
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            // <b>探索モードへ戻してから常駐を消す。</b>
            //
            // 入力は project-wide の `InputActionAsset`（`InputSystem.actions`）を直接 Enable／Disable する。
            // これは<b>プロジェクトの Asset</b>なので、Action Map の有効・無効が PlayMode を抜けても残る。
            // Loading・Paused・GameOver のまま常駐を壊すと Gameplay マップが無効のまま据え置かれ、
            // <b>以降に走る実キーのテストが全部無反応になる</b>——このクラスだけでなく P4 の試遊 Scene も含めて。
            // 実際に踏んだ：全件実行が 123 秒／1 失敗から 211 秒／10 失敗へ変わり、
            // 「主人公が攻撃に入らない」「E が届かない」が一斉に出た。
            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            // 先に Area Scene から抜けてから常駐を消す。逆にすると、残った AreaInitializer が
            // サービス不在で初期化に失敗し、後始末中にエラーログが出る（実際に踏んだ）。
            yield return SceneManager.LoadSceneAsync(TrialScene, LoadSceneMode.Single);
            DestroyTrialLaunchers();
            yield return null;

            // 統合起動 Scene の起動役を残さない。
            //
            // 起動役は常駐の起動を待つ Coroutine を持つ（起動順に依存しないため）。
            // ここで消しておかないと、<b>次のテストが常駐を立てた瞬間に待ちが解けて</b>
            // そのテストが要求していない遷移が走り、完了回数や到着要求を横から書き換える
            // （実際に踏んだ：全テストが 1 回多い完了を見る形で落ちた）。
            DestroyTrialLaunchers();

            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            if (_bootstrap != null)
            {
                Object.DestroyImmediate(_bootstrap);
                _bootstrap = null;
            }

            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            PerceptionTargetRegistry.Clear();
            GameplayClockProvider.Current = null;
            GameSessionProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            AreaPendingArrival.Clear();
            AreaInteractableRegistry.Clear();
            RemoveInputDevices();
            yield return null;
        }

        /// <summary>
        /// project-wide の Action Asset で Gameplay マップを有効へ戻す。
        /// Asset の Enable／Disable は PlayMode を抜けても残るので、ここで必ず戻す。
        /// </summary>
        private static void RestoreGameplayActionMap()
        {
            InputActionAsset asset = InputSystem.actions;
            if (asset == null)
            {
                return;
            }

            foreach (InputActionMap map in asset.actionMaps)
            {
                if (map.name == "Gameplay")
                {
                    map.Enable();
                }
                else if (map.enabled)
                {
                    map.Disable();
                }
            }
        }

        /// <summary>テストで足した入力デバイスを外す（次のテストへ残さない）。</summary>
        private void RemoveInputDevices()
        {
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            if (_gamepad != null)
            {
                InputSystem.RemoveDevice(_gamepad);
                _gamepad = null;
            }

            RemoveStrayTestDevices();
        }

        /// <summary>
        /// 前の実行が残した仮想デバイスを外す。
        ///
        /// デバイスは PlayMode を抜けても残るので、同じ名前で足すと <c>P5Keyboard1</c> のように
        /// <b>枝番が付いて 2 台になる</b>。そうなると Action が複数のキーボードに解決され、
        /// こちらが押したデバイスの押下が押下エッジとして立たなくなる（実際に踏んだ：
        /// UI マップは有効・Submit も有効なのに、押下回数が 0 のまま）。
        /// 名前で見分けられるのはこのテスト専用のデバイスだけなので、それだけを外す。
        /// </summary>
        private static void RemoveStrayTestDevices()
        {
            for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = InputSystem.devices[i];
                if (device != null && device.name != null && device.name.StartsWith("P5"))
                {
                    InputSystem.RemoveDevice(device);
                }
            }
        }

        private Keyboard _keyboard;
        private Gamepad _gamepad;

        /// <summary>
        /// 統合起動 Scene の起動役を消す。待ち Coroutine を次のテストへ持ち越さないため
        /// （待ちが解けると、そのテストが要求していない遷移が走る）。
        /// </summary>
        private static void DestroyTrialLaunchers()
        {
            Phase5TrialLauncher[] launchers =
                Object.FindObjectsByType<Phase5TrialLauncher>(FindObjectsSortMode.None);
            for (int i = 0; i < launchers.Length; i++)
            {
                if (launchers[i] != null)
                {
                    Object.DestroyImmediate(launchers[i].gameObject);
                }
            }
        }

        /// <summary>常駐サービスを作る（各テストで作り直す）。</summary>
        private IEnumerator CreateBootstrap()
        {
            // 常駐を立てる前に、待っている起動役が残っていないことを確かめる。
            DestroyTrialLaunchers();

            // 前の実行が残した常駐を先に消す。BootstrapRoot は DontDestroyOnLoad なので、
            // 残っていると新しい方が「重複」として Awake で破棄され、初期化が一切走らない
            // （既存の CompanionTrialScenePlayTests と同じ手順。これを省いて実際に踏んだ）。
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P5Test");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null; // Start で RunBootstrap が走る。
            Assert.IsTrue(BootstrapRoot.HasInstance, "BootstrapRoot が生成されていること。");
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐サービスの初期化が成立していること（失敗すると GameMode も遷移も差さらない）。理由="
                + BootstrapRoot.Instance.BootstrapFailure);
            Assert.IsNotNull(GameModeProvider.Current, "GameMode の提供点が差さっていること。");
            Assert.IsNotNull(BootstrapServices.Get<GameSessionBootService>(), "Session サービスが登録されていること。");
            Assert.IsNotNull(BootstrapServices.Get<AreaTransitionService>(), "遷移サービスが登録されていること。");

            // 前のテストが残した起動役が、常駐の起動で目を覚まして遷移を始めていないこと。
            // ここを素通しにすると、調停役が既定の監視上限で作られてしまい、
            // このあとの TimeoutSeconds が効かない（実際に踏んだ）。
            DestroyTrialLaunchers();
            Assert.IsNull(BootstrapServices.Get<AreaTransitionService>().Coordinator,
                "前提：まだ誰も遷移サービスへカタログを渡していない。");
        }

        private static void AssertSceneRegistered(string scenePath)
        {
            bool registered = false;
            foreach (UnityEditor.EditorBuildSettingsScene s in UnityEditor.EditorBuildSettings.scenes)
            {
                if (s.path == scenePath)
                {
                    registered = true;
                    break;
                }
            }

            Assert.IsTrue(registered,
                "Scene が Build Settings へ未登録です: " + scenePath
                + "（Momotaro / Phase 5 / Generate Exploration Trial で登録される）");
        }

        private static AreaInitializer FindInitializer()
        {
            var found = Object.FindFirstObjectByType<AreaInitializer>();
            Assert.IsNotNull(found, "AreaInitializer が Scene にありません。");
            return found;
        }

        private static AreaTransitionService Transitions()
        {
            AreaTransitionService service = BootstrapServices.Get<AreaTransitionService>();
            Assert.IsNotNull(service, "遷移サービスが常駐していません。");
            return service;
        }

        private static GameSessionBootService Sessions()
        {
            GameSessionBootService service = BootstrapServices.Get<GameSessionBootService>();
            Assert.IsNotNull(service, "Session サービスが常駐していません。");
            return service;
        }

        // ---------------------------------------------------------------- P01

        /// <summary>
        /// P5-P01：統合起動・A 直開き・B 直開きのいずれでも、<b>Session は唯一の正本</b>で、
        /// Ready を確定してから活動が始まる（§5.1／§5.2）。
        ///
        /// 直開きは「既存 P5 Session があれば再利用し、なければ新規作成し、そのエリアの既定入口」。
        /// B の既定入口は <c>area_p5_b_from_a</c>（§3.1）。
        /// </summary>
        [UnityTest]
        public IEnumerator BootstrapAndDirectOpen_CreateOneReadySession()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);

            yield return CreateBootstrap();

            // ---- A を直開き ----
            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;

            AreaInitializer initA = FindInitializer();
            Assert.IsTrue(initA.Initialized, "A の初期化が成功する。理由=" + initA.FailureReason);
            Assert.AreEqual(AreaA.Value, initA.AreaId.Value);

            var contextA = Object.FindFirstObjectByType<AreaContext>();
            Assert.IsNotNull(contextA);
            Assert.IsTrue(contextA.IsAreaReady, "Ready が確定している（§5.1 手順 8）。");
            Assert.AreEqual(1, contextA.ReadyCount, "Ready の確定は 1 回だけ。");
            Assert.AreEqual(AreaAStart.Value, contextA.EntryId.Value, "A 直開きの既定入口は開始点。");

            GameSessionBootService sessions = Sessions();
            Assert.AreEqual(1, sessions.CreatedCount, "Session はまだ 1 個しか作っていない。");
            GameSessionState session = sessions.Session;
            Assert.IsNotNull(session);
            Assert.AreSame(session, GameSessionProvider.Current, "供給点が同じ実体を差している。");
            Assert.IsTrue(session.HasVisited(AreaA), "訪問済みに入る。");

            // 進行データの窓口が Session の State を差している（徳を二重に持たない。§4.2）。
            var progress = Object.FindFirstObjectByType<PlayerProgressHolder>();
            Assert.IsNotNull(progress);
            Assert.IsTrue(progress.IsBound, "外部 State が注入されている。");
            Assert.AreSame(session.Progress, progress.State);

            // 調査記録も Area 記録を差している（§4.3）。
            var record = Object.FindFirstObjectByType<InvestigationRecordHolder>();
            Assert.IsNotNull(record);
            Assert.IsTrue(record.IsBound);
            Assert.IsTrue(session.TryGetArea(AreaA, out AreaRuntimeState areaA));
            Assert.AreSame(areaA.Investigation, record.Record);

            // モードは初期化担当が決める（§12.1）。
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);

            // ---- B を直開き。Session は作り直さない ----
            yield return SceneManager.LoadSceneAsync(AreaBScene, LoadSceneMode.Single);
            yield return null;

            AreaInitializer initB = FindInitializer();
            Assert.IsTrue(initB.Initialized, "B の初期化が成功する。理由=" + initB.FailureReason);
            Assert.AreEqual(AreaB.Value, initB.AreaId.Value);

            var contextB = Object.FindFirstObjectByType<AreaContext>();
            Assert.AreEqual(AreaBFromA.Value, contextB.EntryId.Value, "B 直開きの既定入口（§3.1）。");
            Assert.IsTrue(contextB.IsAreaReady);

            Assert.AreEqual(1, sessions.CreatedCount, "直開きでも Session を作り直さない（§5.2）。");
            Assert.AreSame(session, sessions.Session, "同じ Session の実体が続く。");
            Assert.AreSame(session, GameSessionProvider.Current);
            Assert.IsTrue(session.HasVisited(AreaB), "B も訪問済みに入る。");
            Assert.AreEqual(2, session.VisitedAreaCount);

            // 旧 Scene の AreaContext は破棄されている（常駐させない）。
            AreaContext[] contexts = Object.FindObjectsByType<AreaContext>(FindObjectsSortMode.None);
            Assert.AreEqual(1, contexts.Length, "活動中の AreaContext は 1 つだけ。");
        }

        // ---------------------------------------------------------------- P13

        /// <summary>
        /// P5-P13：完了通知の中から次の遷移を要求しても、古い実行世代が新しい遷移を壊さない（§6.2 末尾）。
        ///
        /// 実 Scene のロードを挟んだうえで、旧世代の解除・通知が効かないことを確かめる。
        /// EditMode の E09 は純粋な調停役を見ているので、ここでは<b>Coroutine が遷移をまたいで生き残る</b>
        /// 実機の条件で同じ性質を確認する。
        /// </summary>
        [UnityTest]
        public IEnumerator CompletionCallback_CanRequestTravelWithoutOldRunCorruption()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;
            Assert.IsNotNull(coordinator, "遷移サービスが調停役を持っている。");

            // ---- 再入は<b>本物の完了通知の中から</b>起こす ----
            //
            // 完了を待ってから外で要求するのは再入ではない。危ないのは、
            // 完了の通知を受けた購読者がその場で次の遷移を要求し、
            // <b>呼び出しが戻ってきた先で旧世代の後始末が走る</b>形。
            // 徳の変化のような代用の通知では、遷移の後始末との前後関係が再現できず、
            // 通知と後始末の順序を入れ替えても落ちない（GPT レビュー R2 の指摘 5）。
            var arrivals = new System.Collections.Generic.List<string>();
            AreaTransitionDecision second = default;
            bool reentered = false;
            System.Action<StableId> onArrived = id =>
            {
                arrivals.Add(id.Value);
                if (reentered)
                {
                    return;
                }

                reentered = true;
                second = service.TryTravel(AreaA, AreaAFromB);
            };

            service.ArrivalCompleted += onArrived;

            // A → B を要求する。到着の通知の中から、B → A が要求される。
            AreaTransitionDecision first = service.TryTravel(AreaB, AreaBFromA);
            Assert.IsTrue(first.Accepted, "受理される。理由=" + first.Rejection);
            int firstId = first.TransitionId;
            Assert.IsTrue(service.Clock.IsFrozen, "受理で Gameplay 時計が止まる。");

            // 遷移中の二重要求は拒否。
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning,
                service.TryTravel(AreaB, AreaBFromA).Rejection);

            float waited = 0f;
            while (!reentered && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(reentered, "完了通知の中から要求している。");
            Assert.AreEqual(1, arrivals.Count, "到着 1 回につき通知は 1 回。");
            Assert.AreEqual(AreaB.Value, arrivals[0], "到着したエリアが通知される。");
            Assert.AreEqual(1, coordinator.CompletedCount, "B への遷移は完了として数える。");

            Assert.IsTrue(second.Accepted, "通知の中からでも受理される。理由=" + second.Rejection);
            Assert.AreNotEqual(firstId, second.TransitionId, "世代が進む。");

            // 古い世代の解除は効かない（旧 Coroutine の finally 相当）。
            Assert.IsFalse(coordinator.Release(firstId), "旧世代は新しい遷移の排他を解除しない。");
            Assert.AreEqual(1, coordinator.CompletedCount);
            Assert.IsTrue(service.Clock.IsFrozen, "旧世代の解除で時計が戻ってしまわない。");

            waited = 0f;
            while (coordinator.CompletedCount < 2 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            service.ArrivalCompleted -= onArrived;

            Assert.AreEqual(2, coordinator.CompletedCount, "通知の中から始めた遷移も完了する。");
            Assert.AreEqual(2, arrivals.Count, "2 回目の到着も通知される。");
            Assert.AreEqual(AreaA.Value, arrivals[1]);

            var contextBack = Object.FindFirstObjectByType<AreaContext>();
            Assert.AreEqual(AreaA.Value, contextBack.AreaId.Value, "A へ戻っている。");
            Assert.AreEqual(AreaAFromB.Value, contextBack.EntryId.Value, "指定した入口へ到着する。");
            Assert.IsTrue(contextBack.IsAreaReady, "通知からの再入でも到着側は活動できる。");
            Assert.AreEqual(1, contextBack.ReadyCount, "活動の許可は 1 回だけ。");
            Assert.IsFalse(service.Clock.IsFrozen, "最後は時計が戻っている。");

            // Session は 1 個のまま、訪問済みは保持されている。
            Assert.AreEqual(1, Sessions().CreatedCount);
            Assert.AreEqual(2, Sessions().Session.VisitedAreaCount);
        }

        // ---------------------------------------------------------------- P12

        /// <summary>
        /// P5-P12：不備のある目的地でも Session を失わず、旧 Area の状態を保ったまま復旧する（§6.3）。
        ///
        /// 不備はテスト専用の差し替え口から注入する（処理結果を直接セットしてロード経路を飛ばさない）。
        /// ロードを開始できない状況を作り、失敗として終端することと、
        /// <b>Session と進行が生き残ること</b>を確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator InvalidDestination_RecoversWithoutLosingSession()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionBootService sessions = Sessions();
            GameSessionState session = sessions.Session;

            // 進行に値を入れておく。失敗しても消えないことを後で見る。
            var progress = Object.FindFirstObjectByType<PlayerProgressHolder>();
            progress.Grant(new RewardSnapshot(new StableId("reward_test_p12"), 17, default, false), out int granted);
            Assert.AreEqual(17, granted);
            Assert.AreEqual(17, session.Progress.Virtue);

            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;

            // ---- 受理前の拒否：カタログで解決できない目的地は、その場に留まり進行を変えない ----
            AreaTransitionDecision unknown = service.TryTravel(new StableId("area_p5_z"), new StableId("nope"));
            Assert.IsFalse(unknown.Accepted);
            Assert.AreEqual(AreaTransitionRejection.UnknownDestination, unknown.Rejection);
            Assert.IsFalse(service.Clock.IsFrozen, "拒否では時計を止めない。");
            Assert.AreEqual(AreaA.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value, "A に留まる。");
            Assert.AreEqual(17, session.Progress.Virtue, "進行は変わらない。");

            // ---- 受理後にロードが開始できない：失敗として終端する ----
            // ロードを開始できない実装へ差し替える。本番と同じ IAreaSceneLoader 経路を通る。
            service.Loader = new FailingLoader();
            AreaTransitionDecision accepted = service.TryTravel(AreaB, AreaBFromA);
            Assert.IsTrue(accepted.Accepted, "受付条件は満たしているので受理される。");

            float waited = 0f;
            while (coordinator.Phase != AreaTransitionPhase.Failed && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(AreaTransitionPhase.Failed, coordinator.Phase, "失敗として終端する。");
            Assert.AreEqual(0, coordinator.CompletedCount, "完了として数えない。");

            // ---- Session と進行が生き残っている ----
            Assert.AreEqual(1, sessions.CreatedCount, "失敗で Session を作り直さない。");
            Assert.AreSame(session, sessions.Session, "同じ Session が続く。");
            Assert.AreEqual(17, session.Progress.Virtue, "徳を失わない。");
            Assert.IsTrue(session.HasVisited(AreaA), "訪問済みも残る。");

            // 旧 Scene は生きたまま（破棄前に失敗したので、そのエリアに留まっている）。
            Assert.AreEqual(AreaA.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value);

            // ---- ここが要。API で再遷移できるだけでなく、通常操作が戻っていること ----
            //
            // 受理の時点で活動を閉じ、Gameplay 時計も止めている。失敗したまま時計を戻さないと
            // 「留まってはいるが移動も CD 進行もできない」状態になる。
            Assert.IsFalse(service.Clock.IsFrozen, "失敗後は Gameplay 時計が戻っている。");
            Assert.IsTrue(Object.FindFirstObjectByType<AreaContext>().IsAreaReady, "活動が再開している。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "探索へ戻っている。");

            // CD が実際に進む（時計が動いている証拠）。
            var guardianAfterFail = Object.FindFirstObjectByType<CompanionGuardianController>();
            Assert.IsNotNull(guardianAfterFail);
            Assert.IsTrue(guardianAfterFail.TryImportTransferSnapshot(new CompanionGuardianTransferSnapshot(1f)));
            float cdBefore = guardianAfterFail.ExportTransferSnapshot().CooldownRemaining;
            guardianAfterFail.TickGuardian(0.25f);
            Assert.Less(guardianAfterFail.ExportTransferSnapshot().CooldownRemaining, cdBefore,
                "失敗後に CD が進む（時計が止まったままではない）。");

            // 移動もできる（Warp が拒否されない）。
            var motorAfterFail = Object.FindFirstObjectByType<CompanionMotor>();
            Assert.IsNotNull(motorAfterFail);
            Vector3 posBefore = motorAfterFail.transform.position;
            motorAfterFail.WarpTo(posBefore + new Vector3(2f, 0f, 0f));
            Assert.AreNotEqual(posBefore, motorAfterFail.transform.position, "失敗後は移動できる。");

            // 差し替えを戻せば、次の遷移は普通に通る（無限に壊れたままにしない）。
            service.Loader = new UnitySceneLoader();
            AreaTransitionDecision retry = service.TryTravel(AreaB, AreaBFromA);
            Assert.IsTrue(retry.Accepted, "失敗のあとも新しい要求を受け付ける。理由=" + retry.Rejection);

            waited = 0f;
            while (coordinator.CompletedCount < 1 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, coordinator.CompletedCount, "再試行は成功する。");
            Assert.AreEqual(AreaB.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value);
            Assert.AreEqual(17, sessions.Session.Progress.Virtue, "往復しても徳は同じ。");
        }

        // ---------------------------------------------------------------- P02

        /// <summary>
        /// P5-P02：到着直後の最初の報酬が、注入先（Session の State）へ 1 回だけ入り、HUD へ反映される（§4.2）。
        ///
        /// 注入は Actor の活動開始・報酬購読より<b>前</b>に済んでいる必要がある（§5.1 手順 4 が 6 より前）。
        /// ここが逆だと、到着直後の 1 件だけがローカル State へ落ちて消える。
        /// </summary>
        [UnityTest]
        public IEnumerator FirstRewardAfterLoad_ReachesInjectedStateAndHud()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionState session = Sessions().Session;
            var hud = Object.FindFirstObjectByType<CombatPlayHud>();
            Assert.IsNotNull(hud, "エリアに HUD が配置されている。");

            // A → B へ移動し、到着直後の 1 件目を付与する。
            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;
            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);

            float waited = 0f;
            while (coordinator.CompletedCount < 1 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, coordinator.CompletedCount, "B へ到着する。");

            var progress = Object.FindFirstObjectByType<PlayerProgressHolder>();
            Assert.IsNotNull(progress, "到着先にも進行データの窓口がある。");
            Assert.IsTrue(progress.IsBound, "活動開始より前に注入されている（§5.1 手順 4）。");
            Assert.AreSame(session.Progress, progress.State, "注入先は Session の State そのもの。");

            int before = session.Progress.Virtue;
            var virtues = new System.Collections.Generic.List<int>();
            progress.VirtueChanged += v => virtues.Add(v);

            progress.Grant(new RewardSnapshot(new StableId("reward_test_p02"), 12, default, false), out int granted);
            Assert.AreEqual(12, granted, "到着直後の 1 件目が付与される。");
            Assert.AreEqual(before + 12, session.Progress.Virtue, "注入先へ入る（ローカルへ落ちない）。");
            Assert.AreEqual(1, virtues.Count, "通知は 1 回だけ。");
            Assert.AreEqual(before + 12, virtues[0], "通知の値は累計。");

            // HUD が同じ値を映す。
            var hudAfter = Object.FindFirstObjectByType<CombatPlayHud>();
            Assert.IsNotNull(hudAfter, "到着先にも HUD がある。");
            Assert.AreSame(progress, hudAfter.ProgressSource, "HUD は到着先の窓口を見ている。");

            // 旧 Scene の HUD が残っていない（購読の残留を作らない）。
            CombatPlayHud[] huds = Object.FindObjectsByType<CombatPlayHud>(FindObjectsSortMode.None);
            Assert.AreEqual(1, huds.Length, "活動中の HUD は 1 つだけ。");
        }

        // ---------------------------------------------------------------- P03

        /// <summary>
        /// P5-P03：A → B → A で、同じ Progress・調査・開通に加えて、
        /// <b>負傷 HP・Down の残り時間・CD</b> が保たれる（§4.1／§4.4）。
        ///
        /// 「ロード中に時計が進まない」ことは、ロード前後の残り値と許容する Gameplay tick 数で見る。
        /// 現実のロード時間そのものを秒数で Assert しない（§15.3 の注記）。
        /// </summary>
        [UnityTest]
        public IEnumerator AreaRoundTrip_PreservesProgressVitalsAndDownTimers()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionState session = Sessions().Session;
            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;

            // ---- 進行と世界状態を作る ----
            var progress = Object.FindFirstObjectByType<PlayerProgressHolder>();
            progress.Grant(new RewardSnapshot(new StableId("reward_test_p03"), 23, default, false), out _);
            Assert.IsTrue(session.TryGetArea(AreaA, out AreaRuntimeState areaA));
            var pointId = new StableId("point_p5_a_01");
            var flagId = new StableId("flag_p5_a_gate");
            Assert.IsTrue(areaA.Investigation.TryMarkInvestigated(pointId));
            Assert.IsTrue(areaA.TryOpen(flagId));

            // ---- 犬丸を非初期値にする：負傷・Down・復帰待ち ----
            var receiver = Object.FindFirstObjectByType<CompanionHitReceiver>();
            Assert.IsNotNull(receiver, "エリアに犬丸が居る。");
            CompanionVitals vitals = receiver.Vitals;
            vitals.Health.SetCurrent(0);
            SetPrivate(vitals, "_recoveryRemaining", 3.5f);
            typeof(CompanionVitals).GetProperty("IsDown").SetValue(vitals, true);

            // 状態機も Down にする。値だけ Down にしても状態は Follow のままで、
            // 「生存値と状態が食い違ったまま運ばれる」という現実に起きない前提になってしまう。
            // 被弾由来の強制状態は ForceHit が本物の経路なので、そこを通して前提を作る
            // （検証対象は遷移であって、Down への入り方ではない）。
            var arbiter = Object.FindFirstObjectByType<CompanionStateArbiter>();
            Assert.IsNotNull(arbiter);
            Assert.IsTrue(arbiter.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated),
                "前提：犬丸がダウン状態になる。");
            Assert.AreEqual(CompanionState.Down, Object.FindFirstObjectByType<CompanionActor>().State);

            // 守護 CD も非初期値にする。
            var guardian = Object.FindFirstObjectByType<CompanionGuardianController>();
            Assert.IsNotNull(guardian);
            Assert.IsTrue(guardian.TryImportTransferSnapshot(new CompanionGuardianTransferSnapshot(2.25f)));

            int hpBefore = vitals.Health.Current;
            float recoveryBefore = vitals.RecoveryRemaining;
            float guardianCdBefore = guardian.ExportTransferSnapshot().CooldownRemaining;

            // ---- A → B ----
            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);
            yield return WaitForCompleted(coordinator, 1);

            var receiverB = Object.FindFirstObjectByType<CompanionHitReceiver>();
            Assert.IsNotNull(receiverB, "到着先にも犬丸が居る。");
            Assert.AreNotSame(receiver, receiverB, "Scene ごとに別の実体（DontDestroyOnLoad で運ばない）。");

            Assert.AreEqual(hpBefore, receiverB.Vitals.Health.Current, "負傷 HP を引き継ぐ。");
            Assert.IsTrue(receiverB.Vitals.IsDown, "Down を引き継ぐ。");
            Assert.AreEqual(recoveryBefore, receiverB.Vitals.RecoveryRemaining, 0.2f,
                "復帰の残り時間を引き継ぐ（ロード中に時計が進まない）。");

            var guardianB = Object.FindFirstObjectByType<CompanionGuardianController>();
            Assert.AreEqual(guardianCdBefore, guardianB.ExportTransferSnapshot().CooldownRemaining, 0.2f,
                "守護 CD を引き継ぐ。");

            var actorB = Object.FindFirstObjectByType<CompanionActor>();
            Assert.AreEqual(CompanionState.Down, actorB.State, "配置状態も復元される（§4.6）。");
            Assert.AreEqual(0, actorB.IllegalTransitionCount, "不正遷移を出さない。");

            // ---- B → A ----
            Assert.IsTrue(service.TryTravel(AreaA, AreaAFromB).Accepted);
            yield return WaitForCompleted(coordinator, 2);

            // 進行と世界状態が残っている。
            Assert.AreEqual(23, session.Progress.Virtue, "徳を保持する。");
            Assert.IsTrue(session.TryGetArea(AreaA, out AreaRuntimeState areaAAgain));
            Assert.AreSame(areaA, areaAAgain, "同じ Area 記録が続く。");
            Assert.IsTrue(areaAAgain.Investigation.IsInvestigated(pointId), "調査済みを保持する。");
            Assert.IsTrue(areaAAgain.IsOpen(flagId), "門の開通を保持する。");

            var progressBack = Object.FindFirstObjectByType<PlayerProgressHolder>();
            Assert.AreSame(session.Progress, progressBack.State, "戻っても同じ Progress。");

            // 犬丸の値も往復で保たれる。
            var receiverBack = Object.FindFirstObjectByType<CompanionHitReceiver>();
            Assert.AreEqual(hpBefore, receiverBack.Vitals.Health.Current);
            Assert.IsTrue(receiverBack.Vitals.IsDown);
            Assert.AreEqual(recoveryBefore, receiverBack.Vitals.RecoveryRemaining, 0.3f,
                "2 回の遷移を挟んでも復帰時計が進まない。");
            Assert.AreEqual(CompanionState.Down, Object.FindFirstObjectByType<CompanionActor>().State);
            Assert.AreEqual(0, Object.FindFirstObjectByType<CompanionActor>().IllegalTransitionCount);

            // ---- 到着後に時計が再開する（止まったままにならない） ----
            //
            // 「ロード中に進まない」だけを見ていると、到着後も止まったままの実装が通ってしまう。
            var receiverForClock = Object.FindFirstObjectByType<CompanionHitReceiver>();
            float recoveryAtArrival = receiverForClock.Vitals.RecoveryRemaining;
            Assert.Greater(recoveryAtArrival, 0f, "前提：まだ復帰待ちが残っている。");

            Assert.IsFalse(Transitions().Clock.IsFrozen, "到着後は Gameplay 時計が戻っている。");
            receiverForClock.TickVitals(0.5f);
            Assert.Less(receiverForClock.Vitals.RecoveryRemaining, recoveryAtArrival,
                "到着後は復帰の時計が進む。");

            // 仲間の活動許可も戻っている（未配線だと犬丸が一切動かない）。
            Assert.IsTrue(CompanionActivityProvider.HasSource, "活動 Context が差さっている。");
            Assert.IsTrue(CompanionActivityProvider.Activity.ClocksRun, "到着後は仲間の時計が動く。");

            // 到着位置は入口へ置換されている（位置は運ばない。§4.4）。
            // まず「どの入口へ到着したことになっているか」を見る。ここが正しくて位置がずれるなら配置の問題、
            // ここから間違っているなら入口の解決の問題（原因を切り分けてから位置を見る）。
            Assert.AreEqual(AreaAFromB.Value, Object.FindFirstObjectByType<AreaContext>().EntryId.Value,
                "B から戻ったので、到着入口は area_p5_a_from_b。");

            var areaRoot = Object.FindFirstObjectByType<AreaRoot>();
            Assert.IsTrue(areaRoot.TryGetEntryPoint(AreaAFromB, out AreaEntryPoint entry));
            var player = Object.FindFirstObjectByType<PlayerStateController>();
            Assert.AreEqual(entry.ArrivalPosition.x, player.transform.position.x, 0.5f,
                "主人公は指定した入口へ置かれる。");
        }

        private static IEnumerator WaitForCompleted(AreaTransitionCoordinator coordinator, int target)
        {
            float waited = 0f;
            while (coordinator.CompletedCount < target && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(target, coordinator.CompletedCount, "遷移が完了する。");
        }

        private static void SetPrivate(object target, string field, object value)
        {
            for (System.Type t = target.GetType(); t != null; t = t.BaseType)
            {
                System.Reflection.FieldInfo f = t.GetField(field,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail("field not found: " + field + " on " + target.GetType().FullName);
        }

        // ---------------------------------------------------------------- P04

        /// <summary>
        /// P5-P04：方向を押しっぱなしで到着しても<b>即座に帰還しない</b>し、入口に立っているだけでは
        /// 遷移も戦闘開始も起きない（§6.1）。
        ///
        /// 到着した瞬間は入口 Trigger の中に立っていることが普通で、そこで押しっぱなしの入力を
        /// そのまま拾うと往復し続ける。一度 Trigger から出るまで要求を止めることを確かめる。
        ///
        /// 入力は移動ベクトルを駆動部へ注入する。IA_Momotaro からの実 Action 経路は
        /// Interact を扱う P10（P5-04）で通す。ここで見たいのは<b>滞在と押しっぱなしの扱い</b>。
        /// </summary>
        [UnityTest]
        public IEnumerator HeldExitInput_DoesNotBounceBackOrStartEncounter()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;

            var areaRoot = Object.FindFirstObjectByType<AreaRoot>();
            Assert.AreEqual(1, areaRoot.ExitGates.Count, "A には B への出入口が 1 つある。");
            AreaExitGate gate = areaRoot.ExitGates[0];
            var driver = Object.FindFirstObjectByType<AreaExitGateDriver>();
            Assert.IsNotNull(driver, "出入口の駆動部がある。");

            // ---- 範囲外では、いくら押しても要求しない ----
            for (int i = 0; i < 20; i++)
            {
                driver.Tick(0.05f, Vector3.right);
            }

            Assert.AreEqual(0, gate.RequestCount, "範囲外の入力では要求しない。");
            Assert.AreEqual(0, coordinator.CompletedCount);

            // ---- 範囲内でも「立っているだけ」では遷移しない（§6.1） ----
            gate.SetPlayerInside(true);
            for (int i = 0; i < 20; i++)
            {
                driver.Tick(0.05f, Vector3.zero);
            }

            Assert.AreEqual(0, gate.RequestCount, "入力が無ければ、範囲内に立っていても遷移しない。");

            // 別方向の入力でも遷移しない。
            for (int i = 0; i < 20; i++)
            {
                driver.Tick(0.05f, Vector3.left);
            }

            Assert.AreEqual(0, gate.RequestCount, "出口と別方向の入力では遷移しない。");

            // 連続でなければ溜まらない（途中で切れたら 0 へ戻る）。
            driver.Tick(0.1f, Vector3.right);
            Assert.Greater(gate.HeldSeconds, 0f, "出口方向へ押せば溜まる。");
            driver.Tick(0.1f, Vector3.zero);
            Assert.AreEqual(0f, gate.HeldSeconds, 1e-4f, "途切れたら溜めは 0 へ戻る。");
            Assert.AreEqual(0, gate.RequestCount, "合計 0.2 秒押していても、連続でなければ要求しない。");

            // ---- 0.15 秒の連続入力で要求する ----
            driver.Tick(0.1f, Vector3.right);
            driver.Tick(0.1f, Vector3.right);
            Assert.AreEqual(1, gate.RequestCount, "0.15 秒の連続入力で 1 回だけ要求する。");

            yield return WaitForCompleted(coordinator, 1);
            Assert.AreEqual(AreaB.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value, "B へ着く。");

            // ---- 到着直後。押しっぱなしのまま入口に立っていても帰らない ----
            var areaRootB = Object.FindFirstObjectByType<AreaRoot>();
            AreaExitGate gateB = areaRootB.ExitGates[0];
            var driverB = Object.FindFirstObjectByType<AreaExitGateDriver>();

            Assert.IsFalse(gateB.IsArmed, "到着直後は武装解除されている（§6.1 末尾）。");

            gateB.SetPlayerInside(true);
            for (int i = 0; i < 40; i++)
            {
                driverB.Tick(0.05f, Vector3.left); // A の方向へ押しっぱなし。
            }

            Assert.AreEqual(0, gateB.RequestCount, "押しっぱなしのままでは帰還しない。");
            Assert.AreEqual(1, coordinator.CompletedCount, "遷移は増えていない。");
            Assert.AreEqual(AreaB.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value, "B に留まる。");

            // 戦闘も始まっていない（Encounter は P5-07 だが、ここで始まらないことは今から固定できる）。
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "探索のまま。");

            // ---- 入力を一度離せば、次は普通に通る ----
            driverB.Tick(0.05f, Vector3.zero);
            Assert.IsTrue(gateB.IsArmed, "入力が切れれば持ち越しの抑止が解ける。");

            driverB.Tick(0.1f, Vector3.left);
            driverB.Tick(0.1f, Vector3.left);
            Assert.AreEqual(1, gateB.RequestCount, "入り直せば要求できる。");

            yield return WaitForCompleted(coordinator, 2);
            Assert.AreEqual(AreaA.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value, "A へ戻る。");
        }

        /// <summary>
        /// P5-E06（実機経路）：<b>主人公が歩いて範囲へ入り、実キーで東を押し続けると B へ移動する。</b>
        ///
        /// 既存の E06 は <c>gate.SetPlayerInside</c> と <c>driver.Tick</c> を直接叩いていた。
        /// 判定の中身はそれで十分だが、<b>範囲の出入りを誰が Gate へ伝えるか</b>と
        /// <b>実際の移動入力が駆動部まで届くか</b>という配線は一度も通っていなかった。
        /// 実機では <c>SetPlayerInside</c> を呼ぶ者がどこにも居らず、試遊で A→B が一度も起きなかった
        /// （壁際で東を押し続けても無反応）。テストが緑のままだったのはこの抜けのせい。
        ///
        /// ここでは Gate にも駆動部にも触れず、<b>置く・押す</b>だけで B へ着くことを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingIntoTheExitAndHoldingEast_TravelsToB_ThroughTriggerAndRealInput()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            RemoveStrayTestDevices();
            _keyboard = InputSystem.AddDevice<Keyboard>("P5ExitKeyboard");

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            AreaTransitionCoordinator coordinator = Transitions().Coordinator;

            var areaRoot = Object.FindFirstObjectByType<AreaRoot>();
            Assert.AreEqual(1, areaRoot.ExitGates.Count, "A には B への出入口が 1 つある。");
            AreaExitGate gate = areaRoot.ExitGates[0];

            Assert.IsTrue(gate.IsWired,
                "出入口に主人公の根が配線されている（配線が無いと Trigger を無視し、永久に反応しない）。");
            Assert.IsFalse(gate.PlayerInside, "まだ範囲外。");

            // 外壁へめり込ませないよう、出入口の少し手前へ置く（範囲内には入る）。
            yield return MovePlayerTo(gate.transform.position + new Vector3(-0.4f, 0f, 0f));

            Assert.IsTrue(gate.PlayerInside,
                "歩いて入れば Trigger が範囲内にする（SetPlayerInside を外から呼ばずに成立する）。");

            // 実キーで東を押し続ける。0.15 秒の連続入力で要求が出る。
            yield return HoldKeyUntil(Key.D, () => coordinator.CompletedCount >= 1, 5f);

            Assert.AreEqual(1, coordinator.CompletedCount, "東へ押し続けると遷移が 1 回成立する。");
            Assert.AreEqual(AreaB.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value, "B へ着く。");
        }

        /// <summary>
        /// <b>成功したことにするが、何も読み込まない</b> Loader。
        /// 到着側の準備がいつまでも完了しない（＝Bind 監視が切れる）状況を実時間で作る。
        /// 旧 Scene は生きたままなので、復旧経路と再開経路の分かれ目だけを見られる。
        /// </summary>
        private sealed class SilentLoader : IAreaSceneLoader
        {
            public IAreaLoadOperation Load(string scenePath) => new DoneOperation();

            private sealed class DoneOperation : IAreaLoadOperation
            {
                public bool IsDone => true;
                public bool HasError => false;
            }
        }

        /// <summary>読込を開始できない Loader（P12）。本番と同じ契約なので経路は 1 本のまま。</summary>
        private sealed class FailingLoader : IAreaSceneLoader
        {
            public IAreaLoadOperation Load(string scenePath) => new FailedOperation();

            private sealed class FailedOperation : IAreaLoadOperation
            {
                public bool IsDone => true;
                public bool HasError => true;
            }
        }

        // ---------------------------------------------------------------- E10（実サービス）

        /// <summary>
        /// P5-E10（実サービス）：監視がタイムアウトしたあと、遅れてロードが完了しても
        /// <b>目的地を活動させず</b>、元 Area を 1 回だけ再ロードして復旧する（§6.3）。
        ///
        /// EditMode の E10 は調停役の回数・フラグを見ているが、実際に復旧ロードを走らせるのは
        /// サービスなので、そこが繋がっていないと「遅れて着いた目的地をそのまま Ready にする」実装が通る
        /// （GPT レビューで指摘された抜け）。
        /// </summary>
        [UnityTest]
        public IEnumerator TimedOutLoad_RecoversToOriginWithoutActivatingDestination()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            AreaTransitionService service = Transitions();
            service.TimeoutSeconds = 0.2f;

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionState session = Sessions().Session;
            var progress = Object.FindFirstObjectByType<PlayerProgressHolder>();
            progress.Grant(new RewardSnapshot(new StableId("reward_test_e10"), 31, default, false), out _);

            AreaTransitionCoordinator coordinator = service.Coordinator;
            Assert.IsNotNull(coordinator);

            // 監視上限より遅れて完了する Loader。実 Scene のロードはしない。
            var slow = new DelayedLoader(0.8f);
            service.Loader = slow;

            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);

            // 復旧が終わるまで待つ。
            float waited = 0f;
            while (service.RecoveredCount < 1 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                slow.Tick(Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.IsTrue(coordinator.TimedOut,
                "監視がタイムアウトしている。診断: phase=" + coordinator.Phase
                + " timeout=" + service.TimeoutSeconds
                + " completed=" + coordinator.CompletedCount
                + " loadStarts=" + coordinator.LoadStartCount
                + " recovery=" + coordinator.RecoveryCount
                + " recovered=" + service.RecoveredCount
                + " terminal=" + service.TerminalFailureCount + "/" + service.TerminalFailureReason
                + " waited=" + waited);
            Assert.AreEqual(1, coordinator.RecoveryCount, "復旧は 1 回だけ。");
            Assert.AreEqual(1, service.RecoveredCount, "元 Area への復旧ロードが完了している。");
            Assert.AreEqual(0, coordinator.CompletedCount, "遅れて着いた目的地を通常の完了にしない。");

            // 元の場所に居て、進行も失っていない。
            Assert.AreEqual(AreaA.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value,
                "元のエリアへ戻っている。");
            Assert.AreEqual(31, session.Progress.Virtue, "徳を失わない。");
            Assert.AreEqual(1, Sessions().CreatedCount, "Session を作り直さない。");

            // 復旧後は普通に操作できる。
            Assert.IsFalse(service.Clock.IsFrozen, "復旧後は Gameplay 時計が戻っている。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);

            service.Loader = new UnitySceneLoader();
        }

        /// <summary>
        /// <b>実際に Scene を読みつつ</b>、完了の報告だけを遅らせる Loader（E10 の復旧経路を通すため）。
        ///
        /// 読込そのものを偽物にすると、復旧先も読み込まれず「復旧に失敗した」という別の経路を
        /// 検査してしまう（最初にそれで落ちた）。遅いロードを再現したいだけなので、
        /// 本物の読込を走らせて <see cref="IAreaLoadOperation.IsDone"/> の報告を遅らせる。
        /// </summary>
        private sealed class DelayedLoader : IAreaSceneLoader
        {
            private readonly float _delay;
            private readonly UnitySceneLoader _inner = new UnitySceneLoader();
            private readonly System.Collections.Generic.List<DelayedOperation> _issued =
                new System.Collections.Generic.List<DelayedOperation>();

            public DelayedLoader(float delay)
            {
                _delay = delay;
            }

            public IAreaLoadOperation Load(string scenePath)
            {
                var op = new DelayedOperation(_inner.Load(scenePath), _delay);
                _issued.Add(op);
                return op;
            }

            /// <summary>発行済みの操作の「報告の遅れ」を進める。</summary>
            public void Tick(float unscaledDelta)
            {
                for (int i = 0; i < _issued.Count; i++)
                {
                    _issued[i].Advance(unscaledDelta);
                }
            }
        }

        /// <summary>本物の読込を走らせつつ、<b>完了の報告だけ</b>を遅らせる操作。</summary>
        private sealed class DelayedOperation : IAreaLoadOperation
        {
            private readonly IAreaLoadOperation _real;
            private readonly float _delay;
            private float _elapsed;

            public DelayedOperation(IAreaLoadOperation real, float delay)
            {
                _real = real;
                _delay = delay;
            }

            public bool IsDone => _real.IsDone && _elapsed >= _delay;
            public bool HasError => _real.HasError;

            public void Advance(float delta) => _elapsed += delta;
        }

        // ---------------------------------------------------------------- P03（行動中の遷移）

        /// <summary>
        /// P5-P03（行動中）：仲間が<b>行動中でも</b>遷移が成立し、到着時に Follow へ整理される（§4.6）。
        ///
        /// 中断しても状態名は変わらないので、生の状態（Attack／Guard／Chase 等）をそのまま運ぶと
        /// 到着側の復元が拒否し、<b>遷移そのものが失敗する</b>（GPT レビューで指摘された）。
        /// 採取のときに §4.6 の復元表へ落としてあることを、実際に攻撃中の遷移で確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator TravelWhileCompanionActing_ArrivesAsFollow()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            // 仲間を行動中の状態にする。Chase は通常の AI 遷移なので Arbiter 経由で作れる。
            var actor = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(actor);
            Assert.IsTrue(actor.RequestState(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget),
                "前提：仲間が行動中になる。");
            Assert.AreEqual(CompanionState.Chase, actor.State);

            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;

            AreaTransitionDecision decision = service.TryTravel(AreaB, AreaBFromA);
            Assert.IsTrue(decision.Accepted, "仲間が行動中でも主人公の遷移は妨げない（§6.1）。理由=" + decision.Rejection);

            yield return WaitForCompleted(coordinator, 1);

            var arrived = Object.FindFirstObjectByType<CompanionActor>();
            Assert.AreEqual(CompanionState.Follow, arrived.State,
                "行動状態は持ち越さず Follow へ整理される（旧攻撃・旧防御・旧探索を再開しない）。");
            Assert.AreEqual(0, arrived.IllegalTransitionCount, "不正遷移を出さない。");

            // 到着後は普通に活動できる。
            Assert.IsTrue(Object.FindFirstObjectByType<AreaContext>().IsAreaReady);
            Assert.IsFalse(service.Clock.IsFrozen);
            Assert.IsTrue(CompanionActivityProvider.Activity.ClocksRun, "仲間の時計が動く。");
        }

        // ---------------------------------------------------------------- E10（目的地を活動させない）

        /// <summary>
        /// P5-E10（補強）：監視がタイムアウトしたあと、目的地の Scene が実際に読み込まれていても
        /// <b>一度も活動させない</b>（§6.3）。
        ///
        /// 既存の E10 は「復旧できたか」を見ている。それだけだと、復旧の前に目的地をいったん
        /// Ready にしてから戻す実装が通ってしまう。ここでは復旧が終わるまでの<b>毎フレーム</b>、
        /// 目的地に居る間は Ready でも操作可能でもないことを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator DelayedDestination_IsNeverActivatedBeforeRecovery()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            AreaTransitionService service = Transitions();
            service.TimeoutSeconds = 0.2f;

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            AreaTransitionCoordinator coordinator = service.Coordinator;
            Assert.AreEqual(0.2f, coordinator.TimeoutSeconds, 1e-4f, "監視上限が調停役まで届いている。");
            var slow = new DelayedLoader(0.8f);
            service.Loader = slow;

            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);

            // B の Scene そのものは読み込まれる（遅れているのは「完了の報告」だけ）。
            int sawDestination = 0;
            float waited = 0f;
            while (service.RecoveredCount < 1 && waited < 20f)
            {
                var current = Object.FindFirstObjectByType<AreaContext>();
                if (current != null && current.AreaId.Equals(AreaB))
                {
                    sawDestination++;
                    Assert.IsFalse(current.IsAreaReady, "遅れて着いた目的地を活動させない。");
                    Assert.AreEqual(0, current.ReadyCount, "目的地への活動許可は 1 度も出ない。");
                    Assert.IsTrue(service.Clock.IsFrozen, "目的地に居る間は Gameplay 時計が止まったまま。");
                    Assert.AreNotEqual(GameMode.Exploration, GameModeProvider.Current.Current,
                        "探索へ戻していない＝入力を許可していない。");

                    // 止まっていることを実物で見る（フラグだけの確認にしない）。
                    var motor = Object.FindFirstObjectByType<CompanionMotor>();
                    if (motor != null)
                    {
                        Vector3 before = motor.transform.position;
                        motor.WarpTo(before + new Vector3(3f, 0f, 0f));
                        Assert.AreEqual(before, motor.transform.position, "凍結中は移動できない。");
                    }
                }

                waited += Time.unscaledDeltaTime;
                slow.Tick(Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.Greater(sawDestination, 0,
                "前提：目的地の Scene が実際に読み込まれ、観測できている（観測できていないと何も検査していない）。");
            Assert.AreEqual(1, service.RecoveredCount, "元 Area へ 1 回だけ復旧する。");
            Assert.AreEqual(1, coordinator.RecoveryCount, "復旧の開始も 1 回だけ。");
            Assert.AreEqual(0, coordinator.CompletedCount, "目的地への到着を完了として数えない。");
            Assert.AreEqual(AreaA.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value,
                "元のエリアへ戻っている。");
            Assert.IsFalse(service.Clock.IsFrozen, "復旧後は時計が戻る。");

            service.Loader = new UnitySceneLoader();
        }

        // ---------------------------------------------------------------- P12（復旧も失敗したとき）

        /// <summary>
        /// P5-P12（補強）：<b>復旧ロードが返ってこない</b>ときに、暗転のまま放置しない（§6.3 の最終行）。
        ///
        /// 復旧の待ちが無界だと、ここで永久に止まる。監視を置いて理由を残し、
        /// 既存 Launcher へ戻る操作を提示する。ただし
        /// <b>生きているロード操作が終端するまでは戻り操作も始めない</b>
        /// （§6.3「古い操作が終端するまで新たなロードを開始しない」）。
        /// </summary>
        [UnityTest]
        public IEnumerator RecoveryLoadThatNeverCompletes_FailsWithReasonAndOffersLauncherReturn()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            AssertSceneRegistered(TrialScene);
            yield return CreateBootstrap();

            AreaTransitionService service = Transitions();
            service.TimeoutSeconds = 0.2f;

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            service.RecoveryTimeoutSeconds = 0.3f;
            service.BindTimeoutSeconds = 0.5f;

            GameSessionState session = Sessions().Session;
            var progress = Object.FindFirstObjectByType<PlayerProgressHolder>();
            progress.Grant(new RewardSnapshot(new StableId("reward_test_recovery"), 41, default, false), out _);

            AreaTransitionCoordinator coordinator = service.Coordinator;
            var loader = new StuckRecoveryLoader(0.8f);
            service.Loader = loader;

            // 終端失敗は Error として出す（表示側が拾う）。想定済みであることを宣言する。
            LogAssert.Expect(LogType.Error, new Regex("Area transition failed terminally"));

            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);

            float waited = 0f;
            while (service.TerminalFailureCount < 1 && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                loader.Tick(Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.AreEqual(1, service.TerminalFailureCount,
                "復旧ロードが返ってこないので、待ち続けずに終端失敗へ落ちる。");
            Assert.IsTrue(service.HasTerminalFailure, "Error 表示の状態に留まる。");
            StringAssert.Contains("復旧ロード", service.TerminalFailureReason,
                "何が起きたかが残る。理由=" + service.TerminalFailureReason);
            Assert.AreEqual(1, coordinator.RecoveryCount, "復旧は 1 回だけ（無限再試行しない）。");
            Assert.IsTrue(service.Clock.IsFrozen, "壊れた状態のまま世界を動かさない。");

            // ---- 生きている復旧ロードが終端するまでは、戻り操作も始めない ----
            //
            // 戻り操作も Scene のロードなので、終端していない操作の上に重ねれば同じ事故になる
            // （§6.3「古い操作が終端するまで新たなロードを開始しない」）。
            Assert.IsFalse(service.CanReturnToLauncher, "終端していない操作の上に重ねない。");
            Assert.IsFalse(service.TryBeginReturnToLauncher(), "押しても始まらない。");
            Assert.IsFalse(service.IsReturningToLauncher, "戻りも始まっていない。");
            Assert.AreEqual(0, service.ReturnedToLauncherCount);

            // ---- 仮の Error 表示に理由が出ている（API だけで「提示した」ことにしない。§6.3） ----
            AreaTransitionFailureView view = BootstrapRoot.Instance.FailureView;
            Assert.IsNotNull(view, "終端失敗の表示が常駐している。");
            Assert.IsTrue(view.IsShowing, "プレイヤーに Error が出ている。");
            StringAssert.Contains("復旧ロード", view.Message, "表示に理由が出ている。");
            Assert.IsFalse(view.CanPressReturn, "生きているロードがある間は戻る操作を押せない。");
            Assert.IsFalse(view.TryPressReturn(), "押しても始まらない。");

            // ---- 遅れて復旧ロードが終端した。ここで初めて戻れる ----
            loader.CompleteStuck();
            Assert.IsTrue(service.CanReturnToLauncher, "終端したら戻れる。");
            Assert.IsTrue(view.CanPressReturn, "表示側の操作も押せるようになる。");

            // ---- まず戻りロードが失敗する。停止状態は維持される ----
            //
            // 「ロードを発行できた」を戻れた証拠にすると、開始に失敗した操作まで成功扱いになる
            // （GPT レビュー R3 の指摘 3）。完了を見届けるまで畳まないことを、実際に失敗させて見る。
            LogAssert.Expect(LogType.Error, new Regex("Return to launcher failed"));
            loader.FailNextLoad = true;
            Assert.IsTrue(view.TryPressReturn(), "戻り操作は始まる。");

            waited = 0f;
            while (service.IsReturningToLauncher && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, service.ReturnToLauncherFailedCount, "戻れなかったことを数える。");
            Assert.AreEqual(0, service.ReturnedToLauncherCount, "戻れていないので増えない。");
            Assert.IsTrue(service.HasTerminalFailure, "失敗したら停止状態を維持する。");
            Assert.IsTrue(service.Clock.IsFrozen, "失敗したら時計も止めたまま。");
            StringAssert.Contains("Launcher", service.TerminalFailureReason,
                "理由が戻りの失敗に差し替わる。理由=" + service.TerminalFailureReason);
            Assert.IsTrue(view.IsShowing, "表示も出たまま。");

            // ---- もう一度押すと、今度は本物の Launcher Scene へ戻る ----
            Assert.IsTrue(view.TryPressReturn(), "再試行はプレイヤーの操作で行う（自動再試行しない）。");

            waited = 0f;
            while (service.ReturnedToLauncherCount < 1 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, service.ReturnedToLauncherCount, "戻り終えて初めて数える。");
            Assert.AreEqual(LauncherScene, SceneManager.GetActiveScene().path,
                "戻り先は既存の Launcher Scene（自動で A へ進む試遊 Scene ではない）。");
            Assert.IsFalse(service.HasTerminalFailure, "戻ったら Error 表示を畳む。");
            Assert.IsFalse(view.IsShowing, "表示も消える。");
            Assert.IsFalse(service.Clock.IsFrozen,
                "戻り先でも止めたままにしない（戻ったのに何も動かない、を作らない）。");

            // Session と進行は失っていない。
            Assert.AreEqual(1, Sessions().CreatedCount, "Session を作り直さない。");
            Assert.AreSame(session, Sessions().Session, "同じ Session が続く。");
            Assert.AreEqual(41, session.Progress.Virtue, "徳を失わない。");

            service.Loader = new UnitySceneLoader();
            service.TimeoutSeconds = AreaTransitionCoordinator.DefaultTimeoutSeconds;
            service.RecoveryTimeoutSeconds = AreaTransitionCoordinator.DefaultTimeoutSeconds;
            service.BindTimeoutSeconds = 10f;
        }

        /// <summary>
        /// 1 回目＝本物のロードだが完了報告を遅らせる、2 回目（＝復旧）＝<b>いつまでも完了しない</b>、
        /// 3 回目以降＝本物。復旧ロードが返ってこない状況をこの 1 本で作る。
        /// </summary>
        private sealed class StuckRecoveryLoader : IAreaSceneLoader
        {
            private readonly UnitySceneLoader _inner = new UnitySceneLoader();
            private readonly float _firstDelay;
            private DelayedOperation _first;
            private ManualOperation _stuck;
            private int _count;

            public StuckRecoveryLoader(float firstDelay)
            {
                _firstDelay = firstDelay;
            }

            /// <summary>次の 1 回のロードを「開始できなかった」ことにする。</summary>
            public bool FailNextLoad { get; set; }

            public IAreaLoadOperation Load(string scenePath)
            {
                if (FailNextLoad)
                {
                    FailNextLoad = false;
                    return new FailedOperation();
                }

                _count++;
                if (_count == 1)
                {
                    _first = new DelayedOperation(_inner.Load(scenePath), _firstDelay);
                    return _first;
                }

                if (_count == 2)
                {
                    _stuck = new ManualOperation();
                    return _stuck;
                }

                return _inner.Load(scenePath);
            }

            private sealed class FailedOperation : IAreaLoadOperation
            {
                public bool IsDone => true;

                public bool HasError => true;
            }

            /// <summary>1 回目の「報告の遅れ」を進める。</summary>
            public void Tick(float unscaledDelta)
            {
                if (_first != null)
                {
                    _first.Advance(unscaledDelta);
                }
            }

            /// <summary>止まっていた復旧ロードを終端させる。</summary>
            public void CompleteStuck()
            {
                Assert.IsNotNull(_stuck, "前提：復旧ロードが始まっている。");
                _stuck.Complete();
            }

            private sealed class ManualOperation : IAreaLoadOperation
            {
                public bool IsDone { get; private set; }

                public bool HasError => false;

                public void Complete() => IsDone = true;
            }
        }

        // ---------------------------------------------------------------- P01（起動順に依存しない）

        /// <summary>
        /// P5-P01（補強）：A の直開きが、<b>常駐の起動が遅れても</b>成立する（§5.1 手順 1）。
        ///
        /// テスト側で先に Bootstrap を完了させてしまうと、順序の問題を一切検査していないことになる
        /// （GPT レビュー R2 の指摘 2）。ここでは常駐が居ない状態でエリアを開き、
        /// 初期化担当が<b>諦めずに待っている</b>こと、あとから立った常駐で成立することを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator DirectOpenAreaA_WaitsForLateBootstrap()
        {
            yield return LateBootstrapDirectOpen(AreaAScene, AreaA, AreaAStart);
        }

        /// <summary>P5-P01（補強）：B の直開きでも同じ（既定入口は <c>area_p5_b_from_a</c>。§3.1）。</summary>
        [UnityTest]
        public IEnumerator DirectOpenAreaB_WaitsForLateBootstrap()
        {
            yield return LateBootstrapDirectOpen(AreaBScene, AreaB, AreaBFromA);
        }

        private IEnumerator LateBootstrapDirectOpen(string scenePath, StableId expectedArea, StableId expectedEntry)
        {
            AssertSceneRegistered(scenePath);

            // 常駐をあえて立てないままエリアを開く。
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            yield return null;

            AreaInitializer init = FindInitializer();
            Assert.IsFalse(init.Initialized, "常駐が居ないうちは初期化しない。");
            Assert.IsFalse(init.WaitFinished, "起動の成否が確定するまで待っている（先に諦めない）。");
            Assert.IsFalse(Object.FindFirstObjectByType<AreaContext>().IsAreaReady,
                "待っている間は活動を許可しない。");

            // あとから常駐が立つ。
            _bootstrap = new GameObject("BootstrapRoot_P5Test");
            _bootstrap.AddComponent<BootstrapRoot>();

            float waited = 0f;
            while (!init.Initialized && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(init.WaitFinished, "待ちが終わっている。");
            Assert.IsTrue(init.Initialized, "あとから起動しても初期化が成立する。理由=" + init.FailureReason);
            Assert.AreEqual(0, init.ArrivalToken, "直開きは遷移を伴わない（到着トークンを持たない）。");

            var context = Object.FindFirstObjectByType<AreaContext>();
            Assert.AreEqual(expectedArea.Value, context.AreaId.Value);
            Assert.AreEqual(expectedEntry.Value, context.EntryId.Value, "直開きの既定入口へ入る。");
            Assert.IsTrue(context.IsAreaReady, "Ready が確定する。");
            Assert.AreEqual(1, context.ReadyCount, "活動の許可は 1 回だけ。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);
            Assert.AreEqual(1, Sessions().CreatedCount, "Session は 1 個だけ作られる。");
        }

        /// <summary>
        /// P5-P01（補強）：統合起動 Scene から Play したとき、<b>常駐の起動が遅れても</b>
        /// A の開始点へ入る（§3.1／§5.2）。戻り先として自分の Scene を名乗ることも併せて見る（§6.3）。
        /// </summary>
        [UnityTest]
        public IEnumerator TrialLauncher_EntersFirstAreaAfterLateBootstrap()
        {
            AssertSceneRegistered(TrialScene);
            AssertSceneRegistered(AreaAScene);

            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            yield return SceneManager.LoadSceneAsync(TrialScene, LoadSceneMode.Single);
            yield return null;

            var launcher = Object.FindFirstObjectByType<Phase5TrialLauncher>();
            Assert.IsNotNull(launcher, "統合起動 Scene に起動役がある。");
            Assert.IsFalse(launcher.WaitFinished, "常駐の起動を待っている。");
            Assert.IsFalse(launcher.Requested, "待っている間は要求しない。");

            _bootstrap = new GameObject("BootstrapRoot_P5Test");
            _bootstrap.AddComponent<BootstrapRoot>();

            AreaTransitionService service = null;
            float waited = 0f;
            while (waited < 20f)
            {
                service = BootstrapServices.Get<AreaTransitionService>();
                if (service != null && service.Coordinator != null && service.Coordinator.CompletedCount >= 1)
                {
                    break;
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsNotNull(service, "常駐が立てば遷移サービスが得られる。");
            Assert.IsNotNull(service.Coordinator, "起動役がカタログを渡している。");
            Assert.AreEqual(1, service.Coordinator.CompletedCount, "統合起動から A へ 1 回だけ遷移する。");

            var context = Object.FindFirstObjectByType<AreaContext>();
            Assert.AreEqual(AreaA.Value, context.AreaId.Value, "死亡再開点＝A の開始点へ着く（§3.1）。");
            Assert.AreEqual(AreaAStart.Value, context.EntryId.Value);
            Assert.IsTrue(context.IsAreaReady, "到着後に活動が許可される。");
            Assert.AreEqual(1, context.ReadyCount,
                "許可は 1 回だけ（到着側の自己許可と所有者の許可が二重にならない）。");
            Assert.AreEqual(1, Sessions().CreatedCount, "Session は 1 個だけ。");
            Assert.IsFalse(service.Clock.IsFrozen, "到着後は時計が動く。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);

            // 遷移で到着した側は到着トークンを持つ（直開きと区別できている）。
            Assert.AreNotEqual(0, FindInitializer().ArrivalToken, "遷移で到着した側はトークンを持つ。");

            // 終端失敗したときの戻り先は<b>既存の Launcher Scene</b>（§6.3 の最終行）。
            // この統合起動 Scene は開くと自動で A へ進むので、戻り先にしてはいけない。
            Assert.AreEqual(LauncherScene, service.LauncherScenePath,
                "戻り先は既存 Launcher に統一されている（自動で A へ進む Scene を戻り先にしない）。");
        }

        // ---------------------------------------------------------------- P12（放棄後の遅延到着）

        /// <summary>
        /// P5-P12（補強）：終端失敗で<b>放棄したあとに遅れて読み終わった Scene</b>が、
        /// 自分で活動を始めない（§6.3。GPT レビュー R3 の指摘 1）。
        ///
        /// Unity の非同期ロードはキャンセルできないので、復旧の監視が切れたあとでも Scene は着く。
        /// 到着側が「到着トークンが無い＝直開き」と判断していると、
        /// <b>失敗して止めたはずのエリアが自分で Ready になり、探索へ戻ってしまう</b>。
        /// ここでは復旧先を<b>実際に遅れて到着させて</b>、活動が始まらないことを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator ArrivalAfterAbandonedTransition_DoesNotSelfActivate()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            AreaTransitionService service = Transitions();
            service.TimeoutSeconds = 0.2f;

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            service.RecoveryTimeoutSeconds = 0.3f;
            service.BindTimeoutSeconds = 0.5f;

            GameSessionState session = Sessions().Session;
            var loader = new DeferredRecoveryLoader(0.8f);
            service.Loader = loader;

            LogAssert.Expect(LogType.Error, new Regex("Area transition failed terminally"));

            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);

            float waited = 0f;
            while (service.TerminalFailureCount < 1 && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                loader.Tick(Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.AreEqual(1, service.TerminalFailureCount, "前提：復旧が返ってこず終端失敗する。");
            Assert.AreEqual(AreaArrivalState.Abandoned, AreaPendingArrival.State,
                "要求は消さずに放棄として残る（消すと直開きと区別できない）。");
            Assert.IsTrue(service.Clock.IsFrozen, "止めたまま。");

            // ---- ここで、キャンセルできなかった復旧ロードが遅れて Scene を読み終える ----
            Assert.IsTrue(loader.ReleaseDeferred(), "前提：保留していた復旧ロードがある。");

            waited = 0f;
            while (SceneManager.GetActiveScene().path != AreaAScene && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(AreaAScene, SceneManager.GetActiveScene().path, "前提：復旧先が実際に到着した。");
            yield return null;
            yield return null;

            // ---- 着いたが、活動は始まらない ----
            AreaInitializer arrived = FindInitializer();
            Assert.IsTrue(arrived.Initialized, "初期化そのものは済む（Bind までは行う）。");
            Assert.AreEqual(0, arrived.ArrivalToken, "生きた到着要求は無い。");
            Assert.IsTrue(arrived.SelfActivationBlocked,
                "トークンが無いことを直開きの証拠にしない（自己許可を断っている）。");
            Assert.AreEqual(1, AreaPendingArrival.BlockedSelfActivationCount, "断ったことを数える。");

            var context = Object.FindFirstObjectByType<AreaContext>();
            Assert.IsNotNull(context);
            Assert.IsFalse(context.IsAreaReady, "放棄後に着いた Scene を活動させない。");
            Assert.AreEqual(0, context.ReadyCount, "活動の許可は 1 度も出ていない。");
            Assert.AreNotEqual(GameMode.Exploration, GameModeProvider.Current.Current,
                "探索へ戻していない（入力を許可していない）。");
            Assert.IsTrue(service.Clock.IsFrozen, "Gameplay 時計も止めたまま。");
            Assert.IsTrue(service.HasTerminalFailure, "Error 表示の状態が続く。");

            // 実物でも動かない。
            var motor = Object.FindFirstObjectByType<CompanionMotor>();
            if (motor != null)
            {
                Vector3 before = motor.transform.position;
                motor.WarpTo(before + new Vector3(3f, 0f, 0f));
                Assert.AreEqual(before, motor.transform.position, "凍結中は移動できない。");
            }

            // ---- プレイヤーは Error 表示から Launcher へ戻れる ----
            AreaTransitionFailureView view = BootstrapRoot.Instance.FailureView;
            Assert.IsTrue(view.IsShowing);
            Assert.IsTrue(view.CanPressReturn, "生きているロードは終端しているので押せる。");
            Assert.IsTrue(view.TryPressReturn());

            waited = 0f;
            while (service.ReturnedToLauncherCount < 1 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, service.ReturnedToLauncherCount, "Launcher へ戻れる。");
            Assert.AreEqual(AreaArrivalState.None, AreaPendingArrival.State, "戻ったら放棄も畳む。");
            Assert.AreEqual(1, Sessions().CreatedCount, "Session を作り直さない。");
            Assert.AreSame(session, Sessions().Session);

            service.Loader = new UnitySceneLoader();
            service.TimeoutSeconds = AreaTransitionCoordinator.DefaultTimeoutSeconds;
            service.RecoveryTimeoutSeconds = AreaTransitionCoordinator.DefaultTimeoutSeconds;
            service.BindTimeoutSeconds = 10f;
        }

        /// <summary>
        /// 1 回目＝本物のロードだが完了報告を遅らせる、2 回目（＝復旧）＝<b>まだ読み始めない</b>、
        /// 3 回目以降＝本物。<see cref="ReleaseDeferred"/> で、保留していた復旧ロードを
        /// <b>本当に走らせて遅延到着を起こす</b>。
        ///
        /// 前のテスト（<c>RecoveryLoadThatNeverCompletes…</c>）は Scene を生成しない操作で止めていたので、
        /// 「遅れて着いた Scene が自分で動き出す」経路を検査できなかった（GPT レビュー R3 の指摘 1）。
        /// </summary>
        private sealed class DeferredRecoveryLoader : IAreaSceneLoader
        {
            private readonly UnitySceneLoader _inner = new UnitySceneLoader();
            private readonly float _firstDelay;
            private DelayedOperation _first;
            private DeferredOperation _deferred;
            private int _count;

            public DeferredRecoveryLoader(float firstDelay)
            {
                _firstDelay = firstDelay;
            }

            public IAreaLoadOperation Load(string scenePath)
            {
                _count++;
                if (_count == 1)
                {
                    _first = new DelayedOperation(_inner.Load(scenePath), _firstDelay);
                    return _first;
                }

                if (_count == 2)
                {
                    _deferred = new DeferredOperation(_inner, scenePath);
                    return _deferred;
                }

                return _inner.Load(scenePath);
            }

            /// <summary>1 回目の「報告の遅れ」を進める。</summary>
            public void Tick(float unscaledDelta)
            {
                if (_first != null)
                {
                    _first.Advance(unscaledDelta);
                }
            }

            /// <summary>保留していた復旧ロードを実際に走らせる（遅延到着を起こす）。</summary>
            public bool ReleaseDeferred()
            {
                if (_deferred == null)
                {
                    return false;
                }

                _deferred.Begin();
                return true;
            }

            private sealed class DeferredOperation : IAreaLoadOperation
            {
                private readonly IAreaSceneLoader _inner;
                private readonly string _scenePath;
                private IAreaLoadOperation _real;

                public DeferredOperation(IAreaSceneLoader inner, string scenePath)
                {
                    _inner = inner;
                    _scenePath = scenePath;
                }

                public bool IsDone => _real != null && _real.IsDone;

                public bool HasError => _real != null && _real.HasError;

                public void Begin()
                {
                    if (_real == null)
                    {
                        _real = _inner.Load(_scenePath);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- E07（凍結中の主人公）

        /// <summary>
        /// P5-E07（補強）：移動中に遷移を始めても、主人公は<b>その場で止まる</b>（§6.2 手順 3）。
        ///
        /// 時計を見て早期 return するだけだと、直前の <c>Rigidbody</c> 速度が残り、
        /// 旧 Scene が生きている間ずっと滑り続ける（GPT レビュー R3 の指摘 2）。
        /// 仲間の Motor は同じ条件で速度をゼロにしているので、主人公だけが滑る形になっていた。
        ///
        /// 目的地のロードは<b>完了しない</b>操作にして、旧 Scene を生かしたまま物理を進める。
        /// </summary>
        [UnityTest]
        public IEnumerator FrozenDuringTransition_PlayerDoesNotSlide()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            var playerRoot = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(playerRoot, "エリアに主人公が居る。");
            Rigidbody body = playerRoot.Body;
            Assert.IsNotNull(body, "主人公に Rigidbody がある。");

            AreaTransitionService service = Transitions();

            // 目的地を読み終えない Loader。旧 Scene が生き続けるので、物理を進めて観測できる。
            service.Loader = new NeverCompletingLoader();

            // 走っている最中に遷移を要求する。
            body.linearVelocity = new Vector3(6f, body.linearVelocity.y, 4f);
            Vector3 before = body.position;

            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);
            Assert.IsTrue(service.Clock.IsFrozen, "前提：受理で Gameplay 時計が止まる。");

            for (int i = 0; i < 12; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Vector3 velocity = body.linearVelocity;
            Assert.AreEqual(0f, Mathf.Abs(velocity.x), 1e-3f, "凍結中に XZ 速度を残さない（x）。");
            Assert.AreEqual(0f, Mathf.Abs(velocity.z), 1e-3f, "凍結中に XZ 速度を残さない（z）。");

            Vector3 after = body.position;
            Assert.AreEqual(before.x, after.x, 0.05f, "凍結中は滑らない（x）。");
            Assert.AreEqual(before.z, after.z, 0.05f, "凍結中は滑らない（z）。");

            service.Loader = new UnitySceneLoader();
        }

        /// <summary>いつまでも読み終えない Loader（旧 Scene を生かしたまま凍結を観測するため）。</summary>
        private sealed class NeverCompletingLoader : IAreaSceneLoader
        {
            public IAreaLoadOperation Load(string scenePath) => new PendingOperation();

            private sealed class PendingOperation : IAreaLoadOperation
            {
                public bool IsDone => false;

                public bool HasError => false;
            }
        }

        // ---------------------------------------------------------------- P10（実 Action 経路）

        /// <summary>
        /// P5-P10：キーボード E とゲームパッド South の<b>実 Action 経路</b>で Interact が各 1 回だけ効き、
        /// 長押しでも複数対象でも二重に実行されず、Step を発動しない（§7.1。受入 P10）。
        ///
        /// ここまでの検査は仲介へ Fake の入力を差していた。それでは
        /// <b>Action の割当（E ／南ボタン）が Step と重なっていないこと</b>も、
        /// 押下エッジが実デバイスから 1 回だけ届くことも確かめられない。
        /// 実デバイスを足して、生成された Scene の配線をそのまま通す。
        /// </summary>
        [UnityTest]
        public IEnumerator KeyboardAndGamepadInteract_ActOnceWithoutStep()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            Assert.IsNotNull(PlayerInputProvider.Current, "主人公入力の提供点が入っている（IA_Momotaro）。");

            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");
            _gamepad = InputSystem.AddDevice<Gamepad>("P5Gamepad");

            var mediator = Object.FindFirstObjectByType<AreaInteractInput>();
            Assert.IsNotNull(mediator, "生成された Scene に P5 の入力仲介がある。");
            Assert.IsNull(Object.FindFirstObjectByType<InvestigationInteractInput>(),
                "P4 の旧仲介は P5 Scene に居ない（§7.1。同じ押下を 2 回消費しない）。");

            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "生成された Scene にレバーがある。");
            Assert.IsNotNull(lever.Door, "レバーに門が配線されている。");

            var player = Object.FindFirstObjectByType<PlayerStateController>();
            Assert.IsNotNull(player);
            yield return MovePlayerTo(lever.InteractionAnchor + new Vector3(0.6f, 0f, 0f));

            // ---- キーボード E：押しっぱなしでも 1 回だけ ----
            // <b>固定フレーム待ちにしない。</b> 実デバイスの押下が Action を通って届くまでの
            // フレーム数は編集器の負荷で変わる。効果が出るまで待ち、そのあと押し続けて
            // 「2 回目が起きないこと」を見る（実際に、固定待ちで取りこぼして落ちた）。
            bool sawStep = false;
            yield return PressKey(Key.E);
            yield return WaitUntilOrTimeout(() => mediator.InteractCount >= 1, 3f);

            for (int i = 0; i < 12; i++)
            {
                sawStep |= player.Current == PlayerState.Step;
                yield return null;
            }

            Assert.AreEqual(1, mediator.InteractCount,
                "E の押しっぱなしでも実行は 1 回。診断: discarded=" + mediator.DiscardedCount
                + " rejection=" + mediator.Controller.LastRejection
                + " registry=" + AreaInteractableRegistry.Count
                + " mode=" + (GameModeProvider.Current != null ? GameModeProvider.Current.Current.ToString() : "null")
                + " ready=" + Object.FindFirstObjectByType<AreaContext>().IsAreaReady
                + " input=" + (PlayerInputProvider.Current != null ? PlayerInputProvider.Current.GetType().Name : "null")
                + " interactPressed=" + ((PlayerInputProvider.Current as IInteractInput)?.InteractPressed)
                + " " + DescribeInputAsset());
            Assert.AreEqual(1, lever.OpenedCount, "門が 1 回だけ開く。");
            Assert.IsTrue(lever.Door.IsOpened, "門が開いている。");
            Assert.IsTrue(Object.FindFirstObjectByType<AreaFlagDoor>().IsOpened);
            Assert.IsFalse(sawStep, "Interact で Step を発動しない（割当が重なっていない）。");

            yield return ReleaseKeys();

            // ---- ゲームパッド South：複数対象でも 1 回だけ ----
            //
            // 実行の回数を数えるだけの対象を 2 つ登録する。実物の仕掛けを 2 つ置くと
            // 「どちらが選ばれたか」と「何回実行されたか」が混ざるので、ここは入力の側だけを見る。
            // 主人公はレバーの +X 側 0.6m に立っている。near は 0.3m、far は 1.2m の位置に置く
            // （+X 方向へ遠ざけると主人公に近づくので、主人公の位置から測って並べる）。
            Vector3 playerSpot = lever.InteractionAnchor + new Vector3(0.6f, 0f, 0f);
            var near = new CountingInteractable("aa_near", lever.AreaId,
                playerSpot + new Vector3(0.3f, 0f, 0f));
            var far = new CountingInteractable("zz_far", lever.AreaId,
                playerSpot + new Vector3(1.2f, 0f, 0f));
            AreaInteractableRegistry.Register(near);
            AreaInteractableRegistry.Register(far);

            int before = mediator.InteractCount;
            yield return PressGamepadSouth();
            yield return WaitUntilOrTimeout(() => near.Calls >= 1, 3f);

            for (int i = 0; i < 12; i++)
            {
                sawStep |= player.Current == PlayerState.Step;
                yield return null;
            }

            Assert.AreEqual(before + 1, mediator.InteractCount, "South の押しっぱなしでも実行は 1 回。");
            Assert.AreEqual(1, near.Calls, "選ばれた対象だけが 1 回実行される。");
            Assert.AreEqual(0, far.Calls, "同じ押下が次点へ流れない。");
            Assert.IsFalse(sawStep, "ゲームパッド South でも Step を発動しない。");

            yield return ReleaseGamepad();

            // ---- 離して押し直せば、また 1 回だけ効く ----
            yield return PressGamepadSouth();
            yield return WaitUntilOrTimeout(() => near.Calls >= 2, 3f);

            Assert.AreEqual(2, near.Calls, "押し直せば 1 回ぶん効く。");
            Assert.AreEqual(0, far.Calls);
            yield return ReleaseGamepad();
        }

        /// <summary>主人公を指定位置へ置く（根と Rigidbody の両方。子を動かすと引き戻される）。</summary>
        private IEnumerator MovePlayerTo(Vector3 position)
        {
            var root = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(root, "主人公の根がある。");
            root.transform.position = position;
            if (root.Body != null)
            {
                root.Body.position = position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        /// <summary>
        /// 条件が成り立つまで待つ（実時間の上限つき）。
        /// 実デバイスの押下が届くまでのフレーム数は編集器の負荷で変わるので、固定フレームで待たない。
        /// </summary>
        private static IEnumerator WaitUntilOrTimeout(System.Func<bool> condition, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerator PressKey(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
        }

        /// <summary>
        /// 効くまで<b>押し直しながら</b>待つ（人が操作するときと同じ）。
        ///
        /// 1 回きりの押下は、Scene を何度も往復したあとだと届かないことがある（P5-10 で踏んだ）。
        /// 入力エッジの扱いそのものは P18 が専用に見るので、それ以外の検査では
        /// 「届くまで押し直す」で揺れを吸収し、本題の失敗と取り違えないようにする。
        /// </summary>
        private IEnumerator PressKeyUntil(Key key, System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return PressKey(key);
                yield return null;
                yield return ReleaseKeys();

                for (int i = 0; i < 10 && !condition(); i++)
                {
                    yield return null;
                }
            }

            yield return ReleaseKeys();
        }

        /// <summary>条件が成るまで<b>押しっぱなし</b>にする（押し直さない）。連続入力を見る検査で使う。</summary>
        private IEnumerator HoldKeyUntil(Key key, System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
            }

            yield return ReleaseKeys();
        }

        private IEnumerator ReleaseKeys()
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        private IEnumerator PressGamepadSouth()
        {
            InputSystem.QueueStateEvent(_gamepad, new GamepadState().WithButton(GamepadButton.South));
            yield return null;
        }

        private IEnumerator ReleaseGamepad()
        {
            InputSystem.QueueStateEvent(_gamepad, new GamepadState());
            yield return null;
            yield return null;
        }

        /// <summary>実行された回数だけを数える対象（入力の側を見るための道具）。</summary>
        private sealed class CountingInteractable : IAreaInteractable
        {
            private readonly string _id;

            public CountingInteractable(string id, StableId areaId, Vector3 anchor)
            {
                _id = id;
                AreaId = areaId;
                InteractionAnchor = anchor;
            }

            public int Calls { get; private set; }

            public StableId InteractableId => new StableId(_id);
            public StableId AreaId { get; }
            public int FloorId => 0;
            public Vector3 InteractionAnchor { get; }
            public float InteractionRadius => 0f;
            public bool IsAvailable => true;
            public string Prompt => _id;

            public AreaInteractionOutcome Interact()
            {
                Calls++;
                return AreaInteractionOutcome.Accepted(_id);
            }
        }

        // ---------------------------------------------------------------- E14（実 Scene）／P05 の探索部分

        /// <summary>
        /// P5-E14（実 Scene）：レバーで開けた門が<b>Scene を往復しても開いたまま</b>で、
        /// Interact の扉から移動できる（§7.3／§4.3／§6.1 の 2 行目）。
        ///
        /// 契約は EditMode で固めてあるが、実 Scene では別の失敗のしかたがある：
        /// 記録が Area ごとに分かれていない、門が Scene の作り直しで閉じ直る、
        /// 扉の要求を誰も取りに来ない。ここはその 3 つを実入力で通す。
        /// P05（全行程）は Encounter（P5-07）が要るので、探索の部分だけをここで固定する。
        /// </summary>
        [UnityTest]
        public IEnumerator ExplorationRoute_LeverOpensGateAndDoorTravelsBack()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            _keyboard = InputSystem.AddDevice<Keyboard>("P5RouteKeyboard");

            GameSessionState session = Sessions().Session;
            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;

            // ---- レバーを引いて門を開ける（実入力） ----
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever);
            AreaFlagDoor door = lever.Door;
            Assert.IsNotNull(door);
            Assert.IsFalse(door.IsOpened, "前提：門は閉じている。");

            yield return MovePlayerTo(lever.InteractionAnchor + new Vector3(0.6f, 0f, 0f));
            yield return PressKey(Key.E);
            yield return WaitUntilOrTimeout(() => lever.OpenedCount >= 1, 3f);
            yield return ReleaseKeys();

            Assert.AreEqual(1, lever.OpenedCount, "レバーで 1 回だけ開通する。");
            Assert.IsTrue(door.IsOpened, "門が開いている。");
            Assert.IsTrue(session.TryGetArea(AreaA, out AreaRuntimeState areaA));
            Assert.IsTrue(areaA.IsOpen(lever.FlagId), "記録が正本として開通を持つ。");

            // ---- A → B ----
            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);
            yield return WaitForCompleted(coordinator, 1);
            Assert.AreEqual(AreaB.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value);

            // ---- B の扉を Interact で押して A へ戻る（§6.1 の 2 行目） ----
            var backDoor = Object.FindFirstObjectByType<AreaTransitionDoor>();
            Assert.IsNotNull(backDoor, "B に A へ戻る扉がある。");
            Assert.AreEqual(AreaA.Value, backDoor.DestinationAreaId.Value);

            yield return MovePlayerTo(backDoor.InteractionAnchor + new Vector3(0.8f, 0f, 0f));
            yield return PressKey(Key.E);
            yield return WaitUntilOrTimeout(() => backDoor.RequestCount >= 1, 3f);
            yield return ReleaseKeys();
            Assert.AreEqual(1, backDoor.RequestCount, "扉は押下 1 回で 1 件だけ要求する。");

            yield return WaitForCompleted(coordinator, 2);
            Assert.AreEqual(AreaA.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value,
                "扉から A へ戻っている。");
            Assert.AreEqual(AreaAFromB.Value, Object.FindFirstObjectByType<AreaContext>().EntryId.Value,
                "扉が指定した入口へ着く。");

            // ---- 作り直された A で、門は開いたまま ----
            var doorBack = Object.FindFirstObjectByType<AreaFlagDoor>();
            Assert.IsNotNull(doorBack);
            Assert.AreNotSame(door, doorBack, "Scene ごとに別の実体（運んでいない）。");
            Assert.IsTrue(doorBack.IsOpened, "記録から開通が復元されている。");

            var leverBack = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.AreEqual(0, leverBack.OpenedCount, "復元は「いま開通した」ではない（通知を再発行しない）。");

            AreaInteractionOutcome again = leverBack.Interact();
            Assert.IsFalse(again.Handled, "開通済みのレバーは断る。");
            Assert.AreEqual("開通済み", again.Message);
            Assert.AreEqual(0, leverBack.OpenedCount, "断りで開通回数は増えない。");
            Assert.AreEqual(1, Sessions().CreatedCount, "Session を作り直さない。");
        }

        // ---------------------------------------------------------------- P11（NavMesh の迂回と門）

        /// <summary>
        /// P5-P11：仲間の長距離追従が<b>実 L 字通路を迂回</b>し、<b>閉じた門は抜けず</b>、
        /// 開通したら経路が更新される（§10.1／§10.2）。
        ///
        /// 経路そのものは焼いた NavMesh と実配置に依存するので、EditMode では確かめられない。
        /// ここでは実 Scene の NavMesh へ問い合わせ、門のくり抜きが効いていること、
        /// 開通が Navigation へ反映されることを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator NavMeshFollow_DetoursAndUpdatesAfterDoorOpens()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            // ---- 経路の供給元が注入されている（§10.1。未注入なら迂回しない） ----
            var follow = Object.FindFirstObjectByType<CompanionFollowController>();
            Assert.IsNotNull(follow, "エリアに犬丸の追従がある。");
            Assert.IsTrue(follow.HasPathProvider,
                "NavMesh の供給元が注入されている（未注入だと長距離の迂回を行わない）。");

            var binder = Object.FindFirstObjectByType<AreaNavigationBinder>();
            Assert.IsNotNull(binder, "経路の配線役がエリアに居る。");
            Assert.IsTrue(binder.IsWired);

            var provider = new NavMeshPathProvider();

            // ---- 迂回：衝立を挟んだ 2 点は、直線ではなく回り込みで繋がる ----
            //
            // 衝立は (-7.5, 0, -1) に幅 0.5・奥行 4（z は -3〜1）。その東西に立つと直線は通らない。
            var west = new Vector3(-10f, 0f, -1f);
            var east = new Vector3(-5f, 0f, -1f);

            PathQueryResult detour = provider.Query(west, east);
            Assert.AreEqual(PathQueryStatus.Complete, detour.Status, "回り込めば繋がっている。");
            Assert.Greater(detour.CornerCount, 2,
                "直線ではなく角を持つ（衝立を回り込んでいる）。角数=" + detour.CornerCount);

            float straight = Vector3.Distance(west, east);
            float along = 0f;
            for (int i = 1; i < detour.CornerCount; i++)
            {
                along += Vector3.Distance(detour.Corners[i - 1], detour.Corners[i]);
            }

            Assert.Greater(along, straight * 1.2f,
                "経路長が直線より明らかに長い（実際に回り込んでいる）。直線=" + straight + " 経路=" + along);

            // ---- 閉じた門は抜けない ----
            //
            // 東の通路は仕切り（x=6）と外壁（x=12）の間。門はそこを z=0 で塞いでいる。
            var southOfGate = new Vector3(9f, 0f, -6f);
            var northOfGate = new Vector3(9f, 0f, 6f);

            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever);
            Assert.IsFalse(lever.Door.IsOpened, "前提：門は閉じている。");
            Assert.IsNotNull(lever.Door.NavObstacle, "門は NavMesh のくり抜きを持つ。");
            Assert.IsTrue(lever.Door.NavObstacle.carving, "くり抜きが有効（避けるだけでは経路は素通りする）。");

            // くり抜きが NavMesh へ反映されるまで数フレーム待つ。
            yield return WaitForCarving(provider, southOfGate, northOfGate, blocked: true);

            PathQueryResult closed = provider.Query(southOfGate, northOfGate);
            Assert.AreNotEqual(PathQueryStatus.Complete, closed.Status,
                "閉じた門を抜ける経路は出ない（Partial を成功にしないのはこの形のため）。状態=" + closed.Status);

            // ---- 開通したら、経路が更新される ----
            Assert.AreEqual(0, binder.PathUpdateCount, "前提：まだ更新していない。");
            AreaInteractionOutcome outcome = lever.Interact();
            Assert.IsTrue(outcome.Handled, "レバーで開通する。");
            Assert.AreEqual(1, binder.PathUpdateCount, "開通が経路へ伝わる（§10.1 の再探索条件）。");
            Assert.IsFalse(lever.Door.NavObstacle.enabled, "くり抜きが外れる（§10.2 末尾）。");

            yield return WaitForCarving(provider, southOfGate, northOfGate, blocked: false);

            PathQueryResult opened = provider.Query(southOfGate, northOfGate);
            Assert.AreEqual(PathQueryStatus.Complete, opened.Status,
                "開通したら通れる。状態=" + opened.Status);
        }

        /// <summary>くり抜きの反映を待つ（NavMesh の更新は即時ではない）。</summary>
        private static IEnumerator WaitForCarving(
            NavMeshPathProvider provider, Vector3 from, Vector3 to, bool blocked)
        {
            float waited = 0f;
            while (waited < 3f)
            {
                bool complete = provider.Query(from, to).Status == PathQueryStatus.Complete;
                if (complete != blocked)
                {
                    yield break;
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // ---------------------------------------------------------------- P06（実 Collider）

        /// <summary>
        /// P5-P06：実 Collider で、通常移動・Step・押し出しが壁と水の境界を越えない。
        /// 通れる床は通る（§3.3。止めすぎも失敗）。
        ///
        /// 「止まること」だけを見ると、全部止まる実装（動けない主人公）が通ってしまう。
        /// <b>通れる床で実際に進むこと</b>を先に確かめてから、越えないことを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator MovementStepAndHitback_RespectWallsAndWaterBoundary()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            var root = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(root);
            Rigidbody body = root.Body;
            Assert.IsNotNull(body);

            var motorForInput = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.IsNotNull(motorForInput, "主人公に Motor がある。");

            // 入力を差し替えて<b>通常移動の経路そのもの</b>を動かす。
            //
            // Rigidbody へ直接速度を書いても、Motor が毎 FixedUpdate に上書きするので動かない。
            // また Motor は入力の供給点を<b>最初の 1 回だけ</b>覚えるので、
            // Scene が立ち上がったあとに提供点を差し替えても効かない（どちらも実際に踏んだ）。
            // 見たいのは「物理が壁と境界を止めるか」なので、Motor が持つ入力そのものを差し替える。
            // 実デバイスから Action を通す経路は P10 が見ている。
            IPlayerInput previousInput = PlayerInputProvider.Current;
            var fakeInput = new StickInput();
            PlayerInputProvider.Current = fakeInput;
            SetPrivate(motorForInput, "_input", fakeInput);

            try
            {
                // ---- 通れる床は通る ----
                yield return MovePlayerTo(new Vector3(0f, 0f, -6f));
                float startX = body.position.x;
                yield return DriveInput(fakeInput, new Vector2(1f, 0f), 30);
                Assert.Greater(body.position.x, startX + 0.5f,
                    "開けた床では実際に進む（止まりすぎも失敗）。x=" + body.position.x);

                // ---- 外壁は越えない（東の外壁は x = 12） ----
                yield return MovePlayerTo(new Vector3(10.5f, 0f, -6f));
                yield return DriveInput(fakeInput, new Vector2(1f, 0f), 40);
                Assert.Less(body.position.x, 12f, "外壁を越えない。x=" + body.position.x);

                // ---- 水の境界は越えない（水場は (-8, 5.5) 付近） ----
                Vector3 water = new Vector3(-8f, 0f, 5.5f);
                yield return MovePlayerTo(water + new Vector3(0f, 0f, -4f));
                yield return DriveInput(fakeInput, new Vector2(0f, 1f), 40);
                Assert.Less(body.position.z, water.z - 1.0f,
                    "水の境界を越えない（見た目の板ではなく透明な境界で止まる）。z=" + body.position.z);
            }
            finally
            {
                PlayerInputProvider.Current = previousInput;
                SetPrivate(motorForInput, "_input", previousInput);
            }

            // ---- Step も越えない ----
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            Assert.IsNotNull(motor, "主人公に Motor がある。");
            yield return MovePlayerTo(new Vector3(10.5f, 0f, -6f));
            motor.MovementSuppressed = true;
            motor.StepVelocity = new Vector3(12f, 0f, 0f);
            for (int i = 0; i < 30; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            motor.MovementSuppressed = false;
            motor.StepVelocity = Vector3.zero;
            Assert.Less(body.position.x, 12f, "Step でも外壁を越えない。x=" + body.position.x);

            // ---- 押し出し（ヒットバック）も越えない ----
            yield return MovePlayerTo(new Vector3(10.5f, 0f, -6f));
            motor.PushReaction(Vector3.right, 6f, 0.4f);
            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            motor.ClearReaction();
            Assert.Less(body.position.x, 12f, "押し出しでも外壁を越えない。x=" + body.position.x);
        }

        // ---------------------------------------------------------------- P05・P08（Encounter）

        private static readonly Vector3 EncounterTriggerPoint = new Vector3(2f, 0f, 0f);

        /// <summary>敵が受けた命中を記録する（誰の攻撃で削れたかを見るため）。</summary>
        private sealed class EnemyHitLog : IHitResultListener
        {
            private readonly ICombatActor _player;

            public EnemyHitLog(ICombatActor player)
            {
                _player = player;
            }

            public int DamageFromPlayer { get; private set; }

            public int DamageTotal { get; private set; }

            /// <summary>種別を問わない結果数（当たってすらいないのかを見分ける）。</summary>
            public int AnyKindCount { get; private set; }

            public void OnHitResult(in HitResult result)
            {
                AnyKindCount++;
                if (result.Kind != HitResultKind.Damage)
                {
                    return;
                }

                DamageTotal++;
                if (_player != null && ReferenceEquals(result.Attacker, _player))
                {
                    DamageFromPlayer++;
                }
            }
        }

        /// <summary>
        /// P5-P05：実入力で<b>調査 → 開通 → 移動 → 実敵撃破 → 探索復帰</b>まで一周する（§15.3）。
        /// 徳は 10＋12＝22（§8.5）。再訪では敵が湧かない（§8.4 末尾）。
        ///
        /// <b>撃破は実 Hitbox で通す</b>（§15.3 の但し書き）。結果を直接セットして命中経路を飛ばさない。
        /// 主人公の攻撃が実際に当たっていることを、敵側の命中結果の<b>攻撃者</b>で確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator FullRoute_InvestigateOpenTravelFightAndReturn()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            // 仮想デバイスは Scene が立ってから足す（P10 と同じ順。先に足すと Action の再解決に間に合わない）。
            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");

            IGameModeService modes = GameModeProvider.Current;
            GameSessionState session = Sessions().Session;
            Assert.AreEqual(0, session.Progress.Virtue, "前提：まだ何も得ていない。");

            // ================================ 1. 調査（実キー E）================================
            var coordinator = Object.FindFirstObjectByType<InvestigationCoordinator>();
            Assert.IsNotNull(coordinator, "A に調査の調停役がある。");

            var interaction = Object.FindFirstObjectByType<AreaInteractionController>();
            Assert.IsNotNull(interaction, "Interact の単一窓口がある。");
            var mediatorA = Object.FindFirstObjectByType<AreaInteractInput>();
            Assert.IsNotNull(mediatorA, "Interact の入力仲介がある。");

            var point = Object.FindFirstObjectByType<InvestigationInteractable>();
            Assert.IsNotNull(point, "調査地点が Interact 候補として出ている。");

            yield return MovePlayerTo(point.InteractionAnchor + new Vector3(0f, 0f, -0.8f));
            yield return PressKey(Key.E);
            yield return WaitUntilOrTimeout(() => coordinator.LastRequestId > 0, 5f);
            yield return ReleaseKeys();
            Assert.Greater(coordinator.LastRequestId, 0,
                "実キー E で調査が受理される。調停の理由=" + coordinator.LastRejectReason
                + " 窓口の拒否=" + interaction.LastRejection
                + " 実行=" + mediatorA.InteractCount + " 捨てた=" + mediatorA.DiscardedCount
                + " 候補数=" + AreaInteractableRegistry.Count
                + " 受付半径=" + point.InteractionRadius
                + " 距離=" + Vector3.Distance(
                    Object.FindFirstObjectByType<PlayerRoot>().transform.position, point.InteractionAnchor)
                + " 利用可=" + point.IsAvailable);

            Assert.IsTrue(session.TryGetArea(AreaA, out AreaRuntimeState areaA));
            yield return WaitUntilOrTimeout(() => areaA.InvestigatedCount >= 1, 15f);
            Assert.AreEqual(1, areaA.InvestigatedCount, "調査が成功して記録に残る。");

            // ================================ 2. 門（実キー E）================================
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever);
            Assert.IsFalse(lever.Door.IsOpened, "前提：門は閉じている。");

            yield return MovePlayerTo(lever.InteractionAnchor + new Vector3(0.6f, 0f, 0f));
            yield return PressKey(Key.E);
            yield return WaitUntilOrTimeout(() => lever.OpenedCount >= 1, 5f);
            yield return ReleaseKeys();
            Assert.AreEqual(1, lever.OpenedCount, "実キー E で門が開通する。");
            Assert.IsTrue(areaA.IsOpen(lever.FlagId), "開通が記録に残る。");

            // ================================ 3. 移動（A → B）================================
            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator transitions = service.Coordinator;
            Assert.IsNotNull(transitions);
            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);
            yield return WaitForArrival(transitions, 1);

            // ================================ 4. 戦闘 ================================
            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runner, "B に Encounter の調停がある。");
            Assert.IsTrue(runner.IsWired, "Encounter が配線されている。");
            Assert.AreEqual(AreaEncounterState.Dormant, runner.State, "まだ始まっていない。");
            Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                "到着時点で敵は 0（§13.2 の「初期敵 0」）。");

            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            Assert.IsNotNull(arena);
            Assert.IsFalse(arena.IsEnabled, "探索中は境界が無効。");

            // Trigger の中へ入る。開始は Trigger が要求し、条件は調停が見る。
            yield return MovePlayerTo(EncounterTriggerPoint);
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 5f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                "Trigger 進入で戦闘が始まる。拒否=" + runner.LastRejection + " 補足=" + runner.LastFailureDetail);
            Assert.AreEqual(GameMode.Combat, modes.Current, "GameMode は Combat。");
            Assert.IsTrue(arena.IsEnabled, "アリーナ境界が有効になる。");
            Assert.AreEqual(arena.BlockerCount, arena.ActiveBlockerCount,
                "封鎖 Collider が実際に有効（意図だけでなく実体を見る）。");

            EnemyActor[] enemies = Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);
            Assert.AreEqual(2, enemies.Length, "骸骨剣士 1 ＋ 骸骨弓兵 1（§8.1）。");

            var playerActor = Object.FindFirstObjectByType<PlayerStateController>();
            Assert.IsNotNull(playerActor);
            var log = new EnemyHitLog(playerActor);
            for (int i = 0; i < enemies.Length; i++)
            {
                enemies[i].Results.AddListener(log);
            }

            try
            {
                for (int i = 0; i < enemies.Length; i++)
                {
                    yield return KillWithRealHitbox(enemies[i]);
                }
            }
            finally
            {
                for (int i = 0; i < enemies.Length; i++)
                {
                    if (enemies[i] != null)
                    {
                        enemies[i].Results.RemoveListener(log);
                    }
                }
            }

            Assert.Greater(log.DamageFromPlayer, 0,
                "主人公の攻撃が実 Hitbox で当たっている（結果を直接セットしていない）。");

            // ================================ 5. 探索復帰 ================================
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Cleared, 5f);
            Assert.AreEqual(AreaEncounterState.Cleared, runner.State, "勝利で終わる。");
            Assert.AreEqual(GameMode.Exploration, modes.Current, "探索へ戻る（§8.4 手順 6）。");
            Assert.AreEqual("戦闘終了", runner.ResultMessage, "短文だけを出す（§8.4 手順 8）。");

            // <b>値を公開しただけでは表示したことにならない</b>（遷移の Error 表示と同じ）。
            var resultView = Object.FindFirstObjectByType<AreaEncounterResultView>();
            Assert.IsNotNull(resultView, "結果の短文を出す表示が Scene にある。");
            Assert.IsTrue(resultView.IsWired);
            Assert.IsTrue(resultView.IsShowing, "実際に出ている（§8.4 手順 8）。");
            Assert.AreEqual("戦闘終了", resultView.Message);
            Assert.AreEqual(1, resultView.ShowCount);

            // 結果パネルや Enter 待ちで止めない：時間で消える。
            resultView.Tick(10f);
            Assert.IsFalse(resultView.IsShowing, "短文は自動で消える（入力待ちで止めない）。");
            Assert.IsFalse(arena.IsEnabled, "一時境界を解放する（§8.4 手順 5）。");
            Assert.AreEqual(0, arena.ActiveBlockerCount,
                "封鎖 Collider が実際に無効へ戻る（探索中に通れない壁を残さない）。");
            Assert.AreEqual(22, session.Progress.Virtue, "10 ＋ 12 ＝ 22（§8.5）。");

            Assert.IsTrue(session.TryGetArea(AreaB, out AreaRuntimeState areaB));
            Assert.IsTrue(areaB.IsEncounterCleared(new StableId("encounter_p5_b_road"), session.RespawnCycle),
                "この周期のクリアを記録する。");

            // ================================ 6. 再訪では湧かない ================================
            //
            // 直前の一振りの硬直が残っていると §6.1 の「行動中は遷移しない」で断られる。
            // 人が操作するときと同じく、手が空くのを待ってから移動する。
            yield return WaitUntilOrTimeout(() => playerActor.IsFreeToTravel, 3f);

            AreaTransitionDecision back = service.TryTravel(AreaA, AreaAFromB);
            Assert.IsTrue(back.Accepted, "戦闘が終わっていれば遷移できる。理由=" + back.Rejection);
            yield return WaitForArrival(transitions, 2);
            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);
            yield return WaitForArrival(transitions, 3);

            var runnerAgain = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runnerAgain);
            Assert.AreEqual(AreaEncounterState.Cleared, runnerAgain.State,
                "記録からクリア済みを復元する（§4.3）。");

            yield return MovePlayerTo(EncounterTriggerPoint);
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            Assert.AreEqual(AreaEncounterState.Cleared, runnerAgain.State, "再訪では始まらない（§8.4 末尾）。");
            Assert.AreEqual(EncounterStartRejection.AlreadyCleared, runnerAgain.LastRejection);
            Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                "再訪の敵は 0。");
            Assert.AreEqual(22, session.Progress.Virtue, "往復しても徳は増えない。");

            yield return ReleaseKeys();
        }

        /// <summary>
        /// P5-P08：生成失敗と勝利のどちらでも<b>残留物を残さない</b>。探索へ戻って犬丸が再活動する（§15.3）。
        ///
        /// 生成失敗はテスト専用の差し替え（<c>AreaEncounterRunner.Bind</c> の公開注入）で作る。
        /// 結果を直接セットするのではなく、<b>本番と同じ開始手順</b>を通して失敗させる。
        /// </summary>
        [UnityTest]
        public IEnumerator SpawnAndCombatCleanup_LeaveNoProjectileOrBoundary()
        {
            AssertSceneRegistered(AreaBScene);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaBScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            IGameModeService modes = GameModeProvider.Current;
            GameSessionState session = Sessions().Session;

            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            var spawner = Object.FindFirstObjectByType<AreaEncounterSpawner>();
            Assert.IsNotNull(runner);
            Assert.IsNotNull(arena);
            Assert.IsNotNull(spawner);

            var trigger = Object.FindFirstObjectByType<AreaEncounterTrigger>();
            Assert.IsNotNull(trigger);
            Assert.IsTrue(trigger.IsWired);

            // ================================ 1. 生成失敗 ================================
            //
            // 生成だけを差し替え、<b>開始手順そのものは本番の経路</b>（Trigger 進入）を通す。
            var failing = new FailingSpawner();
            runner.Bind(null, null, null, failing, null);

            yield return MovePlayerTo(EncounterTriggerPoint);
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Failed, 5f);

            Assert.AreEqual(1, trigger.RequestCount, "Trigger が 1 回だけ要求する。");
            Assert.AreEqual(EncounterStartRejection.SpawnFailed, runner.LastRejection,
                "生成の差し替えだけで落ちる（他の手順は通る）。補足=" + runner.LastFailureDetail);
            Assert.AreEqual(AreaEncounterState.Failed, runner.State);
            Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                "生成途中の敵を残さない。");
            Assert.IsFalse(arena.IsEnabled, "境界を元へ戻す。");
            Assert.AreEqual(0, arena.ActiveBlockerCount, "封鎖 Collider も実際に無効へ戻る。");
            Assert.AreEqual(GameMode.Exploration, modes.Current, "モードも元へ戻す。");
            Assert.AreEqual(0, session.Progress.Virtue, "報酬は付かない。");
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount, "飛翔体も残らない。");
            Assert.IsNull(runner.ActivitySession, "失敗のあとは「活動中 Encounter なし」。");
            Assert.IsTrue(CompanionActivityProvider.Activity.CanInvestigate,
                "犬丸は探索へ戻る（戦闘中のまま固まらない）。");

            // ================================ 2. 実生成 → 勝利 ================================
            runner.Bind(null, null, null, spawner, null);

            // 失敗後は「Trigger 退出 → 再進入」で再試行できる（§8.2）。居座ったままでは再要求しない。
            yield return MovePlayerTo(new Vector3(-9f, 0f, 6f));
            yield return WaitUntilOrTimeout(() => !trigger.PlayerInside, 3f);
            Assert.IsFalse(trigger.PlayerInside, "Trigger から出ている。");
            Assert.AreEqual(AreaEncounterState.Failed, runner.State, "出ただけでは再開しない。");

            yield return MovePlayerTo(EncounterTriggerPoint);
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 5f);
            Assert.AreEqual(2, trigger.RequestCount, "再進入で 1 回だけ再要求する。");
            Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                "再試行で始まる。拒否=" + runner.LastRejection + " 補足=" + runner.LastFailureDetail);

            EnemyActor[] enemies = Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);
            Assert.AreEqual(2, enemies.Length);
            Assert.IsTrue(arena.IsEnabled);
            Assert.AreEqual(arena.BlockerCount, arena.ActiveBlockerCount, "封鎖 Collider が実際に有効。");
            Assert.IsFalse(CompanionActivityProvider.Activity.CanInvestigate, "戦闘中は探索を受け付けない。");

            for (int i = 0; i < enemies.Length; i++)
            {
                yield return KillWithRealHitbox(enemies[i]);
            }

            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Cleared, 5f);
            Assert.AreEqual(AreaEncounterState.Cleared, runner.State);

            // ================================ 3. 残留物なし ================================
            //
            // 破棄は次のフレームに回ることがあるので、数フレーム待ってから数える
            // （「Destroy 予定になった」で成功にしない）。
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                "敵の死体を残さない（次 Scene へ持ち越さない。§8.4 末尾）。");
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount, "残留 Projectile なし（§8.4 手順 5）。");
            Assert.IsFalse(arena.IsEnabled, "一時境界を解放する。");
            Assert.AreEqual(0, arena.ActiveBlockerCount,
                "封鎖 Collider が実際に無効へ戻る（探索中に通れない壁を残さない）。");
            Assert.AreEqual(0, spawner.SpawnedCount, "生成物の管理も空になる。");

            int hostiles = CountHostilePerceptionTargets();
            Assert.AreEqual(0, hostiles, "索敵レジストリに敵が残らない。残り=" + hostiles);

            // ================================ 4. 犬丸が再活動する ================================
            Assert.AreEqual(GameMode.Exploration, modes.Current);
            Assert.IsNull(runner.ActivitySession, "解放後は「活動中 Encounter なし」（§8.4 末尾）。");
            Assert.IsTrue(CompanionActivityProvider.Activity.CanInvestigate,
                "犬丸が探索を受け付ける状態へ戻る（Victory が残って永久停止しない）。");

            var companionMotor = Object.FindFirstObjectByType<CompanionMotor>();
            Assert.IsNotNull(companionMotor);
            Rigidbody companionBody = companionMotor.GetComponent<Rigidbody>();
            var companionActor = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(companionActor);

            // 実戦なので、犬丸が倒れていることはある（それ自体は正常）。
            // ここで見たいのは<b>戦闘が終われば行動できる状態へ戻る</b>ことなので、復帰を待ってから追従を見る。
            yield return WaitUntilOrTimeout(() => CanFollowAgain(companionActor.State), 20f);
            Assert.IsTrue(CanFollowAgain(companionActor.State),
                "犬丸が行動できる状態へ戻る（戦闘中のまま固まらない）。状態=" + companionActor.State);

            // 実際に付いてくる（「止まっていない」を位置で見る）。
            Vector3 before = companionBody.position;
            yield return MovePlayerTo(new Vector3(-9f, 0f, -6f));
            yield return WaitUntilOrTimeout(
                () => Vector3.Distance(before, companionBody.position) > 1f, 10f);
            Assert.Greater(Vector3.Distance(before, companionBody.position), 1f,
                "犬丸が追従を再開する。移動量=" + Vector3.Distance(before, companionBody.position)
                + " 状態=" + companionActor.State
                + " 活動=" + CompanionActivityProvider.Activity
                + " 時計停止=" + GameplayClockProvider.IsFrozen
                + " 移動所有=" + companionMotor.GetComponent<CompanionMovementArbiter>()?.Owner
                + " 追従判断=" + companionMotor.GetComponent<CompanionFollowController>()?.Decision
                + " 戦闘に譲る=" + companionMotor.GetComponent<CompanionFollowController>()?.IsYieldingToCombat
                + " 調査に譲る=" + companionMotor.GetComponent<CompanionFollowController>()?.IsYieldingToInvestigation
                + " 距離=" + Vector3.Distance(
                    companionBody.position, Object.FindFirstObjectByType<PlayerRoot>().transform.position)
                + " 犬丸位置=" + companionBody.position
                + " 主人公位置=" + Object.FindFirstObjectByType<PlayerRoot>().transform.position
                + " 危険で止めたWarp=" + companionMotor.GetComponent<CompanionFollowController>()?.UnsafeWarpBlockedCount
                + " Warp回数=" + companionMotor.WarpCount);

            yield return ReleaseKeys();
        }

        /// <summary>生成に必ず失敗する差し替え（テスト専用。開始手順そのものは本番と同じ経路を通る）。</summary>
        private sealed class FailingSpawner : IEncounterSpawner
        {
            public int SpawnedCount => 0;
            public bool SpawnedActive => false;
            public IReadOnlyList<IEnemyDefeatSource> Spawned => System.Array.Empty<IEnemyDefeatSource>();
            public int ReleaseCount { get; private set; }

            public bool TrySpawnAll(in EncounterPlan plan, out string error)
            {
                error = "テスト：敵 Prefab を解決できません。";
                return false;
            }

#pragma warning disable 67 // 生成に失敗するので発火しない。
            public event System.Action SpawnedActivated;
#pragma warning restore 67

            public void ActivateSpawned()
            {
                Assert.Fail("生成に失敗したのに活動を許可している。");
            }

            public void ReleaseAll()
            {
                ReleaseCount++;
            }
        }

        /// <summary>索敵レジストリに残っている敵対対象の数（主人公・犬丸から見た敵）。</summary>
        private static int CountHostilePerceptionTargets()
        {
            var buffer = new List<IThreatTarget>();
            PerceptionTargetRegistry.CollectHostileThreatTargets(
                Vector3.zero, CombatFaction.Ally, 1000f, buffer);
            return buffer.Count;
        }

        /// <summary>
        /// 実 Hitbox で 1 体倒す。主人公を敵の隣へ置き、向きを作ってから実キー J を押す。
        /// 犬丸も実機どおり戦うので、<b>撃破そのもの</b>は両者のどちらでも成立してよい。
        /// 主人公の命中が起きていることは呼び出し側が命中結果の攻撃者で確かめる。
        /// </summary>
        private IEnumerator KillWithRealHitbox(EnemyActor enemy)
        {
            if (enemy == null || enemy.IsDefeated)
            {
                yield break;
            }

            var playerRoot = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(playerRoot);
            var facing = playerRoot.GetComponentInChildren<PlayerFacing>();
            Assert.IsNotNull(facing, "主人公の向き（PlayerFacing）がある。");
            var player = playerRoot.GetComponentInChildren<PlayerStateController>();
            Assert.IsNotNull(player);
            bool sawAttack = false;
            int activeFrames = 0;
            var probe = new EnemyHitLog(player);
            enemy.Results.AddListener(probe);

            float deadline = Time.realtimeSinceStartup + 25f;
            float nextPress = 0f;
            bool pressed = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (enemy == null || enemy.IsDefeated)
                {
                    yield break;
                }

                // 敵の手前 1.0m へ張り付き、敵の方（+Z）を向く。攻撃の判定は Active の間だけ出る。
                Vector3 stick = enemy.transform.position + new Vector3(0f, 0f, -1.0f);
                if (playerRoot.Body != null)
                {
                    playerRoot.Body.position = stick;
                    playerRoot.Body.linearVelocity = Vector3.zero;
                }

                playerRoot.transform.position = stick;
                facing.ConfirmFromInput(Vector2.up);

                // 実キー J を押して離す（3 連撃の入力を回す）。
                if (Time.realtimeSinceStartup >= nextPress)
                {
                    pressed = !pressed;
                    InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.J) : new KeyboardState());
                    nextPress = Time.realtimeSinceStartup + 0.12f;
                }

                sawAttack |= player.Current == PlayerState.Attack;
                if (player.IsSwingHitboxActive)
                {
                    activeFrames++;
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            if (enemy != null)
            {
                enemy.Results.RemoveListener(probe);
            }

            Assert.IsTrue(enemy == null || enemy.IsDefeated,
                "実 Hitbox で敵を倒せていない（HP=" + (enemy != null ? enemy.CurrentHp : 0)
                + " 体幹=" + (enemy != null ? enemy.CurrentPoise : 0f)
                + " 命中結果=" + probe.DamageTotal + "/主人公 " + probe.DamageFromPlayer
                + " 全種=" + probe.AnyKindCount
                + " 主人公の状態=" + player.Current + " 攻撃に入った=" + sawAttack
                + " 敵レイヤー=" + (enemy != null ? LayerMask.LayerToName(enemy.gameObject.layer) : "-")
                + " 判定中心=" + player.SwingCenter + " 前=" + player.SwingForward
                + " 主人公位置=" + player.transform.position
                + " 敵位置=" + (enemy != null ? enemy.transform.position.ToString() : "-")
                + " 判定中フレーム=" + activeFrames + " 重なり=" + OverlapCountAt(player)
                + " 主人公の root=" + player.transform.root.name
                + " 敵の root=" + (enemy != null ? enemy.transform.root.name : "-")
                + " 距離=" + (enemy != null ? Vector3.Distance(player.transform.position, enemy.transform.position) : 0f)
                + " mode=" + (GameModeProvider.Current != null ? GameModeProvider.Current.Current.ToString() : "null")
                + " input=" + (PlayerInputProvider.Current != null ? PlayerInputProvider.Current.GetType().Name : "null")
                + " active=" + (PlayerInputProvider.Current != null && PlayerInputProvider.Current.Active) + "）。");
        }

        /// <summary>追従を再開できる状態か（倒れている・退場中は除く）。</summary>
        private static bool CanFollowAgain(CompanionState state)
        {
            return state != CompanionState.Down
                && state != CompanionState.Recovering
                && state != CompanionState.Away
                && state != CompanionState.Stagger;
        }

        /// <summary>いまの判定区間に何が重なっているか（診断用）。</summary>
        private static int OverlapCountAt(PlayerStateController player)
        {
            Physics.SyncTransforms();
            Collider[] hits = Physics.OverlapBox(
                player.SwingCenter, player.SwingHalfExtents, Quaternion.identity, ~0,
                QueryTriggerInteraction.Collide);
            return hits != null ? hits.Length : 0;
        }

        /// <summary>遷移の完了を待つ（完了回数で数える）。</summary>
        private static IEnumerator WaitForArrival(AreaTransitionCoordinator coordinator, int expected)
        {
            float waited = 0f;
            while (coordinator.CompletedCount < expected && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(expected, coordinator.CompletedCount, "遷移が完了する。");
        }

        // ------------------------------------------- R4：実 Update 順・Feedback・結果表示・Pause／Loading

        /// <summary>
        /// 同じフレームの死亡が勝利に優先することを、<b>実 Update 順を挟んで</b>確かめる
        /// （§8.3 末尾。GPT レビュー R4 の指摘 2。P5-E18 の補助）。
        ///
        /// E18 は両方の通知を手で渡してから確定を呼ぶので、<b>確定がどの実行段で走るか</b>を見ていない。
        /// 実機では命中の解決が各 Actor の <c>Update</c> に散っており、その順序は Unity が決める。
        /// 確定を <c>Update</c> の段で行うと、自分より後に走る Actor の命中を取りこぼす。
        ///
        /// ここでは<b>必ず後に走る</b>注入役（実行順 1000）を置き、最後の敵が倒れた同じフレームの
        /// あとの方で主人公の死亡を通知する。確定が刻みの終わり（LateUpdate）なら死亡が勝つ。
        /// </summary>
        [UnityTest]
        public IEnumerator SameFramePlayerDeath_WinsOverClearInRealUpdateOrder()
        {
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaBScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");

            GameSessionState session = Sessions().Session;
            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            var combatSession = Object.FindFirstObjectByType<CombatSessionController>();
            var playerVitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.IsNotNull(runner);
            Assert.IsNotNull(combatSession);
            Assert.IsNotNull(playerVitals);

            yield return MovePlayerTo(EncounterTriggerPoint);
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 5f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                "前提：戦闘が始まる。拒否=" + runner.LastRejection + " " + runner.LastFailureDetail);

            EnemyActor[] enemies = Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);
            Assert.AreEqual(2, enemies.Length);

            // 1 体目は実 Hitbox で倒す（本物の戦闘の途中であることを保つ）。
            yield return KillWithRealHitbox(enemies[0]);
            Assert.AreEqual(1, combatSession.AliveEnemyCount, "前提：あと 1 体。");

            // どちらの敵が先に倒れるかは列挙順しだいなので、額ではなく「変わらないこと」を見る。
            int virtueAfterRealKill = session.Progress.Virtue;
            Assert.Greater(virtueAfterRealKill, 0, "前提：実撃破で徳が入っている。");

            // <b>確定の前後を挟む</b>。実行順 -1000 の注入役が最後の敵の撃破を通知し、
            // 実行順 1000 の注入役が同じフレームのあとの方で主人公の死亡を通知する。
            // 確定が Update の段にあると、この 2 つの<b>間</b>で勝利が確定してしまう。
            var earlyGo = new GameObject("EarlyEnemyDefeatInjector");
            var early = earlyGo.AddComponent<EarlyEnemyDefeatInjector>();
            early.Bind(enemies[1]);

            var lateGo = new GameObject("LateDefeatInjector");
            var late = lateGo.AddComponent<LateDefeatInjector>();
            late.Bind(early, playerVitals.Defeats);

            try
            {
                early.Armed = true;
                late.Armed = true;

                yield return WaitUntilOrTimeout(
                    () => early.FiredCount > 0 && late.FiredCount > 0, 5f);
                yield return null;

                Assert.AreEqual(1, early.FiredCount, "最後の敵の撃破を 1 回だけ通知している（前提）。");
                Assert.AreEqual(1, late.FiredCount, "同じフレームの後の方で死亡を通知している（前提）。");
                Assert.AreEqual(AreaEncounterState.Defeated, runner.State,
                    "同じフレームの死亡が勝利に優先する（処理順だけで相打ちを勝利にしない）。");
            }
            finally
            {
                Object.DestroyImmediate(earlyGo);
                Object.DestroyImmediate(lateGo);
            }

            Assert.IsTrue(session.TryGetArea(AreaB, out AreaRuntimeState areaB));
            Assert.IsFalse(areaB.IsEncounterCleared(new StableId("encounter_p5_b_road"), session.RespawnCycle),
                "勝利記録は付けない。");
            Assert.IsFalse(arena.IsEnabled, "一時境界は解放する。");
            Assert.AreEqual(0, arena.ActiveBlockerCount);
            // 実際に倒した 1 体ぶんの徳は、あとで死んでも取り消さない（§8.5）。
            // 2 体目は順序を作るために通知だけを注入しているので、報酬は載せていない。
            Assert.AreEqual(virtueAfterRealKill, session.Progress.Virtue,
                "撃破済みの徳は取り消さない（§8.5）。");

            yield return ReleaseKeys();
        }

        /// <summary>
        /// 最後の敵の撃破を、実行順の<b>先頭側</b>（-1000）で実チャネルへ通知する注入役（テスト専用）。
        ///
        /// 撃破の通知経路（<c>EnemyDefeatChannel</c> → <c>CombatSessionController</c>）はそのまま通す。
        /// 通知の<b>タイミング</b>だけを固定したいので、HP の削りは行わない
        /// （実 Hitbox の撃破は同じテストの 1 体目と、P05／P08 が通している）。
        /// </summary>
        [DefaultExecutionOrder(-1000)]
        private sealed class EarlyEnemyDefeatInjector : MonoBehaviour
        {
            private EnemyActor _enemy;

            public bool Armed { get; set; }

            public int FiredCount { get; private set; }

            public void Bind(EnemyActor enemy)
            {
                _enemy = enemy;
            }

            private void Update()
            {
                if (!Armed || FiredCount > 0 || _enemy == null)
                {
                    return;
                }

                FiredCount++;
                _enemy.Defeats.Publish(new EnemyDefeatedEvent(
                    _enemy.DamageableId,
                    new EnemyRewardRequest(_enemy.DamageableId, EnemyRole.Ranged, null, _enemy.transform.position)));
            }
        }

        /// <summary>
        /// 生存が 0 になったフレームの<b>後の方</b>で主人公の死亡を通知する注入役（テスト専用）。
        ///
        /// 実行順を 1000 に固定してあるので、既定順（0）の <c>AreaEncounterRunner.Update</c> より必ず後に走る。
        /// 死亡条件の設定はテスト専用の公開経路で行ってよい（仕様書 §15.3 の但し書き）。
        /// </summary>
        [DefaultExecutionOrder(1000)]
        private sealed class LateDefeatInjector : MonoBehaviour
        {
            private EarlyEnemyDefeatInjector _early;
            private PlayerDefeatChannel _channel;

            public bool Armed { get; set; }

            public int FiredCount { get; private set; }

            /// <summary>
            /// 撃破を通知した側を見て、<b>同じフレームの後の方</b>で死亡を通知する。
            ///
            /// 生存数のような<b>結果</b>を条件にすると、確定が早すぎる実装では
            /// その結果が先に消されてしまい（登録の解放）、「前提が満たせない」という形で落ちる。
            /// それでは何が壊れたのか読めないので、条件は<b>撃破を通知したか</b>だけにする。
            /// </summary>
            public void Bind(EarlyEnemyDefeatInjector early, PlayerDefeatChannel channel)
            {
                _early = early;
                _channel = channel;
            }

            private void Update()
            {
                if (!Armed || FiredCount > 0 || _early == null || _channel == null || _early.FiredCount == 0)
                {
                    return;
                }

                FiredCount++;
                _channel.Publish(new PlayerDefeatedEvent(1, Vector3.zero));
            }
        }

        /// <summary>
        /// 命中 Feedback が P5 の Scene へ接続されていて、<b>生成直後の最初の命中</b>も拾える
        /// （§8.2 手順 7。GPT レビュー R4 の指摘 4。P5-P08 の補助）。
        ///
        /// 配信役は周期（既定 1 秒）で対象を探し直す。その周期を待つ実装だと、
        /// 湧いた直後の数発が「当たったのに手応えが無い」状態になる。
        /// <b>最初の命中のフレームまでに購読が済んでいること</b>をフレーム番号で見る。
        /// </summary>
        [UnityTest]
        public IEnumerator EncounterFeedback_IsConnectedBeforeTheFirstHit()
        {
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaBScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");

            var dispatcher = Object.FindFirstObjectByType<CombatFeedbackDispatcher>();
            Assert.IsNotNull(dispatcher, "命中 Feedback の配信役が Scene にある（§8.2 手順 7）。");

            var presenter = Object.FindFirstObjectByType<CombatFeedbackPresenter>();
            Assert.IsNotNull(presenter, "手応え演出の調停役がある。");
            Assert.IsNotNull(presenter.CameraShake, "揺れは Camera 子の既存 ShakePresenter を使う（§11）。");

            var binder = Object.FindFirstObjectByType<EncounterFeedbackBinder>();
            Assert.IsNotNull(binder, "生成直後に購読し直す橋渡しがある。");
            Assert.IsTrue(binder.IsWired);
            Assert.AreEqual(0, binder.RescanCount, "前提：まだ生成していない。");

            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            var log = new FeedbackFrameLog();
            dispatcher.Feedback.AddListener(log);

            try
            {
                yield return MovePlayerTo(EncounterTriggerPoint);
                yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 5f);
                Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                    "前提：戦闘が始まる。拒否=" + runner.LastRejection + " " + runner.LastFailureDetail);
                Assert.AreEqual(1, binder.RescanCount, "活動許可の直後に購読し直す（周期を待たない）。");

                EnemyActor[] enemies = Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);
                Assert.AreEqual(2, enemies.Length);

                var hits = new EnemyFirstDamageLog();
                for (int i = 0; i < enemies.Length; i++)
                {
                    enemies[i].Results.AddListener(hits);
                }

                try
                {
                    yield return KillWithRealHitbox(enemies[0]);
                }
                finally
                {
                    for (int i = 0; i < enemies.Length; i++)
                    {
                        if (enemies[i] != null)
                        {
                            enemies[i].Results.RemoveListener(hits);
                        }
                    }
                }

                Assert.Greater(hits.FirstDamageFrame, 0, "前提：敵に実際の命中が届いている。");
                Assert.IsNotNull(hits.FirstTarget, "前提：最初の命中の被弾対象が取れる。");
                Assert.Greater(log.Count, 0, "命中が Feedback へ届く。");

                // <b>同じ命中</b>で照合する。件数や「どれか 1 件」では、別の敵・別の結果種別の
                // Feedback をもって「最初の命中も拾えた」と誤読しうる。
                // 敵側で観測した最初のダメージ（HitId・被弾対象・種別・フレーム）と
                // 同一のものが Feedback 側にも出ていることを求める。
                Assert.IsTrue(log.TryFind(hits.FirstHitId, hits.FirstTarget, out int matchedFrame, out HitResultKind matchedKind),
                    "<b>最初の命中そのもの</b>が Feedback へ届く（別の敵・別の攻撃で代用しない）。"
                    + " 命中=" + Describe(hits.FirstHitId, hits.FirstTarget)
                    + " Feedback 側=" + log.Describe());
                Assert.AreEqual(HitResultKind.Damage, matchedKind, "結果種別も同じ（ダメージがダメージとして届く）。");
                Assert.AreEqual(hits.FirstDamageFrame, matchedFrame,
                    "同じフレームで届く（周期の再探索を待って遅れていない）。"
                    + " Feedback=" + matchedFrame + " 命中=" + hits.FirstDamageFrame);
                Assert.LessOrEqual(log.FirstFrame, hits.FirstDamageFrame,
                    "<b>最初の命中</b>の時点で購読済み（周期の再探索を待っていない）。"
                    + " 最初の Feedback=" + log.FirstFrame + " 最初の命中=" + hits.FirstDamageFrame);
            }
            finally
            {
                dispatcher.Feedback.RemoveListener(log);
            }

            yield return ReleaseKeys();
        }

        /// <summary>命中の同一性を読める文にする（失敗時の手掛かり）。</summary>
        private static string Describe(HitId hitId, IDamageable target)
        {
            string name = target is Component component && component != null ? component.gameObject.name : "(不明)";
            return "HitId=" + hitId.InstanceId + ":" + hitId.Stage + " 対象=" + name;
        }

        /// <summary>
        /// 届いた Feedback を<b>命中ごと</b>に覚える。件数だけでは「別の敵の別の攻撃」を
        /// 取り違えるため、HitId と被弾対象で引けるようにしておく。
        /// </summary>
        private sealed class FeedbackFrameLog : ICombatFeedbackListener
        {
            private readonly List<Entry> _entries = new List<Entry>();

            public int Count => _entries.Count;

            public int FirstFrame { get; private set; } = int.MaxValue;

            public void OnCombatFeedback(in CombatFeedbackEvent feedback)
            {
                _entries.Add(new Entry(feedback.Result.HitId, feedback.Result.Target, feedback.Result.Kind, Time.frameCount));
                if (FirstFrame == int.MaxValue)
                {
                    FirstFrame = Time.frameCount;
                }
            }

            /// <summary>同じ命中（HitId＋被弾対象）の Feedback を探す。</summary>
            public bool TryFind(HitId hitId, IDamageable target, out int frame, out HitResultKind kind)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    Entry entry = _entries[i];
                    if (entry.HitId == hitId && ReferenceEquals(entry.Target, target))
                    {
                        frame = entry.Frame;
                        kind = entry.Kind;
                        return true;
                    }
                }

                frame = 0;
                kind = HitResultKind.Rejected;
                return false;
            }

            /// <summary>診断用に届いた分を並べる。</summary>
            public string Describe()
            {
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(" / ");
                    }

                    Entry entry = _entries[i];
                    text.Append(P5ExplorationPlayTests.Describe(entry.HitId, entry.Target))
                        .Append(" 種別=").Append(entry.Kind)
                        .Append(" F=").Append(entry.Frame);
                }

                return text.Length == 0 ? "(なし)" : text.ToString();
            }

            private readonly struct Entry
            {
                public Entry(HitId hitId, IDamageable target, HitResultKind kind, int frame)
                {
                    HitId = hitId;
                    Target = target;
                    Kind = kind;
                    Frame = frame;
                }

                public HitId HitId { get; }

                public IDamageable Target { get; }

                public HitResultKind Kind { get; }

                public int Frame { get; }
            }
        }

        /// <summary>敵に最初のダメージが届いた命中（同一性・対象・フレーム）を覚える。</summary>
        private sealed class EnemyFirstDamageLog : IHitResultListener
        {
            public int FirstDamageFrame { get; private set; }

            public HitId FirstHitId { get; private set; }

            public IDamageable FirstTarget { get; private set; }

            public void OnHitResult(in HitResult result)
            {
                if (result.Kind == HitResultKind.Damage && FirstDamageFrame == 0)
                {
                    FirstDamageFrame = Time.frameCount;
                    FirstHitId = result.HitId;
                    FirstTarget = result.Target;
                }
            }
        }

        /// <summary>
        /// Pause と受理済み Loading は<b>新規操作を受け付けない</b>。解除後は新しい押下が要る
        /// （§8.3 の競合表。GPT レビュー R4 への追加。P5-P07 の補助）。
        /// </summary>
        [UnityTest]
        public IEnumerator PauseAndLoadingRejectNewOperationsAndStaleInput()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");

            IGameModeService modes = GameModeProvider.Current;
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            var mediator = Object.FindFirstObjectByType<AreaInteractInput>();
            var interaction = Object.FindFirstObjectByType<AreaInteractionController>();
            Assert.IsNotNull(lever);
            Assert.IsNotNull(mediator);
            Assert.IsNotNull(interaction);

            yield return MovePlayerTo(lever.InteractionAnchor + new Vector3(0.6f, 0f, 0f));

            // ---- Pause 中は新規操作を受け付けない ----
            modes.ChangeMode(GameMode.Paused);
            yield return null;
            yield return PressKey(Key.E);
            for (int i = 0; i < 12; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0, lever.OpenedCount, "Pause 中の押下では開通しない。");
            Assert.AreEqual(0, mediator.InteractCount, "窓口まで通さない。");

            // ---- 解除しても、押しっぱなしの旧入力では動かない ----
            modes.ChangeMode(GameMode.Exploration);
            for (int i = 0; i < 12; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0, lever.OpenedCount,
                "解除しただけでは動かない（Pause で落ちた押下を復活させない）。");
            Assert.AreEqual(0, mediator.InteractCount);

            // ---- 押し直せば動く ----
            yield return ReleaseKeys();
            yield return PressKey(Key.E);
            yield return WaitUntilOrTimeout(() => lever.OpenedCount >= 1, 3f);
            yield return ReleaseKeys();
            Assert.AreEqual(1, lever.OpenedCount, "新しい押下でだけ動く。");

            // ---- 受理済み Loading 中は、古い Trigger の要求を捨てる ----
            yield return SceneManager.LoadSceneAsync(AreaBScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            Assert.IsNotNull(runner);
            Assert.AreEqual(AreaEncounterState.Dormant, runner.State);

            AreaTransitionService service = Transitions();
            Assert.IsTrue(service.TryTravel(AreaA, AreaAFromB).Accepted, "遷移を受理させる。");
            Assert.IsTrue(service.Clock.IsFrozen, "受理で Gameplay 時計が止まる（前提）。");

            EncounterStartDecision stale = runner.TryStart();

            Assert.IsFalse(stale.Started, "Loading 中の古い Trigger 要求は通さない。");

            // 断り方は 3 通りありうる。§6.2 手順 3 が受理の時点で活動を閉じ（AreaNotReady）、
            // モードを Loading へ変え（WrongMode）、Gameplay 時計を止める（Transitioning）ためで、
            // どれも「遷移側を維持する」同じ判断の別の面。<b>通らないこと</b>が要点なので 3 つとも受ける。
            Assert.IsTrue(
                stale.Rejection == EncounterStartRejection.Transitioning
                || stale.Rejection == EncounterStartRejection.WrongMode
                || stale.Rejection == EncounterStartRejection.AreaNotReady,
                "遷移側を維持して断る。理由=" + stale.Rejection);
            Assert.AreEqual(AreaEncounterState.Dormant, runner.State, "状態も動かさない。");
            Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                "敵も湧かない。");
            Assert.IsFalse(arena.IsEnabled, "境界も閉じない。");

            AreaTransitionCoordinator coordinator = service.Coordinator;
            yield return WaitForArrival(coordinator, 1);
            yield return ReleaseKeys();
        }

        // ---------------------------------------------------------------- P17（カメラ）

        /// <summary>
        /// P5-P17：領域移動・Scene 到着・揺れのあとで<b>基準位置がずれない</b>。
        /// Camera・AudioListener・入力・HUD は活動中<b>各 1 つ</b>（§11）。
        ///
        /// <b>「揺れても戻る」だけを見ない。</b> <c>CameraShakePresenter</c> は満了で基準へ戻すので、
        /// 追従が同じ Transform を書いていても「戻った」ようには見える。壊れるのは
        /// <b>基準そのもの</b>で、追従の書込みが揺れの基準を上書きすると、揺れ終わりに
        /// Camera 子の局所位置が別の値になる。だから<b>Rig の位置＝収めた基準位置</b>と
        /// <b>Camera 子の局所位置＝出荷時のオフセット</b>を別々に見る。
        ///
        /// 画面比は実行環境で変わるので、期待値は絶対値ではなく
        /// <c>CameraBoundsMath.ClampFocus</c> という同じ純粋関数から作る。
        /// 奥行きだけは俯角だけで決まる（<c>size / sin(俯角)</c>）ので、追従する軸として固定で使える。
        ///
        /// 補間の途中を見る区間は <b>Rig の LateUpdate を止めて時間を注入する</b>。
        /// 実フレームの間隔は編集器の負荷で変わり、0.15 秒の補間が 1 フレームで終わることがある。
        /// </summary>
        [UnityTest]
        public IEnumerator CameraTransition_PreservesBoundsAndShakeBase()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            AssertSingleOwners("A 直開き");

            var rig = Object.FindFirstObjectByType<AreaCameraRig>();
            Assert.IsNotNull(rig, "エリアにカメラ Rig がある。");
            Assert.IsTrue(rig.IsWired, "追従対象・カメラ・既定領域が配線されている。");

            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "Main Camera がある。");
            Assert.AreSame(rig.transform, camera.transform.parent,
                "Camera は Rig の子（追従と揺れで書込み先を分ける。§11）。");

            var shake = Object.FindFirstObjectByType<CameraShakePresenter>();
            Assert.IsNotNull(shake, "既存の ShakePresenter を使う（§11。独自 HitStop を足さない）。");
            Assert.AreSame(camera.transform, shake.Target, "揺れは Camera 子へ書く。");

            Vector3 shakeBase = camera.transform.localPosition;
            Assert.Greater(shakeBase.y, 0.1f, "前提：カメラは Rig の上にある。");

            var playerRoot = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(playerRoot);

            // ---- 到着では補間しない（§11「Scene 到着・死亡再開では補間せず即時配置」）----
            Assert.IsFalse(rig.Blend.IsBlending, "到着の直後に補間が走っていない（前の部屋から滑ってこない）。");
            AssertFocusIsClamped(rig, playerRoot.transform.position, "到着");
            Assert.GreaterOrEqual(rig.SnapCount, 1, "到着で即時配置している。");

            Vector2 half = rig.HalfFootprint();
            Assert.Greater(9f, half.y, "前提：奥行きは部屋（半分 9m）より見える範囲が狭い＝追従する軸。");
            Assert.Greater(half.x, 3f, "前提：東の通路（半分 3m）は横が入りきらない＝中央固定になる軸。");

            // 範囲の内側なら主人公の位置そのもの。
            yield return MovePlayerTo(new Vector3(-1f, 0f, 2f));
            rig.Tick(1f);
            Assert.AreEqual(2f, rig.transform.position.z, 0.02f,
                "範囲の内側では主人公をそのまま追う。z=" + rig.transform.position.z);
            AssertFocusIsClamped(rig, playerRoot.transform.position, "西の部屋・中央");

            // 端では領域に収める（外＝仮背景を見せない）。
            yield return MovePlayerTo(new Vector3(-1f, 0f, -7f));
            rig.Tick(1f);
            float southZ = rig.transform.position.z;
            Assert.Greater(southZ, -7f + 1f, "南の端は領域に収める（主人公より手前で止まる）。z=" + southZ);
            AssertFocusIsClamped(rig, playerRoot.transform.position, "西の部屋・南");

            yield return MovePlayerTo(new Vector3(-1f, 0f, 7f));
            rig.Tick(1f);
            float northZ = rig.transform.position.z;
            Assert.Greater(northZ, southZ + 1f,
                "入りきる軸は主人公を追う（止まったままにしない）。南=" + southZ + " 北=" + northZ);
            AssertFocusIsClamped(rig, playerRoot.transform.position, "西の部屋・北");

            // ---- 領域移動：切替は補間し、<b>途中のフレームも範囲内</b>（§11）----
            //
            // 補間の途中を見る区間だけ切替時間を延ばす。実フレームの間隔は編集器の負荷で変わり、
            // 0.15 秒の補間が 1 フレームで終わってしまうと「途中」が存在しなくなる。
            var context = Object.FindFirstObjectByType<AreaContext>();
            Assert.IsNotNull(context);
            SetPrivate(rig, "_blendSeconds", 2f);

            try
            {
                Assert.AreEqual("region_p5_a_west", rig.CurrentRegion.RegionId.Value, "いまは西の大部屋。");
                int changesBefore = rig.Blend.RegionChangeCount;

                yield return MovePlayerTo(new Vector3(9f, 0f, -6f));
                rig.Tick(0.001f);
                Assert.AreEqual("region_p5_a_east", rig.CurrentRegion.RegionId.Value, "東の通路へ移る。");
                Assert.AreEqual(changesBefore + 1, rig.Blend.RegionChangeCount, "切替を 1 回数える。");
                Assert.IsTrue(rig.Blend.IsBlending, "部屋の切替は補間する（§11）。");

                for (int i = 0; i < 20; i++)
                {
                    rig.Tick(0.02f);

                    // 補間の途中は「前の部屋の端」と「今の部屋の端」の間に居る。
                    // 追従先と一致はしないが、<b>いまの領域の有効範囲からは出ない</b>（§11）。
                    AssertFocusInsideRegion(rig, "補間の途中 " + i);
                }

                Assert.IsTrue(rig.Blend.IsBlending, "前提：まだ補間の途中（途中が存在している）。");

                rig.Tick(5f);
                Assert.IsFalse(rig.Blend.IsBlending, "補間は終わる。");
                Assert.AreEqual(9f, rig.transform.position.x, 0.02f,
                    "入りきらない軸は中央固定（東の通路の中心 x=9）。x=" + rig.transform.position.x);
                AssertFocusIsClamped(rig, playerRoot.transform.position, "東の通路");

                // ---- 準備完了の報告は<b>補間を打ち切って</b>即時配置する（§5.1 手順 7／§11）----
                //
                // Scene 到着・死亡再開でカメラが前の部屋から滑ってくると、居なかった場所に居たように見える。
                // 初期化担当（Infrastructure）は Presentation を知らないので、この即時配置は
                // AreaContext の準備完了通知を Rig が購読して行う。
                yield return MovePlayerTo(new Vector3(-1f, 0f, 2f));
                rig.Tick(0.001f);
                Assert.IsTrue(rig.Blend.IsBlending, "前提：部屋を跨いだので補間が始まっている。");

                int snapsBefore = rig.SnapCount;
                context.MarkPrepared();

                // まず<b>振る舞い</b>を見る。回数は「即時配置を通った」ことの裏取り。
                Assert.IsFalse(rig.Blend.IsBlending, "即時配置は補間を打ち切る（前の部屋から滑ってこない）。");
                AssertFocusIsClamped(rig, playerRoot.transform.position, "準備完了");
                Assert.AreEqual(snapsBefore + 1, rig.SnapCount, "準備完了の報告で即時配置する。");
            }
            finally
            {
                SetPrivate(rig, "_blendSeconds", CameraFocusBlend.DefaultBlendSeconds);
            }

            // ---- 揺れ：追従と書込み先が分かれているので、基準がずれない ----
            Assert.IsFalse(shake.IsShaking, "前提：揺れていない。");
            shake.Shake(0.3f, 0.25f);
            Assert.IsTrue(shake.IsShaking);

            // 揺れている最中に部屋の中で動かす。同じ Transform を 2 人が書いていると、ここで基準が壊れる。
            yield return MovePlayerTo(new Vector3(9f, 0f, 5f));
            yield return WaitUntilOrTimeout(() => !shake.IsShaking, 3f);
            Assert.IsFalse(shake.IsShaking, "揺れは満了で止まる。");
            yield return null;

            Assert.AreEqual(shakeBase.x, camera.transform.localPosition.x, 0.001f,
                "揺れ終わりに Camera 子の基準が戻る（追従が基準を書いていない）。");
            Assert.AreEqual(shakeBase.y, camera.transform.localPosition.y, 0.001f);
            Assert.AreEqual(shakeBase.z, camera.transform.localPosition.z, 0.001f);

            rig.Tick(1f); // 部屋を跨いだ分の補間を終わらせてから基準を見る。
            AssertFocusIsClamped(rig, playerRoot.transform.position, "揺れのあと");
            Vector3 expectedCamera = rig.transform.position + shakeBase;
            Assert.Less(Vector3.Distance(expectedCamera, camera.transform.position), 0.01f,
                "カメラの世界位置は Rig の基準＋出荷時のオフセット。期待=" + expectedCamera
                + " 実際=" + camera.transform.position);

            // ---- Scene 到着：旧 Scene の Camera・Listener を残さない ----
            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;
            Assert.IsNotNull(coordinator);
            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);

            float waited = 0f;
            while (coordinator.CompletedCount < 1 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, coordinator.CompletedCount, "B へ到着する。");
            AssertSingleOwners("B 到着");

            var rigB = Object.FindFirstObjectByType<AreaCameraRig>();
            Assert.IsNotNull(rigB, "B にもカメラ Rig がある。");
            Assert.AreNotSame(rig, rigB, "旧 Scene の Rig は残らない。");
            Assert.IsTrue(rigB.IsWired);
            Assert.GreaterOrEqual(rigB.SnapCount, 1, "到着で即時配置している（補間で滑ってこない）。");
            Assert.IsFalse(rigB.Blend.IsBlending, "到着の直後に補間が走っていない。");

            var playerB = Object.FindFirstObjectByType<PlayerRoot>();
            AssertFocusIsClamped(rigB, playerB.transform.position, "B 到着");
        }

        /// <summary>
        /// Rig の位置が「いまの領域に収めた基準位置」と一致することを見る。
        /// 画面比が環境で変わるので、期待値は同じ純粋関数から作る（絶対値で書かない）。
        /// </summary>
        private static void AssertFocusIsClamped(AreaCameraRig rig, Vector3 target, string label)
        {
            Vector3 expected = CameraBoundsMath.ClampFocus(target, rig.CurrentRegion, rig.HalfFootprint());
            Assert.AreEqual(expected.x, rig.transform.position.x, 0.02f,
                label + "：基準の x が領域に収まっている。region=" + rig.CurrentRegion.RegionId.Value);
            Assert.AreEqual(expected.z, rig.transform.position.z, 0.02f,
                label + "：基準の z が領域に収まっている。region=" + rig.CurrentRegion.RegionId.Value);
        }

        /// <summary>
        /// 基準位置が<b>いまの領域の有効範囲</b>に入っていることを見る（補間の途中でも成り立つ条件）。
        /// 部屋の方が見える範囲より狭い軸は中央固定（§11「無理な min/max clamp をしない」）。
        /// </summary>
        private static void AssertFocusInsideRegion(AreaCameraRig rig, string label)
        {
            Vector2 half = rig.HalfFootprint();
            CameraRegionDefinition region = rig.CurrentRegion;
            Vector3 focus = rig.transform.position;

            AssertAxisInside(focus.x, region.Min.x, region.Max.x, half.x, region.Center.x, label + "：x");
            AssertAxisInside(focus.z, region.Min.y, region.Max.y, half.y, region.Center.y, label + "：z");
        }

        private static void AssertAxisInside(
            float value, float min, float max, float half, float center, string label)
        {
            float low = min + half;
            float high = max - half;

            if (low > high)
            {
                Assert.AreEqual(center, value, 0.02f, label + "：入りきらない軸は中央固定。");
                return;
            }

            Assert.GreaterOrEqual(value, low - 0.02f, label + "：領域の外を映さない（下限 " + low + "）。");
            Assert.LessOrEqual(value, high + 0.02f, label + "：領域の外を映さない（上限 " + high + "）。");
        }

        /// <summary>
        /// Camera・AudioListener・入力・HUD が活動中 1 つずつであることを見る（§11）。
        /// 旧 Scene から持ち越すと、音が二重になったり押下が 2 回消費されたりする。
        /// </summary>
        private static void AssertSingleOwners(string label)
        {
            Assert.AreEqual(1, Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length,
                label + "：活動中の Camera は 1 つ。");
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length,
                label + "：活動中の AudioListener は 1 つ。");
            Assert.AreEqual(1, Object.FindObjectsByType<AreaInteractInput>(FindObjectsSortMode.None).Length,
                label + "：Interact の押下を消費する入力は 1 つ（§7.1）。");
            Assert.AreEqual(0, Object.FindObjectsByType<InvestigationInteractInput>(FindObjectsSortMode.None).Length,
                label + "：P4 の調査専用入力は置かない（同じ押下を 2 回消費させない）。");
            Assert.AreEqual(1, Object.FindObjectsByType<CombatPlayHud>(FindObjectsSortMode.None).Length,
                label + "：HUD は 1 つ。");
            Assert.AreEqual(1, Object.FindObjectsByType<AreaContext>(FindObjectsSortMode.None).Length,
                label + "：AreaContext は 1 つ。");
        }

        // ---------------------------------------------------------------- P07（Pause／Loading）

        /// <summary>
        /// P5-P07：実行中の<b>経路・探索・戦闘</b>を Pause と Loading が止め、復帰で続く。
        /// Loading へ<b>旧速度も Hitbox も残さない</b>（§6.2 手順 3〜5、§11、受入 P07）。
        ///
        /// <b>止まることだけを見ると、全部止まったままの実装が通る。</b> だからどの区間でも
        /// 「動いている」ことを先に確かめ、止めて、<b>また動く</b>ところまで見る。
        ///
        /// Pause と Loading は<b>止め方が違う</b>（§12.1／<c>CompanionActivity</c>）。
        /// Pause は<b>凍結</b>で、進行中の攻撃・調査を保ったまま時計だけ止める。
        /// Loading は遷移の受理で<b>進行中の行動を同期的に打ち切って</b>から値を採る。
        /// 同じ「止まった」で済ませると、どちらかが必ず壊れる。
        ///
        /// 入力は実デバイス（W）を通す。<b>凍結の窓口を差し替えない</b>のがここの要点で、
        /// 偽の入力を刺すと GameMode のゲートを跨いでしまい、Pause で止まる理由が消える。
        /// </summary>
        [UnityTest]
        public IEnumerator PauseAndLoading_StopActorsAndResumeWithoutResidue()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            IGameModeService modes = GameModeProvider.Current;
            Assert.IsNotNull(modes);
            Assert.AreEqual(GameMode.Exploration, modes.Current, "前提：探索中。");

            var playerRoot = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(playerRoot);
            Rigidbody playerBody = playerRoot.Body;
            Assert.IsNotNull(playerBody);

            var companionMotor = Object.FindFirstObjectByType<CompanionMotor>();
            Assert.IsNotNull(companionMotor, "犬丸の Motor がある。");
            Rigidbody companionBody = companionMotor.GetComponent<Rigidbody>();
            Assert.IsNotNull(companionBody);

            var follow = Object.FindFirstObjectByType<CompanionFollowController>();
            Assert.IsNotNull(follow);
            var combat = Object.FindFirstObjectByType<CompanionCombatController>();
            Assert.IsNotNull(combat);
            var investigation = Object.FindFirstObjectByType<CompanionInvestigationController>();
            Assert.IsNotNull(investigation);
            var coordinator = Object.FindFirstObjectByType<InvestigationCoordinator>();
            Assert.IsNotNull(coordinator);

            // ================================ 1. 経路（追従）================================
            yield return MovePlayerTo(new Vector3(0f, 0f, -6f));
            yield return PressKey(Key.W);
            yield return WaitUntilOrTimeout(() => PlanarSpeed(playerBody) > 0.5f, 3f);
            Assert.Greater(PlanarSpeed(playerBody), 0.5f,
                "前提：実キーで主人公が動いている。速度=" + PlanarSpeed(playerBody));

            yield return WaitUntilOrTimeout(() => PlanarSpeed(companionBody) > 0.1f, 5f);
            Assert.Greater(PlanarSpeed(companionBody), 0.1f,
                "前提：犬丸が付いてきている。速度=" + PlanarSpeed(companionBody));

            modes.ChangeMode(GameMode.Paused);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.Less(PlanarSpeed(playerBody), 0.01f,
                "Pause で主人公に旧速度が残らない。速度=" + PlanarSpeed(playerBody));
            Assert.Less(PlanarSpeed(companionBody), 0.01f,
                "Pause で犬丸に旧速度が残らない。速度=" + PlanarSpeed(companionBody));

            Vector3 pausedPlayer = playerBody.position;
            Vector3 pausedCompanion = companionBody.position;
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(Vector3.Distance(pausedPlayer, playerBody.position), 0.05f,
                "Pause 中は押しっぱなしでも進まない。");
            Assert.Less(Vector3.Distance(pausedCompanion, companionBody.position), 0.05f,
                "Pause 中は犬丸も進まない（経路の追従が止まる）。");

            // 復帰。押下は Pause でゲートが閉じたときに落ちているので、押し直して同じ経路を通す。
            modes.ChangeMode(GameMode.Exploration);
            yield return ReleaseKeys();
            yield return PressKey(Key.W);
            yield return WaitUntilOrTimeout(() => PlanarSpeed(playerBody) > 0.5f, 3f);
            Assert.Greater(PlanarSpeed(playerBody), 0.5f, "復帰すればまた動く（止めっぱなしにしない）。");
            yield return ReleaseKeys();
            yield return WaitUntilOrTimeout(() => PlanarSpeed(playerBody) < 0.01f, 2f);

            // ================================ 2. 探索（調査）================================
            // 調査地点は (-3, 0, 1)、受付距離は 1.5m（SO_Investigation_Trial）。余裕を持って内側に立つ。
            yield return MovePlayerTo(new Vector3(-3f, 0f, -0.1f));
            yield return WaitUntilOrTimeout(() => PlanarSpeed(companionBody) < 0.05f, 4f);

            InvestigationRequestResult request = coordinator.RequestAt(new StableId("point_p5_a_01"));
            Assert.IsTrue(request.Accepted, "調査を受け付ける。理由=" + request.Reason);
            yield return WaitUntilOrTimeout(() => investigation.IsBusy, 3f);
            Assert.IsTrue(investigation.IsBusy, "前提：調査が走っている。");

            modes.ChangeMode(GameMode.Paused);
            yield return null;
            InvestigationPhase pausedPhase = investigation.Phase;
            float pausedProgress = investigation.Progress;
            int interruptedBefore = investigation.InterruptedCount;
            Vector3 pausedAt = companionBody.position;

            for (int i = 0; i < 12; i++)
            {
                yield return null;
            }

            Assert.IsTrue(investigation.IsBusy, "Pause は<b>凍結</b>。依頼を捨てない（§6.4）。");
            Assert.AreEqual(interruptedBefore, investigation.InterruptedCount, "Pause は中断ではない。");
            Assert.AreEqual(pausedPhase, investigation.Phase, "Pause 中は段が進まない。");
            Assert.AreEqual(pausedProgress, investigation.Progress, 1e-4f, "Pause 中は進捗が進まない。");
            Assert.Less(Vector3.Distance(pausedAt, companionBody.position), 0.05f,
                "Pause 中は調査へ歩かない。");

            modes.ChangeMode(GameMode.Exploration);
            yield return WaitUntilOrTimeout(
                () => investigation.Progress > pausedProgress + 1e-3f
                    || investigation.Phase != pausedPhase
                    || Vector3.Distance(pausedAt, companionBody.position) > 0.1f,
                5f);
            Assert.IsTrue(
                investigation.Progress > pausedProgress + 1e-3f
                || investigation.Phase != pausedPhase
                || Vector3.Distance(pausedAt, companionBody.position) > 0.1f,
                "復帰すれば<b>同じ依頼の続き</b>が進む。段=" + investigation.Phase
                + " 進捗=" + investigation.Progress);

            // 次の区間のために調査を畳む（戦闘が始まれば §8.3 の調停で中断される経路と同じ）。
            coordinator.InterruptAllForCombat();
            yield return null;
            Assert.IsFalse(investigation.IsBusy, "前提：調査は終わっている。");

            // ================================ 3. 戦闘 ================================
            yield return WaitUntilOrTimeout(() => PlanarSpeed(companionBody) < 0.05f, 4f);

            var enemyGo = new GameObject("P07_FakeEnemy");
            var enemy = enemyGo.AddComponent<PauseFakeEnemy>();
            enemyGo.transform.position = companionBody.position + new Vector3(0.9f, 0f, 0f);
            PerceptionTargetRegistry.Register(enemy);

            try
            {
                yield return WaitUntilOrTimeout(() => combat.IsAttacking, 6f);
                Assert.IsTrue(combat.IsAttacking,
                    "前提：犬丸が攻撃を始めている。対象=" + (combat.CurrentTarget != null ? "有" : "無"));

                modes.ChangeMode(GameMode.Paused);
                float elapsedBefore = combat.AttackState.Elapsed;
                CompanionAttackPhase phaseBefore = combat.AttackState.Phase;

                for (int i = 0; i < 12; i++)
                {
                    yield return null;
                }

                Assert.IsTrue(combat.IsAttacking, "Pause は<b>凍結</b>。段を捨てない（§12.1）。");
                Assert.AreEqual(phaseBefore, combat.AttackState.Phase, "Pause 中は段が進まない。");
                Assert.AreEqual(elapsedBefore, combat.AttackState.Elapsed, 1e-4f, "Pause 中は攻撃時間が進まない。");

                modes.ChangeMode(GameMode.Exploration);
                yield return WaitUntilOrTimeout(
                    () => combat.AttackState.Elapsed > elapsedBefore + 1e-3f || !combat.IsAttacking, 3f);
                Assert.IsTrue(combat.AttackState.Elapsed > elapsedBefore + 1e-3f || !combat.IsAttacking,
                    "復帰すれば攻撃の続きが進む。");

                // ================================ 4. Loading ================================
                //
                // 主人公も犬丸も動いている状態で遷移を始める。
                yield return PressKey(Key.S);
                yield return WaitUntilOrTimeout(() => PlanarSpeed(playerBody) > 0.5f, 3f);
                Assert.Greater(PlanarSpeed(playerBody), 0.5f, "前提：遷移の直前に主人公が動いている。");
                yield return WaitUntilOrTimeout(() => combat.AttackState.IsHitboxActive, 6f);
                Assert.IsTrue(combat.AttackState.IsHitboxActive,
                    "前提：判定が出ている最中に遷移する（残るなら残る条件で見る）。");

                AreaTransitionService service = Transitions();
                AreaTransitionCoordinator coordinator2 = service.Coordinator;
                Assert.IsNotNull(coordinator2);

                Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted, "遷移が受理される。");

                // 受理した「その場で」止まっている（次のフレームまで判定が生き残らない。§6.2 手順 4）。
                Assert.IsTrue(service.Clock.IsFrozen, "受理で Gameplay 時計が止まる。");
                Assert.AreEqual(GameMode.Loading, modes.Current, "GameMode は Loading。");
                Assert.IsFalse(combat.AttackState.IsHitboxActive,
                    "Loading へ Hitbox を持ち越さない（段=" + combat.AttackState.Phase + "）。");
                Assert.IsFalse(combat.IsAttacking, "進行中の攻撃は同期的に打ち切られる。");

                // 対象の登録は Scene と一緒に消える前に外す（静的登録を次のテストへ残さない）。
                PerceptionTargetRegistry.Unregister(enemy);

                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Assert.Less(PlanarSpeed(playerBody), 0.01f,
                    "Loading 中は旧速度が残らない（旧 Scene が生きている間も滑らない）。速度="
                    + PlanarSpeed(playerBody));
                Assert.Less(PlanarSpeed(companionBody), 0.01f,
                    "犬丸にも旧速度が残らない。速度=" + PlanarSpeed(companionBody));
                Assert.IsFalse(investigation.IsBusy, "Loading へ調査を持ち越さない。");

                float waited = 0f;
                while (coordinator2.CompletedCount < 1 && waited < 15f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                Assert.AreEqual(1, coordinator2.CompletedCount, "B へ到着する。");
            }
            finally
            {
                PerceptionTargetRegistry.Unregister(enemy);
                if (enemyGo != null)
                {
                    Object.DestroyImmediate(enemyGo);
                }
            }

            yield return ReleaseKeys();

            // ---- 到着側：残留なしで再開できる ----
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "到着で凍結が解ける。");
            Assert.AreEqual(GameMode.Exploration, modes.Current, "探索へ戻る。");

            var playerB = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(playerB);
            var companionMotorB = Object.FindFirstObjectByType<CompanionMotor>();
            Assert.IsNotNull(companionMotorB);
            Rigidbody companionBodyB = companionMotorB.GetComponent<Rigidbody>();
            var combatB = Object.FindFirstObjectByType<CompanionCombatController>();
            var investigationB = Object.FindFirstObjectByType<CompanionInvestigationController>();

            yield return new WaitForFixedUpdate();
            Assert.Less(PlanarSpeed(playerB.Body), 0.01f,
                "到着した主人公に旧速度が残らない（押しっぱなしを新しい押下と解釈もしない）。");

            // 犬丸は到着した瞬間から追従を始めてよい（速度 0 を求めるのは「動かない犬丸」を通してしまう）。
            // ここで見たいのは<b>旧 Scene の位置と行動を引きずっていないこと</b>。
            Assert.Less(Vector3.Distance(companionBodyB.position, playerB.transform.position), 3f,
                "犬丸は入口の隣へ置き直される（旧 Scene の位置から滑ってこない）。距離="
                + Vector3.Distance(companionBodyB.position, playerB.transform.position));
            Assert.IsFalse(combatB.AttackState.IsHitboxActive, "到着側に Hitbox が残らない。");
            Assert.IsFalse(combatB.IsAttacking, "到着側で攻撃が続いていない。");
            Assert.IsFalse(investigationB.IsBusy, "中断した調査を自動で再開しない（§6.3）。");

            // 到着後もちゃんと動く（止めっぱなしにしない）。
            yield return PressKey(Key.S);
            yield return WaitUntilOrTimeout(() => PlanarSpeed(playerB.Body) > 0.5f, 3f);
            Assert.Greater(PlanarSpeed(playerB.Body), 0.5f, "到着後は実キーでまた動く。");
            yield return ReleaseKeys();
        }

        /// <summary>XZ 平面の速さ（Y は重力・接地の話なので見ない）。</summary>
        private static float PlanarSpeed(Rigidbody body)
        {
            if (body == null)
            {
                return 0f;
            }

            Vector3 v = body.linearVelocity;
            return new Vector2(v.x, v.z).magnitude;
        }

        /// <summary>P07 用の敵役。犬丸に攻撃を始めさせるためだけの登録先で、被弾は受け流す。</summary>
        private sealed class PauseFakeEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
        {
            public CombatFaction Faction => CombatFaction.Enemy;

            public int FloorId => 0;

            public Vector3 WorldPosition => transform.position;

            public Vector3 Forward => Vector3.back;

            public int DamageableId => GetInstanceID();

            public int ActorId => GetInstanceID();

            public Vector3 Position => transform.position;

            public bool IsActive => true;

            public bool IsDown => false;

            public float BaseThreat => 0f;

            public float AcquiredThreatMultiplier => 1f;

            public void ReceiveHit(in HitInfo hit)
            {
            }
        }

        /// <summary>移動入力を倒したまま物理を進める（通常移動の経路をそのまま通す）。</summary>
        private static IEnumerator DriveInput(StickInput input, Vector2 move, int steps)
        {
            input.Move = move;
            for (int i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            input.Move = Vector2.zero;
            yield return new WaitForFixedUpdate();
        }

        /// <summary>移動だけを倒せる主人公入力（通常移動の検査用）。</summary>
        private sealed class StickInput : IPlayerInput
        {
            public Vector2 Move { get; set; }

            public bool GuardHeld => false;

            public event System.Action GuardStarted;

            public event System.Action GuardCanceled;

            public bool Active => true;

            public bool SpecialAttackHeld => false;

            public bool ConsumeAttackPressed() => false;

            public bool ConsumeStepPressed() => false;

            /// <summary>未使用のイベントで警告が出ないようにするためだけの呼び出し口。</summary>
            public void RaiseGuardForCompiler()
            {
                GuardStarted?.Invoke();
                GuardCanceled?.Invoke();
            }
        }

        // ---------------------------------------------------------------- P09・P15・P18（本編型死亡再開）

        /// <summary>死亡させるためだけの攻撃者（実 Hitbox を持たないので、被弾の入口だけを実物で通す）。</summary>
        private sealed class LethalAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;

            public int FloorId => 0;

            public int ActorId => GetInstanceID();

            public Vector3 WorldPosition => transform.position;

            public Vector3 Forward => transform.forward;
        }

        /// <summary>
        /// 主人公を<b>実際の被弾経路</b>で死なせる（§15 の注記「処理結果を直接セットしない」）。
        /// 死亡確定の一度性・通知も本番の <c>ReceiveHit</c> が行う。
        /// </summary>
        private IEnumerator KillPlayerWithRealHit()
        {
            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.IsNotNull(vitals, "主人公の生存がある。");

            var attackerGo = new GameObject("P5LethalAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = vitals.transform.position + Vector3.forward;

            // <b>1 発で死ぬとは限らない。</b> 探索中は犬丸が「かばう」で肩代わりし、被弾後無敵も挟まる。
            // どちらも本番の防御経路なので、飛ばさずに<b>届くまで実際に殴り続ける</b>。
            var companion = Object.FindFirstObjectByType<CompanionHitReceiver>();
            int hits = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!vitals.IsDefeated && Time.realtimeSinceStartup < deadline)
            {
                hits++;
                vitals.ReceiveHit(new HitInfo(
                    attacker, vitals, Vector3.back, vitals.transform.position,
                    new HitDamage(9999, 0f, 0f), guardable: false, justGuardable: false,
                    hitId: HitId.Single(7700 + hits)));
                yield return null;
            }

            Object.Destroy(attackerGo);
            yield return null;

            Assert.IsTrue(vitals.IsDefeated,
                "前提：主人公が死んでいる。打った数=" + hits
                + " HP=" + vitals.Vitals.Health.Current
                + " 犬丸=" + (companion != null ? companion.Vitals.IsDown.ToString() : "(なし)")
                + " mode=" + (GameModeProvider.Current != null ? GameModeProvider.Current.Current.ToString() : "null"));
        }

        /// <summary>
        /// P5-P20：<b>死亡再開のロードが失敗したら、Error 表示ではなく再開画面へ戻る</b>
        /// （§9.1 末尾。GPT レビュー R6 の指摘 1）。
        ///
        /// 既存の E21 は Fake の遷移役と <c>NotifyTravelFailed</c> の直接呼び出しで段階だけを見ていたので、
        /// <b>実サービスと常駐 Session の接続</b>を一度も通していなかった。ここは実 <c>AreaTransitionService</c> に
        /// 失敗する Loader を差して、失敗が常駐の調停役まで届き、<b>同じ再開要求 ID のまま</b>
        /// 再試行できることを見る。
        ///
        /// 併せて、再開が<b>復旧（RecoverToOrigin）へ流れない</b>ことも見る。死亡再開は手順 6 のために
        /// 持ち越しを捨ててから出発するので、復旧へ流すと必ず終端失敗になり、
        /// 再試行できるはずの失敗が Error 表示になっていた。
        /// </summary>
        [UnityTest]
        public IEnumerator RespawnLoadFailure_ReturnsToRetryableRespawn_NotTerminalError()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            RemoveStrayTestDevices();
            _keyboard = InputSystem.AddDevice<Keyboard>("P5RespawnFailKeyboard");

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionState session = Sessions().Session;
            AreaTransitionService service = Transitions();
            var modes = GameModeProvider.Current;

            yield return KillPlayerWithRealHit();
            yield return WaitForRespawnPrompt();

            int requestId = session.Respawn.CurrentRequestId;
            Assert.Greater(requestId, 0, "前提：再開要求 ID が発行されている。");
            int cycleBefore = session.RespawnCycle;

            // ---- ロードを失敗させたまま再開する ----
            IAreaSceneLoader original = service.Loader;
            service.Loader = new FailingLoader();

            var runner = Object.FindFirstObjectByType<CampaignRespawnRunner>();
            Assert.IsNotNull(runner, "再開の実行役が Scene にある。");
            RespawnDecision accepted = runner.RequestRespawn();
            Assert.IsTrue(accepted.Accepted, "再開を受理する。拒否=" + accepted.Rejection);

            yield return WaitUntilOrTimeout(
                () => session.Respawn.Phase == CampaignRespawnPhase.Failed, 20f);

            Assert.AreEqual(CampaignRespawnPhase.Failed, session.Respawn.Phase,
                "ロード失敗は<b>常駐の調停役まで</b>届く（Scene の実行役を探しに行かない）。");
            Assert.AreEqual(requestId, session.Respawn.CurrentRequestId,
                "同じ死の続きなので再開要求 ID を振り直さない。");
            Assert.AreEqual(1, session.Respawn.AdvanceCount, "周期の更新は再開要求につき 1 回のまま。");
            Assert.AreEqual(cycleBefore + 1, session.RespawnCycle, "再試行で周期を二度進めない。");
            Assert.AreEqual(0, session.Respawn.CompletedCount, "着いていないので完了していない。");
            Assert.IsTrue(session.Respawn.IsAwaitingRespawn, "再開画面で待てる状態へ戻る。");
            Assert.AreEqual(GameMode.GameOver, modes.Current, "探索へは戻さない（主人公は死んだまま）。");

            // <b>ここが核心。</b> 復旧へ流していた頃はここが終端失敗になり、Error 表示になっていた。
            Assert.IsFalse(service.HasTerminalFailure,
                "再試行できる失敗を Error 表示にしない。理由=" + service.TerminalFailureReason);
            Assert.AreEqual(1, service.RespawnFailureCount, "死亡再開の失敗として 1 回数える。");

            // ---- ロードを戻して、同じ再開要求のまま再試行する ----
            service.Loader = original;

            RespawnDecision retry = runner.RequestRespawn();
            Assert.IsTrue(retry.Accepted, "再開画面から再試行できる。拒否=" + retry.Rejection);
            Assert.AreEqual(requestId, retry.RequestId, "同じ再開要求の続き。");

            yield return WaitUntilOrTimeout(
                () => session.Respawn.Phase == CampaignRespawnPhase.Idle, 20f);

            Assert.AreEqual(CampaignRespawnPhase.Idle, session.Respawn.Phase,
                "再試行では着いて完了する。段階=" + session.Respawn.Phase
                + " 遷移の拒否=" + runner.LastTravelRejection);
            Assert.AreEqual(1, session.Respawn.CompletedCount, "完了は 1 回だけ。");
            Assert.AreEqual(cycleBefore + 1, session.RespawnCycle,
                "再試行で再出現周期を二度進めない（§9.1 末尾）。");

            // 完了は<b>活動許可のあと</b>に確定している。
            yield return WaitUntilOrTimeout(() => modes.Current == GameMode.Exploration, 5f);
            Assert.AreEqual(GameMode.Exploration, modes.Current, "到着して探索へ戻る（手順 7）。");
            var arrivedContext = Object.FindFirstObjectByType<AreaContext>();
            Assert.IsNotNull(arrivedContext);
            Assert.IsTrue(arrivedContext.IsAreaReady, "活動が許可されている。");
        }

        /// <summary>
        /// P5-P21：<b>到着側の初期化が後段で失敗したら、死亡再開を完了扱いにしない</b>
        /// （§9.1 手順 7。GPT レビュー R6 の指摘 1）。
        ///
        /// 以前は Actor を全回復した直後に完了扱いにしていた。そのあとにも門の復元など
        /// 失敗しうる段が残っており、そこで落ちると段階はすでに <c>Idle</c> なので
        /// <b>失敗通知を受理できず、「再開する」の再表示・再試行が成立しなかった</b>。
        ///
        /// ここでは門の Collider の配線を外して 6a を確実に失敗させ、
        /// 段階が <c>Requested</c> のまま残る（＝所有者が失敗を確定できる）ことを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator ArrivalInitializationFailure_DoesNotCompleteRespawn()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            AreaInitializer initializer = FindInitializer();
            Assert.IsTrue(initializer.Initialized, "前提：ふつうに初期化できている。");

            GameSessionState session = Sessions().Session;

            // ---- 再開で到着した状況を作る（段階だけを Requested にする。遷移は走らせない） ----
            Assert.IsTrue(session.Respawn.NotifyPlayerDefeated(), "前提：死亡を受理できる。");
            RespawnDecision decision = session.Respawn.TryRequest();
            Assert.IsTrue(decision.Accepted, "前提：再開を受理できる。");
            Assert.AreEqual(CampaignRespawnPhase.Requested, session.Respawn.Phase);
            int requestId = decision.RequestId;

            // ---- 6a（門の復元）を確実に失敗させる ----
            var root = Object.FindFirstObjectByType<AreaRoot>();
            Assert.Greater(root.Doors.Count, 0, "前提：この Area に門がある。");
            AreaFlagDoor door = root.Doors[0];
            Assert.IsNotNull(door);

            AreaRuntimeState area = session.GetOrCreateArea(root.AreaId);
            Assert.IsTrue(area.TryOpen(door.FlagId), "前提：開通済みとして記録できる（復元が走る条件）。");
            SetPrivate(door, "_blocker", null); // 復元が必ず失敗する。

            // 初期化をやり直させる。
            typeof(AreaInitializer).GetProperty("Initialized")
                .GetSetMethod(true).Invoke(initializer, new object[] { false });

            // 失敗は Error ログとして出る（出ないほうが問題なので、握りつぶさず期待として書く）。
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("門の Collider が未配線です"));
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("Area initialization failed.*門を復元できませんでした"));

            bool ok = initializer.Initialize();

            Assert.IsFalse(ok, "門を復元できないので初期化は失敗する。");
            StringAssert.Contains("門", initializer.FailureReason, "理由は門の復元失敗。");

            // <b>ここが核心。</b> 修正前はここが Idle になっていて、失敗を受理できなかった。
            Assert.AreEqual(CampaignRespawnPhase.Requested, session.Respawn.Phase,
                "到着の途中で落ちたのだから、まだ完了ではない。");
            Assert.AreEqual(requestId, session.Respawn.CurrentRequestId, "要求 ID は生きている。");
            Assert.AreEqual(0, session.Respawn.CompletedCount, "完了として数えない。");

            // 所有者はこの段階から失敗を確定でき、同じ要求 ID で再試行できる。
            Assert.IsTrue(session.Respawn.NotifyFailed(requestId), "失敗通知を受理できる。");
            Assert.AreEqual(CampaignRespawnPhase.Failed, session.Respawn.Phase);
            Assert.IsTrue(session.Respawn.IsAwaitingRespawn, "「再開する」を出し直せる。");
            Assert.AreEqual(requestId, session.Respawn.TryRequest().RequestId, "同じ再開要求のまま再試行できる。");
        }

        /// <summary>
        /// P5-P22：<b>死亡再開が復旧（RecoverToOrigin）へ流れない</b>（§9.1 末尾。GPT レビュー R6 の指摘 1）。
        ///
        /// 到着側の準備が完了しないときの既定の立て直しは「運んでいた Actor 値を持って元 Area へ戻る」。
        /// ところが死亡再開は手順 6 のために<b>持ち越しを捨ててから</b>出発するので、
        /// 復旧は必ず「復旧元のエリア・入口を解決できませんでした」で終端し、
        /// <b>再試行できるはずの失敗が Error 表示</b>になっていた。
        /// そもそも死亡再開の「元の場所」は死んだ場所で、戻る先として正しくない。
        ///
        /// ここでは読込が成功したことにして何も読まない Loader を差し、
        /// 到着の準備が来ないまま監視が切れる状況を実時間で作る。
        /// </summary>
        [UnityTest]
        public IEnumerator RespawnArrivalNeverPrepares_ReturnsToRespawnScreen_NotRecovery()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionState session = Sessions().Session;
            AreaTransitionService service = Transitions();
            var modes = GameModeProvider.Current;

            yield return KillPlayerWithRealHit();
            yield return WaitForRespawnPrompt();

            int requestId = session.Respawn.CurrentRequestId;
            int cycleBefore = session.RespawnCycle;
            int recoveredBefore = service.RecoveredCount;

            IAreaSceneLoader original = service.Loader;
            float originalBind = service.BindTimeoutSeconds;
            service.Loader = new SilentLoader();
            service.BindTimeoutSeconds = 1.5f; // 失敗経路を実時間で通す。

            var runner = Object.FindFirstObjectByType<CampaignRespawnRunner>();
            Assert.IsTrue(runner.RequestRespawn().Accepted, "前提：再開を受理する。");

            yield return WaitUntilOrTimeout(
                () => session.Respawn.Phase == CampaignRespawnPhase.Failed, 20f);

            service.Loader = original;
            service.BindTimeoutSeconds = originalBind;

            Assert.AreEqual(CampaignRespawnPhase.Failed, session.Respawn.Phase,
                "到着の準備が来なければ、再開画面へ戻して再試行できるようにする。");
            Assert.AreEqual(requestId, session.Respawn.CurrentRequestId, "要求 ID は振り直さない。");
            Assert.AreEqual(cycleBefore + 1, session.RespawnCycle, "周期は二度進めない。");
            Assert.AreEqual(0, session.Respawn.CompletedCount, "着いていないので完了していない。");
            Assert.AreEqual(GameMode.GameOver, modes.Current, "探索へは戻さない。");

            // <b>ここが核心。</b> 復旧へ流していた頃は必ず終端失敗になり、Error 表示になっていた。
            Assert.IsFalse(service.HasTerminalFailure,
                "再試行できる失敗を Error 表示にしない。理由=" + service.TerminalFailureReason);
            Assert.AreEqual(recoveredBefore, service.RecoveredCount, "復旧ロードは走らせない。");
            Assert.AreEqual(1, service.RespawnFailureCount, "死亡再開の失敗として数える。");
            Assert.IsTrue(session.Respawn.IsAwaitingRespawn, "「再開する」を出し直せる。");
        }

        /// <summary>
        /// P5-P23：<b>到着時の向きは主人公にも適用する</b>（§4.4「向きは Data の入口定義が正本」）。
        ///
        /// 以前は犬丸だけに適用しており、主人公は到着直後だけ入口定義と無関係な向き
        /// （Prefab の初期値＝下）のままだった。B の入口 <c>area_p5_b_from_a</c> は East 定義なので、
        /// 到着直後の主人公は<b>右</b>を向いていなければならない。
        /// 入力は握らずに検査する（押していれば次のフレームから入力が勝つのが正しい）。
        /// </summary>
        [UnityTest]
        public IEnumerator ArrivalFacing_AppliesEntryDefinitionToThePlayerToo()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            var before = Object.FindFirstObjectByType<PlayerFacing>();
            Assert.IsNotNull(before, "主人公の向きを持つ部品がある。");
            // A の既定入口 area_p5_a_start は North 定義。直開きでも入口定義が適用される。
            Assert.AreEqual(FacingDirection.Up, before.Current,
                "直開きでも既定入口の向き（North）が主人公に適用される（§5.2／§4.4）。");

            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;
            int completedBefore = coordinator.CompletedCount;

            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted, "B へ移動を受理する。");
            yield return WaitForCompleted(coordinator, completedBefore + 1);

            Assert.AreEqual(AreaB.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value, "B へ着く。");

            var arrived = Object.FindFirstObjectByType<PlayerFacing>();
            Assert.IsNotNull(arrived);
            Assert.AreEqual(FacingDirection.Right, arrived.Current,
                "入口 area_p5_b_from_a は East 定義なので、到着直後の主人公は右を向く（§4.4）。");
        }

        /// <summary>
        /// P5-P24：<b>到着先 Scene の初期化が失敗しても、実キーの再開操作で再ロードまで通る</b>
        /// （§9.1 末尾。GPT レビュー R7 の指摘 1）。
        ///
        /// 既存の 2 本はどちらもこの経路を通っていなかった。
        /// <c>ArrivalInitializationFailure_DoesNotCompleteRespawn</c> は初期化済み Scene を
        /// 再初期化する検査で、<c>RespawnArrivalNeverPrepares_...</c> は旧 Scene を残す。
        /// <b>どちらも「新しい Scene の未配線状態」を作れない。</b>
        ///
        /// ここでは旧 Scene を実際に破棄させ、新しく読まれた Scene の門を
        /// <c>sceneLoaded</c>（<c>Start</c> より前）で壊して初期化を失敗させる。
        /// その状態から<b>実キーの再開操作</b>で再ロードが完了することを見る。
        /// 修正前は、新しい Scene の実行役が未配線のまま（配線は門の復元より後だった）で、
        /// 段階は「再試行待ち」でも <c>RequestRespawn</c> が NotWired で断っていた。
        /// </summary>
        [UnityTest]
        public IEnumerator ArrivalSceneFailsToInitialize_RespawnInputStillRetriesToCompletion()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            RemoveStrayTestDevices();
            _keyboard = InputSystem.AddDevice<Keyboard>("P5RetryKeyboard");

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionState session = Sessions().Session;
            var modes = GameModeProvider.Current;

            // 開通済みの門を記録しておく（到着側で復元が走る条件）。
            var rootBefore = Object.FindFirstObjectByType<AreaRoot>();
            Assert.Greater(rootBefore.Doors.Count, 0, "前提：A に門がある。");
            StableId gateFlag = rootBefore.Doors[0].FlagId;
            AreaRuntimeState areaA = session.GetOrCreateArea(AreaA);
            Assert.IsTrue(areaA.TryOpen(gateFlag), "前提：開通済みとして記録できる。");

            yield return KillPlayerWithRealHit();
            yield return WaitForRespawnPrompt();

            int requestId = session.Respawn.CurrentRequestId;
            Assert.Greater(requestId, 0, "前提：再開要求 ID が発行されている。");
            int cycleBefore = session.RespawnCycle;

            // ---- 次に読まれる Scene の門を、Start より前に壊す ----
            bool broken = false;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> breakNext = (scene, mode) =>
            {
                if (broken)
                {
                    return;
                }

                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    var door = go.GetComponentInChildren<AreaFlagDoor>(true);
                    if (door != null)
                    {
                        SetPrivate(door, "_blocker", null); // 復元が必ず失敗する。
                        broken = true;
                        break;
                    }
                }
            };
            SceneManager.sceneLoaded += breakNext;

            // <b>後始末は try/finally で保証する</b>（GPT レビュー R8 の指摘 3）。
            // 途中の Assert で抜けると、Scene を壊すハンドラが残って後続テストの Scene を壊し、
            // ログ無視も残って後続テストが Error を見逃す。
            bool ignoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // 壊した Scene が出す Error は想定内。
            try
            {
                var runner = Object.FindFirstObjectByType<CampaignRespawnRunner>();
                Assert.IsTrue(runner.RequestRespawn().Accepted, "前提：再開を受理する。");

                // 旧 Scene は破棄され、新しい Scene の初期化が失敗する。
                yield return WaitUntilOrTimeout(
                    () => session.Respawn.Phase == CampaignRespawnPhase.Failed, 30f);

                SceneManager.sceneLoaded -= breakNext; // 次の読込は壊さない。

                Assert.IsTrue(broken, "前提：新しく読まれた Scene の門を壊せた。");
                Assert.AreEqual(CampaignRespawnPhase.Failed, session.Respawn.Phase,
                    "到着初期化に失敗したら、再試行待ちへ戻す。");
                Assert.IsFalse(FindInitializer().Initialized, "前提：到着側の初期化は失敗している。");
                Assert.AreEqual(requestId, session.Respawn.CurrentRequestId, "要求 ID は振り直さない。");
                Assert.AreEqual(0, session.Respawn.CompletedCount, "完了として数えない。");

                // <b>ここが核心。</b> 実行役が未配線だと、ここから先へ進めなかった。
                var brokenRunner = Object.FindFirstObjectByType<CampaignRespawnRunner>();
                Assert.IsNotNull(brokenRunner, "壊れた Scene にも実行役は居る。");
                Assert.IsTrue(brokenRunner.IsAwaitingRespawn, "再試行待ちとして見える。");

                // ---- 実キーの再開操作で再ロードまで通す ----
                yield return PressKeyUntil(Key.Enter,
                    () => session.Respawn.Phase == CampaignRespawnPhase.Idle, 30f);
            }
            finally
            {
                SceneManager.sceneLoaded -= breakNext;
                LogAssert.ignoreFailingMessages = ignoring;
            }

            Assert.AreEqual(CampaignRespawnPhase.Idle, session.Respawn.Phase,
                "再開操作で再ロードが完了する。拒否="
                + Object.FindFirstObjectByType<CampaignRespawnRunner>()?.LastRejection
                + " 遷移の拒否=" + Object.FindFirstObjectByType<CampaignRespawnRunner>()?.LastTravelRejection);
            Assert.AreEqual(1, session.Respawn.CompletedCount, "完了は 1 回だけ。");
            Assert.AreEqual(1, session.Respawn.AdvanceCount, "周期の更新は再開要求につき 1 回。");
            Assert.AreEqual(cycleBefore + 1, session.RespawnCycle,
                "再試行で再出現周期を二度進めない（§9.1 末尾）。");

            AreaInitializer arrived = FindInitializer();
            Assert.IsTrue(arrived.Initialized, "やり直した到着は成功している。");
            Assert.AreEqual(AreaA.Value, arrived.AreaId.Value, "再開地点は A。");
            yield return WaitUntilOrTimeout(() => modes.Current == GameMode.Exploration, 5f);
            Assert.AreEqual(GameMode.Exploration, modes.Current, "探索へ戻る（手順 7）。");

            Assert.IsTrue(session.TryGetArea(AreaA, out AreaRuntimeState afterArea));
            Assert.IsTrue(afterArea.IsOpen(gateFlag), "門の開通は残る。");
        }

        /// <summary>
        /// P5-P25：<b>門を開けたら、その先まで実際に渡れる</b>（§10.2／§13.3 の 7 行目。
        /// GPT レビュー R7 の指摘 2）。
        ///
        /// Scene の静的検査は、閉じた門の先を「門の両肩がそれぞれ到達できる」までしか見られない。
        /// <b>門の手前と向こうを結ぶ区間は見ていない</b>ので、門の近くに別の壁があったり
        /// NavMesh が切れていたりしても、その 2 区間は成立してしまう。
        /// くり抜き（NavMeshObstacle）の解除にはフレームが要るため、静的検査では埋められない。
        /// ここが受入条件の側。
        ///
        /// 門を開け、くり抜きが消えるのを待ってから、<b>既定入口から門の先の出入口まで
        /// 一本の経路が繋がる</b>ことを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator GateOpened_MakesTheGatedRouteTraversable()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            var root = Object.FindFirstObjectByType<AreaRoot>();
            Assert.AreEqual(1, root.ExitGates.Count, "前提：A には B への出入口が 1 つある。");
            Assert.Greater(root.Doors.Count, 0, "前提：A に門がある。");

            AreaEntryPoint start = null;
            foreach (AreaEntryPoint p in root.EntryPoints)
            {
                if (p != null && p.EntryId.Equals(AreaAStart))
                {
                    start = p;
                }
            }

            Assert.IsNotNull(start, "前提：既定入口がある。");
            Vector3 from = start.ArrivalPosition;
            Vector3 to = root.ExitGates[0].transform.position;

            // ---- 閉じている間は渡れない（前提の確認。ここが通ると検査の意味が無い） ----
            Assert.IsFalse(IsNavPathComplete(from, to),
                "前提：門が閉じている間は経路が繋がっていない（くり抜きが効いている）。");

            // ---- 門を開ける ----
            AreaFlagDoor gate = root.Doors[0];
            Assert.IsTrue(gate.TryApplyOpened(out string error), "門を開けられる。" + error);
            Assert.IsTrue(gate.IsOpened);

            // くり抜きの解除は即時ではない（NavMesh の更新にフレームが要る）。
            yield return WaitUntilOrTimeout(() => IsNavPathComplete(from, to), 10f);

            // <b>ここが核心。</b> 両肩に到達できることと、開けて渡れることは別。
            Assert.IsTrue(IsNavPathComplete(from, to),
                "門を開けたら、既定入口から門の先の出入口まで経路が繋がる（§10.2 末尾）。");

            // 扉・出現点など、門の先にある他の地点も同様に繋がる。
            foreach (AreaEntryPoint p in root.EntryPoints)
            {
                if (p != null)
                {
                    Assert.IsTrue(IsNavPathComplete(from, p.ArrivalPosition),
                        "門を開けたら入口 " + p.EntryId.Value + " まで繋がる。");
                }
            }
        }

        /// <summary>NavMesh 上で 2 点が繋がっているか（両端を NavMesh へ寄せてから引く）。</summary>
        private static bool IsNavPathComplete(Vector3 from, Vector3 to)
        {
            const float sampleRadius = 1.5f;
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, sampleRadius, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(to, out NavMeshHit b, sampleRadius, NavMesh.AllAreas))
            {
                return false;
            }

            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>
        /// P5-P26：<b>到着先の実行役が使えなくなっても、常駐側が表示と再試行受付を肩代わりする</b>
        /// （§9.1 末尾。GPT レビュー R8 の指摘 1）。
        ///
        /// 遷移役を常駐で公開しても、それを<b>呼ぶ側</b>（Scene の <c>RespawnSubmitInput</c>／
        /// <c>CampaignRespawnView</c>）はどちらも Scene の実行役を前提にしている。
        /// 死亡再開では旧 Scene を破棄してから読むので、到着先で実行役が欠落・利用不能になると
        /// Session は Failed でも表示も操作経路も無くなる。しかも死亡再開の失敗は終端失敗にしないので
        /// Error 表示も出ない——<b>何も出ないまま操作不能</b>になっていた。
        ///
        /// ここでは新しく読まれた Scene の実行役を <c>sceneLoaded</c>（<c>Start</c> より前）で
        /// 破棄し、常駐の受付が出ること・実入力で<b>同じ要求 ID のまま</b>再試行できることを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator ArrivalRunnerUnavailable_ResidentHostShowsAndRetriesRespawn()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            RemoveStrayTestDevices();
            _keyboard = InputSystem.AddDevice<Keyboard>("P5ResidentKeyboard");

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionState session = Sessions().Session;
            var resident = Object.FindFirstObjectByType<CampaignRespawnResidentView>();
            Assert.IsNotNull(resident, "常駐の肩代わり表示が立ち上がっている。");
            Assert.IsFalse(resident.ShouldTakeOver, "Scene 側が生きているうちは肩代わりしない。");

            yield return KillPlayerWithRealHit();
            yield return WaitForRespawnPrompt();

            Assert.IsFalse(resident.ShouldTakeOver,
                "死亡直後も Scene 側の受付が生きているので、常駐は何も出さない（二重処理しない）。");
            Assert.AreEqual(string.Empty, resident.Message);

            int requestId = session.Respawn.CurrentRequestId;
            int cycleBefore = session.RespawnCycle;

            // ---- 次に読まれる Scene の実行役を消し、初期化も失敗させる（Start より前） ----
            //
            // 実行役を消すだけでは到着が成功してしまい、再開は完了して終わる。
            // 「到着に失敗し、かつ実行役も居ない」——常駐が肩代わりするしかない状態を作る。
            var rootBefore = Object.FindFirstObjectByType<AreaRoot>();
            Assert.Greater(rootBefore.Doors.Count, 0, "前提：A に門がある。");
            StableId gateFlag = rootBefore.Doors[0].FlagId;
            Assert.IsTrue(session.GetOrCreateArea(AreaA).TryOpen(gateFlag),
                "前提：開通済みとして記録できる（到着側で復元が走る条件）。");

            bool removed = false;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> killRunner = (scene, mode) =>
            {
                if (removed)
                {
                    return;
                }

                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    var runner = go.GetComponentInChildren<CampaignRespawnRunner>(true);
                    var door = go.GetComponentInChildren<AreaFlagDoor>(true);
                    if (door != null)
                    {
                        SetPrivate(door, "_blocker", null); // 到着初期化を失敗させる。
                    }

                    if (runner != null)
                    {
                        Object.DestroyImmediate(runner); // 再試行の受け口を奪う。
                        removed = true;
                    }
                }
            };

            SceneManager.sceneLoaded += killRunner;
            bool ignoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var sceneRunner = Object.FindFirstObjectByType<CampaignRespawnRunner>();
                Assert.IsTrue(sceneRunner.RequestRespawn().Accepted, "前提：再開を受理する。");

                // 旧 Scene は破棄され、新しい Scene は初期化に失敗し、実行役も居ない。
                yield return WaitUntilOrTimeout(
                    () => session.Respawn.Phase == CampaignRespawnPhase.Failed, 30f);

                Assert.IsTrue(removed, "前提：新しく読まれた Scene の実行役を消せた。");
                Assert.IsNull(Object.FindFirstObjectByType<CampaignRespawnRunner>(),
                    "前提：到着先に実行役が居ない。");
                Assert.AreEqual(CampaignRespawnPhase.Failed, session.Respawn.Phase,
                    "到着初期化に失敗したので再試行待ちへ戻る。");
                Assert.AreEqual(requestId, session.Respawn.CurrentRequestId, "要求 ID は振り直さない。");

                // <b>ここが核心。</b> 呼ぶ側が居ないので、以前はここで何も出なかった。
                Assert.IsTrue(resident.ShouldTakeOver, "Scene 側が使えないので常駐が肩代わりする。");
                Assert.IsTrue(resident.IsShowing, "常駐側が再開の短文を出す。");
                Assert.AreEqual(CampaignRespawnLabels.Retry, resident.Message,
                    "読込失敗からの再試行なので「もう一度」の短文。");

                // 次の読込は壊さない。
                SceneManager.sceneLoaded -= killRunner;

                // ---- 実キーの再開操作で、常駐の受付から再ロードまで通す ----
                yield return PressKeyUntil(Key.Enter,
                    () => session.Respawn.Phase == CampaignRespawnPhase.Idle, 30f);
            }
            finally
            {
                SceneManager.sceneLoaded -= killRunner;
                LogAssert.ignoreFailingMessages = ignoring;
            }

            Assert.AreEqual(CampaignRespawnPhase.Idle, session.Respawn.Phase,
                "常駐の受付で再ロードが完了する。肩代わりの受理="
                + resident.TakeoverSubmitCount + " 判定=" + resident.LastDecision.Rejection);
            Assert.GreaterOrEqual(resident.TakeoverSubmitCount, 1, "常駐側が押下を受け取っている。");
            Assert.AreEqual(1, session.Respawn.CompletedCount, "完了は 1 回だけ。");
            Assert.AreEqual(1, session.Respawn.AdvanceCount, "周期の更新は再開要求につき 1 回。");
            Assert.AreEqual(cycleBefore + 1, session.RespawnCycle, "再試行で周期を二度進めない。");

            AreaInitializer arrived = FindInitializer();
            Assert.IsTrue(arrived.Initialized, "やり直した到着は成功している。");
            Assert.AreEqual(AreaA.Value, arrived.AreaId.Value, "再開地点は A。");
            Assert.IsFalse(resident.ShouldTakeOver, "Scene 側が戻ったので常駐は退く。");
        }

        /// <summary>死亡が受理され、再開画面が出るまで待つ。</summary>
        private IEnumerator WaitForRespawnPrompt()
        {
            var view = Object.FindFirstObjectByType<CampaignRespawnView>();
            Assert.IsNotNull(view, "再開操作の表示が Scene にある（§11 の必須 UI）。");
            Assert.IsTrue(view.IsWired, "再開操作の表示が配線されている。");

            yield return WaitUntilOrTimeout(() => view.IsShowing, 5f);

            Assert.IsTrue(view.IsShowing,
                "死亡したら再開操作が出る（値を持つだけでは表示したことにならない）。");
            Assert.AreEqual(CampaignRespawnView.RespawnLabel, view.Message,
                "ラベルは「再開する」（既存試遊の Retry とは別物。§9.1 の 1 行目）。");
            Assert.AreEqual(GameMode.GameOver, GameModeProvider.Current.Current, "GameOver になる（手順 1）。");
        }

        /// <summary>
        /// P5-P09：<b>撃破で得た徳は死んでも失われず、通常敵だけが再出現する</b>
        /// （§8.5／§9.1 手順 5〜7）。近接 1 体で 10 → 死亡再開しても 10 → 再戦の全滅で 32。
        ///
        /// ここが崩れると試遊の意味が変わる。徳が消える再開は「やり直し」であって本編型ではないし、
        /// 逆に通常敵が湧き直さなければ、死んだあとに戦う相手がいなくなる。
        /// </summary>
        [UnityTest]
        public IEnumerator DeathAfterOneKill_PreservesVirtueAndReopensNormalEncounter()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaBScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");

            IGameModeService modes = GameModeProvider.Current;
            GameSessionState session = Sessions().Session;
            Assert.AreEqual(0, session.Progress.Virtue, "前提：まだ何も得ていない。");

            // A 側の恒久進行を前提として置く（開通・調査の<b>記録経路</b>そのものは P10／FullRoute が実物で見ている。
            // ここで見たいのは「死んでも残るか」なので、記録の出どころは問わない）。
            AreaRuntimeState areaA = session.GetOrCreateArea(AreaA);
            var gateFlag = new StableId("flag_p5_a_gate");
            var investigated = new StableId("point_p5_a_trial");
            Assert.IsTrue(areaA.TryOpen(gateFlag), "前提：門の開通を記録できる。");
            Assert.IsTrue(areaA.Investigation.TryMarkInvestigated(investigated), "前提：調査済みを記録できる。");

            // ================================ 1. 近接 1 体を実 Hitbox で倒す ================================
            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runner);
            yield return MovePlayerTo(EncounterTriggerPoint);
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 5f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                "前提：戦闘が始まる。拒否=" + runner.LastRejection + " " + runner.LastFailureDetail);

            EnemyActor[] enemies = Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);
            Assert.AreEqual(2, enemies.Length, "前提：骸骨剣士 1 ＋ 骸骨弓兵 1。");
            EnemyActor melee = FindMelee(enemies);
            Assert.IsNotNull(melee, "近接の敵が居る。名前=" + DescribeEnemies(enemies));

            yield return KillWithRealHitbox(melee);
            yield return WaitUntilOrTimeout(() => session.Progress.Virtue >= 10, 5f);
            Assert.AreEqual(10, session.Progress.Virtue, "近接 1 体の撃破で 10（§8.5）。");
            Assert.AreEqual(AreaEncounterState.Playing, runner.State, "前提：まだ戦闘中（1 体残っている）。");

            // ================================ 2. 死ぬ ================================
            yield return KillPlayerWithRealHit();
            yield return WaitForRespawnPrompt();

            Assert.AreEqual(10, session.Progress.Virtue, "撃破した瞬間の徳は、その戦闘で死んでも取り消さない（§8.5）。");
            Assert.IsTrue(session.TryGetArea(AreaB, out AreaRuntimeState areaB));
            Assert.IsFalse(areaB.IsEncounterCleared(new StableId("encounter_p5_b_road"), session.RespawnCycle),
                "前提：勝っていないのでクリア記録は付いていない。");

            int cycleBefore = session.RespawnCycle;
            AreaTransitionCoordinator transitions = Transitions().Coordinator;
            int completedBefore = transitions.CompletedCount;

            // ================================ 3. 実キーで再開する ================================
            yield return PressKey(Key.Enter);
            yield return WaitUntilOrTimeout(() => transitions.CompletedCount > completedBefore, 20f);
            yield return ReleaseKeys();

            Assert.AreEqual(completedBefore + 1, transitions.CompletedCount,
                "実キーの Submit で再開のロードが完了する。段階=" + session.Respawn.Phase
                + " 再開の拒否=" + Object.FindFirstObjectByType<CampaignRespawnRunner>()?.LastRejection
                + " 遷移の拒否=" + Object.FindFirstObjectByType<CampaignRespawnRunner>()?.LastTravelRejection);
            yield return WaitUntilOrTimeout(() => modes.Current == GameMode.Exploration, 5f);
            Assert.AreEqual(GameMode.Exploration, modes.Current, "到着して探索へ戻る（手順 7）。");

            AreaInitializer arrived = FindInitializer();
            Assert.IsTrue(arrived.Initialized);
            Assert.AreEqual(AreaA.Value, arrived.AreaId.Value, "再開地点は A（§3.1）。");
            Assert.AreEqual(cycleBefore + 1, session.RespawnCycle, "再出現周期が 1 進む（手順 5）。");
            Assert.AreEqual(CampaignRespawnPhase.Idle, session.Respawn.Phase, "到着で再開は終わる。");
            Assert.AreEqual(1, session.Respawn.AdvanceCount, "周期の更新は再開要求につき 1 回。");

            // ---- 進行 State は残り、Actor だけが初期化される ----
            Assert.AreEqual(10, session.Progress.Virtue, "死亡再開でも既得の徳は 10 のまま。");
            Assert.IsTrue(areaA.IsOpen(gateFlag), "門の開通は保持する。");
            Assert.IsTrue(areaA.Investigation.IsInvestigated(investigated), "調査済みは保持する。");

            var vitalsAfter = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.IsNotNull(vitalsAfter);
            Assert.IsFalse(vitalsAfter.IsDefeated, "主人公は生き返っている。");
            Assert.AreEqual(vitalsAfter.Vitals.Health.Max, vitalsAfter.Vitals.Health.Current, "全回復して再開する。");

            var companion = Object.FindFirstObjectByType<CompanionHitReceiver>();
            Assert.IsNotNull(companion, "加入済みの犬丸が居る。");
            Assert.IsFalse(companion.Vitals.IsDown, "犬丸も復帰している。");

            // ================================ 4. B へ戻ると通常敵が再出現する ================================
            var playerActor = Object.FindFirstObjectByType<PlayerStateController>();
            yield return WaitUntilOrTimeout(() => playerActor == null || playerActor.IsFreeToTravel, 3f);

            AreaTransitionService service = Transitions();
            AreaTransitionDecision toB = service.TryTravel(AreaB, AreaBFromA);
            Assert.IsTrue(toB.Accepted, "探索へ戻っているので移動できる。理由=" + toB.Rejection);
            yield return WaitForArrival(transitions, completedBefore + 2);

            var runnerAgain = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runnerAgain);
            Assert.AreEqual(AreaEncounterState.Dormant, runnerAgain.State,
                "死亡再開でクリア記録が消えているので、また戦える（§9.1 手順 5）。");

            yield return MovePlayerTo(EncounterTriggerPoint);
            yield return WaitUntilOrTimeout(() => runnerAgain.State == AreaEncounterState.Playing, 5f);
            Assert.AreEqual(AreaEncounterState.Playing, runnerAgain.State,
                "通常敵が再出現する。拒否=" + runnerAgain.LastRejection + " " + runnerAgain.LastFailureDetail);

            EnemyActor[] again = Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);
            Assert.AreEqual(2, again.Length, "再戦でも 2 体。");

            for (int i = 0; i < again.Length; i++)
            {
                yield return KillWithRealHitbox(again[i]);
            }

            yield return WaitUntilOrTimeout(() => runnerAgain.State == AreaEncounterState.Cleared, 8f);
            Assert.AreEqual(AreaEncounterState.Cleared, runnerAgain.State, "再戦に勝つ。");
            Assert.AreEqual(32, session.Progress.Virtue,
                "10 ＋ 再戦の 22 ＝ 32（GrantOnce=false は再撃破で再付与する。§8.5）。");

            yield return ReleaseKeys();
        }

        /// <summary>敵の並びを読める文にする（失敗時の手掛かり）。</summary>
        private static string DescribeEnemies(EnemyActor[] enemies)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < enemies.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(" / ");
                }

                text.Append(enemies[i] != null ? enemies[i].name : "(null)");
            }

            return text.Length == 0 ? "(なし)" : text.ToString();
        }

        /// <summary>近接の敵を選ぶ（弓兵でない方）。</summary>
        private static EnemyActor FindMelee(EnemyActor[] enemies)
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null && enemies[i].name.IndexOf("Ranged", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return enemies[i];
                }
            }

            return null;
        }


        /// <summary>
        /// P5-P15：進行の<b>持ち場</b>を取り違えない（§4.2／§5.2／§9.2）。
        /// P5 の新規開始は 0 から、P5 の往復では保持、既存試遊は最初からローカルで、その Retry も 0 に戻る。
        ///
        /// 混ざると被害が大きい方向に非対称になる。試遊の Retry が本編の徳を消したら取り返せないし、
        /// 逆に試遊が本編の徳を増やせたら受入の数字が信用できなくなる。
        /// </summary>
        [UnityTest]
        public IEnumerator NewGameAndLegacyTrial_UseCorrectProgressScope()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(LegacyTrialScene),
                "既存試遊 Scene が Build Settings に登録されている: " + LegacyTrialScene);

            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            GameSessionBootService sessions = Sessions();
            GameSessionState first = sessions.Session;
            Assert.IsNotNull(first);
            Assert.AreEqual(1, sessions.CreatedCount, "本編型 Area の初期化で Session が 1 つできる（§5.2）。");
            Assert.AreEqual(0, first.Progress.Virtue, "P5 の新規開始は 0 から。");

            // 既得の進行を置く（記録の出どころは P09／FullRoute が実物で見ている）。
            first.Progress.TryGrant(new RewardSnapshot(new StableId("reward_p5_scope_case"), 21, default, true), out _);
            Assert.AreEqual(21, first.Progress.Virtue, "前提：徳を得た。");

            // ---- 本編の進行は Scene 側から初期化できない（§4.2 の「共有 State を Scene からリセットさせない」）----
            var boundHolder = Object.FindFirstObjectByType<PlayerProgressHolder>();
            Assert.IsNotNull(boundHolder, "A に進行の窓口がある。");
            Assert.IsTrue(boundHolder.IsBound, "P5 の窓口は共有 State に束ねられている。");
            Assert.IsFalse(boundHolder.ResetProgress(), "束ねられた進行を Scene 側から初期化できない。");
            Assert.AreEqual(21, first.Progress.Virtue, "拒否したのだから値も動かない。");

            // ================================ 1. P5 の往復では保持する ================================
            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator transitions = service.Coordinator;
            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);
            yield return WaitForArrival(transitions, 1);
            Assert.IsTrue(service.TryTravel(AreaA, AreaAFromB).Accepted);
            yield return WaitForArrival(transitions, 2);

            Assert.AreSame(first, sessions.Session, "往復で Session を作り直さない（§5.2）。");
            Assert.AreEqual(1, sessions.CreatedCount);
            Assert.AreEqual(21, first.Progress.Virtue, "A↔B の往復で徳は保持される（§4.1 の表）。");

            // ================================ 2. 明示的な New Game だけが 0 へ戻す ================================
            GameSessionState fresh = sessions.StartNewSession();
            Assert.AreNotSame(first, fresh, "New Game は新しい Session を作る（§9.2）。");
            Assert.AreEqual(2, sessions.CreatedCount);
            Assert.AreEqual(0, fresh.Progress.Virtue, "徳が初期値へ戻る。");
            Assert.AreEqual(0, fresh.VisitedAreaCount, "訪問済みも戻る。");
            Assert.AreEqual(0, fresh.RecruitedCount, "加入設定も戻る。");
            Assert.AreEqual(0, fresh.RespawnCycle, "再出現周期も戻る。");
            Assert.AreSame(fresh, GameSessionProvider.Current, "提供点も差し替わる。");

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            Assert.AreSame(fresh, sessions.Session, "New Game のあとの Area 初期化は新しい Session を使い回す。");
            Assert.AreEqual(2, sessions.CreatedCount, "Scene を読み直しただけで Session は増えない。");
            Assert.AreEqual(0, fresh.Progress.Virtue, "新規開始の続きなので 0 のまま。");

            // P5 の Scene には既存試遊の Retry 経路を置かない（§9.1 の 1 行目）。
            Assert.IsNull(Object.FindFirstObjectByType<CombatRetryInput>(),
                "P5 Scene に既存試遊の Retry 入力は居ない。");
            Assert.IsNotNull(Object.FindFirstObjectByType<CampaignRespawnView>(),
                "代わりに本編型の再開操作が居る。");

            // ================================ 3. 既存試遊はローカル。Retry も 0 へ ================================
            yield return SceneManager.LoadSceneAsync(LegacyTrialScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var trialHolder = Object.FindFirstObjectByType<PlayerProgressHolder>();
            Assert.IsNotNull(trialHolder, "既存試遊にも進行の窓口がある。");
            Assert.IsFalse(trialHolder.IsBound,
                "既存試遊は共有 State に束ねない（§5.2。試遊 Scene へ本編 Session を自動注入しない）。");

            trialHolder.Grant(new RewardSnapshot(new StableId("reward_legacy_case"), 33, default, false), out _);
            Assert.AreEqual(33, trialHolder.Virtue, "試遊の徳はローカルに積まれる。");
            Assert.AreEqual(0, fresh.Progress.Virtue, "試遊で本編の徳は増えない。");

            Assert.IsTrue(trialHolder.ResetProgress(), "束ねていない進行は Scene 側から初期化できる（試遊の Retry）。");
            Assert.AreEqual(0, trialHolder.Virtue, "試遊の Retry はローカルを 0 に戻す。");
            Assert.AreEqual(0, fresh.Progress.Virtue, "本編側は触られない。");
            Assert.AreEqual(2, sessions.CreatedCount, "試遊へ切り替えても Session は作られない。");

            // 既存試遊の Scene を<b>自分で畳む</b>。読み込んだまま終わると、その Scene の常駐物と
            // Action Map の状態が次のテストへ残る（実際に踏んだ：実キーのテストが一斉に無反応になった）。
            yield return SceneManager.LoadSceneAsync(TrialScene, LoadSceneMode.Single);
            yield return null;
            DestroyTrialLaunchers();
            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }
        }

        /// <summary>
        /// P5-P18：再開に使った物理ボタンは、<b>一度離すまで</b>到着先の操作にならない（§9.1 末尾）。
        ///
        /// ラッチを 1 回消すだけでは足りない。Action Map を閉じてから開き直すと、押しっぱなしの
        /// ボタンが「新しい押下」として立ち上がる。キーボードとゲームパッドの両方で見る。
        /// </summary>
        [UnityTest]
        public IEnumerator RespawnSubmit_DoesNotBecomeArrivalInteract()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");
            _gamepad = InputSystem.AddDevice<Gamepad>("P5Gamepad");

            IGameModeService modes = GameModeProvider.Current;
            AreaTransitionCoordinator transitions = Transitions().Coordinator;

            // ================================ 1. ゲームパッド South を押しっぱなしで再開 ================================
            //
            // South は UI/Submit と Gameplay/Interact の<b>同じ物理ボタン</b>（IA_Momotaro）。
            // ここが P18 の本番で、押しっぱなしのまま到着すると Interact が 1 回走ってしまう。
            yield return KillPlayerWithRealHit();
            yield return WaitForRespawnPrompt();

            int completed = transitions.CompletedCount;
            yield return PressGamepadSouth();
            yield return WaitUntilOrTimeout(() => transitions.CompletedCount > completed, 20f);
            yield return WaitUntilOrTimeout(() => modes.Current == GameMode.Exploration, 5f);
            Assert.AreEqual(completed + 1, transitions.CompletedCount,
                "South の押下で再開が完了する。段階=" + Sessions().Session.Respawn.Phase);
            Assert.AreEqual(GameMode.Exploration, modes.Current, "到着して探索へ戻る。");

            // <b>押したまま</b>到着後を観測する。
            yield return AssertHeldButtonDoesNotAct("ゲームパッド South");

            // 離して押し直せば、ふつうに 1 回効く（塞ぎっぱなしにしない）。
            yield return ReleaseGamepad();
            yield return null;
            Assert.IsFalse(InputReleaseGateProvider.Current.RequiresRelease,
                "離したら解放待ちが解ける（最初の 1 回を飲み込まない）。");

            CountingInteractable padTarget = RegisterTargetNearPlayer("p18_pad");
            yield return PressGamepadSouth();
            yield return WaitUntilOrTimeout(() => padTarget.Calls >= 1, 3f);
            Assert.AreEqual(1, padTarget.Calls,
                "離して押し直せば Interact が 1 回効く（解放待ちが解ける）。"
                + " 仲介=" + Object.FindFirstObjectByType<AreaInteractInput>()?.InteractCount);
            yield return ReleaseGamepad();

            // ================================ 2. 実キー Enter を押しっぱなしで再開 ================================
            yield return KillPlayerWithRealHit();
            yield return WaitForRespawnPrompt();

            completed = transitions.CompletedCount;
            yield return PressKey(Key.Enter);
            yield return WaitUntilOrTimeout(() => transitions.CompletedCount > completed, 20f);
            yield return WaitUntilOrTimeout(() => modes.Current == GameMode.Exploration, 5f);
            Assert.AreEqual(completed + 1, transitions.CompletedCount,
                "Enter の押下でも再開が完了する。" + DescribeSubmit());

            yield return AssertHeldButtonDoesNotAct("実キー Enter");

            yield return ReleaseKeys();
            CountingInteractable keyTarget = RegisterTargetNearPlayer("p18_key");
            yield return PressKey(Key.E);
            yield return WaitUntilOrTimeout(() => keyTarget.Calls >= 1, 3f);
            Assert.AreEqual(1, keyTarget.Calls, "離して E を押せば Interact が 1 回効く。");
            yield return ReleaseKeys();
        }

        /// <summary>押しっぱなしのまま到着した直後を観測する（Interact も Step も起きない）。</summary>
        private IEnumerator AssertHeldButtonDoesNotAct(string label)
        {
            var mediator = Object.FindFirstObjectByType<AreaInteractInput>();
            Assert.IsNotNull(mediator, "到着先に Interact の入力仲介がある。");
            var player = Object.FindFirstObjectByType<PlayerStateController>();
            Assert.IsNotNull(player);

            // 仕組みも固定する。<b>「たまたま起きなかった」では受入にならない。</b>
            // 押している間は解放待ちが立っていること自体を見る（§9.1 末尾が求めているのはこれ）。
            Assert.IsNotNull(InputReleaseGateProvider.Current, "解放待ちの提供点が入っている。");
            Assert.IsTrue(InputReleaseGateProvider.Current.RequiresRelease,
                label + "：押している間は「離すまで使わせない」が立っている。"
                + " ボタン=" + (InputReleaseGateProvider.Current as PlayerInputAdapter)?.HeldSubmitDiagnostics
                + " " + DescribeSubmit());

            bool sawStep = false;
            for (int i = 0; i < 24; i++)
            {
                sawStep |= player.Current == PlayerState.Step;
                yield return null;
            }

            Assert.IsTrue(InputReleaseGateProvider.Current.RequiresRelease,
                label + "：押し続けている間は解けない。");
            Assert.AreEqual(0, mediator.InteractCount,
                label + "：再開の押しっぱなしが到着先の Interact にならない（§9.1 末尾）。"
                + " 捨てた=" + mediator.DiscardedCount
                + " 解放待ち=" + ((PlayerInputProvider.Current as PlayerInputState)?.RequiresRelease)
                + " ラッチ=" + ((PlayerInputProvider.Current as IInteractInput)?.InteractPressed));
            Assert.IsFalse(sawStep, label + "：Step にもならない。");
        }

        /// <summary>
        /// 入力 Asset の状態を読める文にする。
        /// <b>この症状は 2 回踏んでいる</b>（Action Map が PlayMode をまたいで無効のまま残る／
        /// 外れたデバイスの参照で入力が止まる）。どちらも「キーが届かない」としか見えないので、
        /// 失敗時に必ず Map の有効・無効が出るようにしておく。
        /// </summary>
        private static string DescribeInputAsset()
        {
            InputActionAsset asset = InputSystem.actions;
            if (asset == null)
            {
                return "asset=(project-wide 未設定)";
            }

            var text = new System.Text.StringBuilder("asset=" + asset.name + " maps=");
            foreach (InputActionMap map in asset.actionMaps)
            {
                text.Append(map.name).Append(map.enabled ? "[有効] " : "[無効] ");
            }

            InputAction submit = asset.FindAction("UI/Submit", false);
            text.Append(" Submit=")
                .Append(submit == null ? "(見つからない)" :
                    (submit.enabled ? "有効" : "無効") + " controls=" + submit.controls.Count
                    + " pressed=" + submit.IsPressed());

            text.Append(" timeScale=").Append(Time.timeScale)
                .Append(" updateMode=").Append(InputSystem.settings.updateMode)
                .Append(" devices=");
            foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
            {
                text.Append(device.name).Append(device.added ? "[在] " : "[無] ");
            }

            InputAction interact = asset.FindAction("Gameplay/Interact", false);
            text.Append(" Interact=")
                .Append(interact == null ? "(見つからない)" :
                    (interact.enabled ? "有効" : "無効") + " controls=" + interact.controls.Count
                    + " pressed=" + interact.IsPressed())
                .Append(" 入力ゲート=")
                .Append(PlayerInputProvider.Current != null ? PlayerInputProvider.Current.Active.ToString() : "(なし)");

            text.Append(" 解放待ち=")
                .Append(InputReleaseGateProvider.Current != null
                    ? InputReleaseGateProvider.Current.RequiresRelease.ToString() : "(なし)");
            return text.ToString();
        }

        /// <summary>再開入力の様子を読める文にする（失敗時の手掛かり）。</summary>
        private static string DescribeSubmit()
        {
            var input = Object.FindFirstObjectByType<RespawnSubmitInput>();
            var runner = Object.FindFirstObjectByType<CampaignRespawnRunner>();
            return "段階=" + (GameSessionProvider.Current != null
                    ? GameSessionProvider.Current.Respawn.Phase.ToString() : "(Session なし)")
                + " mode=" + (GameModeProvider.Current != null ? GameModeProvider.Current.Current.ToString() : "null")
                + " 仲介=" + (input != null
                    ? "submit=" + input.SubmitCount + " 捨てた=" + input.DiscardedCount
                    : "(なし)")
                + " 押下ラッチ=" + (RespawnSubmitProvider.Current != null
                    ? RespawnSubmitProvider.Current.SubmitPressed.ToString() : "(提供点なし)")
                + " 再開の拒否=" + (runner != null ? runner.LastRejection.ToString() : "-")
                + " 遷移の拒否=" + (runner != null ? runner.LastTravelRejection.ToString() : "-")
                + " 待ち=" + (runner != null ? runner.IsAwaitingRespawn.ToString() : "-")
                + " 生の押下=" + ((RespawnSubmitProvider.Current as RespawnSubmitState)?.IsHeld)
                + " 押下回数=" + ((RespawnSubmitProvider.Current as RespawnSubmitState)?.PressCount)
                + " " + DescribeInputAsset();
        }

        /// <summary>主人公のすぐ隣に、実行回数だけを数える対象を登録する。</summary>
        private CountingInteractable RegisterTargetNearPlayer(string id)
        {
            var root = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(root);
            var target = new CountingInteractable(id, AreaA, root.transform.position + new Vector3(0.3f, 0f, 0f));
            AreaInteractableRegistry.Register(target);
            return target;
        }


        /// <summary>
        /// P5-P16：<b>Builder の出力だけで一周が立ち上がる</b>（§13.1「初回だけ手で接続する工程を残さない」）。
        ///
        /// このテストは<b>自分では一切 Bind しない</b>。それがこの検査の全部で、
        /// ほかの受入テストが必要な参照を自分で注入してしまうために見えなくなる穴を、ここだけが塞ぐ。
        /// 統合起動 Scene から入って A を確かめ、B へ渡って戦闘まで、すべて出荷物の配線で通す。
        /// </summary>
        [UnityTest]
        public IEnumerator RebuiltScenes_RunWithoutManualWiring()
        {
            AssertSceneRegistered(TrialScene);
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);

            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            // ================================ 1. 統合起動 Scene から入る ================================
            yield return SceneManager.LoadSceneAsync(TrialScene, LoadSceneMode.Single);
            yield return null;

            var launcher = Object.FindFirstObjectByType<Phase5TrialLauncher>();
            Assert.IsNotNull(launcher, "統合起動 Scene に起動役がある（出荷物）。");

            _bootstrap = new GameObject("BootstrapRoot_P5Test");
            _bootstrap.AddComponent<BootstrapRoot>();

            // 起動役が自分で A を要求して到着し、<b>活動の許可まで出る</b>のを待つ
            // （テスト側からは何も要求しない）。初期化の成立だけでは到着の後始末がまだ終わっていない。
            yield return WaitUntilOrTimeout(
                () => BootstrapRoot.HasInstance
                    && BootstrapRoot.Instance.GetService<AreaTransitionService>()?.Coordinator != null
                    && BootstrapRoot.Instance.GetService<AreaTransitionService>().Coordinator.CompletedCount >= 1,
                20f);

            AreaTransitionCoordinator transitions = Transitions().Coordinator;
            Assert.IsNotNull(transitions, "常駐の遷移サービスが立つ。");
            Assert.AreEqual(1, transitions.CompletedCount,
                "起動役が A へ入れる（手で遷移を要求しない）。理由=" + launcher.LastRejection);

            // ---- 注入（Provider）がそろっている ----
            Assert.IsNotNull(GameModeProvider.Current, "GameMode の提供点が入る。");
            Assert.IsNotNull(PlayerInputProvider.Current, "主人公入力の提供点が入る。");
            Assert.IsNotNull(RespawnSubmitProvider.Current, "再開操作の提供点が入る（§9.1 手順 3）。");
            Assert.IsNotNull(GameplayClockProvider.Current, "Gameplay 時計の提供点が入る。");
            Assert.IsNotNull(CompanionActivityProvider.Current, "仲間の活動 Context が提供点へ入る。");
            Assert.IsNotNull(GameSessionProvider.Current, "本編型 Session が提供点へ入る。");

            AreaInitializer initA = FindInitializer();
            Assert.IsTrue(initA.Initialized, "A が初期化される。理由=" + initA.FailureReason);
            Assert.AreEqual(AreaA.Value, initA.AreaId.Value);
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);

            // ---- A の配線（HUD・調査・Interact・カメラ・再開）----
            AssertAreaWiredWithoutHelp("A");

            var coordinator = Object.FindFirstObjectByType<InvestigationCoordinator>();
            Assert.IsNotNull(coordinator, "A に調査の調停役がある。");
            Assert.IsTrue(coordinator.IsWired, "調査が配線済みで立ち上がる。");
            Assert.IsTrue(coordinator.ExplicitTargetOnly, "P5 は指定地点モード（§7.1）。");
            Assert.Greater(AreaInteractableRegistry.Count, 0,
                "Interact 候補が自分で登録される（テストが登録しない）。");

            // ================================ 2. B へ渡って戦闘まで ================================
            AreaTransitionDecision toB = Transitions().TryTravel(AreaB, AreaBFromA);
            Assert.IsTrue(toB.Accepted, "出荷物の配線だけで移動できる。理由=" + toB.Rejection);
            yield return WaitForArrival(transitions, 2);

            AreaInitializer initB = FindInitializer();
            Assert.IsTrue(initB.Initialized, "B が初期化される。理由=" + initB.FailureReason);
            AssertAreaWiredWithoutHelp("B");

            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runner, "B に遭遇戦の調停がある。");
            Assert.IsTrue(runner.IsWired, "遭遇戦が配線済みで立ち上がる。");
            Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                "到着時点の敵は 0（§13.2）。");

            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");
            yield return MovePlayerTo(EncounterTriggerPoint);
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 5f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                "Trigger を踏むだけで戦闘が始まる（手で開始しない）。拒否=" + runner.LastRejection
                + " " + runner.LastFailureDetail);
            Assert.AreEqual(2, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                "敵が出荷物の表から湧く。");

            var dispatcher = Object.FindFirstObjectByType<CombatFeedbackDispatcher>();
            Assert.IsNotNull(dispatcher, "命中 Feedback の配信役も出荷物に含まれる。");
            var binder = Object.FindFirstObjectByType<EncounterFeedbackBinder>();
            Assert.IsNotNull(binder);
            Assert.IsTrue(binder.IsWired);
            Assert.AreEqual(1, binder.RescanCount, "生成直後に購読し直している（§8.2 手順 7）。");

            yield return ReleaseKeys();
        }

        /// <summary>出荷 Scene が「置いてあるだけ」でなく繋がっていることを見る（テスト側は Bind しない）。</summary>
        private static void AssertAreaWiredWithoutHelp(string label)
        {
            var context = Object.FindFirstObjectByType<AreaContext>();
            Assert.IsNotNull(context, label + "：エリアの初期化状態がある。");
            Assert.IsTrue(context.IsAreaReady, label + "：活動が許可されている。");

            var interaction = Object.FindFirstObjectByType<AreaInteractionController>();
            Assert.IsNotNull(interaction, label + "：Interact の単一窓口がある。");
            Assert.IsTrue(interaction.IsWired, label + "：Interact 窓口が配線されている。");

            var mediator = Object.FindFirstObjectByType<AreaInteractInput>();
            Assert.IsNotNull(mediator, label + "：Interact の入力仲介がある。");
            Assert.IsNotNull(mediator.Controller, label + "：仲介が窓口へ配線されている。");

            var progress = Object.FindFirstObjectByType<PlayerProgressHolder>();
            Assert.IsNotNull(progress, label + "：進行の書込先がある。");
            Assert.IsTrue(progress.IsBound, label + "：進行が共有 State へ束ねられている（§4.2）。");

            var rig = Object.FindFirstObjectByType<AreaCameraRig>();
            Assert.IsNotNull(rig, label + "：カメラの Rig がある。");
            Assert.IsTrue(rig.IsWired, label + "：カメラが配線されている。");
            Assert.GreaterOrEqual(rig.SnapCount, 1, label + "：到着で即時配置されている（§11）。");

            var hud = Object.FindFirstObjectByType<CombatPlayHud>();
            Assert.IsNotNull(hud, label + "：HUD がある（§11 の必須 UI）。");

            var respawnView = Object.FindFirstObjectByType<CampaignRespawnView>();
            Assert.IsNotNull(respawnView, label + "：再開操作の表示がある。");
            Assert.IsTrue(respawnView.IsWired, label + "：再開の表示が配線されている。");

            var respawnInput = Object.FindFirstObjectByType<RespawnSubmitInput>();
            Assert.IsNotNull(respawnInput, label + "：再開操作の仲介がある。");
            Assert.IsTrue(respawnInput.IsWired, label + "：再開の仲介が配線されている。");

            var navigation = Object.FindFirstObjectByType<AreaNavigationBinder>();
            Assert.IsNotNull(navigation, label + "：経路 Adapter の配線役がある。");
            Assert.IsTrue(navigation.IsWired, label + "：経路の配線役が繋がっている。");

            var follow = Object.FindFirstObjectByType<CompanionFollowController>();
            Assert.IsNotNull(follow, label + "：犬丸が居る。");
            Assert.IsTrue(follow.HasPathProvider,
                label + "：経路の供給元が実際に注入されている（§10.1。未注入なら経路追従が止まる）。");
        }


        // ---------------------------------------------------------------- P14・P19（残留の検査）

        /// <summary>
        /// P5-P14：A↔B を 3 往復して死亡再開まで通しても、<b>旧 Scene 由来の登録と実体が残らない</b>
        /// （§5.1／§6.2 手順 4）。新しい Scene の正規の登録は残っていることも同時に見る。
        ///
        /// <b>「全部 0」を求めない。</b> 活動中の Area には登録があるのが正しく、
        /// 全体 0 を要求すると「新しい Scene も壊れている」状態を合格にしてしまう（§15.5）。
        /// 見るのは「いま居る Scene のものだけが居る」こと。
        /// </summary>
        [UnityTest]
        public IEnumerator RepeatedTravelAndRespawn_LeaveOneOwnerAndNoOldColliders()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");

            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator transitions = service.Coordinator;
            GameSessionState session = Sessions().Session;

            // ================================ 1. A↔B を 3 往復 ================================
            int completed = 0;
            for (int lap = 1; lap <= 3; lap++)
            {
                Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted, lap + " 周目：B へ。");
                yield return WaitForArrival(transitions, ++completed);
                AssertOnlyCurrentSceneIsRegistered(lap + " 周目の B");

                Assert.IsTrue(service.TryTravel(AreaA, AreaAFromB).Accepted, lap + " 周目：A へ。");
                yield return WaitForArrival(transitions, ++completed);
                AssertOnlyCurrentSceneIsRegistered(lap + " 周目の A");
            }

            Assert.AreEqual(1, Sessions().CreatedCount, "往復で Session を作り直さない（§5.2）。");

            // 往復しても<b>入力が生きている</b>ことを実キーで確かめる。
            // ここが死んでいると、以降の失敗が「再開できない」に見えて原因を取り違える。
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "A にレバーがある。");
            yield return MovePlayerTo(lever.InteractionAnchor + new Vector3(0.6f, 0f, 0f));
            yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 10f);
            Assert.AreEqual(1, lever.OpenedCount,
                "3 往復したあとでも実キーの Interact が効く。" + DescribeInputAsset());

            // ================================ 2. 死亡再開 ================================
            yield return KillPlayerWithRealHit();
            yield return WaitForRespawnPrompt();

            int before = transitions.CompletedCount;

            yield return PressKeyUntil(Key.Enter, () => transitions.CompletedCount > before, 20f);
            Assert.AreEqual(before + 1, transitions.CompletedCount,
                "再開のロードが完了する。" + DescribeSubmit());
            yield return WaitUntilOrTimeout(
                () => GameModeProvider.Current.Current == GameMode.Exploration, 5f);

            AssertOnlyCurrentSceneIsRegistered("死亡再開の到着");
            Assert.AreEqual(1, session.RespawnCycle, "再出現周期は 1 回だけ進む。");

            // ================================ 3. 犬丸の健全性（§5.1 末尾）================================
            var actor = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(actor, "到着先に犬丸が居る。");
            Assert.AreEqual(0, actor.IllegalTransitionCount,
                "往復と再開を通して不正遷移を出さない（診断カウンタを消してから見ない）。");

            // 到着直後の最初の Tick まで観測する（§15.3）。
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0, actor.IllegalTransitionCount, "最初の Tick を回しても増えない。");

            // 旧 Scene の行動の券を持ち越さない（§15.3）。
            // <b>追従が握っているのは正常</b>なので、そこは咎めない。戦闘・調査が残っていないことを見る。
            var states = actor.GetComponent<CompanionStateArbiter>();
            Assert.IsNotNull(states);
            Assert.AreNotEqual(CompanionActionOwner.Combat, states.CurrentOwner,
                "到着直後に戦闘の券を握ったままにしない。");
            Assert.AreNotEqual(CompanionActionOwner.Investigate, states.CurrentOwner,
                "到着直後に調査の券を握ったままにしない。");
            Assert.IsTrue(CanFollowAgain(actor.State),
                "到着直後から行動できる状態に居る。状態=" + actor.State);
        }

        /// <summary>
        /// いま活動している Scene のものだけが登録に残っていることを見る。
        /// <b>破棄予定になっただけを成功にしない</b>ので、登録の中身が現在の Scene に属することまで確かめる（§15.3）。
        /// </summary>
        private static void AssertOnlyCurrentSceneIsRegistered(string label)
        {
            Scene current = SceneManager.GetActiveScene();
            AssertSingleOwners(label);

            Assert.AreEqual(1, Object.FindObjectsByType<AreaRoot>(FindObjectsSortMode.None).Length,
                label + "：エリアの根は 1 つ（旧 Scene の根が残らない）。");
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount, label + "：残留 Projectile なし。");

            // 索敵：敵が居ない Area では 0、居る Area でも「その Scene の敵だけ」。
            int hostiles = CountHostilePerceptionTargets();
            int enemies = Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length;
            Assert.AreEqual(enemies, hostiles,
                label + "：索敵の登録は活動中の敵と同数（旧 Scene 由来の登録が残らない）。敵=" + enemies);

            // Interact 候補・調査地点：数だけでなく、<b>いまの Scene に属している</b>ことを見る。
            var interactables = new List<IAreaInteractable>();
            AreaInteractableRegistry.CopyTo(interactables);
            Assert.Greater(interactables.Count, 0, label + "：新 Scene の Interact 候補は残っている。");
            for (int i = 0; i < interactables.Count; i++)
            {
                AssertBelongsToScene(interactables[i], current, label + "：Interact 候補");
            }

            var points = new List<IInvestigationPoint>();
            InvestigationPointRegistry.CopyTo(points);
            Assert.Greater(points.Count, 0, label + "：新 Scene の調査地点は残っている。");
            for (int i = 0; i < points.Count; i++)
            {
                AssertBelongsToScene(points[i], current, label + "：調査地点");
            }
        }

        /// <summary>登録されている実体が、いま活動している Scene のものかを見る。</summary>
        private static void AssertBelongsToScene(object registered, Scene current, string label)
        {
            var component = registered as Component;
            Assert.IsNotNull(component, label + "：登録が Scene の実体ではありません（" + registered + "）。");
            Assert.IsTrue(component != null && component.gameObject != null,
                label + "：破棄済みの実体が登録に残っています。");
            Assert.AreEqual(current.handle, component.gameObject.scene.handle,
                label + "：旧 Scene の実体が登録に残っています（" + component.name
                + " は " + component.gameObject.scene.name + " 所属）。");
        }

        /// <summary>
        /// P5-P19：Area と Actor をすべて破棄して unload が終わったあと、
        /// <b>Clear を呼ぶ前に</b>静的な登録が 0 であることを見る（§15.3 の 2 行目）。
        ///
        /// Assert の前に Clear すると、残留をテスト自身が隠してしまう。
        /// 隔離のための Clear は失敗後の <c>finally</c> でだけ行う。
        /// </summary>
        [UnityTest]
        public IEnumerator RegistryTeardown_LeavesZeroWithoutClearingBeforeAssertion()
        {
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaBScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            _keyboard = InputSystem.AddDevice<Keyboard>("P5Keyboard");

            // 敵まで湧かせてから畳む（「何も起きていない Scene を捨てた」では検査にならない）。
            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runner);
            yield return MovePlayerTo(EncounterTriggerPoint);
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 5f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                "前提：戦闘が始まる。拒否=" + runner.LastRejection + " " + runner.LastFailureDetail);
            Assert.Greater(CountHostilePerceptionTargets(), 0, "前提：索敵に敵が載っている。");
            Assert.Greater(AreaInteractableRegistry.Count, 0, "前提：Interact 候補が載っている。");
            Assert.Greater(InvestigationPointRegistry.Count, 0, "前提：調査地点が載っている。");

            try
            {
                // ---- すべて畳む。unload の完了まで待つ ----
                yield return ReleaseKeys();
                yield return SceneManager.LoadSceneAsync(TrialScene, LoadSceneMode.Single);
                DestroyTrialLaunchers();
                yield return null;

                AsyncOperation unload = Resources.UnloadUnusedAssets();
                while (unload != null && !unload.isDone)
                {
                    yield return null;
                }

                // 破棄は次のフレームに回ることがある。「Destroy 予定になった」で成功にしない。
                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                }

                Assert.AreEqual(0, Object.FindObjectsByType<AreaRoot>(FindObjectsSortMode.None).Length,
                    "前提：Area はすべて破棄されている。");
                Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                    "前提：敵はすべて破棄されている。");

                // ---- ここで Clear を呼ばずに数える（呼んだら検査にならない）----
                Assert.AreEqual(0, CountHostilePerceptionTargets(),
                    "索敵の登録が残らない。残り=" + PerceptionTargetRegistry.Count);
                Assert.AreEqual(0, AreaInteractableRegistry.Count,
                    "Interact 候補の登録が残らない。残り=" + AreaInteractableRegistry.Count);
                Assert.AreEqual(0, InvestigationPointRegistry.Count,
                    "調査地点の登録が残らない。残り=" + InvestigationPointRegistry.Count);
                Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount,
                    "Projectile の登録が残らない。残り=" + EnemyProjectileRegistry.LiveCount);
            }
            finally
            {
                // 隔離のための Clear は失敗したあとだけ（§15.3）。
                PerceptionTargetRegistry.Clear();
                AreaInteractableRegistry.Clear();
                InvestigationPointRegistry.Clear();
                EnemyProjectileRegistry.Clear();
            }
        }

    }
}
