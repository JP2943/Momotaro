using Momotaro.EditorBridge;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// Editor 常駐ブリッジが「成功」と報告してよい条件を固定する。
    ///
    /// ブリッジは<b>検証結果を報告する装置</b>なので、ここが緩いと他のすべてのテストの緑が信用できなくなる。
    /// 実際、<c>failed &gt; 0</c> だけで status を決めていたころに、フィルタが 1 件も一致しなかった実行が
    /// 「成功 0 / 失敗 0」で <c>ok</c> と返り、何も検証していないのに緑と報告された。
    ///
    /// <see cref="BridgeTestOutcome"/> を純粋関数として切り出してあるので、Editor を動かさずにここで固定できる。
    /// </summary>
    public sealed class BridgeTestOutcomeTests
    {
        private static BridgeTestOutcome.Outcome Decide(
            int passed, int failed, int skipped,
            int inconclusive = 0, int expected = 0, int minPassed = 0, int leafCount = -1,
            BridgeRunTermination termination = BridgeRunTermination.Completed,
            string terminationDetail = "Passed")
        {
            return BridgeTestOutcome.Decide(new BridgeRunSummary(
                passed, failed, skipped, inconclusive, expected, minPassed, leafCount,
                termination, terminationDetail));
        }

        [Test]
        public void AllPassed_IsOk()
        {
            // 「成功あり・失敗なし・非必須 Skip あり」の代表例（実運用の全件実行と同じ形）。
            BridgeTestOutcome.Outcome outcome = Decide(passed: 1380, failed: 0, skipped: 13);

            Assert.AreEqual(BridgeTestOutcome.Ok, outcome.Status);
            Assert.IsTrue(outcome.IsOk);
            Assert.IsEmpty(outcome.Reason, "成功のときは理由を付けない。");
        }

        [Test]
        public void NoTestsMatched_IsError_NotOk()
        {
            BridgeTestOutcome.Outcome outcome = Decide(passed: 0, failed: 0, skipped: 0);

            Assert.AreEqual(BridgeTestOutcome.Error, outcome.Status,
                "1 件も一致しない実行は成功ではない。合否が不明なので failed ではなく error。");
            Assert.IsFalse(outcome.IsOk);
            Assert.IsNotEmpty(outcome.Reason);
        }

        [Test]
        public void AllSkipped_IsFailed_NotOk()
        {
            BridgeTestOutcome.Outcome outcome = Decide(passed: 0, failed: 0, skipped: 7);

            Assert.AreEqual(BridgeTestOutcome.Failed, outcome.Status,
                "全件スキップは「検証できていない」。実行自体は成立しているので failed。");
            Assert.IsFalse(outcome.IsOk);
        }

        [Test]
        public void AnyFailure_IsFailed()
        {
            BridgeTestOutcome.Outcome outcome = Decide(passed: 100, failed: 1, skipped: 0);

            Assert.AreEqual(BridgeTestOutcome.Failed, outcome.Status);
        }

        [Test]
        public void Incomplete_IsError_EvenWithoutFailures()
        {
            // 予定より少ない件数しか実行されていない。
            BridgeTestOutcome.Outcome outcome = Decide(passed: 12, failed: 0, skipped: 0, expected: 51);

            Assert.AreEqual(BridgeTestOutcome.Error, outcome.Status,
                "途中で止まった実行は、失敗 0 件でも緑ではない。");
            StringAssert.Contains("51", outcome.Reason, "予定件数を理由に出す。");
        }

        [Test]
        public void Complete_WithExpectedCount_IsOk()
        {
            BridgeTestOutcome.Outcome outcome = Decide(passed: 45, failed: 0, skipped: 6, expected: 51);

            Assert.AreEqual(BridgeTestOutcome.Ok, outcome.Status, "予定どおり完走していれば緑。");
        }

        [Test]
        public void Incomplete_WithFailures_ReportsFailedAndSaysSoInReason()
        {
            BridgeTestOutcome.Outcome outcome = Decide(passed: 10, failed: 2, skipped: 0, expected: 51);

            Assert.AreEqual(BridgeTestOutcome.Failed, outcome.Status,
                "失敗が出ているならまず失敗を伝える（そちらのほうが行動に繋がる）。");
            StringAssert.Contains("51", outcome.Reason, "完走していないことも理由に残す。");
        }

        [Test]
        public void BelowMinPassed_IsFailed()
        {
            BridgeTestOutcome.Outcome outcome = Decide(passed: 3, failed: 0, skipped: 0, minPassed: 1000);

            Assert.AreEqual(BridgeTestOutcome.Failed, outcome.Status,
                "件数が想定より大幅に少ない実行は、テストが消えている疑いがある。");
        }

        [Test]
        public void AtMinPassed_IsOk()
        {
            BridgeTestOutcome.Outcome outcome = Decide(passed: 1000, failed: 0, skipped: 0, minPassed: 1000);

            Assert.AreEqual(BridgeTestOutcome.Ok, outcome.Status, "下限ちょうどは満たしているとみなす。");
        }

        [Test]
        public void MinPassedZero_MeansUnspecified()
        {
            BridgeTestOutcome.Outcome outcome = Decide(passed: 1, failed: 0, skipped: 0, minPassed: 0);

            Assert.AreEqual(BridgeTestOutcome.Ok, outcome.Status, "下限を指定しなければ件数では落とさない。");
        }

        [Test]
        public void ExpectedZero_MeansUnknown_DoesNotBlock()
        {
            // RunStarted の通知が届かなかった場合や絞り込み実行。予定件数を知らないだけで、実行結果まで否定しない。
            BridgeTestOutcome.Outcome outcome = Decide(passed: 5, failed: 0, skipped: 0, expected: 0);

            Assert.AreEqual(BridgeTestOutcome.Ok, outcome.Status);
        }
    }

    /// <summary>
    /// 「件数だけ見て成功」を潰す残りの経路を固定する（N07・N08。レビュー §3.1）。
    ///
    /// <see cref="BridgeTestOutcome"/> は成功件数の下限（<c>minPassed</c>）を持つが、
    /// <b>下限は中断や必須実行の証明にならない。</b>下限を超えた時点で止まった実行も、
    /// 結果が丸ごと届かなかった実行も、Pass に混ざった Inconclusive も、件数の上では緑に見える。
    /// UI で偶然の中断を狙うのではなく、境界へ状況を注入して決定的に再現する。
    /// </summary>
    public sealed class BridgeRunCompletionTests
    {
        private static BridgeTestOutcome.Outcome Decide(in BridgeRunSummary summary)
        {
            return BridgeTestOutcome.Decide(summary);
        }

        [Test]
        public void CancelledFilteredRun_IsNotOkAfterMinPassed()
        {
            // 下限 10 件を超えた 12 件目で中断。件数だけを見れば条件を満たしている。
            var cancelled = new BridgeRunSummary(
                passed: 12, failed: 0, skipped: 0, inconclusive: 0,
                expected: 0,      // 絞り込み実行なので予定件数は使えない。
                minPassed: 10,
                leafCount: 12,
                termination: BridgeRunTermination.Cancelled,
                terminationDetail: "Cancelled");

            BridgeTestOutcome.Outcome outcome = Decide(cancelled);

            Assert.AreEqual(BridgeTestOutcome.Error, outcome.Status,
                "下限を超えていても、残りが走っていない実行は合格の証拠にならない。");
            StringAssert.Contains("中断", outcome.Reason);
        }

        [Test]
        public void InconclusiveOrMissingResult_IsNotOk()
        {
            // (a) 成功に混ざった Inconclusive。件数の上では失敗 0 件。
            var withInconclusive = new BridgeRunSummary(
                passed: 100, failed: 0, skipped: 0, inconclusive: 2,
                expected: 0, minPassed: 0, leafCount: 102,
                termination: BridgeRunTermination.Completed, terminationDetail: "Passed");

            BridgeTestOutcome.Outcome a = Decide(withInconclusive);
            Assert.AreEqual(BridgeTestOutcome.Error, a.Status,
                "結果が確定していないテストがあるなら、他が通っていても合否は不明。");

            // (b) 全体結果そのものが届かない。
            BridgeTestOutcome.Outcome b = Decide(BridgeRunSummary.Missing(expected: 51, minPassed: 0));
            Assert.AreEqual(BridgeTestOutcome.Error, b.Status,
                "結果が届いていない実行を、件数 0 のまま成功にも失敗にも丸めない。");

            // (c) 全体結果が合否を語れない（Inconclusive な全体結果・全体と葉の食い違い）。
            var unknownRoot = new BridgeRunSummary(
                passed: 30, failed: 0, skipped: 0, inconclusive: 0,
                expected: 0, minPassed: 0, leafCount: 30,
                termination: BridgeRunTermination.Unknown, terminationDetail: "Inconclusive");

            BridgeTestOutcome.Outcome c = Decide(unknownRoot);
            Assert.AreEqual(BridgeTestOutcome.Error, c.Status,
                "全体結果が確定していないなら、葉が全部 Passed でも緑にしない。");
        }

        [Test]
        public void LeafCountMismatch_IsNotOk()
        {
            // 集計は 100 件と言っているのに、実際に集めた結果は 98 件しかない。
            var mismatch = new BridgeRunSummary(
                passed: 100, failed: 0, skipped: 0, inconclusive: 0,
                expected: 0, minPassed: 0, leafCount: 98,
                termination: BridgeRunTermination.Completed, terminationDetail: "Passed");

            BridgeTestOutcome.Outcome outcome = Decide(mismatch);

            Assert.AreEqual(BridgeTestOutcome.Error, outcome.Status,
                "件数と実際の結果が食い違うなら、どちらも信用できない。");
        }

        [Test]
        public void LeafCountUnknown_DoesNotBlock()
        {
            // 葉を数えていない経路（負値）では、食い違い検査を働かせない。
            var unknownLeaves = new BridgeRunSummary(
                passed: 100, failed: 0, skipped: 0, inconclusive: 0,
                expected: 0, minPassed: 0, leafCount: -1,
                termination: BridgeRunTermination.Completed, terminationDetail: "Passed");

            Assert.AreEqual(BridgeTestOutcome.Ok, Decide(unknownLeaves).Status);
        }
    }
}
