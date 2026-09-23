using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Transfer;
using Momotaro.Presentation.Hud;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.World;
using NUnit.Framework;
using UnityEngine;
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
            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            PerceptionTargetRegistry.Clear();
            GameplayClockProvider.Current = null;
            GameSessionProvider.Current = null;
            AreaPendingArrival.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            // 先に Area Scene から抜けてから常駐を消す。逆にすると、残った AreaInitializer が
            // サービス不在で初期化に失敗し、後始末中にエラーログが出る（実際に踏んだ）。
            yield return SceneManager.LoadSceneAsync(
                "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_ExplorationTrial.unity", LoadSceneMode.Single);

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
            AreaPendingArrival.Clear();
            yield return null;
        }

        /// <summary>常駐サービスを作る（各テストで作り直す）。</summary>
        private IEnumerator CreateBootstrap()
        {
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

            // A → B を要求する。
            AreaTransitionDecision first = service.TryTravel(AreaB, AreaBFromA);
            Assert.IsTrue(first.Accepted, "受理される。理由=" + first.Rejection);
            int firstId = first.TransitionId;
            Assert.IsTrue(service.Clock.IsFrozen, "受理で Gameplay 時計が止まる。");

            // 遷移中の二重要求は拒否。
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning,
                service.TryTravel(AreaB, AreaBFromA).Rejection);

            // 完了まで待つ。
            float waited = 0f;
            while (coordinator.CompletedCount < 1 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, coordinator.CompletedCount, "A → B の遷移が完了する。");
            Assert.IsFalse(service.Clock.IsFrozen, "完了で時計が戻る。");
            Assert.AreEqual(AreaB.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value);
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);

            // ---- 完了通知の中から次の遷移を要求した状況 ----
            AreaTransitionDecision second = service.TryTravel(AreaA, AreaAFromB);
            Assert.IsTrue(second.Accepted, "続けて要求できる。理由=" + second.Rejection);
            Assert.AreNotEqual(firstId, second.TransitionId, "世代が進む。");

            // 古い世代の解除は効かない（旧 Coroutine の finally 相当）。
            int completedBefore = coordinator.CompletedCount;
            Assert.IsFalse(coordinator.Release(firstId), "旧世代は新しい遷移の排他を解除しない。");
            Assert.AreEqual(completedBefore, coordinator.CompletedCount);
            Assert.IsTrue(service.Clock.IsFrozen, "旧世代の解除で時計が戻ってしまわない。");

            waited = 0f;
            while (coordinator.CompletedCount < 2 && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(2, coordinator.CompletedCount, "2 回目の遷移も完了する。");
            Assert.AreEqual(AreaA.Value, Object.FindFirstObjectByType<AreaContext>().AreaId.Value, "A へ戻っている。");
            Assert.AreEqual(AreaAFromB.Value, Object.FindFirstObjectByType<AreaContext>().EntryId.Value,
                "指定した入口へ到着する。");

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
            service.LoadOverride = _ => null; // ロードを開始できない状況をテスト専用に注入する。
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

            // 差し替えを戻せば、次の遷移は普通に通る（無限に壊れたままにしない）。
            service.LoadOverride = null;
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
    }
}
