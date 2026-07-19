using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data;
using UnityEditor;

namespace Momotaro.Editor.Validation
{
    /// <summary>
    /// プロジェクト内の全 <see cref="GameDataAsset"/> を走査し、各自の検証（<see cref="IValidatableData"/>）と
    /// Stable ID の重複検査（<see cref="StableIdRegistry"/>）をまとめて実行する（仕様書 11.11）。
    ///
    /// 走査ロジックと検証ロジックを分離し、検証部（<see cref="Validate"/>）は AssetDatabase に依存せず
    /// テスト可能にしている。
    /// </summary>
    public static class ProjectDataValidator
    {
        /// <summary>
        /// 与えられた Data Asset 群を検証し、集約レポートを返す。
        /// </summary>
        public static DataValidationReport Validate(IEnumerable<GameDataAsset> assets)
        {
            var report = new DataValidationReport();
            var registry = new StableIdRegistry();

            foreach (GameDataAsset asset in assets)
            {
                if (asset == null)
                {
                    continue;
                }

                asset.Validate(report);

                // 書式が正しい ID のみ重複検査へ回す（書式不正は各 Validate が既に報告済み）。
                if (!asset.Id.IsEmpty && asset.Id.IsValid)
                {
                    if (!registry.TryRegister(asset.Id, asset.name, out string error))
                    {
                        report.Error(error);
                    }
                }
            }

            return report;
        }

        /// <summary>プロジェクト内の全 <see cref="GameDataAsset"/> を読み込む。</summary>
        public static List<GameDataAsset> CollectAllDataAssets()
        {
            var list = new List<GameDataAsset>();
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(GameDataAsset));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameDataAsset>(path);
                if (asset != null)
                {
                    list.Add(asset);
                }
            }

            return list;
        }

        /// <summary>プロジェクト全体を検証する。</summary>
        public static DataValidationReport RunAll()
        {
            return Validate(CollectAllDataAssets());
        }
    }
}
