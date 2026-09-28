using System.Collections;
using Momotaro.Gameplay.Companion;
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
using Momotaro.Presentation.Transition;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 実 Rig でのスライド（P5.5 仕様書 §7.1／付録 A.4。工程 P55-04a。§11 の E05 の一部）。
    ///
    /// EditMode が見るのは進み方の数値。ここで見るのは<b>実カメラへの書込が 1 系統か</b>と、
    /// <b>終わったあとに跳ね返らないか</b>——どちらも実 Rig と実 Scene が無いと言えない。
    ///
    /// <b>まだ遷移とは結線していない</b>（P55-04b）。ここではスライドを直接始めて、
    /// 演出そのものの性質を固定する。
    /// </summary>
    public sealed class P55CameraSlidePlayTests
    {
        private const string P55AreaAScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity";
        private const string P55TrialScene =
            "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_ConnectionTrial.unity";

        private GameObject _bootstrap;
        private AreaTransitionDisplayProxySet _proxies;

        [SetUp]
        public void SetUp()
        {
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
            if (_proxies != null && !_proxies.IsReleased)
            {
                _proxies.Release();
            }

            _proxies = null;

            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            yield return SceneManager.LoadSceneAsync(P55TrialScene, LoadSceneMode.Single);
            DestroyLaunchers();
            yield return null;
            DestroyLaunchers();

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
            yield return null;
        }

        // ---------------------------------------------------------------- 書込の一系統化

        /// <summary>
        /// <b>期間中はスライド担当だけが Rig 位置を書く</b>（付録 A.4）。
        ///
        /// 通常追従は毎フレーム書いているので、止めないと同じフレームに 2 人が書く。
        /// どちらが見えているのか後から言えなくなるし、追従が勝てば演出が消える。
        /// <b>書込回数を別々に数えて</b>、期間中の追従の書込が 0 であることを見る——
        /// 「見た目が正しい」では、たまたま順序が良かっただけを合格にしてしまう。
        ///
        /// 回転・投影・<c>orthographicSize</c> を触らないこと（§7.1）も併せて見る。
        /// </summary>
        [UnityTest]
        public IEnumerator DuringTheSlide_OnlyTheSlideWritesTheRigPosition()
        {
            yield return EnterArea();

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            AreaCameraRig rig = host.Rig;
            Camera camera = host.Camera;

            // 前提：通常追従が書いている状態から始める。
            rig.ResetWriteCountsForTests();
            yield return null;
            Assert.Greater(rig.FollowWriteCount, 0, "前提：通常追従が Rig を書いている。");

            Quaternion rotationBefore = camera.transform.rotation;
            float sizeBefore = camera.orthographicSize;
            bool orthographicBefore = camera.orthographic;

            Vector3 from = rig.transform.position;
            Vector3 to = from + new Vector3(12f, 0f, 0f);

            rig.ResetWriteCountsForTests();
            Assert.IsTrue(host.BeginSlide(to, AreaCameraSlide.DefaultSeconds), "スライドを始められる。");
            Assert.IsTrue(host.IsSliding);
            Assert.IsTrue(rig.FollowSuspended, "通常追従の書込を止めている（付録 A.4）。");

            int frames = 0;
            while (host.TickSlide(1f / 60f) && frames < 200)
            {
                frames++;
                yield return null; // 実フレームを回す（通常追従が動く機会を与える）。
                Assert.AreEqual(0, rig.FollowWriteCount,
                    "期間中に通常追従が 1 度も書かない（frame " + frames + "）。");
            }

            Assert.Greater(frames, 10, "途中のフレームが存在している。");
            Assert.AreEqual(0, rig.FollowWriteCount, "最後まで追従は書かない。");
            Assert.Greater(rig.SlideWriteCount, 10, "書いていたのはスライド担当。");

            host.EndSlide();
            Assert.AreEqual(to.x, rig.transform.position.x, 0.001f, "終点へ厳密に着く。");
            Assert.AreEqual(to.z, rig.transform.position.z, 0.001f);

            Assert.AreEqual(rotationBefore, camera.transform.rotation, "回転は変えない（§7.1）。");
            Assert.AreEqual(sizeBefore, camera.orthographicSize, "orthographicSize も変えない。");
            Assert.AreEqual(orthographicBefore, camera.orthographic, "投影も変えない。");
        }

        // ---------------------------------------------------------------- 跳ね返り

        /// <summary>
        /// 終わったあと<b>翌フレームに跳ね返らない</b>（付録 A.4／A.7）。
        ///
        /// 終点だけ合わせて追従の内部状態を放っておくと、次の <c>Tick</c> で
        /// 前の値へ引き戻される。<b>数フレーム見る</b>のが要点——終わった直後の 1 枚だけでは
        /// 「1 フレーム遅れて戻る」を見逃す（記録 013 の教訓）。
        ///
        /// 出発 Area に結び付いたままなので通常追従は<b>止まったまま</b>が正しい。
        /// 戻すのは結び直しと入口配置が済んだとき（付録 A.10）。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterTheSlide_ThePositionDoesNotSpringBack()
        {
            yield return EnterArea();

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            AreaCameraRig rig = host.Rig;

            Vector3 to = rig.transform.position + new Vector3(12f, 0f, 0f);
            host.BeginSlide(to, 0.1f);
            while (host.TickSlide(1f / 60f))
            {
                yield return null;
            }

            host.EndSlide();
            Vector3 atEnd = rig.transform.position;

            // <b>「適用待ち」ではなく「結び直し待ち」である。</b>
            // 適用待ちは「結び先が変わった」ことの印なので、出発 Area に結び付いたまま
            // それを立てると、次のフレームに即時配置が走って出発側へ引き戻される
            // （実際に踏んだ：終点から 12m 戻った）。
            Assert.IsTrue(host.IsHoldingAfterSlide,
                "結び直しを待って終点に留まっている（付録 A.4／A.10）。");
            Assert.IsFalse(host.ArrivalPending,
                "適用待ちにはしない（出発 Area は準備済みなので、即時配置が走ってしまう）。");
            Assert.IsTrue(rig.FollowSuspended, "通常追従は止まったまま（出発側へ引き戻さない）。");

            for (int i = 0; i < 6; i++)
            {
                yield return null;
                Assert.Less(Vector3.Distance(atEnd, rig.transform.position), 0.01f,
                    "frame " + i + "：終点から動かない（跳ね返らない）。実際=" + rig.transform.position);
            }
        }

        // ---------------------------------------------------------------- 表示代理と同じ進行度

        /// <summary>
        /// 表示代理は<b>カメラと同じ進行度</b>で運ばれる（§7.2）。
        ///
        /// 別の時計で動かすと、同じ 0.45 秒でも端でずれて「主人公だけ先に着く」ように見える。
        /// ここでは<b>毎フレーム</b>、代理の進み具合とカメラの進み具合が一致することを見る。
        /// 端だけ見ると、途中で別の曲線を通っていても通ってしまう。
        /// </summary>
        [UnityTest]
        public IEnumerator TheDisplayProxy_TravelsOnTheSameProgressAsTheCamera()
        {
            yield return EnterArea();

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            AreaCameraRig rig = host.Rig;

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            var companion = Object.FindFirstObjectByType<CompanionActor>();
            _proxies = new AreaTransitionDisplayProxySet();
            _proxies.Build(player, companion);

            Vector3 playerFrom = player.transform.position;
            Vector3 playerTo = playerFrom + new Vector3(12f, 0f, 0f);
            _proxies.SetRoute(playerFrom, playerTo, playerFrom, playerTo);

            Vector3 cameraTo = rig.transform.position + new Vector3(12f, 0f, 0f);
            host.BeginSlide(cameraTo, AreaCameraSlide.DefaultSeconds);

            int frames = 0;
            while (true)
            {
                bool running = host.TickSlide(1f / 60f);

                // <b>カメラの進行度をそのまま配る</b>（自前の時計を持たせない）。
                _proxies.SetProgress(host.Slide.Eased);
                _proxies.TickDisplayClock(1f / 60f);

                float expectedX = Mathf.LerpUnclamped(playerFrom.x, playerTo.x, host.Slide.Eased);
                Assert.AreEqual(expectedX, _proxies.Player.transform.position.x, 0.001f,
                    "frame " + frames + "：代理はカメラと同じ進行度の位置に居る。");

                if (!running)
                {
                    break;
                }

                frames++;
                yield return null;
                Assert.Less(frames, 200, "終わらないスライド。");
            }

            host.EndSlide();
            _proxies.SetProgress(1f);

            Assert.AreEqual(playerTo.x, _proxies.Player.transform.position.x, 0.001f,
                "代理も厳密に到着位置へ着く。");
            Assert.Greater(frames, 10, "途中のフレームで比べている。");
        }

        // ---------------------------------------------------------------- 留まりの解け方

        /// <summary>
        /// 留まりは<b>結び直しで解ける</b>（付録 A.4／A.10）。
        ///
        /// スライドの終点に留まったままでは、到着後に操作できない。
        /// 活動 Area が入れ替わって入口配置が終わったら、通常追従へ戻る必要がある。
        /// <b>ここが P55-04b との継ぎ目</b>——実際の Commit はまだ無いので、
        /// 従来の遷移で結び直しを起こして、留まりが解けることだけを確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator TheHoldAfterTheSlide_IsReleasedWhenTheAreaIsRebound()
        {
            yield return EnterArea();

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            AreaCameraRig rig = host.Rig;

            host.BeginSlide(rig.transform.position + new Vector3(12f, 0f, 0f), 0.1f);
            while (host.TickSlide(1f / 60f))
            {
                yield return null;
            }

            host.EndSlide();
            Assert.IsTrue(host.IsHoldingAfterSlide, "前提：終点に留まっている。");

            // 活動 Area を入れ替える（従来経路。演出とは結線していない）。
            AreaTransitionService service = BootstrapServices.Get<AreaTransitionService>();
            Assert.IsNotNull(service);
            AreaTransitionDecision decision = service.TryTravel(
                new Momotaro.Core.Identification.StableId("area_p55_b"),
                new Momotaro.Core.Identification.StableId("area_p5_b_from_a"));
            Assert.IsTrue(decision.Accepted, "遷移が受理される。理由=" + decision.Rejection);

            float deadline = Time.realtimeSinceStartup + 20f;
            while (service.CompletedCount < 1 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, service.CompletedCount, "到着する。");
            yield return null;
            yield return null;

            AreaCameraRigHost arrived = AreaCameraRigHost.Instance;
            Assert.IsFalse(arrived.IsHoldingAfterSlide, "結び直しで留まりが解ける。");
            Assert.IsFalse(arrived.ArrivalPending, "到着の適用も済んでいる。");
            Assert.IsFalse(arrived.Rig.FollowSuspended, "通常追従が復帰している。");
            Assert.AreEqual("area_p55_b", arrived.BoundArea.Value, "結び先は B。");

            // 復帰したあとも跳ねない（数フレーム見る）。
            Vector3 settled = arrived.Rig.transform.position;
            for (int i = 0; i < 4; i++)
            {
                yield return null;
                Assert.Less(Vector3.Distance(settled, arrived.Rig.transform.position), 0.15f,
                    "frame " + i + "：復帰後も跳ねない。");
            }
        }

        // ---------------------------------------------------------------- 打ち切り

        /// <summary>
        /// 打ち切ると<b>出発位置へ戻り</b>、通常追従が復帰する（§8）。
        /// 失敗した遷移で到着側の位置を見せない。
        /// </summary>
        [UnityTest]
        public IEnumerator CancellingTheSlide_ReturnsToTheDeparturePositionAndResumesFollow()
        {
            yield return EnterArea();

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            AreaCameraRig rig = host.Rig;

            Vector3 from = rig.transform.position;
            Vector3 to = from + new Vector3(12f, 0f, 0f);
            host.BeginSlide(to, AreaCameraSlide.DefaultSeconds);
            host.TickSlide(0.1f);
            yield return null;

            Assert.Greater(rig.transform.position.x, from.x + 0.1f, "前提：途中まで動いている。");

            host.CancelSlide();

            Assert.IsFalse(host.IsSliding, "打ち切れている。");
            Assert.AreEqual(from.x, rig.transform.position.x, 0.001f, "出発位置へ戻る。");
            Assert.IsFalse(rig.FollowSuspended, "通常追従が復帰する（その場で遊べる状態へ）。");

            yield return null;
            Assert.Greater(rig.FollowWriteCount, 0, "追従が再び書いている。");
        }

        // ---------------------------------------------------------------- 補助

        private IEnumerator EnterArea()
        {
            DestroyLaunchers();
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P55SlideTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐が立つ。理由=" + BootstrapRoot.Instance.BootstrapFailure);
            DestroyLaunchers();

            yield return SceneManager.LoadSceneAsync(P55AreaAScene, LoadSceneMode.Single);
            yield return null;

            var initializer = Object.FindFirstObjectByType<AreaInitializer>();
            Assert.IsNotNull(initializer);
            Assert.IsTrue(initializer.Initialized, "初期化が成立する。理由=" + initializer.FailureReason);

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");

            // 置き直しの補間を終わらせてから始める（演出の話に補間の残りを混ぜない）。
            float deadline = Time.realtimeSinceStartup + 3f;
            while ((host.ArrivalPending || host.Rig.Blend.IsBlending)
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(host.ArrivalPending, "前提：到着の適用が済んでいる。");
            yield return null;
        }

        private static void DestroyLaunchers()
        {
            foreach (Phase5TrialLauncher launcher in
                Object.FindObjectsByType<Phase5TrialLauncher>(FindObjectsSortMode.None))
            {
                if (launcher != null)
                {
                    Object.DestroyImmediate(launcher.gameObject);
                }
            }
        }
    }
}
