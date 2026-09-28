using System.Collections.Generic;
using Momotaro.EditorBridge;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P5.5 の必須テスト一覧そのものを検査する（仕様書 §11 末尾／§11 の V03。工程 P55-05a）。
    ///
    /// §11 は「件数だけでなく、<b>要求したテスト名の存在と Passed を検査する</b>」と定め、
    /// V03 は「Bridge から検査・必須 manifest 検証が同じ経路へ接続、<b>未実装／Skip／0 件成功を
    /// 失敗扱い</b>」と定めている。ところが一覧そのものは<b>何にも検査されていなかった</b>——
    /// P4・P5 の一覧には整合性の検査があるのに、P5.5 の一覧には無かった。
    ///
    /// <b>ここで見つけた穴：</b> <c>supportingTests</c> は照合されていなかった。
    /// 受入記録に「この検査も受入条件」と書いた名前が 118 件あったのに、
    /// 実行結果から消えても合格していた。P55-05a で照合対象に入れた（下の検査で固定する）。
    /// </summary>
    public sealed class P55RequiredTestsManifestTests
    {
        private static Phase4RequiredTests.Manifest Load()
        {
            Phase4RequiredTests.Manifest manifest = Phase4RequiredTests.Load("P5.5", out string error);
            Assert.IsNotNull(manifest, "P5.5 の一覧が読める: " + error);
            return manifest;
        }

        /// <summary>鍵で選べる（Bridge の <c>verify-required-tests</c> と同じ経路）。</summary>
        [Test]
        public void TheManifest_IsSelectableByItsKeysAndLivesOutsideAssets()
        {
            foreach (string key in new[] { "P5.5", "p55", "P55RequiredTests.json" })
            {
                Assert.IsTrue(
                    Phase4RequiredTests.TryResolveFileName(key, out string file, out string error),
                    "鍵 '" + key + "' で選べる: " + error);
                Assert.AreEqual(Phase4RequiredTests.Phase55FileName, file);
            }

            // Assets の外に置く（Unity に資産として取り込ませない。meta・GUID を増やさない）。
            string path = Phase4RequiredTests.PathOf(Phase4RequiredTests.Phase55FileName);
            Assert.IsFalse(path.Replace('\\', '/').Contains("/Assets/"),
                "一覧は Assets の外にある: " + path);
        }

        /// <summary>要求 1 件につき、実装済みのテストが 1 本以上ある。</summary>
        [Test]
        public void EveryRequirement_IsCoveredByAnImplementedTest()
        {
            Phase4RequiredTests.Manifest manifest = Load();
            Assert.Greater(manifest.requirements.Length, 0, "要求が載っている。");

            var covered = new HashSet<string>();
            foreach (Phase4RequiredTests.RequiredEntry entry in manifest.tests)
            {
                if (entry == null || !entry.IsRequirable)
                {
                    continue; // 未実装・名前なしは覆っていない（§11 末尾）。
                }

                foreach (string id in entry.RequirementIds())
                {
                    covered.Add(id);
                }
            }

            foreach (Phase4RequiredTests.RequirementEntry requirement in manifest.requirements)
            {
                Assert.IsTrue(covered.Contains(requirement.id),
                    "要求に対応する実装済みテストが無い: " + requirement.id);
            }

            Assert.AreEqual(0, manifest.UnmetRequirements(string.Empty).Count,
                "未対応の要求が無い。");
        }

        /// <summary>載っているテストの書式が揃っている（工程名・mode・完全名）。</summary>
        [Test]
        public void EveryTest_IsWellFormed()
        {
            Phase4RequiredTests.Manifest manifest = Load();
            var stages = new HashSet<string>(manifest.stages);
            var requirementIds = new HashSet<string>();
            foreach (Phase4RequiredTests.RequirementEntry requirement in manifest.requirements)
            {
                requirementIds.Add(requirement.id);
            }

            Assert.Greater(manifest.tests.Length, 0, "テストが載っている。");

            foreach (Phase4RequiredTests.RequiredEntry entry in manifest.tests)
            {
                Assert.IsNotNull(entry);
                Assert.IsFalse(string.IsNullOrEmpty(entry.fullName), "完全名が空のテストがある。");
                Assert.IsTrue(entry.fullName.StartsWith("Momotaro.Tests."),
                    "完全名は実行結果に現れる形で持つ: " + entry.fullName);
                Assert.IsTrue(stages.Contains(entry.stage),
                    "未知の工程が指定されている: " + entry.stage + "（" + entry.fullName + "）");
                Assert.IsTrue(entry.mode == "EditMode" || entry.mode == "PlayMode",
                    "mode は EditMode / PlayMode のどちらか: " + entry.mode);

                foreach (string id in entry.RequirementIds())
                {
                    Assert.IsTrue(requirementIds.Contains(id),
                        "一覧に無い要求 ID を名乗っている: " + id + "（" + entry.fullName + "）");
                }

                if (entry.supportingTests == null)
                {
                    continue;
                }

                foreach (string name in entry.supportingTests)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(name),
                        "支えのテスト名が空: " + entry.fullName);
                    Assert.IsTrue(name.StartsWith("Momotaro.Tests."),
                        "支えのテスト名も実行結果の形で持つ: " + name);
                }
            }
        }

        /// <summary>
        /// <b>支えのテストも照合される</b>（P55-05a で入れた穴の修正を固定する）。
        ///
        /// これが崩れると、「受入条件」と書いた名前が実行結果から消えても緑になる。
        /// </summary>
        [Test]
        public void SupportingTests_AreRequiredToo()
        {
            Phase4RequiredTests.Manifest manifest = Load();
            List<string> required = manifest.RequiredFullNames(string.Empty);

            int supporting = 0;
            foreach (Phase4RequiredTests.RequiredEntry entry in manifest.tests)
            {
                if (entry?.supportingTests == null)
                {
                    continue;
                }

                foreach (string name in entry.supportingTests)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    supporting++;
                    Assert.IsTrue(required.Contains(name),
                        "支えのテストが照合対象に入っていない: " + name);
                }
            }

            Assert.Greater(supporting, 0, "前提：支えのテストが載っている。");
            Assert.Greater(required.Count, manifest.tests.Length,
                "照合対象は主のテストより多い（支えのぶんが乗っている）。");
        }

        /// <summary>
        /// <b>未実装のエントリは要求にも coverage にも数えない</b>（§11 末尾「未実装を失敗扱い」）。
        /// 名前だけ置いて「対応済み」にできると、0 件成功が合格になる。
        /// </summary>
        [Test]
        public void AnUnimplementedEntry_IsNeitherRequiredNorCounted()
        {
            var manifest = new Phase4RequiredTests.Manifest
            {
                stages = new[] { "S1" },
                requirements = new[]
                {
                    new Phase4RequiredTests.RequirementEntry { id = "X01", stage = "S1" },
                },
                tests = new[]
                {
                    new Phase4RequiredTests.RequiredEntry
                    {
                        requirementId = "X01",
                        stage = "S1",
                        mode = "EditMode",
                        fullName = "Momotaro.Tests.EditMode.NotYet",
                        implemented = false,
                    },
                },
            };

            Assert.AreEqual(0, manifest.RequiredFullNames(string.Empty).Count,
                "未実装は要求しない（あるふりをしない）。");
            Assert.AreEqual(1, manifest.UnmetRequirements(string.Empty).Count,
                "未実装で覆ったことにしない。");
        }

        /// <summary>
        /// <b>0 件成功を合格にしない</b>（§11 末尾／V03）。
        /// 実行結果が空なら、必須はすべて「欠落」になる。
        /// </summary>
        [Test]
        public void AnEmptyRun_FailsTheGate()
        {
            Phase4RequiredTests.Manifest manifest = Load();
            List<string> required = manifest.RequiredFullNames(string.Empty);
            Assert.Greater(required.Count, 0, "前提：必須が載っている。");

            Phase4RequiredTestGate.GateReport report = Phase4RequiredTestGate.Check(
                required, manifest.AllowedSkipFullNames(),
                new Phase4RequiredTestGate.LeafOutcome[0]);

            Assert.IsFalse(report.Passed, "1 件も走っていないのに合格にしない。");
            Assert.AreEqual(0, report.RequiredPassed);

            // 欠落は名前ごとに 1 回だけ挙がる（同じテストを 2 つの要求が支えることがある）。
            var distinct = new HashSet<string>(required);
            Assert.AreEqual(distinct.Count, report.Missing.Count, "全部が欠落として挙がる。");
        }

        /// <summary>
        /// 支えのテストが Skip や欠落になったら不合格になる（この一覧の実際の名前で見る）。
        /// </summary>
        [Test]
        public void ASkippedOrMissingSupportingTest_FailsTheGate()
        {
            Phase4RequiredTests.Manifest manifest = Load();
            List<string> required = manifest.RequiredFullNames(string.Empty);

            // 支えのテストを 1 つ選ぶ。
            string supporting = null;
            foreach (Phase4RequiredTests.RequiredEntry entry in manifest.tests)
            {
                if (entry?.supportingTests == null || entry.supportingTests.Length == 0)
                {
                    continue;
                }

                supporting = entry.supportingTests[0];
                break;
            }

            Assert.IsNotNull(supporting, "前提：支えのテストが載っている。");

            // 全部 Passed のうえで、その 1 本だけ Skip にする。
            var leaves = new List<Phase4RequiredTestGate.LeafOutcome>();
            foreach (string name in required)
            {
                leaves.Add(new Phase4RequiredTestGate.LeafOutcome(
                    name, name == supporting ? "Skipped" : "Passed"));
            }

            Phase4RequiredTestGate.GateReport skipped = Phase4RequiredTestGate.Check(
                required, allowedSkips: null, leaves: leaves);
            Assert.IsFalse(skipped.Passed, "支えのテストの Skip を見逃さない。");

            // 非 Passed は「名前: 状態」の形で挙がるので、名前を含むかで見る。
            bool named = false;
            foreach (string line in skipped.NotPassed)
            {
                named |= line != null && line.Contains(supporting);
            }

            Assert.IsTrue(named, "その名前が挙がる。挙がったのは=" + string.Join(" / ", skipped.NotPassed));

            // 欠落でも同じ。
            leaves.RemoveAll(l => l.FullName == supporting);
            Phase4RequiredTestGate.GateReport missing = Phase4RequiredTestGate.Check(
                required, allowedSkips: null, leaves: leaves);
            Assert.IsFalse(missing.Passed, "支えのテストの欠落を見逃さない。");
            Assert.Contains(supporting, missing.Missing, "その名前が挙がる。");
        }

        /// <summary>説明済み Skip には理由が要る（件数だけで許容しない）。</summary>
        [Test]
        public void AllowedSkips_CarryAReason()
        {
            Phase4RequiredTests.Manifest manifest = Load();
            foreach (Phase4RequiredTests.AllowedSkipEntry entry in manifest.allowedSkips)
            {
                Assert.IsNotNull(entry);
                Assert.IsFalse(string.IsNullOrEmpty(entry.fullName), "完全名が空の許容 Skip がある。");
                Assert.IsFalse(string.IsNullOrEmpty(entry.reason),
                    "理由の無い許容 Skip がある: " + entry.fullName);
            }
        }
    }
}
