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
            int passed, int failed, int skipped, int expected = 0, int minPassed = 0)
        {
            return BridgeTestOutcome.Decide(passed, failed, skipped, expected, minPassed);
        }

        [Test]
        public void AllPassed_IsOk()
        {
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
            // キャンセル・再生モードの異常終了：予定より少ない件数しか実行されていない。
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
            // RunStarted の通知が届かなかった場合。予定件数を知らないだけで、実行結果まで否定しない。
            BridgeTestOutcome.Outcome outcome = Decide(passed: 5, failed: 0, skipped: 0, expected: 0);

            Assert.AreEqual(BridgeTestOutcome.Ok, outcome.Status);
        }
    }
}
