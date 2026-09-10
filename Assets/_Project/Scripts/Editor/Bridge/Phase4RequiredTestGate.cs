using System;
using System.Collections.Generic;

namespace Momotaro.EditorBridge
{
    /// <summary>
    /// 実行結果を<b>名前付きの必須一覧</b>と照合する（レビュー §4）。純粋ロジックなので EditMode から
    /// 任意の結果を注入して固定できる。
    ///
    /// なぜ件数では足りないか。成功件数の下限（<c>minPassed</c>）は「総数が減った」ことしか見ておらず、
    /// <b>必須テスト A が Skip に化け、別のテスト B が増えて総数が同じ</b>という入れ替わりを検出できない。
    /// 実際に受入で守りたいのは「この名前のテストが、この工程で、Passed であること」なので、そのまま名前で照合する。
    ///
    /// 非必須の Skip も件数では許容しない。許容一覧に名前が載っているものだけを通し、
    /// 見覚えのない Skip が増えたら不合格にする。「以前と同数だから非必須だろう」は当てにならない。
    /// </summary>
    public static class Phase4RequiredTestGate
    {
        /// <summary>照合結果。</summary>
        public sealed class GateReport
        {
            /// <summary>必須がすべて Passed で、説明の無い Skip も無いか。</summary>
            public bool Passed => Missing.Count == 0 && NotPassed.Count == 0 && UnlistedSkips.Count == 0;

            /// <summary>実行結果に現れなかった必須テスト（名前変更・削除・フィルタ漏れ）。</summary>
            public List<string> Missing { get; } = new List<string>();

            /// <summary>実行結果に現れたが Passed でなかった必須テスト（"name: 状態" の形）。</summary>
            public List<string> NotPassed { get; } = new List<string>();

            /// <summary>許容一覧に無い Skip／Inconclusive。</summary>
            public List<string> UnlistedSkips { get; } = new List<string>();

            /// <summary>照合した必須テストの件数。</summary>
            public int RequiredCount { get; set; }

            /// <summary>そのうち Passed だった件数。</summary>
            public int RequiredPassed { get; set; }

            /// <summary>1 行の要約。</summary>
            public string Summarize()
            {
                if (Passed)
                {
                    return "必須 " + RequiredPassed + "/" + RequiredCount + " 件すべて Passed。未説明の Skip なし。";
                }

                return "必須 " + RequiredPassed + "/" + RequiredCount + " 件 Passed"
                    + " / 欠落 " + Missing.Count
                    + " / 非 Passed " + NotPassed.Count
                    + " / 未説明の Skip " + UnlistedSkips.Count;
            }
        }

        /// <summary>1 件の実行結果（照合に必要な最小限。Test Framework の型に依存しない）。</summary>
        public readonly struct LeafOutcome
        {
            /// <summary>実行結果に現れる完全名。</summary>
            public string FullName { get; }

            /// <summary>Passed / Failed / Skipped / Inconclusive。</summary>
            public string Status { get; }

            public LeafOutcome(string fullName, string status)
            {
                FullName = fullName ?? string.Empty;
                Status = status ?? string.Empty;
            }

            /// <summary>合格として数えてよいか。</summary>
            public bool IsPassed => string.Equals(Status, "Passed", StringComparison.Ordinal);

            /// <summary>実行されなかった扱い（Skip・結果不明）か。</summary>
            public bool IsSkippedOrUnknown =>
                string.Equals(Status, "Skipped", StringComparison.Ordinal)
                || string.Equals(Status, "Inconclusive", StringComparison.Ordinal);
        }

        /// <summary>
        /// 必須一覧と実行結果を突き合わせる。
        /// </summary>
        /// <param name="required">この工程で Passed を要求するテストの完全名。</param>
        /// <param name="allowedSkips">説明済みの非必須 Skip の完全名。</param>
        /// <param name="leaves">実行結果の葉。</param>
        public static GateReport Check(
            IEnumerable<string> required, IEnumerable<string> allowedSkips, IEnumerable<LeafOutcome> leaves)
        {
            var report = new GateReport();

            // 同じ葉が複数回現れても水増しにしない。名前ごとに「最も悪い結果」を採る。
            var observed = new Dictionary<string, string>(StringComparer.Ordinal);
            if (leaves != null)
            {
                foreach (LeafOutcome leaf in leaves)
                {
                    if (string.IsNullOrEmpty(leaf.FullName))
                    {
                        continue;
                    }

                    if (!observed.TryGetValue(leaf.FullName, out string existing) || IsWorse(leaf.Status, existing))
                    {
                        observed[leaf.FullName] = leaf.Status;
                    }
                }
            }

            var requiredSet = new HashSet<string>(StringComparer.Ordinal);
            if (required != null)
            {
                foreach (string name in required)
                {
                    if (string.IsNullOrEmpty(name) || !requiredSet.Add(name))
                    {
                        continue;
                    }

                    report.RequiredCount++;

                    if (!observed.TryGetValue(name, out string status))
                    {
                        report.Missing.Add(name);
                        continue;
                    }

                    if (string.Equals(status, "Passed", StringComparison.Ordinal))
                    {
                        report.RequiredPassed++;
                        continue;
                    }

                    report.NotPassed.Add(name + ": " + status);
                }
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (allowedSkips != null)
            {
                foreach (string name in allowedSkips)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        allowed.Add(name);
                    }
                }
            }

            foreach (KeyValuePair<string, string> entry in observed)
            {
                bool skipped = string.Equals(entry.Value, "Skipped", StringComparison.Ordinal)
                    || string.Equals(entry.Value, "Inconclusive", StringComparison.Ordinal);
                if (!skipped)
                {
                    continue;
                }

                // 必須が Skip した場合は NotPassed 側で既に不合格にしているので、ここで二重に数えない。
                if (requiredSet.Contains(entry.Key) || allowed.Contains(entry.Key))
                {
                    continue;
                }

                report.UnlistedSkips.Add(entry.Key + ": " + entry.Value);
            }

            report.Missing.Sort(StringComparer.Ordinal);
            report.NotPassed.Sort(StringComparer.Ordinal);
            report.UnlistedSkips.Sort(StringComparer.Ordinal);
            return report;
        }

        /// <summary>同じ名前が複数回現れたときに、より悪いほうを採るための順位。</summary>
        private static bool IsWorse(string candidate, string current)
        {
            return Rank(candidate) > Rank(current);
        }

        private static int Rank(string status)
        {
            switch (status)
            {
                case "Passed": return 0;
                case "Skipped": return 1;
                case "Inconclusive": return 2;
                case "Failed": return 3;
                default: return 2;
            }
        }
    }
}
