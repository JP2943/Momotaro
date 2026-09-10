namespace Momotaro.EditorBridge
{
    /// <summary>
    /// テスト実行が「成功」と呼べる状態だったかを決める規則（純粋。Unity API にも Test Framework の型にも触れないので、
    /// EditMode から任意の状況を注入して固定できる）。
    ///
    /// これを型として切り出したのは、<b>失敗 0 件は成功と同じではない</b>という事故があったため。
    /// フィルタの綴りが実装と食い違って 1 件も一致しなかった実行が「成功 0 / 失敗 0」として <c>ok</c> で返り、
    /// 何も検証していないのに緑と報告された。件数だけで status を決めると必ず再発する。
    ///
    /// <c>error</c> と <c>failed</c> を分けるのは、外から取るべき行動が違うため。
    /// <list type="bullet">
    /// <item><description><b>error</b>：実行そのものが成立していない（一致 0 件・中断・結果欠落・結果不明・集計の食い違い）。
    /// テストの合否は<b>不明</b>で、やり直さないと何も分からない。</description></item>
    /// <item><description><b>failed</b>：実行は成立したが結果が不合格（失敗あり・全スキップ・下限割れ）。
    /// 中身を読んで直す段階にある。</description></item>
    /// </list>
    /// </summary>
    public static class BridgeTestOutcome
    {
        /// <summary>実行が成立し、結果も合格。</summary>
        public const string Ok = "ok";

        /// <summary>実行は成立したが結果が不合格。</summary>
        public const string Failed = "failed";

        /// <summary>実行が成立していない（合否は不明）。</summary>
        public const string Error = "error";

        /// <summary>判定結果。</summary>
        public readonly struct Outcome
        {
            /// <summary><see cref="Ok"/> / <see cref="Failed"/> / <see cref="Error"/>。</summary>
            public string Status { get; }

            /// <summary>そう判定した理由（<see cref="Ok"/> のときは空）。結果メッセージの先頭に置く。</summary>
            public string Reason { get; }

            /// <summary>成功として扱ってよいか。</summary>
            public bool IsOk => Status == Ok;

            public Outcome(string status, string reason)
            {
                Status = status;
                Reason = reason ?? string.Empty;
            }
        }

        /// <summary>
        /// 件数と全体の終わり方から status を決める。
        /// </summary>
        public static Outcome Decide(in BridgeRunSummary summary)
        {
            // 1) 全体結果そのものが届かない。件数を見る以前の問題。
            if (summary.Termination == BridgeRunTermination.Missing)
            {
                return new Outcome(Error,
                    "実行結果が届きませんでした。合否は判定できません（Test Runner の状態を確認してください）。");
            }

            // 2) 中断。件数が下限を超えていても、残りが走っていない以上は合格の証拠にならない。
            if (summary.Termination == BridgeRunTermination.Cancelled)
            {
                return new Outcome(Error,
                    "実行が中断されました（" + summary.Executed + " 件まで実行）。合否は判定できません。");
            }

            if (summary.Executed <= 0)
            {
                return new Outcome(Error,
                    "実行 0 件。フィルタに一致するテストがありません"
                    + "（正規表現の綴り・対象アセンブリ・コンパイルが通っているかを確認してください）。");
            }

            // 3) 合否を語れない結果が混ざっている。Passed が多くても「不明」を成功へ丸めない。
            if (summary.Inconclusive > 0)
            {
                return new Outcome(Error,
                    "結果が確定していないテストが " + summary.Inconclusive + " 件あります。合否は判定できません。");
            }

            // 4) 集計と、実際に集めた葉の数が合わない。どちらかが欠けている＝件数を信用できない。
            if (summary.LeafCount >= 0 && summary.LeafCount != summary.Executed)
            {
                return new Outcome(Error,
                    "集計件数 " + summary.Executed + " と実際のテスト結果 " + summary.LeafCount
                    + " 件が食い違います。結果の取りこぼしが疑われます。");
            }

            bool incomplete = summary.Expected > 0 && summary.Executed < summary.Expected;
            string incompleteNote = incomplete
                ? "（予定 " + summary.Expected + " 件のうち " + summary.Executed + " 件しか実行されていません）"
                : string.Empty;

            // 5) 失敗が出ている時点で緑ではない。完走していなくても、まず失敗を伝えるほうが行動に繋がる。
            if (summary.Failed > 0)
            {
                return new Outcome(Failed, "失敗 " + summary.Failed + " 件。" + incompleteNote);
            }

            // 6) 全体結果が「失敗」を主張しているのに、失敗した葉が 1 件も無い。どちらかが嘘なので緑にしない。
            if (summary.Termination == BridgeRunTermination.Unknown)
            {
                return new Outcome(Error,
                    "全体の結果が確定していません（" + summary.TerminationDetail + "）。合否は判定できません。");
            }

            if (incomplete)
            {
                return new Outcome(Error, "実行が完走していません" + incompleteNote + "。合否は判定できません。");
            }

            if (summary.Passed <= 0)
            {
                return new Outcome(Failed,
                    "成功 0 件（" + summary.Skipped + " 件すべてスキップ）。実行環境の前提が満たされていないため、検証できていません。");
            }

            if (summary.MinPassed > 0 && summary.Passed < summary.MinPassed)
            {
                return new Outcome(Failed,
                    "成功 " + summary.Passed + " 件は下限 " + summary.MinPassed + " 件を下回ります"
                    + "（テストが消えている・スキップに化けている可能性があります）。");
            }

            return new Outcome(Ok, string.Empty);
        }
    }

    /// <summary>
    /// テスト実行の終わり方。件数だけでは「最後まで走ったか」が分からないため、全体結果から別に取る。
    /// Test Framework の型をここへ持ち込まないのは、パッケージ更新で判定規則まで巻き込まれないようにするため
    /// （変換は <c>EditorBridgeTestRun</c> の 1 箇所だけで行う）。
    /// </summary>
    public enum BridgeRunTermination
    {
        /// <summary>最後まで走り、全体結果も合否を語れる状態で届いた。</summary>
        Completed,

        /// <summary>全体結果そのものが届かなかった。</summary>
        Missing,

        /// <summary>途中で中断された。</summary>
        Cancelled,

        /// <summary>全体結果が Inconclusive など、合否を語れない状態だった。</summary>
        Unknown,
    }

    /// <summary>1 回のテスト実行を要約した、判定に必要な情報だけの入れ物。</summary>
    public readonly struct BridgeRunSummary
    {
        /// <summary>成功件数。</summary>
        public int Passed { get; }

        /// <summary>失敗件数。</summary>
        public int Failed { get; }

        /// <summary>スキップ件数。</summary>
        public int Skipped { get; }

        /// <summary>結果が確定しなかった件数（Inconclusive）。従来は集計から落ちていた。</summary>
        public int Inconclusive { get; }

        /// <summary>開始時に予定されていた件数。0 は「不明」として完走判定に使わない。</summary>
        public int Expected { get; }

        /// <summary>成功件数の下限。0 は指定なし。</summary>
        public int MinPassed { get; }

        /// <summary>実際に集めた葉の結果の数。負値は「数えていない」。</summary>
        public int LeafCount { get; }

        /// <summary>全体の終わり方。</summary>
        public BridgeRunTermination Termination { get; }

        /// <summary>終わり方の補足（全体結果の ResultState など。メッセージに出す）。</summary>
        public string TerminationDetail { get; }

        /// <summary>実行された件数の合計。</summary>
        public int Executed => Passed + Failed + Skipped + Inconclusive;

        public BridgeRunSummary(
            int passed, int failed, int skipped, int inconclusive,
            int expected, int minPassed, int leafCount,
            BridgeRunTermination termination, string terminationDetail)
        {
            Passed = passed;
            Failed = failed;
            Skipped = skipped;
            Inconclusive = inconclusive;
            Expected = expected;
            MinPassed = minPassed;
            LeafCount = leafCount;
            Termination = termination;
            TerminationDetail = terminationDetail ?? string.Empty;
        }

        /// <summary>結果が丸ごと届かなかった場合の要約。</summary>
        public static BridgeRunSummary Missing(int expected, int minPassed)
        {
            return new BridgeRunSummary(0, 0, 0, 0, expected, minPassed, -1,
                BridgeRunTermination.Missing, "全体結果が null");
        }
    }
}
