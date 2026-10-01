using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Encounter;
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
            float movedEast = cameraAfter.x - cameraBefore.x;
            Assert.GreaterOrEqual(movedEast, AreaConnectionRules.MinAlongMovement,
                "カメラが東へ動いた（右スライド。" + movedEast + " m／§7.1 の最低 "
                + AreaConnectionRules.MinAlongMovement + "）。**5 m は継ぎ目領域が居た時代の値**——連続追従では出発位置が境界のすぐ手前になる（工程 P55-14d）。");
            Assert.AreEqual(transitions.Slide.LastSlideTo.x, cameraAfter.x, 0.01f, "終点へ厳密に着いた。");

            // <b>事前に境界位置へ瞬間移動していない</b>（§7.1）。
            // 受理から準備完了までの間に実カメラが動いていないこと、そしてスライドの
            // 最初のフレームがその位置から始まっていること——この 2 つで「先に飛んでいない」が言える。
            Assert.AreEqual(samples.FirstPreSlideX, samples.LastPreSlideX, 0.2f,
                "受理から準備完了までの間、実カメラは動いていない。");
            Assert.AreEqual(samples.LastPreSlideX, samples.CameraSamples[0].x, 0.2f,
                "スライドは準備完了時点の実カメラ位置から始まる（§7.1）。");
            // <b>5 m という差は継ぎ目領域が居た時代の値である</b>（工程 P55-14d）。
            // 連続追従では出発位置が主人公の位置（＝境界のすぐ手前）になるので、
            // 始点と終点の差はもっと小さい。**言いたいのは「始点が終点側に居ない」こと**なので、
            // §7.1 が定める最低移動距離で言う。
            Assert.Less(samples.LastPreSlideX,
                transitions.Slide.LastSlideTo.x - AreaConnectionRules.MinAlongMovement,
                "始点は到着側ではなく出発側にある（始点 x=" + samples.LastPreSlideX
                + " 終点 x=" + transitions.Slide.LastSlideTo.x + "）。");

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

            // <b>旧 Area は撤去せず、非活動のまま先読み枠へ預けた</b>（§6.2 手順 11。裁定 2。工程 P55-10c）。
            //
            // <b>ここは反転した検査である。</b> 以前は「出発時の Scene は撤去されている」を見ていた——
            // 撤去してすぐ同じ Area を読み直していたので、同じ Area でも Scene は別物だった
            // （到着直後に虚空が出ていたのはこのため。付録 C.17.4）。
            // いまは<b>同じ Scene をそのまま預かる</b>ので、handle の一致が保持の証拠になる。
            Assert.AreEqual(0, transitions.Slide.UnloadFailureCount, "撤去は失敗していない。");
            Assert.AreEqual(1, transitions.Slide.RetainedCount,
                "旧 Area を預けた。断った理由=" + transitions.Slide.LastRetainDecline);
            Assert.AreEqual(0, transitions.Slide.RetainDeclinedCount, "預かりは断られていない。");
            Assert.IsTrue(transitions.SlideSceneHost.IsLoaded(departureSceneHandle),
                "出発時の Scene は載ったまま（毎回の unload をしない）。");

            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaPreloadPhase.Staged, preloader.Phase,
                "落ち着いた先は「隣を持っている」（保持したものがそのまま在庫になる）。");
            Assert.AreEqual(AreaA.Value, preloader.StagedArea.AreaId.Value,
                "持っているのは来た方の A。");
            Assert.AreEqual(departureSceneHandle, preloader.StagedSceneHandle,
                "預かっているのは<b>同じ Scene</b> である（読み直していない）。");

            // <b>預けた Area では何も進まない</b>（§6.2 手順 11 の契約表「非活動Area」）。
            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle retained), "旧 Area の束はまだある。");
            Assert.IsFalse(retained.ActivityGate.IsOpen, "活動ゲートは閉じたまま。");
            Assert.IsFalse(retained.Context.IsAreaReady, "旧 Area では遊べない。");
            Assert.AreEqual(AreaActivationPhase.Staged,
                transitions.Slide.Residency.PhaseOf(retained.Instance),
                "台帳の上でも非活動（Active は B だけ）。");
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
            float movedWest = cameraBefore.x - RigPosition().x;
            Assert.GreaterOrEqual(movedWest, AreaConnectionRules.MinAlongMovement,
                "カメラが西へ動いた（左スライド。" + movedWest + " m／§7.1 の最低 "
                + AreaConnectionRules.MinAlongMovement + "）。**5 m は継ぎ目領域が居た時代の値**——連続追従では出発位置が境界のすぐ手前になる（工程 P55-14d）。");
            Assert.AreEqual(transitions.Slide.LastSlideTo.x, RigPosition().x, 0.01f,
                "終点へ厳密に着いた。");
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
            // <b>3 m という差も継ぎ目領域が居た時代の値だった</b>（工程 P55-14d）。
            // スライドの区間そのものが短くなったので、**区間に対する割合**で言う——
            // 配置を変えても「途中で引き返した」の意味が変わらない。
            float slideSpan = transitions.Slide.LastSlideTo.x - transitions.Slide.LastSlideFrom.x;
            Assert.Greater(slideSpan, 0f, "前提：東へ進む区間である。");
            Assert.Less(peakX, transitions.Slide.LastSlideFrom.x + (slideSpan * 0.5f),
                "壊れたと気付いた場所から引き返している（区間の前半で折り返した）。到達点=" + peakX
                + " 始点=" + transitions.Slide.LastSlideFrom.x
                + " 終点=" + transitions.Slide.LastSlideTo.x + " 区間=" + slideSpan);
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
            //
            // <b>戻り先は「スライドの始点」であって「歩き出す前の位置」ではない</b>（工程 P55-14d）。
            // 連続追従になったので、出入口の中で押している間にカメラは主人公について進む——
            // 置いた直後の位置とスライドの始点は<b>別の場所</b>になった。
            // §8 の 3 行目が言うのは「同じ経路を逆向きに戻す」なので、戻り先は経路の始点である。
            Assert.AreEqual(transitions.Slide.LastSlideFrom.x, RigPosition().x, 0.2f,
                "カメラがスライドの始点へ戻っている（始点 x=" + transitions.Slide.LastSlideFrom.x
                + " いま x=" + RigPosition().x + " 置いた直後 x=" + cameraBefore.x + "）。");

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
            // <b>この検査は撤去経路そのものを見る</b>（工程 P55-10c）。
            // 裁定 2 で旧 Area は既定で保持されるようになったので、保持を切って
            // 従来どおり毎回撤去させる——保持に隠れた経路は壊れても誰も気付かない。
            transitions.RetainDepartedArea = false;
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

            // 待ち表示は常駐が持っている（§5。工程 P55-09a）。
            AreaTransitionWaitNoticeHost notice = AreaTransitionWaitNoticeHost.Instance;
            Assert.IsNotNull(notice, "待ち表示の常駐が居る。");
            Assert.AreSame(notice, AreaTransitionWaitNoticeProvider.Current,
                "差さっているのは生きている常駐。");
            Assert.IsFalse(notice.IsShowing, "前提：まだ出ていない。");

            // 受理されるまで押す（先読みは頼んでいないので、受理後に読み始める）。
            yield return HoldUntil(Key.D, () => transitions.ConnectionTravelCount > 0, 25f);
            Assert.AreEqual(1, transitions.ConnectionTravelCount, "受理された。");

            // ---- 準備できるまでの待ち：A が見えている・カメラは動かない・代理はまだ立たない ----
            int frames = 0;
            bool sawNotice = false;
            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                frames++;

                // <b>0.3 秒の壁</b>（§5。工程 P55-09a）。
                //
                // <b>フレームごとの値では言えない。</b> Scene ロード中はフレームが重いので、
                // 「読んだ時点で 0.3 秒を超えている」は閾値を無視した実装でも成り立つ
                // （実際そうなった——注入 80 が素通りした）。
                // <b>決めた時点の秒数</b>を実装に記録させて、そこを見る。
                if (notice.IsShowing)
                {
                    sawNotice = true;
                    Assert.AreEqual(AreaTransitionWaitNoticeHost.NoticeText, notice.ShownLine,
                        "控えめな 1 行だけを出す。");
                    Assert.IsFalse(notice.HasFullScreenBackdrop,
                        "暗幕を敷かない（§5「全画面を黒くしない」）。");
                    Assert.Less(notice.ScreenAreaFraction, 0.05f,
                        "占める面積は画面の 5% 未満（控えめ）。実測=" + notice.ScreenAreaFraction);
                }

                Assert.AreEqual(AreaSlideTransactionPhase.Preparing,
                    transitions.Slide.Coordinator.Phase, "準備の段階で待っている。");
                Assert.AreEqual(0, transitions.SlideCommittedCount, "まだ着いていない。");
                Assert.AreEqual(0, AreaCameraRigHost.Instance.SlideCount, "演出は始まっていない。");
                // <b>「動かない」から「主人公に付いているだけ」へ</b>（§7.1 改定。工程 P55-14d）。
                //
                // 以前は継ぎ目領域がカメラを 1 点へ留めていたので、待っている間の位置は
                // 1 mm も動かなかった。連続追従では<b>主人公が動けばカメラも動く</b>——
                // それは正常である。言いたいのは「<b>境界へ先に飛んでいない</b>」ことなので、
                // カメラが主人公に付いていること（＝勝手に進んでいないこと）で言う。
                var waitingPlayer = Object.FindFirstObjectByType<PlayerRoot>();
                Assert.IsNotNull(waitingPlayer, "主人公が居る。");
                Assert.AreEqual(waitingPlayer.transform.position.x, RigPosition().x, 0.2f,
                    "待っている間、カメラは主人公に付いているだけで境界へ先に飛んでいない（§7.1）。"
                    + " カメラ x=" + RigPosition().x
                    + " 主人公 x=" + waitingPlayer.transform.position.x
                    + " 待ち始め x=" + cameraBefore.x);

                // <b>全画面を黒くしない</b>（§5）。出発側の地形が見えていることで見る。
                Assert.IsTrue(DepartureTerrainIsVisible(AreaA),
                    "出発 Area の地形が見えたまま待っている（暗転していない）。");

                IAreaTransitionDisplay display = AreaTransitionDisplayProvider.Current;
                Assert.IsFalse(display.IsActive, "代理は準備できてから立てる（§6.2 手順 5）。");
                AssertPlayerVisible();
            }

            Assert.Greater(frames, 10, "待ちを複数フレーム観測できた。");
            Assert.AreEqual(1, host.LoadCount, "待っている間に読み直さない（重複ロードなし。§5）。");

            // 2 秒待ったのだから、0.3 秒の壁は越えている。
            Assert.IsTrue(sawNotice,
                "0.3 秒以上待ったので「読み込み中」が出た（待ち="
                + transitions.Slide.WaitNotice.WaitedSeconds + " 秒）。");
            Assert.IsTrue(notice.IsShowing, "待っている間は出したまま。");
            Assert.AreEqual(1, notice.ShowCount, "毎フレーム出し直していない。");
            Assert.GreaterOrEqual(transitions.Slide.WaitNotice.ShownAtSeconds, 0.29f,
                "<b>出すと決めたのは 0.3 秒に届いてから</b>である（§5）。決めた時点="
                + transitions.Slide.WaitNotice.ShownAtSeconds + " 秒");

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

            // <b>待ちが終われば消える</b>（§5。工程 P55-09a）。出したまま残すと、
            // スライドの最中も到着後も「読み込み中」が居座る。
            Assert.IsFalse(notice.IsShowing, "待ちが終わったので消えている。");
            Assert.AreEqual(1, notice.HideCount, "消したのは 1 回。");
            Assert.AreEqual(1, AreaCameraRigHost.Instance.SlideCount, "演出も 1 回だけ。");
            float slidEast = RigPosition().x - cameraBefore.x;
            Assert.GreaterOrEqual(slidEast, AreaConnectionRules.MinAlongMovement,
                "東へスライドした（" + slidEast + " m／§7.1 の最低 "
                + AreaConnectionRules.MinAlongMovement + "）。**5 m は継ぎ目領域が居た時代の値**——連続追従では出発位置が境界のすぐ手前になる（工程 P55-14d）。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
        }

        /// <summary>
        /// <b>待ちの計時は、ロードを頼むより前に始まっている</b>（§5。工程 P55-15f で契約を確定）。
        ///
        /// <b>これは「ロード開始前に字が出る」という意味ではない。</b>
        /// <c>Begin()</c> は <c>ShouldShow</c> を false にして数え始めるだけで、
        /// 字が出るのは <b>0.3 秒の壁を越えてから</b>である（§5）。
        ///
        /// <b>工程 P55-15d では、ここを取り違えて書いていた。</b>
        /// 「ロード開始前に待機表示を描画できるようにし、
        /// 『壁に引っかかったように停止してから表示が出る』順序を避ける」という前回の裁定を
        /// <b>この順番で満たした</b>と書いたが、満たしていない——
        /// この検査が見ているのは <c>BegunCount</c>（計時の開始）であって、<b>描画ではない</b>。
        /// GPT レビューの指摘で分かった（記録 060）。
        /// 仕様は「受理から 0.3 秒以上待つ場合に表示」で確定し、
        /// <b>ロード開始前の描画保証は要件から外した</b>——
        /// 0.3 秒未満で終わる正常系で一瞬だけ字が出ると、ちらつきとして見えるためである。
        ///
        /// <b>それでもこの順番は守る価値がある。</b> 計時をロードのあとで始めると、
        /// ロードに 0.25 秒かかっても壁の手前から数え直すので、
        /// <b>操作できない時間が実際より短く見積もられ、出すべき場面で出なくなる</b>
        /// （付録 C.31 の「待機表示を準備待ち全体へ」がまさにこの話である）。
        /// ここで縛るのは<b>その通算の起点</b>である。
        ///
        /// <b>フレームごとに覗く方法では言えない。</b> 2 つは同じフレームの中で続けて起きるので、
        /// 次のフレームから見ると<b>どちらも済んでいる</b>。
        /// だから<b>ロードを頼まれた瞬間に Scene 操作側で読む</b>——
        /// <c>LoadAdditive</c> は <c>Request</c> から同じフレームの中で呼ばれるので、
        /// そこで読んだ値は「ロード開始の直前」である。
        /// </summary>
        [UnityTest]
        public IEnumerator TheWaitTimer_StartsBeforeTheLoadIsRequested()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new DelayedRealSceneHost();
            transitions.SlideSceneHost = host;

            AreaTransitionWaitNoticeTimer timer = transitions.Slide.WaitNotice;
            int begunBefore = timer.BegunCount;
            host.ProbeAtLoad = () => timer.BegunCount;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            Assert.AreEqual(0, host.LoadCount,
                "前提：境界へ寄っただけでは読んでいない（工程 P55-15c）。");

            yield return HoldUntil(Key.D, () => host.LoadCount > 0, 25f);

            Assert.AreEqual(1, host.LoadCount,
                "受理されてロードが始まった（" + host.LoadCount + " 回）。");
            Assert.AreEqual(begunBefore + 1, host.ProbedAtLoad,
                "<b>ロードを頼む前に待ちの計時が始まっている</b>（頼まれた瞬間の計時開始回数="
                + host.ProbedAtLoad + "／この遷移の前=" + begunBefore
                + "）。逆順だと 0.3 秒の壁が<b>ロード待ちを含まない</b>ので、"
                + " 操作できない時間が実際より短く見積もられる（§5・付録 C.31）。");

            Assert.IsFalse(timer.ShouldShow,
                "<b>この時点ではまだ字は出ない</b>（出ていれば ShouldShow=true）。"
                + " 字が出るのは 0.3 秒の壁を越えてからで、"
                + " 「ロード開始前に描画する」ことは要件ではない（工程 P55-15f で確定）。");

            // 待たせたまま終わらせない——出した字を片付けるところまで通す。
            host.ReleaseLoad();
            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "そのまま着いた。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(1, host.LoadCount, "読込は 1 回だけ（重複なし）。");
        }

        /// <summary>
        /// <b>隣がもう載っているなら、待ち表示は一度も出ない</b>（§5。工程 P55-09a）。
        ///
        /// §5 の「0.3 秒以上待つ場合<b>だけ</b>」の後半である。距離による先読みが間に合っている
        /// 正常系では待ちは 1〜2 フレームで終わるので、そこで字が出ると<b>ちらつき</b>になる。
        ///
        /// <b>「出す」側だけを実装しても受入は通る。</b> 誰も「出ない」を見ないからである。
        /// 閾値そのものは EditMode が決定的に見るが、<b>正常系で本当に出ないか</b>は
        /// 実遷移でしか言えない——待ちの長さは実際のロードが決めるので。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheNeighbourIsAlreadyStaged_TheWaitNoticeNeverShows()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            AreaExitGate gate = FindExitGate(ExitAEast);

            // <b>「既に載っている」の作り方が変わった</b>（工程 P55-15c。読み込み方針の裁定）。
            //
            // 以前は<b>出入口へ近づいて距離による先読みに載せさせて</b>いた。
            // 距離で読むのをやめたので、載っている状態は<b>一度訪れたあと</b>にしか作れない——
            // 裁定の「ロード済みエリアへの再移動は保持した Scene を再利用する」がそれである。
            yield return StandJustBefore(gate, Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A へ戻った。");
            Assert.AreEqual(AreaPreloadPhase.Staged, transitions.Slide.Preloader.Phase,
                "前提：隣（B）が閉じたまま載っている。" + DumpWorld(transitions));

            gate = FindExitGate(ExitAEast);
            AreaTransitionWaitNoticeHost notice = AreaTransitionWaitNoticeHost.Instance;
            Assert.IsNotNull(notice, "待ち表示の常駐が居る。");

            // <b>往復の初回ロードでは出てよい。</b> 見たいのは
            // 「<b>載っている相手への遷移では出ない</b>」なので、ここから先で比べる。
            int noticeShownBefore = notice.ShowCount;
            int begunBeforeThisTravel = transitions.Slide.WaitNotice.BegunCount;

            yield return StandJustBefore(gate, Vector3.left);
            yield return SettleCamera();

            // ---- 受理から到着まで、毎フレーム「出ていないこと」を見る ----
            //
            // <b>往復ぶんを控えてから数える</b>（工程 P55-15c。前提の作り方が往復になったので、
            // 絶対値で待つと<b>ループが一度も回らない</b>）。
            int committedBeforeThisTravel = transitions.SlideCommittedCount;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == committedBeforeThisTravel
                   && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;

                Assert.IsFalse(notice.IsShowing,
                    "隣が載っているのだから待ち表示は出ない（待ち="
                    + transitions.Slide.WaitNotice.WaitedSeconds + " 秒）。");
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.AreEqual(committedBeforeThisTravel + 1, transitions.SlideCommittedCount,
                "この遷移でもう 1 回着いた。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(noticeShownBefore, notice.ShowCount,
                "<b>載っている相手への遷移では一度も出していない</b>（ちらつかせない）。"
                + " いま=" + notice.ShowCount + " 往復のあと=" + noticeShownBefore);
            Assert.Less(transitions.Slide.WaitNotice.WaitedSeconds,
                AreaTransitionWaitNoticeTimer.ThresholdSeconds,
                "そもそも待ちが 0.3 秒に届いていない。");
            Assert.AreEqual(begunBeforeThisTravel + 1, transitions.Slide.WaitNotice.BegunCount,
                "待ちの数え直しはこの遷移で 1 回（遷移ごとに 0 から数える。この遷移で "
                + (transitions.Slide.WaitNotice.BegunCount - begunBeforeThisTravel) + " 本）。");
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

            /// <summary>
            /// <b>ロードを頼まれた瞬間</b>に読む値（工程 P55-15d）。
            ///
            /// <c>LoadAdditive</c> は <c>AreaPreloader.Request</c> から<b>同じフレームの中で</b>
            /// 呼ばれるので、ここで読んだ値は「<b>ロード開始の直前</b>の状態」である。
            /// フレームごとに外から覗く方法では、同一フレーム内の前後は決められない。
            /// </summary>
            internal System.Func<int> ProbeAtLoad { get; set; }

            /// <summary>ロードを頼まれた瞬間の <see cref="ProbeAtLoad"/> の値（未測定なら −1）。</summary>
            internal int ProbedAtLoad { get; private set; } = -1;

            public IAreaSceneOperation LoadAdditive(string scenePath)
            {
                LoadCount++;
                if (ProbeAtLoad != null)
                {
                    ProbedAtLoad = ProbeAtLoad();
                }

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

        // ---------------------------------------------------------------- 旧 Area の引き継ぎ（工程 P55-10c）

        /// <summary>
        /// <b>すぐ戻ってきたら、預けた Area をそのまま使う</b>（§6.2 手順 11 の契約表「即時の逆移動」）。
        ///
        /// 裁定 2 の前は、Commit のたびに旧 Area を unload し、到着した主人公が逆向きの出入口の
        /// すぐ内側に居るので<b>その場でもう一度読み直していた</b>。撤去と読込を 1 往復ぶん
        /// 無駄に撃っていたうえ、その隙間が到着直後の虚空になっていた（付録 C.17.4）。
        ///
        /// <b>「読み直していない」は読込の回数で言う。</b> 「同じ Area に居る」では言えない——
        /// 読み直しても Area は同じである。数えるのは <c>LoadAdditive</c> の呼び出しで、
        /// 裏づけに Scene handle の一致も見る。
        /// </summary>
        [UnityTest]
        public IEnumerator ReturningImmediately_ReusesTheRetainedAreaWithoutLoading()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new CountingSceneHost();
            transitions.SlideSceneHost = host;

            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle before), "A の束を引ける。");
            int areaASceneHandle = before.SceneHandle;

            // ---- A → B ----
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(1, transitions.Slide.RetainedCount,
                "A を預けた。断った理由=" + transitions.Slide.LastRetainDecline);
            Assert.AreEqual(1, host.CountOf(P55AreaBScene), "B は 1 回だけ読んだ。");
            Assert.AreEqual(0, host.CountOf(P55AreaAScene),
                "A は読み直していない（預かったままなので読む必要が無い）。読んだ順="
                + string.Join(", ", host.Loaded));

            // ---- B → A（すぐ戻る）----
            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return SettleCamera();
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(2, transitions.SlideCommittedCount,
                "二度目も着いた。失敗=" + transitions.Slide.LastFailure);
            Assert.AreEqual(0, host.CountOf(P55AreaAScene),
                "戻るときも A を読み直していない（契約表「重複ロードしない」）。読んだ順="
                + string.Join(", ", host.Loaded));
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に戻っている。");
            Assert.AreEqual(areaASceneHandle, CurrentAreaProvider.Current.SceneHandle,
                "戻った先は<b>同じ Scene</b>である（handle が一致する）。");
            Assert.AreEqual(1, transitions.Slide.ReenteredCount,
                "保持していた Area への再入場として数えている（入場準備を呼んだ経路）。");
            Assert.AreEqual(0, transitions.Slide.RetireWaitCount,
                "撤去の終わりを待っていない——そもそも撤去していない。");
            Assert.AreEqual(0, transitions.Slide.RolledBackCount, "戻していない。");
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                "在留は 2 のまま（上限を超えていない）。" + DumpResidency(transitions));
            Assert.AreEqual(2, SceneManager.sceneCount, "実 Scene も 2 枚。");
        }

        /// <summary>
        /// <b>B で削った値は、A へ戻っても削れたまま</b>（§11 の P17。§6.2 手順 5）。
        ///
        /// <b>これが「再入場準備の分離」を入れた理由である。</b> 保持した Area へ戻ると
        /// 同じ Actor へ帰るので、入場準備が走らないと<b>初回入場のときの値</b>がそのまま残る——
        /// 読み直していた従来なら Actor が新品になって Snapshot から復元されるので、
        /// この壊れ方は起こりようがなかった。
        ///
        /// <b>1 往復では出ない壊れ方である。</b> 行きは初回入場なので必ず正しい。
        /// 見えるのは<b>2 周目</b>——だから往復してから値を見る。
        ///
        /// 見るのは HP だけにする。スタミナは時間で戻り、CD は時間で減るので、
        /// 歩いている時間の長さに検査が依存してしまう（値が持ち越されたかの話ではなくなる）。
        /// </summary>
        [UnityTest]
        public IEnumerator ChangingTheHpInB_ThenReturningToA_KeepsTheDentedValue()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // ---- A → B ----
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "前提：B に居る。");

            // ---- B 側で HP を削る ----
            ActorValues inB = ReadActorValues();
            Assert.IsTrue(inB.Found, "B 側の Actor 部品がそろっている。");
            Assert.Greater(inB.MaxHp, 3, "HP を減らせる構成である。");

            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            int dentedHp = inB.MaxHp - 3;
            Assert.IsTrue(vitals.TryImportTransferSnapshot(new PlayerVitalsTransferSnapshot(
                    new VitalTransferSnapshot(dentedHp),
                    new StaminaTransferSnapshot(inB.Stamina, inB.StaminaRegenDelay, 0f))),
                "B で HP を削れた。");
            yield return null;
            Assert.AreEqual(dentedHp, ReadActorValues().Hp, "前提：B 側の HP が削れている。");

            // ---- B → A ----
            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return SettleCamera();
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value,
                "A に戻っている。失敗=" + transitions.Slide.LastFailure);

            ActorValues backInA = ReadActorValues();
            Assert.IsTrue(backInA.Found, "A 側の Actor 部品がそろっている。");
            Assert.AreEqual(dentedHp, backInA.Hp,
                "B で削った HP がそのまま（初回入場の値へ戻っていない。§11 の P17）。"
                + " 最大=" + backInA.MaxHp + " 再入場=" + transitions.Slide.ReenteredCount);
        }

        /// <summary>
        /// <b>Single 読込の前には、預けた Area も片付ける</b>（§6.2 手順 11 の契約表「Single 遷移」）。
        ///
        /// 保持を「先読みの預かり」として実装したので、既存の後始末
        /// （<c>DiscardStagedForSingleLoad</c>）が<b>そのまま効く</b>。
        /// 別に保持リストを作っていたら、この経路をもう一度書く必要があった。
        /// </summary>
        [UnityTest]
        public IEnumerator BeforeASingleLoad_TheRetainedAreaIsReleasedToo()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var loader = new CountingSceneLoader(transitions.Loader);
            transitions.Loader = loader;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(1, transitions.Slide.RetainedCount, "前提：A を預けている。");
            Assert.AreEqual(2, SceneManager.sceneCount, "前提：2 枚載っている。");
            Assert.AreEqual(0, loader.LoadCount, "前提：ここまで Single 読込は発行されていない。");

            // <b>預かっている実 Scene を控える。</b> 枚数では言えない——Single 読込で着いた先でも
            // §5 の距離による先読みがすぐ隣を持つので、落ち着いた先はまた 2 枚になる。
            int retainedSceneHandle = transitions.Slide.Preloader.StagedSceneHandle;
            Assert.AreNotEqual(0, retainedSceneHandle, "前提：預かっている Scene がある。");

            Assert.IsTrue(transitions.TryTravel(AreaA, AreaAFromBEntry).Accepted,
                "Fade の要求は受理される。");

            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.CompletedCount == 0 && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, transitions.CompletedCount, "Single 読込で着いた。");
            Assert.AreEqual(1, loader.LoadCount, "Single の発行は 1 回だけ。");
            Assert.AreEqual(1, transitions.Slide.DiscardedForSingleLoadCount,
                "預かりを先に解いてから発行した。");
            Assert.IsFalse(transitions.SlideSceneHost.IsLoaded(retainedSceneHandle),
                "預けていた実 Scene は消えている（抱えたまま残さない）。");
            Assert.LessOrEqual(SceneManager.sceneCount, 2,
                "載っているのは着いた先と、その隣を先読みした分まで（上限 2）。");
            Assert.LessOrEqual(transitions.Slide.Residency.ResidentCount, 2,
                "台帳も上限の中。" + DumpResidency(transitions));
        }

        // ------------------------------------------------- 再入場での復元の内訳（工程 P55-11c。§11 の P17）

        /// <summary>
        /// <b>B で立てた CD とスタミナは、保持した A へ戻っても復元される</b>
        /// （§11 の P17。§6.2 手順 5。工程 P55-11c）。
        ///
        /// <b>なぜ別に要るのか。</b> 同じ往復で HP を見る検査
        /// （<c>ChangingTheHpInB_ThenReturningToA_KeepsTheDentedValue</c>）は<b>HP だけ</b>を見ている。
        /// P17 は「B 側で HP／CD を変えて A へ戻る」を求めているので、
        /// <b>HP を見た検査が CD まで保証したとは扱わない</b>。
        /// 値そのものを運ぶ経路（§6.3）は P05 が見ているが、そちらの到着側は<b>新しく読んだ Area</b>で、
        /// Actor が新品なので Snapshot からの復元しか道が無い——
        /// <b>保持した Area では古い Actor がそこに居る</b>ので、復元を呼ばなければ
        /// <b>A を出たときの値</b>（どれも 0）がそのまま見える。これは 2 周目にしか出ない。
        ///
        /// <b>区別する相手は「A の古い値」である。</b> だから A を出るときの値を控えて、
        /// 戻ったときにそれを上回っていることで言う。
        ///
        /// <b>秒数は Data から採る。</b> 構えと回避の CD は<b>設定値が上限</b>で、
        /// 超えると復元そのものが拒否される（<c>EnemyGuardAbility.TryImportTransferSnapshot</c>）——
        /// 実際 30 秒を書いて「防御 CD を立てられた」で落ちた。固定秒を書くと、
        /// Data が変わった日に<b>復元の検査が値域の話で落ちる</b>（P05 と同じ理由）。
        ///
        /// <b>値を立てるのは出入口の手前に立ってから。</b> CD は実時計で減るので、
        /// 立ててから歩き始めると「復元されたか」と「切れたか」が混ざる。
        /// 残り秒数そのものは期待値にしない——歩く時間に寄りかかる検査になる。
        /// 同じ理由で<b>被弾後無敵はここでは見ない</b>：上限が 0.5 秒なので往復より短くでき、
        /// 切れたのか復元しなかったのかを分けられない（P05 が演出区間だけを見て確かめている）。
        /// </summary>
        [UnityTest]
        public IEnumerator ChangingTheCooldownsInB_ThenReturningToA_RestoresThemInsteadOfTheStaleValues()
        {
            // スタミナの回復待ち。値域の検査が無く、要るのは「歩いて戻るあいだに回復が始まらない」ことだけ。
            const float NoRegenWhileWalking = 30f;

            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            ActorValues leftA = ReadActorValues();
            Assert.IsTrue(leftA.Found, "A 側の Actor 部品がそろっている。");
            Assert.AreEqual(0f, leftA.AttackCooldown, 0.001f, "前提：A を出るとき攻撃 CD は 0。");
            Assert.AreEqual(0f, leftA.GuardCooldown, 0.001f, "前提：構えの CD も 0。");
            Assert.AreEqual(0f, leftA.EvadeCooldown, 0.001f, "前提：回避の CD も 0。");
            Assert.AreEqual(0f, leftA.GuardianCooldown, 0.001f, "前提：守護の CD も 0。");
            Assert.Greater(leftA.Stamina, 1f, "前提：スタミナを削れる構成である。");

            // ---- A → B ----
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "前提：B に居る。");

            // ---- 先に戻り口の手前へ立つ（値を立ててから歩かない）----
            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return SettleCamera();

            // ---- B 側で CD とスタミナを立てる（ゲーム自身の復元経路で作る）----
            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            var actor = Object.FindFirstObjectByType<CompanionActor>();
            var combat = Object.FindFirstObjectByType<CompanionCombatController>();
            var defense = Object.FindFirstObjectByType<CompanionDefenseController>();
            var guardian = Object.FindFirstObjectByType<CompanionGuardianController>();
            Assert.IsNotNull(actor, "B 側に犬丸が居る。");
            Assert.IsNotNull(combat, "B 側に犬丸の戦闘部品がある。");

            // 上限は Data が決める（<c>CompanionDefenseController.Build</c> と同じ引き方）。
            var data = actor.Data;
            float guardCd = data != null ? data.GuardCooldownSeconds : 3f;
            float evadeCd = data != null ? data.EvadeCooldownSeconds : 4f;
            float guardianCd = data != null ? data.GuardianCooldownSeconds : 6f;

            // 攻撃 CD には値域の検査が無いので、いちばん長いものに合わせる（新しい固定秒を作らない）。
            float attackCd = Mathf.Max(guardianCd, Mathf.Max(guardCd, evadeCd));

            // <b>前提として言っておく。</b> 設定が往復より短くなった日に、
            // 「復元されなかった」ではなく「切れた」で落ちるのを防ぐ。
            Assert.Greater(guardCd, 1f, "前提：構えの CD の設定が往復より長い（設定=" + guardCd + "）。");
            Assert.Greater(evadeCd, 1f, "前提：回避の CD の設定が往復より長い（設定=" + evadeCd + "）。");
            Assert.Greater(guardianCd, 1f, "前提：守護の CD の設定が往復より長い（設定=" + guardianCd + "）。");

            ActorValues inB = ReadActorValues();
            float dentedStamina = Mathf.Max(1f, inB.Stamina - 5f);

            // <b>回復待ちも長くする。</b> そうしないと歩いているあいだにスタミナが満タンへ戻り、
            // 「復元されたか」ではなく「回復したか」を見る検査になってしまう。
            Assert.IsTrue(vitals.TryImportTransferSnapshot(new PlayerVitalsTransferSnapshot(
                    new VitalTransferSnapshot(inB.Hp),
                    new StaminaTransferSnapshot(dentedStamina, NoRegenWhileWalking, 0f))),
                "B でスタミナを削れた。");
            Assert.IsTrue(combat.TryImportTransferSnapshot(
                    new CompanionCombatTransferSnapshot(attackCd)),
                "攻撃 CD を立てられた（" + attackCd + " 秒）。");
            Assert.IsTrue(defense.TryImportTransferSnapshot(new CompanionDefenseTransferSnapshot(
                    new GuardAbilityTransferSnapshot(guardCd),
                    new EvadeAbilityTransferSnapshot(evadeCd))),
                "防御 CD を立てられた（構え " + guardCd + " 秒・回避 " + evadeCd + " 秒）。"
                + " 設定値が上限である。");
            Assert.IsTrue(guardian.TryImportTransferSnapshot(
                    new CompanionGuardianTransferSnapshot(guardianCd)),
                "守護 CD を立てられた（" + guardianCd + " 秒）。");

            yield return null;

            ActorValues dented = ReadActorValues();
            Assert.Greater(dented.AttackCooldown, 0f, "前提：B 側で CD が立っている。");
            Assert.Greater(dented.GuardCooldown, 0f, "前提：構えの CD も立っている。");
            Assert.AreEqual(dentedStamina, dented.Stamina, 0.001f, "前提：B 側でスタミナが削れている。");

            // ---- B → A（保持していた A へ戻る）----
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value,
                "A に戻っている。失敗=" + transitions.Slide.LastFailure);
            Assert.AreEqual(1, transitions.Slide.ReenteredCount,
                "保持していた Area への再入場として通っている（読み直しではない）。");

            ActorValues backInA = ReadActorValues();
            Assert.IsTrue(backInA.Found, "A 側の Actor 部品がそろっている。");

            string tail = "（A を出たときは 0。B で立てた値と設定=攻撃 " + attackCd + "・構え " + guardCd
                + "・回避 " + evadeCd + "・守護 " + guardianCd
                + "。再入場=" + transitions.Slide.ReenteredCount + "）";

            Assert.Greater(backInA.AttackCooldown, leftA.AttackCooldown,
                "攻撃 CD が B の値で復元されている。A の古い値へ戻っていない。いま="
                + backInA.AttackCooldown + tail);
            Assert.Greater(backInA.GuardCooldown, leftA.GuardCooldown,
                "構えの CD も同じ。いま=" + backInA.GuardCooldown + tail);
            Assert.Greater(backInA.EvadeCooldown, leftA.EvadeCooldown,
                "回避の CD も同じ。いま=" + backInA.EvadeCooldown + tail);
            Assert.Greater(backInA.GuardianCooldown, leftA.GuardianCooldown,
                "守護の CD も同じ。いま=" + backInA.GuardianCooldown + tail);

            Assert.LessOrEqual(backInA.AttackCooldown, attackCd + 0.001f,
                "立てた値を超えていない（増えてはいない）。");

            Assert.Less(backInA.Stamina, leftA.Stamina,
                "スタミナも B で削った値のまま。A の満タンへ戻っていない（削り=" + dentedStamina
                + " 戻り=" + backInA.Stamina + "）。");
            Assert.AreEqual(dentedStamina, backInA.Stamina, 0.001f,
                "削った値がそのまま（回復待ちを長くしてあるので回復もしていない）。");
        }

        /// <summary>
        /// <b>B で Down／Away にした犬丸は、保持した A へ戻ってもその状態のまま</b>
        /// （§4.6 の復元表。§11 の P17。工程 P55-11c）。
        ///
        /// <b>P06（<c>SlidingKeepsTheCompanionState</c>）とは見ている経路が違う。</b>
        /// あちらは A で状態を作って<b>新しく読んだ B</b>へ運ぶ——到着側の犬丸は新品なので、
        /// Snapshot からの復元しか道が無い。ここは<b>保持した A へ戻る</b>ので、
        /// A に残っている犬丸は<b>Follow のまま</b>そこに立っている。
        /// 復元を呼ばなければ、B で倒れた犬丸が<b>A に戻った瞬間に健常へ戻る</b>。
        ///
        /// <b>区別する相手は Follow である。</b> だから A 側が Follow であることを前提として確かめ、
        /// 戻ったときに Follow でないことで言う。
        ///
        /// <b>復帰待ちの秒数は Data から採る。</b> 上限は
        /// <c>CompanionData.LeaveRecoverySeconds</c> で、超えると復元が拒否される
        /// （<c>CompanionVitals.TryImportTransferSnapshot</c>）——30 秒を書いて実際に落ちた。
        /// 立てるのは戻り口の手前に立ってから（復帰待ちは実時計で減る）。
        /// </summary>
        [UnityTest]
        public IEnumerator WithTheCompanionDownOrAwayInB_ReturningToTheRetainedArea_KeepsThatState(
            [Values(CompanionState.Down, CompanionState.Away)] CompanionState wanted)
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            var inA = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(inA, "A に犬丸が居る。");
            Assert.AreEqual(CompanionState.Follow, inA.State,
                "前提：A 側の犬丸は健常である（これが復元しなかったときに見える値）。");

            // ---- A → B ----
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "前提：B に居る。");

            // ---- 先に戻り口の手前へ立つ（状態を作ってから歩かない）----
            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return SettleCamera();

            // ---- B 側で Down／Away にする ----
            var companionVitals = Object.FindFirstObjectByType<CompanionHitReceiver>();
            var arbiter = Object.FindFirstObjectByType<CompanionStateArbiter>();
            var actorInB = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(companionVitals, "B 側に犬丸の生存値がある。");
            Assert.IsNotNull(arbiter, "B 側に犬丸の状態調停役がある。");
            Assert.IsNotNull(actorInB, "B 側に犬丸が居る。");

            if (wanted == CompanionState.Down)
            {
                // 上限は Data（<c>CompanionVitals</c> の組み立てと同じ引き方）。
                var data = actorInB.Data;
                float recovery = data != null && data.LeaveRecoverySeconds > 0f
                    ? data.LeaveRecoverySeconds
                    : CompanionVitals.DefaultRecoverySeconds;
                Assert.Greater(recovery, 1f,
                    "前提：復帰待ちの設定が往復より長い（設定=" + recovery + "）。");

                Assert.IsTrue(companionVitals.Vitals.TryImportTransferSnapshot(
                        new CompanionVitalsTransferSnapshot(0, true, recovery, 0f,
                            new FlinchTransferSnapshot(0f, 0f, 0f, 0f))),
                    "B で Down ＋ 復帰待ちにできた（" + recovery + " 秒。設定値が上限である）。");
            }

            Assert.IsTrue(arbiter.TryRestoreState(wanted), "B で " + wanted + " へ置けた。");
            yield return null;

            Assert.AreEqual(wanted, actorInB.State, "前提：B 側が " + wanted + " になっている。");

            // ---- B → A（保持していた A へ戻る）----
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value,
                "A に戻っている。失敗=" + transitions.Slide.LastFailure);
            Assert.AreEqual(1, transitions.Slide.ReenteredCount,
                "保持していた Area への再入場として通っている。");

            var backInA = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(backInA, "A 側に犬丸が居る。");
            Assert.AreNotEqual(CompanionState.Follow, backInA.State,
                "A に残っていた健常な犬丸へ戻っていない（§4.6）。いま=" + backInA.State);
            Assert.AreEqual(wanted, backInA.State,
                "B で作った状態のまま復元されている。いま=" + backInA.State
                + " 再入場=" + transitions.Slide.ReenteredCount);

            if (wanted == CompanionState.Down)
            {
                ActorValues values = ReadActorValues();
                Assert.IsTrue(values.CompanionDown, "Down の生存値も復元されている。");
                Assert.Greater(values.CompanionRecovery, 0f,
                    "復帰待ちも残っている（勝手に復帰していない）。残り=" + values.CompanionRecovery);
            }
        }

        // ---------------------------------------------------------------- P18（工程 P55-10e）

        /// <summary>
        /// <b>ロードが終わっていても、到着準備が遅れれば待ち表示を出す</b>
        /// （§5。§11 の P18。工程 P55-10e。GPT 追加修正）。
        ///
        /// 工程 P55-09a の実装は先読みの <c>Loading</c>／<c>Releasing</c> の間<b>だけ</b>数えていた。
        /// その前後——旧 Area の撤去待ちと<b>到着側の初期化待ち</b>——では表示されない。
        /// つまり<b>ロードは終わったのに初期化が返ってこない</b>とき、
        /// プレイヤーは操作不能のまま、何の説明も無く待たされていた。
        ///
        /// <b>09a の検査はこの経路を一度も通っていなかった。</b> 待ちを作る手段が
        /// 「先読み未完」しか無かったからである——読み終わったあとの区間は作れなかった。
        ///
        /// ここでは<b>隣を先に載せてから</b>、到着側の初期化担当を取り除く。
        /// 読込の待ちは 0 フレームで、待ちはすべて「準備の報告が来ない」区間になる。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheArrivalPreparationIsLate_TheWaitNoticeShowsAndThenClears()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            AreaExitGate gate = FindExitGate(ExitAEast);

            // <b>「既に載っている」は往復で作る</b>（工程 P55-15c。距離では読まなくなった）。
            yield return StandJustBefore(gate, Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            gate = FindExitGate(ExitAEast);
            AreaPreloader preloader = transitions.Slide.Preloader;

            // <b>往復ぶんを控える</b>（工程 P55-15c）。初回ロードの待ちは出てよいので、
            // 「この遷移で何回出たか」で見る。
            AreaTransitionWaitNoticeHost noticeHost = AreaTransitionWaitNoticeHost.Instance;
            Assert.IsNotNull(noticeHost, "待ち表示の常駐が居る。");
            int hostShownBefore = noticeHost.ShowCount;
            int begunBefore = transitions.Slide.WaitNotice.BegunCount;
            Assert.AreEqual(AreaPreloadPhase.Staged, preloader.Phase,
                "前提：隣が閉じたまま載っている（読込の待ちは 0 になる）。" + DumpWorld(transitions));
            int loadsBefore = preloader.LoadStartedCount;

            // <b>到着側の初期化担当を取り除く。</b> 準備完了の報告が誰からも来なくなるので、
            // 遷移は「到着側の準備待ち」で <c>BindTimeoutSeconds</c> まで待ってから戻る。
            // 実際の遅れ（重い Awake・Bootstrap 待ち）と同じ区間を、決定的に作れる。
            int removed = 0;
            foreach (AreaInitializer initializer in
                Object.FindObjectsByType<AreaInitializer>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (initializer != null && initializer.AreaId.Equals(AreaB))
                {
                    Object.DestroyImmediate(initializer);
                    removed++;
                }
            }

            Assert.AreEqual(1, removed, "前提：到着側の初期化担当を 1 つ取り除いた。");

            // 0.3 秒の壁を越えるが、検査が長引かない長さにする。
            transitions.BindTimeoutSeconds = 1.2f;

            AreaTransitionWaitNoticeHost notice = AreaTransitionWaitNoticeHost.Instance;
            Assert.IsNotNull(notice, "待ち表示の常駐が居る。");
            Assert.AreEqual(0, notice.ShowCount, "前提：まだ一度も出していない。");

            yield return StandJustBefore(gate, Vector3.left);
            yield return SettleCamera();

            yield return HoldUntil(Key.D, () => transitions.SlideRolledBackCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideRolledBackCount,
                "到着側の準備が来ないので戻した。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(2, transitions.SlideCommittedCount,
                "この遷移では着いていない（往復の 2 回から増えていない）。");

            // ---- 待ち表示は出た ----
            AreaTransitionWaitNoticeTimer timer = transitions.Slide.WaitNotice;
            Assert.AreEqual(1, timer.ShownCount,
                "ロード済みでも、到着準備の遅れで待ち表示を出す（待ち="
                + timer.WaitedSeconds + " 秒・出した時点=" + timer.ShownAtSeconds + " 秒）。");
            Assert.GreaterOrEqual(timer.ShownAtSeconds,
                AreaTransitionWaitNoticeTimer.ThresholdSeconds - 0.001f,
                "出したのは 0.3 秒を越えてから（決めた時点の秒数で見る）。");
            Assert.AreEqual(hostShownBefore + 1, notice.ShowCount,
                "常駐にも 1 回だけ出させた（この遷移で "
                + (notice.ShowCount - hostShownBefore) + " 回）。");

            // ---- 待ちは 1 本。段階が変わってもリセットしない ----
            Assert.AreEqual(begunBefore + 1, timer.BegunCount,
                "受理からスライド開始までを<b>1 本の待ち</b>として数えている"
                + "（段階ごとに 0 から数え直していない。この遷移で "
                + (timer.BegunCount - begunBefore) + " 本）。");

            // ---- 読込の待ちではない ----
            Assert.AreEqual(loadsBefore, preloader.LoadStartedCount,
                "読み直していない（待ちはすべて準備の報告待ちだった）。");

            // ---- 戻したら消える ----
            Assert.IsFalse(notice.IsShowing, "Rollback で消えている（§5）。");
            Assert.AreEqual(1, notice.HideCount, "消したのは 1 回。");

            // ---- 段階ごとの上限は、段階が持ったまま ----
            Assert.Less(timer.WaitedSeconds, transitions.BindTimeoutSeconds + 1.5f,
                "到着側の待ちは自分の上限（" + transitions.BindTimeoutSeconds
                + " 秒）で切れている。通算へ寄せていたら切れない。");

            // ---- 戻った先で遊べる ----
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に戻っている。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "A で遊べる。");
        }

        /// <summary>
        /// <b>旧 Area の撤去待ちでも待ち表示を出す</b>（§5。§11 の P18。工程 P55-10e）。
        ///
        /// GPT が名指しした「その前の旧 Area 撤去待ち」の側である。到着通知の中から次の遷移を頼むと、
        /// 新しい遷移は<b>前の撤去が終わるまで</b>進めない（§8 末尾）。
        /// そこで操作不能のまま待たされるのに、09a では表示されなかった。
        ///
        /// <b>保持を切って見る。</b> 裁定 2（工程 P55-10c）で旧 Area は既定で保持されるので、
        /// そもそも撤去が走らない——この区間は保持を切った構成にしか存在しない。
        /// 通らない経路は壊れても誰も気付かないので、切って通す（付録 C.29.5）。
        /// </summary>
        [UnityTest]
        public IEnumerator WhileTheRetireIsStillRunning_TheWaitNoticeShows()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            transitions.RetainDepartedArea = false;

            var host = new DelayedRealSceneHost();
            transitions.SlideSceneHost = host;

            bool requested = false;
            void OnArrived(StableId areaId)
            {
                if (requested || !areaId.Equals(AreaB))
                {
                    return;
                }

                requested = true;
                Assert.IsTrue(
                    transitions.Connections.TryGetFromExit(AreaB, ExitBWest, out AreaConnectionSnapshot west),
                    "西向きの接続を引ける。");
                Assert.IsTrue(transitions.TryTravel(west).Accepted, "通知の中からの要求が受理される。");
            }

            transitions.ArrivalCompleted += OnArrived;
            try
            {
                yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);

                // <b>撤去だけを止める。</b> 読込は毎フレーム解放する。
                host.HoldUnload = true;

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

                Assert.AreEqual(1, transitions.SlideCommittedCount, "前提：スライドで着いた。");
                Assert.IsTrue(requested, "前提：到着通知の中から次の遷移を頼んだ。");
                Assert.IsTrue(transitions.Slide.IsRetiring, "前提：撤去がまだ走っている。");

                AreaTransitionWaitNoticeHost notice = AreaTransitionWaitNoticeHost.Instance;
                Assert.IsNotNull(notice, "待ち表示の常駐が居る。");

                // ---- 撤去待ちのまま 0.3 秒を越えさせる ----
                float until = Time.realtimeSinceStartup + 0.8f;
                while (Time.realtimeSinceStartup < until)
                {
                    host.ReleaseLoad();
                    yield return null;
                }

                Assert.IsTrue(transitions.Slide.IsRetiring, "まだ撤去中（前提が崩れていない）。");
                Assert.IsTrue(notice.IsShowing,
                    "撤去の終わりを待っている間も待ち表示を出す（待ち="
                    + transitions.Slide.WaitNotice.WaitedSeconds + " 秒・出した時点="
                    + transitions.Slide.WaitNotice.ShownAtSeconds + " 秒）。");
                Assert.AreEqual(2, transitions.Slide.WaitNotice.BegunCount,
                    "待ちは<b>遷移ごとに 1 本</b>（ここまで 2 回遷移したので 2 本）。"
                    + "段階ごとに数え直していたら、この数は段階の数だけ増える。");

                // ---- 撤去が終われば進み、スライド開始で消える ----
                host.ReleaseUnload();

                float deadline = Time.realtimeSinceStartup + 25f;
                while (transitions.SlideCommittedCount < 2
                       && !transitions.HasTerminalFailure
                       && Time.realtimeSinceStartup < deadline)
                {
                    host.ReleaseLoad();
                    host.ReleaseUnload();
                    yield return null;
                }

                Assert.AreEqual(2, transitions.SlideCommittedCount,
                    "撤去が終わってから二度目も着いた。理由=" + transitions.Slide.LastFailure);
                Assert.IsFalse(notice.IsShowing, "スライドが始まったので消えている（§5）。");
            }
            finally
            {
                transitions.ArrivalCompleted -= OnArrived;
            }
        }

        // ---------------------------------------------------------------- P19（工程 P55-10f）

        /// <summary>
        /// <b>P19</b>：非フォーカス中はスライドの表示時間を進めず、復帰時に巨大 delta で飛ばさない
        /// （§7.1。§11 の P19。工程 P55-10f）。
        ///
        /// §7.1 は「0.45 秒は<b>演出時間</b>であり Scene ロード時間を含まない。
        /// アプリ非フォーカス中はスライドの表示時間を進めず、復帰時に巨大 delta で飛ばさない。
        /// ロード監視は別の unscaled／実時間で継続する」と定める。
        ///
        /// <b>見るのは 2 つで、どちらも外しやすい。</b>
        /// <list type="number">
        /// <item><description><b>止まること</b>——背面に回っているあいだ、カメラも代理も動かない。
        /// 進めてしまうと、戻ってきたときには<b>もう着いている</b>（移動を一切見ていない）。</description></item>
        /// <item><description><b>戻ったときに飛ばないこと</b>——非フォーカスから戻った 1 フレームの
        /// <c>unscaledDeltaTime</c> は<b>止まっていた時間そのもの</b>になりうる。
        /// そのまま渡すと、止めた意味が 1 フレームで消える。</description></item>
        /// </list>
        ///
        /// <b>Editor では Engine の通知が使えない。</b> 無人の自動実行では Game View に
        /// フォーカスが無いのが普通で（<c>PlayModeInputFocusFixture</c> が入力について同じ問題に
        /// 対処している）、通知を聞くと PlayMode の全件が一斉に止まる。
        /// だから常駐は Editor で通知を聞かず、ここは<b>窓口を明示的に切り替えて</b>見る——
        /// ビルドでも同じ道（<c>AppFocusHost.Apply</c>）を通るので、検査だけの別経路にはならない。
        /// </summary>
        [UnityTest]
        public IEnumerator WhileTheAppIsNotFocused_TheSlideDoesNotAdvanceAndDoesNotJumpBack()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            AppFocusHost focus = AppFocusHost.Instance;
            Assert.IsNotNull(focus, "前面判定の常駐が居る（常駐 Rig が足す）。");
            Assert.IsTrue(AppFocusProvider.IsFocused, "前提：前面に居る。");

            // ---- スライドが始まるまで歩く ----
            AreaCameraRigHost rig = AreaCameraRigHost.Instance;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (!rig.IsSliding && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.IsTrue(rig.IsSliding, "前提：スライドが始まった。失敗=" + transitions.Slide.LastFailure);
            Assert.Less(rig.SlideEased, 0.5f,
                "前提：まだ序盤である（止めたあと 1 フレームで終わってしまう位置では見られない）。"
                + " 進行度=" + rig.SlideEased);

            // ---- 背面へ回す ----
            focus.Apply(false);
            Assert.IsFalse(AppFocusProvider.IsFocused, "前提：非フォーカスになった。");
            Assert.AreEqual(1, focus.LostCount, "前面を失ったと数えている。");

            float easedAtPause = rig.SlideEased;
            Vector3 rigAtPause = RigPosition();
            AreaTransitionDisplayProxy proxy = AreaTransitionDisplayHost.Instance.Set.Player;
            Assert.IsNotNull(proxy, "前提：主人公の表示代理が立っている。");
            int frameAtPause = proxy.FrameIndex;
            Vector3 proxyAtPause = proxy.transform.position;

            for (int i = 0; i < 30; i++)
            {
                yield return null;

                Assert.AreEqual(easedAtPause, rig.SlideEased, 1e-6f,
                    "非フォーカス中は進行度が動かない（" + i + " フレーム目）。");
                Assert.AreEqual(rigAtPause, RigPosition(),
                    "カメラも動かない（" + i + " フレーム目）。");
                Assert.AreEqual(frameAtPause, proxy.FrameIndex,
                    "代理のコマも進まない（表示時計そのものが止まっている。" + i + " フレーム目）。");
                Assert.AreEqual(proxyAtPause, proxy.transform.position,
                    "代理も動かない（" + i + " フレーム目）。");
            }

            Assert.IsTrue(rig.IsSliding, "スライドは終わっていない（止まっているだけ）。");
            Assert.AreEqual(0, transitions.SlideCommittedCount, "着いてもいない。");

            // ---- 前面へ戻す ----
            //
            // <b>ここでは巨大 delta を作れない。</b> 背面に回しても Unity はフレームを回し続けるので、
            // 戻った 1 フレームの <c>unscaledDeltaTime</c> は普通のフレーム時間のままである
            // （実機で起きる「止まっていた時間がそのまま届く」は再現できない）。
            // 上限そのものは EditMode（<c>P55AppFocusTests.AHugeStep_IsCappedSoTheSlideCannotJump</c>）が
            // 値を与えて見る。ここで見るのは<b>止めた続きから進むこと</b>である。
            focus.Apply(true);
            yield return null;

            Assert.AreEqual(1, focus.RegainedCount, "前面へ戻ったと数えている。");
            Assert.Greater(rig.SlideEased, easedAtPause, "戻ったので進み始めた。");
            Assert.Less(rig.SlideEased, 1f,
                "戻った 1 フレームで終端まで飛んでいない（進行度=" + rig.SlideEased + "）。");
            Assert.IsTrue(rig.IsSliding, "まだスライド中である。");

            // ---- そのまま着く ----
            float finish = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < finish)
            {
                yield return null;
            }

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "止めて戻しても、そのまま着く。失敗=" + transitions.Slide.LastFailure);
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
            Assert.IsFalse(rig.IsSliding, "演出は終わっている。");
        }

        /// <summary>
        /// <b>Editor では Engine の通知を聞いていない——だから規則は窓口で見ている</b>
        /// （§11 の P19 の受入区分。工程 P55-11c）。
        ///
        /// <b>この検査は境目そのものを固定する。</b> 非フォーカスの実装は Editor で
        /// <c>OnApplicationFocus</c>／<c>OnApplicationPause</c> を<b>コンパイルごと外している</b>
        /// （無人の自動実行では Game View にフォーカスが無いのが普通で、聞くと PlayMode 全件が止まる）。
        /// つまり受入で言えているのは<b>規則のほう</b>だけで、
        /// <b>実際の通知が届くこと</b>は Editor では一度も試していない。
        /// それを散文で書くだけにすると、あとから読む人が「P19 は全部見てある」と受け取る。
        ///
        /// ここで固定するのは 3 つ。
        /// <list type="number">
        /// <item><description>常駐が実際に居て、窓口へ差さっている（配線は実 Scene で言える）。</description></item>
        /// <item><description>Editor では Engine の通知を<b>聞いていない</b>
        /// （<c>ListensToEngineNotifications</c>）。ビルドでは聞く。</description></item>
        /// <item><description>規則を見るときに通る口（<c>Apply</c>）は、
        /// ビルドで通知が呼ぶ口と<b>同じ</b>——検査だけの別経路ではない。</description></item>
        /// </list>
        ///
        /// <b>ビルド上で実際の通知に従うことは、ここでは確かめられない</b>（記録 047 §3 に
        /// 手動確認事項として残す）。この検査はその<b>欠けている一片を名指しする</b>ためにある。
        /// </summary>
        [UnityTest]
        public IEnumerator TheFocusHost_IsWiredButDoesNotListenToEngineNotificationsInTheEditor()
        {
            yield return EnterArea(P55AreaAScene);

            AppFocusHost host = AppFocusHost.Instance;
            Assert.IsNotNull(host, "前面判定の常駐が居る（常駐 Rig が足している）。");
            Assert.IsTrue(AppFocusProvider.HasOwner, "窓口へ差さっている。");
            Assert.AreSame(host, AppFocusProvider.Current,
                "差さっているのはこの常駐である（表示時計はここを読む）。");
            Assert.IsTrue(AppFocusProvider.IsFocused, "既定は前面。");

            // <b>ここが受入の境目である。</b> Editor は false、ビルドは true。
            Assert.AreEqual(!Application.isEditor, AppFocusHost.ListensToEngineNotifications,
                "Editor では Engine の通知を聞かない（ビルドでは聞く）。"
                + " したがって P19 の受入で言えているのは<b>規則</b>のほうだけで、"
                + "実通知が届くことは別に手で確かめる必要がある。");

            // <b>規則を見るときに通る口は、ビルドで通知が呼ぶ口と同じ。</b>
            int lostBefore = host.LostCount;
            int regainedBefore = host.RegainedCount;

            host.Apply(false);
            Assert.IsFalse(AppFocusProvider.IsFocused, "窓口越しに非フォーカスへ落ちた。");
            Assert.AreEqual(lostBefore + 1, host.LostCount, "前面を失った回数が 1 つ増える。");
            Assert.AreEqual(0f, AreaSlideTransitionRunner.ResolveDisplayStep(1f / 60f, false), 0.0001f,
                "非フォーカスでは表示時計が進まない（§7.1 の規則そのもの）。");

            host.Apply(true);
            Assert.IsTrue(AppFocusProvider.IsFocused, "前面へ戻った。");
            Assert.AreEqual(regainedBefore + 1, host.RegainedCount, "戻った回数も 1 つ増える。");
            Assert.Greater(AreaSlideTransitionRunner.ResolveDisplayStep(1f / 60f, true), 0f,
                "前面では進む。");
        }

        // ---------------------------------------------------------------- 保持が有効なままの解放失敗（工程 P55-11b）

        /// <summary>
        /// <b>保持した Area の解放が失敗しても、到着は取り消さない。再試行で片付く</b>
        /// （§8 の 4 行目／§6.2 手順 11。工程 P55-11b。GPT 指示 2）。
        ///
        /// <b>保持を切った検査では、この経路を代替できない。</b> 撤去経路の受入 5 件は
        /// <c>RetainDepartedArea = false</c> で見ているが、あれは<b>Commit 直後の撤去</b>が
        /// 失敗する経路である。裁定 2 のあと既定で通るのは<b>こちら</b>——
        /// 預けた先（先読み枠）の解放が失敗する経路で、抱える場所も再試行の入口も違う。
        ///
        /// <list type="number">
        /// <item><description>保持が有効なまま A→B へ渡り、<b>A が預けられた</b>ことを見る。</description></item>
        /// <item><description>預けた A の解放を失敗させ、<c>ReleaseFailed</c> と
        /// <see cref="AreaSlideTransitionRunner.HasUnreleasedScene"/> を見る。</description></item>
        /// <item><description>解放が失敗しても<b>到着済みの B も進行値も巻き戻らない</b>。</description></item>
        /// <item><description><b>無操作では再試行を連発しない</b>——押すのはプレイヤー（§5 と同じ規律）。</description></item>
        /// <item><description>明示的な再試行で解放が進み、抱え込みが解ける。</description></item>
        /// </list>
        /// </summary>
        [UnityTest]
        public IEnumerator WhenTheRetainedAreaCannotBeReleased_TheArrivalStaysAndRetryClearsIt()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            Assert.IsTrue(transitions.RetainDepartedArea, "前提：保持は既定で有効である。");

            // 撤去だけを断る。読込は本物へ通すので、スライドそのものは成立する。
            var host = new UnloadRefusingSceneHost();
            transitions.SlideSceneHost = host;

            GameSessionState session = GameSessionProvider.Current;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            // ---- 1. A は預けられた ----
            AreaPreloader preloader = transitions.Slide.Preloader;
            Assert.AreEqual(1, transitions.Slide.RetainedCount,
                "旧 A を預けた。断った理由=" + transitions.Slide.LastRetainDecline);
            Assert.AreEqual(0, host.UnloadCount, "預けたので撤去は一度も頼んでいない。");
            Assert.AreEqual(AreaPreloadPhase.Staged, preloader.Phase, "預かっている。");
            Assert.AreEqual(AreaA.Value, preloader.StagedArea.AreaId.Value, "預かっているのは A。");

            int virtueBefore = session.Progress.Virtue;
            bool visitedBefore = session.HasVisited(AreaB);
            int loadsBefore = preloader.LoadStartedCount;

            // ---- 2. 解放を失敗させる ----
            //
            // <c>ClearRequest</c> はその場で <c>Poll</c> まで進むので、距離による先読みが
            // 望みを立て直す前に解放が始まる。撤去を断る host なので、そのまま失敗する。
            preloader.ClearRequest();

            float deadline = Time.realtimeSinceStartup + 10f;
            while (preloader.Phase != AreaPreloadPhase.ReleaseFailed
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(AreaPreloadPhase.ReleaseFailed, preloader.Phase,
                "解放に失敗して抱えている。" + DumpWorld(transitions));
            Assert.IsTrue(transitions.Slide.HasUnreleasedScene,
                "「撤去し切れていない Scene がある」と言える（HasUnreleasedScene）。");
            Assert.IsFalse(transitions.Slide.HasPendingRetire,
                "抱えているのは<b>撤去経路ではない</b>（預けた先が抱えている）。");
            Assert.IsNotEmpty(preloader.FailureReason, "理由が残っている。");
            Assert.AreEqual(2, SceneManager.sceneCount, "A の実 Scene はまだ載っている。");
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                "在留枠も返さない（空きありと誤認して 3 枚目を読まない）。" + DumpResidency(transitions));

            // ---- 3. 到着も進行値も巻き戻らない ----
            Assert.AreEqual(1, transitions.SlideCommittedCount, "到着の成功は取り消さない（§8 の 4 行目）。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "B で遊べる。");
            Assert.AreEqual(virtueBefore, session.Progress.Virtue, "徳は動かない。");
            Assert.AreEqual(visitedBefore, session.HasVisited(AreaB), "訪問記録も動かない。");
            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle retained), "A の束はまだある。");
            Assert.IsFalse(retained.ActivityGate.IsOpen, "抱えている A は非活動のまま。");
            Assert.IsFalse(retained.Context.IsAreaReady, "A では遊べない。");

            // ---- 4. 無操作では再試行を連発しない ----
            int unloadsAfterFailure = host.UnloadCount;
            Assert.AreEqual(1, unloadsAfterFailure, "解放を頼んだのは 1 回だけ。");
            int suppressedBefore = preloader.SuppressedRetryCount;

            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.AreEqual(unloadsAfterFailure, host.UnloadCount,
                    "誰も押していないのに解放を撃ち直さない（" + i + " フレーム目）。"
                    + " 恒久的に失敗する Scene へ延々と操作を出さない（§5 と同じ規律）。");
                Assert.AreEqual(loadsBefore, preloader.LoadStartedCount,
                    "抱えている間は次の候補も読まない（" + i + " フレーム目）。");
            }

            Assert.Greater(preloader.SuppressedRetryCount, suppressedBefore,
                "見送った回数を数えている（黙って止まっているのではない）。");

            // ---- 5. 明示的な再試行で片付く ----
            //
            // <b>待つ相手を <c>HasUnreleasedScene</c> にしてはいけない</b>（GPT 指摘 2。工程 P55-13a）。
            // あれは先読み側について <c>ReleaseFailed</c> だけを見ているので、
            // 再試行が <c>Releasing</c> へ移った瞬間に false になる——
            // **「再試行を始めた」までは言えるが「片付いた」は言えない**。
            //
            // <b>控えた handle の実 Scene が消えるまで待つ。</b> Scene の枚数や AreaId では言えない——
            // §5 の距離による先読みが<b>同じ Area をすぐ読み直す</b>（到着した主人公は
            // 逆向きの出入口のすぐ内側に居る）。読み直された Scene は別物なので、
            // 枚数は 2 のままになり、AreaId も A のままになる。
            int retainedSceneHandle = preloader.StagedSceneHandle;
            AreaInstanceHandle retainedInstance = preloader.StagedArea;
            Assert.AreNotEqual(0, retainedSceneHandle, "前提：預かっている実 Scene がある。");
            Assert.IsTrue(host.IsLoaded(retainedSceneHandle), "前提：その Scene は載っている。");
            Assert.AreNotEqual(AreaActivationPhase.Unloaded,
                transitions.Slide.Residency.PhaseOf(retainedInstance),
                "前提：旧実体は台帳に載っている。" + DumpResidency(transitions));

            // ---- 5a. 解放を保留しているあいだは、完了扱いにならない ----
            //
            // 撤去を通すようにしてから<b>終端させない</b>。「始まったから片付いた」で
            // 通ってしまわないことを、ここで塞ぐ。
            host.RefuseUnload = false;
            host.HoldUnload = true;

            Assert.IsTrue(transitions.Slide.TryRetryRetiringDeparture(),
                "再試行を始められる（抱えているのが預けた先でも、押す場所は 1 つ）。");
            Assert.AreEqual(1, transitions.Slide.RetryStartedCount, "再試行を数えている。");
            yield return null;

            Assert.AreEqual(2, host.UnloadCount, "解放を頼んだのは失敗した 1 回と再試行の 1 回だけ。");

            for (int i = 0; i < 20; i++)
            {
                yield return null;
                Assert.IsTrue(host.IsLoaded(retainedSceneHandle),
                    "保留しているあいだ、控えた実 Scene は消えない（" + i + " フレーム目・先読み="
                    + preloader.Phase + "）。");
                Assert.IsTrue(transitions.Slide.HasLiveSceneOperation,
                    "終端していない Scene 操作として数えている（" + i + " フレーム目）。"
                    + " ここが false なら「片付いた」と誤認できる。");
                Assert.AreNotEqual(AreaActivationPhase.Unloaded,
                    transitions.Slide.Residency.PhaseOf(retainedInstance),
                    "台帳からも落としていない（" + i + " フレーム目）。" + DumpResidency(transitions));
                Assert.AreEqual(2, host.UnloadCount,
                    "走っているあいだに撃ち直してもいない（" + i + " フレーム目）。");

                // <b>これが GPT 指摘 2 の理由そのものである。</b> 実 Scene はまだ載っているのに
                // <c>HasUnreleasedScene</c> は false になる——先読み側については
                // <c>ReleaseFailed</c> だけを見ているので、<c>Releasing</c> へ移った瞬間に落ちる。
                // **この値で「片付いた」を判定してはいけない**ことを、ここで固定しておく。
                // 名前（撤去し切れていない Scene がある）より<b>測っている範囲が狭い</b>。
                Assert.IsFalse(transitions.Slide.HasUnreleasedScene,
                    "HasUnreleasedScene は解放中には false になる（" + i + " フレーム目）。"
                    + " 実 Scene は載っているので、待つ相手はこの値ではなく控えた handle である。");
            }

            // ---- 5b. 終端させると、実 Scene も参照も片付く ----
            host.ReleaseUnload();

            float cleared = Time.realtimeSinceStartup + 15f;
            while (host.IsLoaded(retainedSceneHandle) && Time.realtimeSinceStartup < cleared)
            {
                yield return null;
            }

            Assert.IsFalse(host.IsLoaded(retainedSceneHandle),
                "控えた handle の実 Scene が消えた（解放が<b>終端した</b>）。" + DumpWorld(transitions));

            // 解放の<b>操作</b>が終端したことは、段階で言う——<c>HasLiveSceneOperation</c> では言えない。
            // 解放が終わった直後に距離による先読みが A を読み直すので、あの値はまた true になりうる。
            float settled = Time.realtimeSinceStartup + 10f;
            while (transitions.Slide.Residency.PhaseOf(retainedInstance) != AreaActivationPhase.Unloaded
                   && Time.realtimeSinceStartup < settled)
            {
                yield return null;
            }

            Assert.AreNotEqual(AreaPreloadPhase.Releasing, preloader.Phase,
                "もう解放中ではない（操作が終端した）。いま=" + preloader.Phase);
            Assert.AreNotEqual(AreaPreloadPhase.ReleaseFailed, preloader.Phase,
                "失敗を抱えてもいない。いま=" + preloader.Phase);
            Assert.AreEqual(AreaActivationPhase.Unloaded,
                transitions.Slide.Residency.PhaseOf(retainedInstance),
                "旧実体の台帳登録が消えた。" + DumpResidency(transitions));
            Assert.AreNotEqual(retainedInstance, preloader.StagedArea,
                "保持参照も、その実体を指していない（読み直したなら別世代である）。");
            Assert.AreNotEqual(retainedSceneHandle, preloader.StagedSceneHandle,
                "預かっている Scene も別物である。");
            Assert.IsFalse(transitions.Slide.HasUnreleasedScene, "抱え込みが解けた。");
            Assert.AreEqual(2, host.UnloadCount, "解放を頼んだのは失敗した 1 回と再試行の 1 回だけ。");
            Assert.AreEqual(1, transitions.SlideCommittedCount, "到着の成功は増減しない。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居たまま。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "B で遊べたまま。");
            Assert.LessOrEqual(transitions.Slide.Residency.ResidentCount, 2,
                "台帳も上限の中。" + DumpResidency(transitions));
        }

        /// <summary>
        /// <b>解放の操作が走っているあいだは、Single 読込を発行しない</b>
        /// （§6.2 手順 11 の契約表「Single 遷移」。工程 P55-11b。GPT 指示 2 の 5）。
        ///
        /// <b>止めるのは「終端していない操作」だけである</b>（付録 C.20 の表）。
        /// 最初この検査を「<c>ReleaseFailed</c> を抱えている間は発行しない」と書いて落ちた——
        /// <c>ReleaseFailed</c> は<b>操作が終端したあとの状態</b>で、実 Scene が残っているだけである。
        /// Single 読込は載っている Scene を<b>全部置き換える</b>ので、残っている Scene は
        /// 発行を止める理由にならない（止めると、一度解放に失敗しただけで
        /// 死亡再開も扉移動も通らなくなる）。
        ///
        /// 止めるべきなのは<b>解放の Unload が走っている最中</b>——そこへ Single を重ねると、
        /// 終端していない操作の上に新しい操作が乗る。ここではその窓を作って見る。
        ///
        /// <b>発行そのものを数える</b>（工程 P55-07c と同じ理由）。「着いていない」では
        /// 「まだ終わっていない」としか言えない。
        /// </summary>
        [UnityTest]
        public IEnumerator WhileTheRetainedReleaseIsRunning_NoSingleLoadIsIssued()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new UnloadRefusingSceneHost { RefuseUnload = false };
            transitions.SlideSceneHost = host;

            var loader = new CountingSceneLoader(transitions.Loader);
            transitions.Loader = loader;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            AreaPreloader preloader = transitions.Slide.Preloader;
            Assert.AreEqual(1, transitions.Slide.RetainedCount, "前提：A を預けている。");
            Assert.AreEqual(0, host.UnloadCount, "前提：撤去はまだ一度も頼んでいない。");
            Assert.AreEqual(0, loader.LoadCount, "前提：Single 読込も発行されていない。");

            // ---- 解放を始めさせ、終端させない ----
            host.HoldUnload = true;
            preloader.ClearRequest();
            yield return null;

            Assert.AreEqual(AreaPreloadPhase.Releasing, preloader.Phase,
                "前提：解放が走っている。" + DumpWorld(transitions));
            Assert.IsTrue(transitions.Slide.HasLiveSceneOperation,
                "終端していない Scene 操作として数えている。");
            Assert.AreEqual(1, host.UnloadCount, "解放は 1 回だけ頼まれている。");

            // ---- その最中に Fade を頼む ----
            Assert.IsTrue(transitions.TryTravel(AreaA, AreaAFromBEntry).Accepted,
                "Fade の要求は受理される（受理そのものは止めない）。");

            for (int i = 0; i < 30; i++)
            {
                yield return null;
                Assert.AreEqual(0, loader.LoadCount,
                    "解放が終端するまで Single 読込を<b>発行</b>しない（" + i + " フレーム目・先読み="
                    + preloader.Phase + "）。");
            }

            Assert.AreEqual(AreaPreloadPhase.Releasing, preloader.Phase,
                "まだ解放中のまま（前提が崩れていない）。");
            Assert.AreEqual(1, host.UnloadCount, "解放を撃ち直してもいない。");

            // ---- 終端すれば、一度だけ発行される ----
            host.ReleaseUnload();

            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.CompletedCount == 0 && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, loader.LoadCount, "解放の終端後に一度だけ発行される。");
            Assert.AreEqual(1, transitions.CompletedCount, "その発行が着いた。");
            Assert.IsFalse(transitions.Slide.HasUnreleasedScene, "抱え込みも残っていない。");
        }

        /// <summary>
        /// <b>解放に失敗して抱えたままでも、Single 読込は止めない</b>
        /// （付録 C.20 の「『失敗した』と『まだ掴んでいる』は別」。工程 P55-11b。修正 P55-13a）。
        ///
        /// <b>「解放が終端するまで」は「解放が成功するまで」ではない。</b> ここを取り違えると
        /// 検査が空振りする。<c>ReleaseFailed</c> は**操作が終端したあとの状態**であり、
        /// 終端しているのだから新しい Scene 操作を重ねてよい。残っているのは実 Scene だけで、
        /// Single 読込は載っている Scene を<b>全部置き換える</b>。
        /// 止めれば、一度解放に失敗しただけで<b>死亡再開も扉移動も通らなくなる</b>。
        /// 止めるべきなのは<b>Unload が走っている最中</b>（<c>HasLiveSceneOperation</c>）だけである。
        ///
        /// <b>最初この検査は、Single を頼む直前に撤去の拒否を解いていた</b>（GPT 指摘 1）。
        /// そうすると <c>DiscardStagedForSingleLoad</c> の中の再試行が<b>成功してから</b>
        /// Single へ進む経路でも合格してしまう——**見たかった経路を通らずに緑になる**。
        /// いまは<b>拒否を最後まで維持する</b>。再試行も失敗し、
        /// 実 Scene を抱えたまま Single が発行される。
        ///
        /// 発行の瞬間を 3 つで固定する。
        /// <list type="number">
        /// <item><description>控えた旧 Scene が<b>まだ載っている</b>（engine に聞く）。</description></item>
        /// <item><description><b>終端していない Scene 操作は無い</b>。</description></item>
        /// <item><description><c>SingleLoadOverRemainingSceneCount</c> が<b>1 つ増える</b>——
        /// 実装自身が「載ったままの Scene を抱えて Single へ進んだ」と数える口。</description></item>
        /// </list>
        ///
        /// 終わったあとは、抱えていた Scene が<b>消えて参照も片付く</b>ことまで見る。
        /// 残ると、在留枠を食ったまま次のスライドが上限で断られる。
        /// </summary>
        [UnityTest]
        public IEnumerator EvenWhileHoldingAFailedRelease_TheSingleLoadStillGoesThrough()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            var host = new UnloadRefusingSceneHost();
            transitions.SlideSceneHost = host;

            var loader = new CountingSceneLoader(transitions.Loader);
            transitions.Loader = loader;

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            AreaPreloader preloader = transitions.Slide.Preloader;
            Assert.AreEqual(1, transitions.Slide.RetainedCount, "前提：A を預けている。");

            preloader.ClearRequest();

            float failed = Time.realtimeSinceStartup + 10f;
            while (preloader.Phase != AreaPreloadPhase.ReleaseFailed
                   && Time.realtimeSinceStartup < failed)
            {
                yield return null;
            }

            Assert.AreEqual(AreaPreloadPhase.ReleaseFailed, preloader.Phase,
                "前提：解放に失敗して抱えている。");
            Assert.IsTrue(transitions.Slide.HasUnreleasedScene, "前提：抱え込みがある。");
            Assert.AreEqual(0, loader.LoadCount, "前提：Single 読込はまだ発行されていない。");

            // 判定に使う handle を控える。Scene の枚数では言えない——
            // Single のあとも §5 の距離による先読みがすぐ隣を持つので、落ち着いた先はまた 2 枚になる。
            int retainedSceneHandle = preloader.StagedSceneHandle;
            AreaInstanceHandle retainedInstance = preloader.StagedArea;
            Assert.AreNotEqual(0, retainedSceneHandle, "前提：預かっている実 Scene がある。");
            Assert.IsTrue(host.IsLoaded(retainedSceneHandle), "前提：その Scene は載っている。");

            int overRemainingBefore = transitions.Slide.SingleLoadOverRemainingSceneCount;
            int unloadsBefore = host.UnloadCount;

            // <b>撤去の拒否は解かない。</b> 抱えたまま Single へ進む経路だけを通す。
            Assert.IsTrue(host.RefuseUnload, "前提：撤去はこのあとも断り続ける。");

            Assert.IsTrue(transitions.TryTravel(AreaA, AreaAFromBEntry).Accepted,
                "Fade の要求は受理される。");

            // 発行の瞬間（＝発行される前の最後のフレーム）の状態を控える。
            bool stillLoadedAtIssue = false;
            bool liveOperationAtIssue = true;
            string phaseAtIssue = string.Empty;

            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.CompletedCount == 0 && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                if (loader.LoadCount == 0)
                {
                    stillLoadedAtIssue = host.IsLoaded(retainedSceneHandle);
                    liveOperationAtIssue = transitions.Slide.HasLiveSceneOperation;
                    phaseAtIssue = preloader.Phase.ToString();
                }

                yield return null;
            }

            Assert.IsFalse(transitions.HasTerminalFailure,
                "抱えていたからといって終端失敗にしない。理由=" + transitions.TerminalFailureReason);

            // ---- 発行の瞬間 ----
            Assert.IsTrue(stillLoadedAtIssue,
                "Single を発行する時点で、控えた旧 Scene はまだ載っていた（先読み=" + phaseAtIssue + "）。"
                + " ここが false なら、解放が成功してから発行した経路を見てしまっている。");
            Assert.IsFalse(liveOperationAtIssue,
                "発行の時点で、終端していない Scene 操作は無かった（先読み=" + phaseAtIssue + "）。"
                + " 止めるのは走っている操作だけである（付録 C.20）。");
            Assert.AreEqual(overRemainingBefore + 1, transitions.Slide.SingleLoadOverRemainingSceneCount,
                "実装自身が「載ったままの Scene を抱えて Single へ進んだ」と数えている。");
            Assert.AreEqual(1, loader.LoadCount, "Single は一度だけ発行された。");
            Assert.AreEqual(1, transitions.CompletedCount, "その発行が着いた。");
            Assert.AreEqual(unloadsBefore + 1, host.UnloadCount,
                "撤去を頼んだのは、Single の前の再試行 1 回ぶんだけ（それも断られている）。");

            yield return null;

            // ---- 置き換わったあと ----
            Assert.IsFalse(host.IsLoaded(retainedSceneHandle),
                "抱えていた旧 Scene は Single が置き換えて消えた（控えた handle で見ている）。"
                + DumpWorld(transitions));
            Assert.AreEqual(AreaActivationPhase.Unloaded,
                transitions.Slide.Residency.PhaseOf(retainedInstance),
                "旧実体の台帳登録も消えた。" + DumpResidency(transitions));
            Assert.AreNotEqual(retainedInstance, preloader.StagedArea,
                "保持参照もその実体を指していない。");
            Assert.AreNotEqual(retainedSceneHandle, preloader.StagedSceneHandle,
                "預かっている Scene も別物である。");
            Assert.IsFalse(transitions.Slide.HasUnreleasedScene,
                "置き換えたので抱え込みが解けた（在留枠を食ったまま残さない）。");
            Assert.LessOrEqual(transitions.Slide.Residency.ResidentCount, 2,
                "台帳も上限の中。" + DumpResidency(transitions));
        }

        // ------------------------------------------------- 接続口の見えない境界（工程 P55-14b。§7.3）

        /// <summary>
        /// <b>出口判定が成立しなくても、Area の外へは出られない</b>
        /// （§7.3。工程 P55-14b。試遊報告③。GPT 受入 3 の条件 1・3）。
        ///
        /// <b>試遊で出た壊れ方。</b> 外周壁は接続口の区間を空けて 2 本に分けてある——
        /// スライド中は両 Area が描かれるので、境界に<b>見える</b>壁が立つと画面を覆う。
        /// ところがそれで<b>当たりまで無くした</b>ので、出口判定（範囲内で出口方向へ 0.15 秒連続入力）が
        /// 成立しないまま通り抜けると主人公が Area の外へ出られた。
        /// カメラは追従範囲に収まっているので、<b>主人公は画面から消えたまま進む</b>。
        ///
        /// <b>出口判定を止めて、境界だけを見る。</b> 出入口の部品を取り除いてから境界へ押し込む——
        /// そうしないと「遷移したから外に出ていない」と区別が付かない。
        /// 斜めからも押す（角をかすめて回り込めないこと。条件 1）。
        /// </summary>
        [UnityTest]
        public IEnumerator WithoutTheExitGate_PushingIntoTheSeamNeverLeavesTheArea()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            AreaExitGate gate = FindExitGate(ExitAEast);
            Assert.IsNotNull(gate, "前提：出入口がある。");
            Vector3 seamAt = gate.transform.position;

            // <b>境界が居ることを先に確かめる。</b> 無ければこの検査は何も守っていない。
            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle bundle), "A の束を引ける。");
            Assert.IsTrue(bundle.TryResolve(out AreaRoot areaRoot), "A の根を引ける。");
            Assert.AreEqual(1, areaRoot.SeamBarriers.Count, "接続口の境界が 1 枚ある。");

            AreaSeamBarrier barrier = areaRoot.SeamBarriers[0];
            Assert.IsTrue(barrier.IsWired, "境界が配線されている（Collider と接続 ID）。");
            Assert.IsTrue(barrier.IsInvisible,
                "<b>見た目を持っていない</b>（持たせると、壁を消した理由へ逆戻りする）。");
            Assert.IsTrue(barrier.Blocker.enabled, "活動中なので当たりが有効。");

            float barrierX = barrier.Blocker.bounds.max.x;

            // <b>先に手前へ立たせる。</b> 出入口を取り除くと立ち位置の基準が無くなる。
            yield return StandJustBefore(gate, Vector3.left);
            yield return SettleCamera();

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");
            Assert.Less(Vector3.Distance(player.transform.position, seamAt), 4f,
                "前提：出入口の手前に立てている。");

            // ---- 出口判定を止める ----
            Object.DestroyImmediate(gate);
            yield return null;

            // <c>FindExitGate</c> は見つからないと自分で失敗するので、ここでは使わない。
            AreaExitGate[] remaining = Object.FindObjectsByType<AreaExitGate>(FindObjectsSortMode.None);
            for (int i = 0; i < remaining.Length; i++)
            {
                Assert.AreNotEqual(ExitAEast.Value, remaining[i].ExitId.Value,
                    "前提：この出入口はもう無い（遷移は起こりえない）。");
            }

            float worstX = player.transform.position.x;

            // まっすぐ・斜め上・斜め下の 3 通りで押す（角から回り込めないこと）。
            Key[][ ] pushes =
            {
                new[] { Key.D },
                new[] { Key.D, Key.W },
                new[] { Key.D, Key.S },
            };

            for (int p = 0; p < pushes.Length; p++)
            {
                for (int i = 0; i < 40; i++)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(pushes[p]));
                    yield return null;
                    worstX = Mathf.Max(worstX, player.transform.position.x);
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;
            }

            Assert.AreEqual(0, transitions.SlideCommittedCount, "前提：遷移は起きていない。");
            Assert.Less(worstX, barrierX + 0.01f,
                "<b>境界より外へ出ていない</b>（最も進んだ x=" + worstX + " / 境界の外面 x=" + barrierX
                + "）。斜めからも回り込めない。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に居たまま。");
        }

        /// <summary>
        /// <b>境界に止められた位置でも、出口判定は続いて遷移が成立する</b>
        /// （GPT 受入 3 の条件 2。工程 P55-14b）。
        ///
        /// 境界を置いたことで「押し当てているのに出入口の範囲から外れて遷移できない」になっては、
        /// 通れない接続を作ったのと同じである。**止まった位置が範囲の内側**でなければならない。
        ///
        /// <b>止められたことも確かめる。</b> 止まらずに通り抜けていたら、
        /// 「範囲内だった」は境界のおかげではない。
        /// </summary>
        [UnityTest]
        public IEnumerator StoppedByTheBarrier_TheExitGateStillAccepts()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            AreaExitGate gate = FindExitGate(ExitAEast);
            Assert.IsNotNull(gate, "前提：出入口がある。");

            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle bundle), "A の束を引ける。");
            Assert.IsTrue(bundle.TryResolve(out AreaRoot areaRoot), "A の根を引ける。");
            AreaSeamBarrier barrier = areaRoot.SeamBarriers[0];
            float barrierX = barrier.Blocker.bounds.min.x;

            var player = Object.FindFirstObjectByType<PlayerRoot>();

            yield return StandJustBefore(gate, Vector3.left);
            yield return SettleCamera();

            bool sawInsideWhileHeld = false;
            bool sawStopped = false;
            float previousX = player.transform.position.x;
            float held = 0f;

            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;

                if (gate != null)
                {
                    held = Mathf.Max(held, gate.HeldSeconds);
                    if (gate.PlayerInside && gate.HeldSeconds > 0f)
                    {
                        sawInsideWhileHeld = true;
                    }
                }

                float x = player.transform.position.x;
                // 境界へ触れる距離まで来て、なお前進が止まっている＝押し当てている。
                if (x > barrierX - 1.2f && Mathf.Abs(x - previousX) < 0.002f)
                {
                    sawStopped = true;
                }

                previousX = x;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "境界に押し当てたまま遷移が成立した。失敗=" + transitions.Slide.LastFailure);
            Assert.IsTrue(sawInsideWhileHeld,
                "押しているあいだ、出入口の範囲の内側に居た（止まった位置が範囲内）。"
                + " 最大の連続入力=" + held + " 秒 / 必要=" + AreaExitGate.RequiredHoldSeconds + " 秒");
            Assert.IsTrue(sawStopped,
                "境界の手前で前進が止まった（通り抜けていない）。止まらないなら、"
                + "「範囲内だった」は境界のおかげではない。");
        }

        /// <summary>
        /// <b>到着位置は行き先の境界より内側で、境界と重なっていない</b>
        /// （GPT 受入 3 の条件 4。工程 P55-14b）。
        ///
        /// 重なって到着すると、物理が主人公を押し出す——押し出された先が境界の外側なら、
        /// **到着した瞬間に Area の外へ出る**。犬丸も同じ（入口から進行方向と逆へ 1.2m に置かれる）。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterArriving_TheHeroAndCompanionAreInsideTheDestinationBarrier()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に着いた。");

            Assert.IsTrue(TryFindBundle(AreaB, out AreaRuntimeBundle bundle), "B の束を引ける。");
            Assert.IsTrue(bundle.TryResolve(out AreaRoot areaRoot), "B の根を引ける。");
            Assert.AreEqual(1, areaRoot.SeamBarriers.Count, "B にも境界が 1 枚ある。");

            Bounds blocker = areaRoot.SeamBarriers[0].Blocker.bounds;

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");
            Vector3 at = player.transform.position;

            Assert.IsFalse(blocker.Contains(new Vector3(at.x, blocker.center.y, at.z)),
                "主人公が境界と重なっていない（位置=" + at + " 境界=" + blocker + "）。");
            Assert.Greater(at.x, blocker.max.x,
                "主人公は境界より<b>内側</b>に居る（x=" + at.x + " 境界の内面 x=" + blocker.max.x + "）。");

            var companion = Object.FindFirstObjectByType<CompanionActor>();
            if (companion != null && companion.State != CompanionState.Away)
            {
                Vector3 dog = companion.transform.position;
                Assert.IsFalse(blocker.Contains(new Vector3(dog.x, blocker.center.y, dog.z)),
                    "犬丸も境界と重なっていない（位置=" + dog + "）。");
                Assert.Greater(dog.x, blocker.max.x,
                    "犬丸も境界より内側（x=" + dog.x + "）。");
            }
        }

        /// <summary>
        /// <b>表示経路検査から外すのは、いま使う接続の境界だけ</b>
        /// （GPT 受入 3。工程 P55-14b）。
        ///
        /// 境界を普通の壁として数えると、主人公の表示経路は<b>必ず塞がっている</b>ことになり、
        /// 接続そのものが「表示経路を安全に作れません」で失敗する。
        /// 逆に一括で外すと、通常の壁や別の接続の境界まで見えなくなる。
        ///
        /// 外した枚数を数で見る（出発側と到着側の 2 枚）。
        /// </summary>
        [UnityTest]
        public IEnumerator TheDisplayRoute_IgnoresExactlyTheBarriersOfThisConnection()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            Assert.AreEqual(0, transitions.Slide.SeamBarriersIgnoredForRoute, "前提：まだ数えていない。");

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドが成立した（境界を普通の壁として数えていたら準備失敗になる）。失敗="
                + transitions.Slide.LastFailure);
            Assert.AreEqual(0, transitions.Slide.RolledBackCount, "戻していない。");
            Assert.AreEqual(2, transitions.Slide.SeamBarriersIgnoredForRoute,
                "外したのは出発側と到着側の境界 2 枚だけ（"
                + transitions.Slide.SeamBarriersIgnoredForRoute + " 枚）。");
        }

        // ================================================ 出入口の範囲内の残留（工程 P55-15a。試遊報告①）

        /// <summary>
        /// <b>去った出入口が「まだ範囲内に居る」と思い込んだままにならない</b>
        /// （工程 P55-15a。試遊報告①「エリア B の戦闘区域を歩き回っていると、突然
        /// エリア A の『B へ』の位置まで強制的に移動させられる」）。
        ///
        /// <b>原因は 2 つの事実の組み合わせだった。</b>
        /// <list type="number">
        /// <item><c>OnTriggerEnter</c>／<c>OnTriggerExit</c> は<b>その MonoBehaviour が有効な間</b>
        /// しか届かない。<see cref="AreaActivityGate"/> は非活動 Area の出入口を止めるので、
        /// 止まっている間に主人公が範囲から出ても<b>退出が一度も届かない</b>。</item>
        /// <item><b>到着入口は出入口の Trigger の外にある</b>——東西配置では
        /// 出入口が x=−13.4（奥行 1.6 なので −14.2〜−12.6）、到着入口が x=−11.5。
        /// だから再入場でも入退出が起きず、<b>去ったときの true が固まって残る</b>。</item>
        /// </list>
        ///
        /// 残ると、出口方向へ 0.15 秒入力するだけで<b>主人公がどこに居ても遷移が要求される</b>。
        ///
        /// <b>「false になっている」だけでは受入にならない。</b> 直す前から false の配置なら
        /// 何も守っていないので、<b>固まった true を実際に落としたこと</b>
        /// （<c>StaleOccupancyClearedCount</c>）も見る——残留が起きていた証拠である。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterLeavingAndComingBack_TheExitGateNoLongerThinksTheHeroIsInside()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // ---- A → B（B の出入口はまだ使っていない）----
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");

            // ---- B → A（ここで B の出入口が「範囲内」になる）----
            AreaExitGate backToA = FindExitGate(ExitBWest);
            yield return StandJustBefore(backToA, Vector3.right);
            Assert.IsTrue(backToA.PlayerInside, "前提：B の出入口の範囲内に居る。");

            yield return SettleCamera();
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に戻った。");

            // ---- A → B（もう一度。ここで残留が表に出る）----
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 2, 25f);
            yield return SettleWorld(transitions);
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "また B に居る。");

            AreaExitGate gate = FindExitGate(ExitBWest);
            Assert.Greater(gate.ResyncCount, 0,
                "入場のたびに範囲内を測り直している（" + gate.ResyncCount + " 回）。");
            Assert.Greater(gate.StaleOccupancyClearedCount, 0,
                "<b>固まっていた『範囲内』を実際に落とした</b>（" + gate.StaleOccupancyClearedCount
                + " 回）。0 なら残留が起きていないので、この検査は何も守っていない。");

            // <b>到着位置は Trigger の外である</b>ことを数で残す（これが残留の前提）。
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");
            Collider trigger = gate.GetComponent<Collider>();
            Assert.IsNotNull(trigger, "出入口は Trigger を持つ。");
            Assert.IsFalse(gate.PlayerInside,
                "<b>到着した主人公は B の出入口の範囲内に居ない</b>（主人公 x="
                + player.transform.position.x + " / Trigger x "
                + trigger.bounds.min.x + "〜" + trigger.bounds.max.x
                + "）。居ないのに true が残っていたのが試遊報告①である。");
        }

        /// <summary>
        /// <b>境界から離れた場所で出口方向を押し続けても、遷移しない</b>
        /// （工程 P55-15a。試遊報告①の振る舞いそのもの）。
        ///
        /// 上の検査が<b>状態</b>を見るのに対し、こちらは<b>結果</b>を見る——
        /// 直したつもりでも別の道で要求が飛べば、プレイヤーには同じ現象として出る。
        /// </summary>
        [UnityTest]
        public IEnumerator FarFromTheSeam_HoldingTheExitDirection_DoesNotTravel()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // A → B → A → B（残留が作られる往復を通す）。
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 2, 25f);
            yield return SettleWorld(transitions);
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");

            int committedBefore = transitions.SlideCommittedCount;
            AreaExitGate gate = FindExitGate(ExitBWest);

            // <b>要求数は 0 ではない。</b> この出入口は往路で一度<b>正しく</b>要求している。
            // 見たいのは「ここから先で増えないこと」なので、控えてから比べる。
            int requestsBefore = gate.RequestCount;

            // <b>戦闘区域のあたりへ移す。</b> 出入口からは十分に離れている。
            // ここは「そこへ歩けるか」の検査ではないので、座標で置いてよい
            // （歩いて到達できることは付録 C.39 が別に見ている）。
            yield return PlaceHero(Phase55ArenaProbe());

            // <b>「範囲内でない」は前提に置かない。</b> 前提で落とすと、
            // 残留しているときに<b>実際に連れて行かれること</b>を見ないまま終わる——
            // プレイヤーが報告したのは状態ではなく<b>飛ばされたこと</b>である。
            // 状態は下の最後で見る。

            // ---- 出口方向（西）を押し続ける ----
            //
            // <b>一度別の向きを押してから</b>にする。到着直後は「入力が一度切れるまで」
            // 要求しない仕組み（§6.1 末尾）が効いているので、
            // それだけで通ってしまう検査にしない。
            for (int i = 0; i < 10; i++)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;
            }

            for (int i = 0; i < 120; i++)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.A));
                yield return null;

                Assert.AreEqual(committedBefore, transitions.SlideCommittedCount,
                    "<b>境界から離れた場所で西を押しても遷移しない</b>（frame " + i
                    + " 要求数=" + gate.RequestCount + " 範囲内=" + gate.PlayerInside
                    + " 溜め=" + gate.HeldSeconds + "）。試遊報告①の現象である。");
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.AreEqual(requestsBefore, gate.RequestCount,
                "境界から離れている間、出入口は一度も要求していない（"
                + gate.RequestCount + " / 往路の正しい要求 " + requestsBefore + " 回）。");
            Assert.IsFalse(gate.PlayerInside,
                "出入口は「範囲内」と思っていない（主人公は戦闘区域のあたりに居る）。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value,
                "B から動いていない。");
        }

        /// <summary>
        /// <b>活動していない Area の出入口は、共有の移動入力を数えない</b>
        /// （工程 P55-15a）。
        ///
        /// 先読みと引き継ぎで<b>隣の Area も載っている</b>のが常態である（§5／§6.2 手順 11）。
        /// その Area の <c>AreaExitGateDriver</c> は <c>Update</c> で回り続けるので、
        /// 出入口が入力を数えると<b>隣の Area の出入口が遷移を要求できる</b>。
        ///
        /// 止めているのは <see cref="AreaActivityGate"/> が出入口を <c>enabled = false</c> に
        /// することだが、<c>Tick</c> は Unity の呼び出しではないので<b>止まっていても呼べる</b>——
        /// そこを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator WhileTheAreaIsNotCurrent_ItsExitGatesDoNotCountInput()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            AreaExitGate gateInA = FindExitGate(ExitAEast);
            yield return StandJustBefore(gateInA, Vector3.left);
            Assert.IsTrue(gateInA.PlayerInside, "前提：A の出入口の範囲内に居る。");

            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");

            // A はまだ載っている（引き継ぎ。§6.2 手順 11）。その出入口を直接叩く。
            Assert.IsTrue(gateInA != null, "A の出入口の参照が生きている（Scene が載っている）。");
            Assert.IsFalse(gateInA.enabled,
                "A は活動していないので出入口は止まっている（活動ゲートが止める）。");

            int skippedBefore = gateInA.SkippedWhileDisabledCount;
            // 往路で一度<b>正しく</b>要求しているので、増えないことを見る。
            int requestsBefore = gateInA.RequestCount;
            bool requested = false;
            for (int i = 0; i < 20; i++)
            {
                // 出口方向（東）を、十分な時間ぶん入れる。
                requested |= gateInA.Tick(0.05f, Vector3.right);
            }

            Assert.IsFalse(requested,
                "<b>止まっている出入口は要求しない</b>（溜め=" + gateInA.HeldSeconds
                + " 範囲内=" + gateInA.PlayerInside + "）。"
                + " 要求すると、隣の Area の出入口が主人公を連れて行く。");
            Assert.AreEqual(requestsBefore, gateInA.RequestCount,
                "要求数も増えない（" + gateInA.RequestCount + " / 往路の正しい要求 "
                + requestsBefore + " 回）。");
            Assert.Greater(gateInA.SkippedWhileDisabledCount, skippedBefore,
                "止まっていたことを数えている（" + gateInA.SkippedWhileDisabledCount + "）。");
        }

        /// <summary>
        /// 主人公をそこへ置く（<b>到達可能性は主張しない</b>——
        /// 歩いて行けることは付録 C.39 が別に見ている。ここは出入口の状態の検査である）。
        /// </summary>
        private static IEnumerator PlaceHero(Vector3 spot)
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

        /// <summary>戦闘区域のあたり（出入口から十分に離れた場所）。</summary>
        private static Vector3 Phase55ArenaProbe()
        {
            var root = CurrentAreaProvider.Current;
            Assert.IsNotNull(root, "活動中 Area がある。");
            var encounter = Object.FindFirstObjectByType<
                Momotaro.Gameplay.Encounter.AreaEncounterTrigger>();
            if (encounter != null)
            {
                return encounter.transform.position;
            }

            // 遭遇 Trigger が無い構成でも、Area の中心なら出入口から離れている。
            return root.Root != null ? root.Root.transform.position : Vector3.zero;
        }

        /// <summary>
        /// <b>Trigger の中へ到着しても、占有を正しく判定する</b>
        /// （工程 P55-15b。GPT 指摘 2）。
        ///
        /// <b>ここが危ない場所だった。</b> 測り直しが走るのは<b>入場準備の最中</b>で、
        /// そのとき活動ゲートはまだ開いていない——出入口の Collider は<b>無効</b>である。
        /// <c>Collider.bounds</c> は無効な Collider では信頼できないので、
        /// そこを鵜呑みにすると「いつも範囲外」になり、
        /// <b>Trigger の中へ到着する配置では出られなくなる</b>。
        ///
        /// いまの配置では到着入口が Trigger の外にあるので、この経路は自然には通らない。
        /// だから<b>主人公を Trigger の中へ置いて</b>、
        /// 活動ゲートを閉じた状態で測り直しを走らせる。
        ///
        /// <b>「外なら false」だけでは受入にならない。</b> 一律 false にする実装でも通ってしまい、
        /// 「出られない」を防いでいることを言えなくなる。
        /// </summary>
        [UnityTest]
        public IEnumerator ArrivingInsideTheGate_TheOccupancyIsTrueEvenWhileTheAreaIsClosed()
        {
            yield return EnterArea(P55AreaAScene);

            AreaExitGate gate = FindExitGate(ExitAEast);
            Collider trigger = gate.GetComponent<Collider>();
            Assert.IsNotNull(trigger, "出入口は Trigger を持つ。");

            var activity = Object.FindFirstObjectByType<AreaActivityGate>();
            Assert.IsNotNull(activity, "活動ゲートがある。");

            // ---- (1) Trigger の外：false になる ----
            yield return PlaceHero(gate.transform.position + new Vector3(-6f, 0f, 0f));
            gate.ResyncOccupancy();
            Assert.IsFalse(gate.PlayerInside,
                "Trigger から離れていれば範囲外（主人公 "
                + Object.FindFirstObjectByType<PlayerRoot>().transform.position + "）。");

            // ---- (2) Trigger の中：true になる ----
            yield return PlaceHero(gate.transform.position);
            gate.ResyncOccupancy();
            Assert.IsTrue(gate.PlayerInside,
                "<b>Trigger の中なら範囲内</b>（主人公 "
                + Object.FindFirstObjectByType<PlayerRoot>().transform.position
                + " / Trigger 中心 " + gate.transform.position + "）。"
                + " 一律 false にする実装ならここで落ちる。");

            // ---- (3) 活動ゲートを閉じた状態（＝入場準備の最中）でも同じ ----
            //
            // 実際の入場準備はこの状態で走る。Collider が無効なので、
            // 形から測っていない実装はここで false になる。
            activity.Close();
            yield return null;
            Assert.IsFalse(trigger.enabled,
                "前提：活動ゲートが出入口の Collider を止めている。");

            gate.ResyncOccupancy();
            Assert.IsTrue(gate.PlayerInside,
                "<b>止められている Collider でも、形から重なりを測れている</b>（Trigger 中心 "
                + gate.transform.position + "）。"
                + " Collider.bounds に頼る実装はここで落ちる——"
                + "入場準備は必ずこの状態で走るので、Trigger の中へ到着する配置で出られなくなる。");

            activity.Open();
            yield return null;
        }

        /// <summary>
        /// <b>未クリアの遭遇戦は、往復して戻っても始められる</b>
        /// （工程 P55-15b。GPT 指摘 1）。
        ///
        /// <c>AreaEncounterTrigger</c> も「範囲内」を持つ。true で固まると
        /// <c>OnTriggerEnter</c> が即 return するので、<b>遭遇戦が二度と始まらない</b>。
        ///
        /// <b>工程 P55-15a では、測り直しのメソッドを足したのに入場処理から呼んでいなかった</b>
        /// （記録 038 §1「API があることと、繋がっていることは別」）。
        /// この検査はその配線を見る——<c>ResyncCount</c> が増えていることと、
        /// <b>実際に戦闘が始まること</b>の両方で。
        /// </summary>
        [UnityTest]
        public IEnumerator AnUnclearedEncounter_CanStillStartAfterComingBack()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            // ---- A → B（戦わずに戻る）----
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            var encounter = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(encounter, "B に遭遇戦がある。");
            Assert.AreEqual(AreaEncounterState.Dormant, encounter.State,
                "前提：まだ始まっていない（戦っていない）。");

            var trigger = Object.FindFirstObjectByType<AreaEncounterTrigger>();
            Assert.IsNotNull(trigger, "遭遇 Trigger がある。");
            Assert.IsTrue(trigger.IsWired, "主人公が配線されている。");

            // ---- B → A → B（往復する）----
            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 2, 25f);
            yield return SettleWorld(transitions);
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "B に居る。");

            AreaEncounterTrigger back = Object.FindFirstObjectByType<AreaEncounterTrigger>();
            Assert.IsNotNull(back, "戻った B に遭遇 Trigger がある。");
            Assert.Greater(back.ResyncCount, 0,
                "<b>入場のたびに測り直している</b>（" + back.ResyncCount
                + " 回）。0 なら入場処理から呼ばれていない——工程 P55-15a の繋ぎ忘れである。");
            Assert.IsFalse(back.PlayerInside,
                "到着した主人公は戦闘区域の中に居ない（入口に居る）。");

            AreaEncounterRunner runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runner, "戻った B に遭遇戦がある。");
            Assert.AreEqual(AreaEncounterState.Dormant, runner.State,
                "まだ始まっていない（記録は未クリアのまま）。");

            // ---- 戦闘区域へ入る：始まる ----
            yield return PlaceHero(back.transform.position);

            float deadline = Time.realtimeSinceStartup + 6f;
            while (runner.State == AreaEncounterState.Dormant
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreNotEqual(AreaEncounterState.Dormant, runner.State,
                "<b>往復して戻っても、戦闘区域へ入れば遭遇戦が始まる</b>（状態 " + runner.State
                + " 要求数 " + back.RequestCount + " 範囲内 " + back.PlayerInside
                + "）。始まらないなら「範囲内」が true で固まっている。");
        }

        // ------------------------------------------------- 到着後の追従（工程 P55-14d。裁定の注意点 3）

        /// <summary>
        /// <b>スライドの終点と、到着後の通常追従の位置が一致する</b>
        /// （裁定の注意点 3。工程 P55-14d）。
        ///
        /// <b>到着直後にもう一度寄り直したり、横へ跳ねたりしない。</b>
        /// 終点は <c>TryComputeArrivalPoint</c> が、追従は <c>TryComputeFocus</c> が求めるが、
        /// どちらも<b>同じ純粋関数</b>（領域選択 ＋ <c>ClampFocus</c>）を通る。
        /// 領域が 1 つになったので、<b>到着位置で選ばれる領域と、その後に選ばれる領域が必ず同じ</b>になる——
        /// 以前は到着位置が継ぎ目領域で、動き出すと別の領域へ移るので寄り直しが起きえた。
        ///
        /// <b>入力を入れずに待つ。</b> 動かしてしまうと、寄り直しと通常追従の区別が付かない。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterTheSlide_TheCameraDoesNotReSnapOrJump()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");

            int changesBefore = host.Rig.Blend.RegionChangeCount;

            // <b>測るのは Rig の位置である</b>（＝カメラが見ている床の上の点）。
            //
            // 間違えた測り方を 2 つ通った。<c>Blend.Current</c> はスライド中に据え置かれる——
            // スライド中は通常追従を止めて<b>Rig を直接動かす</b>ので、追従側の値は出発位置のまま残り、
            // 到着時に追いつくのを「3.15 m の跳び」と読んでしまう（診断：到着時 Blend=(11.35,0,0)／
            // 終点=(14.50,0,0)／落ち着き=(14.50,0,0)）。
            // <c>Camera.transform.position</c> は<b>俯角ぶん後ろの空中</b>にあるので、
            // 追従が求める床の上の点（(14.50, 0, 0)）とは 9.8 m ずれる——比べる相手が違う。
            // <b>Rig の位置だけが、スライドでも追従でも同じ空間の同じ量である。</b>

            Vector3 atArrival = Vector3.zero;
            bool captured = false;
            void OnArrived(StableId areaId)
            {
                atArrival = RigPosition();
                captured = true;
            }

            transitions.ArrivalCompleted += OnArrived;
            try
            {
                yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
                yield return SettleCamera();
                yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);

                // <b>入力を切る。</b> 押しっぱなしのままだと動いてしまう。
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;

                Assert.IsTrue(captured, "到着の瞬間のカメラ位置を採れた。");

                // ---- 到着直後の 30 フレームを、入力なしで見る ----
                float worstJump = 0f;
                Vector3 previous = atArrival;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    Vector3 now = RigPosition();
                    float step = Vector2.Distance(
                        new Vector2(now.x, now.z), new Vector2(previous.x, previous.z));
                    worstJump = Mathf.Max(worstJump, step);
                    previous = now;
                }

                Vector3 settled = RigPosition();

                Assert.AreEqual(changesBefore, host.Rig.Blend.RegionChangeCount,
                    "<b>領域の切替が起きていない</b>（" + host.Rig.Blend.RegionChangeCount
                    + " / 開始時 " + changesBefore + "）。起きると 0.15 秒の寄り直しが走る。");
                Assert.IsFalse(host.Rig.Blend.IsBlending, "補間も走っていない。");
                Assert.Less(worstJump, 0.02f,
                    "<b>1 フレームも跳ねていない</b>（最大の動き " + worstJump + " m）。"
                    + " 入力を入れていないので、動いたなら寄り直しである。");
                Assert.Less(
                    Vector2.Distance(new Vector2(atArrival.x, atArrival.z),
                        new Vector2(settled.x, settled.z)),
                    0.02f,
                    "<b>到着の瞬間と落ち着いた先が同じ</b>（到着 " + atArrival
                    + " → 落ち着き " + settled + "）。ずれるなら終点と追従位置が一致していない。");

                // <b>スライドの終点と、到着後の通常追従の位置が一致する</b>（裁定 4）。
                // 終点だけ合わせて追従が別を向いていると、次に動いた瞬間に横へ跳ぶ。
                Assert.IsTrue(host.Rig.TryComputeFocus(out Vector3 wanted), "追従位置を求められる。");
                Assert.Less(
                    Vector2.Distance(new Vector2(settled.x, settled.z),
                        new Vector2(wanted.x, wanted.z)),
                    0.05f,
                    "通常追従が求める位置と一致している（いま " + settled + " 追従 " + wanted + "）。");

                Vector3 slideEnd = transitions.Slide.LastSlideTo;
                Assert.Less(
                    Vector2.Distance(new Vector2(slideEnd.x, slideEnd.z),
                        new Vector2(wanted.x, wanted.z)),
                    0.05f,
                    "<b>スライドの終点＝到着後の通常追従位置</b>（終点 " + slideEnd
                    + " 追従 " + wanted + "）。ずれるなら到着後に寄り直しが要る配置である（裁定 4）。");
            }
            finally
            {
                transitions.ArrivalCompleted -= OnArrived;
            }
        }

        /// <summary>
        /// <b>往復しても、スライド・到着後の追従・犬丸の表示がつながる</b>
        /// （裁定の注意点 3 と受入表の最後の行。工程 P55-14d）。
        ///
        /// 行きと帰りで同じことを見る——片方だけ整えても往復では崩れる。
        /// 犬丸は<b>到着後に見えている</b>こと（退場中でなければ）を見る：
        /// スライド中は表示代理が運ぶので、実体の Renderer を戻し忘れると消えたままになる。
        /// </summary>
        [UnityTest]
        public IEnumerator TheRoundTrip_KeepsTheCameraAndTheCompanionContinuous()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            AreaCameraRigHost host = AreaCameraRigHost.Instance;

            for (int leg = 1; leg <= 2; leg++)
            {
                bool forward = leg == 1;
                StableId exitId = forward ? ExitAEast : ExitBWest;
                Key key = forward ? Key.D : Key.A;
                int changesBefore = host.Rig.Blend.RegionChangeCount;

                yield return StandJustBefore(FindExitGate(exitId), forward ? Vector3.left : Vector3.right);
                yield return SettleCamera();
                yield return HoldUntil(key, () => transitions.SlideCommittedCount >= leg, 25f);

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;
                yield return SettleWorld(transitions);

                string label = forward ? "往路" : "復路";
                Assert.AreEqual(leg, transitions.SlideCommittedCount,
                    label + "：着いた。失敗=" + transitions.Slide.LastFailure);
                Assert.AreEqual(changesBefore, host.Rig.Blend.RegionChangeCount,
                    label + "：領域の切替が起きていない（" + host.Rig.Blend.RegionChangeCount + "）。");
                Assert.IsFalse(host.Rig.Blend.IsBlending, label + "：補間も走っていない。");

                // 犬丸が見えている（退場中でなければ）。
                var companion = Object.FindFirstObjectByType<CompanionActor>();
                if (companion != null && companion.State != CompanionState.Away)
                {
                    bool visible = false;
                    foreach (Renderer r in companion.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r != null && r.enabled && r.gameObject.activeInHierarchy)
                        {
                            visible = true;
                            break;
                        }
                    }

                    Assert.IsTrue(visible,
                        label + "：犬丸が見えている（状態=" + companion.State
                        + "）。代理から実体へ戻し忘れると消えたままになる。");
                }

                // 追従が求める位置と一致している。
                Assert.IsTrue(host.Rig.TryComputeFocus(out Vector3 wanted), label + "：追従位置を求められる。");
                Vector3 now = host.Rig.Blend.Current;
                Assert.Less(
                    Vector2.Distance(new Vector2(now.x, now.z), new Vector2(wanted.x, wanted.z)),
                    0.05f, label + "：いまの基準位置が通常追従と一致（" + now + " / " + wanted + "）。");
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
            AreaTransitionWaitNoticeProvider.ClearForTests();
            AppFocusProvider.ClearForTests();
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
            // <b>この検査は撤去経路そのものを見る</b>（工程 P55-10c）。
            // 裁定 2 で旧 Area は既定で保持されるようになったので、保持を切って
            // 従来どおり毎回撤去させる——保持に隠れた経路は壊れても誰も気付かない。
            transitions.RetainDepartedArea = false;
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
            // <b>この検査は撤去経路そのものを見る</b>（工程 P55-10c）。
            // 裁定 2 で旧 Area は既定で保持されるようになったので、保持を切って
            // 従来どおり毎回撤去させる——保持に隠れた経路は壊れても誰も気付かない。
            transitions.RetainDepartedArea = false;

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
            // <b>この検査は撤去経路そのものを見る</b>（工程 P55-10c）。
            // 裁定 2 で旧 Area は既定で保持されるようになったので、保持を切って
            // 従来どおり毎回撤去させる——保持に隠れた経路は壊れても誰も気付かない。
            transitions.RetainDepartedArea = false;
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
            // <b>この検査は撤去経路そのものを見る</b>（工程 P55-10c）。
            // 裁定 2 で旧 Area は既定で保持されるようになったので、保持を切って
            // 従来どおり毎回撤去させる——保持に隠れた経路は壊れても誰も気付かない。
            transitions.RetainDepartedArea = false;
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
                .Append(" 読込要求=").Append(transitions.Slide.PreloadRequestCount)
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

        // ---------------------------------------------------------------- Move 周期（§7.2。P55-09b）

        /// <summary>
        /// <b>代理は Move の既存 6 コマを表示専用の時計で回す</b>（§7.2。工程 P55-09b）。
        ///
        /// コマ送りの仕掛けは工程 P55-03d-2 からあったが、<b>本番では誰も素材を渡していなかった</b>
        /// ——代理は写した 1 枚を出し続け、通路を渡る主人公が<b>滑って移動する</b>絵だった。
        ///
        /// ここは<b>実資産に対する検査</b>である。EditMode はテストが組んだクリップで
        /// 取り出しの規則を見るが、<b>本番のクリップから本当に 6 コマ出るか</b>は
        /// 実 Scene の Animator を通さないと言えない
        /// （コマ数の数え方は <c>clip.length</c> の解釈に依っていて、そこを間違えると
        /// 5 コマや 7 コマになる——実際に 7 コマ出た）。
        /// </summary>
        [UnityTest]
        public IEnumerator TheProxy_PlaysTheRealSixFrameMoveCycleWhileSliding()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            var seenFrames = new HashSet<int>();
            var seenSprites = new HashSet<string>();
            int frameCount = -1;
            string fallbackReason = null;

            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0
                   && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;

                AreaTransitionDisplayHost display = AreaTransitionDisplayHost.Instance;
                AreaTransitionDisplayProxy proxy = display != null && display.Set != null
                    ? display.Set.Player : null;
                if (proxy == null)
                {
                    continue;
                }

                frameCount = display.PlayerMoveFrameCount;
                fallbackReason = display.MoveFrameFallbackReason;
                seenFrames.Add(proxy.FrameIndex);
                if (proxy.Renderer != null && proxy.Renderer.sprite != null)
                {
                    seenSprites.Add(proxy.Renderer.sprite.name);
                }
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドで着いた。理由=" + transitions.Slide.LastFailure);

            // <b>本番のクリップから 6 コマ</b>（§7.2「既存 6 コマ周期」）。
            Assert.AreEqual(AreaTransitionDisplayProxy.MoveFrameCount, frameCount,
                "実資産の Move クリップから 6 コマ取り出せた。理由=" + fallbackReason);
            Assert.IsEmpty(fallbackReason, "取りこぼしていない。");

            // <b>回っていること</b>——止まった絵なら 1 種類しか見えない。
            Assert.Greater(seenFrames.Count, 1,
                "スライド中にコマが進んだ（見えたコマ番号=" + string.Join(",", seenFrames) + "）。");
            Assert.Greater(seenSprites.Count, 1,
                "絵も実際に替わった（見えた Sprite=" + string.Join(",", seenSprites) + "）。");

            // 代理は<b>描画部品しか持たない</b>ままである（コマを渡しても増やさない。§7.2）。
            AreaTransitionDisplayProxy[] proxies =
                Object.FindObjectsByType<AreaTransitionDisplayProxy>(FindObjectsSortMode.None);
            Assert.AreEqual(0, proxies.Length, "着いたので代理は畳まれている。");
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

        // -------------------------------------------- 読み込みは遷移の受理だけ（工程 P55-15c）

        /// <summary>
        /// <b>出入口へ近づいても隣は読まない。読むのは遷移が受理されたときだけ</b>
        /// （工程 P55-15c。読み込み方針の裁定）。
        ///
        /// <b>この検査は契約ごと書き換わった。</b> 以前は「近づいただけで隣が載る」を要求していた
        /// （§5 の距離による先読み。工程 P55-08b）。それは
        /// 「境界の向こうが虚空になる」を消すために入った仕組みだったが、
        /// その役目は<b>裁定 2 の引き継ぎ（付録 C.29）と背景の補完（付録 C.33）</b>が引き取り、
        /// 距離で読むことの代償——<b>歩いている最中に一瞬止まる</b>——だけが残っていた。
        ///
        /// 裁定（オーナー提案）は<b>待ち時間をプレイヤーが理解できる場所へまとめる</b>ことを選んだ：
        /// 接近ではロードせず、<b>初めてそのエリアへ遷移するときにロードし、以降保持する</b>。
        ///
        /// 見るのは 3 つ。<b>離れていても近づいても読まない</b>、
        /// <b>受理されて初めて読む</b>、<b>読んだあとは台帳と実 Scene が合っている</b>。
        /// </summary>
        [UnityTest]
        public IEnumerator ApproachingTheSeam_DoesNotLoadTheNeighbourUntilTheTravelIsAccepted()
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

            // ---- 離れていても読まない ----
            yield return PlacePlayerAt(gate.transform.position + (Vector3.left * 9f));
            yield return WaitFrames(30);

            Assert.AreEqual(1, SceneManager.sceneCount, "9 units 離れていれば隣は載らない。");
            Assert.AreEqual(0, host.Loaded.Count, "読込も発行していない。");

            // ---- <b>近づいても読まない</b>（ここが契約の変わった場所）----
            //
            // 以前はここで「要求より先に載る」ことを求めていた。
            // いまは<b>何も起きない</b>ことを、十分な数のフレームで確かめる——
            // 1 フレーム見るだけだと「まだ始まっていないだけ」と区別が付かない。
            yield return PlacePlayerAt(gate.transform.position + (Vector3.left * 4f));
            yield return WaitFrames(120);

            var probe = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.AreEqual(0, host.CountOf(P55AreaBScene),
                "<b>出入口へ近づいただけでは B を読まない</b>（工程 P55-15c）。"
                + " 主人公=" + probe.transform.position
                + " 出入口=" + gate.transform.position
                + " 距離=" + Vector3.Distance(
                    new Vector3(probe.transform.position.x, 0f, probe.transform.position.z),
                    new Vector3(gate.transform.position.x, 0f, gate.transform.position.z))
                + " 読んだ順=" + string.Join(",", host.Loaded)
                + " 頼んだ回数=" + transitions.Slide.PreloadRequestCount
                + " " + DumpWorld(transitions));
            Assert.AreEqual(1, SceneManager.sceneCount, "実 Scene は 1 枚のまま。");
            Assert.AreEqual(0, transitions.Slide.PreloadRequestCount,
                "読込を一度も頼んでいない。");
            Assert.AreEqual(AreaPreloadPhase.Idle, transitions.Slide.Preloader.Phase,
                "先読みは何も抱えていない。" + DumpWorld(transitions));
            Assert.IsFalse(transitions.Slide.Preloader.DesiredArea.IsValid,
                "望む先も立てていない。");

            // <b>止めない</b>（歩けるままである）。
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "Gameplay 時計は止まっていない。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "遊べるまま。");

            // ---- 受理されて初めて読む ----
            yield return StandJustBefore(gate, Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドで渡れた。失敗=" + transitions.Slide.LastFailure);
            Assert.AreEqual(1, host.CountOf(P55AreaBScene),
                "<b>B を読んだのは 1 回だけ</b>（受理の 1 回。読んだ順="
                + string.Join(",", host.Loaded) + "）。");
            Assert.GreaterOrEqual(transitions.Slide.PreloadRequestCount, 1,
                "受理が読込を頼んだ。");
            Assert.AreEqual(2, SceneManager.sceneCount,
                "着いたあとは 2 枚（活動 B ＋ 引き継いだ A）。" + DumpWorld(transitions));

            // <b>台帳が実 Scene に合っている</b>（工程 P55-08c。GPT 再修正③）。
            //
            // 枚数だけでは足りない。載っているのに台帳に居ないと、在留枠が空いていると誤認して
            // 上限 2 を超えて読める。<b>実体ハンドルと Scene の対応</b>まで見る。
            Assert.IsTrue(TryFindBundle(AreaA, out AreaRuntimeBundle aBundle), "A の束がある。");
            Assert.IsTrue(TryFindBundle(AreaB, out AreaRuntimeBundle bBundle), "B の束もある。");
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                "在留は 2（活動 B ＋ 引き継いだ A）。" + DumpWorld(transitions));
            Assert.AreEqual(AreaActivationPhase.Active,
                transitions.Slide.Residency.PhaseOf(bBundle.Instance), "B が Active。");
            Assert.AreEqual(AreaActivationPhase.Staged,
                transitions.Slide.Residency.PhaseOf(aBundle.Instance),
                "<b>A は捨てずに預かっている</b>（裁定 2 の引き継ぎ。付録 C.29）。");
            Assert.IsTrue(transitions.SlideSceneHost.IsLoaded(aBundle.SceneHandle),
                "A の Scene は載ったまま。");
            Assert.IsTrue(transitions.SlideSceneHost.IsLoaded(bBundle.SceneHandle),
                "B の Scene も載っている。");
            Assert.AreNotEqual(aBundle.SceneHandle, bBundle.SceneHandle, "別の Scene である。");
            Assert.AreEqual(AreaB.Value, CurrentAreaProvider.Current.AreaId.Value, "居るのは B。");

            // ---- 往復しても読み直さない（保持した Scene を再利用する）----
            //
            // <b>裁定の「ロード済みエリアへの再移動は保持した Scene を再利用する。
            // 人工的な待ち時間は入れない」</b>を、読込回数で言う。
            yield return StandJustBefore(FindExitGate(ExitBWest), Vector3.right);
            yield return SettleCamera();
            yield return HoldUntil(Key.A, () => transitions.SlideCommittedCount > 1, 25f);
            yield return SettleWorld(transitions);

            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();
            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 2, 25f);
            yield return SettleWorld(transitions);

            Assert.AreEqual(3, transitions.SlideCommittedCount,
                "3 回渡った（A→B→A→B）。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(1, host.CountOf(P55AreaBScene),
                "<b>B を読んだのは最初の 1 回だけ</b>（往復で読み直さない）。"
                + " 読んだ順=" + string.Join(",", host.Loaded));
            Assert.AreEqual(0, host.CountOf(P55AreaAScene),
                "<b>A は一度も読み直していない</b>（起動時から載っていて、引き継ぎで保持された）。"
                + " 読んだ順=" + string.Join(",", host.Loaded));
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
        /// <item><description>次のフレームに同じ接続が<b>自動で選ばれ</b>、
        /// <b>遅れて着いた Scene が「また必要な先読み」になって撤去されない</b>。</description></item>
        /// </list>
        ///
        /// <b>工程 P55-15c で、その心配ごとは根から無くなった</b>——
        /// 距離で読むのをやめたので、<b>プレイヤーの操作なしに読込が始まる経路が無い</b>。
        /// この検査はいま「立っているだけでは何も起きない」を、より強い形で見ている
        /// （抑止が効いているからではなく、<b>始める者が居ない</b>から）。
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

            // <b>抑止はもう要らない</b>（工程 P55-15c）。抑止は「距離による先読みが
            // プレイヤーの再操作なしに同じ先へ向き直す」のを止める仕掛けだった。
            // 距離で読むのをやめたので、<b>向き直る経路そのものが無い</b>——
            // 見るべきは「望みが立て直されていない」ことである。
            Assert.IsFalse(transitions.Slide.Preloader.DesiredArea.IsValid,
                "望みを立て直していない。" + DumpWorld(transitions));

            // ---- 2 回目（手動の再試行）：これも時間切れで戻る ----
            //
            // <b>ここで抑止が解ける</b>（§5 の「次の新しい遷移操作で一度だけ」）。
            // 解けたあと遷移が走っている間は選定が回らないので、保持は空のままである。
            yield return HoldUntil(Key.D, () => transitions.Slide.TimedOutCount > 1, 25f);

            Assert.AreEqual(2, transitions.Slide.TimedOutCount, "再試行も時間切れになった。");
            Assert.AreEqual(2, transitions.SlideRolledBackCount, "もう一度戻した。");
            Assert.AreEqual(1, host.LoadCount,
                "終端していない操作の上に読込を重ねていない。" + DumpWorld(transitions));
            Assert.IsFalse(transitions.Slide.Preloader.DesiredArea.IsValid,
                "<b>再失敗でも望みが残らない</b>（工程 P55-15c）。" + DumpWorld(transitions));

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
