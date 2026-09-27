using System.Collections;
using Momotaro.Core.Identification;
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
    /// 活動ゲートを実 Scene で通す（P5.5 仕様書 §4.2。工程 P55-02c）。
    ///
    /// <b>ここが §4.2 の本題。</b> Builder が出荷する Scene は<b>閉じた状態で保存されている</b>ので、
    /// 先読みで読み込んでも <c>Awake</c>／<c>OnEnable</c> の副作用が一切走らない——
    /// Provider の奪い合いも、登録簿への混入も起きない。
    /// 「読み込んでから Find して無効化する」では間に合わないことの裏返しを、実 Scene で確かめる。
    ///
    /// 既定は「読み込んだらすぐ開ける」。既存の単一 Area 構成（P3.5／P4／P5）が
    /// 従来どおり動くのはそのため。
    /// </summary>
    public sealed class P55ActivityGatePlayTests
    {
        private const string AreaAScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity";
        private const string TrialScene =
            "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_ExplorationTrial.unity";

        private static readonly StableId AreaA = new StableId("area_p5_a");
        private static readonly StableId AreaB = new StableId("area_p5_b");

        private GameObject _bootstrap;

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
            // <b>要求を残さない。</b> 残すと次のテストが読む Scene が閉じたまま起動する。
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
            GameModeProvider.Current = null;
            GameSessionProvider.Current = null;
            GameplayClockProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            AreaPendingArrival.Clear();
            yield return null;
        }

        // ---------------------------------------------------------------- 既定（直開き）

        /// <summary>
        /// 先読みを頼まれていなければ、<b>読み込んだその場で開く</b>。
        /// 出荷状態は閉じているのに、既存の単一 Area 構成が従来どおり動くのはこのため（§4.2）。
        /// </summary>
        [UnityTest]
        public IEnumerator WithoutAStagingRequest_TheGateOpensOnLoad()
        {
            yield return CreateBootstrap();
            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;

            AreaActivityGate gate = FindGate();
            Assert.IsTrue(gate.IsOpen, "その場で開く。");
            Assert.IsTrue(gate.OpenedOnLoad, "開けたのは読み込み時。");
            Assert.AreEqual(AreaA, gate.AreaId);

            for (int i = 0; i < gate.GatedRoots.Count; i++)
            {
                Assert.IsTrue(gate.GatedRoots[i].activeSelf, "Gameplay が動き出している。");
            }

            Assert.IsTrue(FindInitializer().Initialized, "初期化も従来どおり成立する。");
            Assert.Greater(AreaInteractableRegistry.Count, 0, "仕掛けが登録簿に載っている。");
        }

        // ---------------------------------------------------------------- 先読み

        /// <summary>
        /// 名指しで先読みを頼めば、読み込んでも<b>何も起きない</b>（§4.2）。
        /// Gameplay は止まったまま、登録簿にも載らない。そのあと開ければ動き出す。
        /// </summary>
        [UnityTest]
        public IEnumerator WithAStagingRequest_NothingWakesUpUntilTheGateOpens()
        {
            yield return CreateBootstrap();
            Assert.IsTrue(AreaStagingRequest.TryRequest(AreaA), "前提：この Area を名指しで頼む。");

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;

            AreaActivityGate gate = FindGate();
            Assert.IsFalse(gate.IsOpen, "頼んだ Area は閉じたまま待つ。");
            Assert.IsFalse(gate.OpenedOnLoad);
            Assert.AreEqual(1, AreaStagingRequest.ConsumedCount, "要求は消費されている。");
            Assert.IsFalse(AreaStagingRequest.IsRequested, "使い切って残さない。");

            for (int i = 0; i < gate.GatedRoots.Count; i++)
            {
                Assert.IsFalse(gate.GatedRoots[i].activeSelf, "Gameplay は動いていない。");
            }

            // <b>ここが §4.2 の要点。</b> 読み込んだのに登録簿が空＝OnEnable が一度も走っていない。
            // 「読み込んでから Find して無効化する」形では、この時点でもう登録が済んでいる。
            Assert.AreEqual(0, AreaInteractableRegistry.Count, "仕掛けが登録簿に載っていない。");
            Assert.AreEqual(0, PerceptionTargetRegistry.Count, "索敵対象も載っていない。");

            // 束は活動ゲートの外なので、閉じている間も在処が分かる（§4.3）。
            Assert.IsTrue(AreaBundleDirectory.TryGetSingle(out AreaRuntimeBundle bundle),
                "閉じていても束は索引から引ける。");
            Assert.AreSame(gate, bundle.ActivityGate, "束からゲートへ辿れる。");

            // ---- 開ける ----
            gate.Open();
            yield return null;

            Assert.IsTrue(gate.IsOpen);
            Assert.Greater(AreaInteractableRegistry.Count, 0, "開ければ登録が走る。");
            Assert.IsTrue(FindInitializer().Initialized, "初期化もここから始まる。");
        }

        /// <summary>
        /// 宛先違いの要求は<b>素通り</b>させ、要求はそのまま残す（§4.2）。
        ///
        /// 無条件フラグにすると、要求したのに読まれなかったとき（開始失敗・タイムアウト）に
        /// 次の直開きが閉じたまま起動する。「何も動かないゲーム」は一番追いにくい壊れ方。
        /// </summary>
        [UnityTest]
        public IEnumerator AStagingRequestForAnotherArea_DoesNotHoldThisOneBack()
        {
            yield return CreateBootstrap();
            Assert.IsTrue(AreaStagingRequest.TryRequest(AreaB), "前提：別の Area を頼む。");

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;

            AreaActivityGate gate = FindGate();
            Assert.IsTrue(gate.IsOpen, "宛先違いなので通常どおり開く。");
            Assert.IsTrue(AreaStagingRequest.IsRequested, "要求は残る（頼んだ Area のために取っておく）。");
            Assert.AreEqual(AreaB, AreaStagingRequest.AreaId);
            Assert.AreEqual(1, AreaStagingRequest.MismatchCount);
            Assert.AreEqual(0, AreaStagingRequest.ConsumedCount);
            Assert.IsTrue(FindInitializer().Initialized);
        }

        // ---------------------------------------------------------------- 補助

        private static AreaActivityGate FindGate()
        {
            var found = Object.FindFirstObjectByType<AreaActivityGate>(FindObjectsInactive.Include);
            Assert.IsNotNull(found, "活動ゲートが Scene にありません（Builder が置く）。");
            return found;
        }

        private static AreaInitializer FindInitializer()
        {
            var found = Object.FindFirstObjectByType<AreaInitializer>(FindObjectsInactive.Include);
            Assert.IsNotNull(found, "AreaInitializer が Scene にありません。");
            return found;
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

            _bootstrap = new GameObject("BootstrapRoot_P55GateTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.HasInstance, "BootstrapRoot が生成されていること。");
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐サービスの初期化が成立していること。理由=" + BootstrapRoot.Instance.BootstrapFailure);

            DestroyTrialLaunchers();
        }
    }
}
