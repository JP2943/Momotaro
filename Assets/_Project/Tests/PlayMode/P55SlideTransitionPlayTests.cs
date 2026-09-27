using System.Collections;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Cameras;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P5.5 の<b>スライド遷移そのもの</b>（仕様書 §6.2／§7／§8。工程 P55-04b。§11 の P01／P02）。
    ///
    /// <b>Gate も Driver も完了通知もテストから直接叩かない</b>（§11 P01／P02 の書き方）。
    /// 主人公を出入口の手前へ置いて<b>実キーを押し続ける</b>だけで、
    /// 受理 → Additive 先読み → 隔離された Prepared → スライド → Commit → 旧 Area 撤去まで
    /// 出荷物の配線に任せる。
    ///
    /// <b>「着いた」だけを見ない。</b> 途中の不変条件——読み込まれた Area が 2 つ、到着側はまだ
    /// 遊べない、訪問済みはまだ付かない、カメラは接続軸だけを動く、表示代理が立っている——を
    /// 毎フレーム見る。ここを見ないと「暗転して着いた」でも通ってしまう。
    /// </summary>
    public sealed class P55SlideTransitionPlayTests
    {
        private const string P55AreaAScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity";
        private const string P55AreaBScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaB.unity";
        private const string P55TrialScene =
            "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_ConnectionTrial.unity";

        private static readonly StableId AreaA = new StableId("area_p55_a");
        private static readonly StableId AreaB = new StableId("area_p55_b");
        private static readonly StableId ExitAEast = new StableId("exit_p55_a_east");
        private static readonly StableId ExitBWest = new StableId("exit_p55_b_west");

        /// <summary>接続軸の Z（<c>Phase55WorldLayout.SeamZ</c>。Editor 側の定数は参照しない）。</summary>
        private const float SeamAxisZ = 0f;

        private GameObject _bootstrap;
        private Keyboard _keyboard;

        [SetUp]
        public void SetUp()
        {
            P55ResidentRig.Reset();
            RemoveStrayDevices();
            ResetStatics();
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
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

            ResetStatics();
            RemoveDevices();
            yield return null;
        }

        // ---------------------------------------------------------------- P01：東へ

        /// <summary>
        /// <b>P01</b>：A の東の出入口へ実キーで歩くと、<b>右へスライドして</b>B に着く（§11 P01）。
        ///
        /// 途中の不変条件を毎フレーム見る。
        /// <list type="bullet">
        /// <item><description>読み込まれた Area は 2 つ（Single ではない＝両方の地形が見えている）。</description></item>
        /// <item><description>到着側はまだ遊べない（<c>IsAreaReady</c> が false。§4.1）。</description></item>
        /// <item><description>訪問済みはまだ付かない（Commit だけが確定する。§11 の E06）。</description></item>
        /// <item><description>Gameplay 時計は止まったまま（§6.2 手順 2）。</description></item>
        /// <item><description>表示代理が立っている（§7.2）。</description></item>
        /// <item><description>カメラは X へ単調に進み、Z は接続軸から動かない（§7.1）。</description></item>
        /// </list>
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingEastIntoTheSeam_SlidesRightAndArrivesInB()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            GameSessionState session = GameSessionProvider.Current;
            Assert.IsNotNull(session, "Session が差さっている。");
            Assert.IsFalse(session.HasVisited(AreaB), "前提：B はまだ訪問していない。");

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            // <b>置き直したあとに記録する。</b> 出入口の手前へ置くと通常追従がそこへ寄るので、
            // 置く前の位置を「出発位置」にすると、追従の分までスライドの移動に数えてしまう。
            Vector3 cameraBefore = RigPosition();

            var samples = new SlideSamples(transitions, session, AreaB);
            yield return HoldWhileSampling(Key.D, samples, () => transitions.SlideCommittedCount > 0, 25f);

            // ---- 受理と成功 ----
            Assert.AreEqual(1, transitions.ConnectionTravelCount, "接続で 1 回だけ受理された。");
            Assert.AreEqual(1, transitions.SlideCommittedCount, "スライドで 1 回だけ到着が確定した。");
            Assert.AreEqual(0, transitions.SlideRolledBackCount, "戻していない。");
            Assert.AreEqual(0, transitions.CompletedCount,
                "従来の Single 経路は通っていない（スライド経路で着いた）。");
            Assert.AreEqual(string.Empty, transitions.Slide.LastFailure, "失敗を抱えていない。");

            // ---- 途中の不変条件 ----
            samples.AssertObserved();
            Assert.AreEqual(2, samples.MaxResidentCount,
                "スライド中は Area が 2 つ載っている（§1.2 の上限 2／§4.1）。");
            Assert.AreEqual(2, samples.MaxLoadedSceneCount, "実 Scene も 2 枚（Additive で受け渡した）。");
            Assert.IsFalse(samples.SawArrivalPlayable, "Commit まで到着側は遊べない（§4.1）。");
            Assert.IsFalse(samples.SawVisitedBeforeCommit,
                "Commit より前に訪問済みが付かない（§11 の E06）。");
            Assert.IsTrue(samples.AlwaysFrozen, "スライド中ずっと Gameplay 時計が止まっている（§6.2 手順 2）。");
            Assert.Greater(samples.MaxProxyCount, 0, "表示代理が立っている（§7.2）。");
            Assert.Greater(samples.MaxHiddenRendererCount, 1,
                "出発側と到着側の実 Renderer を両方隠している（§6.2 手順 6）。");
            Assert.AreEqual(1, samples.MaxActivePlayerCount,
                "活動している主人公は常に 1 人（到着側を開ける前に出発側を閉じている。§4.3）。");
            Assert.IsFalse(samples.SawVisibleRealActor,
                "スライド中に見えているのは代理だけ（到着側の実 Actor も隠している。§6.2 手順 6）。"
                + " 見えていたのは=" + samples.VisibleRealActorName);

            // ---- カメラ（§7.1）----
            Assert.Greater(samples.CameraSamples.Count, 2,
                "スライドが複数フレームにわたっている（1 フレームで飛んでいない）。");
            Assert.Less(samples.WorstAxisDrift, 0.1f,
                "東西のスライドで Z が動かない（最大のずれ=" + samples.WorstAxisDrift + "。§7.1）。");
            AssertMonotonicIncreasingX(samples.CameraSamples);

            Vector3 cameraAfter = RigPosition();
            Assert.Greater(cameraAfter.x, cameraBefore.x + 5f, "カメラが東へ大きく動いた（右スライド）。");
            Assert.AreEqual(transitions.Slide.LastSlideTo.x, cameraAfter.x, 0.01f, "終点へ厳密に着いた。");

            // <b>事前に境界位置へ瞬間移動していない</b>（§7.1）。
            // 受理から準備完了までの間に実カメラが動いていないこと、そしてスライドの
            // 最初のフレームがその位置から始まっていること——この 2 つで「先に飛んでいない」が言える。
            Assert.AreEqual(samples.FirstPreSlideX, samples.LastPreSlideX, 0.2f,
                "受理から準備完了までの間、実カメラは動いていない。");
            Assert.AreEqual(samples.LastPreSlideX, samples.CameraSamples[0].x, 0.2f,
                "スライドは準備完了時点の実カメラ位置から始まる（§7.1）。");
            Assert.Less(samples.LastPreSlideX, transitions.Slide.LastSlideTo.x - 5f,
                "始点は到着側ではなく出発側にある。");

            // ---- 到着後 ----
            yield return null;
            Assert.IsTrue(session.HasVisited(AreaB), "Commit で訪問済みになった（§11 の E06）。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "探索へ戻った。");
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "Gameplay 時計が戻った。");

            AreaRuntimeBundle current = CurrentAreaProvider.Current;
            Assert.IsNotNull(current, "活動 Area が指定されている。");
            Assert.AreEqual(AreaB.Value, current.AreaId.Value, "活動 Area は B。");
            Assert.IsTrue(current.Context.IsAreaReady, "B で遊べる。");

            Assert.IsFalse(Display().IsActive, "表示代理は畳まれている（§7.2）。");
            Assert.AreEqual(0, Display().HiddenRendererCount, "隠した実 Renderer を戻した。");
            AssertPlayerVisible();

            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount,
                "旧 Area を撤去して在留は 1 つ（§6.2 手順 11）。");

            // <b>先読みは到着 Area を手放している</b>（§6.2 手順 4）。
            // 手放し忘れると、次に別の候補を望んだ瞬間に「いま遊んでいる Area」を撤去しにかかる。
            AreaPreloader preloader = transitions.Slide.Preloader;
            Assert.AreEqual(1, preloader.HandedOffCount, "遷移が到着 Area を引き取った。");
            Assert.AreEqual(AreaPreloadPhase.Idle, preloader.Phase, "先読みは手ぶらへ戻っている。");
            Assert.IsFalse(preloader.DesiredArea.IsValid, "望みも残っていない。");
            Assert.AreEqual(1, SceneManager.sceneCount, "実 Scene も 1 枚に戻った。");
            Assert.AreEqual(0, transitions.Slide.UnloadFailureCount, "撤去は失敗していない。");
            Assert.AreEqual(1, Camera.allCamerasCount, "活動中の Camera は 1 台だけ（§11 の P12）。");
            Assert.AreEqual(AreaSlideTransactionPhase.Idle, transitions.Slide.Coordinator.Phase,
                "排他が解けている（次の遷移を受けられる）。");
        }

        // ---------------------------------------------------------------- P02：西へ

        /// <summary>
        /// <b>P02</b>：B の西の出入口へ実キーで歩くと左へスライドして A に着き、
        /// <b>押しっぱなしでは逆戻りしない</b>。離して再入力すれば再び移動できる（§11 P02）。
        ///
        /// 到着直後の主人公は出入口の範囲内に立っているので、押下を持ち込んだまま
        /// 再入場を許すと<b>A と B を往復し続ける</b>。§6.1 末尾の「一度離してから再操作」が
        /// 効いていることを、キーを離さずに確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingWestIntoTheSeam_SlidesLeftAndHoldingDoesNotBounceBack()
        {
            yield return EnterArea(P55AreaBScene);

            AreaTransitionService transitions = Transitions();
            GameSessionState session = GameSessionProvider.Current;

            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return SettleCamera();
            Vector3 cameraBefore = RigPosition();

            var samples = new SlideSamples(transitions, session, AreaA);
            yield return HoldWhileSampling(Key.A, samples, () => transitions.SlideCommittedCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideCommittedCount, "左へスライドして着いた。");
            Assert.Less(RigPosition().x, cameraBefore.x - 5f, "カメラが西へ大きく動いた（左スライド）。");
            Assert.Less(samples.WorstAxisDrift, 0.1f, "東西のスライドで Z が動かない。");
            AssertMonotonicDecreasingX(samples.CameraSamples);
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居る。");

            // ---- 押しっぱなしのまま待つ：逆戻りしない ----
            float deadline = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.A));
                yield return null;
            }

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "押しっぱなしでは 2 回目の遷移が起きない（§6.1 末尾）。");
            Assert.AreEqual(1, transitions.ConnectionTravelCount, "受理も 1 回だけ。");

            // ---- 離して再入力：また移動できる ----
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return null;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.ConnectionTravelCount > 1, 10f);

            Assert.AreEqual(2, transitions.ConnectionTravelCount,
                "離して再操作すれば移動できる（§8 末尾「一度入力を離してから再操作」）。");
        }

        // ---------------------------------------------------------------- 準備失敗（§8 の 2 行目）

        /// <summary>
        /// 到着側の先読みが失敗したら、<b>A をそのまま遊べる状態へ戻す</b>（§8 の 2 行目）。
        ///
        /// 実サービスへ「Additive ロードが必ず失敗する実装」を差し込む。暗転へ切り替えて
        /// 成功扱いにしない・進行を変えない・出発側の活動が戻ることを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenThePreloadFails_TheDepartureAreaBecomesPlayableAgain()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            transitions.SlideSceneHost = new AlwaysFailingSceneHost();

            GameSessionState session = GameSessionProvider.Current;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            yield return HoldUntil(Key.D, () => transitions.SlideRolledBackCount > 0, 25f);

            Assert.AreEqual(1, transitions.ConnectionTravelCount, "受理はされた。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "成功扱いにしない（暗転へ逃げない）。");
            Assert.AreEqual(1, transitions.SlideRolledBackCount, "出発側へ戻した。");
            Assert.IsNotEmpty(transitions.Slide.LastFailure, "理由が残っている。");

            Assert.IsFalse(session.HasVisited(AreaB), "訪問済みを増やさない（§8 末尾／E07）。");
            Assert.AreEqual(1, SceneManager.sceneCount, "到着 Scene は載っていない。");
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount, "在留も 1 つのまま。");

            AreaContext context = CurrentAreaProvider.Current.Context;
            Assert.IsTrue(context.IsAreaReady, "A で遊べる状態へ戻った（§6.3 の 2 行目）。");
            Assert.AreEqual(1, context.ReopenCount, "初期化のやり直しではなく再開として戻した。");
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "Gameplay 時計も戻った。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "探索へ戻った。");

            Assert.IsFalse(Display().IsActive, "表示代理は畳まれている。");
            Assert.AreEqual(0, Display().HiddenRendererCount, "隠した実 Renderer を戻した。");
            AssertPlayerVisible();

            // <b>カメラは演出に一度も触られていない。</b> 位置の比較ではなく書込みの回数で見る——
            // 位置は通常追従が動かしうるので、「動いていない」では演出の有無を言えない。
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.AreEqual(0, host.SlideCount, "スライドを始めていない（準備段階で戻した）。");
            Assert.IsFalse(host.IsSliding, "スライドは走っていない。");
            Assert.AreEqual(0, host.Rig.SlideWriteCount, "演出が Rig へ書き込んでいない。");
            Assert.IsFalse(host.Rig.FollowSuspended, "通常追従が戻っている（動けなくならない）。");
            Assert.AreEqual(AreaA.Value, host.BoundArea.Value, "カメラは出発 Area に結び付いたまま。");

            Assert.AreEqual(AreaSlideTransactionPhase.Idle, transitions.Slide.Coordinator.Phase,
                "排他が解けている（次の操作を受けられる）。");
        }

        // ---------------------------------------------------------------- Fade との排他（§8）

        /// <summary>
        /// <b>Fade と Slide は同じ遷移排他を共有する</b>（§8「別サービスが同時ロードを発行しない」）。
        ///
        /// 別々の調停役に持たせると、どちらも「自分は空いている」と判断して<b>同時に 2 本の
        /// Scene ロードを発行する</b>——どちらが最後に Scene を置き換えるか決まらない。
        /// 両方向を見る：スライド中の Fade 要求と、Fade 中のスライド要求。
        ///
        /// <b>ここだけはサービスを直接叩く。</b> 実入力では「同じフレームに 2 種類の要求」を
        /// 作れない（出入口は 1 フレームに 1 件しか出さない）。P01／P02 が実入力を見る役で、
        /// ここは排他という内部契約を見る役である。
        /// </summary>
        [UnityTest]
        public IEnumerator FadeAndSlide_ShareTheSameTransitionExclusion()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            Assert.IsNotNull(transitions.Connections, "接続一覧がある。");
            Assert.IsTrue(
                transitions.Connections.TryGetFromExit(AreaA, ExitAEast, out AreaConnectionSnapshot east),
                "東向きの接続を引ける。");

            // ---- スライド中は Fade を受けない ----
            Assert.IsTrue(transitions.TryTravel(east).Accepted, "スライドが受理される。");
            Assert.IsTrue(transitions.Slide.IsTransitioning, "スライドが走っている。");

            AreaTransitionDecision fadeDuringSlide = transitions.TryTravel(east.ToAreaId, east.EntryId);
            Assert.IsFalse(fadeDuringSlide.Accepted, "スライド中に Single／Fade 経路を受けない。");
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning, fadeDuringSlide.Rejection);

            AreaTransitionDecision secondSlide = transitions.TryTravel(east);
            Assert.IsFalse(secondSlide.Accepted, "スライド中に二本目のスライドも受けない。");
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning, secondSlide.Rejection);

            // 走り切るまで待つ。<b>着いたか戻ったかは問わない</b>——ここで見たいのは
            // 「断った要求が走らなかったこと」である。
            //
            // なお、出入口から離れた場所で要求すると、実カメラが接続軸から外れているため
            // <b>準備失敗として戻る</b>（§7.2 末尾「表示経路を安全に作れない接続は準備失敗」）。
            // これは正しい振る舞いで、実入力で出入口へ歩く P01／P02 では起きない。
            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.Slide.IsTransitioning && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(transitions.Slide.IsTransitioning, "1 本目が終端した。");
            Assert.AreEqual(1, transitions.Slide.Coordinator.AcceptedCount,
                "受理されたスライドは 1 本だけ（二本目は断った）。");
            Assert.AreEqual(0, transitions.CompletedCount, "断った Fade は走っていない。");

            // ---- Fade 中はスライドを受けない ----
            //
            // <b>終わらないロードを差し込む</b>。実際に Single 読込を完走させてしまうと、
            // 見たい「走っている最中」が一瞬で過ぎてしまう。
            transitions.Loader = new NeverFinishingLoader();

            Assert.IsTrue(transitions.TryTravel(east.ToAreaId, east.EntryId).Accepted,
                "Single／Fade 経路が受理される。");
            Assert.IsTrue(transitions.Coordinator.IsTransitioning, "Fade が走っている。");

            AreaTransitionDecision slideDuringFade = transitions.TryTravel(east);
            Assert.IsFalse(slideDuringFade.Accepted, "Fade 中にスライドを受けない。");
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning, slideDuringFade.Rejection);
            Assert.AreEqual(1, transitions.Slide.Coordinator.AcceptedCount,
                "スライドの受理は増えていない。");
            Assert.AreEqual(AreaSlideTransactionPhase.Idle, transitions.Slide.Coordinator.Phase,
                "断った要求で段階を進めていない（受理前に State を変えない。§6.2 手順 1）。");

            yield return null;
        }

        /// <summary>終わらない Single 読込（「走っている最中」を作るための注入）。</summary>
        private sealed class NeverFinishingLoader : IAreaSceneLoader
        {
            public IAreaLoadOperation Load(string scenePath) => new Pending();

            private sealed class Pending : IAreaLoadOperation
            {
                public bool IsDone => false;
                public bool HasError => false;
                public float Progress => 0f;
            }
        }

        // ---------------------------------------------------------------- 観測

        /// <summary>スライド中の不変条件を毎フレーム集める。</summary>
        private sealed class SlideSamples
        {
            private readonly AreaTransitionService _transitions;
            private readonly GameSessionState _session;
            private readonly StableId _destination;

            internal SlideSamples(
                AreaTransitionService transitions, GameSessionState session, StableId destination)
            {
                _transitions = transitions;
                _session = session;
                _destination = destination;
            }

            internal List<Vector3> CameraSamples { get; } = new List<Vector3>();

            /// <summary>受理〜準備完了の間に最初に観測した実カメラの X。</summary>
            internal float FirstPreSlideX { get; private set; } = float.NaN;

            /// <summary>受理〜準備完了の間に最後に観測した実カメラの X。</summary>
            internal float LastPreSlideX { get; private set; } = float.NaN;

            internal int MaxResidentCount { get; private set; }
            internal int MaxLoadedSceneCount { get; private set; }
            internal int MaxProxyCount { get; private set; }
            internal int MaxHiddenRendererCount { get; private set; }
            internal bool SawArrivalPlayable { get; private set; }
            internal bool SawVisitedBeforeCommit { get; private set; }
            internal bool AlwaysFrozen { get; private set; } = true;

            /// <summary>スライド中に Active だった主人公の最大数（出発側を閉じ忘れると 2 になる）。</summary>
            internal int MaxActivePlayerCount { get; private set; }

            /// <summary>
            /// スライド中に<b>実 Actor の絵が出ていた</b>か（§6.2 手順 6）。
            ///
            /// 隠した枚数を数えるだけでは足りない——出発側だけ隠しても数は増えるので、
            /// 到着側を隠し忘れても検知できない（実際に注入で通り抜けた）。
            /// 見るのは「代理以外の絵が出ていないこと」そのもの。
            /// </summary>
            internal bool SawVisibleRealActor { get; private set; }

            /// <summary>その絵の持ち主（失敗したときに原因が分かるように残す）。</summary>
            internal string VisibleRealActorName { get; private set; } = string.Empty;
            internal float WorstAxisDrift { get; private set; }

            internal void Tick()
            {
                AreaSlideTransactionPhase phase = _transitions.Slide.Coordinator.Phase;

                // 受理〜準備完了の間の実カメラ位置。<b>ここが動いていたら「先に飛んでいる」</b>（§7.1）。
                if (phase == AreaSlideTransactionPhase.Accepted
                    || phase == AreaSlideTransactionPhase.Preparing
                    || phase == AreaSlideTransactionPhase.Ready)
                {
                    float x = RigPosition().x;
                    if (float.IsNaN(FirstPreSlideX))
                    {
                        FirstPreSlideX = x;
                    }

                    LastPreSlideX = x;
                }

                if (phase != AreaSlideTransactionPhase.Sliding)
                {
                    return;
                }

                CameraSamples.Add(RigPosition());
                WorstAxisDrift = Mathf.Max(WorstAxisDrift, Mathf.Abs(RigPosition().z - SeamAxisZ));
                MaxResidentCount = Mathf.Max(MaxResidentCount, _transitions.Slide.Residency.ResidentCount);
                MaxLoadedSceneCount = Mathf.Max(MaxLoadedSceneCount, SceneManager.sceneCount);

                IAreaTransitionDisplay display = AreaTransitionDisplayProvider.Current;
                if (display != null)
                {
                    MaxProxyCount = Mathf.Max(MaxProxyCount, display.ProxyCount);
                    MaxHiddenRendererCount = Mathf.Max(MaxHiddenRendererCount, display.HiddenRendererCount);
                }

                if (!GameplayClockProvider.IsFrozen)
                {
                    AlwaysFrozen = false;
                }

                if (_session != null && _session.HasVisited(_destination))
                {
                    SawVisitedBeforeCommit = true;
                }

                int activePlayers = 0;
                foreach (PlayerRoot player in
                    Object.FindObjectsByType<PlayerRoot>(FindObjectsSortMode.None))
                {
                    if (player == null || !player.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    activePlayers++;
                    if (HasVisibleSprite(player.transform, out string playerSprite))
                    {
                        SawVisibleRealActor = true;
                        VisibleRealActorName = player.name + "/" + playerSprite;
                    }
                }

                MaxActivePlayerCount = Mathf.Max(MaxActivePlayerCount, activePlayers);

                foreach (Momotaro.Gameplay.Companion.CompanionActor companion in
                    Object.FindObjectsByType<Momotaro.Gameplay.Companion.CompanionActor>(
                        FindObjectsSortMode.None))
                {
                    if (companion != null && companion.gameObject.activeInHierarchy
                        && HasVisibleSprite(companion.transform, out string companionSprite))
                    {
                        SawVisibleRealActor = true;
                        VisibleRealActorName = companion.name + "/" + companionSprite;
                    }
                }

                foreach (AreaRuntimeBundle bundle in AreaBundleDirectory.All)
                {
                    if (bundle != null && bundle.AreaId.Equals(_destination)
                        && bundle.Context != null && bundle.Context.IsAreaReady)
                    {
                        SawArrivalPlayable = true;
                    }
                }
            }

            internal void AssertObserved()
            {
                Assert.Greater(CameraSamples.Count, 0,
                    "スライド中のフレームを観測できた（Sliding 段階を通っている）。");
                Assert.IsFalse(float.IsNaN(FirstPreSlideX),
                    "受理〜準備完了のフレームも観測できた（段階を飛ばしていない）。");
            }
        }

        private static void AssertMonotonicIncreasingX(List<Vector3> samples)
        {
            for (int i = 1; i < samples.Count; i++)
            {
                Assert.GreaterOrEqual(samples[i].x, samples[i - 1].x - 0.001f,
                    "カメラが東へ進み続ける（戻らない）。i=" + i);
            }
        }

        private static void AssertMonotonicDecreasingX(List<Vector3> samples)
        {
            for (int i = 1; i < samples.Count; i++)
            {
                Assert.LessOrEqual(samples[i].x, samples[i - 1].x + 0.001f,
                    "カメラが西へ進み続ける（戻らない）。i=" + i);
            }
        }

        /// <summary>その Actor の絵が出ているか（有効で Sprite を持つ SpriteRenderer が 1 枚でもある）。</summary>
        private static bool HasVisibleSprite(Transform actorRoot, out string rendererName)
        {
            foreach (SpriteRenderer r in actorRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.sprite != null)
                {
                    rendererName = r.name + "(order=" + r.sortingOrder + ")";
                    return true;
                }
            }

            rendererName = string.Empty;
            return false;
        }

        /// <summary>主人公の絵が出ている（代理を畳んだあとに消えていない）。</summary>
        private static void AssertPlayerVisible()
        {
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");
            bool visible = false;
            foreach (SpriteRenderer r in player.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (r != null && r.enabled && r.sprite != null)
                {
                    visible = true;
                }
            }

            Assert.IsTrue(visible, "主人公の Renderer が有効へ戻っている（§7.2）。");
        }

        /// <summary>必ず失敗する追加読込（準備失敗の注入用）。</summary>
        private sealed class AlwaysFailingSceneHost : IAreaSceneHost
        {
            public IAreaSceneOperation LoadAdditive(string scenePath) => new Failed();

            public IAreaSceneOperation Unload(int sceneHandle) => new Failed();

            public bool IsLoaded(int sceneHandle) => false;

            private sealed class Failed : IAreaSceneOperation
            {
                public bool IsDone => true;
                public bool HasError => true;
                public int SceneHandle => 0;
            }
        }

        // ---------------------------------------------------------------- 補助

        private IEnumerator EnterArea(string scenePath)
        {
            AssertSceneRegistered(scenePath);

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

            yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            yield return null;

            AreaInitializer initializer = Object.FindFirstObjectByType<AreaInitializer>();
            Assert.IsNotNull(initializer, "初期化担当が居る。");
            Assert.IsTrue(initializer.Initialized, "初期化が成立する。理由=" + initializer.FailureReason);

            // 置き直した直後の補間を終わらせる（配置の検査に補間の残りを混ぜない）。
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");
            float deadline = Time.realtimeSinceStartup + 3f;
            while (host.Rig.Blend.IsBlending && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsNotNull(AreaTransitionDisplayProvider.Current,
                "遷移中の表示担当が常駐している（§7.2）。");

            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
        }

        private IEnumerator StandJustBefore(AreaExitGate gate, Vector3 back, float offsetZ = 0f)
        {
            Vector3 spot = gate.transform.position + back * 0.4f + new Vector3(0f, 0f, offsetZ);
            var root = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(root, "主人公の根がある。");
            root.transform.position = new Vector3(spot.x, root.transform.position.y, spot.z);
            if (root.Body != null)
            {
                root.Body.position = root.transform.position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(gate.IsWired, "出入口に主人公の根が配線されている。");
            Assert.IsTrue(gate.PlayerInside, "範囲内に居る。");
        }

        private IEnumerator HoldUntil(Key key, System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        /// <summary>押しっぱなしにしながら、毎フレーム不変条件を集める。</summary>
        private IEnumerator HoldWhileSampling(
            Key key, SlideSamples samples, System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
                samples.Tick();
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }

        /// <summary>置き直した直後の補間を終わらせる（演出の検査に追従の残りを混ぜない）。</summary>
        private static IEnumerator SettleCamera()
        {
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");

            float deadline = Time.realtimeSinceStartup + 3f;
            while (host.Rig.Blend.IsBlending && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(host.Rig.Blend.IsBlending, "前提：補間が終わっている。");
        }

        private static Vector3 RigPosition()
        {
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            return host != null && host.Rig != null ? host.Rig.transform.position : Vector3.zero;
        }

        private static IAreaTransitionDisplay Display()
        {
            IAreaTransitionDisplay display = AreaTransitionDisplayProvider.Current;
            Assert.IsNotNull(display, "遷移中の表示担当が常駐している。");
            return display;
        }

        private static AreaExitGate FindExitGate(StableId exitId)
        {
            foreach (AreaExitGate gate in
                Object.FindObjectsByType<AreaExitGate>(FindObjectsSortMode.None))
            {
                if (gate != null && gate.ExitId.Equals(exitId))
                {
                    return gate;
                }
            }

            Assert.Fail("出入口 '" + exitId.Value + "' が Scene にありません。");
            return null;
        }

        private static AreaTransitionService Transitions()
        {
            AreaTransitionService service = BootstrapServices.Get<AreaTransitionService>();
            Assert.IsNotNull(service, "遷移サービスが常駐していません。");
            return service;
        }

        private static void AssertSceneRegistered(string scenePath)
        {
            foreach (UnityEditor.EditorBuildSettingsScene s in UnityEditor.EditorBuildSettings.scenes)
            {
                if (s.path == scenePath)
                {
                    return;
                }
            }

            Assert.Fail("Scene が Build Settings へ未登録です: " + scenePath);
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

        private static void ResetStatics()
        {
            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            AreaTransitionDisplayProvider.ClearForTests();
            PerceptionTargetRegistry.Clear();
            AreaInteractableRegistry.Clear();
            InvestigationPointRegistry.Clear();
            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            GameSessionProvider.Current = null;
            GameplayClockProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            AreaPendingArrival.Clear();
            AreaPendingArrival.ResetDiagnostics();
        }

        private void RemoveDevices()
        {
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            RemoveStrayDevices();
        }

        private static void RemoveStrayDevices()
        {
            for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = InputSystem.devices[i];
                if (device != null && device.name != null && device.name.StartsWith("P55Keyboard"))
                {
                    InputSystem.RemoveDevice(device);
                }
            }
        }
    }
}
