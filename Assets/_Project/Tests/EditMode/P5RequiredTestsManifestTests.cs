using System.Collections.Generic;
using Momotaro.EditorBridge;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P5-09：必須テスト一覧を<b>許可リストで選べる</b>ことを固定する（仕様書 §16.1）。
    ///
    /// 既存の照合は P4 固定だった。P5 の一覧でも照合したいが、
    /// <b>ユーザー入力の任意パスをそのまま読む口を増やさない</b>のが §16.1 の要求なので、
    /// 選べるのは既知の鍵だけで、未知の鍵は「読まずに断る」ことまで見る。
    /// 既定（未指定）が P4 のままであることも併せて固定する：ここが変わると既存の受入が黙って別物になる。
    /// </summary>
    public sealed class P5RequiredTestsManifestTests
    {
        [Test]
        public void Manifest_DefaultsToPhase4AndSelectsPhase5ByKey()
        {
            Assert.IsTrue(Phase4RequiredTests.TryResolveFileName(null, out string defaultFile, out string error),
                "未指定は既定として通る: " + error);
            Assert.AreEqual(Phase4RequiredTests.FileName, defaultFile,
                "既定は P4 のまま（既存の照合を壊さない）。");

            Assert.IsTrue(Phase4RequiredTests.TryResolveFileName("P5", out string p5File, out error), error);
            Assert.AreEqual(Phase4RequiredTests.Phase5FileName, p5File);

            Assert.IsTrue(Phase4RequiredTests.TryResolveFileName("p5", out p5File, out error),
                "鍵は大文字小文字を区別しない: " + error);
            Assert.AreEqual(Phase4RequiredTests.Phase5FileName, p5File);
        }

        [Test]
        public void Manifest_UnknownKeyIsRefusedWithoutReadingAPath()
        {
            // 任意のパスを渡しても読まない（§16.1）。ここが通ると、ブリッジが任意ファイル読み取り口になる。
            Assert.IsFalse(
                Phase4RequiredTests.TryResolveFileName("../../secret.json", out string file, out string error));
            Assert.IsNull(file);
            StringAssert.Contains("未知の必須テスト一覧", error);

            Assert.IsNull(Phase4RequiredTests.Load("P6", out string loadError),
                "未知の鍵では一覧を返さない。");
            StringAssert.Contains("未知の必須テスト一覧", loadError);
        }

        [Test]
        public void Phase5Manifest_LoadsAndCoversEveryRequirement()
        {
            Phase4RequiredTests.Manifest manifest = Phase4RequiredTests.Load("P5", out string error);
            Assert.IsNotNull(manifest, "P5 の一覧が読める: " + error);
            Assert.AreEqual(54, manifest.requirements.Length, "§15 の要求は 54 件。");

            // <b>要求 1 件につき 1 本。</b> そのうえで、レビューで見つかった欠陥の再発防止テストも
            // 受入ゲートへ載せる（GPT レビュー R8 の指摘 2）。
            // <c>supportingTests</c> は <c>RequiredFullNames()</c> が読まないので照合対象外——
            // 「この PlayMode 検査が受入条件」と書いても、実行結果から欠落したまま合格してしまう。
            // 独立エントリにして初めてゲートに載る。したがって tests は requirements 以上になる。
            var covered = new HashSet<string>();
            for (int i = 0; i < manifest.tests.Length; i++)
            {
                covered.Add(manifest.tests[i].requirementId);
            }

            for (int i = 0; i < manifest.requirements.Length; i++)
            {
                Assert.IsTrue(covered.Contains(manifest.requirements[i].id),
                    "要求に対応する名前付きテストが無い: " + manifest.requirements[i].id);
            }

            Assert.GreaterOrEqual(manifest.tests.Length, manifest.requirements.Length,
                "必須テストは要求の数を下回らない。");

            var stages = new HashSet<string>(manifest.stages);
            for (int i = 0; i < manifest.tests.Length; i++)
            {
                Phase4RequiredTests.RequiredEntry entry = manifest.tests[i];
                Assert.IsFalse(string.IsNullOrEmpty(entry.fullName), "完全名が空のテストがある。");
                Assert.IsTrue(entry.fullName.StartsWith("Momotaro.Tests."),
                    "完全名は実行結果に現れる形で持つ: " + entry.fullName);
                Assert.IsTrue(stages.Contains(entry.stage),
                    "未知の工程が指定されている: " + entry.stage + "（" + entry.fullName + "）");
                Assert.IsTrue(entry.mode == "EditMode" || entry.mode == "PlayMode",
                    "mode は EditMode / PlayMode のどちらか: " + entry.mode);
            }
        }
    }
}
