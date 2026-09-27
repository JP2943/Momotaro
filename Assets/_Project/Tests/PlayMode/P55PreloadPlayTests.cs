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
using NUnit.Framework;
using UnityEngine;
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
    /// <b>カメラの一本化はまだしていない</b>（§4.3 の Rig 単一化は P55-03）。
    /// この段階では B 側のカメラとライトも活きているので、見た目は正しくない。
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
        /// 先読みしても <b>Camera と AudioListener は 1 つのまま</b>（P5.5 §4.1）。
        ///
        /// ここが漏れると、隣 Area のカラが同じ画面へ描き、音も二重に聞こえる。
        /// <b>見た目の壊れ方なので自動テストで気付きにくい</b>——数で落とす。
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

            // 開けば B 側も動き出す（止めたままにならない）。
            Assert.IsTrue(
                AreaBundleDirectory.TryGetByScene(_preloader.StagedSceneHandle, out AreaRuntimeBundle staged));
            staged.ActivityGate.Open();
            yield return null;
            Assert.AreEqual(2, CountEnabled<Camera>(), "開けば B の Camera も描く。");
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
                    || _preloader.Phase == AreaPreloadPhase.Failed)
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
