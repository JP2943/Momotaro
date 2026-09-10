using System.Collections.Generic;
using System.IO;
using Momotaro.EditorBridge;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 工程の受入判定（必須テストの名前照合）を固定する（N04〜N06。レビュー §4）。
    ///
    /// 成功件数の下限だけでは、<b>必須テスト A が Skip に化け、別のテスト B が増えて総数が同じ</b>という
    /// 入れ替わりを検出できない。受入で守りたいのは総数ではなく「この名前が Passed であること」なので、
    /// そのまま名前で照合する。ここが緩いと、緑の報告が「何を検証した緑なのか」を語れなくなる。
    /// </summary>
    public sealed class Phase4RequiredTestGateTests
    {
        private static Phase4RequiredTestGate.LeafOutcome Leaf(string name, string status)
        {
            return new Phase4RequiredTestGate.LeafOutcome(name, status);
        }

        private static readonly string[] Required =
        {
            "Suite.AlphaTests.RequiredOne",
            "Suite.AlphaTests.RequiredTwo",
        };

        [Test]
        public void AllRequiredPassed_PassesGate()
        {
            Phase4RequiredTestGate.GateReport report = Phase4RequiredTestGate.Check(
                Required,
                allowedSkips: null,
                leaves: new[]
                {
                    Leaf("Suite.AlphaTests.RequiredOne", "Passed"),
                    Leaf("Suite.AlphaTests.RequiredTwo", "Passed"),
                });

            Assert.IsTrue(report.Passed, report.Summarize());
            Assert.AreEqual(2, report.RequiredPassed);
        }

        /// <summary>
        /// N04：必須テストが実行結果に現れない（名前変更・削除・フィルタ漏れ）。
        /// 別のテストが増えて<b>総数が一致していても</b>不合格にする。
        /// </summary>
        [Test]
        public void MissingRequiredTest_FailsGate()
        {
            Phase4RequiredTestGate.GateReport report = Phase4RequiredTestGate.Check(
                Required,
                allowedSkips: null,
                leaves: new[]
                {
                    Leaf("Suite.AlphaTests.RequiredOne", "Passed"),
                    Leaf("Suite.AlphaTests.SomethingNew", "Passed"), // 総数は 2 のまま。
                });

            Assert.IsFalse(report.Passed, "必須が消えているのに合格にしてはいけない。");
            CollectionAssert.Contains(report.Missing, "Suite.AlphaTests.RequiredTwo");
            Assert.AreEqual(1, report.RequiredPassed, "通ったのは 1 本だけ。");
        }

        /// <summary>
        /// N05：必須テストが Skip。ほかの成功がいくら増えても受入は未完了。
        /// </summary>
        [Test]
        public void RequiredSkip_FailsGateDespiteOtherPasses()
        {
            var leaves = new List<Phase4RequiredTestGate.LeafOutcome>
            {
                Leaf("Suite.AlphaTests.RequiredOne", "Passed"),
                Leaf("Suite.AlphaTests.RequiredTwo", "Skipped"),
            };

            for (int i = 0; i < 50; i++)
            {
                leaves.Add(Leaf("Suite.OtherTests.Extra" + i, "Passed"));
            }

            Phase4RequiredTestGate.GateReport report =
                Phase4RequiredTestGate.Check(Required, allowedSkips: null, leaves: leaves);

            Assert.IsFalse(report.Passed, "必須が Skip なら、ほかが 50 本通っていても不合格。");
            CollectionAssert.IsEmpty(report.Missing, "存在はしているので「欠落」ではない。");
            Assert.AreEqual(1, report.NotPassed.Count);
            StringAssert.Contains("RequiredTwo", report.NotPassed[0]);
            StringAssert.Contains("Skipped", report.NotPassed[0], "何が起きたかを理由に残す。");
        }

        /// <summary>
        /// N06：許容一覧に無い Skip が新しく現れたら不合格。件数で自動許容しない。
        /// </summary>
        [Test]
        public void UnlistedSkip_FailsGate()
        {
            string[] allowed = { "Suite.EnvTests.NeedsPhysics" };

            Phase4RequiredTestGate.GateReport report = Phase4RequiredTestGate.Check(
                Required,
                allowed,
                new[]
                {
                    Leaf("Suite.AlphaTests.RequiredOne", "Passed"),
                    Leaf("Suite.AlphaTests.RequiredTwo", "Passed"),
                    Leaf("Suite.EnvTests.NeedsPhysics", "Skipped"),   // 説明済み。
                    Leaf("Suite.OtherTests.QuietlySkipped", "Skipped"), // 説明が無い。
                });

            Assert.IsFalse(report.Passed, "説明の無い Skip が増えたら止める。");
            Assert.AreEqual(1, report.UnlistedSkips.Count);
            StringAssert.Contains("QuietlySkipped", report.UnlistedSkips[0]);
        }

        [Test]
        public void InconclusiveRequired_FailsGate()
        {
            Phase4RequiredTestGate.GateReport report = Phase4RequiredTestGate.Check(
                Required,
                allowedSkips: null,
                leaves: new[]
                {
                    Leaf("Suite.AlphaTests.RequiredOne", "Passed"),
                    Leaf("Suite.AlphaTests.RequiredTwo", "Inconclusive"),
                });

            Assert.IsFalse(report.Passed, "結果が確定していない必須を合格に数えない。");
        }

        /// <summary>
        /// 同じ葉が複数回現れても水増しにしない。1 回でも Passed でなければ合格にしない。
        /// </summary>
        [Test]
        public void DuplicateLeaf_TakesTheWorstResult()
        {
            Phase4RequiredTestGate.GateReport report = Phase4RequiredTestGate.Check(
                new[] { "Suite.AlphaTests.RequiredOne" },
                allowedSkips: null,
                leaves: new[]
                {
                    Leaf("Suite.AlphaTests.RequiredOne", "Passed"),
                    Leaf("Suite.AlphaTests.RequiredOne", "Failed"),
                });

            Assert.IsFalse(report.Passed, "重複結果のうち良いほうだけを採って合格にしない。");
            Assert.AreEqual(1, report.NotPassed.Count);
        }
    }

    /// <summary>
    /// 必須一覧の正本（<c>P4RequiredTests.json</c>）そのものを検査する。
    /// 一覧が壊れていたら照合は素通りしてしまうので、ここで形を固定しておく。
    /// </summary>
    public sealed class Phase4RequiredTestsManifestTests
    {
        [Test]
        public void Manifest_LoadsAndHasEntries()
        {
            Phase4RequiredTests.Manifest manifest = Phase4RequiredTests.Load(out string error);

            Assert.IsNotNull(manifest, error);
            Assert.Greater(manifest.tests.Length, 0, "必須が 0 件の一覧は照合の意味が無い。");
            Assert.Greater(manifest.stages.Length, 0, "工程の並びが無いと、先の工程を前倒しで要求してしまう。");
        }

        [Test]
        public void Manifest_EntriesAreWellFormed()
        {
            Phase4RequiredTests.Manifest manifest = Phase4RequiredTests.Load(out string error);
            Assert.IsNotNull(manifest, error);

            var seen = new HashSet<string>();
            foreach (Phase4RequiredTests.RequiredEntry entry in manifest.tests)
            {
                Assert.IsNotNull(entry);
                Assert.IsNotEmpty(entry.requirementId, "要求 ID が無いと、名前を変えたとき対応を失う。");
                Assert.IsNotEmpty(entry.stage, "どの工程で必須になるかが無いと、前倒しか先送りか決まらない。");
                Assert.IsTrue(entry.mode == "EditMode" || entry.mode == "PlayMode",
                    "mode は EditMode / PlayMode のいずれか: " + entry.mode);
                Assert.IsNotEmpty(entry.fullName);

                string expectedPrefix = entry.mode == "EditMode"
                    ? "Momotaro.Tests.EditMode."
                    : "Momotaro.Tests.PlayMode.";
                StringAssert.StartsWith(expectedPrefix, entry.fullName,
                    "完全名の名前空間が mode と食い違っている（照合が必ず外れる）。");

                Assert.IsTrue(seen.Add(entry.fullName), "同じテストを二重に登録している: " + entry.fullName);
                Assert.AreNotEqual(int.MaxValue, manifest.StageIndex(entry.stage),
                    "stages に無い工程名を使っている（未知の工程は永久に要求されない）: " + entry.stage);
            }
        }

        /// <summary>
        /// 許容 Skip には必ず理由を書く。理由の無い許容は「件数で許した」のと変わらない。
        /// </summary>
        [Test]
        public void Manifest_AllowedSkipsHaveReasons()
        {
            Phase4RequiredTests.Manifest manifest = Phase4RequiredTests.Load(out string error);
            Assert.IsNotNull(manifest, error);

            foreach (Phase4RequiredTests.AllowedSkipEntry entry in manifest.allowedSkips)
            {
                Assert.IsNotNull(entry);
                Assert.IsNotEmpty(entry.fullName);
                Assert.IsNotEmpty(entry.reason, "Skip を許容するなら理由を書く: " + entry.fullName);
            }
        }

        /// <summary>
        /// 正本はプロジェクト直下に 1 つだけ。Assets 配下へ複製すると、どちらが正本か分からなくなる。
        /// </summary>
        [Test]
        public void Manifest_LivesOutsideAssets()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.IsNotNull(projectRoot);
            Assert.AreEqual(
                Path.Combine(projectRoot, Phase4RequiredTests.FileName),
                Phase4RequiredTests.Path);
            Assert.IsTrue(File.Exists(Phase4RequiredTests.Path), "正本が置かれていない: " + Phase4RequiredTests.Path);
        }
    }
}
