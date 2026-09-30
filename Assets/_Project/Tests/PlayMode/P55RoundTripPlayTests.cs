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

            // 門を押し直した回数の控え（復路ごとに増えることで「入場ごとの復元」を言う）。
            int reappliedBefore = lever.Door.ReappliedCount;

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

                // <b>Scene 側の門も、押し直されはするが二重には開かない</b>
                // （工程 P55-11c。§6.2 手順 5・手順 6）。
                //
                // 上の 2 行は<b>Session の記録</b>を見ている。記録は集合と数なので、
                // 二度書いても値は変わらない——<b>Scene の部品が二度動いたこと</b>は現れない。
                // 旧 Area の引き継ぎ（手順 11）が入ってからは、戻り先の門は<b>壊されずに残っている</b>ので、
                // 入場ごとの「門の復元」が毎回そこへ触る。
                //
                // <b>レバーの <c>OpenedCount</c> では言えない</b>——あれは
                // <c>AreaRuntimeState.TryOpen</c> が通ったときだけ増えるので、記録が既に開通済みなら
                // 復元がどう壊れていても増えない（最初そう書いて、空振りだと分かった）。
                // 門側の 2 つの数で言う。
                // <list type="bullet">
                // <item><c>AppliedCount</c>（いま開通した回数）は<b>1 のまま</b>——二重に開通しない。</item>
                // <item><c>ReappliedCount</c>（押し直した回数）は<b>入場ごとに増える</b>——
                // 「入場ごとに押し直す」が実際に走っている。走っていなければ、留守のあいだに
                // 記録が変わった門を反映できない。</item>
                // </list>
                AreaFlagDoor doorNow = Object.FindFirstObjectByType<AreaFlagLever>()?.Door;
                Assert.IsNotNull(doorNow, "復路 " + trip + "：戻り先に門がある。");
                Assert.IsTrue(doorNow.IsOpened, "復路 " + trip + "：門は Scene の上でも開いたまま。");
                Assert.AreEqual(1, doorNow.AppliedCount,
                    "復路 " + trip + "：開通そのものは 1 回だけ（復元が「いま開通した」を繰り返していない）。");
                Assert.Greater(doorNow.ReappliedCount, reappliedBefore,
                    "復路 " + trip + "：入場ごとに押し直している（前回=" + reappliedBefore
                    + " いま=" + doorNow.ReappliedCount + "）。§6.2 手順 6。");
                reappliedBefore = doorNow.ReappliedCount;
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
            Assert.AreEqual(1, transitions.Slide.Residency.ResidentCount,
                "在留台帳も実 Scene に合っている（載っている 1 枚 ＝ 在留 1 つ。工程 P55-08c）。"
                + " 以前は 0 だった——Single 読込は台帳を通らないので、活動中 Area が"
                + " 台帳に載らないままだった。距離による先読みは発行の前に活動中 Area を載せる。");
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
        // ================================================================ 歩いて到達できるか（工程 P55-14c）

        /// <summary>
        /// <b>歩いて到達できるか</b>を見る（工程 P55-14c。GPT 受入 4）。
        ///
        /// <b>なぜ別に要るのか。</b> 既存の検査は目的地のたびに
        /// <c>PlaceAt</c> で主人公を<b>置き直して</b>いた。だから
        /// 「そこで押せば通る」は見ていたが、<b>そこまで歩けるか</b>は見ていなかった——
        /// 試遊で出た「戦闘区域に入れない」（アリーナ封鎖 4 枚が立っていた）は、
        /// 壁が立っていても既存の検査がすべて緑のままだった。
        ///
        /// <b>この検査は途中で座標を変えない。</b> 実キーだけで歩き、着けたかどうかだけを見る。
        /// Trigger を占有させたり、遷移を直接要求したりもしない——
        /// 到着後の配置だけはゲーム自身の遷移が行う。
        ///
        /// <b>寄り道の目標（waypoint）は座標変更ではない。</b> L 字の通路があるので
        /// 直線では仕切り壁に当たる。仕切りの<b>実際の当たりから</b>抜け口を割り出して、
        /// そこを目標にしてから次へ向かう。決め打ちの座標を書かないので、
        /// 配置を作り直しても壊れない。
        /// </summary>
        [UnityTest]
        public IEnumerator FromTheStart_WalkingReachesTheLeverTheInvestigationAndTheExit()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");
            Vector3 startedAt = player.transform.position;

            GameSessionState session = Sessions().Session;
            Assert.IsTrue(session.TryGetArea(_route.AreaAId, out AreaRuntimeState areaA), "A の記録がある。");

            // ---- 1. 調査地点まで歩く ----
            InvestigationInteractable point = FindInvestigation(OpenInvestigationPoint);
            Assert.IsNotNull(point, "調査地点がある。");
            yield return WalkTo(point.InteractionAnchor, 1.1f, 25f, "開始点 → 調査地点");

            var coordinator = Object.FindFirstObjectByType<InvestigationCoordinator>();
            yield return PressKeyUntil(Key.E, () => coordinator.LastRequestId > 0, 6f);
            yield return WaitUntilOrTimeout(() => areaA.InvestigatedCount >= 1, 15f);
            Assert.AreEqual(1, areaA.InvestigatedCount,
                "歩いて着いた場所で調査が通った。調停の理由=" + coordinator.LastRejectReason);

            // ---- 2. レバーまで歩く ----
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "レバーがある。");
            Assert.IsFalse(lever.Door.IsOpened, "前提：門は閉じている。");
            StableId gateFlag = lever.FlagId;

            yield return WalkTo(lever.InteractionAnchor, 1.1f, 25f, "調査地点 → レバー");
            yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 6f);
            Assert.AreEqual(1, lever.OpenedCount, "歩いて着いた場所でレバーを引けた。");
            Assert.IsTrue(areaA.IsOpen(gateFlag), "開通が記録に残る。");

            // ---- 3. 開通した門をくぐって出口まで歩く ----
            //
            // 仕切り壁の抜け口を<b>壁の当たりから</b>割り出す（決め打ちの座標を書かない）。
            AreaExitGate exit = FindExitGate(_route.ExitFromA);
            Assert.IsNotNull(exit, "出入口がある。");

            foreach (Vector3 waypoint in WaypointsAroundDivider(lever.InteractionAnchor,
                         exit.transform.position))
            {
                yield return WalkTo(waypoint, 1.4f, 25f, "仕切りの抜け口へ");
            }

            // <b>出口へ歩くと、着く前に遷移が成立する。</b> 出入口の範囲の中で出口方向へ
            // 0.15 秒押し続けた時点で受理されるので、そこで世界は止まる（§6.3）——
            // 「着くまで歩く」形にすると、止まった主人公を待って時間切れになる。
            AreaTransitionService transitions = Transitions();
            yield return WalkToUntil(exit.transform.position, 1.6f, 30f, "抜け口 → 出口",
                () => transitions.SlideCommittedCount > 0);

            yield return WaitUntilOrTimeout(() => transitions.SlideCommittedCount > 0, 25f);

            Assert.IsTrue(lever.Door.IsOpened, "門は開いたまま（くぐれた）。");
            Assert.Greater(Vector3.Distance(player.transform.position, startedAt), 5f,
                "実際に移動している（開始点に居たままではない）。");
            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "<b>歩いて行っただけで遷移が成立した</b>（途中で座標を変えていない）。失敗="
                + transitions.Slide.LastFailure);
        }

        /// <summary>
        /// <b>B の到着地点から戦闘区域へ歩いて入れる。封鎖は戦闘開始のときだけ立つ</b>
        /// （工程 P55-14c。GPT 受入 4。試遊報告①がここで捕まる）。
        ///
        /// <b>試遊で最初に詰まった経路である。</b> 活動ゲートの初回 Open が
        /// アリーナ封鎖 4 枚まで有効にしていたので、入口から戦闘区域の手前に壁が立っていた。
        /// 既存の検査は <c>PlaceAt</c> で区域の中へ置いていたので、素通りしていた。
        ///
        /// 見るのは 3 点。<b>入る前は封鎖が 0 枚</b>、<b>歩いて入れる</b>、
        /// <b>戦闘が始まって初めて封鎖される</b>。
        /// </summary>
        [UnityTest]
        public IEnumerator FromTheArrivalInB_WalkingEntersTheArenaAndOnlyThenItSeals()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();

            // ---- A から B へ（歩くだけ。到着配置はゲーム自身が行う）----
            yield return WalkFromStartToB();
            AssertSettled(transitions, _route.AreaBId, "往路");

            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            Assert.IsNotNull(arena, "アリーナ境界が居る。");
            Assert.Greater(arena.BlockerCount, 0, "前提：封鎖する Collider がある。");
            Assert.AreEqual(0, arena.ActiveBlockerCount,
                "<b>入る前は 1 枚も立っていない</b>（" + arena.ActiveBlockerCount + " / "
                + arena.BlockerCount + " 枚）。立っていたら歩いて入れない。");

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Vector3 arrivedAt = player.transform.position;

            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();
            Assert.IsNotNull(runner, "遭遇の調停がある。");
            Assert.AreEqual(AreaEncounterState.Dormant, runner.State, "前提：まだ始まっていない。");

            // ---- 歩いて戦闘区域へ入る ----
            Vector3 trigger = EncounterTriggerPoint();
            Assert.Greater(Vector3.Distance(arrivedAt, trigger), 4f,
                "前提：到着地点は戦闘区域の外にある（" + arrivedAt + " → " + trigger + "）。"
                + " 中に着いているなら、この検査は歩いて入ることを見ていない。");

            yield return WalkToUntil(trigger, 1.0f, 30f, "B の到着地点 → 戦闘区域",
                () => runner.State != AreaEncounterState.Dormant);

            Assert.AreNotEqual(AreaEncounterState.Dormant, runner.State,
                "歩いて戦闘区域へ入り、遭遇が始まった（いま=" + runner.State + "）。"
                + " 主人公=" + player.transform.position + " 目標=" + trigger);

            // ---- 封鎖はここで初めて立つ ----
            yield return WaitUntilOrTimeout(() => arena.ActiveBlockerCount > 0, 8f);
            Assert.IsTrue(arena.IsEnabled, "戦闘が始まって封鎖した（§8.2 手順 5）。");
            Assert.AreEqual(arena.BlockerCount, arena.ActiveBlockerCount,
                "封鎖 Collider が実際に有効（" + arena.ActiveBlockerCount + " 枚）。");
            Assert.AreEqual(1, arena.EnableCount, "封鎖したのは 1 回だけ。");
        }

        /// <summary>
        /// <b>戦闘が終わると封鎖が解け、歩いて出られる</b>（工程 P55-14c。GPT 受入 4）。
        ///
        /// 解放したつもりで壁が残ると、戦闘後に<b>区域から出られなくなる</b>。
        /// <c>IsEnabled</c>（意図）だけでなく <c>ActiveBlockerCount</c>（実体）を見て、
        /// さらに<b>実際に歩いて出る</b>。
        ///
        /// <b>敵を倒す区間だけは既存の戦闘補助を使う</b>（<c>KillWithRealHitbox</c>）。
        /// あれは敵へ張り付くので位置を動かすが、**封鎖された区域の内側での移動**であり、
        /// 到達可能性については何も主張していない。歩いて確かめるのは
        /// 「区域へ入る」（上の検査）と「区域から出る」（この検査）である。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterTheFight_TheArenaReleasesAndWalkingLeavesIt()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            yield return WalkFromStartToB();
            AssertSettled(transitions, _route.AreaBId, "往路");

            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            var runner = Object.FindFirstObjectByType<AreaEncounterRunner>();

            Vector3 trigger = EncounterTriggerPoint();
            yield return WalkToUntil(trigger, 1.0f, 30f, "戦闘区域へ",
                () => runner.State != AreaEncounterState.Dormant);
            Assert.AreNotEqual(AreaEncounterState.Dormant, runner.State, "前提：遭遇が始まった。");

            yield return FightToVictory(runner);

            Assert.AreEqual(0, arena.ActiveBlockerCount, "前提：封鎖が解けている（実体）。");

            // ---- 歩いて区域から出る ----
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Bounds safe = arena.SafeBounds;
            Vector3 outside = new Vector3(safe.min.x - 3f, player.transform.position.y,
                player.transform.position.z);

            yield return WalkTo(outside, 1.8f, 30f, "戦闘区域 → 外へ");

            Assert.Less(player.transform.position.x, safe.min.x,
                "歩いて区域の外へ出られた（x=" + player.transform.position.x
                + " 区域の西端 x=" + safe.min.x + "）。");
        }

        /// <summary>
        /// <b>斜めや端から境界へ寄っても、遷移するか Area 内で止まるかのどちらかになる</b>
        /// （工程 P55-14c。GPT 受入 4）。<b>未遷移のまま外へは出ない。</b>
        ///
        /// 試遊報告③の再現経路である。境界（<c>AreaSeamBarrier</c>）が入ったので、
        /// 出口判定が成立しなければ押し当てて止まる。
        ///
        /// 通路の端・斜めの 2 通りで寄せ、<b>どのフレームでも Area の外に居ない</b>ことを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator ApproachingTheSeamOffCentre_NeverLeavesTheAreaUntransitioned()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            var player = Object.FindFirstObjectByType<PlayerRoot>();

            Assert.IsTrue(TryFindAreaRoot(_route.AreaAId, out AreaRoot areaRoot), "A の根を引ける。");
            Assert.AreEqual(1, areaRoot.SeamBarriers.Count, "前提：接続口に境界がある。");
            float outerX = areaRoot.SeamBarriers[0].Blocker.bounds.max.x;

            AreaExitGate exit = FindExitGate(_route.ExitFromA);
            Vector3 gateAt = exit.transform.position;

            // <b>門を開けてから通路へ入る。</b> 開けないと通路へ辿り着けない。
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "レバーがある。");
            yield return WalkTo(lever.InteractionAnchor, 1.1f, 25f, "開始点 → レバー");
            yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 6f);
            Assert.AreEqual(1, lever.OpenedCount, "門を開通させた。");

            foreach (Vector3 waypoint in WaypointsAroundDivider(
                         player.transform.position, gateAt))
            {
                yield return WalkTo(waypoint, 1.4f, 25f, "仕切りの抜け口へ");
            }

            // 通路の端へ寄ってから、斜めに押し込む。
            Vector3 offCentre = gateAt + new Vector3(-3f, 0f, 1.4f);
            yield return WalkToUntil(offCentre, 1.4f, 25f, "通路の端へ",
                () => transitions.SlideCommittedCount > 0);

            float worstX = player.transform.position.x;
            Key[][ ] pushes =
            {
                new[] { Key.D, Key.S },
                new[] { Key.D, Key.W },
                new[] { Key.D },
            };

            bool committed = false;
            for (int p = 0; p < pushes.Length && !committed; p++)
            {
                for (int i = 0; i < 60; i++)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(pushes[p]));
                    yield return null;

                    if (transitions.SlideCommittedCount > 0)
                    {
                        committed = true;
                        break;
                    }

                    worstX = Mathf.Max(worstX, player.transform.position.x);
                    Assert.Less(player.transform.position.x, outerX + 0.01f,
                        "<b>遷移していないのに境界より外へ出た</b>（x="
                        + player.transform.position.x + " / 境界の外面 x=" + outerX + "）。");
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;
            }

            // どちらでもよい——遷移したか、Area 内で止まったか。
            Assert.IsTrue(committed || worstX < outerX + 0.01f,
                "遷移が成立したか、Area 内で止まったかのどちらかである（遷移="
                + transitions.SlideCommittedCount + " 最も進んだ x=" + worstX + "）。");
        }

        /// <summary>
        /// <b>門を開けて離脱し、再入場しても、見た目と当たりがともに開通状態</b>
        /// （工程 P55-14c。GPT 受入 4）。
        ///
        /// <b>ゲームの導線だけで A を作り直させる。</b> B へスライドしてから
        /// <b>B の扉（Fade）を歩いて使う</b>と Single 読込になり、A が新しく読み直される。
        /// そこで活動ゲートの<b>初回 Open</b>が走る——工程 P55-14a で
        /// 「出荷状態を復元する」ようにした経路である。
        ///
        /// 出荷状態では門の Collider は<b>有効</b>（閉じた門だから）。
        /// 開通の記録は入場準備が反映するので、<b>Open のあとに無効へ戻る</b>のが正しい。
        /// 順序が崩れると「見た目は開いているのに通れない」になる。
        ///
        /// <b>通れることを歩いて確かめる。</b> 状態の値だけでは、
        /// 当たりが残っているかは言えない。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterOpeningTheGateLeavingAndComingBack_TheGateIsStillPassable()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            GameSessionState session = Sessions().Session;

            // ---- 1. 歩いてレバーを引く ----
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "レバーがある。");
            StableId gateFlag = lever.FlagId;

            yield return WalkTo(lever.InteractionAnchor, 1.1f, 25f, "開始点 → レバー");
            yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 6f);
            Assert.AreEqual(1, lever.OpenedCount, "門を開通させた。");

            // ---- 2. 歩いて B へ ----
            yield return WalkFromStartToB();
            AssertSettled(transitions, _route.AreaBId, "往路");

            // ---- 3. B の扉まで歩いて Fade（A が読み直される）----
            var door = Object.FindFirstObjectByType<AreaTransitionDoor>();
            Assert.IsNotNull(door, "B に A へ戻る扉がある。");

            int completedBefore = transitions.CompletedCount;
            yield return WalkTo(door.InteractionAnchor, 1.2f, 30f, "B の到着地点 → 扉");
            yield return PressKeyUntil(Key.E, () => transitions.CompletedCount > completedBefore, 25f);

            yield return WaitUntilOrTimeout(
                () => CurrentAreaProvider.Current != null
                      && CurrentAreaProvider.Current.AreaId.Value == _route.AreaAId.Value, 25f);
            Assert.AreEqual(_route.AreaAId.Value, CurrentAreaProvider.Current.AreaId.Value,
                "扉で A へ戻った（暗転経路）。");
            yield return null;

            // <b>枚数では言えない。</b> Single のあとも §5 の距離による先読みがすぐ隣を持つので、
            // 落ち着いた先はまた 2 枚になる。見たいのは「A が<b>新しく読み直された</b>」ことなので、
            // その Area の活動ゲートが<b>初回の Open</b>を通ったことで言う。
            var freshGate = Object.FindFirstObjectByType<AreaActivityGate>();
            Assert.IsNotNull(freshGate, "読み直した A に活動ゲートが居る。");
            Assert.IsFalse(freshGate.HasRestoreState,
                "閉める直前の記録を持っていない＝この Scene は新しく読まれた（保持していた A ではない）。");
            Assert.AreEqual(1, freshGate.OpenCount, "初回の Open を通っている。");

            // ---- 4. 見た目と当たりの両方 ----
            var reloaded = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(reloaded, "読み直した A にレバーがある。");
            Assert.IsTrue(session.TryGetArea(_route.AreaAId, out AreaRuntimeState areaA));
            Assert.IsTrue(areaA.IsOpen(gateFlag), "記録は開通したまま。");
            Assert.IsTrue(reloaded.Door.IsOpened, "<b>見た目</b>も開通している。");

            var doorBlocker = reloaded.Door.GetComponentInChildren<Collider>(true);
            if (doorBlocker != null)
            {
                Assert.IsFalse(doorBlocker.enabled,
                    "<b>当たりも開通している</b>（門の Collider が無効）。"
                    + " 有効なら「見た目は開いているのに通れない」になる。");
            }

            // ---- 5. 歩いてくぐれる ----
            var gate = Object.FindFirstObjectByType<AreaActivityGate>();
            Assert.IsNotNull(gate, "活動ゲートが居る。");
            Assert.AreEqual(0, gate.UniformOpenCount,
                "一律有効化へ落ちていない（落ちると門も塞ぎ直される）。");

            AreaExitGate exit = FindExitGate(_route.ExitFromA);
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            float doorX = reloaded.Door.transform.position.x;

            foreach (Vector3 waypoint in WaypointsAroundDivider(player.transform.position,
                         exit.transform.position))
            {
                yield return WalkTo(waypoint, 1.4f, 25f, "仕切りの抜け口へ");
            }

            // 門を越えた時点で止める（出口まで行くと遷移が成立して世界が止まる）。
            yield return WalkToUntil(exit.transform.position, 1.6f, 30f, "門をくぐって出口へ",
                () => player.transform.position.x > doorX + 0.8f);

            Assert.Greater(player.transform.position.x, doorX,
                "門をくぐれた（主人公 x=" + player.transform.position.x
                + " 門 x=" + doorX + "）。くぐれないなら当たりが残っている。");
        }

        /// <summary>
        /// <b>歩いてレバーを引き、仕切りの抜け口を通って B へ渡る</b>（工程 P55-14c）。
        ///
        /// 座標は一切書き換えない。Trigger を占有させたり遷移を直接要求したりもしない——
        /// <b>到着後の配置だけ</b>をゲーム自身の遷移が行う。
        ///
        /// <b>門を開けないと通路へ入れない。</b> 門（x≈9）は通路の入口を横に塞いでいて、
        /// 仕切りの抜け口を通ったあとに立ちはだかる。README が
        /// 「まず門を開けてください。レバーを引かないと境界へ行けません」と書いているとおり。
        /// </summary>
        private IEnumerator WalkFromStartToB()
        {
            AreaTransitionService transitions = Transitions();
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");

            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "レバーがある。");

            if (!lever.Door.IsOpened)
            {
                yield return WalkTo(lever.InteractionAnchor, 1.1f, 25f, "開始点 → レバー");
                yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 6f);
                Assert.AreEqual(1, lever.OpenedCount, "歩いてレバーを引けた。");
            }

            AreaExitGate exit = FindExitGate(_route.ExitFromA);
            Assert.IsNotNull(exit, "出入口がある。");

            foreach (Vector3 waypoint in WaypointsAroundDivider(
                         player.transform.position, exit.transform.position))
            {
                yield return WalkTo(waypoint, 1.4f, 25f, "仕切りの抜け口へ");
            }

            int before = transitions.SlideCommittedCount;
            yield return WalkToUntil(exit.transform.position, 1.6f, 30f, "抜け口 → 出口",
                () => transitions.SlideCommittedCount > before);
            yield return WaitUntilOrTimeout(
                () => transitions.SlideCommittedCount > before, 25f);

            Assert.Greater(transitions.SlideCommittedCount, before,
                "<b>歩いて行っただけで B へ渡れた</b>。失敗=" + transitions.Slide.LastFailure);
            yield return WaitUntilFreeToTravel();
        }

        // ================================================================ エリア内は連続追従（工程 P55-14d）

        /// <summary>
        /// <b>同一 Area 内では、どこを通ってもカメラの基準位置が切り替わらない</b>
        /// （裁定：同一エリア内は連続追従、エリア間だけスライド。工程 P55-14d。試遊報告②）。
        ///
        /// <b>試遊で報告された振る舞い。</b> 以前は A 内に「西の大部屋」「東の通路」「継ぎ目」の
        /// 3 領域が重なっていて、跨ぐたびに 0.15 秒の補間が走った。
        /// 門（x=9）と仕切りの抜け口（x=6）が、ちょうど西／東の境目のすぐ内側にあるので、
        /// 「門を開けてから通過すると画面スライドが発生する」ように見えていた。
        ///
        /// <b>旧領域の境目・門・出口への接近を、実キーで全部通る。</b> そのあいだ
        /// 領域の切替が 0 回で、補間も一度も始まらないことを<b>毎フレーム</b>見る。
        ///
        /// <b>「切り替わらない」だけでは足りない。</b> 追従が止まっていても切替は 0 回になる。
        /// だから<b>基準位置が主人公にぴたりと付いている</b>ことも見る——
        /// 領域をエリア全体＋半画面へ広げたので、clamp はエリア内では効かない。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingAcrossTheOldRoomBoundariesAndTheGate_TheCameraFollowsContinuously()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");
            Assert.IsFalse(host.Rig.Blend.IsBlending, "前提：補間は走っていない。");

            int changesBefore = host.Rig.Blend.RegionChangeCount;
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");

            // 毎フレームの見張りを仕掛ける（歩行の補助はフレームを進めるだけなので、
            // ここで Coroutine を別に回して見る）。
            var watcher = new CameraFollowWatcher(host, player, changesBefore);
            _cameraWatcher = watcher;

            try
            {
                // ---- 旧「西の大部屋」→ レバー ----
                var lever = Object.FindFirstObjectByType<AreaFlagLever>();
                Assert.IsNotNull(lever, "レバーがある。");
                yield return WalkTo(lever.InteractionAnchor, 1.1f, 25f, "開始点 → レバー");
                yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 6f);
                Assert.AreEqual(1, lever.OpenedCount, "門を開通させた。");

                // ---- 旧「西／東」の境目（仕切りの抜け口）→ 門 → 出口の手前 ----
                AreaExitGate exit = FindExitGate(_route.ExitFromA);
                foreach (Vector3 waypoint in WaypointsAroundDivider(
                             player.transform.position, exit.transform.position))
                {
                    yield return WalkTo(waypoint, 1.4f, 25f, "旧領域の境目を越える");
                }

                AreaTransitionService transitions = Transitions();
                yield return WalkToUntil(exit.transform.position, 1.6f, 30f, "門をくぐって出口へ",
                    () => transitions.SlideCommittedCount > 0);
            }
            finally
            {
                _cameraWatcher = null;
            }

            Assert.AreEqual(changesBefore, host.Rig.Blend.RegionChangeCount,
                "<b>領域の切替が一度も起きていない</b>（" + host.Rig.Blend.RegionChangeCount
                + " / 開始時 " + changesBefore + "）。門の通過・旧部屋境界・出口への接近では"
                + "カメラの補間演出を始めない（裁定）。");
            Assert.AreEqual(0, watcher.BlendFrames,
                "補間が走ったフレームが 1 つも無い（" + watcher.BlendFrames + " フレーム）。");
            Assert.Greater(watcher.Frames, 60, "十分な数のフレームを見ている（" + watcher.Frames + "）。");
            Assert.Less(watcher.WorstLag, 0.05f,
                "<b>エリア内では clamp が基準位置を寄せない</b>（最悪の寄せ " + watcher.WorstLag
                + " m・場所 " + watcher.WorstLagAt + "）。寄せているなら領域が狭い。");
            Assert.Greater(watcher.FocusTravel, 5f,
                "<b>基準位置は実際に動いている</b>（最大 " + watcher.FocusTravel
                + " m）。動いていないなら追従そのものが止まっている。");
        }

        /// <summary>
        /// <b>追従領域は「エリア全体＋見える範囲の半分」で、背景がその外側を覆う</b>
        /// （裁定の注意点 2。工程 P55-14d）。
        ///
        /// 領域をエリアと同じにすると <c>ClampFocus</c> が端でカメラを止める（＝追従が切れる）。
        /// 四方へ半画面ぶん広げると clamp が効かなくなる代わりに、
        /// <b>エリアの外が画面に入る</b>——そこは背景の補完（付録 C.33）が覆う。
        ///
        /// <b>拡張量は画面比・正射影サイズ・俯角から求める</b>（定数で信じない）。
        /// </summary>
        [UnityTest]
        public IEnumerator TheFollowRegion_LetsTheClampGoAndTheBackdropCoversTheOutside()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            var set = Object.FindFirstObjectByType<AreaCameraRegionSet>();
            Assert.IsNotNull(set, "カメラ領域集合がある。");
            Assert.IsNotNull(set.DefaultRegion, "既定領域がある。");
            Assert.AreEqual(0, set.Regions.Count,
                "<b>重ねる領域は 1 つも無い</b>（" + set.Regions.Count + " 個）。"
                + "継ぎ目領域も置かない（裁定の注意点 1）。");

            CameraRegionDefinition region = set.DefaultRegion.Definition;
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsTrue(host.Rig.TryGetHalfFootprint(out Vector2 half),
                "見える範囲の半分を求められる。");

            // エリアの広さは床の当たりから採る（決め打ちの定数を書かない）。
            Bounds floor = default;
            bool foundFloor = false;
            foreach (Collider c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c != null && c.gameObject.name == "Floor")
                {
                    floor = c.bounds;
                    foundFloor = true;
                    break;
                }
            }

            Assert.IsTrue(foundFloor, "床が見つかる（広さの正本）。");

            // <b>clamp が効かない</b>＝領域の内側の許容範囲が、エリアと同じか広い。
            float lowX = region.Min.x + half.x;
            float highX = region.Max.x - half.x;
            float lowZ = region.Min.y + half.y;
            float highZ = region.Max.y - half.y;

            Assert.LessOrEqual(lowX, floor.min.x + 0.01f,
                "西端でも clamp が効かない（許容 " + lowX + " ≤ 床 " + floor.min.x + "）。");
            Assert.GreaterOrEqual(highX, floor.max.x - 0.01f,
                "東端でも clamp が効かない（許容 " + highX + " ≥ 床 " + floor.max.x + "）。");
            Assert.LessOrEqual(lowZ, floor.min.z + 0.01f, "南端でも効かない。");
            Assert.GreaterOrEqual(highZ, floor.max.z - 0.01f, "北端でも効かない。");

            // <b>背景がその外側を覆う。</b> 背景の当たりは持たないので、Renderer の範囲で見る。
            Bounds backdrop = default;
            bool foundBackdrop = false;
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r != null && r.gameObject.name == "Backdrop")
                {
                    backdrop = r.bounds;
                    foundBackdrop = true;
                    break;
                }
            }

            Assert.IsTrue(foundBackdrop, "背景面がある（付録 C.33）。");
            Assert.LessOrEqual(backdrop.min.x, floor.min.x - half.x,
                "背景が西へ半画面ぶん以上はみ出している（背景 " + backdrop.min.x
                + " ≤ 床 " + floor.min.x + " − " + half.x + "）。カメラが端へ寄っても虚空が出ない。");
            Assert.GreaterOrEqual(backdrop.max.x, floor.max.x + half.x, "東も同じ。");
            Assert.LessOrEqual(backdrop.min.z, floor.min.z - half.y, "南も同じ（奥行は俯角で伸びる）。");
            Assert.GreaterOrEqual(backdrop.max.z, floor.max.z + half.y, "北も同じ。");
        }

        /// <summary>毎フレーム、追従のずれと補間の有無を見る（工程 P55-14d）。</summary>
        private sealed class CameraFollowWatcher
        {
            private readonly AreaCameraRigHost _host;
            private readonly PlayerRoot _player;

            internal CameraFollowWatcher(AreaCameraRigHost host, PlayerRoot player, int changesBefore)
            {
                _host = host;
                _player = player;
                ChangesBefore = changesBefore;
                _startedFocus = host != null ? host.Rig.Blend.Current : Vector3.zero;
            }

            internal int ChangesBefore { get; }

            internal int Frames { get; private set; }

            internal int BlendFrames { get; private set; }

            internal float WorstLag { get; private set; }

            internal Vector3 WorstLagAt { get; private set; }

            /// <summary>基準位置が実際に動いた最大距離（追従が止まっていないこと）。</summary>
            internal float FocusTravel { get; private set; }

            private readonly Vector3 _startedFocus;

            internal void Observe()
            {
                if (_host == null || _player == null)
                {
                    return;
                }

                Frames++;
                if (_host.Rig.Blend.IsBlending)
                {
                    BlendFrames++;
                }

                Vector3 at = _player.transform.position;

                // <b>clamp が効いていないことは純粋計算で見る。</b>
                //
                // 基準位置と主人公の現在位置を直接くらべると、<b>1 フレームぶんの順序差</b>が
                // 混ざる（カメラが読むのは主人公が動く前の位置なので、速度 × dt だけ遅れる）。
                // 実際それで 0.52 m のずれが出て、clamp と見分けが付かなかった。
                //
                // 見たいのは「エリア内では clamp が寄せない」ことなので、
                // <c>ClampFocus</c> に主人公の位置を通して<b>寄せられないこと</b>を見る。
                // 実行時と同じ純粋関数なので、別の期待値を作らない。
                if (_host.Rig.TryGetHalfFootprint(out Vector2 half)
                    && _host.Rig.TryComputeFocus(out Vector3 wanted))
                {
                    float pull = Vector2.Distance(
                        new Vector2(wanted.x, wanted.z), new Vector2(at.x, at.z));
                    if (pull > WorstLag)
                    {
                        WorstLag = pull;
                        WorstLagAt = at;
                    }
                }

                Vector3 focus = _host.Rig.Blend.Current;
                float moved = Vector2.Distance(
                    new Vector2(focus.x, focus.z), new Vector2(_startedFocus.x, _startedFocus.z));
                if (moved > FocusTravel)
                {
                    FocusTravel = moved;
                }
            }
        }

        private CameraFollowWatcher _cameraWatcher;

        // ================================================================ 実ステップ回避（14b の補完）

        /// <summary>
        /// <b>実際のステップ回避で接続口へ突っ込んでも、未遷移のまま境界の外へ出ない</b>
        /// （試遊報告①「ダッシュ等で『B へ』のオブジェクトを踏まずに通過する」。
        /// 工程 P55-14d で 14b を補完。GPT 指摘）。
        ///
        /// <b>14b の検査は歩行と斜め移動しか作っていなかった。</b> 報告された現象は
        /// <b>ステップ回避</b>——1 回で約 3m を短い時間で移動するので、
        /// 出口 Trigger の中に居るフレーム数が歩行よりずっと少ない。
        /// 「0.15 秒の入力が足りない」だけが原因なら、<b>止める物が無ければ通り抜ける</b>。
        ///
        /// <b>止めるのは見えない境界</b>（<see cref="AreaSeamBarrier"/>。付録 C.38）である。
        /// ここでは正面（東）と斜め（北東・南東）から、<b>実キーの Space</b> で連続して
        /// 突っ込み、毎フレーム「境界の外に居ない」ことを見る。
        ///
        /// <b>「遷移しない」を求めてはいない。</b> 突っ込んだ結果スライドが成立するのは正常で、
        /// 許されないのは<b>遷移していないのに向こう側へ出ている</b>ことだけである。
        /// </summary>
        [UnityTest]
        public IEnumerator DodgingIntoTheSeam_NeverLeavesTheAreaUntransitioned()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");

            Assert.IsTrue(TryFindAreaRoot(_route.AreaAId, out AreaRoot areaRoot), "A の根を引ける。");
            Assert.AreEqual(1, areaRoot.SeamBarriers.Count, "前提：接続口に境界がある。");
            float outerX = areaRoot.SeamBarriers[0].Blocker.bounds.max.x;

            AreaExitGate exit = FindExitGate(_route.ExitFromA);
            Vector3 gateAt = exit.transform.position;

            // <b>門を開けてから通路へ入る。</b> 開けないと通路へ辿り着けない（付録 C.39）。
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "レバーがある。");
            yield return WalkTo(lever.InteractionAnchor, 1.1f, 25f, "開始点 → レバー");
            yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 6f);
            Assert.AreEqual(1, lever.OpenedCount, "門を開通させた。");

            foreach (Vector3 waypoint in WaypointsAroundDivider(player.transform.position, gateAt))
            {
                yield return WalkTo(waypoint, 1.4f, 25f, "仕切りの抜け口へ");
            }

            // 通路の手前・少し離れた位置から助走をつけて突っ込む。
            Vector3 runUp = gateAt + new Vector3(-3.5f, 0f, 0f);
            yield return WalkToUntil(runUp, 1.4f, 25f, "通路の手前へ",
                () => transitions.SlideCommittedCount > 0);

            // 正面・北東・南東。ステップは押した瞬間に消費されるので、押して離すを繰り返す。
            Key[][] directions =
            {
                new[] { Key.D },
                new[] { Key.D, Key.W },
                new[] { Key.D, Key.S },
            };

            // <b>回避が実際に発動したことを見る。</b> Space を送っただけでは受入にならない——
            // 入力が届いていない構成でも「境界を越えなかった」は成り立ってしまう。
            var state = Object.FindFirstObjectByType<PlayerStateController>();
            Assert.IsNotNull(state, "主人公の状態機がある。");

            float worstX = player.transform.position.x;
            float fastestStep = 0f;
            int dodges = 0;
            int steppingFrames = 0;
            bool committed = transitions.SlideCommittedCount > 0;

            for (int d = 0; d < directions.Length && !committed; d++)
            {
                for (int burst = 0; burst < 6 && !committed; burst++)
                {
                    var withStep = new System.Collections.Generic.List<Key>(directions[d])
                        { Key.Space };

                    // 押す（この 1 フレームで回避が始まる）。
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(withStep.ToArray()));
                    yield return null;
                    dodges++;

                    // 回避が走っている間は向きだけ押し続ける（Space は離して次を溜めない）。
                    Vector3 previous = player.transform.position;
                    for (int i = 0; i < 30; i++)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(directions[d]));
                        yield return null;

                        if (state.IsStepping)
                        {
                            steppingFrames++;
                        }

                        Vector3 now = player.transform.position;
                        fastestStep = Mathf.Max(fastestStep,
                            Vector2.Distance(new Vector2(now.x, now.z),
                                new Vector2(previous.x, previous.z)));
                        previous = now;

                        if (transitions.SlideCommittedCount > 0)
                        {
                            committed = true;
                            break;
                        }

                        worstX = Mathf.Max(worstX, player.transform.position.x);
                        Assert.Less(player.transform.position.x, outerX + 0.01f,
                            "<b>回避で境界を越えた</b>（x=" + player.transform.position.x
                            + " / 境界の外面 x=" + outerX + "・" + dodges + " 回目の回避・"
                            + (d == 0 ? "正面" : d == 1 ? "北東" : "南東")
                            + "）。試遊報告①の現象である。");
                    }
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.Greater(dodges, 0, "回避を実際に入力した（" + dodges + " 回）。");
            Assert.Greater(steppingFrames, 0,
                "<b>回避が実際に発動した</b>（Stepping だったフレーム " + steppingFrames
                + "）。0 なら Space が届いていないので、この検査は何も見ていない。");
            Assert.Greater(fastestStep, 0f,
                "回避中に主人公が動いた（1 フレームの最大移動 " + fastestStep + " m）。");
            Assert.IsTrue(committed || worstX < outerX + 0.01f,
                "遷移が成立したか、Area 内で止まったかのどちらかである（遷移="
                + transitions.SlideCommittedCount + " 最も進んだ x=" + worstX
                + " / 境界の外面 x=" + outerX + "・回避 " + dodges + " 回）。");
        }

        // ================================ 読み込みは遷移の受理だけ（工程 P55-15c。読み込み方針の裁定）

        /// <summary>
        /// <b>エリア内を歩き回っても、ロードも解放も一度も起きない</b>
        /// （工程 P55-15c。試遊報告②。GPT 作業指示 4・6）。
        ///
        /// 試遊で「エリア内を歩き回っていると時折ゲームが一瞬止まり、ロードが行われたような挙動」
        /// と報告された。裁定は<b>接近ではロードせず、初めてそのエリアへ遷移するときにロードする</b>
        /// ——待ち時間を<b>プレイヤーが理解できる場所へまとめる</b>ためである。
        ///
        /// <b>歩くのは実キーで、座標は変えない</b>（付録 C.39）。
        /// レバーを引き、仕切りを回り、通路へ入り、また戻る——
        /// <b>以前なら先読みの境目（6 units）を何度もまたぐ道のり</b>である。
        ///
        /// <b>フレーム時間も一緒に測る</b>（GPT 作業指示 6）。
        /// 「ロード 0 回」と「止まらない」は別のことなので、両方を数で残す——
        /// 残る停止があれば、それはロード以外の原因である。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingInsideTheArea_NeverLoadsOrReleasesAnything()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            AreaPreloader preloader = transitions.Slide.Preloader;

            // 落ち着かせてから数え始める。
            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }

            int loads0 = preloader.LoadStartedCount;
            int releases0 = preloader.ReleaseStartedCount;
            int requests0 = transitions.Slide.PreloadRequestCount;
            int scenes0 = SceneManager.sceneCount;

            Assert.AreEqual(1, scenes0,
                "前提：まだ隣は載っていない（初訪問の前）。" );
            Assert.AreEqual(AreaPreloadPhase.Idle, preloader.Phase,
                "前提：先読みは何も抱えていない。");

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が居る。");

            // ---- 以前なら先読みの境目をまたぐ道のりを歩く ----
            var lever = Object.FindFirstObjectByType<AreaFlagLever>();
            Assert.IsNotNull(lever, "レバーがある。");
            yield return WalkTo(lever.InteractionAnchor, 1.1f, 25f, "開始点 → レバー");
            yield return PressKeyUntil(Key.E, () => lever.OpenedCount >= 1, 6f);

            AreaExitGate exit = FindExitGate(_route.ExitFromA);
            Vector3 gateAt = exit.transform.position;

            foreach (Vector3 waypoint in WaypointsAroundDivider(player.transform.position, gateAt))
            {
                yield return WalkTo(waypoint, 1.4f, 25f, "仕切りの抜け口へ");
            }

            // 出入口の手前まで寄って（以前はここで読み始めた）、また仕切りの向こうへ離れる。
            //
            // <b>直線では戻れない</b>——仕切り壁があるので、抜け口を回る
            // （付録 C.39 の「寄り道の目標は座標変更ではない」）。
            Vector3 nearSeam = gateAt + new Vector3(-3f, 0f, 0f);
            Vector3 farFromSeam = lever.InteractionAnchor;
            for (int lap = 0; lap < 2; lap++)
            {
                foreach (Vector3 waypoint in WaypointsAroundDivider(
                             player.transform.position, nearSeam))
                {
                    yield return WalkTo(waypoint, 1.4f, 25f, "境界へ寄る（抜け口）");
                }

                yield return WalkToUntil(nearSeam, 1.4f, 25f, "境界へ寄る",
                    () => transitions.SlideCommittedCount > 0);

                foreach (Vector3 waypoint in WaypointsAroundDivider(
                             player.transform.position, farFromSeam))
                {
                    yield return WalkTo(waypoint, 1.4f, 25f, "離れる（抜け口）");
                }

                yield return WalkToUntil(farFromSeam, 1.6f, 25f, "境界から離れる",
                    () => transitions.SlideCommittedCount > 0);
            }

            Assert.AreEqual(0, transitions.SlideCommittedCount,
                "前提：この間に遷移していない（歩いただけ）。");

            // ---- 数える ----
            Assert.AreEqual(loads0, preloader.LoadStartedCount,
                "<b>歩いている間にロードを始めていない</b>（この間 "
                + (preloader.LoadStartedCount - loads0) + " 回）。試遊報告②の場所である。");
            Assert.AreEqual(releases0, preloader.ReleaseStartedCount,
                "<b>解放も始めていない</b>（この間 "
                + (preloader.ReleaseStartedCount - releases0) + " 回）。");
            Assert.AreEqual(requests0, transitions.Slide.PreloadRequestCount,
                "<b>読込を一度も頼んでいない</b>（この間 "
                + (transitions.Slide.PreloadRequestCount - requests0) + " 回）。");
            Assert.AreEqual(scenes0, SceneManager.sceneCount,
                "<b>Scene の枚数が変わっていない</b>（" + scenes0 + " → "
                + SceneManager.sceneCount + " 枚）。");
            Assert.AreEqual(AreaPreloadPhase.Idle, preloader.Phase,
                "先読みは何も抱えないまま（" + preloader.Phase + "）。");
        }

        /// <summary>
        /// <b>初訪問で 1 回だけ読み、往復では読み直さない</b>
        /// （工程 P55-15c。裁定の「ロード済みエリアへの再移動は保持した Scene を再利用する」）。
        ///
        /// <b>歩いて渡る</b>——置き直しでは「そこへ行ける」も「読みに行く時機」も見ていない。
        /// 数えるのは<b>実 Scene のロード開始</b>で、望みの回数ではない。
        /// </summary>
        [UnityTest]
        public IEnumerator TheFirstTravelLoadsOnce_AndTheRoundTripReusesIt()
        {
            _route = RouteOf("EastWest");
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            AreaPreloader preloader = transitions.Slide.Preloader;

            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }

            int loads0 = preloader.LoadStartedCount;

            // ---- 歩いて B へ（ここで初めて読む）----
            yield return WalkFromStartToB();
            Assert.AreEqual(1, transitions.SlideCommittedCount, "B へ渡った。");

            int loadsAfterFirst = preloader.LoadStartedCount;
            Assert.AreEqual(loads0 + 1, loadsAfterFirst,
                "<b>初訪問でちょうど 1 回読んだ</b>（" + (loadsAfterFirst - loads0) + " 回）。");

            // ---- 戻る・また行く：読み直さない ----
            yield return WalkBackAndForthOnce(transitions);

            Assert.AreEqual(3, transitions.SlideCommittedCount,
                "3 回渡った（A→B→A→B）。失敗=" + transitions.Slide.LastFailure);
            Assert.AreEqual(loadsAfterFirst, preloader.LoadStartedCount,
                "<b>往復では一度も読み直していない</b>（この間 "
                + (preloader.LoadStartedCount - loadsAfterFirst) + " 回）。"
                + " 保持した Scene を再利用している（裁定）。");
        }

        /// <summary>B→A→B と歩いて往復する（座標は変えない）。</summary>
        private IEnumerator WalkBackAndForthOnce(AreaTransitionService transitions)
        {
            int before = transitions.SlideCommittedCount;

            // <b>半径は小さく採る。</b> 1.6 だと出入口の Trigger（奥行 1.6）へ入る前に
            // 「着いた」ことになり、遷移しないまま戻ってしまう。
            AreaExitGate back = FindExitGate(_route.ExitFromB);
            yield return WalkToUntil(back.transform.position, 0.4f, 30f, "B → A",
                () => transitions.SlideCommittedCount > before);
            yield return WaitUntilFreeToTravel();

            int mid = transitions.SlideCommittedCount;
            AreaExitGate forward = FindExitGate(_route.ExitFromA);
            yield return WalkToUntil(forward.transform.position, 0.4f, 30f, "A → B",
                () => transitions.SlideCommittedCount > mid);
            yield return WaitUntilFreeToTravel();
        }

        // ---------------------------------------------------------------- 歩く（座標を変えない）

        /// <summary>
        /// <b>実キーだけで目標へ歩く</b>（工程 P55-14c）。座標は一切書き換えない。
        ///
        /// 着かなければ失敗させる。メッセージに出発点・現在地・目標・残り距離を入れるのは、
        /// <b>何に止められたか</b>を読めるようにするためである（試遊報告の 2 件はどちらも
        /// 「見えない壁に止められていた」形だった）。
        /// </summary>
        private IEnumerator WalkTo(Vector3 target, float radius, float seconds, string label) =>
            WalkToUntil(target, radius, seconds, label, null);

        /// <summary>
        /// 目標へ歩く。<paramref name="until"/> が真になったらそこで止める
        /// （遭遇のように「着く前に起きること」を待つため）。
        /// </summary>
        private IEnumerator WalkToUntil(
            Vector3 target, float radius, float seconds, string label, System.Func<bool> until)
        {
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, label + "：主人公が居る。");

            Vector3 from = player.transform.position;
            float deadline = Time.realtimeSinceStartup + seconds;
            float closest = Flat(from, target);
            Vector3 stuckAt = from;
            float stuckFor = 0f;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (until != null && until())
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                    yield return null;
                    yield break;
                }

                Vector3 at = player.transform.position;
                float distance = Flat(at, target);
                closest = Mathf.Min(closest, distance);
                if (distance <= radius)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                    yield return null;
                    yield break;
                }

                // 止まったままの時間を数える（何に止められたかを言えるように）。
                if (Flat(at, stuckAt) < 0.05f)
                {
                    stuckFor += Time.unscaledDeltaTime;
                }
                else
                {
                    stuckAt = at;
                    stuckFor = 0f;
                }

                var keys = new System.Collections.Generic.List<Key>(2);
                float dx = target.x - at.x;
                float dz = target.z - at.z;
                if (dz > 0.35f) { keys.Add(Key.W); }
                else if (dz < -0.35f) { keys.Add(Key.S); }
                if (dx > 0.35f) { keys.Add(Key.D); }
                else if (dx < -0.35f) { keys.Add(Key.A); }

                InputSystem.QueueStateEvent(_keyboard, keys.Count == 0
                    ? new KeyboardState()
                    : keys.Count == 1 ? new KeyboardState(keys[0]) : new KeyboardState(keys[0], keys[1]));
                yield return null;
                _cameraWatcher?.Observe();
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            if (until != null && until())
            {
                yield break;
            }

            Vector3 endedAt = player.transform.position;
            Assert.Fail(label + "：歩いて着けませんでした。出発=" + from + " 現在=" + endedAt
                + " 目標=" + target + " 残り=" + Flat(endedAt, target)
                + " 最接近=" + closest + " 同じ場所に留まった時間=" + stuckFor + " 秒。"
                + " 何かに止められている可能性があります（見えない当たり・封鎖・門）。");
        }

        private static float Flat(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>
        /// 仕切り壁の抜け口を<b>壁の実際の当たりから</b>割り出して、そこを通る目標にする。
        ///
        /// 決め打ちの座標を書かないので、配置を作り直しても壊れない。
        /// 目標と同じ側に居るなら寄り道は要らない。
        /// </summary>
        private static System.Collections.Generic.IEnumerable<Vector3> WaypointsAroundDivider(
            Vector3 from, Vector3 to)
        {
            GameObject divider = null;
            foreach (Collider c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c != null && c.gameObject.name == "Wall_Divider")
                {
                    divider = c.gameObject;
                    break;
                }
            }

            if (divider == null)
            {
                yield break; // 仕切りが無い配置なら寄り道も要らない。
            }

            Bounds b = divider.GetComponent<Collider>().bounds;
            bool crossing = (from.x < b.center.x) != (to.x < b.center.x);
            if (!crossing)
            {
                yield break;
            }

            // 抜け口は壁の南側（当たりの min.z より手前）。壁の手前・向こう側の 2 点を通る。
            float gapZ = b.min.z - 1.5f;
            yield return new Vector3(b.center.x - 2.0f, from.y, gapZ);
            yield return new Vector3(b.center.x + 2.0f, from.y, gapZ);
        }

        /// <summary>その Area の根を引く（束の索引から）。</summary>
        private static bool TryFindAreaRoot(StableId areaId, out AreaRoot root)
        {
            foreach (AreaRoot candidate in Object.FindObjectsByType<AreaRoot>(FindObjectsSortMode.None))
            {
                if (candidate != null && candidate.AreaId.Value == areaId.Value)
                {
                    root = candidate;
                    return true;
                }
            }

            root = null;
            return false;
        }

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
