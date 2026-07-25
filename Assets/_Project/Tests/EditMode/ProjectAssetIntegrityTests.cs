using System.Collections.Generic;
using System.Linq;
using Momotaro.Core.Identification;
using Momotaro.Data;
using Momotaro.Editor.Validation;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-12 統合受入（項目 L）：実プロジェクトの Data Asset 群を対象に、Stable ID の書式・一意性・
    /// 各 Asset の Validate をまとめて検証する。合成データではなく実際に出荷される SO を走査するため、
    /// ID 重複や不正 ID・欠落を回帰として検出できる（AssetDatabase 走査は Editor 専用）。
    /// </summary>
    public sealed class ProjectAssetIntegrityTests
    {
        // 現行 Phase 2 時点で存在する Data Asset の下限（SO 9 種）。将来の追加で増える分には支障しない下限値。
        private const int MinimumExpectedAssets = 9;

        [Test]
        public void RealProject_RunAll_HasNoErrors()
        {
            DataValidationReport report = ProjectDataValidator.RunAll();
            Assert.IsFalse(
                report.HasErrors,
                "実プロジェクトの Data 検証でエラー: " + string.Join(" / ", report.Errors));
        }

        [Test]
        public void RealProject_CollectsDataAssets()
        {
            List<GameDataAsset> assets = ProjectDataValidator.CollectAllDataAssets();
            Assert.GreaterOrEqual(
                assets.Count, MinimumExpectedAssets,
                "実プロジェクトの Data Asset が想定数以上に走査される（検証が空振りしていないこと）。");
        }

        [Test]
        public void RealProject_AllStableIds_AreValidAndUnique()
        {
            List<GameDataAsset> assets = ProjectDataValidator.CollectAllDataAssets();

            foreach (GameDataAsset asset in assets)
            {
                Assert.IsTrue(
                    StableIdFormat.IsValid(asset.Id.Value),
                    "Stable ID の書式が不正: '" + asset.Id.Value + "' (" + asset.name + ")");
            }

            List<string> duplicates = assets
                .Select(a => a.Id.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .GroupBy(v => v)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.IsEmpty(duplicates, "Stable ID が重複している: " + string.Join(", ", duplicates));
        }
    }
}
