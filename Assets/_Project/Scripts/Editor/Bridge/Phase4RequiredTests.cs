using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Momotaro.EditorBridge
{
    /// <summary>
    /// 必須テスト一覧の正本（<c>P4RequiredTests.json</c>）を読む。
    ///
    /// Markdown の対応表やクラス名の正規表現では代替しない。前者は機械が読めず、後者は
    /// <b>そのクラスからテストが 1 本消えても気付けない</b>。必須は「実行結果に現れる完全名」で持つ。
    ///
    /// 置き場所は Assets の外（プロジェクト直下）。Unity にアセットとして取り込ませる必要が無く、
    /// meta・GUID の管理を増やさないため。
    /// </summary>
    public static class Phase4RequiredTests
    {
        /// <summary>正本のファイル名（プロジェクト直下）。</summary>
        public const string FileName = "P4RequiredTests.json";

        /// <summary>1 件の必須テスト。</summary>
        [Serializable]
        public sealed class RequiredEntry
        {
            /// <summary>対応する要求 ID（R03、N01 等）。名前を変えても要求との対応を失わないために持つ。</summary>
            public string requirementId;

            /// <summary>この工程の終了までに Passed を要求する。</summary>
            public string stage;

            /// <summary>EditMode / PlayMode。</summary>
            public string mode;

            /// <summary>実行結果に現れる完全名。</summary>
            public string fullName;
        }

        /// <summary>説明済みの非必須 Skip。</summary>
        [Serializable]
        public sealed class AllowedSkipEntry
        {
            /// <summary>実行結果に現れる完全名。</summary>
            public string fullName;

            /// <summary>なぜ Skip してよいのか（環境ガード等）。件数だけで許容しないための説明。</summary>
            public string reason;
        }

        /// <summary>正本の中身。</summary>
        [Serializable]
        public sealed class Manifest
        {
            /// <summary>書式のバージョン。</summary>
            public int version = 1;

            /// <summary>この一覧の位置づけ（人が読む注記）。</summary>
            public string note;

            /// <summary>工程の並び（先の工程ほど後ろ）。</summary>
            public string[] stages = Array.Empty<string>();

            /// <summary>必須テスト。</summary>
            public RequiredEntry[] tests = Array.Empty<RequiredEntry>();

            /// <summary>説明済みの非必須 Skip。</summary>
            public AllowedSkipEntry[] allowedSkips = Array.Empty<AllowedSkipEntry>();

            /// <summary>
            /// 指定工程までに完了を要求する必須テストの完全名を集める。
            /// 空文字なら全工程（＝一覧に載っているすべて）。
            /// </summary>
            public List<string> RequiredFullNames(string stage)
            {
                var names = new List<string>();
                bool all = string.IsNullOrEmpty(stage);
                int limit = all ? int.MaxValue : StageIndex(stage);

                foreach (RequiredEntry entry in tests)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.fullName))
                    {
                        continue;
                    }

                    if (!all && StageIndex(entry.stage) > limit)
                    {
                        continue; // まだ着手していない後続工程のぶんは、この時点では要求しない。
                    }

                    names.Add(entry.fullName);
                }

                return names;
            }

            /// <summary>説明済み Skip の完全名。</summary>
            public List<string> AllowedSkipFullNames()
            {
                var names = new List<string>();
                foreach (AllowedSkipEntry entry in allowedSkips)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.fullName))
                    {
                        names.Add(entry.fullName);
                    }
                }

                return names;
            }

            /// <summary>工程の順番（未知の工程は最後尾扱いにして、うっかり前倒しで要求しない）。</summary>
            public int StageIndex(string stage)
            {
                if (string.IsNullOrEmpty(stage))
                {
                    return int.MaxValue;
                }

                for (int i = 0; i < stages.Length; i++)
                {
                    if (string.Equals(stages[i], stage, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }

                return int.MaxValue;
            }
        }

        /// <summary>正本の絶対パス。</summary>
        public static string Path
        {
            get
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return System.IO.Path.Combine(projectRoot, FileName);
            }
        }

        /// <summary>正本を読む。読めなければ <paramref name="error"/> に理由を入れて null を返す。</summary>
        public static Manifest Load(out string error)
        {
            error = null;

            try
            {
                if (!File.Exists(Path))
                {
                    error = "必須テスト一覧が見つかりません: " + FileName;
                    return null;
                }

                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path));
                if (manifest == null)
                {
                    error = "必須テスト一覧を解析できません: " + FileName;
                    return null;
                }

                return manifest;
            }
            catch (Exception e)
            {
                error = "必須テスト一覧を読めません: " + e.Message;
                return null;
            }
        }
    }
}
