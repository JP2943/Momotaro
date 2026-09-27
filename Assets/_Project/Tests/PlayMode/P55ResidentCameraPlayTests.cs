using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Cameras;
using Momotaro.Data.World;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 単一常駐 CameraRig の受入（P5.5 仕様書 §4.3／§7.1／付録 A。工程 P55-03b）。
    ///
    /// 付録 A.7 が挙げた 5 点のうち、<b>実 Scene でしか言えないもの</b>をここで見る。
    /// <list type="bullet">
    /// <item><description><b>A／B の直開きが成立する</b>——どちらから入っても常駐 Rig が
    ///   一度だけ立ち、その Area の領域と結び付く（A.2 の ensure-create）。</description></item>
    /// <item><description><b>A→B→A で同一 Camera インスタンスが維持される</b>（A.5）。
    ///   Scene handle は往復で使い回されることがあるので、参照の同一性で見る。</description></item>
    /// <item><description><b>Commit 翌フレームに位置が跳ばない</b>（A.4／A.7）。</description></item>
    /// <item><description><b>Area Scene は表示系を持たない</b>（A.1）。個数ではなく
    ///   「Scene の中に居ないこと」で見る（§9.1）。</description></item>
    /// <item><description><b>揺れが常駐から解決できる</b>（A.2）。Scene 側に配線が無いので、
    ///   ここが切れていると手応えの揺れが静かに消える。</description></item>
    /// </list>
    ///
    /// 契約そのもの（索引・提供点・計算と適用の分離）は EditMode の
    /// <c>P55ResidentCameraTests</c> が見る。ここは実 Scene の起動経路だけを見る。
    /// </summary>
    public sealed class P55ResidentCameraPlayTests
    {
        private const string AreaAScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity";
        private const string AreaBScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaB.unity";
        private const string TrialScene =
            "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_ExplorationTrial.unity";

        private static readonly StableId AreaA = new StableId("area_p5_a");
        private static readonly StableId AreaB = new StableId("area_p5_b");
        private static readonly StableId AreaAFromB = new StableId("area_p5_a_from_b");
        private static readonly StableId AreaBFromA = new StableId("area_p5_b_from_a");

        private GameObject _bootstrap;
        private AreaResidencyLedger _ledger;
        private AreaPreloader _preloader;
        private readonly object _owner = new object();

        [SetUp]
        public void SetUp()
        {
            // 前のテストが残した常駐 CameraRig を持ち込まない（付録 A.2）。
            P55ResidentRig.Reset();

            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
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
                float waited = 0f;
                while (_preloader.Phase == AreaPreloadPhase.Releasing && waited < 6f)
                {
                    _preloader.Poll();
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
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

            // Scene から抜けた<b>あと</b>に常駐を消す（先に消すと、まだ載っている Area の
            // 領域集合が「常駐が居ない」と見て作り直す）。
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

        // ---------------------------------------------------------------- 直開き（付録 A.2／A.7）

        /// <summary>
        /// <b>B の直開きでも常駐 Rig が一度だけ立つ</b>（付録 A.2／A.7）。
        ///
        /// A の直開きは P5-P17 が見ているので、ここは<b>入口が A でない</b>場合を見る。
        /// 生成主体を Area 側の領域集合部品に置いた理由がここに効く——どの Area から
        /// 入っても同じ 1 本の経路を通るので、B から始めても成立する。
        /// </summary>
        [UnityTest]
        public IEnumerator OpeningAreaBDirectly_StandsUpTheResidentRigOnce()
        {
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            Assert.AreEqual(0, AreaCameraRigHost.CreatedCount, "前提：まだ常駐は立っていない。");

            yield return SceneManager.LoadSceneAsync(AreaBScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized, "前提：B の初期化が成立している。");

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "B から入っても常駐 Rig が立つ（付録 A.2）。");
            Assert.AreEqual(1, AreaCameraRigHost.CreatedCount, "一度だけ生成する。");
            Assert.IsTrue(host.IsWired, "B の領域集合と結び付いている。");
            Assert.AreEqual(AreaB.Value, host.BoundArea.Value, "結び先は B。");
            Assert.AreSame(host, AreaCameraOwnerProvider.Current, "提供点は常駐が所有する。");

            Assert.AreEqual(1, EnabledCount<Camera>(), "描く Camera は 1 台（付録 A.7）。");
            Assert.AreEqual(1, EnabledCount<AudioListener>(), "聞く AudioListener は 1 つ。");
            Assert.AreSame(host.Camera, Camera.main, "主カメラは常駐のもの。");
            Assert.AreSame(host.Rig.transform, host.Camera.transform.parent,
                "Camera は Rig の子（追従と揺れで書込み先を分ける。§11）。");

            Assert.GreaterOrEqual(host.Rig.SnapCount, 1, "到着で即時配置している（§11）。");
            Assert.IsFalse(host.Rig.Blend.IsBlending, "直開きの直後に補間が走っていない。");

            // 揺れの解決（付録 A.2）。Scene 側に配線が無いので、ここが切れると静かに揺れなくなる。
            Assert.AreSame(host.Shake, CameraShakeProvider.Current, "揺れも常駐が差している。");
            foreach (CombatFeedbackPresenter presenter in
                Object.FindObjectsByType<CombatFeedbackPresenter>(FindObjectsSortMode.None))
            {
                Assert.IsNull(presenter.CameraShake, "Area Scene 側には配線しない。");
                Assert.AreSame(host.Shake, presenter.ResolvedCameraShake,
                    "演出の調停役は常駐の揺れへ解決できる。");
            }
        }

        // ---------------------------------------------------------------- 往復（付録 A.5／A.7）

        /// <summary>
        /// <b>A→B→A で同じ Camera インスタンスが生き続ける</b>（付録 A.5／A.7）。
        ///
        /// <b>Scene handle で「同じ Area か」を判断してはいけない。</b>
        /// Single 読込で A を捨てて B を載せると Unity は<b>同じ handle を使い回す</b>ことがあり、
        /// handle だけで見ると「すでに結び付いている」と誤判定して
        /// 破棄済みの追従対象を持ち続ける（実際に踏んだ：B 到着後も結び先が A のまま、
        /// <c>IsWired</c> が false）。往復させるとその取り違えが必ず出る。
        ///
        /// あわせて<b>Commit 翌フレームに跳ねない</b>ことも見る（付録 A.4 の「追従の内部状態も
        /// 終点へ同期」）。同期していないと、到着位置から元の位置へ 1 フレームだけ戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator RoundTrip_KeepsTheSameCameraAndDoesNotJumpTheFrameAfterCommit()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized, "前提：A の初期化が成立している。");

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host);
            Camera camera = host.Camera;
            AreaCameraRig rig = host.Rig;
            AreaCameraRegionSet setA = null;
            Assert.IsTrue(AreaCameraRegionSetRegistry.TryGetSingle(out setA), "前提：A の領域集合が引ける。");
            Assert.AreEqual(AreaA.Value, host.BoundArea.Value, "前提：A へ結び付いている。");

            AreaTransitionService service = Transitions();

            // ---- A → B ----
            yield return Travel(service, AreaB, AreaBFromA, 1);

            Assert.AreEqual(1, AreaCameraRigHost.CreatedCount, "往路で作り直さない。");
            Assert.AreSame(camera, host.Camera, "同じ Camera インスタンス（付録 A.5）。");
            Assert.AreSame(rig, host.Rig, "同じ Rig。");
            Assert.AreEqual(AreaB.Value, host.BoundArea.Value, "結び先が B へ入れ替わる。");
            Assert.IsTrue(host.IsWired, "B の領域集合と結び付いている。");
            Assert.IsTrue(AreaCameraRegionSetRegistry.TryGetSingle(out AreaCameraRegionSet setB));
            Assert.AreNotSame(setA, setB, "領域集合は Area ごとに別の実体。");
            Assert.AreEqual(1, EnabledCount<Camera>(), "B でも描く Camera は 1 台。");

            PlayerRoot playerB = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(playerB, "B に主人公が居る。");
            yield return AssertDoesNotJumpNextFrame(rig, playerB.transform, "B 到着");

            // ---- B → A（handle の使い回しが出るのはここ）----
            yield return Travel(service, AreaA, AreaAFromB, 2);

            Assert.AreEqual(1, AreaCameraRigHost.CreatedCount, "復路でも作り直さない。");
            Assert.AreSame(camera, host.Camera, "往復しても同じ Camera（付録 A.7）。");
            Assert.AreSame(camera, Camera.main, "主カメラも同じ実体のまま。");
            Assert.AreEqual(AreaA.Value, host.BoundArea.Value, "結び先が A へ戻る。");
            Assert.IsTrue(host.IsWired, "戻った A の領域集合と結び付いている（破棄済みを掴んでいない）。");
            Assert.IsTrue(AreaCameraRegionSetRegistry.TryGetSingle(out AreaCameraRegionSet setA2));
            Assert.AreNotSame(setA, setA2, "同じ Area でも実体は作り直される（§4.3）。");
            Assert.AreSame(setA2.FollowTarget, PrivateTarget(rig),
                "追従対象も戻った A のものへ差し替わっている。");
            Assert.AreEqual(1, EnabledCount<Camera>(), "戻っても描く Camera は 1 台。");

            PlayerRoot playerA = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(playerA, "戻った A に主人公が居る。");
            yield return AssertDoesNotJumpNextFrame(rig, playerA.transform, "A 復帰");
        }

        // ---------------------------------------------------------------- 初回配置の時機（付録 A.10）

        /// <summary>
        /// <b>保存位置と到着位置が別の領域にあっても、最初の描画で到着位置に居る</b>（付録 A.10）。
        ///
        /// A の主人公は<b>既定入口（西の大部屋）</b>の位置で保存されている。そこへ
        /// 「東の通路の入口へ着く」要求を入れて直開きすると、保存位置と到着位置が
        /// <b>別のカメラ領域</b>になる——常駐 Rig が結び付けた瞬間に即時配置してしまうと、
        /// 西を基準に置いたあと東へ移った主人公を追って<b>部屋を跨ぐ補間が始まる</b>。
        ///
        /// だから見るのは最後の 1 枚ではなく<b>最初の何フレームか全部</b>：
        /// その間 1 度も補間が走らず、即時配置は 1 回だけであること。
        /// 入口配置の前に置いてしまう実装では、ここで補間が立つ。
        /// </summary>
        [UnityTest]
        public IEnumerator DirectOpen_PlacesTheCameraAtTheEntryEvenWhenTheSavedPositionIsInAnotherRegion()
        {
            AssertSceneRegistered(AreaAScene);
            yield return CreateBootstrap();

            // 到着要求を入れておく（遷移サービスを通さず、到着先だけを指定する）。
            // 活動許可は出ないが、入口配置と準備完了の報告は行われる——カメラの検査には足りる。
            AreaPendingArrival.Set(9901, AreaA, AreaAFromB);

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);

            // <b>最初に描かれるフレームから見る。</b> 端の 1 枚だけ見ると、
            // 「一度跳んでから戻った」を見逃す。
            for (int frame = 0; frame < 8; frame++)
            {
                yield return new WaitForEndOfFrame();

                AreaCameraRigHost watching = AreaCameraRigHost.Instance;
                if (watching == null || watching.Rig == null)
                {
                    continue; // まだ立っていない。
                }

                Assert.IsFalse(watching.Rig.Blend.IsBlending,
                    "frame " + frame + "：一度も補間が走らない（保存位置から滑ってこない）。"
                    + " 位置=" + watching.Rig.transform.position);
                Assert.LessOrEqual(watching.Rig.SnapCount, 1,
                    "frame " + frame + "：即時配置は 1 回だけ（置き直していない）。");
            }

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");
            Assert.IsFalse(host.ArrivalPending, "適用が済んでいる（保留が残っていない）。");
            Assert.IsFalse(host.Rig.FollowSuspended, "通常追従が戻っている。");
            Assert.AreEqual(1, host.Rig.SnapCount, "即時配置は 1 回。");

            AreaInitializer initializer = FindInitializer();
            Assert.IsTrue(initializer.Initialized, "前提：初期化が成立している。理由=" + initializer.FailureReason);

            // 前提：保存位置（既定入口）と到着位置（東の入口）が別の領域である。
            // これが崩れていると、この検査は何も見ていない。
            Assert.IsTrue(AreaCameraRegionSetRegistry.TryGetSingle(out AreaCameraRegionSet set));
            AreaRoot areaRoot = Object.FindFirstObjectByType<AreaRoot>();
            Assert.IsNotNull(areaRoot);
            Vector3 savedSpot = EntryPosition(areaRoot, areaRoot.Definition.DefaultEntryId);
            Vector3 arrivalSpot = EntryPosition(areaRoot, AreaAFromB);
            Assert.IsTrue(set.TryResolveRegion(savedSpot, out CameraRegionDefinition savedRegion));
            Assert.IsTrue(set.TryResolveRegion(arrivalSpot, out CameraRegionDefinition arrivalRegion));
            Assert.AreNotEqual(savedRegion.RegionId.Value, arrivalRegion.RegionId.Value,
                "前提：保存位置と到着位置は別のカメラ領域（" + savedRegion.RegionId.Value + "）。");

            PlayerRoot player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");
            Assert.AreEqual(arrivalRegion.RegionId.Value, host.Rig.CurrentRegion.RegionId.Value,
                "カメラが見ている領域は到着側（東の通路）。");
            AssertPinnedToTarget(host.Rig, player.transform, "直開き（別領域の入口）");
        }

        // ---------------------------------------------------------------- 移動先の到着点（付録 A.10）

        /// <summary>
        /// <b>A を追従したまま B の到着点を計算できる</b>（付録 A.3／A.10）。
        ///
        /// スライドの終点を決めるには、Commit の<b>前に</b>移動先の到着点が要る。
        /// 「先に Bind を B へ切り替えて測る」やり方は使えない——切り替えた瞬間に
        /// 領域と追従対象が入れ替わり、§7.1 の「事前に境界位置へ瞬間移動させない」が破れる。
        ///
        /// ここで固めるのは 2 つ。計算が<b>B の領域</b>を使うこと（A の領域で clamp しない）、
        /// そして計算しても<b>Camera 位置・結び先・配置回数・適用回数が 1 つも変わらない</b>こと。
        /// </summary>
        [UnityTest]
        public IEnumerator ComputingTheDestinationArrivalPoint_UsesTheDestinationRegionsAndChangesNothing()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized, "前提：A の初期化が成立している。");

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host);
            Assert.AreEqual(AreaA.Value, host.BoundArea.Value, "前提：A へ結び付いている。");

            // B を先読みする（載っているが活動していない＝スライド前の状態）。
            _ledger = new AreaResidencyLedger();
            AreaInstanceHandle activeHandle = _ledger.NextHandle(AreaA);
            Assert.IsTrue(_ledger.TryAdmitStaged(activeHandle));
            Assert.IsTrue(_ledger.TrySetPhase(activeHandle, AreaActivationPhase.Active));
            _preloader = new AreaPreloader(_ledger, new UnityAreaSceneHost(), _owner);

            Assert.IsTrue(_preloader.Request(AreaB, AreaBScene));
            float waited = 0f;
            while (_preloader.Phase == AreaPreloadPhase.Loading && waited < 10f)
            {
                _preloader.Poll();
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase,
                "前提：B が載っている。理由=" + _preloader.FailureReason);

            Assert.IsTrue(
                AreaBundleDirectory.TryGetByScene(_preloader.StagedSceneHandle, out AreaRuntimeBundle staged));
            staged.BindInstance(_preloader.StagedArea);
            Assert.IsTrue(staged.Instance.IsValid, "前提：B の実体識別子が付いている。");

            // B の到着位置（A から入る入口）。
            AreaRoot bRoot = staged.Root;
            Assert.IsNotNull(bRoot, "B の根へ辿れる。");
            Vector3 arrivalInB = EntryPosition(bRoot, AreaBFromA);

            // B の領域集合から期待値を作る（同じ純粋関数で。絶対値を書かない）。
            Assert.IsTrue(
                AreaCameraRegionSetRegistry.TryGetByScene(staged.SceneHandle, out AreaCameraRegionSet setB),
                "B の領域集合を Scene handle で引ける。");
            Assert.IsTrue(setB.TryResolveRegion(arrivalInB, out CameraRegionDefinition regionInB));
            Assert.AreNotEqual(host.Rig.CurrentRegion.RegionId.Value, regionInB.RegionId.Value,
                "前提：B の領域はいま見ている A の領域とは別物（" + regionInB.RegionId.Value + "）。");
            Assert.IsTrue(host.Rig.TryGetHalfFootprint(out Vector2 half));
            Vector3 expected = CameraBoundsMath.ClampFocus(arrivalInB, regionInB, half);

            // ---- 計算する前の状態を覚える ----
            Vector3 cameraBefore = host.Rig.transform.position;
            Vector3 worldBefore = host.Camera.transform.position;
            string boundBefore = host.BoundArea.Value;
            int bindsBefore = host.BindCount;
            int snapsBefore = host.Rig.SnapCount;
            int appliesBefore = host.ApplyCount;
            Assert.IsTrue(host.TryComputeArrivalPoint(out Vector3 followingA),
                "前提：いまの結び先（A）の追従位置も取れる。");

            // ---- 移動先を明示して計算する ----
            Assert.IsTrue(host.TryComputeArrivalPoint(staged.Instance, arrivalInB, out Vector3 point),
                "B の到着点を計算できる。");

            Assert.AreEqual(expected.x, point.x, 0.001f, "B の領域で clamp した位置（期待=" + expected + "）。");
            Assert.AreEqual(expected.z, point.z, 0.001f);
            Assert.Greater(Vector3.Distance(point, followingA), 0.1f,
                "A の追従位置とは別の答えになる（結び先の値を返していない）。");

            // ---- 何も動いていないこと（§7.1）----
            Assert.AreEqual(cameraBefore, host.Rig.transform.position, "Rig の位置が変わらない。");
            Assert.AreEqual(worldBefore, host.Camera.transform.position, "Camera の世界位置も変わらない。");
            Assert.AreEqual(boundBefore, host.BoundArea.Value, "結び先が変わらない（B へ寄らない）。");
            Assert.AreEqual(bindsBefore, host.BindCount, "結び直しも起きない。");
            Assert.AreEqual(snapsBefore, host.Rig.SnapCount, "配置回数が増えない。");
            Assert.AreEqual(appliesBefore, host.ApplyCount, "適用回数も増えない。");
            Assert.IsFalse(host.Rig.Blend.IsBlending, "補間も始まらない。");

            // 無効な指定では当て推量しない。
            Assert.IsFalse(host.TryComputeArrivalPoint(AreaInstanceHandle.None, arrivalInB, out _),
                "実体識別子が無効なら計算しない。");
            Assert.IsFalse(
                host.TryComputeArrivalPoint(new AreaInstanceHandle(AreaB, 99), arrivalInB, out _),
                "載っていない実体でも計算しない（別世代を取り違えない）。");
        }

        // ---------------------------------------------------------------- Scene の持ち物（付録 A.1）

        /// <summary>
        /// <b>Area Scene は表示系を持たない</b>（付録 A.1／A.6）。
        ///
        /// 「活動中は各 1 つ」を全体の個数で見ると、2 Area 同時読込で
        /// <b>Scene ごとに 1 つずつ＝合計 2 つ</b>でも通ってしまう（§9.1）。
        /// そこでここでは Scene を特定して「その中に居ないこと」を見る。
        /// Scene 検査（Validator）と二重になるが、<b>実行時に本当に居ないか</b>は別の問いである——
        /// 実行時に生成して足す経路が入り込めば Scene 検査は気付かない。
        /// </summary>
        [UnityTest]
        public IEnumerator AreaScenes_OwnNoCameraOrListenerOrLightOfTheirOwn()
        {
            AssertSceneRegistered(AreaAScene);
            AssertSceneRegistered(AreaBScene);
            yield return CreateBootstrap();

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            Assert.IsTrue(FindInitializer().Initialized);
            AssertSceneOwnsNoVisuals(AreaAScene);

            yield return Travel(Transitions(), AreaB, AreaBFromA, 1);
            AssertSceneOwnsNoVisuals(AreaBScene);
        }

        private static void AssertSceneOwnsNoVisuals(string scenePath)
        {
            Assert.AreEqual(0, InScene<Camera>(scenePath), scenePath + "：Camera を持たない。");
            Assert.AreEqual(0, InScene<AudioListener>(scenePath), scenePath + "：AudioListener を持たない。");
            Assert.AreEqual(0, InScene<Light>(scenePath), scenePath + "：基準照明を持たない。");
            Assert.AreEqual(0, InScene<AreaCameraRig>(scenePath), scenePath + "：Rig を持たない。");
            Assert.AreEqual(0, InScene<AreaCameraRigHost>(scenePath),
                scenePath + "：常駐を焼き付けていない。");
            Assert.AreEqual(0, InScene<CameraShakePresenter>(scenePath), scenePath + "：揺れを持たない。");

            // 持たないものばかり数えると「Scene が空でも通る」ので、提供する側も見る。
            Assert.AreEqual(1, InScene<AreaCameraRegionSet>(scenePath),
                scenePath + "：領域集合は Area が提供する（§4.3）。");
        }

        // ---------------------------------------------------------------- 補助

        /// <summary>
        /// 到着の翌フレームに<b>カメラ側の理由で</b>跳ばないことを見る（付録 A.4／A.7）。
        ///
        /// 到着時に <c>SnapToTarget</c> で位置だけ合わせても、追従の内部状態（補間の現在値）が
        /// 前の Area の値のままだと、次の <c>Tick</c> でそこへ引き戻される。
        ///
        /// <b>座標の差で見てはいけない。</b> 到着直後の主人公はまだ静止していない
        /// （入口の当たりからの押し出しが数フレーム続く）。実測で B→A の復帰時に
        /// 主人公が 1 フレームで 0.43m 動き、カメラは正しく追っているのに落ちた。
        /// 見るべきは<b>カメラが追従先に張り付いているか</b>——両フレームで
        /// 「領域に収めた追従先」と一致していること、かつ補間が走っていないこと。
        /// </summary>
        private static IEnumerator AssertDoesNotJumpNextFrame(
            AreaCameraRig rig, Transform target, string label)
        {
            AssertPinnedToTarget(rig, target, label + "・到着フレーム");
            Assert.IsFalse(rig.Blend.IsBlending, label + "：到着の直後に補間が走っていない。");

            yield return null;

            Assert.IsFalse(rig.Blend.IsBlending, label + "：翌フレームも補間していない（滑ってこない）。");
            AssertPinnedToTarget(rig, target, label + "・翌フレーム");
        }

        /// <summary>Rig が「領域に収めた追従先」と一致していることを見る（期待値は同じ純粋関数から作る）。</summary>
        private static void AssertPinnedToTarget(AreaCameraRig rig, Transform target, string label)
        {
            Vector3 expected = CameraBoundsMath.ClampFocus(
                target.position, rig.CurrentRegion, rig.HalfFootprint());
            Assert.Less(Vector3.Distance(
                    new Vector3(expected.x, 0f, expected.z),
                    new Vector3(rig.transform.position.x, 0f, rig.transform.position.z)),
                0.05f,
                label + "：カメラは追従先に張り付いている。期待=" + expected
                + " 実際=" + rig.transform.position);
        }

        /// <summary>
        /// 遷移して到着を待つ。<b>待ったあとに 1 フレーム置く</b>——常駐 Rig は LateUpdate で
        /// 活動 Area の入れ替わりを拾うが、Coroutine は Update で再開するので、
        /// 同じフレームに観測すると「まだ前の Area に結び付いている」を掴む。
        /// 描画は LateUpdate のあとなので、これは遅れではない。
        /// </summary>
        private static IEnumerator Travel(
            AreaTransitionService service, StableId area, StableId entry, int expectedCompleted)
        {
            AreaTransitionDecision decision = service.TryTravel(area, entry);
            Assert.IsTrue(decision.Accepted, "遷移が受理される。理由=" + decision.Rejection);

            AreaTransitionCoordinator coordinator = service.Coordinator;
            Assert.IsNotNull(coordinator, "調停役が居る。");

            float waited = 0f;
            while (coordinator.CompletedCount < expectedCompleted && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(expectedCompleted, coordinator.CompletedCount,
                area.Value + " へ到着する（待ち " + waited + " 秒）。");

            yield return null;
        }

        /// <summary>その入口の世界座標（Scene の入口定義が正本）。</summary>
        private static Vector3 EntryPosition(AreaRoot areaRoot, StableId entryId)
        {
            Assert.IsTrue(areaRoot.TryGetEntryPoint(entryId, out AreaEntryPoint entry),
                "入口が Scene にありません: " + entryId.Value);
            return entry.transform.position;
        }

        /// <summary>有効な部品の数（非 Active な物体の下は数えない）。</summary>
        private static int EnabledCount<T>() where T : Behaviour
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int n = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].enabled && found[i].gameObject.activeInHierarchy)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// その Scene に載っている部品の数（<b>常駐を含めない</b>）。
        /// <c>FindObjectsByType</c> は <c>DontDestroyOnLoad</c> の常駐も拾うので、
        /// 「Area Scene には 0 個」を見るには Scene を特定して数える必要がある。
        /// </summary>
        private static int InScene<T>(string scenePath) where T : Component
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, "前提：Scene が読み込まれている: " + scenePath);

            int n = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                n += root.GetComponentsInChildren<T>(true).Length;
            }

            return n;
        }

        /// <summary>Rig が実際に掴んでいる追従対象（private な配線の照合用）。</summary>
        private static Transform PrivateTarget(AreaCameraRig rig)
        {
            System.Reflection.FieldInfo field = typeof(AreaCameraRig).GetField(
                "_target",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "AreaCameraRig._target が見つからない（名前が変わった）。");
            return field.GetValue(rig) as Transform;
        }

        private static AreaTransitionService Transitions()
        {
            AreaTransitionService service = BootstrapServices.Get<AreaTransitionService>();
            Assert.IsNotNull(service, "遷移サービスが常駐していません。");
            return service;
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
            AreaInitializer[] found = Object.FindObjectsByType<AreaInitializer>(FindObjectsSortMode.None);
            Assert.AreEqual(1, found.Length, "活動中の初期化担当は 1 つ。");
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

            _bootstrap = new GameObject("BootstrapRoot_P55ResidentCameraTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.HasInstance, "BootstrapRoot が生成されていること。");
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐サービスの初期化が成立していること。理由=" + BootstrapRoot.Instance.BootstrapFailure);

            DestroyTrialLaunchers();
        }
    }
}
