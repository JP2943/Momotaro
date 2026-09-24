using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Characters;
using Momotaro.Data.Events;
using Momotaro.Data.Exploration;
using Momotaro.Data.Progression;
using Momotaro.Data.World;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Session;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Phase5
{
    /// <summary>
    /// P5 の Asset／Build 検査（P5-09。仕様書 v1.1 §13.2 の「Asset／Build」行、§13.3）。
    ///
    /// <b>Scene Validator とは別クラスにする</b>（§13.2 末尾が名指しで要求している）。
    /// こちらは Editor 専用で <see cref="AssetDatabase"/>・<see cref="EditorBuildSettings"/> を使う。
    /// 混ぜると Scene Validator が「Scene を渡せば検査できる」性質を失い、素の Scene を流せなくなる。
    ///
    /// 見るのは、Scene を開かなくても分かること：Data の実在と解決、経路の存在、
    /// Build Settings 登録、共有素材の参照、報酬の合計。
    /// </summary>
    public static class Phase5AssetValidator
    {
        /// <summary>初回 Encounter の徳の合計（§8.5。近接 10 ＋ 弓 12）。</summary>
        public const int ExpectedFirstEncounterVirtue = 22;

        /// <summary>Build Settings に登録されているべき Scene（§13.1）。</summary>
        public static readonly string[] RequiredScenePaths =
        {
            Phase5AreaIds.TrialScenePath,
            Phase5AreaIds.AreaAScenePath,
            Phase5AreaIds.AreaBScenePath,
        };

        /// <summary>P5 の Asset／Build を検査して追記する。</summary>
        public static void Validate(List<string> errors, List<string> warnings)
        {
            AreaCatalogData catalog = ValidateDataAssets(errors);
            ValidateCatalogRoutes(catalog, errors);
            ValidateBuildSettings(errors);
            ValidateSharedAssets(errors, warnings);
            ValidateEncounterAndRewards(errors);
        }

        // ---------------------------------------------------------------- Data の実在

        private static AreaCatalogData ValidateDataAssets(List<string> errors)
        {
            RequireAsset<AreaDefinition>(Phase5AreaIds.AreaADataPath, "エリア A の Data", errors);
            RequireAsset<AreaDefinition>(Phase5AreaIds.AreaBDataPath, "エリア B の Data", errors);
            RequireAsset<EncounterData>(Phase5AreaIds.EncounterBDataPath, "遭遇戦の Data", errors);
            return RequireAsset<AreaCatalogData>(Phase5AreaIds.CatalogDataPath, "エリアカタログ", errors);
        }

        // ---------------------------------------------------------------- 経路（§13.3 の 1 行目）

        private static void ValidateCatalogRoutes(AreaCatalogData catalogData, List<string> errors)
        {
            if (catalogData == null)
            {
                return;
            }

            if (!AreaCatalog.TryBuild(catalogData, out AreaCatalog catalog, out IReadOnlyList<string> buildErrors))
            {
                errors.Add("カタログを構築できません: " + Join(buildErrors));
                return;
            }

            // A↔B の往復と、死亡再開の 3 経路（§3.1）。
            RequireEntry(catalog, Phase5AreaIds.AreaA, Phase5AreaIds.AreaAStart, errors);
            RequireEntry(catalog, Phase5AreaIds.AreaA, Phase5AreaIds.AreaAFromB, errors);
            RequireEntry(catalog, Phase5AreaIds.AreaB, Phase5AreaIds.AreaBFromA, errors);

            if (!catalog.TryGetRespawnEntry(out AreaEntryInfo respawn))
            {
                errors.Add("死亡再開点を解決できません（§9.1 手順 4）。");
            }
            else if (respawn.EntryId.Value != Phase5AreaIds.AreaAStart.Value
                || respawn.AreaId.Value != Phase5AreaIds.AreaA.Value)
            {
                errors.Add("死亡再開点は A の開始点であるべきです（§3.1）。いまは "
                    + respawn.AreaId.Value + "/" + respawn.EntryId.Value + "。");
            }

            // Scene が実在すること（パスだけ合っていて中身が無い、を通さない）。
            foreach (StableId areaId in new[] { Phase5AreaIds.AreaA, Phase5AreaIds.AreaB })
            {
                if (!catalog.TryGetScenePath(areaId, out string scenePath))
                {
                    errors.Add("カタログから " + areaId.Value + " の Scene を解決できません。");
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                {
                    errors.Add("カタログが指す Scene がありません: " + scenePath);
                }
            }
        }

        private static void RequireEntry(AreaCatalog catalog, StableId areaId, StableId entryId, List<string> errors)
        {
            if (!catalog.TryGetEntry(areaId, entryId, out _))
            {
                errors.Add("カタログから入口を解決できません: " + areaId.Value + "/" + entryId.Value);
            }
        }

        // ---------------------------------------------------------------- Build Settings（§13.1）

        private static void ValidateBuildSettings(List<string> errors)
        {
            for (int i = 0; i < RequiredScenePaths.Length; i++)
            {
                string path = RequiredScenePaths[i];
                if (!IsRegisteredAndEnabled(path))
                {
                    errors.Add("Build Settings に有効な登録がありません: " + path
                        + "（登録が無い、または無効になっています）。");
                }
            }
        }

        /// <summary>Build Settings に<b>有効な状態で</b>登録されているか。無効の登録は「登録あり」にしない。</summary>
        public static bool IsRegisteredAndEnabled(string scenePath)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] != null && scenes[i].path == scenePath)
                {
                    return scenes[i].enabled;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- 共有素材（§13.1 の「参照して使う」）

        private static void ValidateSharedAssets(List<string> errors, List<string> warnings)
        {
            RequirePrefab(Phase5AreaIds.EnemyMeleePrefabPath, "近接の敵 Prefab", errors);
            RequirePrefab(Phase5AreaIds.EnemyRangedPrefabPath, "弓の敵 Prefab", errors);

            if (AssetDatabase.LoadAssetAtPath<InvestigationSettingsData>(
                    Phase5AreaIds.InvestigationSettingsPath) == null)
            {
                errors.Add("調査の設定 Data がありません: " + Phase5AreaIds.InvestigationSettingsPath);
            }

            // 旧 Asset 汚染（§13.2 の「旧 Asset 汚染」）。生成先に見覚えのない Data が増えていないか。
            var expected = new HashSet<string>
            {
                Phase5AreaIds.AreaADataPath,
                Phase5AreaIds.AreaBDataPath,
                Phase5AreaIds.CatalogDataPath,
                Phase5AreaIds.EncounterBDataPath,

                // NavMesh の焼き上がりも同じ生成先に出る（Builder が置く正規の出力）。
                Phase5AreaIds.DataFolder + "/NavMesh_P5_A.asset",
                Phase5AreaIds.DataFolder + "/NavMesh_P5_B.asset",
            };

            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { Phase5AreaIds.DataFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path) || expected.Contains(path))
                {
                    continue;
                }

                warnings.Add("生成先に見覚えのない Asset があります: " + path
                    + "（作り直しで消えない残骸かもしれません）。");
            }
        }

        // ---------------------------------------------------------------- 敵構成と報酬（§13.3 の 3・12 行目）

        private static void ValidateEncounterAndRewards(List<string> errors)
        {
            var encounter = AssetDatabase.LoadAssetAtPath<EncounterData>(Phase5AreaIds.EncounterBDataPath);
            if (encounter == null)
            {
                return; // 実在は ValidateDataAssets が報告済み。
            }

            ValidateEncounter(encounter, errors);
        }

        /// <summary>
        /// 敵構成と報酬を検査する（§13.3 の 3・12 行目）。
        /// <b>Data を引数で受ける</b>：出荷 Data を壊さずに「空構成・Boss・合計違い」を検査へ流せるようにするため。
        /// </summary>
        public static void ValidateEncounter(EncounterData encounter, List<string> errors)
        {
            if (encounter == null)
            {
                errors.Add("遭遇戦の Data が空です。");
                return;
            }

            if (encounter.IsBossEncounter)
            {
                errors.Add("P5 に Boss Encounter は置けません（§8.1）。");
            }

            if (encounter.EnemyIds.Count == 0)
            {
                errors.Add("敵構成が空です（§13.3 の 3 行目）。");
                return;
            }

            int total = 0;
            for (int i = 0; i < encounter.EnemyIds.Count; i++)
            {
                StableId enemyId = encounter.EnemyIds[i];
                if (!TryFindEnemyData(enemyId, out EnemyArchetypeData data))
                {
                    errors.Add("敵 " + enemyId.Value + " の EnemyArchetypeData が見つかりません。");
                    continue;
                }

                RewardData reward = data.Reward;
                if (reward == null)
                {
                    errors.Add("敵 " + enemyId.Value + " に Reward が割り当てられていません（§8.5）。");
                    continue;
                }

                total += reward.VirtueAmount;
            }

            if (total != ExpectedFirstEncounterVirtue)
            {
                errors.Add("初回 Encounter の徳の合計は " + ExpectedFirstEncounterVirtue
                    + " であるべきですが " + total + " です（§8.5）。");
            }
        }

        private static bool TryFindEnemyData(StableId enemyId, out EnemyArchetypeData found)
        {
            found = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(EnemyArchetypeData)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<EnemyArchetypeData>(path);
                if (data != null && data.Id.Value == enemyId.Value)
                {
                    found = data;
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- ヘルパ

        private static T RequireAsset<T>(string path, string label, List<string> errors) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                errors.Add(label + "がありません: " + path
                    + "（Momotaro / Phase 5 / Generate Exploration Trial で生成します）。");
            }

            return asset;
        }

        private static void RequirePrefab(string path, string label, List<string> errors)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                errors.Add(label + "がありません: " + path);
                return;
            }

            if (prefab.GetComponentInChildren<EnemyActor>(true) == null)
            {
                errors.Add(label + " に EnemyActor がありません: " + path);
            }
        }

        private static string Join(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "(詳細なし)";
            }

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(" / ");
                }

                text.Append(values[i]);
            }

            return text.ToString();
        }
    }
}
