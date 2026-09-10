namespace Momotaro.EditorBridge
{
    /// <summary>
    /// テスト実行の件数から「成功と呼んでよいか」を決める規則（純粋関数。Unity API に触れないので EditMode から直接固定できる）。
    ///
    /// これを型として切り出したのは、<b>失敗 0 件は成功と同じではない</b>という事故があったため。
    /// フィルタの綴りが実装と食い違って 1 件も一致しなかった実行が「成功 0 / 失敗 0」として <c>ok</c> で返り、
    /// 何も検証していないのに緑と報告された。<c>failed &gt; 0</c> だけで status を決めると必ず再発する。
    ///
    /// <c>error</c> と <c>failed</c> を分けるのは、外から取るべき行動が違うため。
    /// <list type="bullet">
    /// <item><description><b>error</b>：実行そのものが成立していない（一致 0 件・途中で止まった）。
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

        /// <summary>件数から status を決める。</summary>
        /// <param name="passed">成功件数。</param>
        /// <param name="failed">失敗件数。</param>
        /// <param name="skipped">スキップ件数。</param>
        /// <param name="expected">開始時に予定されていた件数。0 は「不明」として完走判定に使わない。</param>
        /// <param name="minPassed">成功件数の下限。0 は指定なし。テストが消えた・全部スキップに化けたことを検出する。</param>
        public static Outcome Decide(int passed, int failed, int skipped, int expected, int minPassed)
        {
            int executed = passed + failed + skipped;

            if (executed <= 0)
            {
                return new Outcome(Error,
                    "実行 0 件。フィルタに一致するテストがありません"
                    + "（正規表現の綴り・対象アセンブリ・コンパイルが通っているかを確認してください）。");
            }

            bool incomplete = expected > 0 && executed < expected;
            string incompleteNote = incomplete
                ? "（予定 " + expected + " 件のうち " + executed + " 件しか実行されていません。中断された可能性があります）"
                : string.Empty;

            // 失敗が出ている時点で緑ではない。完走していなくても、まず失敗を伝えるほうが行動に繋がる。
            if (failed > 0)
            {
                return new Outcome(Failed, "失敗 " + failed + " 件。" + incompleteNote);
            }

            if (incomplete)
            {
                return new Outcome(Error, "実行が完走していません" + incompleteNote + "。合否は判定できません。");
            }

            if (passed <= 0)
            {
                return new Outcome(Failed,
                    "成功 0 件（" + skipped + " 件すべてスキップ）。実行環境の前提が満たされていないため、検証できていません。");
            }

            if (minPassed > 0 && passed < minPassed)
            {
                return new Outcome(Failed,
                    "成功 " + passed + " 件は下限 " + minPassed + " 件を下回ります"
                    + "（テストが消えている・スキップに化けている可能性があります）。");
            }

            return new Outcome(Ok, string.Empty);
        }
    }
}
