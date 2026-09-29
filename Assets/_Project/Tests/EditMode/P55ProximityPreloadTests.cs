using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// <b>距離による先読みの選定</b>（P5.5 仕様書 §5 の 1〜3 行目。工程 P55-08b）。
    ///
    /// §5 が定めるのは 3 つ。
    /// <list type="bullet">
    /// <item><description>出入口の 6 units 以内で候補にし、<b>距離 → ConnectionId の Ordinal 順</b>で選ぶ。</description></item>
    /// <item><description>一度読み始めた先は、<b>同じ Area に居る間は境界から離れても保持</b>する。</description></item>
    /// <item><description>別候補への切替は<b>距離差 2 units 以上が 0.5 秒継続</b>したときだけ、
    /// <b>旧先読みの終端・解放後</b>に行う。</description></item>
    /// </list>
    ///
    /// <b>実 Scene では見分けにくい。</b> ヒステリシス（2 units・0.5 秒）は時間の絡む判断なので、
    /// 実遷移で確かめようとすると Scene ロードの束になり、しかも
    /// 「読み直さなかったのは保持のおかげか、たまたま同じ先だったのか」が言えない。
    /// 判断だけを取り出して刻みを与える。実 Scene での距離先読みは PlayMode で見る。
    /// </summary>
    public sealed class P55ProximityPreloadTests
    {
        private static readonly StableId AreaHere = new StableId("area_here");
        private static readonly StableId AreaEast = new StableId("area_east");
        private static readonly StableId AreaWest = new StableId("area_west");
        private static readonly StableId ConnEast = new StableId("conn_east");
        private static readonly StableId ConnWest = new StableId("conn_west");

        private const string EastPath = "Assets/East.unity";
        private const string WestPath = "Assets/West.unity";
        private const float Range = 6f;

        private static AreaPreloadCandidate East(float distance) =>
            new AreaPreloadCandidate(ConnEast, AreaEast, EastPath, distance, Range);

        private static AreaPreloadCandidate West(float distance) =>
            new AreaPreloadCandidate(ConnWest, AreaWest, WestPath, distance, Range);

        private static AreaPreloadCandidate WestWithRange(float distance, float preloadDistance) =>
            new AreaPreloadCandidate(ConnWest, AreaWest, WestPath, distance, preloadDistance);

        private static List<AreaPreloadCandidate> Only(AreaPreloadCandidate c) =>
            new List<AreaPreloadCandidate> { c };

        private static List<AreaPreloadCandidate> Both(
            AreaPreloadCandidate a, AreaPreloadCandidate b) =>
            new List<AreaPreloadCandidate> { a, b };

        /// <summary>
        /// 範囲の外では何も望まない。<b>「近づいたら読む」であって「常に読む」ではない</b>——
        /// 常に読むと、出入口から遠い場所でも在留枠が埋まったままになる。
        /// </summary>
        [Test]
        public void OutsideTheRange_NothingIsWanted()
        {
            var planner = new AreaProximityPreloadPlanner();

            Assert.IsFalse(
                planner.Tick(AreaHere, Only(East(6.01f)), 0.016f, true, out _, out _),
                "6 units より遠ければ望まない。");
            Assert.AreEqual(0, planner.StartedCount, "始めてもいない。");
            Assert.IsFalse(planner.HeldAreaId.IsValid, "保持もしていない。");
        }

        /// <summary>範囲に入った瞬間に望む（境界そのものは範囲に含む）。</summary>
        [Test]
        public void EnteringTheRange_StartsThePreload()
        {
            var planner = new AreaProximityPreloadPlanner();

            Assert.IsTrue(
                planner.Tick(AreaHere, Only(East(6f)), 0.016f, true,
                    out StableId area, out string path),
                "6 units ちょうどは範囲の内側。");
            Assert.AreEqual(AreaEast.Value, area.Value, "望むのは東の先。");
            Assert.AreEqual(EastPath, path, "Scene も一緒に渡す。");
            Assert.AreEqual(1, planner.StartedCount, "始めた回数は 1。");
        }

        /// <summary>
        /// 同じ距離なら <b>ConnectionId の Ordinal 順</b>で決める（§5）。
        ///
        /// ここを決めておかないと、<b>フレームごとに別の候補を選びうる</b>。
        /// 候補が一つしかない P5.5 実試遊では現れないが、増やした瞬間に
        /// 毎フレーム読み直す形になる。
        /// </summary>
        [Test]
        public void AtTheSameDistance_TheOrdinalOrderDecides()
        {
            var planner = new AreaProximityPreloadPlanner();

            // conn_east < conn_west（Ordinal）。並べる順を変えても答えは変わらない。
            Assert.IsTrue(
                planner.Tick(AreaHere, Both(West(3f), East(3f)), 0.016f, true,
                    out StableId area, out _),
                "範囲内の候補がある。");
            Assert.AreEqual(AreaEast.Value, area.Value,
                "同距離なら ConnectionId の Ordinal 順で若いほうを選ぶ。");
        }

        /// <summary>近いほうを選ぶ（順序より距離が先）。</summary>
        [Test]
        public void TheNearerCandidate_WinsOverTheOrdinalOrder()
        {
            var planner = new AreaProximityPreloadPlanner();

            Assert.IsTrue(
                planner.Tick(AreaHere, Both(East(5f), West(1f)), 0.016f, true,
                    out StableId area, out _),
                "範囲内の候補がある。");
            Assert.AreEqual(AreaWest.Value, area.Value, "近いほうを選ぶ。");
        }

        /// <summary>
        /// <b>一度読み始めた先は、境界から離れても保持する</b>（§5 の 2 行目）。
        ///
        /// 捨てると、出入口の前を行ったり来たりするだけで<b>ロードと unload を反復</b>する。
        /// </summary>
        [Test]
        public void OnceStarted_ItIsKeptEvenFarFromTheSeam()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.016f, true, out _, out _);

            Assert.IsTrue(
                planner.Tick(AreaHere, Only(East(20f)), 0.016f, true, out StableId area, out _),
                "範囲の外へ出ても望みは消えない。");
            Assert.AreEqual(AreaEast.Value, area.Value, "保持しているのは同じ先。");
            Assert.Greater(planner.HeldOutOfRangeCount, 0, "範囲外で保持したことを数えている。");
            Assert.AreEqual(1, planner.StartedCount, "読み直していない。");
        }

        /// <summary>
        /// 切替は<b>距離差 2 units 以上が 0.5 秒継続</b>したときだけ（§5 の 3 行目）。
        ///
        /// 継続を求めるのは、境界の前で立ち止まった主人公が<b>わずかな揺れで先を切り替える</b>のを
        /// 防ぐため。差が足りているだけでは切り替えない。
        /// </summary>
        [Test]
        public void SwitchingNeedsTwoUnitsHeldForHalfASecond()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.1f, true, out _, out _);

            // 差はちょうど 2（5 − 3）。0.4 秒までは切り替えない。
            List<AreaPreloadCandidate> both = Both(East(5f), West(3f));
            for (int i = 0; i < 4; i++)
            {
                planner.Tick(AreaHere, both, 0.1f, true, out StableId waiting, out _);
                Assert.AreEqual(AreaEast.Value, waiting.Value,
                    "0.5 秒に届くまでは保持したまま（" + ((i + 1) * 0.1f) + " 秒）。");
                Assert.AreEqual(0, planner.SwitchedCount, "まだ切り替えていない。");
            }

            planner.Tick(AreaHere, both, 0.1f, true, out StableId switched, out string path);
            Assert.AreEqual(AreaWest.Value, switched.Value, "0.5 秒続いたので切り替える。");
            Assert.AreEqual(WestPath, path, "Scene も切り替わる。");
            Assert.AreEqual(1, planner.SwitchedCount, "切替は 1 回。");
        }

        /// <summary>差が 2 units に届かなければ、いつまで続いても切り替えない。</summary>
        [Test]
        public void ADifferenceUnderTwoUnits_NeverSwitches()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.1f, true, out _, out _);

            List<AreaPreloadCandidate> both = Both(East(5f), West(3.01f));
            for (int i = 0; i < 30; i++)
            {
                planner.Tick(AreaHere, both, 0.1f, true, out StableId area, out _);
                Assert.AreEqual(AreaEast.Value, area.Value, "差が 2 未満なら切り替えない。");
            }

            Assert.AreEqual(0, planner.SwitchedCount, "3 秒続いても切り替えない。");
        }

        /// <summary>
        /// 差が一度でも足りなくなれば、<b>継続時間は 0 に戻る</b>。
        /// 戻さないと「合計 0.5 秒」で切り替わり、揺れを弾く意味が無くなる。
        /// </summary>
        [Test]
        public void TheHoldTimeResets_WhenTheDifferenceDropsBack()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.1f, true, out _, out _);

            for (int i = 0; i < 4; i++)
            {
                planner.Tick(AreaHere, Both(East(5f), West(3f)), 0.1f, true, out _, out _);
            }

            Assert.Greater(planner.PendingSeconds, 0.3f, "前提：あと少しまで溜まっている。");

            // 差が縮んだ 1 フレーム。
            planner.Tick(AreaHere, Both(East(5f), West(4f)), 0.1f, true, out _, out _);
            Assert.AreEqual(0f, planner.PendingSeconds, "溜めは 0 へ戻る。");

            for (int i = 0; i < 4; i++)
            {
                planner.Tick(AreaHere, Both(East(5f), West(3f)), 0.1f, true, out StableId area, out _);
                Assert.AreEqual(AreaEast.Value, area.Value, "数え直しなのでまだ切り替わらない。");
            }

            Assert.AreEqual(0, planner.SwitchedCount, "溜め直しの途中では切り替えない。");
        }

        /// <summary>
        /// <b>旧先読みの終端・解放後にしか切り替えない</b>（§5 の 3 行目）。
        ///
        /// 終端していない操作の上へ次の読込を重ねると、Scene 操作が二重に走る
        /// （付録 C.22.1 と同じ規律）。条件が揃っていても、終端していなければ<b>待つ</b>——
        /// 待つのであって<b>捨てない</b>ので、終端した次のフレームに切り替わる。
        /// </summary>
        [Test]
        public void WhileTheOldPreloadIsStillRunning_TheSwitchWaits()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.1f, true, out _, out _);

            List<AreaPreloadCandidate> both = Both(East(5f), West(3f));
            for (int i = 0; i < 10; i++)
            {
                planner.Tick(AreaHere, both, 0.1f, false, out StableId area, out _);
                Assert.AreEqual(AreaEast.Value, area.Value,
                    "終端していないので切り替えない（" + i + " フレーム目）。");
            }

            Assert.AreEqual(0, planner.SwitchedCount, "切り替えていない。");
            Assert.Greater(planner.SwitchDeferredCount, 0, "見送った回数を数えている。");

            planner.Tick(AreaHere, both, 0.1f, true, out StableId after, out _);
            Assert.AreEqual(AreaWest.Value, after.Value, "終端したら切り替わる（条件は捨てていない）。");
            Assert.AreEqual(1, planner.SwitchedCount, "切替は 1 回。");
        }

        /// <summary>
        /// 活動 Area が変われば保持を捨てる。
        ///
        /// 「同じ Area で活動している間は保持する」（§5）の裏返しで、
        /// 別の Area へ移ったら前の Area の出入口からの距離には意味が無い。
        /// 捨てないと、<b>いま居ない Area の隣</b>を抱えたままになる。
        /// </summary>
        [Test]
        public void MovingToAnotherArea_ForgetsWhatWasHeld()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.1f, true, out _, out _);
            Assert.AreEqual(AreaEast.Value, planner.HeldAreaId.Value, "前提：保持している。");

            Assert.IsFalse(
                planner.Tick(AreaEast, new List<AreaPreloadCandidate>(), 0.1f, true, out _, out _),
                "移った先で候補が無ければ何も望まない。");
            Assert.AreEqual(1, planner.ResetCount, "保持を捨てたことを数えている。");
            Assert.IsFalse(planner.HeldAreaId.IsValid, "抱えたままにしない。");
        }

        /// <summary>
        /// <b>開始距離は候補ごとに違う</b>（工程 P55-08c。GPT 再修正の指摘）。
        ///
        /// 先に最寄りを選んでから 1 件だけ距離を見ると、<b>まだ範囲外の近い候補</b>が
        /// <b>範囲内の遠い候補</b>を遮る。絞ってから順位付けする。
        /// </summary>
        [Test]
        public void ACloserButOutOfRangeCandidate_DoesNotBlockAnEligibleOne()
        {
            var planner = new AreaProximityPreloadPlanner();

            // 西：距離 3・開始距離 2（近いが、まだ範囲の外）。
            // 東：距離 4・開始距離 6（遠いが、範囲の内）。
            Assert.IsTrue(
                planner.Tick(AreaHere, Both(East(4f), WestWithRange(3f, 2f)), 0.016f, true,
                    out StableId area, out _),
                "範囲内の候補があるなら望む。");
            Assert.AreEqual(AreaEast.Value, area.Value,
                "近いが範囲外の候補は、範囲内の候補を遮らない。");
        }

        /// <summary>
        /// <b>取り下げられた先は、自動では選び直さない</b>（工程 P55-08c。GPT 再修正②）。
        ///
        /// 遷移が先読みを取り下げた（Rollback・時間切れ・Single 読込前の受け渡し）とき、
        /// <b>その場に立っているだけで同じ先を言い直してはいけない</b>。
        /// 言い直すと、遅れて着いた Scene が「また必要な先読み」になって撤去されない。
        /// </summary>
        [Test]
        public void ASuppressedCandidate_IsNotChosenAgainOnItsOwn()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.016f, true, out _, out _);

            planner.SuppressHeld();
            Assert.AreEqual(ConnEast.Value, planner.SuppressedConnectionId.Value,
                "取り下げた接続を覚えている。");
            Assert.AreEqual(1, planner.SuppressedCount, "抑止した回数を数えている。");

            for (int i = 0; i < 30; i++)
            {
                Assert.IsFalse(
                    planner.Tick(AreaHere, Only(East(2f)), 0.016f, true, out _, out _),
                    "範囲の内に居ても、抑止した先は選び直さない（" + i + " フレーム目）。");
            }

            Assert.AreEqual(1, planner.StartedCount, "始めた回数は増えない。");
        }

        /// <summary>抑止しても、<b>別の候補</b>は始められる（止めるのは取り下げた先だけ）。</summary>
        [Test]
        public void SuppressingOneCandidate_DoesNotBlockTheOthers()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.016f, true, out _, out _);
            planner.SuppressHeld();

            Assert.IsTrue(
                planner.Tick(AreaHere, Both(East(2f), West(3f)), 0.016f, true,
                    out StableId area, out _),
                "別の候補は望める。");
            Assert.AreEqual(AreaWest.Value, area.Value, "選ぶのは抑止していない側。");
        }

        /// <summary>
        /// 抑止は<b>新しい遷移操作</b>で解ける（§5 の「次の新しい遷移操作で一度だけ再試行できる」）。
        /// </summary>
        [Test]
        public void ANewTransitionRequest_LiftsTheSuppression()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.016f, true, out _, out _);
            planner.SuppressHeld();
            Assert.IsFalse(
                planner.Tick(AreaHere, Only(East(2f)), 0.016f, true, out _, out _),
                "前提：抑止が効いている。");

            planner.ClearSuppression();

            Assert.IsTrue(
                planner.Tick(AreaHere, Only(East(2f)), 0.016f, true, out StableId area, out _),
                "解けば、もう一度読んでよい。");
            Assert.AreEqual(AreaEast.Value, area.Value, "同じ先で構わない。");
        }

        /// <summary>別の Area へ移れば抑止も解ける（前の Area の話ではなくなる）。</summary>
        [Test]
        public void MovingToAnotherArea_LiftsTheSuppressionToo()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.016f, true, out _, out _);
            planner.SuppressHeld();

            planner.Tick(AreaEast, new List<AreaPreloadCandidate>(), 0.016f, true, out _, out _);
            Assert.IsFalse(planner.SuppressedConnectionId.IsValid, "抑止は持ち越さない。");
        }

        /// <summary>望みを取り下げれば、選び直しから始まる（遷移が引き取った・捨てたとき）。</summary>
        [Test]
        public void Forgetting_StartsTheChoiceOver()
        {
            var planner = new AreaProximityPreloadPlanner();
            planner.Tick(AreaHere, Only(East(2f)), 0.1f, true, out _, out _);
            planner.Forget();

            Assert.IsFalse(planner.HeldAreaId.IsValid, "保持は消えている。");
            Assert.IsFalse(
                planner.Tick(AreaHere, Only(East(20f)), 0.1f, true, out _, out _),
                "範囲の外なら、選び直しでは何も望まない。");
        }
    }
}
