using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Transfer;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Hud;
using Momotaro.Presentation.Transition;
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
        /// <summary>Area でも起動 Scene でもない Scene（差し替え注入の受け皿）。</summary>
        private const string EmptySystemScene = "Assets/_Project/Scenes/SCN_System_Loading.unity";

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

            // 撤去できたかどうかは<b>実 Scene</b>で言う（工程 P55-08b）。
            //
            // 枚数では言えなくなった——到着した主人公は逆向きの出入口のすぐ内側に居るので、
            // §5 の距離による先読みが<b>同じ Area をもう一度</b>載せる。
            // 実体ハンドルでも言えない（出発前の Area はまだ世代を配られていない）。
            // Scene handle なら、読み直した A は<b>別の Scene</b>として区別できる。
            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle departureBundle), "出発 Area の束がある。");
            int departureSceneHandle = departureBundle.SceneHandle;
            Assert.AreNotEqual(0, departureSceneHandle, "前提：出発 Scene を特定できる。");

            // <b>置き直したあとに記録する。</b> 出入口の手前へ置くと通常追従がそこへ寄るので、
            // 置く前の位置を「出発位置」にすると、追従の分までスライドの移動に数えてしまう。
            Vector3 cameraBefore = RigPosition();

            var samples = new SlideSamples(transitions, session, AreaB);
            yield return HoldWhileSampling(Key.D, samples, () => transitions.SlideCommittedCount > 0, 25f);

            // ---- 受理と成功 ----
            Assert.AreEqual(1, transitions.ConnectionTravelCount, "接続で 1 回だけ受理された。");
            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドで 1 回だけ到着が確定した。戻した回数=" + transitions.SlideRolledBackCount
                + " 失敗=" + transitions.Slide.LastFailure);
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

            // <b>先読みは到着 Area を手放している</b>（§6.2 手順 4）。
            // 手放し忘れると、次に別の候補を望んだ瞬間に「いま遊んでいる Area」を撤去しにかかる。
            AreaPreloader preloader = transitions.Slide.Preloader;
            Assert.AreEqual(1, preloader.HandedOffCount, "遷移が到着 Area を引き取った。");

            // <b>旧 Area の実体は撤去された</b>（§6.2 手順 11）。
            //
            // 枚数では言えなくなった（工程 P55-08b）。到着した主人公は逆向きの出入口の
            // すぐ内側に居るので、§5 の距離による先読みが<b>同じ Area をもう一度 Staged で持つ</b>。
            // 撤去できたかどうかは<b>実体（世代つきハンドル）</b>で言う——読み直した A は別の世代である。
            Assert.AreEqual(0, transitions.Slide.UnloadFailureCount, "撤去は失敗していない。");
            Assert.IsFalse(transitions.SlideSceneHost.IsLoaded(departureSceneHandle),
                "出発時の Scene は撤去されている。");

            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaPreloadPhase.Staged, preloader.Phase,
                "落ち着いた先は「隣を持っている」（§5 の距離による先読み）。");
            Assert.AreEqual(AreaA.Value, preloader.StagedArea.AreaId.Value,
                "持っているのは来た方の A（B の西の出入口が 6 units 以内）。");
            Assert.AreNotEqual(departureSceneHandle, preloader.StagedSceneHandle,
                "読み直した A は別の Scene である（撤去は本当に起きた）。");
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                "在留は活動中 B と Staged の A（上限 2 のまま）。台帳=" + DumpResidency(transitions)
                + " Scene 枚数=" + SceneManager.sceneCount
                + " 先読み=" + transitions.Slide.Preloader.Phase
                + " Staged=" + transitions.Slide.Preloader.StagedArea);
            Assert.AreEqual(2, SceneManager.sceneCount, "実 Scene も 2 枚。");
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
            var sceneHost = new AlwaysFailingSceneHost();
            transitions.SlideSceneHost = sceneHost;

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

            // ---- 直して再操作すると、こんどは着く（§11 の P08「旧 Area で再操作でき」）----
            //
            // §5 は「先読み失敗は自動で毎フレーム再試行しない。<b>次の新しい遷移操作で
            // 一度だけ再試行できる</b>」と定めている。ここがそれである。
            sceneHost.Failing = false;
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "戻ったあとにもう一度操作できる。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(2, transitions.Slide.Coordinator.AcceptedCount, "受理は 2 回目。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
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

        // ---------------------------------------------------------------- スライド途中の失敗（§8 の 3 行目）

        /// <summary>
        /// スライドの途中で到着側が壊れたら、<b>同じ描画経路を逆向きに戻す</b>（§8 の 3 行目）。
        ///
        /// 注入は<b>実サービスへ</b>行う（§11 P08）：スライド中に到着側の <c>AreaContext</c> を壊す。
        /// 外から Scene が撤去された・到着側の初期化が後から失敗した、のいずれでも同じ形になる。
        ///
        /// 見るのは「戻ったこと」だけではない。<b>翌フレームに跳ね返らないこと</b>も見る——
        /// 戻し切ったあとに追従の内部状態を終点へ同期していないと、次の <c>LateUpdate</c> が
        /// スライド前の位置へ引き戻す。
        /// </summary>
        [UnityTest]
        public IEnumerator FailingMidSlide_ReversesAlongTheSamePathAndRestoresTheDepartureArea()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            GameSessionState session = GameSessionProvider.Current;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            Vector3 cameraBefore = RigPosition();

            yield return HoldUntilPhase(Key.D, AreaSlideTransactionPhase.Sliding, 25f);
            Assert.AreEqual(AreaSlideTransactionPhase.Sliding, transitions.Slide.Coordinator.Phase,
                "前提：スライドが走っている。");

            // <b>途中</b>で壊したいので、少し進ませる。始まった瞬間に壊すと
            // 「戻す距離が 0」になり、逆向きの動きを見たことにならない。
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }

            Assert.AreEqual(AreaSlideTransactionPhase.Sliding, transitions.Slide.Coordinator.Phase,
                "前提：まだスライド中（0.45 秒かかる）。");
            Assert.Greater(RigPosition().x, cameraBefore.x + 0.1f, "前提：少しは進んでいる。");

            // ---- 注入：到着側の AreaContext を壊す ----
            Assert.IsTrue(TryFindBundle(AreaB, out AreaRuntimeBundle destination), "到着側の束がある。");
            Object.DestroyImmediate(destination.Context);

            float peakX = RigPosition().x;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (transitions.Slide.IsTransitioning && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                peakX = Mathf.Max(peakX, RigPosition().x);
            }

            Assert.IsFalse(transitions.Slide.IsTransitioning, "終端した。");
            Assert.AreEqual(1, transitions.Slide.ReversedCount, "同じ経路を逆向きに戻した（§8 の 3 行目）。");

            // <b>気付いた時点で引き返す。</b> 終点まで行ってから戻ると、失敗した遷移で
            // プレイヤーに到着側を見せてしまい、戻しも上限いっぱいの 0.25 秒かかる。
            Assert.Less(peakX, transitions.Slide.LastSlideTo.x - 3f,
                "壊れたと気付いた場所から引き返している（終点まで進んでいない）。到達点=" + peakX
                + " 終点=" + transitions.Slide.LastSlideTo.x);
            Assert.AreEqual(1, transitions.SlideRolledBackCount, "出発側へ戻した。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "成功扱いにしない。");
            Assert.IsNotEmpty(transitions.Slide.LastFailure, "理由が残っている。");

            Assert.IsFalse(session.HasVisited(AreaB), "訪問済みを増やさない（§8 末尾／E07）。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居る。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "A で遊べる。");
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "Gameplay 時計が戻った。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "探索へ戻った。");
            Assert.IsFalse(Display().IsActive, "表示代理は畳まれている。");
            Assert.AreEqual(0, Display().HiddenRendererCount, "隠した実 Renderer を戻した。");
            AssertPlayerVisible();

            Assert.AreEqual(1, SceneManager.sceneCount, "到着 Scene は解放されている（§8「B を隔離・解放」）。");
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount, "在留も 1 つへ戻った。");

            // ---- 戻し切った位置に留まる（翌フレームに跳ねない）----
            Assert.AreEqual(cameraBefore.x, RigPosition().x, 0.2f, "カメラが出発位置へ戻っている。");

            float settled = RigPosition().x;
            for (int i = 0; i < 4; i++)
            {
                yield return null;
                Assert.AreEqual(settled, RigPosition().x, 0.05f,
                    "戻したあとカメラが跳ね返らない（追従の内部状態も終点へ同期している）。i=" + i);
            }

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsFalse(host.IsSliding, "演出は走っていない。");
            Assert.IsFalse(host.IsHoldingAfterSlide, "終点に留まったままにしない（解く者が居ないため）。");
            Assert.IsFalse(host.Rig.FollowSuspended, "通常追従が戻っている。");
        }

        // ---------------------------------------------------------------- 監視タイムアウトと遅延完了（§8 の 5 行目）

        /// <summary>
        /// ロードが返ってこないときは<b>旧 Area への復帰を優先</b>し、古い操作は保持する。
        /// 終端したあとに、遅れて着いた Scene を <b>Staged のまま</b>撤去する（§8 の 5 行目／§11 P09）。
        ///
        /// 「キャンセルできたと扱わない」ことがここの要点である。Unity の非同期ロードは
        /// 止められないので、監視を諦めた時点で撤去はできない——できるようになるのは終端してからで、
        /// それを進めるのは常駐側の役目になる。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheLoadNeverFinishes_ItReturnsToTheDepartureAndReleasesTheLateArrival()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new ControllableSceneHost();
            transitions.SlideSceneHost = host;
            transitions.TimeoutSeconds = 1f;

            GameSessionState session = GameSessionProvider.Current;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            yield return HoldUntil(Key.D, () => transitions.Slide.TimedOutCount > 0, 25f);

            Assert.AreEqual(1, transitions.Slide.TimedOutCount, "監視上限を超えた。");
            Assert.AreEqual(1, transitions.Slide.DeferredReleaseCount, "撤去を終端後へ持ち越した。");
            Assert.AreEqual(1, transitions.SlideRolledBackCount, "出発側へ戻した。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "成功扱いにしない。");
            Assert.AreEqual(0, host.UnloadCount,
                "まだ撤去していない（終端していない操作の上に別の操作を重ねない。§8）。");

            Assert.IsFalse(session.HasVisited(AreaB), "訪問済みを増やさない。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "A で遊べる状態へ戻った。");
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "Gameplay 時計も戻った。");
            Assert.IsFalse(AreaPendingArrival.HasPending, "到着要求は残っていない。");
            Assert.AreEqual(0, AreaCameraRigHost.Instance.SlideCount, "演出は始まっていない。");

            // ---- 遅れて終端する ----
            //
            // <b>主人公はその場に立ったまま</b>（工程 P55-08c。GPT 再修正②）。
            // 取り下げた先読みを距離で選び直してしまうと、遅れて着いた Scene が
            // 「また必要な先読み」になって撤去されない。抑止が効いていればここで撤去される。
            host.CompleteLoad(987654);

            float deadline = Time.realtimeSinceStartup + 10f;
            while ((host.UnloadCount == 0 || transitions.Slide.Residency.ResidentCount > 1)
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, host.UnloadCount,
                "終端したので撤去した（常駐が後始末を進める）。" + DumpWorld(transitions));
            Assert.AreEqual(987654, host.LastUnloadHandle, "撤去したのは遅れて着いた Scene。");
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount, "在留も 1 つへ戻った。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "遅延完了で成功が起きない（§8 末尾）。");
            Assert.IsFalse(session.HasVisited(AreaB), "遅延完了で訪問登録も起きない（§8 末尾）。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value,
                "遅延完了で CurrentArea も変わらない（§8 末尾）。");
        }

        // ---------------------------------------------------------------- Commit 後の撤去失敗（§8 の 4 行目）

        /// <summary>
        /// Commit のあとに旧 Area を撤去できなくても、<b>到着の成功は取り消さない</b>（§8 の 4 行目／§11 P10）。
        ///
        /// 旧 Area は非活動・非物理のまま隔離し、<b>在留枠も返さない</b>——返すと
        /// 「空きあり」と誤認して 3 枚目を読む。新しいスライドは受け付けず、
        /// 撤去の再試行手段だけを開けておく。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheDepartureCannotBeUnloaded_TheArrivalStaysAndRetireCanBeRetried()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new UnloadRefusingSceneHost();
            transitions.SlideSceneHost = host;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            yield return HoldUntil(Key.D, () => transitions.Slide.UnloadFailureCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideCommittedCount, "到着の成功は取り消さない。");
            Assert.AreEqual(1, transitions.Slide.UnloadFailureCount, "撤去は失敗した。");
            Assert.IsTrue(transitions.Slide.HasPendingRetire, "撤去し切れなかった旧 Area を抱えている。");
            Assert.IsNotEmpty(transitions.Slide.LastFailure, "理由が残っている。");

            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "B で遊べる。");
            Assert.AreEqual(2, SceneManager.sceneCount, "旧 Scene はまだ載っている。");
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                "在留枠は返さない（空きありと誤認して 3 枚目を読まない）。");

            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle departure), "旧 Area の束はまだある。");
            Assert.AreEqual(AreaActivationPhase.Retiring,
                transitions.Slide.Residency.PhaseOf(departure.Instance),
                "旧 Area は撤去中として隔離されている。");
            Assert.IsFalse(departure.ActivityGate.IsOpen, "旧 Area は非活動・非物理のまま。");
            Assert.IsFalse(departure.Context.IsAreaReady, "旧 Area では遊べない。");

            // ---- 抱えている間は新しいスライドを受け付けない ----
            Assert.IsTrue(
                transitions.Connections.TryGetFromExit(AreaB, ExitBWest, out AreaConnectionSnapshot west),
                "西向きの接続を引ける。");
            AreaTransitionDecision refused = transitions.TryTravel(west);
            Assert.IsFalse(refused.Accepted, "新しい Area ロードを出さない（§8 の 4 行目）。");
            Assert.AreEqual(AreaTransitionRejection.NotReady, refused.Rejection);
            Assert.AreEqual(AreaSlideTransactionPhase.Idle, transitions.Slide.Coordinator.Phase,
                "断った要求で段階を進めない。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "断っても B で遊べたまま。");

            // ---- 再試行できる ----
            host.RefuseUnload = false;
            Assert.IsTrue(transitions.Slide.TryRetryRetiringDeparture(), "撤去を再試行できる。");
            Assert.AreEqual(1, transitions.Slide.RetryStartedCount);

            float deadline = Time.realtimeSinceStartup + 10f;
            while (transitions.Slide.HasPendingRetire && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(transitions.Slide.HasPendingRetire, "撤去できた。");
            Assert.AreEqual(1, SceneManager.sceneCount, "旧 Scene が消えた。");
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount, "在留枠も返った。");
            Assert.AreEqual(1, transitions.SlideCommittedCount, "到着の成功は増減しない。");
        }

        // ---------------------------------------------------------------- Fade との共存（§8 末尾）

        /// <summary>
        /// <b>Fade（Single 読込）を挟んでもスライドが続けて動く</b>（§8 末尾「同じ Scene 操作管理を共有」）。
        ///
        /// Single 読込は在留台帳を通らない——載っている Scene を全部置き換えるので、
        /// 台帳だけが「まだ 2 枚ある」と思い込む。そのまま次のスライドへ入ると上限に達していると
        /// 誤認し、<b>先読みが断られてスライドできない</b>。受理のたびに台帳を実 Scene へ合わせ直す。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterAFadeTransition_TheLedgerMatchesTheLoadedScenesAndSlidingStillWorks()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // 1 回目：スライドで B へ。
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            Assert.AreEqual(1, transitions.SlideCommittedCount, "前提：スライドで B へ着いた。");
            yield return SettleWorld(transitions);
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                "前提：在留は活動中の B と、距離で読み直した A（§5。工程 P55-08b）。");

            // Fade（Single 読込）で A へ戻る。出入口ではなくサービスへ直接要求する——
            // ここで見たいのは「Single 読込を挟んだあと」であって入力経路ではない。
            Assert.IsTrue(
                transitions.Connections.TryGetFromExit(AreaB, ExitBWest, out AreaConnectionSnapshot west),
                "西向きの接続を引ける。");
            // Single 読込が<b>置き換えた</b>ことを、いま載っている Scene で言えるようにしておく。
            int bSceneBefore = CurrentAreaProvider.Current.SceneHandle;
            int stagedSceneBefore = transitions.Slide.Preloader.StagedSceneHandle;
            Assert.AreNotEqual(0, bSceneBefore, "前提：B の Scene を特定できる。");
            Assert.AreNotEqual(0, stagedSceneBefore, "前提：隣も載っている。");

            Assert.IsTrue(transitions.TryTravel(west.ToAreaId, west.EntryId).Accepted,
                "Single／Fade 経路が受理される。");

            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.CompletedCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, transitions.CompletedCount, "Fade で A へ着いた。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居る。");

            // <b>置き換えたことは「前の Scene が消えた」で言う</b>（工程 P55-08b）。
            // 枚数では言えない——着いた先も出入口の近くなので、距離による先読みが隣を載せる。
            yield return SettleWorld(transitions);
            Assert.IsFalse(transitions.SlideSceneHost.IsLoaded(bSceneBefore),
                "Single 読込が前の B を置き換えた。" + DumpWorld(transitions));
            Assert.IsFalse(transitions.SlideSceneHost.IsLoaded(stagedSceneBefore),
                "隔離していた隣も置き換えられた。");

            // 2 回目：また実キーでスライドできる。
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 1, 25f);

            Assert.AreEqual(2, transitions.SlideCommittedCount,
                "Fade を挟んでもスライドできる（台帳が実 Scene に合っている）。理由="
                + transitions.Slide.LastFailure);
            Assert.GreaterOrEqual(transitions.Slide.ForgottenResidentCount, 1,
                "消えていた実体を台帳から落とした。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");

            yield return SettleWorld(transitions);
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                "在留は活動中の B と、距離で読み直した A（§5。工程 P55-08b）。"
                + DumpWorld(transitions));
            Assert.AreEqual(2, SceneManager.sceneCount, "実 Scene も 2 枚。");
        }

        // ---------------------------------------------------------------- 持ち越す値（§6.3／§11 の P05）

        /// <summary>
        /// <b>P05</b>：非ゼロの HP 差分・スタミナ消費・全 CD・無敵・犬丸の復帰待ちを作ってから
        /// スライドし、<b>待機＋演出の間に値が進まず</b>そのまま持ち越されることを実測する（§6.3）。
        ///
        /// 値は<b>ゲーム自身が使う復元経路</b>（<c>TryImportTransferSnapshot</c>）で作る。
        /// テスト用の裏口を足すと、その裏口が本番と違う道を通っていても気付けない。
        ///
        /// <b>到着時の値は <c>ArrivalCompleted</c> で採る。</b> そこは Commit の同期区間の直後で、
        /// そのフレームの Actor の <c>Update</c> はもう終わっている——あとから読むと
        /// 「解凍後に進んだ分」が混ざり、進まなかったことを言えなくなる。
        /// </summary>
        [UnityTest]
        public IEnumerator SlidingCarriesTheActorValues_WithoutAdvancingThem()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // ---- 非ゼロの値を作る ----
            ActorValues start = ReadActorValues();
            Assert.IsTrue(start.Found, "出発側の Actor 部品がそろっている。");
            Assert.Greater(start.MaxHp, 2, "HP を減らせる構成である。");

            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            var hurt = Object.FindFirstObjectByType<PlayerHitReaction>();
            var companionVitals = Object.FindFirstObjectByType<CompanionHitReceiver>();
            var combat = Object.FindFirstObjectByType<CompanionCombatController>();
            var defense = Object.FindFirstObjectByType<CompanionDefenseController>();
            var guardian = Object.FindFirstObjectByType<CompanionGuardianController>();

            Assert.IsTrue(vitals.TryImportTransferSnapshot(new PlayerVitalsTransferSnapshot(
                    new VitalTransferSnapshot(start.MaxHp - 2),
                    new StaminaTransferSnapshot(Mathf.Max(1f, start.Stamina - 5f), 1.5f, 0f))),
                "HP とスタミナを減らせた。");

            // <b>Hurt は 0 にする。</b> ひるみ中は「行動中」なので §6.1 が遷移を受理しない——
            // 持ち越しを見たいのに受理されない、という別の話になってしまう。
            // 無敵の残りは<b>設定値を超えられない</b>（復元が値域を検査している）。
            // 固定秒を書くと、Data の設定が変わった日に「持ち越しの検査」が値域の話で落ちる。
            Assert.Greater(hurt.PostHitInvincibleSeconds, 0f, "被弾後無敵が設定されている。");
            Assert.IsTrue(hurt.TryImportTransferSnapshot(
                    new HitReactionTransferSnapshot(0f, hurt.PostHitInvincibleSeconds * 0.8f)),
                "被弾後無敵を立てられた。");

            // 犬丸は Down ＋ 復帰待ち（§6.3「犬丸 Down 復帰待ち」）。HP と Down は矛盾させない。
            Assert.IsTrue(companionVitals.Vitals.TryImportTransferSnapshot(
                    new CompanionVitalsTransferSnapshot(0, true, 3f, 0f,
                        new FlinchTransferSnapshot(0f, 0f, 0f, 0f))),
                "犬丸を Down ＋ 復帰待ちにできた。");
            Assert.IsTrue(combat.TryImportTransferSnapshot(new CompanionCombatTransferSnapshot(2f)),
                "攻撃 CD を立てられた。");
            Assert.IsTrue(defense.TryImportTransferSnapshot(new CompanionDefenseTransferSnapshot(
                    new GuardAbilityTransferSnapshot(1.5f), new EvadeAbilityTransferSnapshot(1.2f))),
                "防御 CD を立てられた。");
            Assert.IsTrue(guardian.TryImportTransferSnapshot(new CompanionGuardianTransferSnapshot(2.5f)),
                "守護 CD を立てられた。");

            yield return null;

            // ---- 到着の瞬間の値を採る ----
            ActorValues arrived = default;
            float arrivedAt = 0f;
            void OnArrived(StableId areaId)
            {
                arrived = ReadActorValues();
                arrivedAt = Time.realtimeSinceStartup;
            }

            transitions.ArrivalCompleted += OnArrived;

            try
            {
                yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
                yield return SettleCamera();

                // 受理の直前の値を毎フレーム覚えておく（受理後は止まっているはずの値）。
                ActorValues atAccept = default;
                float acceptedAt = 0f;
                float deadline = Time.realtimeSinceStartup + 25f;
                while (transitions.SlideCommittedCount == 0 && Time.realtimeSinceStartup < deadline)
                {
                    if (transitions.ConnectionTravelCount == 0)
                    {
                        atAccept = ReadActorValues();
                        acceptedAt = Time.realtimeSinceStartup;
                    }

                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                    yield return null;
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

                Assert.AreEqual(1, transitions.SlideCommittedCount, "スライドで着いた。");
                Assert.IsTrue(atAccept.Found, "受理直前の値を採れた。");
                Assert.IsTrue(arrived.Found, "到着時の値を採れた。");

                // <b>時間は確かに経っている。</b> これが無いと「進まなかった」ではなく
                // 「進む暇が無かった」で通ってしまう。
                Assert.Greater(arrivedAt - acceptedAt, 0.4f,
                    "受理から到着まで実時間で 0.4 秒以上かかっている（待機＋0.45 秒の演出）。経過="
                    + (arrivedAt - acceptedAt));

                // ---- 値そのまま（§6.3「受理後の待機・スライド中に減らさない」）----
                Assert.AreEqual(start.MaxHp - 2, arrived.Hp, "主人公の HP が持ち越された（全回復していない）。");
                Assert.AreEqual(atAccept.Hp, arrived.Hp, "HP は受理時の値のまま。");
                Assert.AreEqual(atAccept.Stamina, arrived.Stamina, 0.001f,
                    "スタミナが回復していない（止まっていた）。");
                Assert.AreEqual(atAccept.StaminaRegenDelay, arrived.StaminaRegenDelay, 0.001f,
                    "スタミナの回復待ちも進んでいない。");
                Assert.AreEqual(atAccept.PlayerInvincible, arrived.PlayerInvincible, 0.001f,
                    "被弾後無敵の残りが減っていない。");
                Assert.Greater(arrived.PlayerInvincible, 0f, "無敵はまだ残っている（消えていない）。");

                Assert.IsTrue(arrived.CompanionDown, "犬丸は Down のまま（勝手に復帰していない）。");
                Assert.AreEqual(CompanionState.Down, arrived.CompanionState, "配置状態も Down。");
                Assert.AreEqual(atAccept.CompanionRecovery, arrived.CompanionRecovery, 0.001f,
                    "Down の復帰待ちが減っていない。");
                Assert.Greater(arrived.CompanionRecovery, 0f, "復帰待ちはまだ残っている。");

                Assert.AreEqual(atAccept.AttackCooldown, arrived.AttackCooldown, 0.001f,
                    "攻撃 CD が減っていない。");
                Assert.AreEqual(atAccept.GuardCooldown, arrived.GuardCooldown, 0.001f,
                    "構えの CD が減っていない。");
                Assert.AreEqual(atAccept.EvadeCooldown, arrived.EvadeCooldown, 0.001f,
                    "回避の CD が減っていない。");
                Assert.AreEqual(atAccept.GuardianCooldown, arrived.GuardianCooldown, 0.001f,
                    "守護の CD が減っていない。");
                Assert.Greater(arrived.AttackCooldown, 0f, "CD はまだ残っている（解除されていない）。");
            }
            finally
            {
                transitions.ArrivalCompleted -= OnArrived;
            }
        }

        /// <summary>持ち越しを見るための値の束（同じ読み方で出発側と到着側を比べる）。</summary>
        private readonly struct ActorValues
        {
            internal bool Found { get; }
            internal int Hp { get; }
            internal int MaxHp { get; }
            internal float Stamina { get; }
            internal float StaminaRegenDelay { get; }
            internal float PlayerInvincible { get; }
            internal bool CompanionDown { get; }
            internal float CompanionRecovery { get; }
            internal CompanionState CompanionState { get; }
            internal float AttackCooldown { get; }
            internal float GuardCooldown { get; }
            internal float EvadeCooldown { get; }
            internal float GuardianCooldown { get; }

            internal ActorValues(
                PlayerVitalsHolder vitals, PlayerHitReaction hurt, CompanionHitReceiver companionVitals,
                CompanionActor actor, CompanionCombatController combat,
                CompanionDefenseController defense, CompanionGuardianController guardian)
            {
                PlayerVitalsTransferSnapshot player = vitals.ExportTransferSnapshot();
                CompanionVitalsTransferSnapshot cv = companionVitals.Vitals.ExportTransferSnapshot();
                CompanionDefenseTransferSnapshot cd = defense.ExportTransferSnapshot();

                Found = true;
                Hp = player.Health.Current;
                MaxHp = vitals.Vitals.Health.Max;
                Stamina = player.Stamina.Current;
                StaminaRegenDelay = player.Stamina.RegenDelayRemaining;
                PlayerInvincible = hurt.ExportTransferSnapshot().InvincibleRemaining;
                CompanionDown = cv.IsDown;
                CompanionRecovery = cv.RecoveryRemaining;
                CompanionState = actor.State;
                AttackCooldown = combat.ExportTransferSnapshot().CooldownRemaining;
                GuardCooldown = cd.Guard.CooldownRemaining;
                EvadeCooldown = cd.Evade.CooldownRemaining;
                GuardianCooldown = guardian.ExportTransferSnapshot().CooldownRemaining;
            }
        }

        /// <summary>いま活動している Area の Actor の値を読む（非 Active な旧 Area は拾わない）。</summary>
        private static ActorValues ReadActorValues()
        {
            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            var hurt = Object.FindFirstObjectByType<PlayerHitReaction>();
            var companionVitals = Object.FindFirstObjectByType<CompanionHitReceiver>();
            var actor = Object.FindFirstObjectByType<CompanionActor>();
            var combat = Object.FindFirstObjectByType<CompanionCombatController>();
            var defense = Object.FindFirstObjectByType<CompanionDefenseController>();
            var guardian = Object.FindFirstObjectByType<CompanionGuardianController>();

            if (vitals == null || hurt == null || companionVitals == null || actor == null
                || combat == null || defense == null || guardian == null)
            {
                return default;
            }

            return new ActorValues(vitals, hurt, companionVitals, actor, combat, defense, guardian);
        }

        // ---------------------------------------------------------------- 犬丸の状態（§4.6／§11 の P06）

        /// <summary>
        /// <b>P06</b>：健常／Down／Stagger／Away の犬丸で実キーで往復し、
        /// <b>意図せぬ復帰・出撃が起きない</b>ことと、<b>表示代理から命中も登録も発生しない</b>ことを見る。
        ///
        /// 状態は §4.6 の復元表のとおりに持ち越される。Away は<b>代理も作らない</b>（§7.2）。
        /// 代理が命中・登録を起こさないことは EditMode（<c>P55DisplayProxyTests</c>）が部品の有無で見ているが、
        /// ここでは<b>実遷移の最中に</b>同じことを確かめる——組み立て方が変わっても崩れないように。
        /// </summary>
        [UnityTest]
        public IEnumerator SlidingKeepsTheCompanionState(
            [Values(CompanionState.Follow, CompanionState.Down, CompanionState.Stagger,
                CompanionState.Away)] CompanionState wanted)
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            GameSessionState session = GameSessionProvider.Current;

            yield return SetUpCompanion(wanted);

            var playerVitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            int hpBefore = playerVitals.ExportTransferSnapshot().Health.Current;
            int registeredBefore = PerceptionTargetRegistry.Count;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            bool sawAwaySkip = false;
            bool sawProxy = false;
            int worstRegistered = registeredBefore;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;

                if (transitions.Slide.Coordinator.Phase != AreaSlideTransactionPhase.Sliding)
                {
                    continue;
                }

                IAreaTransitionDisplay display = AreaTransitionDisplayProvider.Current;
                if (display != null && display.CompanionSkippedBecauseAway)
                {
                    sawAwaySkip = true;
                }

                worstRegistered = Mathf.Max(worstRegistered, PerceptionTargetRegistry.Count);
                sawProxy |= AssertProxiesCarryNothingButDrawing();
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "どの状態でも往路は成立する。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(0, transitions.SlideRolledBackCount, "不正な遷移で戻っていない。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
            Assert.IsTrue(sawProxy, "スライド中に代理を観測できた。");
            Assert.LessOrEqual(worstRegistered, registeredBefore,
                "代理は索敵の登録簿に載らない（スライド中に登録が増えない）。");

            var arrived = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(arrived, "到着側に犬丸が居る。");

            switch (wanted)
            {
                case CompanionState.Away:
                    Assert.IsTrue(sawAwaySkip, "Away は代理も作らない（§7.2）。");
                    Assert.AreEqual(CompanionState.Away, arrived.State, "Away のまま（勝手に出撃しない）。");
                    Assert.IsFalse(arrived.gameObject.activeInHierarchy
                        && HasVisibleSprite(arrived.transform, out _),
                        "退場中の犬丸を描かない（§6.3）。");
                    break;

                case CompanionState.Down:
                    Assert.IsFalse(sawAwaySkip, "退場ではないので代理は作る。");
                    Assert.AreEqual(CompanionState.Down, arrived.State, "Down のまま（勝手に復帰しない）。");
                    var vitals = Object.FindFirstObjectByType<CompanionHitReceiver>();
                    CompanionVitalsTransferSnapshot cv = vitals.Vitals.ExportTransferSnapshot();
                    Assert.IsTrue(cv.IsDown, "生存値も Down のまま。");
                    Assert.Greater(cv.RecoveryRemaining, 0f, "復帰待ちが残っている（回復演出を再生していない）。");
                    break;

                case CompanionState.Stagger:
                    Assert.AreEqual(CompanionState.Stagger, arrived.State, "ひるみのまま持ち越す。");
                    break;

                default:
                    Assert.AreEqual(CompanionState.Follow, arrived.State,
                        "健常は追従へ戻る（旧攻撃・旧防御・旧探索を再開しない。§4.6）。");
                    break;
            }

            // 代理は当たらない・撃たない：主人公の HP は往路の間ずっと変わらない。
            var arrivedPlayer = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.AreEqual(hpBefore, arrivedPlayer.ExportTransferSnapshot().Health.Current,
                "代理からの命中は起きない（主人公の HP が変わらない）。");

            Assert.IsTrue(session.HasVisited(AreaB), "到着は成立している（Commit が訪問を確定した）。");
        }

        /// <summary>犬丸を指定の状態にする（ゲーム自身が使う復元経路で作る）。</summary>
        private IEnumerator SetUpCompanion(CompanionState wanted)
        {
            var vitals = Object.FindFirstObjectByType<CompanionHitReceiver>();
            var arbiter = Object.FindFirstObjectByType<CompanionStateArbiter>();
            Assert.IsNotNull(vitals, "犬丸の生存値がある。");
            Assert.IsNotNull(arbiter, "犬丸の状態調停役がある。");

            switch (wanted)
            {
                case CompanionState.Down:
                    // Down は HP 0 ＋ 復帰待ち（§4.6 の整合規則）。
                    Assert.IsTrue(vitals.Vitals.TryImportTransferSnapshot(
                            new CompanionVitalsTransferSnapshot(0, true, 3f, 0f,
                                new FlinchTransferSnapshot(0f, 0f, 0f, 0f))),
                        "Down ＋ 復帰待ちにできた。");
                    Assert.IsTrue(arbiter.TryRestoreState(CompanionState.Down), "Down へ置けた。");
                    break;

                case CompanionState.Stagger:
                    // ひるみは<b>歩いている間に切れない長さ</b>にする（受理まで実時間が進む）。
                    Assert.IsTrue(vitals.Vitals.TryImportTransferSnapshot(
                            new CompanionVitalsTransferSnapshot(
                                vitals.MaxHp, false, 0f, 0f,
                                new FlinchTransferSnapshot(0f, 0f, 30f, 0f))),
                        "ひるみ残りを立てられた。");
                    Assert.IsTrue(arbiter.TryRestoreState(CompanionState.Stagger), "Stagger へ置けた。");
                    break;

                case CompanionState.Away:
                    Assert.IsTrue(arbiter.TryRestoreState(CompanionState.Away), "Away へ置けた。");
                    break;
            }

            yield return null;

            var actor = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(actor, "犬丸が居る。");
            if (wanted != CompanionState.Follow)
            {
                Assert.AreEqual(wanted, actor.State, "前提：出発側が指定の状態になっている。");
            }
        }

        /// <summary>
        /// いま立っている代理が<b>描画部品しか持っていない</b>ことを確かめる（§7.2）。
        /// 戻り値は「代理を 1 つ以上見たか」。
        /// </summary>
        private static bool AssertProxiesCarryNothingButDrawing()
        {
            AreaTransitionDisplayProxy[] proxies =
                Object.FindObjectsByType<AreaTransitionDisplayProxy>(FindObjectsSortMode.None);
            for (int i = 0; i < proxies.Length; i++)
            {
                AreaTransitionDisplayProxy proxy = proxies[i];
                if (proxy == null)
                {
                    continue;
                }

                Assert.IsNull(proxy.GetComponentInChildren<Collider>(true),
                    "代理は Collider を持たない（当たらない）。");
                Assert.IsNull(proxy.GetComponentInChildren<Rigidbody>(true),
                    "代理は Rigidbody を持たない（物理に参加しない）。");
                Assert.IsNull(proxy.GetComponentInChildren<IPerceptionTarget>(true),
                    "代理は索敵対象として登録されない。");
                Assert.IsNull(proxy.GetComponentInChildren<CompanionActor>(true),
                    "代理は Actor の中身を持たない。");
                Assert.IsNull(proxy.GetComponentInChildren<PlayerRoot>(true),
                    "代理は主人公の根を持たない。");
            }

            return proxies.Length > 0;
        }

        // ---------------------------------------------------------------- 先読み中の Single 読込（§5／§11 の P11）

        /// <summary>
        /// <b>P11（死亡）</b>：隣 Area を先読みした状態で主人公が死んでも、暗転再開が成立し、
        /// <b>先読みは安全に破棄される</b>（§5 末尾「先読み中の死亡・暗転扉は先読みを不要扱いにし、
        /// 終端して隔離 Area を unload してからロードを発行する」）。
        ///
        /// 併せて <b>Submit の到着 Interact 化</b>が起きないことを見る（§9.1 末尾）。
        /// 再開に使った押下が離されたと認識されないと、到着後の最初の Interact が飲み込まれる。
        /// </summary>
        [UnityTest]
        public IEnumerator DyingWhileTheNeighbourIsStaged_DiscardsThePreloadAndRespawns()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            GameSessionState session = GameSessionProvider.Current;

            yield return StageTheNeighbour(transitions);

            Assert.AreEqual(2, SceneManager.sceneCount, "前提：隣 Area が載っている。");
            Assert.AreEqual(0, transitions.Slide.DiscardedForSingleLoadCount, "前提：まだ捨てていない。");

            // ---- 実際の被弾経路で死なせる ----
            yield return KillPlayerWithRealHits();
            yield return WaitForRespawnPrompt();

            var interactBefore = Object.FindFirstObjectByType<AreaInteractInput>();
            Assert.IsNotNull(interactBefore, "Interact の仲介が居る。");

            // ---- 実キー（Enter）で再開する ----
            yield return PressKeyUntil(Key.Enter,
                () => transitions.ArrivalCount > 0 || transitions.HasTerminalFailure, 25f);

            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, transitions.CompletedCount, "暗転（Single）経路で再開が着いた。");

            Assert.AreEqual(1, transitions.Slide.DiscardedForSingleLoadCount,
                "Single 読込の前に先読みを捨てた（§5 末尾）。");
            Assert.AreEqual(1, SceneManager.sceneCount, "隔離 Area は残っていない。");
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount,
                "在留台帳も実 Scene に合っている（載っている 1 枚 ＝ 在留 1 つ。工程 P55-08c）。"
                + " 以前は 0 だった——Single 読込は台帳を通らないので、活動中 Area が"
                + " 台帳に載らないままだった。距離による先読みは発行の前に活動中 Area を載せる。");
            Assert.AreEqual(AreaPreloadPhase.Idle, transitions.Slide.Preloader.Phase, "先読みは手ぶら。");
            Assert.IsFalse(AreaStagingRequest.IsRequested,
                "先読みの申し入れが残っていない（次に開く Scene が閉じたまま起動しない）。");

            // ---- 再開そのもの（§9.1 手順 6〜7）----
            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.IsNotNull(vitals, "到着側に主人公が居る。");
            Assert.IsFalse(vitals.IsDefeated, "主人公が復帰している。");
            Assert.AreEqual(vitals.Vitals.Health.Max, vitals.ExportTransferSnapshot().Health.Current,
                "全回復している（§9.1 手順 6）。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "探索へ戻った。");
            Assert.AreEqual(CampaignRespawnPhase.Idle, session.Respawn.Phase, "再開が完了した。");

            // ---- Submit の到着 Interact 化なし（§9.1 末尾）----
            var interactAfter = Object.FindFirstObjectByType<AreaInteractInput>();
            Assert.IsNotNull(interactAfter, "到着側にも Interact の仲介が居る。");
            Assert.AreEqual(0, interactAfter.InteractCount,
                "再開の押下が到着側の Interact になっていない。");
            Assert.IsNotNull(InputReleaseGateProvider.Current, "入力の解放待ちの提供点がある。");
            Assert.IsFalse(InputReleaseGateProvider.Current.RequiresRelease,
                "解放待ちが解けている（到着後の最初の Interact を飲み込まない）。");
        }

        /// <summary>
        /// <b>P11（暗転扉）</b>：隣 Area を先読みした状態で Interact の扉を使っても、
        /// 先読みを捨ててから Single 読込を発行する（§5 末尾）。
        /// </summary>
        [UnityTest]
        public IEnumerator UsingTheInteractDoorWhileTheNeighbourIsStaged_DiscardsThePreloadFirst()
        {
            yield return EnterArea(P55AreaBScene);

            AreaTransitionService transitions = Transitions();

            yield return StageTheNeighbour(transitions);

            // 捨てたことを<b>実体</b>で言えるように控えておく（工程 P55-08b）。
            AreaInstanceHandle stagedBefore = transitions.Slide.Preloader.StagedArea;
            Assert.IsTrue(stagedBefore.IsValid, "前提：隣 Area の実体を掴んでいる。");
            Assert.AreEqual(2, SceneManager.sceneCount, "前提：隣 Area が載っている。");

            var door = Object.FindFirstObjectByType<AreaTransitionDoor>();
            Assert.IsNotNull(door, "B に Interact の扉がある。");

            var root = Object.FindFirstObjectByType<PlayerRoot>();
            root.transform.position = new Vector3(
                door.transform.position.x, root.transform.position.y, door.transform.position.z);
            if (root.Body != null)
            {
                root.Body.position = root.transform.position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;

            yield return PressKeyUntil(Key.E,
                () => transitions.ArrivalCount > 0 || transitions.HasTerminalFailure, 25f);

            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, transitions.CompletedCount, "扉は従来の Single／Fade 経路で着く。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "扉はスライドしない（§3.1 の見せ方）。");
            Assert.AreEqual(1, transitions.Slide.DiscardedForSingleLoadCount,
                "Single 読込の前に先読みを捨てた。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に着いた。");

            // <b>捨てたのは「あの実体」である。</b> 着いた先も出入口の近くなので、
            // 距離による先読み（§5）が隣をもう一度持つ——枚数では捨てたことを言えない。
            yield return SettleWorld(transitions);
            Assert.AreEqual(AreaActivationPhase.Unloaded,
                transitions.Slide.Residency.PhaseOf(stagedBefore),
                "扉の前に隔離していた実体は残っていない。台帳=" + DumpResidency(transitions));
            Assert.AreNotEqual(stagedBefore, transitions.Slide.Preloader.StagedArea,
                "いま持っているのは読み直した別の実体である。");
        }

        /// <summary>
        /// <b>P11（終端待ち）</b>：先読みが<b>まだ読み終わっていない</b>うちに死んでも、
        /// Single 読込は<b>その操作が終端するまで発行しない</b>（§5 末尾「終端して隔離 Area を
        /// unload してからロードを発行する」）。
        ///
        /// ここが要点である。Unity の非同期ロードは止められないので、終端を待たずに
        /// <c>Single</c> を撃つと、遅れて読み終わった Area が<b>新しい世界の上へ足される</b>。
        /// 終端の時期を外から決められる Scene 操作を注入して、順序をそのまま観測する。
        /// </summary>
        [UnityTest]
        public IEnumerator DyingWhileTheNeighbourIsStillLoading_WaitsForItToTerminateBeforeTheFade()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new ControllableSceneHost();
            transitions.SlideSceneHost = host;

            // 読み終わらない先読みを頼む（Staged まで待たない）。
            Assert.IsTrue(
                transitions.Connections.TryGetFromExit(AreaA, ExitAEast,
                    out AreaConnectionSnapshot east),
                "東向きの接続を引ける。");
            Assert.IsTrue(
                transitions.Catalog.TryGetEntry(east.ToAreaId, east.EntryId, out AreaEntryInfo entry),
                "行き先の Scene を引ける。");
            Assert.IsTrue(transitions.Slide.Preloader.Request(east.ToAreaId, entry.ScenePath),
                "先読みを頼めた。");
            yield return null;
            Assert.AreEqual(AreaPreloadPhase.Loading, transitions.Slide.Preloader.Phase,
                "前提：まだ読込中である。");

            yield return KillPlayerWithRealHits();
            yield return WaitForRespawnPrompt();

            // 実キーで再開を要求する。<b>先読みが終端していないので進めない。</b>
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                Assert.AreEqual(0, transitions.CompletedCount,
                    "終端していない先読みの上へ Single 読込を発行しない（§5 末尾）。");
                Assert.AreEqual(0, host.UnloadCount, "まだ撤去もできない（操作が生きている）。");
            }

            Assert.AreEqual(1, transitions.Slide.DiscardedForSingleLoadCount,
                "先読みの取り下げは済んでいる（待っているのは終端だけ）。");

            // ---- 遅れて終端する ----
            host.CompleteLoad(555001);

            deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.CompletedCount == 0 && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, transitions.CompletedCount, "終端したので再開が進んだ。");
            Assert.AreEqual(1, host.UnloadCount, "遅れて着いた Area は撤去された。");
            Assert.AreEqual(555001, host.LastUnloadHandle, "撤去したのは遅れて着いたもの。");
            Assert.AreEqual(1, SceneManager.sceneCount, "新しい世界に余分な Scene が足されていない。");
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount,
                "在留台帳も実 Scene に合っている（載っている 1 枚 ＝ 在留 1 つ。工程 P55-08c）。"
                + " 以前は 0 だった——Single 読込は台帳を通らないので、活動中 Area が"
                + " 台帳に載らないままだった。距離による先読みは発行の前に活動中 Area を載せる。");

            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.IsFalse(vitals.IsDefeated, "主人公が復帰している。");
        }

        // ---------------------------------------------------------------- 後始末（§11 の P14）

        /// <summary>
        /// <b>P14</b>：往復したあと全 Area を破棄すると、<b>掃除を呼ぶ前に</b>
        /// 旧 Scene 由来の参照が 1 つも残らない。
        ///
        /// <b>テストの掃除より前に見る</b>のが要点である。<c>ClearForTests</c> を先に呼ぶと、
        /// 出荷物が自分で外しているのか、テストが後から拭いているのか区別できない。
        /// 常駐の提供点（Camera・時計）は<b>生きたまま</b>であること——
        /// 「全部空」ではなく「旧 Scene 由来だけが 0」を見る。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterAllAreasAreDestroyed_NoReferenceFromTheOldScenesRemains()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            Assert.AreEqual(1, transitions.SlideCommittedCount, "前提：スライドで B へ着いた。");

            // 全 Area を破棄する（Area を持たない Scene へ Single 読込）。
            yield return SceneManager.LoadSceneAsync(P55TrialScene, LoadSceneMode.Single);
            DestroyLaunchers();
            yield return null;
            yield return null;

            // ---- 旧 Scene 由来の参照（掃除を呼ぶ前に見る）----
            Assert.AreEqual(0, AreaBundleDirectory.Count, "参照集合の索引が空（OnDisable で外れている）。");
            Assert.IsFalse(CurrentAreaProvider.HasScope, "活動 Area の指定が落ちている。");
            Assert.AreEqual(0, AreaCameraRegionSetRegistry.Count,
                "カメラ領域集合の索引が空。" + AreaCameraRegionSetRegistry.Describe());
            Assert.AreEqual(0, PerceptionTargetRegistry.Count, "索敵の登録簿が空。");
            Assert.AreEqual(0, AreaInteractableRegistry.Count, "Interact の登録簿が空。");
            Assert.AreEqual(0, InvestigationPointRegistry.Count, "調査地点の登録簿が空。");
            Assert.IsFalse(AreaStagingRequest.IsRequested, "先読みの申し入れが残っていない。");

            Assert.AreEqual(0, Object.FindObjectsByType<AreaRoot>(FindObjectsSortMode.None).Length,
                "AreaRoot が残っていない。");
            Assert.AreEqual(0, Object.FindObjectsByType<AreaContext>(FindObjectsSortMode.None).Length,
                "AreaContext が残っていない。");
            Assert.AreEqual(0,
                Object.FindObjectsByType<AreaTransitionDisplayProxy>(FindObjectsSortMode.None).Length,
                "表示代理が残っていない（DontDestroyOnLoad なので Scene 読み替えでは消えない）。");

            IAreaTransitionDisplay display = AreaTransitionDisplayProvider.Current;
            Assert.IsNotNull(display, "表示担当は常駐なので生きている。");
            Assert.IsFalse(display.IsActive, "代理は畳まれている。");
            Assert.AreEqual(0, display.ProxyCount, "代理は 0。");
            Assert.AreEqual(0, display.HiddenRendererCount, "隠したままの Renderer も 0。");

            Assert.AreEqual(1, SceneManager.sceneCount, "実 Scene も 1 枚だけ。");
            Assert.AreEqual(0, transitions.Slide.Residency.ResidentCount, "在留台帳も空。");

            // ---- 常駐の提供点は生きている（「全部空」ではない）----
            Assert.IsNotNull(AreaCameraOwnerProvider.Current, "常駐 Camera の提供点は残る。");
            Assert.AreSame(AreaCameraRigHost.Instance, AreaCameraOwnerProvider.Current,
                "差さっているのは生きている常駐 Rig。");
            Assert.IsNotNull(GameplayClockProvider.Current, "Gameplay 時計の提供点も残る。");
            Assert.AreSame(transitions.Clock, GameplayClockProvider.Current,
                "差さっているのは常駐サービスの時計。");
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "時計は止まっていない。");
        }

        // ---------------------------------------------------------------- 先読み・死亡の補助

        /// <summary>
        /// 隣 Area を先読みして Staged まで進める（距離による先読みは §5 の後続工程なので、
        /// ここは先読みの口を直接使う）。
        /// </summary>
        private IEnumerator StageTheNeighbour(AreaTransitionService transitions)
        {
            AreaRoot here = Object.FindFirstObjectByType<AreaRoot>();
            Assert.IsNotNull(here, "いまの Area がある。");

            AreaExitGate gate = null;
            foreach (AreaExitGate candidate in
                Object.FindObjectsByType<AreaExitGate>(FindObjectsSortMode.None))
            {
                if (candidate != null && candidate.ExitId.IsValid)
                {
                    gate = candidate;
                }
            }

            Assert.IsNotNull(gate, "接続を持つ出入口がある。");
            Assert.IsTrue(
                transitions.Connections.TryGetFromExit(here.AreaId, gate.ExitId,
                    out AreaConnectionSnapshot connection),
                "その出入口の接続を引ける。");
            Assert.IsTrue(
                transitions.Catalog.TryGetEntry(connection.ToAreaId, connection.EntryId,
                    out AreaEntryInfo entry),
                "行き先の Scene を引ける。");

            AreaPreloader preloader = transitions.Slide.Preloader;
            Assert.IsTrue(preloader.Request(connection.ToAreaId, entry.ScenePath), "先読みを頼めた。");

            float deadline = Time.realtimeSinceStartup + 20f;
            while (preloader.Phase != AreaPreloadPhase.Staged && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(AreaPreloadPhase.Staged, preloader.Phase,
                "隣 Area が閉じたまま載った。理由=" + preloader.FailureReason);
            yield return null;
        }

        /// <summary>主人公を<b>実際の被弾経路</b>で死なせる（処理結果を直接セットしない）。</summary>
        private IEnumerator KillPlayerWithRealHits()
        {
            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.IsNotNull(vitals, "主人公の生存がある。");

            var attackerGo = new GameObject("P55LethalAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = vitals.transform.position + Vector3.forward;

            // 1 発で死ぬとは限らない（犬丸の「かばう」と被弾後無敵が挟まる）。
            // どちらも本番の防御経路なので、飛ばさずに届くまで殴り続ける。
            int hits = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!vitals.IsDefeated && Time.realtimeSinceStartup < deadline)
            {
                hits++;
                vitals.ReceiveHit(new HitInfo(
                    attacker, vitals, Vector3.back, vitals.transform.position,
                    new HitDamage(9999, 0f, 0f), guardable: false, justGuardable: false,
                    hitId: HitId.Single(8800 + hits)));
                yield return null;
            }

            Object.Destroy(attackerGo);
            yield return null;

            Assert.IsTrue(vitals.IsDefeated, "前提：主人公が死んでいる。打った数=" + hits);
        }

        /// <summary>再開操作が出るまで待つ（§9.1 の 1 行目）。</summary>
        private static IEnumerator WaitForRespawnPrompt()
        {
            var view = Object.FindFirstObjectByType<CampaignRespawnView>();
            Assert.IsNotNull(view, "再開操作の表示がある。");

            float deadline = Time.realtimeSinceStartup + 8f;
            while (!view.IsShowing && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(view.IsShowing, "死亡したら再開操作が出る。");
            Assert.AreEqual(GameMode.GameOver, GameModeProvider.Current.Current, "GameOver になる。");
        }

        /// <summary>押して離すを繰り返す（押下エッジを見る入力）。</summary>
        private IEnumerator PressKeyUntil(Key key, System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;

                for (int i = 0; i < 10 && !condition(); i++)
                {
                    yield return null;
                }
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }

        /// <summary>致死の攻撃元（実被弾経路を通すための最小の実装）。</summary>
        private sealed class LethalAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public int ActorId => GetInstanceID();
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
        }

        // ---------------------------------------------------------------- 先読み未完での要求（§5／§11 の P07）

        /// <summary>
        /// <b>P07</b>：先読みが未完のまま要求しても、<b>A を表示したまま待って</b>から
        /// スライドする（§5「受理時に準備不足なら、A を描画したまま操作を止めて待つ。
        /// 全画面を黒くしない」）。<b>重複ロードは起こさない。</b>
        ///
        /// 注入するのは「<b>本物のロードを、離されてから始める</b>」Scene 操作である。
        /// 偽の Scene では「A が見えたまま待つ」を見られない——到着側が本当に立ち上がって
        /// 初めてスライドへ進むので、実 Scene でなければ待ちの先が無い。
        /// </summary>
        [UnityTest]
        public IEnumerator RequestingBeforeTheNeighbourIsReady_WaitsWithTheDepartureAreaVisible()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new DelayedRealSceneHost();
            transitions.SlideSceneHost = host;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            Vector3 cameraBefore = RigPosition();

            // 受理されるまで押す（先読みは頼んでいないので、受理後に読み始める）。
            yield return HoldUntil(Key.D, () => transitions.ConnectionTravelCount > 0, 25f);
            Assert.AreEqual(1, transitions.ConnectionTravelCount, "受理された。");

            // ---- 準備できるまでの待ち：A が見えている・カメラは動かない・代理はまだ立たない ----
            int frames = 0;
            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                frames++;

                Assert.AreEqual(AreaSlideTransactionPhase.Preparing,
                    transitions.Slide.Coordinator.Phase, "準備の段階で待っている。");
                Assert.AreEqual(0, transitions.SlideCommittedCount, "まだ着いていない。");
                Assert.AreEqual(0, AreaCameraRigHost.Instance.SlideCount, "演出は始まっていない。");
                Assert.AreEqual(cameraBefore.x, RigPosition().x, 0.01f,
                    "待っている間カメラは動かない（境界へ先に飛ばない。§7.1）。");

                // <b>全画面を黒くしない</b>（§5）。出発側の地形が見えていることで見る。
                Assert.IsTrue(DepartureTerrainIsVisible(AreaA),
                    "出発 Area の地形が見えたまま待っている（暗転していない）。");

                IAreaTransitionDisplay display = AreaTransitionDisplayProvider.Current;
                Assert.IsFalse(display.IsActive, "代理は準備できてから立てる（§6.2 手順 5）。");
                AssertPlayerVisible();
            }

            Assert.Greater(frames, 10, "待ちを複数フレーム観測できた。");
            Assert.AreEqual(1, host.LoadCount, "待っている間に読み直さない（重複ロードなし。§5）。");

            // ---- 準備できたらスライドして着く ----
            host.ReleaseLoad();

            deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "準備できてからスライドして着いた。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(0, transitions.SlideRolledBackCount, "戻していない。");
            Assert.AreEqual(1, host.LoadCount, "読込は 1 回だけ。");
            Assert.AreEqual(1, AreaCameraRigHost.Instance.SlideCount, "演出も 1 回だけ。");
            Assert.Greater(RigPosition().x, cameraBefore.x + 5f, "東へスライドした。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
        }

        /// <summary>その Area の地形（床・壁）が描かれているか。暗転していないことの証拠に使う。</summary>
        private static bool DepartureTerrainIsVisible(StableId areaId)
        {
            if (!TryFindBundle(areaId, out AreaRuntimeBundle bundle) || bundle.Root == null)
            {
                return false;
            }

            foreach (MeshRenderer renderer in
                bundle.Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- 注入した失敗（§11 の P08）

        /// <summary>
        /// <b>P08</b>：到着側が立ち上がらない（参照集合を引けない）ときも、旧 Area で遊べる状態へ戻り、
        /// <b>持ち越す値も要求の勘定も変わらない</b>。そのうえで<b>もう一度操作できる</b>。
        ///
        /// 注入するのは「要求された Scene ではなく、<b>Area を持たない Scene</b> を読む」Scene 操作。
        /// 読込そのものは成功するのに到着側が立ち上がらない、という形の失敗を実サービスで作る。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheArrivalDoesNotComeUp_TheDepartureStaysOperableAndValuesAreKept()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            // <b>差し替え先は「Area でも起動 Scene でもない Scene」にする</b>（工程 P55-08b）。
            //
            // 以前は起動 Scene（試遊のランチャ）へ差し替えていた。§5 の距離による先読みが
            // 入ってから、この差し替えは<b>出入口の手前に立った時点で</b>起きる——
            // 載っている時間が長くなり、ランチャの <c>Start</c> が自分で Single 読込を始めて
            // 世界ごと入れ替わる（この検査の主題と関係のない壊れ方）。
            // 読込は成功するのに参照集合が無い、という形だけが要る。
            var host = new WrongSceneHost(EmptySystemScene);
            transitions.SlideSceneHost = host;

            GameSessionState session = GameSessionProvider.Current;
            ActorValues before = ReadActorValues();
            Assert.IsTrue(before.Found, "前提：Actor の値を読める。");

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            yield return HoldUntil(Key.D, () => transitions.SlideRolledBackCount > 0, 25f);

            Assert.AreEqual(1, transitions.ConnectionTravelCount, "受理はされた。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "成功扱いにしない。");
            Assert.AreEqual(1, transitions.SlideRolledBackCount, "出発側へ戻した。");
            Assert.IsNotEmpty(transitions.Slide.LastFailure, "理由が残っている。");
            Assert.IsFalse(session.HasVisited(AreaB), "訪問済みを増やさない（§8 末尾）。");

            // ---- 旧 Area で遊べる ----
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居る。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "A で遊べる。");
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "Gameplay 時計も戻った。");

            // 境界から離れてから数える。立ったままだと、距離による先読み（§5）が
            // 隣をもう一度持つ——それは正しい振る舞いで、ここで見たいものではない。
            // <b>主人公を動かさずに数える</b>（工程 P55-08c。GPT 再修正②）。
            // 取り下げた先読みを選び直さないので、その場に立っていても隔離は解放される。
            yield return SettleWorld(transitions);
            Assert.AreEqual(1, SceneManager.sceneCount,
                "隔離した Scene は解放されている。" + DumpWorld(transitions));
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount,
                "在留も 1 つ。" + DumpWorld(transitions));

            // ---- 持ち越す値も要求の勘定も変わらない ----
            ActorValues after = ReadActorValues();
            Assert.AreEqual(before.Hp, after.Hp, "HP を変えない。");
            Assert.AreEqual(before.CompanionState, after.CompanionState, "犬丸の状態も変えない。");
            Assert.AreEqual(1, transitions.Slide.Coordinator.AcceptedCount,
                "受理は 1 回として数えられている（勘定を失わない）。");
            Assert.AreEqual(AreaSlideTransactionPhase.Idle, transitions.Slide.Coordinator.Phase,
                "排他が解けている。");

            // ---- 直して再操作すると、こんどは着く ----
            //
            // <b>差し替えではなく、注入をやめる。</b> 先読みは最初の要求で Scene 操作を掴むので、
            // あとから <c>SlideSceneHost</c> を差し替えても効かない（実際に踏んだ）。
            host.Misdirect = false;
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "戻ったあとにもう一度操作できる（§8「失敗した出入口は一度入力を離してから再操作」）。"
                + " 理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(2, transitions.Slide.Coordinator.AcceptedCount, "受理は 2 回目。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
        }

        // ---------------------------------------------------------------- 失敗注入用の Scene 操作

        /// <summary>
        /// 終端の時期を外から決められる追加読込（監視タイムアウトと遅延完了の注入用）。
        /// <b>実 Scene は読まない。</b> 見たいのは「返ってこない操作をどう扱うか」だけである。
        /// </summary>
        private sealed class ControllableSceneHost : IAreaSceneHost
        {
            private PendingLoad _pending;
            private readonly System.Collections.Generic.HashSet<int> _loaded =
                new System.Collections.Generic.HashSet<int>();

            internal int LoadCount { get; private set; }
            internal int UnloadCount { get; private set; }
            internal int LastUnloadHandle { get; private set; }

            public IAreaSceneOperation LoadAdditive(string scenePath)
            {
                LoadCount++;
                _pending = new PendingLoad();
                return _pending;
            }

            /// <summary>true のあいだ、撤去は<b>終端しない</b>（保留する）。</summary>
            internal bool HoldUnload { get; set; }

            private PendingLoad _pendingUnload;

            public IAreaSceneOperation Unload(int sceneHandle)
            {
                UnloadCount++;
                LastUnloadHandle = sceneHandle;
                if (HoldUnload)
                {
                    _pendingUnload = new PendingLoad();
                    return _pendingUnload;
                }

                _loaded.Remove(sceneHandle);
                return new Done();
            }

            /// <summary>保留していた撤去を終端させる。</summary>
            internal void ReleaseUnload()
            {
                HoldUnload = false;
                _loaded.Remove(LastUnloadHandle);
                _pendingUnload?.Complete(LastUnloadHandle);
                _pendingUnload = null;
            }

            public bool IsLoaded(int sceneHandle) => _loaded.Contains(sceneHandle);

            /// <summary>読込を遅れて終端させる。</summary>
            internal void CompleteLoad(int sceneHandle)
            {
                _loaded.Add(sceneHandle);
                _pending?.Complete(sceneHandle);
            }

            private sealed class PendingLoad : IAreaSceneOperation
            {
                public bool IsDone { get; private set; }
                public bool HasError => false;
                public int SceneHandle { get; private set; }

                internal void Complete(int sceneHandle)
                {
                    SceneHandle = sceneHandle;
                    IsDone = true;
                }
            }

            private sealed class Done : IAreaSceneOperation
            {
                public bool IsDone => true;
                public bool HasError => false;
                public int SceneHandle => 0;
            }
        }

        /// <summary>
        /// <b>本物のロードを、離されてから始める</b>（先読み未完の待ちの注入用。§11 の P07）。
        ///
        /// 偽の Scene を返す実装では「待ったあとに本当に着く」ところまで見られない。
        /// 読込の発行そのものを遅らせるので、待っている間の状態（A が見えている・
        /// カメラが動かない・代理が立たない）をそのまま観測できる。
        /// </summary>
        private sealed class DelayedRealSceneHost : IAreaSceneHost
        {
            private readonly UnityAreaSceneHost _real = new UnityAreaSceneHost();
            private DelayedLoad _pending;

            /// <summary>読込を頼まれた回数（重複ロードを数える）。</summary>
            internal int LoadCount { get; private set; }

            public IAreaSceneOperation LoadAdditive(string scenePath)
            {
                LoadCount++;
                _pending = new DelayedLoad(_real, scenePath);
                return _pending;
            }

            /// <summary>true のあいだ、撤去は<b>本物を始めずに</b>待つ。</summary>
            internal bool HoldUnload { get; set; }

            private DelayedUnload _pendingUnload;

            /// <summary>撤去を頼まれた回数。</summary>
            internal int UnloadCount { get; private set; }

            public IAreaSceneOperation Unload(int sceneHandle)
            {
                UnloadCount++;
                if (!HoldUnload)
                {
                    return _real.Unload(sceneHandle);
                }

                _pendingUnload = new DelayedUnload(_real, sceneHandle);
                return _pendingUnload;
            }

            public bool IsLoaded(int sceneHandle) => _real.IsLoaded(sceneHandle);

            /// <summary>本物の読込を始めさせる。</summary>
            internal void ReleaseLoad() => _pending?.Release();

            /// <summary>保留していた撤去を本物として始めさせる。</summary>
            internal void ReleaseUnload()
            {
                HoldUnload = false;
                _pendingUnload?.Release();
            }

            private sealed class DelayedUnload : IAreaSceneOperation
            {
                private readonly UnityAreaSceneHost _real;
                private readonly int _sceneHandle;
                private IAreaSceneOperation _inner;

                internal DelayedUnload(UnityAreaSceneHost real, int sceneHandle)
                {
                    _real = real;
                    _sceneHandle = sceneHandle;
                }

                public bool IsDone => _inner != null && _inner.IsDone;
                public bool HasError => _inner != null && _inner.HasError;
                public int SceneHandle => _inner != null ? _inner.SceneHandle : 0;

                internal void Release()
                {
                    _inner ??= _real.Unload(_sceneHandle);
                }
            }

            private sealed class DelayedLoad : IAreaSceneOperation
            {
                private readonly UnityAreaSceneHost _real;
                private readonly string _scenePath;
                private IAreaSceneOperation _inner;

                internal DelayedLoad(UnityAreaSceneHost real, string scenePath)
                {
                    _real = real;
                    _scenePath = scenePath;
                }

                public bool IsDone => _inner != null && _inner.IsDone;
                public bool HasError => _inner != null && _inner.HasError;
                public int SceneHandle => _inner != null ? _inner.SceneHandle : 0;

                internal void Release()
                {
                    _inner ??= _real.LoadAdditive(_scenePath);
                }
            }
        }

        /// <summary>
        /// 頼まれた Scene ではなく<b>別の Scene</b>を読む（到着側が立ち上がらない失敗の注入用）。
        /// 読込は成功するのに参照集合が無い、という形を実サービスで作る。
        /// </summary>
        private sealed class WrongSceneHost : IAreaSceneHost
        {
            private readonly UnityAreaSceneHost _real = new UnityAreaSceneHost();
            private readonly string _insteadOf;

            internal WrongSceneHost(string insteadOf)
            {
                _insteadOf = insteadOf;
            }

            /// <summary>
            /// 別の Scene へ差し替えるか。<b>途中で止められるようにしておく</b>——
            /// 先読みは最初の要求で Scene 操作を掴むので、あとから
            /// <c>SlideSceneHost</c> を差し替えても効かない（実際に踏んだ）。
            /// </summary>
            internal bool Misdirect { get; set; } = true;

            public IAreaSceneOperation LoadAdditive(string scenePath) =>
                _real.LoadAdditive(Misdirect ? _insteadOf : scenePath);

            public IAreaSceneOperation Unload(int sceneHandle) => _real.Unload(sceneHandle);

            public bool IsLoaded(int sceneHandle) => _real.IsLoaded(sceneHandle);
        }

        /// <summary>
        /// 読込は本物、<b>撤去だけ断る</b>（Commit 後の撤去失敗の注入用）。
        /// 撤去が成功したことにしないために <c>IsLoaded</c> も本物へ委ねる。
        /// </summary>
        private sealed class UnloadRefusingSceneHost : IAreaSceneHost
        {
            private readonly UnityAreaSceneHost _real = new UnityAreaSceneHost();
            private HeldUnload _held;

            internal bool RefuseUnload { get; set; } = true;

            /// <summary>
            /// true のあいだ、撤去は<b>本物を始めずに待つ</b>（工程 P55-07c）。
            /// 「再試行の Unload が走っている最中」を作るための保留。
            /// </summary>
            internal bool HoldUnload { get; set; }

            /// <summary>撤去を頼まれた回数。</summary>
            internal int UnloadCount { get; private set; }

            public IAreaSceneOperation LoadAdditive(string scenePath) => _real.LoadAdditive(scenePath);

            public IAreaSceneOperation Unload(int sceneHandle)
            {
                UnloadCount++;
                if (RefuseUnload)
                {
                    return new Failed();
                }

                if (!HoldUnload)
                {
                    return _real.Unload(sceneHandle);
                }

                _held = new HeldUnload(_real, sceneHandle);
                return _held;
            }

            /// <summary>保留していた撤去を本物として始めさせる。</summary>
            internal void ReleaseUnload()
            {
                HoldUnload = false;
                _held?.Release();
            }

            public bool IsLoaded(int sceneHandle) => _real.IsLoaded(sceneHandle);

            private sealed class HeldUnload : IAreaSceneOperation
            {
                private readonly UnityAreaSceneHost _real;
                private readonly int _sceneHandle;
                private IAreaSceneOperation _inner;

                internal HeldUnload(UnityAreaSceneHost real, int sceneHandle)
                {
                    _real = real;
                    _sceneHandle = sceneHandle;
                }

                public bool IsDone => _inner != null && _inner.IsDone;
                public bool HasError => _inner != null && _inner.HasError;
                public int SceneHandle => _inner != null ? _inner.SceneHandle : 0;

                internal void Release()
                {
                    _inner ??= _real.Unload(_sceneHandle);
                }
            }

            private sealed class Failed : IAreaSceneOperation
            {
                public bool IsDone => true;
                public bool HasError => true;
                public int SceneHandle => 0;
            }
        }

        /// <summary>
        /// <b>Single 読込の発行そのものを数える</b>（工程 P55-07c。GPT 再修正③）。
        ///
        /// 「発行していない」を <c>CompletedCount</c> で見ると、実際には
        /// <b>「まだ終わっていない」しか言えない</b>——読込に要する時間しだいで、
        /// 発行済みでも 0 のままになる。数えるのは
        /// <see cref="IAreaSceneLoader.Load"/> の呼出そのものにする。
        ///
        /// 実装は<b>本物への転送</b>にする。偽の操作を返すと読込が完走しなくなり、
        /// 「終端後に一度だけ着く」まで同じテストで見られない（注入 52C と同じ考え方）。
        /// </summary>
        private sealed class CountingSceneLoader : IAreaSceneLoader
        {
            private readonly IAreaSceneLoader _inner;

            internal CountingSceneLoader(IAreaSceneLoader inner)
            {
                _inner = inner ?? new UnitySceneLoader();
            }

            /// <summary>読込を頼まれた回数（＝ Single 読込の発行数）。</summary>
            internal int LoadCount { get; private set; }

            public IAreaLoadOperation Load(string scenePath)
            {
                LoadCount++;
                return _inner.Load(scenePath);
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

        /// <summary>
        /// 読込の開始そのものが失敗する追加読込（準備失敗の注入用。§11 の P08）。
        /// <b>途中で止められる</b>——先読みは最初の要求で Scene 操作を掴むので、
        /// あとから <c>SlideSceneHost</c> を差し替えても効かない。
        /// </summary>
        private sealed class AlwaysFailingSceneHost : IAreaSceneHost
        {
            private readonly UnityAreaSceneHost _real = new UnityAreaSceneHost();

            /// <summary>失敗させるか。</summary>
            internal bool Failing { get; set; } = true;

            public IAreaSceneOperation LoadAdditive(string scenePath) =>
                Failing ? new Failed() : _real.LoadAdditive(scenePath);

            public IAreaSceneOperation Unload(int sceneHandle) =>
                Failing ? new Failed() : _real.Unload(sceneHandle);

            public bool IsLoaded(int sceneHandle) => !Failing && _real.IsLoaded(sceneHandle);

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

        /// <summary>
        /// <b>撤去と、距離による先読みの取り直しが落ち着くまで待つ</b>（工程 P55-08b）。
        ///
        /// §5 の距離による先読みが入ってから、<b>境界の近くで立ち止まっている状態は
        /// 「Scene 2 枚」が正常</b>になった——活動中 Area と、6 units 以内の出入口の先である。
        /// 到着した直後の主人公は逆向きの出入口のすぐ内側に居るので、
        /// 旧 Area を撤去したあと<b>同じ Area をもう一度 Staged で持つ</b>。
        ///
        /// 途中の枚数を数えると<b>フレームの巡り合わせで割れる</b>ので、
        /// 撤去も先読みも走っていない状態まで進めてから数える。
        /// </summary>
        private static IEnumerator SettleWorld(AreaTransitionService transitions, float seconds = 15f)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            int quiet = 0;
            while (quiet < 3 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;

                AreaPreloader preloader = transitions.Slide.Preloader;
                bool busy = transitions.Slide.IsTransitioning
                    || transitions.Slide.IsRetireInFlight
                    || preloader.HasLiveSceneOperation
                    || transitions.IsSingleLoadInFlight;
                quiet = busy ? 0 : quiet + 1;
            }
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

        /// <summary>目的の段階になるまで押しっぱなしにする。</summary>
        private IEnumerator HoldUntilPhase(Key key, AreaSlideTransactionPhase phase, float seconds)
        {
            AreaTransitionService transitions = Transitions();
            float deadline = Time.realtimeSinceStartup + seconds;
            while (transitions.Slide.Coordinator.Phase != phase && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
        }

        /// <summary>その AreaId の参照集合を索引から引く（全 Scene 検索をしない）。</summary>
        private static bool TryFindBundle(StableId areaId, out AreaRuntimeBundle bundle)
        {
            foreach (AreaRuntimeBundle candidate in AreaBundleDirectory.All)
            {
                if (candidate != null && candidate.AreaId.Equals(areaId))
                {
                    bundle = candidate;
                    return true;
                }
            }

            bundle = null;
            return false;
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

        // ---------------------------------------------------------------- GPT 受入①（P55-07a）

        /// <summary>
        /// <b>先読みが終端しないまま時間切れになっても、Single 読込を発行しない</b>
        /// （§5・§8 の「Scene 操作は 1 つずつ」。GPT 受入①）。
        ///
        /// 以前は時間切れで警告だけ残して先へ進んでいた。Single 側の監視
        /// （<c>AreaTransitionService._liveOperation</c>）は Additive の先読み操作を
        /// <b>持っていない</b>ので、そこでは重なりを止められない。
        /// 死亡再開・暗転扉と未完了の先読みが重なる経路だった。
        ///
        /// <b>既存のテストは約 2 秒で先読みを完了させていたので、この区間を通っていなかった。</b>
        /// ここでは監視時間を短くして、時間切れそのものを踏む。
        /// </summary>
        [UnityTest]
        public IEnumerator TimingOutTheStagedDiscard_DoesNotIssueTheSingleLoad()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new ControllableSceneHost();
            transitions.SlideSceneHost = host;
            transitions.TimeoutSeconds = 0.3f;

            // 終端しない先読みを 1 つ抱えさせる。
            transitions.Slide.Preloader.Request(AreaB, P55AreaBScene);
            yield return null;
            transitions.Slide.Preloader.Poll();
            Assert.AreEqual(1, host.LoadCount, "前提：先読みのロードが 1 本走っている。");
            Assert.AreEqual(AreaPreloadPhase.Loading, transitions.Slide.Preloader.Phase,
                "前提：先読みは読込中のまま。");

            // ---- 死亡再開を要求する（Single 読込の経路）----
            //
            // 進めないことは<b>終端失敗として記録される</b>ので Error が出る。
            // これは期待どおりの出力である（黙って進むほうが不合格）。
            // 再開は<b>何度でも試せる</b>ので、押すたびに 1 本出る——本数は固定しない。
            //
            // <b>必ず戻す。</b> 途中で Assert が落ちて戻らないと、
            // <b>後続のテストの Error を黙って飲む</b>（GPT 追加の指摘）。
            yield return IgnoringExpectedErrors(RunTimeoutBody(transitions, host));
        }

        /// <summary>
        /// 期待どおり Error が出る区間を包む。<b>落ちても必ず元へ戻す</b>。
        ///
        /// 本数が変わる（再開を押すたびに 1 本出る）ので <c>LogAssert.Expect</c> では書けない。
        /// 広く無視するぶん、<b>戻し忘れないこと</b>が条件になる。
        /// </summary>
        private static IEnumerator IgnoringExpectedErrors(IEnumerator body)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                while (body.MoveNext())
                {
                    yield return body.Current;
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        /// <summary>時間切れテストの本体（Error が出る区間）。</summary>
        private IEnumerator RunTimeoutBody(AreaTransitionService transitions, ControllableSceneHost host)
        {
            yield return KillPlayerWithRealHits();
            yield return WaitForRespawnPrompt();
            yield return PressKeyUntil(Key.Enter,
                () => transitions.CompletedCount > 0
                      || transitions.Slide.StagedDiscardBlockedCount > 0, 15f);

            Assert.AreEqual(0, transitions.CompletedCount,
                "終端していない先読みの上へ Single 読込を発行しない（§5 末尾）。");
            Assert.IsTrue(transitions.HasTerminalFailure,
                "進めないことを終端失敗として伝える（黙って進まない）。理由="
                + transitions.TerminalFailureReason);
            Assert.GreaterOrEqual(transitions.Slide.StagedDiscardBlockedCount, 1,
                "「終端できないので発行しなかった」を数えている。");
            Assert.IsFalse(transitions.Slide.StagedDiscardCompleted,
                "先読みの終端を見届けられていない。");
            Assert.AreEqual(0, host.UnloadCount,
                "終端していない操作を撤去しに行かない（所有権は手放さない）。");
            Assert.AreEqual(1, host.LoadCount, "重ねてロードもしていない。");

            // ---- 遅れて終端する → 撤去 → Single 読込が一度だけ ----
            host.CompleteLoad(770001);
            yield return null;

            yield return PressKeyUntil(Key.Enter,
                () => transitions.CompletedCount > 0, 25f);

            var respawnView = Object.FindFirstObjectByType<CampaignRespawnView>();
            Assert.AreEqual(1, transitions.CompletedCount,
                "終端したあとに Single 読込で着く。"
                + " 先読み=" + transitions.Slide.Preloader.Phase
                + " 見届けた=" + transitions.Slide.StagedDiscardCompleted
                + " 阻止=" + transitions.Slide.StagedDiscardBlockedCount
                + " 捨てた=" + transitions.Slide.DiscardedForSingleLoadCount
                + " 撤去=" + host.UnloadCount + " 読込=" + host.LoadCount
                + " 終端失敗=" + transitions.HasTerminalFailure
                + "（" + transitions.TerminalFailureReason + "）"
                + " 再開段階=" + (GameSessionProvider.Current != null
                    ? GameSessionProvider.Current.Respawn.Phase.ToString() : "null")
                + " 再開表示=" + (respawnView != null && respawnView.IsShowing)
                + " mode=" + (GameModeProvider.Current != null
                    ? GameModeProvider.Current.Current.ToString() : "null"));
            Assert.AreEqual(1, host.UnloadCount, "先読みの撤去は一度だけ。");
            Assert.AreEqual(1, host.LoadCount, "先読みのロードは増えていない。");
            Assert.IsTrue(transitions.Slide.StagedDiscardCompleted, "今度は終端を見届けた。");
            Assert.AreEqual(1, SceneManager.sceneCount, "隔離 Area は残っていない。");
            Assert.AreEqual(AreaPreloadPhase.Idle, transitions.Slide.Preloader.Phase, "先読みは手ぶら。");

            // ---- Launcher 退避も同じ所有状態を見る（GPT 追加①）----
            //
            // ここでは所有が解けているので退避できる。<b>解けていない間に断ること</b>は
            // <c>LauncherRetreatDuringAStagedLoad_IssuesNoSingleLoad</c> が見る。
            transitions.LauncherScenePath = P55TrialScene;
            Assert.IsFalse(transitions.HasTerminalFailure, "終端失敗は解消している。");
        }

        // ---------------------------------------------------------------- GPT 受入②（P55-07a）

        /// <summary>
        /// <b>到着通知の中から次のスライドを頼んでも、撤去の終わりを待って成立する</b>
        /// （§8 末尾「旧 Area の解放終了を待ってから次のロードへ進む」。GPT 受入②）。
        ///
        /// 到着通知は<b>撤去より先に</b>出る（§6.2 手順 10→11）。通知の時点ではまだ
        /// 旧 Area と到着 Area の 2 枚が在留しているので、そのまま先読みを頼むと
        /// 在留上限で失敗する——<b>受理できたのに進めない</b>という形で落ちていた。
        ///
        /// <b>撤去失敗（<c>HasPendingRetire</c>）とは別物である。</b> あちらは断る。
        /// こちらは<b>待たせる</b>。同じ 1 つの状態で表すと、待てばよいものを断ることになる。
        /// </summary>
        [UnityTest]
        public IEnumerator RequestingTheReverseSlideInsideTheArrival_WaitsForTheRetire()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            int notified = 0;
            bool requested = false;
            AreaTransitionDecision inside = default;

            void OnArrived(StableId areaId)
            {
                notified++;
                if (requested || !areaId.Equals(AreaB))
                {
                    return;
                }

                requested = true;

                // <b>通知の中で</b>逆方向を頼む（ここが要点）。
                Assert.IsTrue(
                    transitions.Connections.TryGetReverse(
                        transitions.LastAcceptedConnection.ConnectionId,
                        out AreaConnectionSnapshot reverse),
                    "逆方向の接続が引ける。");
                inside = transitions.TryTravel(reverse);
            }

            transitions.ArrivalCompleted += OnArrived;
            try
            {
                yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
                yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);

                float deadline = Time.realtimeSinceStartup + 30f;
                while ((transitions.SlideCommittedCount < 2 || SceneManager.sceneCount > 1)
                       && !transitions.HasTerminalFailure
                       && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }
            finally
            {
                transitions.ArrivalCompleted -= OnArrived;
            }

            Assert.IsTrue(requested, "前提：通知の中から要求を出した。");
            Assert.IsTrue(inside.Accepted,
                "通知の中からの要求が受理される（理由=" + inside.Rejection + "）。");

            Assert.AreEqual(2, transitions.SlideCommittedCount,
                "二度目の到着も成立する（撤去の終わりを待ってから進んだ。失敗="
                + transitions.Slide.LastFailure + "）。");
            Assert.GreaterOrEqual(transitions.Slide.RetireWaitCount, 1,
                "撤去の終わりを実際に待った（待たずに通ったなら、この経路を見ていない）。");
            Assert.AreEqual(0, transitions.Slide.RolledBackCount, "戻していない。");
            Assert.AreEqual(0, transitions.Slide.UnloadFailureCount, "撤去は失敗していない。");
            Assert.IsFalse(transitions.Slide.HasPendingRetire, "抱えたままの旧 Area も無い。");

            Assert.AreEqual(2, notified, "成功通知は到着ごとに 1 回ずつ（重複していない）。");
            Assert.AreEqual(0, transitions.CompletedCount, "Single 経路は通っていない。");
            Assert.AreEqual(1, SceneManager.sceneCount, "Scene は 1 枚に戻る（重ねて載っていない）。");
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount, "在留は 1 つだけ。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に戻っている。");
        }

        /// <summary>
        /// <b>到着通知の中から Fade（Single 読込）を頼んでも、旧側の撤去と重ならない</b>
        /// （GPT 受入②の 4 行目）。
        ///
        /// Single は全部を置き換えるので待たせる必要は無いが、<b>撤去と重ねてはいけない</b>。
        /// 重なると、撤去し損ねた Scene が新しい世界の上に残る。
        /// </summary>
        [UnityTest]
        public IEnumerator RequestingAFadeInsideTheArrival_DoesNotOverlapTheRetire()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            bool requested = false;
            bool accepted = false;

            void OnArrived(StableId areaId)
            {
                if (requested || !areaId.Equals(AreaB))
                {
                    return;
                }

                requested = true;
                accepted = transitions.TryTravel(AreaA, Phase5AreaIdsAreaAFromB).Accepted;
            }

            int departureSceneBefore = 0;
            transitions.ArrivalCompleted += OnArrived;
            try
            {
                yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);

                Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle before), "出発 Area がある。");
                departureSceneBefore = before.SceneHandle;

                yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);

                float deadline = Time.realtimeSinceStartup + 30f;
                while ((transitions.ArrivalCount < 2
                        || transitions.SlideSceneHost.IsLoaded(departureSceneBefore))
                       && !transitions.HasTerminalFailure
                       && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }
            finally
            {
                transitions.ArrivalCompleted -= OnArrived;
            }

            Assert.IsTrue(requested, "前提：通知の中から Fade を要求した。");
            Assert.IsTrue(accepted, "Single は全部を置き換えるので受理してよい。");
            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, transitions.CompletedCount, "Single 経路で一度だけ着く。");

            // <b>残っていないことは「あの Scene が消えた」で言う</b>（工程 P55-08b）。
            // 枚数では言えない——着いた先も出入口の近くなので、距離による先読みが隣を載せる。
            yield return SettleWorld(transitions);
            Assert.IsFalse(transitions.SlideSceneHost.IsLoaded(departureSceneBefore),
                "撤去し損ねた Scene が新しい世界へ残っていない。" + DumpWorld(transitions));
            Assert.AreEqual(0, transitions.Slide.UnloadFailureCount, "撤去も失敗していない。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居る。");
        }

        /// <summary>P5 の入口 ID（この配置でも再利用している）。</summary>
        private static readonly StableId Phase5AreaIdsAreaAFromB = new StableId("area_p5_a_from_b");


        // ---------------------------------------------------------------- GPT 追加①（P55-07a2）

        /// <summary>
        /// <b>Launcher 退避も、先読みを迂回しない</b>（GPT 追加①）。
        ///
        /// 通常の遷移は先読みの終端を見るようになったが、そこで出るエラーからの
        /// <c>TryBeginReturnToLauncher</c> は <c>_liveOperation</c> だけを見ていた。
        /// <b>この変数は Additive の先読みを管理していない。</b>
        /// 先読みタイムアウト →「Launcher へ戻る」で、未完了の先読みと Single が重なる経路が残っていた。
        ///
        /// <b>入口ごとに条件を足すのではなく、同じ所有状態を見る形に揃えた。</b>
        /// </summary>
        [UnityTest]
        public IEnumerator LauncherRetreatDuringAStagedLoad_IssuesNoSingleLoad()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new ControllableSceneHost();
            transitions.SlideSceneHost = host;
            transitions.LauncherScenePath = P55TrialScene;

            // 終端しない先読みを抱えさせる。
            transitions.Slide.Preloader.Request(AreaB, P55AreaBScene);
            yield return null;
            transitions.Slide.Preloader.Poll();
            Assert.AreEqual(1, host.LoadCount, "前提：先読みのロードが走っている。");
            Assert.IsTrue(transitions.Slide.HasLiveSceneOperation,
                "前提：終端していない Scene 操作を掴んでいる。");

            // <b>GPT が指摘した経路そのものを踏む</b>：先読みタイムアウト →
            // そこで出るエラーからの「Launcher へ戻る」。
            transitions.TimeoutSeconds = 0.3f;
            yield return IgnoringExpectedErrors(FailByRespawnWhileStaged(transitions));
            Assert.IsTrue(transitions.HasTerminalFailure, "前提：終端失敗になっている。");
            Assert.AreEqual(0, transitions.CompletedCount, "前提：Single はまだ発行されていない。");

            int scenesBefore = SceneManager.sceneCount;
            int returnedBefore = transitions.ReturnedToLauncherCount;

            Assert.IsFalse(transitions.CanReturnToLauncher,
                "先読みが終端していない間は退避できない（§5「終端してから発行する」）。");
            Assert.IsFalse(transitions.TryBeginReturnToLauncher(),
                "退避の要求そのものを断る。");

            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(returnedBefore, transitions.ReturnedToLauncherCount,
                "Single ロードは 1 件も発行していない。");
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount, "Scene も置き換わっていない。");
            Assert.AreEqual(1, host.LoadCount, "先読みのロードも重なっていない。");

            // ---- 終端すれば退避できる ----
            host.CompleteLoad(781001);
            yield return null;
            transitions.Slide.Preloader.Poll();
            yield return null;

            Assert.IsFalse(transitions.Slide.HasLiveSceneOperation, "操作は終端した。");
            Assert.IsTrue(transitions.CanReturnToLauncher, "終端したので退避できる。");
        }

        // ---------------------------------------------------------------- GPT 追加②（P55-07a2）

        /// <summary>
        /// <b>Fade も、旧 Area の撤去が終わるまでロードを発行しない</b>（GPT 追加②）。
        ///
        /// 「Single は全部を置き換えるから待たせない」と書いていたが、それは
        /// <b>操作の非重複という別の契約</b>を無視していた。撤去の操作が走っている最中に
        /// Single を撃てば、終端していない操作の上へ新しい操作を重ねることになる。
        ///
        /// <b>撤去を意図的に終端させずに</b>、その間の Single 発行数が 0 であることを見る。
        /// 前のテスト（到着・Scene 数・失敗数だけを見る）では、
        /// <b>同時に走らなかったこと自体</b>を観測できていなかった。
        /// </summary>
        [UnityTest]
        public IEnumerator FadeWhileTheRetireIsStillRunning_WaitsForItToFinish()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // <b>本物の Scene を使う。</b> 偽 Scene では到着そのものが起きないので、
            // 「到着通知の中から頼む」という条件が作れない。
            var host = new DelayedRealSceneHost();
            transitions.SlideSceneHost = host;

            // <b>発行そのものを数える</b>（工程 P55-07c。GPT 再修正③）。
            // 以前はここを <c>CompletedCount</c> で見ていたが、それは「まだ終わっていない」
            // としか言えず、読込の速さに寄りかかった観測だった。
            var loader = new CountingSceneLoader(transitions.Loader);
            transitions.Loader = loader;

            bool requested = false;
            bool accepted = false;
            void OnArrived(StableId areaId)
            {
                if (requested || !areaId.Equals(AreaB))
                {
                    return;
                }

                requested = true;
                accepted = transitions.TryTravel(AreaA, AreaAFromBEntry).Accepted;
            }

            transitions.ArrivalCompleted += OnArrived;
            try
            {
                yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);

                // <b>撤去だけを止める。</b> ここからの Single は、走っている撤去と重なってはいけない。
                host.HoldUnload = true;

                // <b>読込は毎フレーム解放する</b>（この host は既定で読込を保留する）。
                // ここで止めたいのは<b>撤去だけ</b>である。
                float slideDeadline = Time.realtimeSinceStartup + 25f;
                while (transitions.SlideCommittedCount == 0
                       && !transitions.HasTerminalFailure
                       && Time.realtimeSinceStartup < slideDeadline)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                    yield return null;
                    host.ReleaseLoad();
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;

                Assert.IsFalse(transitions.HasTerminalFailure,
                    "前提：スライドは成功する。理由=" + transitions.TerminalFailureReason);
                Assert.AreEqual(1, transitions.SlideCommittedCount, "前提：スライドで着いた。");
                Assert.IsTrue(requested, "前提：到着通知の中から Fade を要求した。");
                Assert.IsTrue(accepted, "前提：Fade の要求は受理された。");
                Assert.IsTrue(transitions.Slide.IsRetiring, "前提：撤去がまだ走っている。");

                // ---- 撤去が終わるまで Single は発行されない ----
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    Assert.AreEqual(0, loader.LoadCount,
                        "撤去が走っている間は Single 読込を<b>発行</b>しない（GPT 追加②・再修正③）。"
                        + " 撤去中=" + transitions.Slide.IsRetiring);
                }

                Assert.IsTrue(transitions.Slide.IsRetiring, "まだ撤去中のまま（前提が崩れていない）。");
                Assert.AreEqual(1, host.UnloadCount, "撤去は一度だけ頼まれている。");

                // ---- 撤去が終端すれば、一度だけ発行される ----
                host.ReleaseUnload();

                float deadline = Time.realtimeSinceStartup + 25f;
                while (transitions.CompletedCount == 0
                       && !transitions.HasTerminalFailure
                       && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }
            finally
            {
                transitions.ArrivalCompleted -= OnArrived;
            }

            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, loader.LoadCount, "撤去のあとに一度だけ<b>発行</b>される。");
            Assert.AreEqual(1, transitions.CompletedCount, "その発行が着いた。");
            Assert.IsFalse(transitions.Slide.IsRetiring, "撤去は終わっている。");
            Assert.AreEqual(1, SceneManager.sceneCount, "Scene は 1 枚に戻る。");
        }

        // ---------------------------------------------------------------- GPT 追加③（P55-07a2）

        /// <summary>
        /// <b>先読みが失敗したあとでも、扉移動と死亡再開は止まらない</b>（GPT 追加③）。
        ///
        /// <c>Failed</c> は<b>操作も Scene も手放したあとの履歴</b>である。
        /// <c>ClearRequest</c>／<c>Poll</c> では Idle へ戻らないので、
        /// これを「まだ掴んでいる」と同じに扱うと、<b>一度先読みに失敗しただけで
        /// 死亡再開も扉移動も毎回タイムアウトする</b>。
        ///
        /// 未完了の <c>Loading</c> や、Scene が残る撤去失敗と<b>同じ扱いにしない</b>。
        /// </summary>
        [UnityTest]
        public IEnumerator TravellingAfterAFailedPreload_IsNotBlocked()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var sceneHost = new AlwaysFailingSceneHost();
            transitions.SlideSceneHost = sceneHost;

            // 先読みを失敗させる。
            transitions.Slide.Preloader.Request(AreaB, P55AreaBScene);
            for (int i = 0; i < 10 && transitions.Slide.Preloader.Phase != AreaPreloadPhase.Failed; i++)
            {
                yield return null;
                transitions.Slide.Preloader.Poll();
            }

            Assert.AreEqual(AreaPreloadPhase.Failed, transitions.Slide.Preloader.Phase,
                "前提：先読みが失敗している。");
            Assert.IsFalse(transitions.Slide.HasLiveSceneOperation,
                "失敗は終端している（操作を掴んでいない）。");
            Assert.IsFalse(transitions.Slide.HoldsRemainingScene,
                "残留物も無い（Scene を預かっていない）。");

            // ---- 死亡再開が通る ----
            sceneHost.Failing = false;
            yield return KillPlayerWithRealHits();
            yield return WaitForRespawnPrompt();
            yield return PressKeyUntil(Key.Enter,
                () => transitions.CompletedCount > 0 || transitions.HasTerminalFailure, 25f);

            Assert.IsFalse(transitions.HasTerminalFailure,
                "先読みの失敗が死亡再開を止めない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, transitions.CompletedCount, "暗転経路で再開できる。");
            Assert.AreEqual(0, transitions.Slide.StagedDiscardBlockedCount,
                "「終端していない」と誤って数えていない（Failed は履歴であって所有ではない）。");
            Assert.AreEqual(1, SceneManager.sceneCount, "Scene は 1 枚。");
        }

        /// <summary>先読みを抱えたまま死亡再開を試して、終端失敗を作る（Error が出る区間）。</summary>
        private IEnumerator FailByRespawnWhileStaged(AreaTransitionService transitions)
        {
            yield return KillPlayerWithRealHits();
            yield return WaitForRespawnPrompt();
            yield return PressKeyUntil(Key.Enter,
                () => transitions.CompletedCount > 0
                      || transitions.Slide.StagedDiscardBlockedCount > 0, 15f);
        }

        /// <summary>A の「B から戻る」入口（この配置でも P5 の ID を再利用している）。</summary>
        private static readonly StableId AreaAFromBEntry = new StableId("area_p5_a_from_b");


        // ---------------------------------------------------------------- GPT 受入③（P55-07b）

        /// <summary>
        /// <b>受理からCommit後まで、主人公の絵はどのフレームでもちょうど 1 つ</b>（GPT 受入③）。
        ///
        /// 以前は、到着側の活動ゲートを開けてから <c>HideArrivals</c> を呼ぶまでに
        /// 何フレームもあった（準備完了待ち＋1 フレーム＋終点計算）。その間、
        /// <b>出発側の代理と到着側の実 Actor が同時に映りうる</b>——
        /// 東西配置では到着入口も出発カメラの画角に入る。
        ///
        /// 実描画の検査（§11 の P15）は <c>IsSliding</c> になってから撮るので、
        /// <b>この準備区間は対象外だった</b>。ここは受理の瞬間から数える。
        /// </summary>
        [UnityTest]
        public IEnumerator FromAcceptanceToCommit_ExactlyOneHeroIsDrawnEveryFrame()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            int worstPlayers = 0;
            int fewestPlayers = int.MaxValue;
            int worstCompanions = 0;
            int frames = 0;
            string worstDetail = string.Empty;

            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;

                if (transitions.Slide.Coordinator.Phase == AreaSlideTransactionPhase.Idle)
                {
                    continue; // まだ受理されていない。
                }

                frames++;
                int players = CountDrawnPlayers(out string detail);
                int companions = CountDrawnCompanions();
                if (players > worstPlayers)
                {
                    worstDetail = detail;
                }

                worstPlayers = Mathf.Max(worstPlayers, players);
                fewestPlayers = Mathf.Min(fewestPlayers, players);
                worstCompanions = Mathf.Max(worstCompanions, companions);
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

            // Commit の<b>あと</b>も数える（代理を畳んだ瞬間に消える／重なるのを見る）。
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                frames++;
                int players = CountDrawnPlayers(out string detail);
                if (players > worstPlayers)
                {
                    worstDetail = detail;
                }

                worstPlayers = Mathf.Max(worstPlayers, players);
                fewestPlayers = Mathf.Min(fewestPlayers, players);
                worstCompanions = Mathf.Max(worstCompanions, CountDrawnCompanions());
            }

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドで着いた（失敗=" + transitions.Slide.LastFailure + "）。");
            Assert.Greater(frames, 5, "受理からCommit後まで複数フレームを数えている（" + frames + "）。");

            Assert.AreEqual(1, worstPlayers,
                "主人公の絵が 2 つ映ったフレームがある（最大 " + worstPlayers
                + "／" + worstDetail + "）。準備中から隠し続けること（§7.2）。");
            Assert.AreEqual(1, fewestPlayers,
                "主人公の絵が 0 になったフレームがある（最小 " + fewestPlayers
                + "）。受け渡しで一瞬の欠落を作らない（§7.2）。");
            Assert.AreEqual(1, worstCompanions,
                "犬丸の絵が 2 つ映ったフレームがある（最大 " + worstCompanions + "）。");
        }

        /// <summary>いま<b>描かれている</b>主人公の数（実 Actor すべて ＋ 表示代理）。</summary>
        private static int CountDrawnPlayers(out string detail)
        {
            int count = 0;
            var sb = new System.Text.StringBuilder();
            foreach (PlayerRoot root in Object.FindObjectsByType<PlayerRoot>(FindObjectsSortMode.None))
            {
                if (root != null && root.VisualRoot != null
                    && HasVisibleSprite(root.VisualRoot, out string which))
                {
                    count++;
                    sb.Append("[実 ").Append(root.gameObject.scene.name).Append(' ').Append(which).Append(']');
                }
            }

            AreaTransitionDisplayHost display = AreaTransitionDisplayHost.Instance;
            AreaTransitionDisplayProxy proxy = display != null && display.Set != null
                ? display.Set.Player : null;
            if (proxy != null && proxy.Renderer != null && proxy.Renderer.enabled
                && proxy.Renderer.sprite != null && proxy.gameObject.activeInHierarchy)
            {
                count++;
                sb.Append("[代理]");
            }

            detail = sb.ToString();
            return count;
        }

        /// <summary>いま描かれている犬丸の数（退場中は 0 でよい）。</summary>
        private static int CountDrawnCompanions()
        {
            int count = 0;
            foreach (CompanionActor actor in
                Object.FindObjectsByType<CompanionActor>(FindObjectsSortMode.None))
            {
                if (actor != null && HasVisibleSprite(actor.transform, out _))
                {
                    count++;
                }
            }

            AreaTransitionDisplayHost display = AreaTransitionDisplayHost.Instance;
            AreaTransitionDisplayProxy proxy = display != null && display.Set != null
                ? display.Set.Companion : null;
            if (proxy != null && proxy.Renderer != null && proxy.Renderer.enabled
                && proxy.Renderer.sprite != null && proxy.gameObject.activeInHierarchy)
            {
                count++;
            }

            return count;
        }

        // ---------------------------------------------------------------- GPT 受入④（P55-07b）

        /// <summary>
        /// <b>犬丸の代理は、到着実体が現れる場所で止まる</b>（GPT 受入④）。
        ///
        /// 以前は「出発時の犬丸位置 ＋ 主人公の移動差分」へ運んでいた。
        /// 到着実体は <c>AreaInitializer.PlaceArrivals</c> が
        /// <b>入口から進行方向と逆へ 1.2m</b> に置くので、出発時の相対位置が
        /// 偶然一致していなければ<b>代理を畳んだ瞬間に犬丸が跳ぶ</b>。
        /// Down のように離れているときほど差が大きい。
        /// </summary>
        [UnityTest]
        public IEnumerator TheCompanionProxy_EndsWhereTheRealOneAppears(
            [Values(CompanionState.Follow, CompanionState.Down, CompanionState.Stagger)]
            CompanionState state)
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            yield return SetUpCompanion(state);
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);

            // <b>主人公の真後ろから外す。</b> 差分で運ぶ実装と、到着位置で運ぶ実装の差を出す。
            var companion = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(companion, "犬丸が居る。");
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            companion.transform.position = player.transform.position + new Vector3(-1.5f, 0f, 1.5f);
            Physics.SyncTransforms();
            yield return null;
            yield return SettleCamera();

            Vector3 lastProxySpot = Vector3.zero;
            bool sawProxy = false;

            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;

                AreaTransitionDisplayHost display = AreaTransitionDisplayHost.Instance;
                AreaTransitionDisplayProxy proxy = display != null && display.Set != null
                    ? display.Set.Companion : null;
                if (proxy != null && proxy.gameObject.activeInHierarchy)
                {
                    lastProxySpot = proxy.transform.position;
                    sawProxy = true;
                }
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドで着いた（失敗=" + transitions.Slide.LastFailure + "）。");
            Assert.IsTrue(sawProxy, "犬丸の表示代理が立っていた。");

            var arrived = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(arrived, "到着側に犬丸が居る。");

            // <b>代理が止まった場所と、実体が現れる場所が一致する。</b>
            float gap = Vector3.Distance(
                new Vector3(lastProxySpot.x, 0f, lastProxySpot.z),
                new Vector3(arrived.transform.position.x, 0f, arrived.transform.position.z));
            Assert.Less(gap, 0.35f,
                "代理の終点と到着実体の位置がずれている（" + gap + "m。状態=" + state
                + " 代理=" + lastProxySpot + " 実体=" + arrived.transform.position
                + " 渡した終点=" + transitions.Slide.LastCompanionRouteTo
                + "）。畳んだ瞬間に犬丸が跳ぶ（§7.2）。");

            Assert.Less(Vector3.Distance(
                    transitions.Slide.LastCompanionRouteTo, arrived.transform.position), 0.35f,
                "代理へ渡した終点そのものが、準備済みの到着位置である。");
        }

        // ---------------------------------------------------------------- GPT 再修正①（P55-07c）

        /// <summary>
        /// <b>撤去の「再試行」も Scene 操作の所有である</b>（工程 P55-07c。GPT 再修正①）。
        ///
        /// 通常の撤去（<c>_retiring</c>）だけを所有判定に入れ、再試行（<c>_retrying</c>）を
        /// 落としていた。撤去に失敗したあと再試行を始め、その Unload が走っている最中に
        /// Fade を頼むと、<b>Single 読込がそのまま発行された</b>——終端していない Unload の上に
        /// 新しい Scene 操作が重なる。
        ///
        /// <b>発行そのものを数える</b>（GPT 再修正③）。「着いていない」ではなく
        /// 「<see cref="IAreaSceneLoader.Load"/> を呼んでいない」を見る。
        /// </summary>
        [UnityTest]
        public IEnumerator RetryingTheRetireWhileAFadeWaits_IssuesNoSingleLoad()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new UnloadRefusingSceneHost();
            transitions.SlideSceneHost = host;

            var loader = new CountingSceneLoader(transitions.Loader);
            transitions.Loader = loader;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.Slide.UnloadFailureCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideCommittedCount, "前提：スライドで B に着いた。");
            Assert.IsTrue(transitions.Slide.HasPendingRetire, "前提：撤去に失敗して旧 Area を抱えている。");
            Assert.AreEqual(0, loader.LoadCount, "前提：ここまで Single 読込は発行されていない。");

            // ---- 再試行を始め、その Unload を終端させない ----
            host.RefuseUnload = false;
            host.HoldUnload = true;
            Assert.IsTrue(transitions.Slide.TryRetryRetiringDeparture(), "撤去を再試行できる。");
            yield return null;

            Assert.IsTrue(transitions.Slide.IsRetryingRetire, "前提：再試行の撤去が走っている。");
            Assert.IsFalse(transitions.Slide.IsRetiring, "前提：通常の撤去ではない（再試行だけが走っている）。");
            Assert.IsTrue(transitions.Slide.HasLiveSceneOperation,
                "再試行も「終端していない Scene 操作」として数える（GPT 再修正①）。");

            // ---- その最中に Fade を頼む ----
            Assert.IsTrue(transitions.TryTravel(AreaA, AreaAFromBEntry).Accepted,
                "前提：Fade の要求は受理される（抱えたままの Scene は Single を止めない）。");

            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.AreEqual(0, loader.LoadCount,
                    "再試行の撤去が終端するまで Single 読込を発行しない（GPT 再修正①）。"
                    + " 再試行中=" + transitions.Slide.IsRetryingRetire
                    + " 所有=" + transitions.Slide.HasLiveSceneOperation);
            }

            Assert.IsTrue(transitions.Slide.IsRetryingRetire, "まだ再試行中のまま（前提が崩れていない）。");
            Assert.AreEqual(2, host.UnloadCount, "撤去は 2 回（失敗した 1 回と再試行の 1 回）だけ頼まれている。");

            // ---- 終端すれば、一度だけ発行される ----
            host.ReleaseUnload();

            float deadline = Time.realtimeSinceStartup + 25f;
            while (loader.LoadCount == 0 && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, loader.LoadCount, "再試行の終端後に一度だけ発行される。");
            Assert.IsFalse(transitions.Slide.IsRetryingRetire, "再試行は終わっている。");
            Assert.IsFalse(transitions.Slide.HasPendingRetire, "抱えたままの旧 Area も無くなった。");
        }

        /// <summary>
        /// <b>排他は両方向で取る</b>（工程 P55-07c。GPT 再修正①）。
        ///
        /// 撤去側が「Single を待つ」だけでは足りない。Single が所有を解いているあいだに
        /// 撤去を<b>始めて</b>しまえば、同じ重なりが逆順で起きる。
        /// 表示側の「撤去をやり直す」は、Single 遷移が始まっていたら<b>断る</b>。
        /// </summary>
        [UnityTest]
        public IEnumerator RetryingTheRetireAfterTheSingleLoadHasBegun_IsRefused()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new UnloadRefusingSceneHost();
            transitions.SlideSceneHost = host;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.Slide.UnloadFailureCount > 0, 25f);

            Assert.IsTrue(transitions.Slide.HasPendingRetire, "前提：抱えたままの旧 Area がある。");

            // <b>終わらない Single 読込を差し込む</b>。完走させると「走っている最中」が
            // 一瞬で過ぎてしまい、見たい窓が作れない。
            transitions.Loader = new NeverFinishingLoader();
            Assert.IsTrue(transitions.TryTravel(AreaA, AreaAFromBEntry).Accepted,
                "前提：Fade の要求は受理される。");

            // 発行まで進める（先読みは持っていないので 1 フレームで足りる）。
            yield return null;
            Assert.IsTrue(transitions.IsSingleLoadInFlight, "前提：Single 読込が走っている。");

            host.RefuseUnload = false;
            Assert.IsFalse(transitions.Slide.TryRetryRetiringDeparture(),
                "Single 遷移が始まっていたら撤去の再試行を始めない（GPT 再修正①）。");
            Assert.AreEqual(1, transitions.Slide.RetryBlockedCount, "断った回数が数えられている。");
            Assert.AreEqual(0, transitions.Slide.RetryStartedCount, "再試行は一度も始まっていない。");
            Assert.AreEqual(1, host.UnloadCount, "撤去は失敗した 1 回のきり（割り込んでいない）。");
        }

        // ---------------------------------------------------------------- GPT 再修正②（P55-07c）

        /// <summary>
        /// <b>出発側の壁も表示経路の検査に映る</b>（工程 P55-07c。GPT 再修正②）。
        ///
        /// 表示経路の検査（§7.2 末尾）は、出発側を<b>閉じたあと</b>に走る。活動ゲートは
        /// 地形の Collider を無効にするので、<c>Physics.SphereCast</c> から見ると
        /// <b>出発側の壁は 1 枚も存在しない</b>——犬丸が出発側の壁の向こうに居ても
        /// 「経路は安全」と判定され、代理が壁を突き抜ける絵を許してしまう。
        ///
        /// 注入は<b>実物の壁</b>で行う。出発 Area の外周壁（活動ゲートが本当に閉じる Collider）を
        /// 境界の向こうへ動かし、表示経路を塞ぐ。偽の Collider を足すと
        /// 「ゲートが閉じている」という肝心の条件が再現できない。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheDepartureSideHasAWallOnTheRoute_ThePreparationFails()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle departure), "出発 Area の束がある。");
            Assert.IsNotNull(departure.ActivityGate, "出発 Area に活動ゲートがある。");

            AreaExitGate gate = FindExitGate(ExitAEast);
            Collider wall = WidestGatedWall(departure.ActivityGate);
            Assert.IsNotNull(wall, "活動ゲートが閉じる外周壁がある。");

            // 境界の<b>向こう側</b>へ動かす。主人公が出入口へ歩く経路には掛からない。
            wall.transform.position = new Vector3(
                gate.transform.position.x + 1.3f, wall.transform.position.y, gate.transform.position.z);
            Physics.SyncTransforms();
            yield return null;

            yield return StandJustBefore(gate, Vector3.left);
            yield return SettleCamera();

            yield return HoldUntil(Key.D,
                () => transitions.Slide.RolledBackCount > 0 || transitions.SlideCommittedCount > 0, 25f);

            Assert.AreEqual(0, transitions.SlideCommittedCount,
                "塞がれた表示経路でスライドを成立させない（GPT 再修正②）。"
                + " 失敗=" + transitions.Slide.LastFailure);
            Assert.AreEqual(1, transitions.Slide.RolledBackCount, "準備失敗として出発側へ戻る。");
            StringAssert.Contains("表示経路", transitions.Slide.LastFailure,
                "戻った理由は表示経路である。");

            // <b>検査のために Gameplay を再開していない。</b>
            Assert.IsTrue(departure.ActivityGate.ObstacleProbeCount > 0, "検査のために当たりを戻した。");
            Assert.IsFalse(departure.ActivityGate.IsProbingObstacles, "検査のあとは戻し切っている。");

            // 戻ったので出発側は遊べる状態に復帰している。
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居たまま。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "A で遊べる。");

            // 主人公を動かさずに数える（取り下げた先読みを選び直さない。工程 P55-08c）。
            yield return SettleWorld(transitions);
            Assert.AreEqual(1, SceneManager.sceneCount,
                "到着側は撤去されている。" + DumpWorld(transitions));
        }

        /// <summary>
        /// <b>到着側の壁も表示経路の検査に映る</b>（工程 P55-07c。GPT 再修正②）。
        ///
        /// 到着側は検査の時点で既に開いている（活動ゲートは <c>Open</c> 済み）ので、
        /// その地形は最初から当たりを持つ。出発側の当たりを戻す仕掛けが、
        /// <b>こちらを壊していない</b>ことを同じ形で見る。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheDestinationSideHasAWallOnTheRoute_ThePreparationFails()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            AreaExitGate gate = FindExitGate(ExitAEast);

            // 境界の向こう（到着 Area の敷地）に、活動ゲートに属さない壁を置く。
            // 到着側の地形と同じく<b>検査の時点で当たりを持っている</b>側の条件になる。
            var blocker = new GameObject("P55TestWall_Destination");
            blocker.layer = LayerMask.NameToLayer("Default");
            BoxCollider box = blocker.AddComponent<BoxCollider>();
            box.size = new Vector3(0.6f, 2f, 18f);
            blocker.transform.position = new Vector3(
                gate.transform.position.x + 1.3f, 1f, gate.transform.position.z);
            Physics.SyncTransforms();
            yield return null;

            try
            {
                yield return StandJustBefore(gate, Vector3.left);
                yield return SettleCamera();

                yield return HoldUntil(Key.D,
                    () => transitions.Slide.RolledBackCount > 0 || transitions.SlideCommittedCount > 0, 25f);

                Assert.AreEqual(0, transitions.SlideCommittedCount,
                    "塞がれた表示経路でスライドを成立させない。失敗=" + transitions.Slide.LastFailure);
                Assert.AreEqual(1, transitions.Slide.RolledBackCount, "準備失敗として出発側へ戻る。");
                StringAssert.Contains("表示経路", transitions.Slide.LastFailure,
                    "戻った理由は表示経路である。");
                Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居たまま。");
                Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "A で遊べる。");
            }
            finally
            {
                if (blocker != null)
                {
                    Object.DestroyImmediate(blocker);
                }
            }
        }

        /// <summary>
        /// <b>犬丸が出発側の壁の向こうに居るときは、運ばずに到着地点で現す</b>
        /// （工程 P55-07c。GPT 再修正②）。
        ///
        /// §7.2 は犬丸について「障害物を横切らない表示経路を<b>選び</b>」と言い、
        /// 「準備失敗とする」と言っているのは<b>接続</b>（主人公の経路）である。
        /// 犬丸の位置は遊びの結果なので、ここで遷移ごと断ると
        /// <b>犬丸を置き去りにしただけで出入口が使えなくなる</b>。
        ///
        /// <b>この判定は出発側の当たりが見えて初めて成立する。</b> 以前は活動停止で
        /// 出発側の Collider が消えていたため、この配置でも「経路は安全」と判定し、
        /// 代理が仕切りを突き抜けていた。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheCompanionIsBehindADepartureWall_ItIsNotCarriedButTheSlideSucceeds()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // 仕切りの向こう（A の西側）へ置く。主人公は東の出入口に居る。
            var companion = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(companion, "犬丸が居る。");
            companion.transform.position = new Vector3(-8f, companion.transform.position.y, -7f);
            Physics.SyncTransforms();
            yield return null;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            bool sawCompanionProxy = false;
            int drawnCompanions = 0;
            int slidingFrames = 0;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;

                if (transitions.Slide.Coordinator.Phase != AreaSlideTransactionPhase.Sliding)
                {
                    continue;
                }

                slidingFrames++;
                AreaTransitionDisplayHost display = AreaTransitionDisplayHost.Instance;
                if (display != null && display.Set != null && display.Set.Companion != null)
                {
                    sawCompanionProxy = true;
                }

                // <b>二重表示を防ぐ</b>（§7.2 の例外）。運ばないと決めても、
                // 出発側の実 Renderer は預かったままで、到着側も隠れたままである。
                drawnCompanions = Mathf.Max(drawnCompanions, DrawnCompanionCount());
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "犬丸を置き去りにしただけで出入口は塞がらない。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(0, transitions.SlideRolledBackCount, "準備失敗にはしない。");
            Assert.AreEqual(1, transitions.Slide.CompanionRouteDroppedCount,
                "犬丸の表示経路が塞がっていることを見抜いた（出発側の当たりが見えている）。"
                + " 理由=" + transitions.Slide.LastCompanionRouteBlocked);
            Assert.IsFalse(sawCompanionProxy, "塞がっている経路へ代理を運ばない（壁を突き抜けない）。");
            Assert.Greater(slidingFrames, 0, "前提：スライド区間を観測できた。");
            Assert.AreEqual(0, drawnCompanions,
                "省略した区間は意図的に非表示。<b>二重表示にはしない</b>（§7.2 の例外）。");

            var arrived = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(arrived, "犬丸は到着地点で現れる。");
            Assert.IsTrue(HasVisibleSprite(arrived.transform, out _), "絵も戻っている。");
        }

        /// <summary>
        /// <b>検査のあいだ戻すのは「止める直前の当たり」だけ</b>（工程 P55-07c。GPT 再修正②）。
        ///
        /// 一律に有効化すると、<b>開通済みの門が壁として映る</b>——門は開通したときに
        /// 自分の Collider を無効にしているので、検査だけが「通れない」と言い出す。
        /// <see cref="AreaActivityGate.Open"/> が同じ理由で「覚えた状態を戻す」形になっている。
        ///
        /// <b>Gameplay は再開しない。</b> 根も仕掛けの部品も止めたままであることを併せて見る。
        /// </summary>
        [UnityTest]
        public IEnumerator TheObstacleProbe_RestoresOnlyTheRememberedColliders()
        {
            yield return EnterArea(P55AreaAScene);

            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle area), "A の束がある。");
            AreaActivityGate activityGate = area.ActivityGate;
            Assert.IsNotNull(activityGate, "活動ゲートがある。");
            Assert.IsTrue(activityGate.IsOpen, "前提：遊べる状態で開いている。");

            // 「開通した門」を模す：活動中に自分で当たりを外した Collider。
            Collider opened = WidestGatedWall(activityGate);
            Assert.IsNotNull(opened, "ゲートが閉じる Collider がある。");
            opened.enabled = false;

            Assert.Greater(activityGate.GatedRoots.Count, 0, "前提：止める根がある。");
            activityGate.Close();

            Assert.IsFalse(activityGate.IsOpen, "閉じた。");
            Assert.IsFalse(activityGate.GatedRoots[0].activeSelf, "前提：Gameplay は止まっている。");

            activityGate.BeginObstacleProbe();
            try
            {
                Assert.IsTrue(activityGate.IsProbingObstacles, "検査のあいだは戻している。");
                Assert.IsFalse(opened.enabled,
                    "活動中に自分で外していた当たりは戻さない（開通済みの門を壁にしない）。");
                Assert.IsFalse(activityGate.GatedRoots[0].activeSelf,
                    "検査のために Gameplay を再開しない（根は止めたまま）。");
                Assert.IsFalse(activityGate.IsOpen, "検査中も「閉じている」ままである。");
            }
            finally
            {
                activityGate.EndObstacleProbe();
            }

            Assert.IsFalse(activityGate.IsProbingObstacles, "検査が終われば戻し切っている。");
            Assert.IsFalse(opened.enabled, "検査の前後で当たりの状態が変わらない。");
        }

        /// <summary>世界の様子を 1 行にする（失敗の理由を読めるように）。</summary>
        private static string DumpWorld(AreaTransitionService transitions)
        {
            var sb = new System.Text.StringBuilder("Scene[");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                sb.Append(SceneManager.GetSceneAt(i).name).Append(' ');
            }

            AreaPreloader preloader = transitions.Slide.Preloader;
            sb.Append("] 束[");
            foreach (AreaRuntimeBundle b in AreaBundleDirectory.All)
            {
                sb.Append(b == null ? "null" : b.AreaId.Value + "→" + b.Instance).Append(' ');
            }

            return sb.Append("] 台帳=").Append(DumpResidency(transitions))
                .Append(" 先読み=").Append(preloader.Phase)
                .Append(" 望み=").Append(preloader.DesiredArea)
                .Append(" Staged=").Append(preloader.StagedArea)
                .Append(" 保持=").Append(transitions.Slide.ProximityPreload.HeldAreaId)
                .Append(" 忘れ=").Append(transitions.Slide.ForgottenResidentCount)
                .Append(" 戻した回数=").Append(transitions.SlideRolledBackCount)
                .Append(" 完了=").Append(transitions.CompletedCount)
                .Append(" 理由=").Append(transitions.Slide.LastFailure)
                .ToString();
        }

        /// <summary>在留台帳の中身を 1 行にする（失敗の理由を読めるように）。</summary>
        private static string DumpResidency(AreaTransitionService transitions)
        {
            var sb = new System.Text.StringBuilder("[");
            foreach (KeyValuePair<AreaInstanceHandle, AreaActivationPhase> kv
                     in transitions.Slide.Residency.Residents)
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            }

            return sb.Append(']').ToString();
        }

        /// <summary>いま描かれている犬丸の数（実体・代理を問わない）。</summary>
        private static int DrawnCompanionCount()
        {
            int drawn = 0;
            foreach (CompanionActor actor
                     in Object.FindObjectsByType<CompanionActor>(FindObjectsSortMode.None))
            {
                if (actor != null && HasVisibleSprite(actor.transform, out _))
                {
                    drawn++;
                }
            }

            AreaTransitionDisplayHost display = AreaTransitionDisplayHost.Instance;
            AreaTransitionDisplayProxy proxy = display != null && display.Set != null
                ? display.Set.Companion : null;
            if (proxy != null && HasVisibleSprite(proxy.transform, out _))
            {
                drawn++;
            }

            return drawn;
        }

        // ---------------------------------------------------------------- 距離による先読み（§5。P55-08b）

        /// <summary>
        /// <b>出入口へ近づいただけで隣が載る</b>（§5 の 1〜2 行目。工程 P55-08b）。
        ///
        /// これまで先読みは<b>受理してからしか始まらなかった</b>ので、境界に着いてから
        /// 隣を読み始めていた。その間、境界の向こうには何も無い——§7.3 の「接続部の穴」が
        /// 境界手前で見えるのはこれが原因である。
        ///
        /// 見るのは 3 つ。<b>範囲の外では読まない</b>、<b>範囲に入ると要求より先に載る</b>、
        /// <b>そのまま歩いた遷移は読み直さずに引き取る</b>。
        /// 3 つ目が抜けると、先読みは<b>ただの二重ロード</b>になる。
        /// </summary>
        [UnityTest]
        public IEnumerator ApproachingTheSeam_StagesTheNeighbourBeforeAnyRequest()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // <b>実行役は本番の初期化経路が作る</b>（工程 P55-08c。GPT 再修正①）。
            //
            // <c>Update</c> は <c>_slideRunner?.Pump()</c> なので、作られるまで何も回らない。
            // ここを触って作ってしまうと<b>テスト自身が初期化を助けて</b>欠陥を隠す——
            // だから作られたことは、作らない窓口で確かめる。
            Assert.IsTrue(transitions.HasSlideRunner,
                "接続が配られた時点で実行役が居る（要求より先に先読みを回すため）。");

            var host = new CountingSceneHost();
            transitions.SlideSceneHost = host;

            AreaExitGate gate = FindExitGate(ExitAEast);

            // <b>ここから先、`Slide` を触らない</b>（工程 P55-08c。GPT 再修正①）。
            //
            // 実行役は初回参照で作られる。テストが先に触ると<b>テスト自身が初期化を助けて</b>、
            // 「本番では実行役が居ないので先読みが動かない」という欠陥を隠す。
            // 載ったかどうかは Scene と Scene 操作の口（差し替えた host）だけで言う。

            // ---- 範囲の外では読まない ----
            yield return PlacePlayerAt(gate.transform.position + (Vector3.left * 9f));
            yield return WaitFrames(30);

            Assert.AreEqual(1, SceneManager.sceneCount, "9 units 離れていれば隣は載らない。");
            Assert.AreEqual(0, host.Loaded.Count, "読込も発行していない。");

            // ---- 近づくと、要求より先に載る ----
            yield return PlacePlayerAt(gate.transform.position + (Vector3.left * 4f));

            float loadDeadline = Time.realtimeSinceStartup + 20f;
            while (SceneManager.sceneCount < 2 && Time.realtimeSinceStartup < loadDeadline)
            {
                yield return null;
            }

            var probe = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.AreEqual(1, host.CountOf(P55AreaBScene),
                "出入口へ近づいただけで B の Additive 読込が始まる（§5）。"
                + " 主人公=" + probe.transform.position
                + " 出入口=" + gate.transform.position
                + " 距離=" + Vector3.Distance(
                    new Vector3(probe.transform.position.x, 0f, probe.transform.position.z),
                    new Vector3(gate.transform.position.x, 0f, gate.transform.position.z))
                + " 接続=" + (transitions.Connections != null)
                + " 読んだ順=" + string.Join(",", host.Loaded)
                + " 頼んだ回数=" + transitions.Slide.ProximityPreloadRequestCount
                + " 抑止=" + transitions.Slide.ProximityPreload.SuppressedConnectionId
                + " " + DumpWorld(transitions));
            Assert.AreEqual(2, SceneManager.sceneCount, "実 Scene も 2 枚。");

            // ---- ここから状態を見る（実行役はもう本番経路が作っている）----
            //
            // 読込の終端は次のフレーム以降なので、落ち着くまで進めてから数える。
            yield return SettleWorld(transitions);

            Assert.AreEqual(0, transitions.Slide.Coordinator.AcceptedCount,
                "遷移はまだ受理していない（要求より先に読んでいる）。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "着いてもいない。");
            Assert.GreaterOrEqual(transitions.Slide.ProximityPreloadRequestCount, 1,
                "距離で先読みを頼んだ。");
            Assert.AreEqual(AreaPreloadPhase.Staged, transitions.Slide.Preloader.Phase,
                "隣が閉じたまま載っている。" + DumpWorld(transitions));

            // <b>台帳が実 Scene に合っている</b>（工程 P55-08c。GPT 再修正③）。
            //
            // 枚数だけでは足りない。載っているのに台帳に居ないと、在留枠が空いていると誤認して
            // 上限 2 を超えて読める。<b>実体ハンドルと Scene の対応</b>まで見る。
            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle aBundle), "A の束がある。");
            Assert.IsTrue(TryFindBundle(AreaB, out AreaRuntimeBundle bBundle), "B の束もある。");
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                "在留は 2（A と B）。" + DumpWorld(transitions));
            Assert.AreEqual(AreaActivationPhase.Active,
                transitions.Slide.Residency.PhaseOf(aBundle.Instance), "A は Active。");
            Assert.AreEqual(AreaActivationPhase.Staged,
                transitions.Slide.Residency.PhaseOf(bBundle.Instance), "B は Staged。");
            Assert.AreEqual(bBundle.Instance, transitions.Slide.Preloader.StagedArea,
                "先読みが預かっているのは B の実体。");
            Assert.AreEqual(bBundle.SceneHandle, transitions.Slide.Preloader.StagedSceneHandle,
                "預かりの Scene handle も B と一致する。");
            Assert.IsTrue(transitions.SlideSceneHost.IsLoaded(aBundle.SceneHandle),
                "A の Scene は載っている。");
            Assert.IsTrue(transitions.SlideSceneHost.IsLoaded(bBundle.SceneHandle),
                "B の Scene も載っている。");
            Assert.AreNotEqual(aBundle.SceneHandle, bBundle.SceneHandle, "別の Scene である。");

            // <b>止めない</b>（§5 の 1 行目「先読み自体は操作・時計・探索を止めない」）。
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "Gameplay 時計は止まっていない。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "遊べるまま。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "居るのは A のまま。");

            // ---- そのまま歩けば、読み直さずに着く ----
            yield return StandJustBefore(gate, Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドで着いた。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(1, host.CountOf(P55AreaBScene),
                "遷移は距離で読んだものを引き取った（B を読み直していない。§5）。"
                + " 読んだ順=" + string.Join(",", host.Loaded));
            Assert.AreEqual(1, transitions.Slide.Preloader.HandedOffCount, "引き取りは 1 回。");
        }

        /// <summary>
        /// <b>再試行も失敗したあと、その場に立っていても先読みが復活しない</b>
        /// （工程 P55-08d。GPT 再修正の残件）。
        ///
        /// 抜けていた経路はこうだった。
        /// <list type="number">
        /// <item><description>1 回目の失敗で、取り下げた接続を抑止する。</description></item>
        /// <item><description>プレイヤーが<b>自分で</b>もう一度操作すると、抑止が解ける（§5）。</description></item>
        /// <item><description>遷移が走っている間は距離による選定が回らないので、<b>保持は空のまま</b>。</description></item>
        /// <item><description>再試行も失敗する。</description></item>
        /// <item><description>保持が空なので<b>誰も抑止されない</b>。</description></item>
        /// <item><description>次のフレームに同じ接続が自動で選ばれ、
        /// <b>遅れて着いた Scene が「また必要な先読み」になって撤去されない</b>。</description></item>
        /// </list>
        ///
        /// だから抑止する相手は<b>失敗した遷移が名指しする</b>（保持から推し測らない）。
        ///
        /// <b>主人公は動かさない。</b> 出入口の手前に立ったままで後始末が成立することが要件である。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheRetryFailsToo_TheNeighbourIsNotRequestedAgainWhileStandingStill()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new ControllableSceneHost();
            transitions.SlideSceneHost = host;
            transitions.TimeoutSeconds = 1f;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            // ---- 1 回目：時間切れで戻る ----
            yield return HoldUntil(Key.D, () => transitions.Slide.TimedOutCount > 0, 25f);

            Assert.AreEqual(1, transitions.Slide.TimedOutCount, "監視上限を超えた。");
            Assert.AreEqual(1, transitions.SlideRolledBackCount, "出発側へ戻した。");
            Assert.AreEqual(1, host.LoadCount, "読込は 1 回だけ発行された。");
            Assert.IsTrue(transitions.Slide.ProximityPreload.SuppressedConnectionId.IsValid,
                "失敗した接続を抑止した。" + DumpWorld(transitions));

            // ---- 2 回目（手動の再試行）：これも時間切れで戻る ----
            //
            // <b>ここで抑止が解ける</b>（§5 の「次の新しい遷移操作で一度だけ」）。
            // 解けたあと遷移が走っている間は選定が回らないので、保持は空のままである。
            yield return HoldUntil(Key.D, () => transitions.Slide.TimedOutCount > 1, 25f);

            Assert.AreEqual(2, transitions.Slide.TimedOutCount, "再試行も時間切れになった。");
            Assert.AreEqual(2, transitions.SlideRolledBackCount, "もう一度戻した。");
            Assert.AreEqual(1, host.LoadCount,
                "終端していない操作の上に読込を重ねていない。" + DumpWorld(transitions));
            Assert.IsTrue(transitions.Slide.ProximityPreload.SuppressedConnectionId.IsValid,
                "<b>再失敗でも抑止できている</b>（保持が空でも名指しで抑止する）。"
                + DumpWorld(transitions));

            // ---- その場で待つ：自動では読み直さない ----
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            for (int i = 0; i < 40; i++)
            {
                yield return null;
                Assert.AreEqual(1, host.LoadCount,
                    "立っているだけでは追加のロードを発行しない（" + i + " フレーム目）。"
                    + DumpWorld(transitions));
                Assert.IsFalse(transitions.Slide.Preloader.DesiredArea.IsValid,
                    "望みも立て直さない（" + i + " フレーム目）。" + DumpWorld(transitions));
            }

            // ---- 遅れて終端したら撤去される（§8 の 5 行目）----
            host.CompleteLoad(987654);

            float deadline = Time.realtimeSinceStartup + 15f;
            while (host.UnloadCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, host.UnloadCount,
                "誰も望まなくなった遅延到着は撤去される。" + DumpWorld(transitions));
            Assert.AreEqual(987654, host.LastUnloadHandle, "撤去したのは遅れて着いた Scene。");
            Assert.AreEqual(1, host.LoadCount, "撤去のあとも読み直さない。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "成功扱いにしない。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居たまま。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "A で遊べる。");
        }

        /// <summary>読み込んだ Scene を記録するだけの host（本物へ転送する）。</summary>
        private sealed class CountingSceneHost : IAreaSceneHost
        {
            private readonly UnityAreaSceneHost _real = new UnityAreaSceneHost();

            /// <summary>読み込みを頼まれた Scene（順番つき）。</summary>
            internal readonly List<string> Loaded = new List<string>();

            /// <summary>その Scene を何回読んだか。</summary>
            internal int CountOf(string scenePath)
            {
                int n = 0;
                for (int i = 0; i < Loaded.Count; i++)
                {
                    if (Loaded[i] == scenePath)
                    {
                        n++;
                    }
                }

                return n;
            }

            public IAreaSceneOperation LoadAdditive(string scenePath)
            {
                Loaded.Add(scenePath);
                return _real.LoadAdditive(scenePath);
            }

            public IAreaSceneOperation Unload(int sceneHandle) => _real.Unload(sceneHandle);

            public bool IsLoaded(int sceneHandle) => _real.IsLoaded(sceneHandle);
        }

        /// <summary>そのフレーム数だけ待つ（常駐の Update を回す）。</summary>
        private static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
        }

        /// <summary>主人公をその場所へ置く（XZ だけ動かす）。</summary>
        private static IEnumerator PlacePlayerAt(Vector3 spot)
        {
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
        }

        /// <summary>活動ゲートが閉じる Collider のうち、接続軸を最も広く横切る壁を選ぶ。</summary>
        private static Collider WidestGatedWall(AreaActivityGate activityGate)
        {
            Collider widest = null;
            foreach (Collider candidate in activityGate.GatedColliders)
            {
                if (candidate == null || !candidate.name.StartsWith("Wall"))
                {
                    continue;
                }

                if (widest == null || candidate.bounds.size.z > widest.bounds.size.z)
                {
                    widest = candidate;
                }
            }

            return widest;
        }

    }
}
