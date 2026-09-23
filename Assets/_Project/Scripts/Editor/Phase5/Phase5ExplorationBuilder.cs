using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.World;
using Momotaro.Gameplay.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase5
{
    /// <summary>
    /// P5 の仮エリアと Area／Entry カタログを生成する（P5-02。仕様書 v1.1 §3／§13.1）。
    ///
    /// P5-02 の範囲は<b>地形・入口・カタログまで</b>。仕掛け（P5-04）、経路（P5-05）、カメラ（P5-06）、
    /// Encounter（P5-07）は、それぞれの工程でこの Builder へ足していく。
    /// いまは後続がぶら下がる位置に<b>ラベル付きの目印</b>だけを置き、実処理は先回りして作らない。
    ///
    /// <b>未保存 Scene があれば何もしない。</b> 手で加えた変更を黙って消さないため、
    /// 理由付きで終了する（§13.1、受入 V04）。実処理はダイアログ無しの public static に分け、
    /// メニューは薄いラッパーにしてある（`CLAUDE.md` の Editor 常駐ブリッジの作法）。
    /// </summary>
    public static class Phase5ExplorationBuilder
    {
        /// <summary>生成の結果。</summary>
        public readonly struct BuildResult
        {
            public bool Success { get; }
            public string Message { get; }
            public IReadOnlyList<string> Outputs { get; }

            public BuildResult(bool success, string message, IReadOnlyList<string> outputs)
            {
                Success = success;
                Message = message;
                Outputs = outputs ?? new List<string>();
            }
        }

        /// <summary>メニュー（薄いラッパー。ダイアログはここだけ）。</summary>
        [MenuItem("Momotaro/Phase 5/Generate Exploration Trial")]
        public static void GenerateInteractive()
        {
            BuildResult r = BuildAll();
            if (!r.Success)
            {
                EditorUtility.DisplayDialog("P5 探索試遊の生成に失敗", r.Message, "OK");
                return;
            }

            Debug.Log("[Phase5] 探索試遊を生成しました — " + r.Message);
        }

        /// <summary>
        /// 3 Scene・Data カタログ・仮地形を生成する（ダイアログ無し。ブリッジとテストから呼ぶ）。
        /// 未保存の Scene 変更があれば<b>何も変更せずに</b>失敗を返す。
        /// </summary>
        public static BuildResult BuildAll()
        {
            if (TryFindDirtyScene(out string dirty))
            {
                return new BuildResult(false,
                    "未保存の変更がある Scene があるため生成しませんでした: " + dirty
                    + "\n保存するか破棄してから再実行してください（仕様書 §13.1）。", null);
            }

            var outputs = new List<string>();

            // 1. Data を先に作る。Scene 側の AreaRoot がこれを参照するため。
            Phase5Placeholder.EnsureFolder(Phase5AreaIds.DataFolder);
            AreaDefinition areaA = EnsureAreaDefinition(
                Phase5AreaIds.AreaADataPath, Phase5AreaIds.AreaA, "エリア A（試作）",
                Phase5AreaIds.AreaAScenePath,
                new[]
                {
                    (Phase5AreaIds.AreaAStart, CardinalDirection.North),
                    (Phase5AreaIds.AreaAFromB, CardinalDirection.West),
                },
                Phase5AreaIds.AreaAStart);

            AreaDefinition areaB = EnsureAreaDefinition(
                Phase5AreaIds.AreaBDataPath, Phase5AreaIds.AreaB, "エリア B（試作）",
                Phase5AreaIds.AreaBScenePath,
                new[]
                {
                    (Phase5AreaIds.AreaBFromA, CardinalDirection.East),
                },
                Phase5AreaIds.AreaBFromA);

            AreaCatalogData catalog = EnsureCatalog(areaA, areaB);
            outputs.Add(Phase5AreaIds.AreaADataPath);
            outputs.Add(Phase5AreaIds.AreaBDataPath);
            outputs.Add(Phase5AreaIds.CatalogDataPath);

            AssetDatabase.SaveAssets();

            // 2. Scene を作る。どれか 1 つでも失敗したら、その時点で止める。
            Phase5Placeholder.EnsureFolder(Phase5AreaIds.SceneFolder);

            if (!BuildScene(Phase5AreaIds.AreaAScenePath, root => PopulateAreaA(root, areaA), out string errorA))
            {
                return new BuildResult(false, "エリア A の生成に失敗: " + errorA, outputs);
            }

            outputs.Add(Phase5AreaIds.AreaAScenePath);

            if (!BuildScene(Phase5AreaIds.AreaBScenePath, root => PopulateAreaB(root, areaB), out string errorB))
            {
                return new BuildResult(false, "エリア B の生成に失敗: " + errorB, outputs);
            }

            outputs.Add(Phase5AreaIds.AreaBScenePath);

            if (!BuildScene(Phase5AreaIds.TrialScenePath, PopulateTrial, out string errorT))
            {
                return new BuildResult(false, "統合起動 Scene の生成に失敗: " + errorT, outputs);
            }

            outputs.Add(Phase5AreaIds.TrialScenePath);

            // 3. Build Settings は既存項目を維持して追記・更新する（§13.1）。
            int added = EnsureBuildSettings(new[]
            {
                Phase5AreaIds.TrialScenePath, Phase5AreaIds.AreaAScenePath, Phase5AreaIds.AreaBScenePath,
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 生成後は空の Scene を開いておく（生成物を開いたままにして誤編集させない）。
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            return new BuildResult(true,
                "Data 3 件（A／B／カタログ）、Scene 3 件（A／B／統合起動）。Build Settings へ "
                + added + " 件を追加。死亡再開点は " + Phase5AreaIds.AreaA.Value + "/" + Phase5AreaIds.AreaAStart.Value + "。",
                outputs);
        }

        // ---------------------------------------------------------------- Data

        private static AreaDefinition EnsureAreaDefinition(
            string path, StableId areaId, string displayName, string scenePath,
            (StableId id, CardinalDirection facing)[] entries, StableId defaultEntry)
        {
            var asset = AssetDatabase.LoadAssetAtPath<AreaDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AreaDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            SetIdentity(asset, areaId, displayName);

            var list = new List<AreaEntryDefinition>();
            foreach ((StableId id, CardinalDirection facing) in entries)
            {
                var e = new AreaEntryDefinition();
                e.EditorSet(id, facing);
                list.Add(e);
            }

            asset.EditorSet(scenePath, AreaDefinition.SupportedFloorId, list, defaultEntry);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static AreaCatalogData EnsureCatalog(AreaDefinition areaA, AreaDefinition areaB)
        {
            var asset = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase5AreaIds.CatalogDataPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AreaCatalogData>();
                AssetDatabase.CreateAsset(asset, Phase5AreaIds.CatalogDataPath);
            }

            SetIdentity(asset, Phase5AreaIds.Catalog, "P5 エリアカタログ");
            asset.EditorSet(
                new List<AreaDefinition> { areaA, areaB },
                Phase5AreaIds.AreaA, Phase5AreaIds.AreaAStart);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>GameDataAsset の共通フィールド（安定 ID・表示名）は private なので SerializedObject で書く。</summary>
        private static void SetIdentity(Object asset, StableId id, string displayName)
        {
            var so = new SerializedObject(asset);
            SerializedProperty idProp = so.FindProperty("_id");
            if (idProp != null)
            {
                // StableId は string 1 本の struct。相対パスで中の値へ入れる。
                SerializedProperty value = idProp.FindPropertyRelative("_value");
                if (value != null)
                {
                    value.stringValue = id.Value;
                }
            }

            SerializedProperty nameProp = so.FindProperty("_displayName");
            if (nameProp != null)
            {
                nameProp.stringValue = displayName;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- Scene

        private static bool TryFindDirtyScene(out string sceneName)
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene s = EditorSceneManager.GetSceneAt(i);
                if (s.isDirty)
                {
                    sceneName = string.IsNullOrEmpty(s.path) ? "(無題 Scene)" : s.path;
                    return true;
                }
            }

            sceneName = null;
            return false;
        }

        private static bool BuildScene(string path, System.Action<Transform> populate, out string error)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var root = new GameObject("AreaRoot");
                CreateCommonSceneObjects();
                populate(root.transform);
            }
            catch (System.Exception e)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                error = "生成中に例外: " + e.Message;
                return false;
            }

            if (!EditorSceneManager.SaveScene(scene, path))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                error = "Scene の保存に失敗しました: " + path;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>どの Scene にも要る最小構成（カメラ・ライト）。活動中は各 1 つ（§11）。</summary>
        private static void CreateCommonSceneObjects()
        {
            var lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 14f, -10f);
            cameraGo.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            cameraGo.AddComponent<AudioListener>();
        }

        private static AreaEntryPoint CreateEntryPoint(
            Transform parent, StableId entryId, Vector3 position, Vector3[] alternates, string label)
        {
            var go = new GameObject("Entry_" + entryId.Value);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var alternateTransforms = new List<Transform>();
            for (int i = 0; i < alternates.Length; i++)
            {
                var alt = new GameObject("Alt_" + i);
                alt.transform.SetParent(go.transform, false);
                alt.transform.position = alternates[i];
                alternateTransforms.Add(alt.transform);
            }

            AreaEntryPoint point = go.AddComponent<AreaEntryPoint>();
            point.EditorSet(entryId, alternateTransforms);

            Material mat = Phase5Placeholder.EnsureMaterial("M_P5_Entry", Phase5Placeholder.EntryColor);
            Phase5Placeholder.CreateBox("Marker", go.transform, position + new Vector3(0f, 0.02f, 0f),
                new Vector3(1.2f, 0.04f, 1.2f), mat, solid: false);
            Phase5Placeholder.CreateLabel(label, go.transform, position + new Vector3(0f, 0.1f, 1.2f),
                Phase5Placeholder.EntryColor, 0.16f);
            return point;
        }

        /// <summary>目印だけの箱（当たり判定なし）。後続工程がここへ機能を足す。</summary>
        private static void CreateMarker(
            Transform parent, string name, Vector3 position, Color color, string label, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            Material mat = Phase5Placeholder.EnsureMaterial("M_P5_" + name, color);
            Phase5Placeholder.CreateBox("Body", go.transform, position + new Vector3(0f, size.y * 0.5f, 0f),
                size, mat, solid: false);
            Phase5Placeholder.CreateLabel(label, go.transform, position + new Vector3(0f, size.y + 0.2f, 0f),
                color, 0.16f);
        }

        private static int EnsureBuildSettings(string[] paths)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int added = 0;
            foreach (string path in paths)
            {
                bool exists = false;
                foreach (EditorBuildSettingsScene s in scenes)
                {
                    if (s.path == path)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                    added++;
                }
            }

            if (added > 0)
            {
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            return added;
        }

        // ---------------------------------------------------------------- 地形

        private static void PopulateAreaA(Transform root, AreaDefinition definition)
        {
            Material floorMat = Phase5Placeholder.EnsureMaterial("M_P5_Floor", Phase5Placeholder.FloorColor);
            Material wallMat = Phase5Placeholder.EnsureMaterial("M_P5_Wall", Phase5Placeholder.WallColor);
            Material waterMat = Phase5Placeholder.EnsureMaterial("M_P5_Water", Phase5Placeholder.WaterColor);

            var env = new GameObject("Environment");
            env.transform.SetParent(root, false);

            const float w = Phase5Layout.AreaAWidth;
            const float d = Phase5Layout.AreaADepth;

            CreateFloor(env.transform, Vector3.zero, w, d, floorMat);
            CreateOuterWalls(env.transform, Vector3.zero, w, d, wallMat);

            // L 字通路：西の大部屋と東の通路を仕切り、南側だけ開ける。
            // 直線追従では仕切りに当たり、NavMesh 経路なら南の開口を回って到達できる（§3.2）。
            float dividerNorthEnd = d * 0.5f;
            float dividerLength = dividerNorthEnd - Phase5Layout.AreaADividerGapNorthZ;
            CreateWall(env.transform, "Wall_Divider",
                new Vector3(Phase5Layout.AreaADividerX, 0f,
                    Phase5Layout.AreaADividerGapNorthZ + dividerLength * 0.5f),
                new Vector3(Phase5Layout.WallThickness, Phase5Layout.WallHeight, dividerLength), wallMat);

            // 壁越し調査を成立させる衝立。
            CreateWall(env.transform, "Wall_Screen", Phase5Layout.AreaABlockingScreen,
                new Vector3(Phase5Layout.WallThickness, Phase5Layout.WallHeight, 4f), wallMat);

            // 水場：見た目は薄い板、通行止めは透明な境界で行う（落下・水泳は実装しない。§3.3）。
            var water = new GameObject("Water");
            water.transform.SetParent(env.transform, false);
            Phase5Placeholder.CreateBox("Water_Visual", water.transform,
                Phase5Layout.AreaAWater + new Vector3(0f, 0.03f, 0f),
                new Vector3(Phase5Layout.AreaAWaterSize.x, 0.06f, Phase5Layout.AreaAWaterSize.y),
                waterMat, solid: false);
            Phase5Placeholder.CreateBlocker("Water_Boundary", water.transform,
                Phase5Layout.AreaAWater + new Vector3(0f, Phase5Layout.WallHeight * 0.5f, 0f),
                new Vector3(Phase5Layout.AreaAWaterSize.x, Phase5Layout.WallHeight, Phase5Layout.AreaAWaterSize.y));
            Phase5Placeholder.CreateLabel("水場", water.transform,
                Phase5Layout.AreaAWater + new Vector3(0f, 0.2f, 0f), Color.white, 0.22f);

            // 目印（機能は後続工程で足す）。
            var markers = new GameObject("Markers");
            markers.transform.SetParent(root, false);
            Phase5Placeholder.CreateLabel("エリア A", markers.transform, new Vector3(0f, 0.2f, -1.5f), Color.white, 0.5f);
            CreateMarker(markers.transform, "Gate", Phase5Layout.AreaAGate,
                Phase5Placeholder.GateColor, "門", new Vector3(Phase5Layout.CorridorWidth, 1.6f, 0.4f));
            CreateMarker(markers.transform, "Lever", Phase5Layout.AreaALever,
                Phase5Placeholder.LeverColor, "レバー", new Vector3(0.5f, 1.2f, 0.5f));
            CreateMarker(markers.transform, "Investigation", Phase5Layout.AreaAInvestigation,
                Phase5Placeholder.InvestigationColor, "調査", new Vector3(0.8f, 0.6f, 0.8f));
            CreateMarker(markers.transform, "InvestigationBlocked", Phase5Layout.AreaAInvestigationBlocked,
                Phase5Placeholder.InvestigationColor, "調査(壁越し)", new Vector3(0.8f, 0.6f, 0.8f));
            CreateMarker(markers.transform, "ExitToB", Phase5Layout.AreaAExitToB,
                Phase5Placeholder.EntryColor, "B へ", new Vector3(0.6f, 1.6f, Phase5Layout.CorridorWidth));

            // 入口。
            var entries = new GameObject("Entries");
            entries.transform.SetParent(root, false);
            AreaEntryPoint start = CreateEntryPoint(entries.transform, Phase5AreaIds.AreaAStart,
                Phase5Layout.AreaAStart, Phase5Layout.AreaAStartAlternates, "開始／再開");
            AreaEntryPoint fromB = CreateEntryPoint(entries.transform, Phase5AreaIds.AreaAFromB,
                Phase5Layout.AreaAFromB, Phase5Layout.AreaAFromBAlternates, "B から");

            AreaRoot areaRoot = root.gameObject.AddComponent<AreaRoot>();
            areaRoot.EditorSet(definition, new List<AreaEntryPoint> { start, fromB });
        }

        private static void PopulateAreaB(Transform root, AreaDefinition definition)
        {
            Material floorMat = Phase5Placeholder.EnsureMaterial("M_P5_Floor", Phase5Placeholder.FloorColor);
            Material wallMat = Phase5Placeholder.EnsureMaterial("M_P5_Wall", Phase5Placeholder.WallColor);

            var env = new GameObject("Environment");
            env.transform.SetParent(root, false);

            const float w = Phase5Layout.AreaBWidth;
            const float d = Phase5Layout.AreaBDepth;

            CreateFloor(env.transform, Vector3.zero, w, d, floorMat);
            CreateOuterWalls(env.transform, Vector3.zero, w, d, wallMat);

            var markers = new GameObject("Markers");
            markers.transform.SetParent(root, false);
            Phase5Placeholder.CreateLabel("エリア B", markers.transform, new Vector3(0f, 0.2f, -1.5f), Color.white, 0.5f);
            CreateMarker(markers.transform, "DoorToA", Phase5Layout.AreaBDoorToA,
                Phase5Placeholder.EntryColor, "A へ（扉）", new Vector3(0.6f, 1.8f, 2.4f));
            CreateMarker(markers.transform, "Investigation", Phase5Layout.AreaBInvestigation,
                Phase5Placeholder.InvestigationColor, "調査", new Vector3(0.8f, 0.6f, 0.8f));
            CreateMarker(markers.transform, "EncounterTrigger", Phase5Layout.AreaBEncounterTrigger,
                Phase5Placeholder.ArenaColor, "遭遇", new Vector3(2f, 0.1f, 2f));
            CreateMarker(markers.transform, "CombatReturn", Phase5Layout.AreaBCombatReturn,
                Phase5Placeholder.EntryColor, "戦闘復帰点", new Vector3(1f, 0.1f, 1f));

            // アリーナ境界の外枠だけを線で示す（封鎖 Collider は P5-07 で足す）。
            var arena = new GameObject("ArenaBounds");
            arena.transform.SetParent(markers.transform, false);
            arena.transform.position = Phase5Layout.AreaBArenaCenter;
            Material arenaMat = Phase5Placeholder.EnsureMaterial("M_P5_Arena", Phase5Placeholder.ArenaColor);
            CreateOutline(arena.transform, Phase5Layout.AreaBArenaCenter,
                Phase5Layout.AreaBArenaSize.x, Phase5Layout.AreaBArenaSize.y, arenaMat);
            Phase5Placeholder.CreateLabel("戦闘区域", arena.transform,
                Phase5Layout.AreaBArenaCenter + new Vector3(0f, 0.2f, Phase5Layout.AreaBArenaSize.y * 0.5f - 1f),
                Phase5Placeholder.ArenaColor, 0.3f);

            // 敵の出現点（EnemyIds の順に対応させる。敵数以上を用意する。§8.1）。
            var spawns = new GameObject("SpawnPoints");
            spawns.transform.SetParent(root, false);
            for (int i = 0; i < Phase5Layout.AreaBSpawnPoints.Length; i++)
            {
                var sp = new GameObject("Spawn_" + i);
                sp.transform.SetParent(spawns.transform, false);
                sp.transform.position = Phase5Layout.AreaBSpawnPoints[i];
                Phase5Placeholder.CreateLabel("出現 " + i, sp.transform,
                    Phase5Layout.AreaBSpawnPoints[i] + new Vector3(0f, 0.2f, 0f),
                    Phase5Placeholder.ArenaColor, 0.16f);
            }

            var entries = new GameObject("Entries");
            entries.transform.SetParent(root, false);
            AreaEntryPoint fromA = CreateEntryPoint(entries.transform, Phase5AreaIds.AreaBFromA,
                Phase5Layout.AreaBFromA, Phase5Layout.AreaBFromAAlternates, "A から");

            AreaRoot areaRoot = root.gameObject.AddComponent<AreaRoot>();
            areaRoot.EditorSet(definition, new List<AreaEntryPoint> { fromA });
        }

        /// <summary>
        /// 統合起動 Scene（§3.1）。AreaId は持たず、初期化後に A へ移動する。
        /// Session の生成と自動遷移の配線は P5-03b の仕事なので、ここでは目印だけ置く。
        /// </summary>
        private static void PopulateTrial(Transform root)
        {
            root.gameObject.name = "Phase5TrialRoot";
            Phase5Placeholder.CreateLabel("P5 探索試遊（起動）", root, new Vector3(0f, 0f, 0f), Color.white, 0.5f);
            Phase5Placeholder.CreateLabel("Play すると エリア A へ移動します（配線は P5-03b）",
                root, new Vector3(0f, 0f, -1.5f), Color.white, 0.2f);
        }

        private static void CreateFloor(Transform parent, Vector3 center, float width, float depth, Material mat)
        {
            Phase5Placeholder.CreateBox("Floor", parent,
                center + new Vector3(0f, -Phase5Layout.FloorThickness * 0.5f, 0f),
                new Vector3(width, Phase5Layout.FloorThickness, depth), mat);
        }

        private static void CreateOuterWalls(Transform parent, Vector3 center, float width, float depth, Material mat)
        {
            float hx = width * 0.5f;
            float hz = depth * 0.5f;
            float t = Phase5Layout.WallThickness;

            CreateWall(parent, "Wall_North", center + new Vector3(0f, 0f, hz),
                new Vector3(width + t, Phase5Layout.WallHeight, t), mat);
            CreateWall(parent, "Wall_South", center + new Vector3(0f, 0f, -hz),
                new Vector3(width + t, Phase5Layout.WallHeight, t), mat);
            CreateWall(parent, "Wall_East", center + new Vector3(hx, 0f, 0f),
                new Vector3(t, Phase5Layout.WallHeight, depth + t), mat);
            CreateWall(parent, "Wall_West", center + new Vector3(-hx, 0f, 0f),
                new Vector3(t, Phase5Layout.WallHeight, depth + t), mat);
        }

        private static void CreateWall(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            Phase5Placeholder.CreateBox(name, parent,
                new Vector3(center.x, size.y * 0.5f, center.z), size, mat);
        }

        /// <summary>矩形の外枠を薄い板 4 本で描く（当たり判定なし。見て分かるようにするだけ）。</summary>
        private static void CreateOutline(Transform parent, Vector3 center, float width, float depth, Material mat)
        {
            float hx = width * 0.5f;
            float hz = depth * 0.5f;
            const float t = 0.2f;

            Phase5Placeholder.CreateBox("Edge_North", parent, center + new Vector3(0f, 0.02f, hz),
                new Vector3(width, 0.04f, t), mat, solid: false);
            Phase5Placeholder.CreateBox("Edge_South", parent, center + new Vector3(0f, 0.02f, -hz),
                new Vector3(width, 0.04f, t), mat, solid: false);
            Phase5Placeholder.CreateBox("Edge_East", parent, center + new Vector3(hx, 0.02f, 0f),
                new Vector3(t, 0.04f, depth), mat, solid: false);
            Phase5Placeholder.CreateBox("Edge_West", parent, center + new Vector3(-hx, 0.02f, 0f),
                new Vector3(t, 0.04f, depth), mat, solid: false);
        }
    }
}
