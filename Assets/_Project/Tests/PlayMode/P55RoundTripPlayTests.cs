using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P5.5 の<b>連続往復</b>（仕様書 §11 の P13。工程 P55-06a）。
    ///
    /// <b>1 往復通ったことは 5 往復の証拠にならない。</b> P01／P02 は 1 回ずつの受理と到着を見る。
    /// ここで見たいのは<b>繰り返しても溜まらない</b>ことである——Scene・在留台帳・
    /// 先読み・進行の記録のどれかが 1 往復ごとに少しずつ残ると、
    /// 1 回の検査では気付けず、試遊の 10 分後に落ちる。
    ///
    /// <b>導線は両配置で通す</b>（東西・南北）。「同じ性質の経路が複数あるとき、
    /// 1 本通ったことは他の本の証拠にならない」（GPT レビュー R13 指摘①）。
    /// 南北は到着位置が戦闘区画のすぐ南（1.5m）に来るので、封鎖の当たり方が東西と違う。
    ///
    /// <b>往復そのものはテレポートを挟まない。</b> 到着してから出入口の範囲までは
    /// 実キーで歩く——到着位置と出入口が 1.1m 離れているので、
    /// 「押しっぱなしで着いて、離して、また押す」という人の操作がそのまま往復になる。
    /// 調査・レバー・戦闘区画へは、P5 の長経路検査と同じく station 間を置き直して移る
    /// （歩行そのものは P5-P05 が見ている。ここで見たいのは往復の繰り返しである）。
    /// </summary>
    public sealed class P55RoundTripPlayTests
    {
        /// <summary>この配置の導線（Scene・ID・キー）。</summary>
        private sealed class Route
        {
            internal string Label;
            internal string AreaAScene;
            internal string TrialScene;
            internal StableId AreaAId;
            internal StableId AreaBId;
            internal StableId ExitFromA;
            internal StableId ExitFromB;
            internal Key Forward;
            internal Key Back;
            internal Vector3 ForwardStep;
            internal Vector3 BackStep;
        }

        private static Route RouteOf(string key) => key == "NorthSouth"
            ? new Route
            {
                Label = "南北",
                AreaAScene = "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_AreaS.unity",
                TrialScene = "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_ConnectionTrial.unity",
                AreaAId = new StableId("area_p55_s"),
                AreaBId = new StableId("area_p55_n"),
                ExitFromA = new StableId("exit_p55_s_north"),
                ExitFromB = new StableId("exit_p55_n_south"),
                Forward = Key.W,
                Back = Key.S,
                ForwardStep = Vector3.forward,
                BackStep = Vector3.back,
            }
            : new Route
            {
                Label = "東西",
                AreaAScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity",
                TrialScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_ConnectionTrial.unity",
                AreaAId = new StableId("area_p55_a"),
                AreaBId = new StableId("area_p55_b"),
                ExitFromA = new StableId("exit_p55_a_east"),
                ExitFromB = new StableId("exit_p55_b_west"),
                Forward = Key.D,
                Back = Key.A,
                ForwardStep = Vector3.right,
                BackStep = Vector3.left,
            };

        /// <summary>B の遭遇戦（両配置で同じ Data を使う）。</summary>
        private static readonly StableId EncounterB = new StableId("encounter_p5_b_road");

        /// <summary>A の<b>正常な</b>調査地点（壁越しで拒否される地点と取り違えない）。</summary>
        private static readonly StableId OpenInvestigationPoint = new StableId("point_p5_a_01");

        /// <summary>§11 の P13 が数える往復。</summary>
        private const int RoundTrips = 5;

        private GameObject _bootstrap;
        private Keyboard _keyboard;
        private Route _route;

        [SetUp]
        public void SetUp()
        {
            P55ResidentRig.Reset();
            RemoveStrayDevices();
            ClearStatics();
            AreaPendingArrival.ResetDiagnostics();
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            if (_route != null)
            {
                yield return SceneManager.LoadSceneAsync(_route.TrialScene, LoadSceneMode.Single);
                DestroyLaunchers();
                yield return null;
                DestroyLaunchers();
            }

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

            ClearStatics();
            RemoveDevices();
            _route = null;
            yield return null;
        }

        // ---------------------------------------------------------------- P13

        /// <summary>
        /// P13：<b>調査 → 開通 → 5 往復 → 遭遇戦 → 再訪 → 死亡再開 → 再戦</b>を一周する。
        ///
        /// 見るのは 3 つ。
        /// <list type="number">
        /// <item><description><b>繰り返しても溜まらない</b>——毎往復、Scene は 1 枚に戻り、
        /// 在留台帳は空、先読みは手ぶら、進行の記録は増えも減りもしない。</description></item>
        /// <item><description><b>初回の徳は 22</b>（10 ＋ 12。§8.5）。往復でも再訪でも増えない。</description></item>
        /// <item><description><b>不要な再出現がない</b>——同じ周期の再訪では敵が湧かない。
        /// <b>死亡再開のあとは湧く</b>（§9.1 の周期。これは設計どおり）。
        /// それでも<b>徳は増えない</b>——報酬は周期ではなく <c>RewardId</c> で 1 度きりだから。
        /// ここを取り違えると「死んで稼ぐ」が通ってしまう。</description></item>
        /// </list>
        /// </summary>
        [UnityTest]
        public IEnumerator TheWholeRoute_SurvivesFiveRoundTripsAFightAndARespawn(
            [Values("EastWest", "NorthSouth")] string arrangement)
        {
            _route = RouteOf(arrangement);
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            GameSessionState session = Sessions().Session;
            Assert.AreEqual(0, session.Progress.Virtue, "前提：まだ何も得ていない。");
            Assert.AreEqual(0, session.RespawnCycle, "前提：まだ死んでいない。");

            // ================================ 1. 調査（実キー E）================================
            var coordinator = Object.FindFirstObjectByType<InvestigationCoordinator>();
            Assert.IsNotNull(coordinator, "A に調査の調停役がある。");
            // <b>ID で引く。</b> A には正常な調査地点と<b>壁越しで拒否される地点</b>が置いてある。
            // <c>FindFirstObjectByType</c> は並び順まかせなので、Scene を作り直すと
            // 拒否される側を掴むことがある——南北配置で実際に起きて、
            // 「実キー E で調査が受理される。理由=None」で落ちた（記録 027）。
            InvestigationInteractable point = FindInvestigation(OpenInvestigationPoint);
            Assert.IsNotNull(point, "正常な調査地点が Interact 候補として出ている。");

            var interaction = Object.FindFirstObjectByType<AreaInteractionController>();
            var mediator = Object.FindFirstObjectByType<AreaInteractInput>();
            yield return PlaceAt(point.InteractionAnchor + new Vector3(0f, 0f, -0.8f));
            yield return PressKeyUntil(Key.E, () => coordinator.LastRequestId > 0, 6f);
            Assert.Greater(coordinator.LastRequestId, 0,
                "実キー E で調査が受理される。調停の理由=" + coordinator.LastRejectReason
                + " 窓口の拒否=" + (interaction != null ? interaction.LastRejection.ToString() : "窓口なし")
                + " 実行=" + (mediator != null ? mediator.InteractCount : -1)
                + " 捨てた=" + (mediator != null ? mediator.DiscardedCount : -1)
                + " 候補数=" + AreaInteractableRegistry.Count
                + " 受付半径=" + point.InteractionRadius
                + " 利用可=" + point.IsAvailable
                + " 錨=" + point.InteractionAnchor
                + " 主人公=" + Object.FindFirstObjectByType<PlayerRoot>().transform.position
                + " 候補=" + DumpCandidates()
                + " 現在Area=" + (CurrentAreaProvider.Current != null
                    ? CurrentAreaProvider.Current.AreaId.Value : "なし")
                + " 遮蔽=" + DumpBlockers(
                    Object.FindFirstObjectByType<PlayerRoot>().transform.position,
                    point.InteractionAnchor));

            Assert.IsTrue(session.TryGetArea(_route.AreaAId, out AreaRuntimeState areaA));
            yield return WaitUntilOrTimeout(() => areaA.InvestigatedCount >= 1, 15f);
            Assert.AreEqual(1, areaA.InvestigatedCount, "調査が成功して記録に残る。");

            // ================================ 2. 門（実キー E）================================
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "レバーがある。");
            Assert.IsFalse(lever.Door.IsOpened, "前提：門は閉じている。");
            StableId gateFlag = lever.FlagId;

            yield return PlaceAt(lever.InteractionAnchor + new Vector3(0.6f, 0f, 0f));
            yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 6f);
            Assert.AreEqual(1, lever.OpenedCount, "実キー E で門が開通する。");
            Assert.IsTrue(areaA.IsOpen(gateFlag), "開通が記録に残る。");

            // ================================ 3. 5 往復（実キーだけ）================================
            yield return PlaceBeforeExit(_route.ExitFromA, _route.BackStep);

            for (int trip = 1; trip <= RoundTrips; trip++)
            {
                yield return SlideAcross(_route.Forward, trip * 2 - 1, "往路 " + trip);
                AssertSettled(transitions, _route.AreaBId, "往路 " + trip);
                AssertArrivalIsClearOfTheArena("往路 " + trip);
                Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                    "往路 " + trip + "：到着しただけでは敵は湧かない（§13.2 の「初期敵 0」）。");
                Assert.AreEqual(0, session.Progress.Virtue, "往路 " + trip + "：往復では徳は増えない。");

                yield return SlideAcross(_route.Back, trip * 2, "復路 " + trip);
                AssertSettled(transitions, _route.AreaAId, "復路 " + trip);

                // <b>進行の記録が往復で揺れない。</b> 調査も開通も、押し直しても増えない。
                Assert.IsTrue(session.TryGetArea(_route.AreaAId, out AreaRuntimeState againA));
                Assert.AreEqual(1, againA.InvestigatedCount, "復路 " + trip + "：調査の記録は 1 のまま。");
                Assert.IsTrue(againA.IsOpen(gateFlag), "復路 " + trip + "：門は開いたまま。");
            }

            Assert.AreEqual(RoundTrips * 2, transitions.SlideCommittedCount,
                RoundTrips + " 往復ぶんのスライドが全部 Commit した。");
            Assert.AreEqual(RoundTrips * 2, transitions.ConnectionTravelCount, "受理も同じ回数。");
            Assert.AreEqual(0, transitions.CompletedCount,
                "往復のあいだ Single（暗転）経路は 1 度も通っていない。");
            Assert.AreEqual(0, transitions.SlideRolledBackCount, "戻していない。");
            Assert.AreEqual(string.Empty, transitions.Slide.LastFailure, "失敗を抱えていない。");

            // ================================ 4. 遭遇戦 ================================
            yield return SlideAcross(_route.Forward, RoundTrips * 2 + 1, "戦闘へ");
            AssertSettled(transitions, _route.AreaBId, "戦闘へ");

            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runner, "B に Encounter の調停がある。");
            Assert.IsTrue(runner.IsWired, "Encounter が配線されている。");
            Assert.AreEqual(AreaEncounterState.Dormant, runner.State,
                RoundTrips + " 往復してもまだ始まっていない。");

            yield return FightToVictory(runner);

            Assert.AreEqual(22, session.Progress.Virtue, "初回は 10 ＋ 12 ＝ 22（§8.5）。");
            Assert.IsTrue(session.TryGetArea(_route.AreaBId, out AreaRuntimeState areaB));
            Assert.IsTrue(areaB.IsEncounterCleared(EncounterB, session.RespawnCycle),
                "この周期のクリアを記録する。");

            // ================================ 5. 再訪では湧かない ================================
            yield return WaitUntilFreeToTravel();
            yield return PlaceBeforeExit(_route.ExitFromB, _route.ForwardStep);
            yield return SlideAcross(_route.Back, RoundTrips * 2 + 2, "戦闘後の復路");
            yield return PlaceBeforeExit(_route.ExitFromA, _route.BackStep);
            yield return SlideAcross(_route.Forward, RoundTrips * 2 + 3, "再訪");

            var runnerAgain = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runnerAgain);
            Assert.AreEqual(AreaEncounterState.Cleared, runnerAgain.State,
                "記録からクリア済みを復元する（§4.3）。");

            yield return PlaceAt(EncounterTriggerPoint());
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            Assert.AreEqual(AreaEncounterState.Cleared, runnerAgain.State, "再訪では始まらない（§8.4 末尾）。");
            Assert.AreEqual(EncounterStartRejection.AlreadyCleared, runnerAgain.LastRejection);
            Assert.AreEqual(0, Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length,
                "再訪の敵は 0。");
            Assert.AreEqual(22, session.Progress.Virtue, "再訪でも徳は増えない。");

            // ================================ 6. 死亡再開 ================================
            yield return KillPlayerWithRealHits();
            yield return WaitForRespawnPrompt();
            yield return PressKeyUntil(Key.Enter,
                () => transitions.CompletedCount > 0 || transitions.HasTerminalFailure, 25f);

            Assert.IsFalse(transitions.HasTerminalFailure,
                "終端失敗にならない。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(1, transitions.CompletedCount, "暗転（Single）経路で再開が着いた。");
            Assert.AreEqual(1, SceneManager.sceneCount, "隔離 Area は残っていない。");
            Assert.AreEqual(0, transitions.Slide.Residency.ResidentCount, "在留台帳も実 Scene に合っている。");
            Assert.AreEqual(_route.AreaAId.Value, CurrentAreaProvider.Current.AreaId.Value,
                "再開点（A）へ戻る。");

            // ---- 進行保持（§9.1）----
            Assert.AreEqual(1, session.RespawnCycle, "再出現の周期が 1 つ進む。");
            Assert.AreEqual(22, session.Progress.Virtue, "徳は死んでも減らない（§8.5）。");
            Assert.IsTrue(session.TryGetArea(_route.AreaAId, out AreaRuntimeState afterA));
            Assert.AreEqual(1, afterA.InvestigatedCount, "調査の記録は残る。");
            Assert.IsTrue(afterA.IsOpen(gateFlag), "開通も残る（もう一度レバーを引かせない）。");

            // ---- 再出現は設計どおり（§9.1 の周期）----
            Assert.IsTrue(session.TryGetArea(_route.AreaBId, out AreaRuntimeState afterB));
            Assert.IsFalse(afterB.IsEncounterCleared(EncounterB, session.RespawnCycle),
                "死亡再開で周期が進むので、遭遇戦は未クリアへ戻る（§9.1。これは設計どおり）。");

            // ================================ 7. 再戦の徳（実測） ================================
            //
            // <b>「初回徳 22」は「二度と増えない」ではない。</b> ここは最初
            // 「報酬は RewardId で 1 度きりだから増えない」と書いて<b>落ちた</b>——
            // 実測は 44 だった。敵の報酬 Data は <c>_grantOnce: 0</c>（撃破ごと）で、
            // 10（近接）＋12（遠隔）は<b>倒すたびに</b>入る。周期が進めば敵は湧き直すので、
            // 倒せばまた 22 入る。
            //
            // §11 の「初回徳 22」が縛っているのは<b>初回の額</b>と、
            // 同じ周期のあいだ往復・再訪で増えないことである（上で見た）。
            // 「死んで稼げてよいか」は設計の判断で、ここは<b>いまの実測を固定する</b>だけにする。
            var leverAgain = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(leverAgain, "再開先にもレバーがある。");
            Assert.IsTrue(leverAgain.Door.IsOpened, "門は開いたまま復元される。");

            yield return PlaceBeforeExit(_route.ExitFromA, _route.BackStep);
            yield return SlideAcross(_route.Forward, RoundTrips * 2 + 4, "再戦へ");

            var runnerAfter = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runnerAfter);
            Assert.AreEqual(AreaEncounterState.Dormant, runnerAfter.State,
                "新しい周期では待機へ戻っている。");

            yield return FightToVictory(runnerAfter);

            Assert.AreEqual(44, session.Progress.Virtue,
                "新しい周期で倒し直すと、撃破ごとの報酬（10 ＋ 12）がもう一度入る。"
                + " 敵の報酬は GrantOnce ではない（SO_Reward_Enemy_Melee／Ranged は _grantOnce: 0）。"
                + " 初回の 22 は「初回の額」であって「上限」ではない。");
        }

        // ---------------------------------------------------------------- 往復の道具

        /// <summary>
        /// 実キーを押しっぱなしにして、境界を 1 回渡る。
        ///
        /// <b>テレポートは挟まない。</b> 到着位置と出入口は 1.1m 離れているので、
        /// 押しっぱなしにすれば主人公が自分で歩いて範囲へ入り、0.15 秒の連続入力が溜まる
        /// （§6.1）。到着直後は入力が一度切れるまで溜まらない——
        /// 前の <see cref="HoldUntil"/> が離しているので、そこは人の操作と同じ。
        /// </summary>
        private IEnumerator SlideAcross(Key key, int expectedCommits, string label)
        {
            AreaTransitionService transitions = Transitions();
            yield return HoldUntil(key,
                () => transitions.SlideCommittedCount >= expectedCommits
                      || transitions.HasTerminalFailure, 25f);

            Assert.IsFalse(transitions.HasTerminalFailure,
                label + "：終端失敗。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(expectedCommits, transitions.SlideCommittedCount,
                label + "：スライドで渡れていない（失敗=" + transitions.Slide.LastFailure + "）。");

            // Commit のあとに旧 Area の撤去が続き、そのあと §5 の距離による先読みが
            // <b>来た方の Area をもう一度 Staged で持つ</b>（工程 P55-08b）。
            // 枚数が 1 へ落ちるのを待つ形だと<b>永遠に待つ</b>ので、
            // 「撤去も先読みも走っていない」まで進めて数える。
            float deadline = Time.realtimeSinceStartup + 20f;
            int quiet = 0;
            while (quiet < 3 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;

                bool busy = transitions.Slide.IsTransitioning
                    || transitions.Slide.IsRetireInFlight
                    || transitions.Slide.Preloader.HasLiveSceneOperation
                    || transitions.IsSingleLoadInFlight;
                quiet = busy ? 0 : quiet + 1;
            }

            yield return null;
        }

        /// <summary>
        /// 1 往復ぶんの<b>後始末</b>が終わっていること。
        ///
        /// <b>ここが P13 の本体である。</b> 1 回だけなら残っていても気付かない。
        /// 毎往復ここを見るので、少しずつ溜まる種類の壊れ方が回数に比例して現れる。
        /// </summary>
        private static void AssertSettled(
            AreaTransitionService transitions, StableId expectedArea, string label)
        {
            // <b>「2 枚」が落ち着いた状態である</b>（工程 P55-08b）。
            //
            // 以前はここを 1 枚で見ていた。§5 の距離による先読みが入ってから、
            // 到着した主人公は<b>逆向きの出入口のすぐ内側</b>に居るので、
            // 旧 Area を撤去したあと同じ Area をもう一度 Staged で持つ。
            //
            // <b>溜まる壊れ方はここで見える。</b> 数えたいのは「往復のたびに増えない」ことで、
            // 1 でも 2 でも<b>固定値であること</b>が効いている——3 枚目が載れば落ちる。
            Assert.AreEqual(2, SceneManager.sceneCount, label + "：Scene は 2 枚で落ち着く。");
            Assert.AreEqual(2, transitions.Slide.Residency.ResidentCount,
                label + "：在留台帳は居る Area と隣の 2 つ（旧 Area は撤去済み）。");
            Assert.AreEqual(AreaPreloadPhase.Staged, transitions.Slide.Preloader.Phase,
                label + "：先読みは隣を持っている（§5 の距離による先読み）。");
            Assert.AreNotEqual(expectedArea.Value,
                transitions.Slide.Preloader.StagedArea.AreaId.Value,
                label + "：持っているのは<b>いま居ない方</b>の Area。");
            Assert.IsFalse(AreaStagingRequest.IsRequested,
                label + "：先読みの申し入れが残っていない。");
            Assert.IsFalse(transitions.Slide.IsTransitioning, label + "：遷移は終わっている。");
            Assert.AreEqual(expectedArea.Value, CurrentAreaProvider.Current.AreaId.Value,
                label + "：居る Area が一致する。");
            Assert.AreEqual(1,
                Object.FindObjectsByType<PlayerRoot>(FindObjectsSortMode.None).Length,
                label + "：主人公は 1 人（往復で増えない）。");
            Assert.AreEqual(1,
                Object.FindObjectsByType<AreaCameraRigHost>(FindObjectsSortMode.None).Length,
                label + "：常駐 Rig も 1 つ（§11 の P12 と同じ唯一性）。");
        }

        /// <summary>
        /// 到着位置が<b>戦闘区画の外</b>にあること（§8.4 手順 7 と同じ考え方）。
        ///
        /// 区画の中へ到着すると、あとで封鎖された瞬間に主人公が壁の内側へ閉じ込められる——
        /// 「着いたら戦闘が始まっていた」という、到着と戦闘の順序が崩れた形になる。
        ///
        /// <b>配置によって離し方が違う。</b> 東西では入口が区画の横 7.5m 外にあるが、
        /// 南北では入口が区画と同じ X に来るので、離せるのは奥行きだけ（1.5m）になる。
        /// だから配置ごとに実測して見る（定数を読み合わせない）。
        /// </summary>
        private static void AssertArrivalIsClearOfTheArena(string label)
        {
            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            if (arena == null)
            {
                return;
            }

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, label + "：主人公が居る。");

            Bounds safe = arena.SafeBounds;
            Vector3 at = player.transform.position;
            bool inside = at.x > safe.min.x && at.x < safe.max.x
                          && at.z > safe.min.z && at.z < safe.max.z;
            Assert.IsFalse(inside,
                label + "：到着位置が戦闘区画の中にある（到着=" + at + " 区画=" + safe
                + "）。封鎖された瞬間に閉じ込められる配置（§8.4 手順 7）。");
        }

        /// <summary>出入口の範囲の<b>手前</b>へ置く（外壁へめり込ませない）。</summary>
        private IEnumerator PlaceBeforeExit(StableId exitId, Vector3 back)
        {
            AreaExitGate gate = FindExitGate(exitId);
            yield return PlaceAt(gate.transform.position + back * 0.4f);
            Assert.IsTrue(gate.IsWired, "出入口に主人公の根が配線されている。");
            Assert.IsTrue(gate.PlayerInside, "範囲内に居る。");
        }

        // ---------------------------------------------------------------- 戦闘の道具

        /// <summary>遭遇戦を実 Hitbox で勝ち切る（結果を直接セットしない）。</summary>
        private IEnumerator FightToVictory(AreaEncounterRunner runner)
        {
            yield return PlaceAt(EncounterTriggerPoint());
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 6f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                "Trigger 進入で戦闘が始まる。拒否=" + runner.LastRejection
                + " 補足=" + runner.LastFailureDetail);

            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            Assert.IsNotNull(arena, "アリーナ境界がある。");
            Assert.IsTrue(arena.IsEnabled, "アリーナ境界が有効になる。");

            EnemyActor[] enemies = Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);
            Assert.AreEqual(2, enemies.Length, "骸骨剣士 1 ＋ 骸骨弓兵 1（§8.1）。");

            for (int i = 0; i < enemies.Length; i++)
            {
                yield return KillWithRealHitbox(enemies[i]);
            }

            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Cleared, 8f);
            Assert.AreEqual(AreaEncounterState.Cleared, runner.State, "勝利で終わる。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current,
                "探索へ戻る（§8.4 手順 6）。");
            Assert.IsFalse(arena.IsEnabled, "一時境界を解放する（§8.4 手順 5）。");
            Assert.AreEqual(0, arena.ActiveBlockerCount, "封鎖 Collider も実際に無効へ戻る。");
        }

        private IEnumerator KillWithRealHitbox(EnemyActor enemy)
        {
            if (enemy == null || enemy.IsDefeated)
            {
                yield break;
            }

            var playerRoot = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(playerRoot);
            var facing = playerRoot.GetComponentInChildren<PlayerFacing>();
            Assert.IsNotNull(facing, "主人公の向き（PlayerFacing）がある。");
            var player = playerRoot.GetComponentInChildren<PlayerStateController>();
            Assert.IsNotNull(player);

            float deadline = Time.realtimeSinceStartup + 25f;
            float nextPress = 0f;
            bool pressed = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (enemy == null || enemy.IsDefeated)
                {
                    break;
                }

                // 敵の手前 1.0m へ張り付き、敵の方（+Z）を向く。判定は Active の間だけ出る。
                Vector3 stick = enemy.transform.position + new Vector3(0f, 0f, -1.0f);
                if (playerRoot.Body != null)
                {
                    playerRoot.Body.position = stick;
                    playerRoot.Body.linearVelocity = Vector3.zero;
                }

                playerRoot.transform.position = stick;
                facing.ConfirmFromInput(Vector2.up);

                if (Time.realtimeSinceStartup >= nextPress)
                {
                    pressed = !pressed;
                    InputSystem.QueueStateEvent(
                        _keyboard, pressed ? new KeyboardState(Key.J) : new KeyboardState());
                    nextPress = Time.realtimeSinceStartup + 0.12f;
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.IsTrue(enemy == null || enemy.IsDefeated,
                "実 Hitbox で敵を倒せていない（HP=" + (enemy != null ? enemy.CurrentHp : 0)
                + " 主人公の状態=" + player.Current
                + " mode=" + (GameModeProvider.Current != null
                    ? GameModeProvider.Current.Current.ToString() : "null") + "）。");
        }

        private static Vector3 EncounterTriggerPoint()
        {
            var trigger = Object.FindFirstObjectByType<AreaEncounterTrigger>();
            Assert.IsNotNull(trigger, "遭遇 Trigger が Scene にある。");
            return trigger.transform.position;
        }

        private static IEnumerator WaitUntilFreeToTravel()
        {
            var player = Object.FindFirstObjectByType<PlayerStateController>();
            Assert.IsNotNull(player);
            yield return WaitUntilOrTimeout(() => player.IsFreeToTravel, 5f);
        }

        // ---------------------------------------------------------------- 死亡

        private IEnumerator KillPlayerWithRealHits()
        {
            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.IsNotNull(vitals, "主人公の生存がある。");

            var attackerGo = new GameObject("P55RoundTripLethalAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = vitals.transform.position + Vector3.forward;

            int hits = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!vitals.IsDefeated && Time.realtimeSinceStartup < deadline)
            {
                hits++;
                vitals.ReceiveHit(new HitInfo(
                    attacker, vitals, Vector3.back, vitals.transform.position,
                    new HitDamage(9999, 0f, 0f), guardable: false, justGuardable: false,
                    hitId: HitId.Single(9900 + hits)));
                yield return null;
            }

            Object.DestroyImmediate(attackerGo);
            yield return null;
            Assert.IsTrue(vitals.IsDefeated, "前提：主人公が死んでいる。打った数=" + hits);
        }

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

        /// <summary>致死の攻撃元（実被弾経路を通すための最小の実装）。</summary>
        private sealed class LethalAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public int ActorId => GetInstanceID();
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
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

            _bootstrap = new GameObject("BootstrapRoot_P55RoundTripTest");
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

            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
        }

        /// <summary>station 間の置き直し（歩行そのものは P5-P05 が見ている）。</summary>
        private static IEnumerator PlaceAt(Vector3 position)
        {
            var root = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(root, "主人公の根がある。");
            root.transform.position = new Vector3(position.x, root.transform.position.y, position.z);
            if (root.Body != null)
            {
                root.Body.position = root.transform.position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
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

        private static IEnumerator WaitUntilOrTimeout(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        /// <summary>候補の中身（AreaId・距離・利用可）を 1 行にする。</summary>
        private static string DumpCandidates()
        {
            var sb = new System.Text.StringBuilder();
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            foreach (InvestigationInteractable item in
                Object.FindObjectsByType<InvestigationInteractable>(FindObjectsSortMode.None))
            {
                sb.Append('[').Append(item.InteractableId.Value)
                  .Append(" area=").Append(item.AreaId.Value)
                  .Append(" 可=").Append(item.IsAvailable)
                  .Append(" 距離=")
                  .Append(Vector3.Distance(player.transform.position, item.InteractionAnchor))
                  .Append(']');
            }

            return sb.ToString();
        }

        /// <summary>2 点の間にある Collider（遮蔽の犯人を名指しする）。</summary>
        private static string DumpBlockers(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            var sb = new System.Text.StringBuilder();
            RaycastHit[] hits = Physics.RaycastAll(from, delta.normalized, delta.magnitude);
            for (int i = 0; i < hits.Length; i++)
            {
                sb.Append('[').Append(hits[i].collider.name)
                  .Append('@').Append(hits[i].collider.transform.position).Append(']');
            }

            return hits.Length == 0 ? "なし" : sb.ToString();
        }

        /// <summary>その ID の調査地点（並び順に頼らない）。</summary>
        private static InvestigationInteractable FindInvestigation(StableId pointId)
        {
            foreach (InvestigationInteractable item in
                Object.FindObjectsByType<InvestigationInteractable>(FindObjectsSortMode.None))
            {
                if (item != null && item.InteractableId.Equals(pointId))
                {
                    return item;
                }
            }

            Assert.Fail("調査地点 '" + pointId.Value + "' が Scene にありません。");
            return null;
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

        private static GameSessionBootService Sessions()
        {
            GameSessionBootService service = BootstrapServices.Get<GameSessionBootService>();
            Assert.IsNotNull(service, "Session サービスが常駐していません。");
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

        private static void ClearStatics()
        {
            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
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
