using System.Collections;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.World;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P5.5 の参照集合を実 Scene で通す（仕様書 §4.3。工程 P55-02）。
    ///
    /// EditMode（<c>P55AreaBundleTests</c>）では見られない 3 つをここで見る。
    /// <list type="number">
    /// <item><description><b>登録が自動で起きる</b>こと（EditMode では <c>OnEnable</c> が走らない）。</description></item>
    /// <item><description><b>Scene をまたいで引かない</b>こと（EditMode では追加 Scene を開けない）。</description></item>
    /// <item><description><b>非 Active を含めて引ける</b>こと（EditMode では includeInactive の有無が効かない）。</description></item>
    /// </list>
    ///
    /// 2 Area 同時読込の検査に<b>本物の Area Scene を 2 枚重ねない。</b>
    /// 活動隔離（Registry の Area 帰属・構造ゲート）は P55-02 の後半・P55-03 の仕事で、
    /// いま重ねると初期化が Provider を取り合ってエラーを出す。ここで見たいのは索引の分離なので、
    /// 2 枚目は<b>最小の擬似 Area</b>で足りる。
    /// </summary>
    public sealed class P55AreaBundlePlayTests
    {
        private const string AreaAScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity";
        private const string AreaBScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaB.unity";
        private const string TrialScene =
            "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_ExplorationTrial.unity";

        private static readonly StableId AreaA = new StableId("area_p5_a");
        private static readonly StableId AreaB = new StableId("area_p5_b");
        private static readonly StableId AreaBFromA = new StableId("area_p5_b_from_a");

        private GameObject _bootstrap;
        private readonly List<Object> _spawned = new List<Object>();
        private Scene _stubScene;

        [SetUp]
        public void SetUp()
        {
            // 前のテストの静的状態を持ち込まない（`CLAUDE.md` の PlayMode の落とし穴）。
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
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
            if (_stubScene.IsValid() && _stubScene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_stubScene);
            }

            _stubScene = default;

            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            // 先に Area Scene から抜けてから常駐を消す（残った AreaInitializer が
            // サービス不在で初期化に失敗してエラーログを出すのを避ける）。
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

            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();

            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            GameModeProvider.Current = null;
            GameSessionProvider.Current = null;
            GameplayClockProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            AreaPendingArrival.Clear();
            yield return null;
        }

        // ---------------------------------------------------------------- P55-P01

        /// <summary>
        /// A を直開きすると、束が<b>自分で登録されて</b>常駐がそこから部品を引く。
        /// 全 Scene 検索の互換経路は<b>一度も通らない</b>（§4.3）。
        /// </summary>
        [UnityTest]
        public IEnumerator DirectOpen_RegistersTheBundleAndResidentUsesItWithoutSearching()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized, "前提：A の初期化が成立している。");

            Assert.AreEqual(1, AreaBundleDirectory.Count,
                "Area Scene が 1 枚なら束も 1 件（OnEnable で自分で登録する）。");
            Assert.IsTrue(AreaBundleDirectory.TryGetSingle(out AreaRuntimeBundle bundle));
            Assert.IsTrue(bundle.IsWired, "Builder が配線して出荷している。");
            Assert.AreEqual(AreaA, bundle.AreaId);
            Assert.AreEqual(AreaAScene, bundle.gameObject.scene.path, "束は A の Scene に居る。");

            // 層をまたぐ部品（Infrastructure）も、この Scene の中だけを見て引ける。
            Assert.IsTrue(bundle.TryResolve(out RespawnSubmitInput submit),
                "再開の受付を束経由で引ける（常駐の肩代わり判定がここを使う）。");
            Assert.AreEqual(bundle.gameObject.scene.path, submit.gameObject.scene.path,
                "引いたのは同じ Scene のもの。");

            Assert.AreEqual(0, Transitions().BundleFallbackCount,
                "束が居るのだから、全 Scene 検索の互換経路は通らない。");
        }

        // ---------------------------------------------------------------- P55-P02

        /// <summary>
        /// Area が 2 枚載ると、索引は<b>Scene ごとに分かれ</b>、互換経路（単一前提）は使えなくなる。
        /// 束は<b>自分の Scene の外へ手を伸ばさない</b>（§4.3）。
        /// </summary>
        [UnityTest]
        public IEnumerator TwoScenesLoaded_IndexSeparatesThemAndBundlesDoNotReachAcross()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized, "前提：A の初期化が成立している。");
            Assert.IsTrue(AreaBundleDirectory.TryGetSingle(out AreaRuntimeBundle real));

            // ---- 2 枚目（最小の擬似 Area）----
            _stubScene = SceneManager.CreateScene("P55_StubArea");
            AreaRuntimeBundle stub = CreateStubBundle(_stubScene);
            yield return null;

            Assert.AreEqual(2, AreaBundleDirectory.Count, "2 枚載れば 2 件。");
            Assert.IsFalse(AreaBundleDirectory.TryGetSingle(out _),
                "2 件のときは単一前提の互換経路を使わせない（当て推量で片方を返さない）。");

            Assert.IsTrue(AreaBundleDirectory.TryGetByScene(real.SceneHandle, out AreaRuntimeBundle byReal));
            Assert.AreSame(real, byReal);
            Assert.IsTrue(AreaBundleDirectory.TryGetByScene(stub.SceneHandle, out AreaRuntimeBundle byStub));
            Assert.AreSame(stub, byStub);
            Assert.AreNotEqual(real.SceneHandle, stub.SceneHandle);

            // <b>境界を越えない。</b> 再開の受付は A の Scene にしか居ない。
            // 全 Scene 検索ならここで A のものを掴む——それが P5.5 で壊れる元。
            Assert.IsNotNull(
                Object.FindFirstObjectByType<RespawnSubmitInput>(FindObjectsInactive.Include),
                "前提：全 Scene 検索なら見つかる（このテストが空転していないことの確認）。");
            Assert.IsFalse(stub.TryResolve(out RespawnSubmitInput _),
                "擬似 Area の束は、隣の Scene の部品を引かない。");

            // <b>非 Active を含めて引ける。</b> Staged／Prepared の間は活動ゲートが閉じている。
            Assert.IsTrue(stub.TryResolve(out AreaEntryPoint entry),
                "非 Active な根の下の部品も引ける（活動ゲートが閉じていても在処は分かる）。");
            Assert.IsFalse(entry.gameObject.activeInHierarchy, "前提：非 Active。");
        }

        // ---------------------------------------------------------------- P55-P03

        /// <summary>
        /// A→B の遷移で<b>現行の指定が到着先へ移る</b>。移った先でも互換経路は通らない（§4.3）。
        /// </summary>
        [UnityTest]
        public IEnumerator Travel_MovesTheCurrentAreaDesignationWithoutFallingBack()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized, "前提：A の初期化が成立している。");

            AreaTransitionService service = Transitions();
            AreaTransitionCoordinator coordinator = service.Coordinator;
            Assert.IsNotNull(coordinator, "前提：カタログが渡されている。");
            int fallbackBefore = service.BundleFallbackCount;

            Assert.IsTrue(service.TryTravel(AreaB, AreaBFromA).Accepted);
            yield return WaitForCompleted(coordinator, 1);

            Assert.IsTrue(CurrentAreaProvider.HasScope, "到着で現行の指定が入る。");
            Assert.AreEqual(AreaB, CurrentAreaProvider.Current.AreaId, "現行は到着先。");
            Assert.AreEqual(CurrentAreaProvider.Current.SceneHandle, CurrentAreaProvider.ActiveSceneHandle);
            Assert.AreEqual(fallbackBefore, service.BundleFallbackCount,
                "遷移の間も全 Scene 検索へ落ちない（採取・活動の開閉・到着の確定すべて束経由）。");
        }

        // ---------------------------------------------------------------- 補助

        /// <summary>
        /// 最小の擬似 Area を作る。<b>常駐の初期化は通さない。</b>
        /// ここで見たいのは索引の分離であって、2 Area 同時の初期化ではない
        /// （それは活動隔離が入る工程の仕事）。
        /// </summary>
        private AreaRuntimeBundle CreateStubBundle(Scene scene)
        {
            var definition = ScriptableObject.CreateInstance<AreaDefinition>();
            _spawned.Add(definition);
            SetPrivate(definition, "_id", new StableId("area_p55_stub"));
            var entryDefinition = new AreaEntryDefinition();
            entryDefinition.EditorSet(new StableId("area_p55_stub_start"), CardinalDirection.North);
            definition.EditorSet(
                "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity",
                0,
                new List<AreaEntryDefinition> { entryDefinition },
                new StableId("area_p55_stub_start"));

            var rootGo = new GameObject("StubAreaRoot");
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            AreaRoot root = rootGo.AddComponent<AreaRoot>();
            root.EditorSet(definition, new List<AreaEntryPoint>());
            AreaContext context = rootGo.AddComponent<AreaContext>();
            AreaRuntimeBundle bundle = rootGo.AddComponent<AreaRuntimeBundle>();
            bundle.EditorSet(root, context);

            // 活動ゲートが閉じた状態の見立て：非 Active な根の下に部品を置く。
            var gameplayRoot = new GameObject("StubGameplayRoot");
            SceneManager.MoveGameObjectToScene(gameplayRoot, scene);
            var entryGo = new GameObject("StubEntry");
            entryGo.transform.SetParent(gameplayRoot.transform, false);
            entryGo.AddComponent<AreaEntryPoint>();
            gameplayRoot.SetActive(false);

            return bundle;
        }

        private IEnumerator WaitForCompleted(AreaTransitionCoordinator coordinator, int expected)
        {
            const float limit = 20f;
            float started = Time.realtimeSinceStartup;
            while (coordinator.CompletedCount < expected)
            {
                if (Time.realtimeSinceStartup - started > limit)
                {
                    Assert.Fail("遷移が " + limit + " 秒で完了しませんでした（完了回数="
                        + coordinator.CompletedCount + "／期待=" + expected + "）。");
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

            _bootstrap = new GameObject("BootstrapRoot_P55Test");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.HasInstance, "BootstrapRoot が生成されていること。");
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐サービスの初期化が成立していること。理由=" + BootstrapRoot.Instance.BootstrapFailure);

            DestroyTrialLaunchers();
        }

        private static void SetPrivate(object target, string field, object value)
        {
            for (System.Type t = target.GetType(); t != null; t = t.BaseType)
            {
                System.Reflection.FieldInfo f = t.GetField(
                    field,
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail("フィールドが見つかりません: " + field);
        }
    }
}
