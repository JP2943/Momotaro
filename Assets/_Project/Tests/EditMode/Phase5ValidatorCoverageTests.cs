using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// <b>Builder が置く部品は、必ず誰かが検査している</b>ことを固定する（配線監査・記録 029）。
    ///
    /// P5 で同じ形の欠陥が 4 件続いた——出入口の Trigger 受信、再試行の実行経路、再試行の受け口、
    /// 受付の所有権。いずれも<b>部品は置いたが、それを使う側・見る側が居ない</b>という形で、
    /// 4 件とも「誰かが検査を書き忘れた」ことが共通の根だった。
    ///
    /// 個別に検査を足すだけでは 5 件目が同じ形で出る。ここでは<b>検査の抜けそのものを検出する</b>：
    /// Builder が Area Scene へ載せる型を列挙し、それぞれが
    /// <list type="bullet">
    /// <item><description>Scene Validator から参照されている（＝何らかの検査を受けている）、または</description></item>
    /// <item><description>下の免除表に<b>理由つきで</b>載っている</description></item>
    /// </list>
    /// のどちらかであることを求める。新しい部品を足したら、検査を書くか免除理由を書くまでここが落ちる。
    ///
    /// <b>ソースを読む検査にしてある。</b> <c>AddComponent&lt;T&gt;</c> は実行しないと分からず、
    /// 反射では拾えない。Editor テストなので、既知のパスにある自分のソースを読むのは妥当な手段と判断した。
    /// </summary>
    public sealed class Phase5ValidatorCoverageTests
    {
        private const string BuilderPath =
            "Assets/_Project/Scripts/Editor/Phase5/Phase5ExplorationBuilder.cs";

        private const string ValidatorPath =
            "Assets/_Project/Scripts/Editor/Phase5/Phase5ExplorationValidator.cs";

        /// <summary>
        /// 検査を免除する型と、その<b>理由</b>。理由を書けないものは免除しない。
        ///
        /// 「間接的に検査されている」は、その検査が落ちれば<b>この型の欠落も必ず落ちる</b>ときだけ認める。
        /// </summary>
        private static readonly Dictionary<string, string> Exempt = new Dictionary<string, string>
        {
            // --- Unity 標準。Area の成立条件ではなく、欠けても Validator の役目ではない ---
            { "Camera", "Unity 標準。カメラの成立は AreaCameraRig の配線検査が見る。" },
            { "AudioListener", "Unity 標準。音の有無は Scene 検査の対象外（P10b）。" },
            { "Light", "Unity 標準。見た目の明るさは検査対象外（P10b）。" },
            { "NavMeshModifier", "Unity AI Navigation の標準部品。焼き込みの結果は NavMesh 検査が見る。" },

            // --- 間接的に必ず検出される ---
            {
                "CombatSessionController",
                "欠けると AreaEncounterRunner.IsWired が false になり、遭遇戦の配線検査が落ちる。"
            },
            {
                "CompanionRosterContext",
                "欠けると InvestigationCoordinator.IsWired が false になり、調査の配線検査が落ちる（実際に注入で確認済み）。"
            },
            {
                "HitStopController",
                "欠けると CombatFeedbackPresenter の配線検査（HitStop != null）が落ちる。"
            },
            {
                "HitFlashPresenter",
                "欠けると CombatFeedbackPresenter の配線検査（Flash != null）が落ちる。"
            },

            // --- Area Scene に載らない ---
            {
                "Phase5TrialLauncher",
                "統合起動 Scene にだけ載る。Area Scene 検査の対象外（統合起動 Scene の検査は後続課題）。"
            },
        };

        [Test]
        public void EveryComponentTheBuilderPlaces_IsCheckedOrExemptedWithAReason()
        {
            string builder = ReadRepoFile(BuilderPath);
            string validator = ReadRepoFile(ValidatorPath);

            SortedSet<string> placed = PlacedTypes(builder);

            Assert.Greater(placed.Count, 20, "Builder の走査に失敗している（型が拾えていない）。");

            var uncovered = new List<string>();
            foreach (string type in placed)
            {
                if (Regex.IsMatch(validator, @"\b" + Regex.Escape(type) + @"\b"))
                {
                    continue; // 何らかの検査を受けている。
                }

                if (Exempt.ContainsKey(type))
                {
                    continue; // 理由つきで免除されている。
                }

                uncovered.Add(type);
            }

            Assert.IsEmpty(uncovered,
                "Builder が置くのに Scene Validator が一度も触れていない部品があります: "
                + string.Join(", ", uncovered)
                + "。検査を足すか、Phase5ValidatorCoverageTests.Exempt へ<b>理由つきで</b>登録してください。"
                + "（同じ形の欠陥が P5 で 4 件続いたので、抜けそのものをここで止めています。記録 029）");
        }

        /// <summary>免除表が現実と食い違っていないか（置かれなくなった型が残っていないか）。</summary>
        [Test]
        public void ExemptionList_HasNoStaleEntries()
        {
            string builder = ReadRepoFile(BuilderPath);
            SortedSet<string> placed = PlacedTypes(builder);

            var stale = Exempt.Keys.Where(t => !placed.Contains(t)).ToList();
            Assert.IsEmpty(stale,
                "Builder が置かなくなった型が免除表に残っています: " + string.Join(", ", stale)
                + "。免除は現実に置かれている部品にだけ与える（古い免除は次の抜けを隠す）。");
        }

        /// <summary>免除には必ず理由を書く（空文字の免除を作らせない）。</summary>
        [Test]
        public void EveryExemption_HasANonEmptyReason()
        {
            foreach (KeyValuePair<string, string> e in Exempt)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.Value), "免除の理由が空です: " + e.Key);
                Assert.Greater(e.Value.Length, 15, "免除の理由が短すぎます（何が検出するのかを書く）: " + e.Key);
            }
        }

        /// <summary>
        /// Builder が載せる型を拾う。
        ///
        /// <b>名前空間つきの指定も拾う。</b> <c>AddComponent&lt;UnityEngine.BoxCollider2D&gt;</c> のように
        /// 書かれると <c>\w+</c> では一致せず、<b>その部品だけ検査の網から外れる</b>。
        /// 実際、この検査を欠陥注入で試したときに素通りして気付いた。最後の区切り以降を型名として使う。
        /// </summary>
        private static SortedSet<string> PlacedTypes(string builder)
        {
            var placed = new SortedSet<string>();
            foreach (Match m in Regex.Matches(builder, @"AddComponent<\s*([\w.]+)\s*>"))
            {
                string name = m.Groups[1].Value;
                int dot = name.LastIndexOf('.');
                placed.Add(dot >= 0 ? name.Substring(dot + 1) : name);
            }

            return placed;
        }

        private static string ReadRepoFile(string relative)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), relative);
            Assert.IsTrue(File.Exists(full), "ソースが見つかりません: " + relative);
            return File.ReadAllText(full);
        }
    }
}
