using System.Collections;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Cameras;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 実 Scene での先読み（P5.5 仕様書 §4.1〜§4.3／§5。工程 P55-02d）。
    ///
    /// <b>P55-02 の総仕上げ。</b> ここで §4.1（段階）・§4.2（構造ゲート）・§4.3（帰属と参照集合）が
    /// 同時に働く——本物の Area B を A の隣へ<b>追加で</b>読み込み、
    /// 「載っているのに何も起きていない」ことを実 Scene で確かめる。
    ///
    /// <b>カメラの一本化は P55-03b で入った</b>（§4.3／付録 A）。Area Scene は Camera・
    /// AudioListener・基準照明を持たず、常駐 Rig が 1 台だけ描く。この工程で書いたときは
    /// まだ B 側のカメラが活きていたので、表示系の主張は<b>そのとき置き換えた</b>
    /// （「開けば 2 台」→「開いても 1 台のまま・常駐を作り直さない」）。
    /// ここで見たいのは活動と登録であって、見せ方ではない。
    /// </summary>
    public sealed class P55PreloadPlayTests
    {
        private const string AreaAScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity";
        private const string AreaBScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaB.unity";
        private const string TrialScene =
            "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_ExplorationTrial.unity";

        private static readonly StableId AreaA = new StableId("area_p5_a");
        private static readonly StableId AreaB = new StableId("area_p5_b");

        private GameObject _bootstrap;
        private AreaResidencyLedger _ledger;
        private AreaPreloader _preloader;
        private readonly object _owner = new object();

        [SetUp]
        public void SetUp()
        {
            // 前のテストが残した常駐 CameraRig を持ち込まない（P5.5 付録 A.2）。
            P55ResidentRig.Reset();

            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            AreaScope.ResetDiagnostics();
            PerceptionTargetRegistry.Clear();
            AreaInteractableRegistry.Clear();
            InvestigationPointRegistry.Clear();
            GameModeProvider.Current = null;
            GameSessionProvider.Current = null;
            GameplayClockProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            AreaPendingArrival.Clear();
            AreaPendingArrival.ResetDiagnostics();
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            // 追加で読んだ Scene を残さない（次のテストが 2 枚載った状態で始まらないように）。
            if (_preloader != null && _preloader.StagedSceneHandle != 0)
            {
                _preloader.ClearRequest();
                yield return PollUntilIdle(6f);
            }

            _preloader = null;
            _ledger = null;
            AreaStagingRequest.ResetForTests();

            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            yield return SceneManager.LoadSceneAsync(TrialScene, LoadSceneMode.Single);
            DestroyTrialLaunchers();
            yield return null;
            DestroyTrialLaunchers();

            // <b>常駐 CameraRig を次のテストへ残さない。</b> Scene を読み替えても消えないので、
            // 残すと次のテストが試遊 Scene の自前カメラと二重になる（P5.5 付録 A.2）。
            // Scene から抜けた<b>あと</b>に消す——先に消すと、まだ載っている Area の
            // 領域集合が「常駐が居ない」と見て作り直す。
            P55ResidentRig.Reset();

            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            if (_bootstrap != null)
            {
                Object.DestroyImmediate(_bootstrap);
                _bootstrap = null;
            }

            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            AreaScope.ResetDiagnostics();
            PerceptionTargetRegistry.Clear();
            AreaInteractableRegistry.Clear();
            InvestigationPointRegistry.Clear();
            GameModeProvider.Current = null;
            GameSessionProvider.Current = null;
            GameplayClockProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            AreaPendingArrival.Clear();
            yield return null;
        }

        // ---------------------------------------------------------------- P04

        /// <summary>
        /// A を遊んでいる間に B を<b>追加で</b>読み込んでも、B 側は何も動かない（§4.1〜§4.3）。
        ///
        /// 3 つが同時に効いていることを確かめる。
        /// <list type="number">
        /// <item><description><b>§4.2</b>：B の活動ゲートは閉じたまま（Gameplay も物理も購読も無効）。</description></item>
        /// <item><description><b>§4.3</b>：登録簿に B のものが混ざらない（境界越しの索敵・Interact が起きない）。</description></item>
        /// <item><description><b>§4.1／§9.1</b>：台帳は「Active 1 ＋ Staged 1」で上限内。</description></item>
        /// </list>
        /// </summary>
        [UnityTest]
        public IEnumerator PreloadingTheNeighbour_LoadsItAsleepAndKeepsItOutOfTheRegistries()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized, "前提：A の初期化が成立している。");
            Assert.AreEqual(1, AreaBundleDirectory.Count, "前提：A の束だけ。");

            int interactablesWithAOnly = AreaInteractableRegistry.Count;
            int perceptionWithAOnly = PerceptionTargetRegistry.Count;
            int pointsWithAOnly = InvestigationPointRegistry.Count;
            Assert.Greater(interactablesWithAOnly, 0, "前提：A の仕掛けは登録されている。");
            Assert.Greater(perceptionWithAOnly, 0, "前提：A の索敵対象は登録されている。");

            CreatePreloader(activeArea: AreaA);

            // ---- B を先読みする ----
            Assert.IsTrue(_preloader.Request(AreaB, AreaBScene));
            yield return PollUntilNotLoading(10f);

            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase,
                "先読みが成立している。理由=" + _preloader.FailureReason);
            Assert.AreNotEqual(0, _preloader.StagedSceneHandle);
            Assert.AreEqual(2, SceneManager.sceneCount, "Scene が 2 枚載っている。");
            Assert.AreEqual(2, AreaBundleDirectory.Count, "束も 2 件。");
            Assert.IsFalse(AreaBundleDirectory.TryGetSingle(out _),
                "2 枚載ったら単一前提の互換経路は使わせない。");

            // ---- §4.3：現行 Area は A のまま ----
            Assert.IsTrue(CurrentAreaProvider.HasScope,
                "2 枚目を載せる前に現行が確定している（載ってからでは言えない）。");
            Assert.AreEqual(AreaA, CurrentAreaProvider.Current.AreaId);

            // ---- §4.2：B の活動ゲートは閉じたまま ----
            Assert.IsTrue(
                AreaBundleDirectory.TryGetByScene(_preloader.StagedSceneHandle, out AreaRuntimeBundle staged),
                "Scene handle から B の束を引ける。");
            Assert.AreEqual(AreaB, staged.AreaId);
            AreaActivityGate stagedGate = staged.ActivityGate;
            Assert.IsNotNull(stagedGate, "B の束からゲートへ辿れる。");
            Assert.IsFalse(stagedGate.IsOpen, "B は閉じたまま載っている。");
            Assert.IsFalse(stagedGate.OpenedOnLoad, "読み込み時に開いていない。");
            for (int i = 0; i < stagedGate.GatedRoots.Count; i++)
            {
                Assert.IsFalse(stagedGate.GatedRoots[i].activeSelf,
                    "B の Gameplay は止まっている（" + stagedGate.GatedRoots[i].name + "）。");
            }

            // ---- §4.3：登録簿に B のものが混ざらない ----
            //
            // <b>ここが P55-02 の結論。</b> 閉じた状態で保存されているので OnEnable が走らず、
            // 登録簿の数は A だけのときと同じまま。
            Assert.AreEqual(interactablesWithAOnly, AreaInteractableRegistry.Count,
                "B の仕掛けは登録簿に載らない（境界の向こうの扉・レバーが Interact 候補に挙がらない）。");
            Assert.AreEqual(perceptionWithAOnly, PerceptionTargetRegistry.Count,
                "B の索敵対象は登録簿に載らない（境界越しの索敵が起きない）。");
            Assert.AreEqual(pointsWithAOnly, InvestigationPointRegistry.Count,
                "B の調査地点は登録簿に載らない（犬丸が隣 Area へ行かない）。");

            // 引き当ても A の Scene のものだけ。
            var interactables = new List<IAreaInteractable>();
            AreaInteractableRegistry.CopyTo(interactables);
            for (int i = 0; i < interactables.Count; i++)
            {
                var component = (Component)interactables[i];
                Assert.AreNotEqual(_preloader.StagedSceneHandle, component.gameObject.scene.handle,
                    "候補に B のものが混ざっていない（" + component.name + "）。");
            }

            // ---- §9.1：台帳は上限内 ----
            Assert.AreEqual(2, _ledger.ResidentCount);
            Assert.AreEqual(1, _ledger.CountOf(AreaActivationPhase.Active));
            Assert.AreEqual(1, _ledger.CountOf(AreaActivationPhase.Staged));
            Assert.IsTrue(_ledger.SatisfiesResidencyRules(out string violation), violation);
            Assert.IsFalse(_ledger.IsSceneOperationInFlight, "読み終わったら Scene 操作は閉じる。");
            Assert.IsFalse(AreaStagingRequest.IsRequested, "申し入れを残さない。");

            // ---- A はそのまま遊べる ----
            Assert.IsTrue(FindInitializer().Initialized, "A の初期化は生きている。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "A は活動中のまま。");
        }

        /// <summary>
        /// 先読みをやめれば、追加で読んだ Scene は撤去され、<b>A だけが残る</b>（§4.1 の Retiring）。
        /// </summary>
        [UnityTest]
        public IEnumerator ClearingThePreload_UnloadsTheNeighbourAndLeavesTheActiveAreaAlone()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            CreatePreloader(activeArea: AreaA);

            Assert.IsTrue(_preloader.Request(AreaB, AreaBScene));
            yield return PollUntilNotLoading(10f);
            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase, _preloader.FailureReason);
            Assert.AreEqual(2, SceneManager.sceneCount);

            _preloader.ClearRequest();
            yield return PollUntilIdle(10f);

            Assert.AreEqual(AreaPreloadPhase.Idle, _preloader.Phase, _preloader.FailureReason);
            Assert.AreEqual(1, SceneManager.sceneCount, "追加で読んだ Scene が消えている。");
            Assert.AreEqual(1, AreaBundleDirectory.Count, "束も 1 件に戻る。");
            Assert.AreEqual(1, _ledger.ResidentCount, "台帳も 1 件（撤去待ちが積み上がらない）。");

            // A は無傷。
            Assert.IsTrue(FindInitializer().Initialized);
            Assert.IsTrue(AreaBundleDirectory.TryGetSingle(out AreaRuntimeBundle remaining));
            Assert.AreEqual(AreaA, remaining.AreaId);
            Assert.IsTrue(remaining.Context.IsAreaReady);
        }

        /// <summary>
        /// 先読みしても <b>Camera と AudioListener は 1 つのまま</b>（P5.5 §4.1／付録 A.7）。
        ///
        /// ここが漏れると、隣 Area のカラが同じ画面へ描き、音も二重に聞こえる。
        /// <b>見た目の壊れ方なので自動テストで気付きにくい</b>——数で落とす。
        ///
        /// <b>後半は置き換えた。</b> P55-03a では B 側の Camera が「止まっているだけ」で
        /// Scene に居たので、ゲートを開ければ 2 台になるのが正しい姿だった。
        /// 付録 A.1 で所有を常駐側へ移した今は<b>B に Camera が無い</b>ので、
        /// 開いても 1 台のまま——そして常駐 Rig が<b>再生成も再 Bind もされない</b>ことが
        /// 見るべき性質になった（付録 A.7 の最後の項目）。
        /// </summary>
        [UnityTest]
        public IEnumerator PreloadingTheNeighbour_DoesNotAddASecondCameraOrListener()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            int camerasWithAOnly = CountEnabled<Camera>();
            int listenersWithAOnly = CountEnabled<AudioListener>();
            Assert.AreEqual(1, camerasWithAOnly, "前提：A だけなら Camera は 1 台。");
            Assert.AreEqual(1, listenersWithAOnly, "前提：AudioListener も 1 つ。");

            CreatePreloader(activeArea: AreaA);
            Assert.IsTrue(_preloader.Request(AreaB, AreaBScene));
            yield return PollUntilNotLoading(10f);
            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase, _preloader.FailureReason);

            Assert.AreEqual(1, CountEnabled<Camera>(), "先読みしても描く Camera は 1 台。");
            Assert.AreEqual(1, CountEnabled<AudioListener>(), "聞く AudioListener も 1 つ。");

            // <b>常駐 Rig は先読みで作り直されない</b>（付録 A.2 の ensure-create／A.7）。
            // B 側の領域集合も ensure-create を呼ぶので、そこが「無ければ作る」でなく
            // 「毎回作る」になっていると、ここで 2 つ目が生まれる。
            Assert.AreEqual(1, AreaCameraRigHost.CreatedCount, "常駐 Rig を先読みで作り直さない。");
            Assert.AreEqual("area_p5_a", AreaCameraRigHost.Instance.BoundArea.Value,
                "先読み中も結び先は活動中の A（隣の領域で clamp しない）。");

            int bindsBeforeOpening = AreaCameraRigHost.Instance.BindCount;

            // 開けば B 側の Gameplay は動き出す（止めたままにならない）。
            // <b>Camera は増えない</b>——B は Camera を持たないので（付録 A.1）。
            Assert.IsTrue(
                AreaBundleDirectory.TryGetByScene(_preloader.StagedSceneHandle, out AreaRuntimeBundle staged));
            staged.ActivityGate.Open();
            yield return null;

            Assert.AreEqual(1, CountEnabled<Camera>(), "開いても描く Camera は 1 台（B は持たない）。");
            Assert.AreEqual(1, CountEnabled<AudioListener>(), "AudioListener も 1 つのまま。");
            Assert.AreEqual(1, AreaCameraRigHost.CreatedCount, "ゲートを開けても作り直さない。");
            Assert.AreEqual(bindsBeforeOpening, AreaCameraRigHost.Instance.BindCount,
                "活動 Area が変わっていないので結び直さない（Commit まで A のまま。付録 A.5）。");
            Assert.AreEqual("area_p5_a", AreaCameraRigHost.Instance.BoundArea.Value,
                "結び先も A のまま。");
        }

        /// <summary>有効な部品の数（非 Active な物体の下は数えない）。</summary>
        private static int CountEnabled<T>() where T : Behaviour
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int n = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].isActiveAndEnabled)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// 先読みしても <b>活動中 Area の経路は変わらない</b>（P5.5 §4.3）。
        ///
        /// NavMeshSurface は <c>OnEnable</c> で NavMeshData を登録するので、保存時に有効なままだと
        /// <b>読み込んだだけで隣 Area の床が経路に加わる</b>。門の NavMeshObstacle も同じで、
        /// Collider と AreaFlagDoor を止めても carving は止まらない（GPT レビュー R10 の指摘 3）。
        ///
        /// 三角形分割の頂点数で見るのは、「登録されたか」が直接出る単一の値だから。
        /// </summary>
        [UnityTest]
        public IEnumerator PreloadingTheNeighbour_DoesNotChangeTheActiveAreaNavMesh()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);

            int verticesWithAOnly = NavMesh.CalculateTriangulation().vertices.Length;
            Assert.Greater(verticesWithAOnly, 0, "前提：A の NavMesh が登録されている。");

            NavMeshPath before = PathAcrossAreaA();

            // <b>PathComplete を要求しない。</b> A の 2 つの入口は閉じた門を挙んでおり、
            // 実行時は Obstacle の carving が効くので PathPartial になる（それが正しい）。
            // ここで見たいのは「先読みで形が変わらない」ことだけ。
            Assert.AreNotEqual(NavMeshPathStatus.PathInvalid, before.status,
                "前提：A 内で経路の計算が成立する。");

            CreatePreloader(activeArea: AreaA);
            Assert.IsTrue(_preloader.Request(AreaB, AreaBScene));
            yield return PollUntilNotLoading(10f);
            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase, _preloader.FailureReason);

            Assert.AreEqual(verticesWithAOnly, NavMesh.CalculateTriangulation().vertices.Length,
                "先読みしても NavMesh の登録は増えない。");

            NavMeshPath during = PathAcrossAreaA();
            Assert.AreEqual(before.status, during.status, "経路の成否が変わらない。");
            Assert.AreEqual(before.corners.Length, during.corners.Length, "経路の形が変わらない。");

            // 開けば B の床も経路に加わる（止めたままにならない）。
            Assert.IsTrue(
                AreaBundleDirectory.TryGetByScene(_preloader.StagedSceneHandle, out AreaRuntimeBundle staged));
            staged.ActivityGate.Open();
            yield return null;
            Assert.Greater(NavMesh.CalculateTriangulation().vertices.Length, verticesWithAOnly,
                "開けば B の NavMesh も登録される。");

            // 撤去すれば登録は残らない。
            _preloader.ClearRequest();
            yield return PollUntilIdle(10f);
            Assert.AreEqual(AreaPreloadPhase.Idle, _preloader.Phase, _preloader.FailureReason);
            yield return null;
            Assert.AreEqual(verticesWithAOnly, NavMesh.CalculateTriangulation().vertices.Length,
                "撤去後に B の登録が残らない（NavMeshDataInstance の残留を作らない。§4.3）。");
        }

        /// <summary>A の入口同士を結ぶ経路を 1 本取る（形の変化を見るため）。</summary>
        private static NavMeshPath PathAcrossAreaA()
        {
            AreaRoot root = null;
            AreaRoot[] roots = Object.FindObjectsByType<AreaRoot>(FindObjectsSortMode.None);
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].AreaId.Equals(AreaA))
                {
                    root = roots[i];
                    break;
                }
            }

            Assert.IsNotNull(root, "A の根が見つかりません。");
            Assert.GreaterOrEqual(root.EntryPoints.Count, 2, "前提：入口が 2 つ以上ある。");

            var path = new NavMeshPath();
            NavMesh.CalculatePath(
                root.EntryPoints[0].transform.position,
                root.EntryPoints[1].transform.position,
                NavMesh.AllAreas,
                path);
            return path;
        }

        // ---------------------------------------------------------------- 補助

        /// <summary>
        /// 先読み役を作り、いま遊んでいる Area を台帳へ Active として登録する。
        /// 台帳に活動中を載せておかないと、在留上限の検査が本番と違う条件になる。
        /// </summary>
        private void CreatePreloader(StableId activeArea)
        {
            _ledger = new AreaResidencyLedger();
            AreaInstanceHandle active = _ledger.NextHandle(activeArea);
            Assert.IsTrue(_ledger.TryAdmitStaged(active));
            Assert.IsTrue(_ledger.TrySetPhase(active, AreaActivationPhase.Active));
            _preloader = new AreaPreloader(_ledger, new UnityAreaSceneHost(), _owner);
        }

        private IEnumerator PollUntilNotLoading(float limitSeconds)
        {
            float started = Time.realtimeSinceStartup;
            while (true)
            {
                _preloader.Poll();
                if (_preloader.Phase != AreaPreloadPhase.Loading
                    && _preloader.Phase != AreaPreloadPhase.Releasing)
                {
                    yield break;
                }

                if (Time.realtimeSinceStartup - started > limitSeconds)
                {
                    Assert.Fail("先読みが " + limitSeconds + " 秒で終わりませんでした（段階="
                        + _preloader.Phase + "／理由=" + _preloader.FailureReason + "）。");
                }

                yield return null;
            }
        }

        private IEnumerator PollUntilIdle(float limitSeconds)
        {
            float started = Time.realtimeSinceStartup;
            while (true)
            {
                _preloader.Poll();
                if (_preloader.Phase == AreaPreloadPhase.Idle
                    || _preloader.Phase == AreaPreloadPhase.Failed
                    || _preloader.Phase == AreaPreloadPhase.ReleaseFailed)
                {
                    yield break;
                }

                if (Time.realtimeSinceStartup - started > limitSeconds)
                {
                    Assert.Fail("撤去が " + limitSeconds + " 秒で終わりませんでした（段階="
                        + _preloader.Phase + "）。");
                }

                yield return null;
            }
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

            Assert.IsTrue(registered, "Scene が Build Settings へ未登録です: " + scenePath);
        }

        /// <summary>活動中 Area の初期化担当を引く（B の分は非 Active なので混ざらない）。</summary>
        private static AreaInitializer FindInitializer()
        {
            AreaInitializer[] found =
                Object.FindObjectsByType<AreaInitializer>(FindObjectsSortMode.None);
            Assert.AreEqual(1, found.Length,
                "活動中の初期化担当は 1 つ（B の分は閉じているので見えない）。");
            return found[0];
        }

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

        private IEnumerator CreateBootstrap()
        {
            DestroyTrialLaunchers();

            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P55PreloadTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.HasInstance, "BootstrapRoot が生成されていること。");
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐サービスの初期化が成立していること。理由=" + BootstrapRoot.Instance.BootstrapFailure);

            DestroyTrialLaunchers();
        }
    }
}
