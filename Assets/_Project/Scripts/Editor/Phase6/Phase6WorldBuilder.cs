using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.Events;
using Momotaro.Data.Progression;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Momotaro.Editor.Phase6
{
    /// <summary>
    /// P6A の検証 campaign（最小 3 エリア A–B–C）を生成する（P6 仕様 §11。工程 P6A-06）。
    ///
    /// <b>地形生成器を複製しない。</b> A・B は P5 の <see cref="Phase5ExplorationBuilder"/> に設定と拡張
    /// （<see cref="Phase5AreaExtension"/>）を渡して作り、C は同じ部品（床・外周壁・境界・入口・出入口・常駐配線・
    /// 活動ゲート・NavMesh）で組む。
    ///
    /// 置くもの：A に初期お地蔵様と普通敵 2 体、B に独立した遭遇戦 2 つ・所持品の配置物・発見（遭遇戦のクリアで開く小部屋）、
    /// C にお地蔵様と仮ボス（既存の精鋭敵をデータで表す。専用 AI は作らない）。A–B–C は東西のスライドで繋ぎ、
    /// 遠隔移動（旅立ち）はお地蔵様の間を Fade で結ぶ。
    /// </summary>
    public static class Phase6WorldBuilder
    {
        /// <summary>生成結果。</summary>
        public readonly struct BuildResult
        {
            public BuildResult(bool success, string message, IReadOnlyList<string> outputs)
            {
                Success = success;
                Message = message;
                Outputs = outputs ?? new List<string>();
            }

            public bool Success { get; }
            public string Message { get; }
            public IReadOnlyList<string> Outputs { get; }
        }

        [MenuItem("Momotaro/Phase 6A/Generate Progression World")]
        private static void GenerateInteractive()
        {
            BuildResult result = BuildAll();
            EditorUtility.DisplayDialog(result.Success ? "P6A 検証ワールド" : "P6A 検証ワールド（失敗）",
                result.Message, "OK");
        }

        [MenuItem("Momotaro/Phase 6B/Generate Growth World")]
        private static void GenerateP6BInteractive()
        {
            BuildResult result = BuildP6B();
            EditorUtility.DisplayDialog(result.Success ? "P6B 検証ワールド" : "P6B 検証ワールド（失敗）",
                result.Message, "OK");
        }

        /// <summary>全部作る（P6A。従来どおり）。</summary>
        public static BuildResult BuildAll() => Build(Phase6Profile.P6A);

        /// <summary>
        /// P6B の検証ワールドを作る（P6B 04。P6A と同じ 3 エリア構成を設定で再利用し、専用 campaign・Data・Scene を生成）。
        /// P6A の生成物には触れない。
        /// </summary>
        public static BuildResult BuildP6B() => Build(Phase6Profile.P6B);

        /// <summary>指定の設定で全部作る（Data → A・B → C → カタログ → タイトル → Build Settings）。</summary>
        public static BuildResult Build(Phase6Profile profile)
        {
            Phase6Profile previous = Phase6WorldIds.Profile;
            Phase6WorldIds.Profile = profile ?? Phase6Profile.P6A;
            try
            {
                return BuildCurrent();
            }
            finally
            {
                Phase6WorldIds.Profile = previous;
            }
        }

        private static BuildResult BuildCurrent()
        {
            bool p6b = Phase6WorldIds.Profile.IsP6B;
            string tag = Phase6WorldIds.Profile.Tag;
            if (Phase5ExplorationBuilder.TryFindDirtyScene(out string dirty))
            {
                return new BuildResult(false,
                    "未保存の変更がある Scene があるため生成しませんでした: " + dirty, null);
            }

            // 戦闘 VFX の素材が揃っていること（P3.5 の Builder と同じ検査。欠けたまま Scene を作らない）。
            var vfxErrors = new List<string>();
            Momotaro.Editor.Phase35.Phase35CombatTrialBuilder.ValidateVfx(vfxErrors);
            if (vfxErrors.Count > 0)
            {
                return new BuildResult(false, "戦闘 VFX の素材が不足: " + string.Join(" / ", vfxErrors), null);
            }

            var outputs = new List<string>();
            Phase5Placeholder.EnsureFolder(Phase6WorldIds.DataFolder);
            Phase5Placeholder.EnsureFolder(Phase6WorldIds.SceneFolder);

            // ---- 1. Data（Scene が参照するので先に作る） ----
            // P6B は A の初到達で 300（全取得 270 が可能。仕様 §10）。ほかの探索報酬は P6A と同じ値（実報酬の経路の確認用）。
            EnsureReward("ArriveA", Phase6WorldIds.RewardId("arrive_a"), "A 初到達",
                p6b ? Phase6BTrialValues.ArrivalA : Phase6TrialValues.ArrivalA, true);
            EnsureReward("ArriveB", Phase6WorldIds.RewardId("arrive_b"), "B 初到達", Phase6TrialValues.ArrivalB, true);
            EnsureReward("ArriveC", Phase6WorldIds.RewardId("arrive_c"), "C 初到達", Phase6TrialValues.ArrivalC, true);
            EnsureReward("Kill", Phase6WorldIds.RewardId("kill"), "個別撃破", Phase6TrialValues.Kill, false);
            EnsureReward("ClearBNorth", Phase6WorldIds.RewardId("clear_b_north"), "B 北の殲滅", Phase6TrialValues.EncounterClear, true);
            EnsureReward("ClearBSouth", Phase6WorldIds.RewardId("clear_b_south"), "B 南の殲滅", Phase6TrialValues.EncounterClear, true);
            EnsureReward("ClearCBoss", Phase6WorldIds.RewardId("clear_c_boss"), "C 仮ボス", Phase6TrialValues.EncounterClear, true);
            EnsureReward("FindScroll", Phase6WorldIds.RewardId("find_scroll"), "発見（巻物）", Phase6TrialValues.Discovery, true);
            AssetDatabase.SaveAssets();
            var data = new WorldData();

            if (p6b)
            {
                EnsureP6BGrowth();
            }
            else
            {
                EnsureGrowth();
            }
            EnsureEncounter(Phase6WorldIds.EncounterBNorthPath, Phase6WorldIds.EncounterBNorth,
                "B 北の遭遇", new[] { Phase5AreaIds.EnemyMelee, Phase5AreaIds.EnemyMelee },
                data.ClearBNorth, Phase6WorldIds.FlagBCache, isBoss: false);
            EnsureEncounter(Phase6WorldIds.EncounterBSouthPath, Phase6WorldIds.EncounterBSouth,
                "B 南の遭遇", new[] { Phase5AreaIds.EnemyMelee, Phase5AreaIds.EnemyRanged },
                data.ClearBSouth, default, isBoss: false);
            EnsureEncounter(Phase6WorldIds.EncounterCBossPath, Phase6WorldIds.EncounterCBoss,
                "C 仮ボス", new[] { Phase6WorldIds.EnemyElite }, data.ClearCBoss, default, isBoss: true);

            AreaConnectionData connections = EnsureConnections();
            AssetDatabase.SaveAssets();
            outputs.Add(Phase6WorldIds.ConnectionDataPath);

            // ---- 2. A・B（P5 の Builder に設定と拡張を渡す） ----
            Phase5BuildTargets t = Targets(data);
            Phase5ExplorationBuilder.BuildResult ab = Phase5ExplorationBuilder.Build(t);
            outputs.AddRange(ab.Outputs);
            if (!ab.Success)
            {
                return new BuildResult(false, "A・B の生成に失敗: " + ab.Message, outputs);
            }

            // ---- 3. C ----
            AreaDefinition areaC = Phase5ExplorationBuilder.EnsureAreaDefinition(
                Phase6WorldIds.AreaCDataPath, Phase6WorldIds.AreaC, "エリア C（" + tag + " 検証）",
                Phase6WorldIds.AreaCScenePath,
                new[]
                {
                    (Phase6WorldIds.EntryCFromB, CardinalDirection.East),
                    (Phase6WorldIds.EntryCShrine, CardinalDirection.North),
                    (Phase6WorldIds.EntryCShrine2, CardinalDirection.North),
                },
                Phase6WorldIds.EntryCFromB);
            AssetDatabase.SaveAssets();
            Phase5ExplorationBuilder.EnsureResidentCameraRigPrefab();
            if (!Phase5ExplorationBuilder.BuildScene(Phase6WorldIds.AreaCScenePath,
                    root => PopulateAreaC(root,
                        AssetDatabase.LoadAssetAtPath<AreaDefinition>(Phase6WorldIds.AreaCDataPath),
                        Phase5ExplorationBuilder.EnsureResidentCameraRigPrefab(), t, data),
                    isStartupScene: false, out string errorC))
            {
                return new BuildResult(false, "エリア C の生成に失敗: " + errorC, outputs);
            }

            outputs.Add(Phase6WorldIds.AreaCScenePath);

            // ---- 4. Area Data の報酬・保存対象一覧、カタログ（campaign） ----
            AreaDefinition areaA = AssetDatabase.LoadAssetAtPath<AreaDefinition>(Phase6WorldIds.AreaADataPath);
            AreaDefinition areaB = AssetDatabase.LoadAssetAtPath<AreaDefinition>(Phase6WorldIds.AreaBDataPath);
            areaC = AssetDatabase.LoadAssetAtPath<AreaDefinition>(Phase6WorldIds.AreaCDataPath);
            ConfigureAreaData(areaA, areaB, areaC, data);
            AreaCatalogData catalog = EnsureCatalog(areaA, areaB, areaC, data);
            outputs.Add(Phase6WorldIds.CatalogDataPath);
            AssetDatabase.SaveAssets();

            // ---- 5. タイトル（New Game／Continue） ----
            if (!Phase5ExplorationBuilder.BuildScene(Phase6WorldIds.TitleScenePath,
                    root => PopulateTitle(root), isStartupScene: true, out string errorT))
            {
                return new BuildResult(false, "タイトルの生成に失敗: " + errorT, outputs);
            }

            outputs.Add(Phase6WorldIds.TitleScenePath);

            int added = Phase5ExplorationBuilder.EnsureBuildSettings(new[]
            {
                Phase6WorldIds.TitleScenePath, Phase6WorldIds.AreaAScenePath,
                Phase6WorldIds.AreaBScenePath, Phase6WorldIds.AreaCScenePath,
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            return new BuildResult(true,
                tag + " 検証ワールド：Scene 4 件（タイトル／A／B／C）、接続 " + connections.Connections.Count
                + " 件、Build Settings へ " + added + " 件を追加。", outputs);
        }

        // ================================================================ Data

        /// <summary>
        /// 生成した Data への参照。<b>値を持ち回らず、使うたびにパスから読み直す。</b>
        /// Scene を Single で開き直すと参照の無い Asset が退避され、持ち回った参照は「破棄済み」になる
        /// （実際に踏んだ：遭遇戦の Data が Scene 側で null になり、調停が未配線のまま保存された）。
        /// </summary>
        private sealed class WorldData
        {
            private static RewardData Reward(string file) =>
                AssetDatabase.LoadAssetAtPath<RewardData>(Phase6WorldIds.RewardPath(file));

            public RewardData ArrivalA => Reward("ArriveA");
            public RewardData ArrivalB => Reward("ArriveB");
            public RewardData ArrivalC => Reward("ArriveC");
            public RewardData Kill => Reward("Kill");
            public RewardData ClearBNorth => Reward("ClearBNorth");
            public RewardData ClearBSouth => Reward("ClearBSouth");
            public RewardData ClearCBoss => Reward("ClearCBoss");
            public RewardData FindScroll => Reward("FindScroll");
            public SkillNodeData Growth => AssetDatabase.LoadAssetAtPath<SkillNodeData>(Phase6WorldIds.GrowthVitalityPath);

            /// <summary>この campaign の成長ノード（P6A は 1 つ、P6B は 9 つ。定義順）。</summary>
            public List<SkillNodeData> GrowthNodes
            {
                get
                {
                    if (!Phase6WorldIds.Profile.IsP6B)
                    {
                        return new List<SkillNodeData> { Growth };
                    }

                    var list = new List<SkillNodeData>();
                    foreach (Phase6BTrialValues.Node n in Phase6BTrialValues.Nodes)
                    {
                        list.Add(AssetDatabase.LoadAssetAtPath<SkillNodeData>(Phase6WorldIds.GrowthNodePath(n.FileName)));
                    }

                    return list;
                }
            }
            public EncounterData EncounterBNorth => AssetDatabase.LoadAssetAtPath<EncounterData>(Phase6WorldIds.EncounterBNorthPath);
            public EncounterData EncounterBSouth => AssetDatabase.LoadAssetAtPath<EncounterData>(Phase6WorldIds.EncounterBSouthPath);
            public EncounterData EncounterCBoss => AssetDatabase.LoadAssetAtPath<EncounterData>(Phase6WorldIds.EncounterCBossPath);
        }

        private static RewardData EnsureReward(string file, string id, string displayName, int virtue, bool grantOnce)
        {
            string path = Phase6WorldIds.RewardPath(file);
            var asset = AssetDatabase.LoadAssetAtPath<RewardData>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<RewardData>();
                AssetDatabase.CreateAsset(asset, path);
            }

            Phase5ExplorationBuilder.SetIdentity(asset, new StableId(id), displayName);
            var so = new SerializedObject(asset);
            so.FindProperty("_virtueAmount").intValue = virtue;
            so.FindProperty("_grantOnce").boolValue = grantOnce;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static SkillNodeData EnsureGrowth()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SkillNodeData>(Phase6WorldIds.GrowthVitalityPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SkillNodeData>();
                AssetDatabase.CreateAsset(asset, Phase6WorldIds.GrowthVitalityPath);
            }

            Phase5ExplorationBuilder.SetIdentity(asset, Phase6WorldIds.GrowthVitality, "体力（検証用・最大 HP +10）");
            asset.EditorSet(Phase6TrialValues.GrowthCost, 0, Phase6TrialValues.GrowthMaxHp);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// P6B の浅い 9 ノード（仕様 §3）。先に全ノードを作ってから前提を結ぶ（参照先が未作成で null にならないよう）。
        /// </summary>
        private static void EnsureP6BGrowth()
        {
            var created = new Dictionary<string, SkillNodeData>();
            foreach (Phase6BTrialValues.Node n in Phase6BTrialValues.Nodes)
            {
                string path = Phase6WorldIds.GrowthNodePath(n.FileName);
                var asset = AssetDatabase.LoadAssetAtPath<SkillNodeData>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<SkillNodeData>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                created[n.Id] = asset;
            }

            foreach (Phase6BTrialValues.Node n in Phase6BTrialValues.Nodes)
            {
                SkillNodeData asset = created[n.Id];
                Phase5ExplorationBuilder.SetIdentity(asset, new StableId(n.Id), n.Name);
                var so = new SerializedObject(asset);
                so.FindProperty("_description").stringValue = n.Description;
                so.ApplyModifiedPropertiesWithoutUndo();
                var prerequisites = new List<SkillNodeData>();
                if (!string.IsNullOrEmpty(n.Prerequisite))
                {
                    prerequisites.Add(created[n.Prerequisite]);
                }

                asset.EditorSetP6B(n.Cost, n.Tier, n.MaxHp, n.Attack, n.Stamina, n.Poise, n.Heal, n.Capacity, prerequisites);
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
        }

        private static EncounterData EnsureEncounter(string path, StableId id, string displayName, StableId[] enemies,
            RewardData clearReward, StableId unlockFlag, bool isBoss)
        {
            var asset = AssetDatabase.LoadAssetAtPath<EncounterData>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<EncounterData>();
                AssetDatabase.CreateAsset(asset, path);
            }

            Phase5ExplorationBuilder.SetIdentity(asset, id, displayName);
            var so = new SerializedObject(asset);
            SerializedProperty ids = so.FindProperty("_enemyIds");
            ids.arraySize = enemies.Length;
            for (int i = 0; i < enemies.Length; i++)
            {
                ids.GetArrayElementAtIndex(i).FindPropertyRelative("_value").stringValue = enemies[i].Value;
            }

            so.FindProperty("_spawnPointId").FindPropertyRelative("_value").stringValue = "spawn_" + id.Value;
            so.ApplyModifiedPropertiesWithoutUndo();
            asset.EditorSetP6(clearReward, unlockFlag, isBoss);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static AreaConnectionData EnsureConnections()
        {
            var asset = AssetDatabase.LoadAssetAtPath<AreaConnectionData>(Phase6WorldIds.ConnectionDataPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AreaConnectionData>();
                AssetDatabase.CreateAsset(asset, Phase6WorldIds.ConnectionDataPath);
            }

            Phase5ExplorationBuilder.SetIdentity(asset, Phase6WorldIds.Connections,
                Phase6WorldIds.Profile.Tag + " エリア接続（東西 A–B–C）");

            var aToB = new AreaConnectionDefinition();
            aToB.Configure(Phase6WorldIds.ConnectionAToB, Phase6WorldIds.AreaA, Phase6WorldIds.ExitAEast,
                Phase6WorldIds.AreaB, Phase5AreaIds.AreaBFromA, AreaTransitionStyle.Slide,
                AreaConnectionDirection.East, Phase6WorldIds.ConnectionBToA, corridorWidth: Phase6WorldLayout.PassageWidth);
            var bToA = new AreaConnectionDefinition();
            bToA.Configure(Phase6WorldIds.ConnectionBToA, Phase6WorldIds.AreaB, Phase6WorldIds.ExitBWest,
                Phase6WorldIds.AreaA, Phase5AreaIds.AreaAFromB, AreaTransitionStyle.Slide,
                AreaConnectionDirection.West, Phase6WorldIds.ConnectionAToB, corridorWidth: Phase6WorldLayout.PassageWidth);
            var bToC = new AreaConnectionDefinition();
            bToC.Configure(Phase6WorldIds.ConnectionBToC, Phase6WorldIds.AreaB, Phase6WorldIds.ExitBEast,
                Phase6WorldIds.AreaC, Phase6WorldIds.EntryCFromB, AreaTransitionStyle.Slide,
                AreaConnectionDirection.East, Phase6WorldIds.ConnectionCToB, corridorWidth: Phase6WorldLayout.PassageWidth);
            var cToB = new AreaConnectionDefinition();
            cToB.Configure(Phase6WorldIds.ConnectionCToB, Phase6WorldIds.AreaC, Phase6WorldIds.ExitCWest,
                Phase6WorldIds.AreaB, Phase6WorldIds.EntryBFromC, AreaTransitionStyle.Slide,
                AreaConnectionDirection.West, Phase6WorldIds.ConnectionBToC, corridorWidth: Phase6WorldLayout.PassageWidth);

            asset.SetConnections(new List<AreaConnectionDefinition> { aToB, bToA, bToC, cToB });
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void ConfigureAreaData(AreaDefinition a, AreaDefinition b, AreaDefinition c, WorldData data)
        {
            a.EditorSetArrivalReward(data.ArrivalA);
            b.EditorSetArrivalReward(data.ArrivalB);
            c.EditorSetArrivalReward(data.ArrivalC);

            var ma = new AreaContentManifest();
            ma.EditorSet(new List<StableId>(), new List<StableId>(),
                new List<StableId> { Phase6WorldIds.FieldA1, Phase6WorldIds.FieldA2 },
                new List<StableId>(), new List<StableId> { Phase5AreaIds.FlagAGate },
                new List<StableId> { Phase5AreaIds.PointAOpen, Phase5AreaIds.PointABlocked });
            a.EditorSetContent(ma);

            var mb = new AreaContentManifest();
            mb.EditorSet(new List<StableId> { Phase6WorldIds.EncounterBNorth, Phase6WorldIds.EncounterBSouth },
                new List<StableId>(), new List<StableId>(),
                new List<StableId> { Phase6WorldIds.PickupTonic, Phase6WorldIds.FindScroll },
                new List<StableId> { Phase6WorldIds.FlagBCache },
                new List<StableId> { Phase5AreaIds.PointBOpen });
            b.EditorSetContent(mb);

            var mc = new AreaContentManifest();
            mc.EditorSet(new List<StableId> { Phase6WorldIds.EncounterCBoss },
                new List<StableId> { Phase6WorldIds.EncounterCBoss }, new List<StableId>(),
                new List<StableId>(), new List<StableId>(),
                new List<StableId> { Phase6WorldIds.PointCOpen });
            c.EditorSetContent(mc);

            EditorUtility.SetDirty(a);
            EditorUtility.SetDirty(b);
            EditorUtility.SetDirty(c);
        }

        private static AreaCatalogData EnsureCatalog(AreaDefinition a, AreaDefinition b, AreaDefinition c, WorldData data)
        {
            var asset = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase6WorldIds.CatalogDataPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AreaCatalogData>();
                AssetDatabase.CreateAsset(asset, Phase6WorldIds.CatalogDataPath);
            }

            bool p6b = Phase6WorldIds.Profile.IsP6B;
            Phase5ExplorationBuilder.SetIdentity(asset, Phase6WorldIds.Campaign, Phase6WorldIds.Profile.Tag + " 検証 campaign");
            asset.EditorSet(new List<AreaDefinition> { a, b, c }, Phase6WorldIds.AreaA, Phase5AreaIds.AreaAStart);

            var shrineA = new ShrineDefinition();
            shrineA.EditorSet(Phase6WorldIds.ShrineA, Phase6WorldIds.AreaA, Phase6WorldIds.EntryAShrine, "A のお地蔵様");
            var shrineC = new ShrineDefinition();
            shrineC.EditorSet(Phase6WorldIds.ShrineC, Phase6WorldIds.AreaC, Phase6WorldIds.EntryCShrine, "C のお地蔵様（ボス前）");
            var shrineC2 = new ShrineDefinition();
            shrineC2.EditorSet(Phase6WorldIds.ShrineC2, Phase6WorldIds.AreaC, Phase6WorldIds.EntryCShrine2, "C のお地蔵様（2）");
            var tonic = new ItemDefinition();
            tonic.EditorSet(Phase6WorldIds.ItemTonic, Phase6WorldIds.TonicMaxStack, "強壮薬（検証用）");

            asset.EditorSetCampaign(EncounterClearPolicy.Permanent, 1,
                new List<ShrineDefinition> { shrineA, shrineC, shrineC2 }, Phase6WorldIds.ShrineA,
                p6b ? Phase6BTrialValues.KibidangoCapacity : Phase6TrialValues.KibidangoCapacity,
                new List<ItemDefinition> { tonic }, data.GrowthNodes);
            asset.EditorSetKnownIds(
                new List<StableId>
                {
                    data.ClearBNorth.Id, data.ClearBSouth.Id, data.ClearCBoss.Id, data.FindScroll.Id,
                },
                new List<StableId> { Momotaro.Gameplay.Companion.CompanionIds.Inumaru },
                new List<StableId> { Phase6WorldIds.QuestFixture });
            if (p6b)
            {
                // P6B：効果を実定義で測るためテスト用の倍率は掛けない（記録 001 §3）。使用・払い戻し・章・専用スロット。
                asset.EditorSetTestTuning(1f, 1f);
                asset.EditorSetP6B(Phase6BTrialValues.KibidangoBaseHeal, Phase6BTrialValues.KibidangoUseSeconds,
                    Phase6BTrialValues.KibidangoCommitSeconds, Phase6BTrialValues.KibidangoMoveSpeedMultiplier,
                    Phase6BTrialValues.RefundRightsInitial, Phase6BTrialValues.RefundRightsPerChapter,
                    Phase6BTrialValues.RefundRightsMax,
                    new List<StableId> { Phase6BTrialValues.ChapterFixture, Phase6BTrialValues.ChapterFixture2 },
                    Phase6BTrialValues.SaveSlot);
            }
            else
            {
                asset.EditorSetTestTuning(Phase6TrialValues.TestEnemyAttackScale, Phase6TrialValues.TestPlayerMaxHpScale);
            }

            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ================================================================ 配置（A・B は拡張、C は同じ部品で）

        /// <summary>A・B の設定（P5.5 の東西配置と同じ寸法・境界。P6A の ID・フォルダ・拡張）。</summary>
        public static Phase5BuildTargets Targets() => Targets(null);

        private static Phase5BuildTargets Targets(WorldData data)
        {
            var seamA = new Phase5SeamOpening(Phase5SeamSide.East, Phase6WorldLayout.SeamZ, Phase6WorldLayout.PassageWidth);
            var seamBWest = new Phase5SeamOpening(Phase5SeamSide.West, Phase6WorldLayout.SeamZ, Phase6WorldLayout.PassageWidth);
            var seamBEast = new Phase5SeamOpening(Phase5SeamSide.East, Phase6WorldLayout.SeamZ, Phase6WorldLayout.PassageWidth);
            Phase5BuildTargets p55 = Momotaro.Editor.Phase55.Phase55WorldLayout.Targets();

            return new Phase5BuildTargets
            {
                Label = Phase6WorldIds.Profile.Tag,
                SceneFolder = Phase6WorldIds.SceneFolder,
                DataFolder = Phase6WorldIds.DataFolder,
                AreaAScenePath = Phase6WorldIds.AreaAScenePath,
                AreaBScenePath = Phase6WorldIds.AreaBScenePath,
                TrialScenePath = Phase6WorldIds.TitleScenePath,
                AreaADataPath = Phase6WorldIds.AreaADataPath,
                AreaBDataPath = Phase6WorldIds.AreaBDataPath,
                CatalogDataPath = Phase6WorldIds.CatalogDataPath,
                CatalogId = Phase6WorldIds.Campaign,
                CatalogDisplayName = Phase6WorldIds.Profile.Tag + " 検証 campaign",
                ConnectionDataPath = Phase6WorldIds.ConnectionDataPath,
                TrialHeadline = Phase6WorldIds.Profile.Title + "（起動）",
                AreaAId = Phase6WorldIds.AreaA,
                AreaBId = Phase6WorldIds.AreaB,
                AreaAOrigin = Phase6WorldLayout.AreaAOrigin,
                AreaBOrigin = Phase6WorldLayout.AreaBOrigin,
                SeamConnectionForwardId = Phase6WorldIds.ConnectionAToB,
                SeamConnectionReverseId = Phase6WorldIds.ConnectionBToA,
                AreaASeam = seamA,
                AreaBSeam = seamBWest,
                ExitAToB = Phase6WorldIds.ExitAEast,
                ExitBToA = Phase6WorldIds.ExitBWest,
                AreaAGatePosition = p55.AreaAGatePosition,
                AreaAExitPosition = p55.AreaAExitPosition,
                AreaAExitDirection = Vector3.right,
                AreaAEntryFromB = p55.AreaAEntryFromB,
                AreaAEntryFromBAlternates = p55.AreaAEntryFromBAlternates,
                AreaBEntryFromA = p55.AreaBEntryFromA,
                AreaBEntryFromAAlternates = p55.AreaBEntryFromAAlternates,
                AreaBDoorToA = p55.AreaBDoorToA,
                AreaBExitDirection = Vector3.left,
                AreaAStartFacing = CardinalDirection.North,
                AreaAFromBFacing = CardinalDirection.West,
                AreaBFromAFacing = CardinalDirection.East,

                // ---- P6A の拡張 ----
                IncludeLegacyEncounter = false,
                IncludeCombatVfx = true,
                AreaBExtraSeam = seamBEast,
                ExtraSeamForwardId = Phase6WorldIds.ConnectionBToC,
                ExtraSeamReverseId = Phase6WorldIds.ConnectionCToB,
                ExtraEntriesA = new[] { (Phase6WorldIds.EntryAShrine, CardinalDirection.North) },
                ExtraEntriesB = new[] { (Phase6WorldIds.EntryBFromC, CardinalDirection.West) },
                ExtendA = data != null ? (System.Action<Phase5AreaExtension>)(ext => ExtendA(ext, data)) : null,
                ExtendB = data != null ? (System.Action<Phase5AreaExtension>)(ext => ExtendB(ext, data)) : null,
            };
        }

        private static void ExtendA(Phase5AreaExtension ext, WorldData data)
        {
            // お地蔵様（初期・死亡再開点）。
            AddShrine(ext.FixtureRoot, Phase6WorldIds.ShrineA, Phase6WorldIds.AreaA, Phase6WorldLayout.ShrineA, "お地蔵様");
            ext.EntryPoints.Add(Phase5ExplorationBuilder.CreateEntryPoint(ext.Entries, Phase6WorldIds.EntryAShrine,
                Phase6WorldLayout.EntryAShrine, Phase6WorldLayout.EntryAShrineAlternates, "お地蔵様の前"));

            // 普通敵 2 体（同じ敵種・別の配置 ID）。
            ext.Fixtures.FieldPlacements.Add(FieldPlacement(ext.FixtureRoot, Phase6WorldIds.FieldA1, Phase6WorldLayout.FieldA1));
            ext.Fixtures.FieldPlacements.Add(FieldPlacement(ext.FixtureRoot, Phase6WorldIds.FieldA2, Phase6WorldLayout.FieldA2));
            ext.Fixtures.FieldEnemyTable = EnemyTable();
            ext.Fixtures.FieldKillReward = data.Kill;
        }

        private static void ExtendB(Phase5AreaExtension ext, WorldData data)
        {
            // C への出入口と、C からの入口。
            AreaExitGate toC = Phase5ExplorationBuilder.CreateExitGate(ext.Root, "ExitGate_ToC", Phase6WorldLayout.ExitBToC,
                Phase5SeamAxis.X, Phase6WorldIds.AreaC, Phase6WorldIds.EntryCFromB, Vector3.right);
            toC.ConfigureExitId(Phase6WorldIds.ExitBEast);
            ext.ExitGates.Add(toC);
            Phase5ExplorationBuilder.CreateMarker(ext.Markers, "ExitToC", Phase6WorldLayout.ExitBToC,
                Phase5Placeholder.EntryColor, "C へ",
                Phase5ExplorationBuilder.AcrossSeam(Phase5SeamAxis.X, 0.6f, Phase5ExplorationBuilder.MarkerPlateHeight,
                    Phase5Layout.CorridorWidth));
            ext.EntryPoints.Add(Phase5ExplorationBuilder.CreateEntryPoint(ext.Entries, Phase6WorldIds.EntryBFromC,
                Phase6WorldLayout.EntryBFromC, Phase6WorldLayout.EntryBFromCAlternates, "C から"));

            // 独立した遭遇戦 2 つ（撤退できる）。
            AddEncounter(ext, data.EncounterBNorth, Phase6WorldLayout.BNorthArenaCenter, Phase6WorldLayout.BArenaSize,
                Phase6WorldLayout.BNorthTrigger, Phase6WorldLayout.BNorthSpawns, data.Kill);
            AddEncounter(ext, data.EncounterBSouth, Phase6WorldLayout.BSouthArenaCenter, Phase6WorldLayout.BArenaSize,
                Phase6WorldLayout.BSouthTrigger, Phase6WorldLayout.BSouthSpawns, data.Kill);

            // 北の遭遇戦のクリアで開く小部屋（発見）。
            Material wallMat = Phase5Placeholder.EnsureMaterial("M_P5_Wall", Phase5Placeholder.WallColor);
            Phase5ExplorationBuilder.CreateWall(ext.Environment, "Wall_Cache", Phase6WorldLayout.CacheWall,
                Phase6WorldLayout.CacheWallSize, wallMat);
            AreaFlagDoor cache = Phase5ExplorationBuilder.CreateFlagDoor(ext.FixtureRoot, Phase6WorldIds.FlagBCache,
                Phase6WorldLayout.CacheGate, Phase6WorldLayout.CacheGateSize);
            ext.Doors = ext.Doors ?? new List<AreaFlagDoor>();
            ext.Doors.Add(cache);
            AddPickup(ext.FixtureRoot, Phase6WorldIds.FindScroll, Phase6WorldIds.AreaB, default, 0, 0, data.FindScroll,
                Phase6WorldLayout.FindScroll, "巻物を調べる", "巻物");

            // 所持品の配置物（強壮薬 1 つ。上限 3）。
            AddPickup(ext.FixtureRoot, Phase6WorldIds.PickupTonic, Phase6WorldIds.AreaB, Phase6WorldIds.ItemTonic, 1,
                Phase6WorldIds.TonicMaxStack, null, Phase6WorldLayout.PickupTonic, "強壮薬を拾う", "強壮薬");
        }

        private static void PopulateAreaC(Transform root, AreaDefinition definition, GameObject residentRig,
            Phase5BuildTargets t, WorldData data)
        {
            Material floorMat = Phase5Placeholder.EnsureMaterial("M_P5_Floor", Phase5Placeholder.FloorColor);
            Material wallMat = Phase5Placeholder.EnsureMaterial("M_P5_Wall", Phase5Placeholder.WallColor);

            var env = new GameObject("Environment");
            env.transform.SetParent(root, false);
            float w = Phase6WorldLayout.AreaCWidth;
            float d = Phase6WorldLayout.AreaCDepth;
            var seamWest = new Phase5SeamOpening(Phase5SeamSide.West, Phase6WorldLayout.SeamZ, Phase6WorldLayout.PassageWidth);
            Phase5ExplorationBuilder.CreateBackdrop(env.transform, Vector3.zero, w, d);
            Phase5ExplorationBuilder.CreateFloor(env.transform, Vector3.zero, w, d, floorMat);
            Phase5ExplorationBuilder.CreateOuterWalls(env.transform, Vector3.zero, w, d, wallMat, seamWest);

            var markers = new GameObject("Markers");
            markers.transform.SetParent(root, false);
            Phase5Placeholder.CreateLabel("エリア C（ボス前）", markers.transform, new Vector3(0f, 0.2f, -1.5f), Color.white, 0.5f);
            Phase5ExplorationBuilder.CreateMarker(markers.transform, "ExitToB", Phase6WorldLayout.ExitCToB,
                Phase5Placeholder.EntryColor, "B へ",
                Phase5ExplorationBuilder.AcrossSeam(Phase5SeamAxis.X, 0.6f, Phase5ExplorationBuilder.MarkerPlateHeight,
                    Phase5Layout.CorridorWidth));

            var entries = new GameObject("Entries");
            entries.transform.SetParent(root, false);
            AreaEntryPoint fromB = Phase5ExplorationBuilder.CreateEntryPoint(entries.transform, Phase6WorldIds.EntryCFromB,
                Phase6WorldLayout.EntryCFromB, Phase6WorldLayout.EntryCFromBAlternates, "B から");
            AreaEntryPoint shrineEntry = Phase5ExplorationBuilder.CreateEntryPoint(entries.transform,
                Phase6WorldIds.EntryCShrine, Phase6WorldLayout.EntryCShrine, Phase6WorldLayout.EntryCShrineAlternates,
                "お地蔵様の前");
            AreaEntryPoint shrine2Entry = Phase5ExplorationBuilder.CreateEntryPoint(entries.transform,
                Phase6WorldIds.EntryCShrine2, Phase6WorldLayout.EntryCShrine2, Phase6WorldLayout.EntryCShrine2Alternates,
                "2 つ目のお地蔵様の前");

            AreaExitGate toB = Phase5ExplorationBuilder.CreateExitGate(root, "ExitGate_ToB", Phase6WorldLayout.ExitCToB,
                Phase5SeamAxis.X, Phase6WorldIds.AreaB, Phase6WorldIds.EntryBFromC, Vector3.left);
            toB.ConfigureExitId(Phase6WorldIds.ExitCWest);

            var fixtures = new Phase5ExplorationBuilder.Fixtures();
            Phase5ExplorationBuilder.CreateSeamBarrier(env.transform, Vector3.zero, w, d, seamWest, fixtures,
                Phase6WorldIds.ConnectionBToC, Phase6WorldIds.ConnectionCToB);
            var fixtureRoot = new GameObject("Fixtures");
            fixtureRoot.transform.SetParent(root, false);

            AddShrine(fixtureRoot.transform, Phase6WorldIds.ShrineC, Phase6WorldIds.AreaC, Phase6WorldLayout.ShrineC,
                "お地蔵様（ボス前）");
            AddShrine(fixtureRoot.transform, Phase6WorldIds.ShrineC2, Phase6WorldIds.AreaC, Phase6WorldLayout.ShrineC2,
                "お地蔵様（C の 2）");
            fixtures.Points.Add(Phase5ExplorationBuilder.CreateInvestigationPoint(fixtureRoot.transform, "Investigation",
                Phase6WorldIds.PointCOpen, Phase6WorldIds.DiscoveryCOpen, Phase6WorldLayout.PointC));

            var ext = new Phase5AreaExtension(root, env.transform, fixtureRoot.transform, markers.transform,
                entries.transform, definition, t, fixtures,
                new List<AreaEntryPoint> { fromB, shrineEntry, shrine2Entry }, new List<AreaExitGate> { toB }, null, null, null);
            AddEncounter(ext, data.EncounterCBoss, Phase6WorldLayout.CBossArenaCenter, Phase6WorldLayout.CBossArenaSize,
                Phase6WorldLayout.CBossTrigger, Phase6WorldLayout.CBossSpawns, data.Kill);

            var cameraRegions = new GameObject("CameraRegions");
            cameraRegions.transform.SetParent(root, false);
            fixtures.DefaultCameraRegion = Phase5ExplorationBuilder.CreateCameraRegion(cameraRegions.transform,
                Phase6WorldIds.RegionCDefault, 0, Vector3.zero, Phase5Layout.FollowRegionSize(w, d));

            AreaRoot areaRoot = root.gameObject.AddComponent<AreaRoot>();
            areaRoot.EditorSet(definition, ext.EntryPoints, ext.ExitGates, null, null, null,
                new List<AreaSeamBarrier>(fixtures.SeamBarriers));

            Phase5ExplorationBuilder.CreateAreaSystems(root, areaRoot, definition, fixtures, residentRig, t);
            Phase5ExplorationBuilder.ApplyAuthoringOrigin(root, Phase6WorldLayout.AreaCOrigin);
            Phase5ExplorationBuilder.BakeNavMesh(root, t.NavMeshAssetName("C"), t.DataFolder);
            Phase5ExplorationBuilder.CloseActivityGate(areaRoot);
        }

        private static void PopulateTitle(Transform root)
        {
            // <b>パスから読み直す。</b> Scene を Single で開き直すと持ち回った参照は破棄済みになり、null が焼かれる
            // （実際に踏んだ：実ビルドで「カタログが null」）。
            var catalog = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase6WorldIds.CatalogDataPath);
            root.gameObject.name = Phase6WorldIds.Profile.SceneTag + "TitleRoot";
            Phase5Placeholder.CreateLabel(Phase6WorldIds.Profile.Title, root, Vector3.zero, Color.white, 0.5f);
            Phase6CampaignLauncher launcher = root.gameObject.AddComponent<Phase6CampaignLauncher>();
            var so = new SerializedObject(launcher);
            so.FindProperty("_catalog").objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ================================================================ 部品

        private static List<Momotaro.Gameplay.Encounter.EnemyPrefabTable.Entry> EnemyTable()
        {
            var table = Phase5ExplorationBuilder.BuildEnemyPrefabEntries();
            table.Add(new Momotaro.Gameplay.Encounter.EnemyPrefabTable.Entry
            {
                EnemyId = Phase6WorldIds.EnemyElite,
                Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Phase6WorldIds.EnemyElitePrefabPath),
            });
            return table;
        }

        private static AreaFieldEnemyDirector.Placement FieldPlacement(Transform parent, StableId id, Vector3 position)
        {
            var go = new GameObject("FieldSpawn_" + id.Value);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Phase5Placeholder.CreateLabel("普通敵", go.transform, position + new Vector3(0f, 0.2f, 0f),
                Phase5Placeholder.ArenaColor, 0.14f);
            return new AreaFieldEnemyDirector.Placement
            {
                PlacementId = id,
                EnemyId = Phase5AreaIds.EnemyMelee,
                Point = go.transform,
            };
        }

        /// <summary>像の中心から受付の錨までの距離（北向き。像の手前）。</summary>
        public const float ShrineAnchorOffset = 0.7f;

        private static void AddShrine(Transform parent, StableId shrineId, StableId areaId, Vector3 position, string label)
        {
            var go = new GameObject("Shrine_" + shrineId.Value);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Material mat = Phase5Placeholder.EnsureMaterial("M_P6A_Shrine", new Color(0.55f, 0.58f, 0.62f));
            Phase5Placeholder.CreateBox("Body", go.transform, position + new Vector3(0f, 0.5f, 0f),
                new Vector3(0.8f, 1.0f, 0.6f), mat);
            Phase5Placeholder.CreateLabel(label, go.transform, position + new Vector3(0f, 1.3f, 0f), Color.white, 0.18f);
            // <b>受付の錨は像の手前（北）に置く。</b> 遮蔽判定の球は Default 層（壁と同じ層）に当たるので、
            // 錨を像の Collider の中に置くと、像そのものが遮蔽になって<b>一度も調べられない</b>
            // （P6A の実 Scene テストで発覚）。像の手前 0.7m（像の奥行き 0.3m ＋ 球の半径 0.25m ＋ 余裕）。
            var anchor = new GameObject("InteractAnchor");
            anchor.transform.SetParent(go.transform, false);
            anchor.transform.position = position + new Vector3(0f, 0f, ShrineAnchorOffset);
            ShrinePoint point = anchor.AddComponent<ShrinePoint>();
            point.Bind(shrineId, areaId, 1.6f);
        }

        private static void AddPickup(Transform parent, StableId placementId, StableId areaId, StableId itemId, int count,
            int maxStack, RewardData reward, Vector3 position, string prompt, string label)
        {
            var go = new GameObject("Pickup_" + placementId.Value);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            Material mat = Phase5Placeholder.EnsureMaterial("M_P6A_Pickup", new Color(0.85f, 0.75f, 0.35f));
            Phase5Placeholder.CreateBox("Body", visual.transform, position + new Vector3(0f, 0.2f, 0f),
                new Vector3(0.5f, 0.4f, 0.5f), mat, solid: false);
            Phase5Placeholder.CreateLabel(label, visual.transform, position + new Vector3(0f, 0.6f, 0f), Color.white, 0.14f);
            AreaPickupPoint pickup = go.AddComponent<AreaPickupPoint>();
            pickup.Bind(placementId, areaId, itemId, count, maxStack, reward, visual, prompt);
        }

        private static void AddEncounter(Phase5AreaExtension ext, EncounterData data, Vector3 center, Vector2 size,
            Vector3 triggerPosition, Vector3[] spawns, RewardData killReward)
        {
            string id = data.Id.Value;
            var boundaryGo = new GameObject("ArenaBoundary_" + id);
            boundaryGo.transform.SetParent(ext.FixtureRoot, false);
            boundaryGo.transform.position = center;
            AreaArenaBoundary arena = boundaryGo.AddComponent<AreaArenaBoundary>();
            Phase5ExplorationBuilder.IgnoreFromNavMeshBuild(boundaryGo);

            Material arenaMat = Phase5Placeholder.EnsureMaterial("M_P5_Arena", Phase5Placeholder.ArenaColor);
            var outline = new GameObject("ArenaOutline_" + id);
            outline.transform.SetParent(ext.Markers, false);
            Phase5ExplorationBuilder.CreateOutline(outline.transform, center, size.x, size.y, arenaMat);

            var triggerGo = new GameObject("EncounterTrigger_" + id);
            triggerGo.transform.SetParent(ext.FixtureRoot, false);
            triggerGo.transform.position = triggerPosition;
            BoxCollider box = triggerGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2f, Phase5Layout.WallHeight, 2f);
            box.center = new Vector3(0f, Phase5Layout.WallHeight * 0.5f, 0f);
            AreaEncounterTrigger trigger = triggerGo.AddComponent<AreaEncounterTrigger>();
            Phase5Placeholder.CreateLabel(data.IsBossEncounter ? "仮ボス" : "遭遇", triggerGo.transform,
                triggerPosition + new Vector3(0f, 0.2f, 0f), Phase5Placeholder.ArenaColor, 0.22f);
            Phase5ExplorationBuilder.IgnoreFromNavMeshBuild(triggerGo);

            var fixture = new Phase5ExplorationBuilder.EncounterFixture
            {
                Data = data,
                Arena = arena,
                Trigger = trigger,
                ArenaSize = size,
                AllowRetreat = true,
                Table = EnemyTable(),
                KillReward = killReward,
            };

            for (int i = 0; i < spawns.Length; i++)
            {
                var sp = new GameObject("Spawn_" + id + "_" + i);
                sp.transform.SetParent(ext.FixtureRoot, false);
                sp.transform.position = spawns[i];
                fixture.SpawnPoints.Add(sp.transform);
            }

            ext.Fixtures.Encounters.Add(fixture);
        }
    }
}
