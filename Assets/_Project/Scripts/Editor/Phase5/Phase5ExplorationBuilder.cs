using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.Events;
using Momotaro.Data.Exploration;
using Momotaro.Data.World;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Interaction;
using Momotaro.Infrastructure.Input;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Navigation;
using Momotaro.Infrastructure.World;
using Unity.AI.Navigation;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Diagnostics;
using Momotaro.Presentation.Combat;
using Momotaro.Presentation.Hud;
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
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";
        private const string CompanionPrefabPath = "Assets/_Project/Prefabs/Companions/PF_Companion_Inumaru.prefab";

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
            EnsureEncounterDefinition();
            outputs.Add(Phase5AreaIds.AreaADataPath);
            outputs.Add(Phase5AreaIds.AreaBDataPath);
            outputs.Add(Phase5AreaIds.CatalogDataPath);
            outputs.Add(Phase5AreaIds.EncounterBDataPath);

            AssetDatabase.SaveAssets();

            // 2. Scene を作る。どれか 1 つでも失敗したら、その時点で止める。
            Phase5Placeholder.EnsureFolder(Phase5AreaIds.SceneFolder);

            if (!BuildScene(Phase5AreaIds.AreaAScenePath, (root, rig) => PopulateAreaA(root, areaA, rig),
                    withCameraRig: true, out string errorA))
            {
                return new BuildResult(false, "エリア A の生成に失敗: " + errorA, outputs);
            }

            outputs.Add(Phase5AreaIds.AreaAScenePath);

            if (!BuildScene(Phase5AreaIds.AreaBScenePath, (root, rig) => PopulateAreaB(root, areaB, rig),
                    withCameraRig: true, out string errorB))
            {
                return new BuildResult(false, "エリア B の生成に失敗: " + errorB, outputs);
            }

            outputs.Add(Phase5AreaIds.AreaBScenePath);

            if (!BuildScene(Phase5AreaIds.TrialScenePath, (root, rig) => PopulateTrial(root),
                    withCameraRig: false, out string errorT))
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
                "Data 4 件（A／B／カタログ／Encounter）、Scene 3 件（A／B／統合起動）。Build Settings へ "
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

        /// <summary>
        /// B の通常 Encounter 定義（P5-07。仕様書 v1.1 §8.1）。
        ///
        /// <b>Data 側には EnemyId までしか置かない。</b> EnemyId → Prefab と SpawnPointId → Transform は
        /// Scene の EncounterBinding が持つ。同じ対応表を Data カタログへ二重登録しない（§8.1）。
        /// </summary>
        private static EncounterData EnsureEncounterDefinition()
        {
            var asset = AssetDatabase.LoadAssetAtPath<EncounterData>(Phase5AreaIds.EncounterBDataPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<EncounterData>();
                AssetDatabase.CreateAsset(asset, Phase5AreaIds.EncounterBDataPath);
            }

            SetIdentity(asset, Phase5AreaIds.EncounterBRoad, "エリア B 街道の遭遇（試作）");

            var so = new SerializedObject(asset);
            SerializedProperty ids = so.FindProperty("_enemyIds");
            ids.arraySize = 2;
            ids.GetArrayElementAtIndex(0).FindPropertyRelative("_value").stringValue = Phase5AreaIds.EnemyMelee.Value;
            ids.GetArrayElementAtIndex(1).FindPropertyRelative("_value").stringValue = Phase5AreaIds.EnemyRanged.Value;
            so.FindProperty("_spawnPointId").FindPropertyRelative("_value").stringValue =
                Phase5AreaIds.SpawnPointsBRoad.Value;
            so.FindProperty("_isBossEncounter").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

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

        private static bool BuildScene(
            string path, System.Action<Transform, AreaCameraRig> populate, bool withCameraRig, out string error)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var root = new GameObject("AreaRoot");
                AreaCameraRig rig = CreateCommonSceneObjects(withCameraRig);
                populate(root.transform, rig);
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

        /// <summary>
        /// どの Scene にも要る最小構成（カメラ・ライト）。<b>活動中は各 1 つ</b>（§11）。
        ///
        /// <b>Rig 親に追従・境界、Camera 子に揺れ</b>（§11）。同じ Transform を 2 人で書かないために、
        /// 追従は Rig の <c>position</c>、揺れは Camera 子の <c>localPosition</c> と、書込み先を分けてある。
        /// 分けないと、揺れの基準が追従で毎フレーム動き、戻り位置がずれる。
        ///
        /// 起動 Scene（エリアではない）は領域も追従対象も持たないので、Rig を置かない。
        /// 置くと「未配線」を毎回警告することになる。
        /// </summary>
        private static AreaCameraRig CreateCommonSceneObjects(bool withCameraRig)
        {
            var lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            AreaCameraRig rig = null;
            Transform cameraParent = null;
            if (withCameraRig)
            {
                var rigGo = new GameObject("CameraRig");
                rig = rigGo.AddComponent<AreaCameraRig>();
                cameraParent = rigGo.transform;
            }

            var cameraGo = new GameObject("Main Camera");
            if (cameraParent != null)
            {
                cameraGo.transform.SetParent(cameraParent, false);
            }

            Camera camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Phase5Layout.CameraOrthographicSize;
            camera.tag = "MainCamera";
            cameraGo.transform.localPosition = Phase5Layout.CameraLocalOffset;
            cameraGo.transform.localRotation = Quaternion.Euler(Phase5Layout.CameraPitchDegrees, 0f, 0f);
            cameraGo.AddComponent<AudioListener>();

            // 揺れは既存の ShakePresenter を使う（§11。独自 HitStop を足さない）。
            // 対象は自分＝Camera 子。Rig を揺らすと追従の書込みと競合する。
            CameraShakePresenter shake = cameraGo.AddComponent<CameraShakePresenter>();
            shake.Target = cameraGo.transform;

            if (rig != null)
            {
                var so = new SerializedObject(rig);
                so.FindProperty("_camera").objectReferenceValue = camera;
                so.FindProperty("_pitchDegrees").floatValue = Phase5Layout.CameraPitchDegrees;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return rig;
        }

        /// <summary>カメラ領域を 1 つ置く（§11。XZ の軸平行矩形。回転は持たない）。</summary>
        private static AreaCameraRegion CreateCameraRegion(
            Transform parent, StableId regionId, int priority, Vector3 center, Vector2 size)
        {
            var go = new GameObject("CameraRegion_" + regionId.Value);
            go.transform.SetParent(parent, false);
            go.transform.position = center;

            // 判定だけの目印。Renderer も Collider も持たないので NavMesh のベイクには乗らない。
            AreaCameraRegion region = go.AddComponent<AreaCameraRegion>();
            region.Configure(regionId, priority, size);
            return region;
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

        private static void PopulateAreaA(Transform root, AreaDefinition definition, AreaCameraRig rig)
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
            CreateMarker(markers.transform, "ExitToB", Phase5Layout.AreaAExitToB,
                Phase5Placeholder.EntryColor, "B へ", new Vector3(0.6f, 1.6f, Phase5Layout.CorridorWidth));

            // 入口。
            var entries = new GameObject("Entries");
            entries.transform.SetParent(root, false);
            AreaEntryPoint start = CreateEntryPoint(entries.transform, Phase5AreaIds.AreaAStart,
                Phase5Layout.AreaAStart, Phase5Layout.AreaAStartAlternates, "開始／再開");
            AreaEntryPoint fromB = CreateEntryPoint(entries.transform, Phase5AreaIds.AreaAFromB,
                Phase5Layout.AreaAFromB, Phase5Layout.AreaAFromBAlternates, "B から");

            AreaExitGate toB = CreateExitGate(root, "ExitGate_ToB", Phase5Layout.AreaAExitToB,
                Phase5AreaIds.AreaB, Phase5AreaIds.AreaBFromA, Vector3.right);

            // 仕掛け（§7）。目印ではなく実物を置く。
            var fixtures = new Fixtures();
            var fixtureRoot = new GameObject("Fixtures");
            fixtureRoot.transform.SetParent(root, false);

            // 門は東の通路を<b>完全に塞ぐ</b>。塞がっていない門は門ではない：
            // 迂回できてしまうと「開通しないと進めない」という §7.3 の意味が消える。
            // 通路は仕切り（x=6）と外壁（x=12）の間の 6m なので、少し余らせて 6.2m で塞ぐ。
            fixtures.Door = CreateFlagDoor(fixtureRoot.transform, Phase5AreaIds.FlagAGate,
                Phase5Layout.AreaAGate, new Vector3(6.2f, Phase5Layout.WallHeight, 0.6f));
            fixtures.Lever = CreateFlagLever(fixtureRoot.transform, Phase5AreaIds.FlagAGate,
                definition.Id, fixtures.Door, Phase5Layout.AreaALever);
            fixtures.Points.Add(CreateInvestigationPoint(fixtureRoot.transform, "Investigation",
                Phase5AreaIds.PointAOpen, Phase5AreaIds.DiscoveryAOpen, Phase5Layout.AreaAInvestigation));
            fixtures.Points.Add(CreateInvestigationPoint(fixtureRoot.transform, "InvestigationBlocked",
                Phase5AreaIds.PointABlocked, Phase5AreaIds.DiscoveryABlocked,
                Phase5Layout.AreaAInvestigationBlocked));

            // カメラ領域（§11）。西の大部屋と東の通路を分ける。
            // 東の通路は幅 6m で、見える範囲（横）より狭い＝横は中央固定になる。
            // 「入りきる軸は追従、入りきらない軸は中央固定」の両方を実 Scene で通すための配置。
            var cameraRegions = new GameObject("CameraRegions");
            cameraRegions.transform.SetParent(root, false);
            fixtures.DefaultCameraRegion = CreateCameraRegion(cameraRegions.transform,
                Phase5AreaIds.RegionADefault, 0, Vector3.zero, Phase5Layout.AreaADefaultRegionSize);
            fixtures.CameraRegions.Add(CreateCameraRegion(cameraRegions.transform,
                Phase5AreaIds.RegionAWest, 1,
                Phase5Layout.AreaAWestRegionCenter, Phase5Layout.AreaAWestRegionSize));
            fixtures.CameraRegions.Add(CreateCameraRegion(cameraRegions.transform,
                Phase5AreaIds.RegionAEast, 1,
                Phase5Layout.AreaAEastRegionCenter, Phase5Layout.AreaAEastRegionSize));

            AreaRoot areaRoot = root.gameObject.AddComponent<AreaRoot>();
            areaRoot.EditorSet(definition, new List<AreaEntryPoint> { start, fromB },
                new List<AreaExitGate> { toB }, new List<AreaFlagDoor> { fixtures.Door },
                null, new List<AreaFlagLever> { fixtures.Lever });

            CreateAreaSystems(root, areaRoot, definition, fixtures, rig);
            BakeNavMesh(root, "NavMesh_P5_A");
        }

        private static void PopulateAreaB(Transform root, AreaDefinition definition, AreaCameraRig rig)
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
            var spawnPoints = new List<Transform>();
            for (int i = 0; i < Phase5Layout.AreaBSpawnPoints.Length; i++)
            {
                var sp = new GameObject("Spawn_" + i);
                sp.transform.SetParent(spawns.transform, false);
                sp.transform.position = Phase5Layout.AreaBSpawnPoints[i];
                spawnPoints.Add(sp.transform);
                Phase5Placeholder.CreateLabel("出現 " + i, sp.transform,
                    Phase5Layout.AreaBSpawnPoints[i] + new Vector3(0f, 0.2f, 0f),
                    Phase5Placeholder.ArenaColor, 0.16f);
            }

            var entries = new GameObject("Entries");
            entries.transform.SetParent(root, false);
            AreaEntryPoint fromA = CreateEntryPoint(entries.transform, Phase5AreaIds.AreaBFromA,
                Phase5Layout.AreaBFromA, Phase5Layout.AreaBFromAAlternates, "A から");

            AreaExitGate toA = CreateExitGate(root, "ExitGate_ToA", Phase5Layout.AreaBDoorToA,
                Phase5AreaIds.AreaA, Phase5AreaIds.AreaAFromB, Vector3.left);

            // A へ戻る扉（§6.1 の 2 行目。押下 1 回で要求する）。
            // 同じ場所の開放出入口と併存させる：方向入力でも Interact でも戻れる。
            var fixtures = new Fixtures();
            var fixtureRoot = new GameObject("Fixtures");
            fixtureRoot.transform.SetParent(root, false);
            fixtures.TransitionDoor = CreateTransitionDoor(fixtureRoot.transform, "DoorToA",
                Phase5AreaIds.DoorBToA, definition.Id,
                Phase5AreaIds.AreaA, Phase5AreaIds.AreaAFromB, Phase5Layout.AreaBDoorToA);

            // 調査地点（§7.2）。アリーナの外に置き、調査中の戦闘開始を作れるようにする（§8.3）。
            fixtures.Points.Add(CreateInvestigationPoint(fixtureRoot.transform, "Investigation",
                Phase5AreaIds.PointBOpen, Phase5AreaIds.DiscoveryBOpen, Phase5Layout.AreaBInvestigation));

            // ---- 戦闘区画（§8）----
            //
            // 出現点は EnemyIds の順に対応させる（§8.1）。Transform は Scene 側が持ち、Data へは入れない。
            fixtures.SpawnPoints.AddRange(spawnPoints);
            CreateArena(fixtureRoot.transform, fixtures);

            // カメラ領域（§11）。B は 1 部屋なので既定領域だけ。
            var cameraRegions = new GameObject("CameraRegions");
            cameraRegions.transform.SetParent(root, false);
            fixtures.DefaultCameraRegion = CreateCameraRegion(cameraRegions.transform,
                Phase5AreaIds.RegionBDefault, 0, Vector3.zero, Phase5Layout.AreaBDefaultRegionSize);

            AreaRoot areaRoot = root.gameObject.AddComponent<AreaRoot>();
            areaRoot.EditorSet(definition, new List<AreaEntryPoint> { fromA },
                new List<AreaExitGate> { toA }, null,
                new List<AreaTransitionDoor> { fixtures.TransitionDoor });

            CreateAreaSystems(root, areaRoot, definition, fixtures, rig);
            BakeNavMesh(root, "NavMesh_P5_B");
        }

        /// <summary>
        /// NavMesh をベイクして Asset として保存する（P5-05。§10.2 末尾）。
        ///
        /// <b>プレイ開始時に毎回焼き直さない。</b> 焼き直しは重いうえ、
        /// 焼けたかどうかが実行時の運次第になる。Builder で焼いて保存し、Scene が参照する。
        ///
        /// 門は <c>NavMeshModifier</c> でベイクから外してある。閉じている間の通行止めは
        /// くり抜き（<c>NavMeshObstacle</c>）で行い、開通時にくり抜きを外す。
        /// こうしておくと「開通したのに NavMesh に穴が残ったまま」にならない。
        /// </summary>
        private static void BakeNavMesh(Transform root, string assetName)
        {
            // <b>面は根に付ける。</b> Children で集める設定なので、子オブジェクトに付けると
            // 「自分の子」＝空を焼くことになり、NavMesh がどこにも生成されない（実際に踏んだ）。
            NavMeshSurface surface = root.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;

            // <b>Collider から焼く。</b> 見た目（Renderer）から焼くと、ラベルや半透明の板まで
            // 地形として拾ってしまう。通行できるかどうかを決めているのは Collider の方で、
            // 「見た目と通行判定がずれない」という §3.3 の考え方とも揃う。Trigger は拾われない。
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                Debug.LogWarning("NavMesh を焼けませんでした: " + assetName);
                return;
            }

            string folder = Phase5AreaIds.DataFolder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            string path = folder + "/" + assetName + ".asset";
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 統合起動 Scene（§3.1）。AreaId は持たず、初期化後に A へ移動する。
        /// Session の生成と自動遷移の配線は P5-03b の仕事なので、ここでは目印だけ置く。
        /// </summary>
        private static void PopulateTrial(Transform root)
        {
            root.gameObject.name = "Phase5TrialRoot";
            Phase5Placeholder.CreateLabel("P5 探索試遊（起動）", root, new Vector3(0f, 0f, 0f), Color.white, 0.5f);
            Phase5Placeholder.CreateLabel("Play すると エリア A の開始点へ移動します",
                root, new Vector3(0f, 0f, -1.5f), Color.white, 0.2f);

            // 実際に A へ進む（§5.2 の「統合起動 Scene から Play」）。
            Phase5TrialLauncher launcher = root.gameObject.AddComponent<Phase5TrialLauncher>();
            var so = new SerializedObject(launcher);
            so.FindProperty("_catalog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase5AreaIds.CatalogDataPath);
            so.ApplyModifiedPropertiesWithoutUndo();
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

        /// <summary>
        /// エリアの常駐配線（§5.1 の「AreaRoot / 初期化担当 / GameplayRoot」のうち、P5-03b で必要な分）。
        ///
        /// 置くのは初期化状態・受付条件・注入先・初期化担当の 4 つだけ。
        /// 主人公・犬丸・HUD・Encounter は後続工程で載せる（先回りして空の配線を置かない）。
        /// </summary>
        /// <summary>この Area に置いた仕掛け（§7）。窓口への配線でまとめて使う。</summary>
        private sealed class Fixtures
        {
            public AreaFlagDoor Door;
            public AreaFlagLever Lever;
            public AreaTransitionDoor TransitionDoor;
            public AreaCameraRegion DefaultCameraRegion;
            public AreaArenaBoundary Arena;
            public AreaEncounterTrigger EncounterTrigger;
            public readonly List<Collider> ArenaBlockers = new List<Collider>();
            public readonly List<Transform> SpawnPoints = new List<Transform>();
            public readonly List<CompanionInvestigationPoint> Points = new List<CompanionInvestigationPoint>();
            public readonly List<AreaCameraRegion> CameraRegions = new List<AreaCameraRegion>();
        }

        /// <summary>Flag で恒久開通する門（§7.3）。通行を止める Collider と、閉じている見た目を持つ。</summary>
        private static AreaFlagDoor CreateFlagDoor(
            Transform parent, StableId flagId, Vector3 position, Vector3 size)
        {
            var go = new GameObject("Door_" + flagId.Value);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            // 通行止め。開通すると無効化される。
            BoxCollider blocker = go.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0f, Phase5Layout.WallHeight * 0.5f, 0f);
            blocker.size = size;

            // 閉じているときだけ見せる板。
            Material mat = Phase5Placeholder.EnsureMaterial("M_P5_Gate", Phase5Placeholder.GateColor);
            var visual = new GameObject("ClosedVisual");
            visual.transform.SetParent(go.transform, false);
            Phase5Placeholder.CreateBox("Body", visual.transform,
                position + new Vector3(0f, Phase5Layout.WallHeight * 0.5f, 0f),
                size, mat, solid: false);
            Phase5Placeholder.CreateLabel("門", visual.transform,
                position + new Vector3(0f, Phase5Layout.WallHeight + 0.2f, 0f),
                Phase5Placeholder.GateColor, 0.2f);

            // ベイクからは外す（§10.2）。焼き込んでしまうと、開通しても NavMesh に穴が残ったままになる。
            // 閉じている間の通行止めは、くり抜き（NavMeshObstacle）で動的に行う。
            NavMeshModifier modifier = go.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = true;
            modifier.applyToChildren = true;

            UnityEngine.AI.NavMeshObstacle obstacle = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, Phase5Layout.WallHeight * 0.5f, 0f);
            obstacle.size = size;
            obstacle.carving = true; // くり抜かないと経路は素通りする（避けるだけになる）。

            AreaFlagDoor door = go.AddComponent<AreaFlagDoor>();
            door.Bind(flagId, blocker, visual, obstacle);
            return door;
        }

        /// <summary>門を開けるレバー（§7.3）。Interact の候補として登録される。</summary>
        private static AreaFlagLever CreateFlagLever(
            Transform parent, StableId flagId, StableId areaId, AreaFlagDoor door, Vector3 position)
        {
            var go = new GameObject("Lever_" + flagId.Value);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            Material mat = Phase5Placeholder.EnsureMaterial("M_P5_Lever", Phase5Placeholder.LeverColor);
            Phase5Placeholder.CreateBox("Body", go.transform, position + new Vector3(0f, 0.6f, 0f),
                new Vector3(0.5f, 1.2f, 0.5f), mat, solid: false);
            Phase5Placeholder.CreateLabel("レバー", go.transform, position + new Vector3(0f, 1.4f, 0f),
                Phase5Placeholder.LeverColor, 0.16f);

            AreaFlagLever lever = go.AddComponent<AreaFlagLever>();
            lever.Bind(flagId, areaId, door, go.transform);
            return lever;
        }

        /// <summary>犬丸の調査地点（§7.2）。設定は P4 の試遊と同じ Asset を使う。</summary>
        private static CompanionInvestigationPoint CreateInvestigationPoint(
            Transform parent, string name, StableId pointId, StableId discoveryId, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            Material mat = Phase5Placeholder.EnsureMaterial("M_P5_Investigation", Phase5Placeholder.InvestigationColor);
            Phase5Placeholder.CreateBox("Body", go.transform, position + new Vector3(0f, 0.3f, 0f),
                new Vector3(0.8f, 0.6f, 0.8f), mat, solid: false);
            Phase5Placeholder.CreateLabel("調査", go.transform, position + new Vector3(0f, 0.9f, 0f),
                Phase5Placeholder.InvestigationColor, 0.16f);

            var settings = AssetDatabase.LoadAssetAtPath<InvestigationSettingsData>(
                Phase5AreaIds.InvestigationSettingsPath);
            CompanionInvestigationPoint point = go.AddComponent<CompanionInvestigationPoint>();
            point.Configure(pointId, CompanionIds.Inumaru, discoveryId, settings);
            return point;
        }

        /// <summary>Interact 1 回で遷移を要求する扉（§6.1 の 2 行目）。</summary>
        private static AreaTransitionDoor CreateTransitionDoor(
            Transform parent, string name, StableId doorId, StableId areaId,
            StableId destinationArea, StableId destinationEntry, Vector3 position)
        {
            var go = new GameObject("Door_" + name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            Material mat = Phase5Placeholder.EnsureMaterial("M_P5_DoorToA", Phase5Placeholder.EntryColor);
            Phase5Placeholder.CreateBox("Body", go.transform, position + new Vector3(0f, 0.9f, 0f),
                new Vector3(0.6f, 1.8f, 2.4f), mat, solid: false);
            Phase5Placeholder.CreateLabel("A へ（扉）", go.transform, position + new Vector3(0f, 2.0f, 0f),
                Phase5Placeholder.EntryColor, 0.16f);

            AreaTransitionDoor door = go.AddComponent<AreaTransitionDoor>();
            door.Configure(doorId, areaId, destinationArea, destinationEntry, go.transform);
            return door;
        }

        private static void CreateAreaSystems(
            Transform root, AreaRoot areaRoot, AreaDefinition definition, Fixtures fixtures, AreaCameraRig rig)
        {
            var systems = new GameObject("AreaSystems");
            systems.transform.SetParent(root, false);

            AreaContext context = systems.AddComponent<AreaContext>();
            AreaTransitionConditionsSource conditions = systems.AddComponent<AreaTransitionConditionsSource>();
            PlayerProgressHolder progress = systems.AddComponent<PlayerProgressHolder>();
            InvestigationRecordHolder record = systems.AddComponent<InvestigationRecordHolder>();

            // 主人公を入口へ置く。§6.1 の受付条件は主人公の状態と生存を見るので、
            // ここが無いと「分からない＝安全側」で遷移が一切通らない（R2-08 と同じ考え方）。
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerStateController player = null;
            PlayerVitalsHolder vitals = null;
            GameObject playerRoot = null;
            if (playerPrefab != null)
            {
                var playerGo = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                playerRoot = playerGo;
                playerGo.name = "Player";
                playerGo.transform.SetParent(root, false);
                playerGo.transform.position = ResolveDefaultArrival(areaRoot, definition);
                player = playerGo.GetComponentInChildren<PlayerStateController>(true);
                vitals = playerGo.GetComponentInChildren<PlayerVitalsHolder>(true);
            }

            conditions.Bind(context, player, vitals, null);

            // 犬丸。主人公の隣へ置く（入口に重ねない。§3.2）。
            var companionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompanionPrefabPath);
            CompanionActor companionActor = null;
            CompanionHitReceiver companionVitals = null;
            CompanionCombatController companionCombat = null;
            CompanionDefenseController companionDefense = null;
            CompanionGuardianController companionGuardian = null;
            CompanionStateArbiter companionStates = null;
            GameObject companionRoot = null;
            if (companionPrefab != null)
            {
                var companionGo = (GameObject)PrefabUtility.InstantiatePrefab(companionPrefab);
                companionRoot = companionGo;
                companionGo.name = "Inumaru";
                companionGo.transform.SetParent(root, false);
                companionGo.transform.position =
                    ResolveDefaultArrival(areaRoot, definition) + new Vector3(-1.2f, 0f, 0f);
                companionActor = companionGo.GetComponentInChildren<CompanionActor>(true);
                companionVitals = companionGo.GetComponentInChildren<CompanionHitReceiver>(true);
                companionCombat = companionGo.GetComponentInChildren<CompanionCombatController>(true);
                companionDefense = companionGo.GetComponentInChildren<CompanionDefenseController>(true);
                companionGuardian = companionGo.GetComponentInChildren<CompanionGuardianController>(true);
                companionStates = companionGo.GetComponentInChildren<CompanionStateArbiter>(true);
            }

            // 仲間の活動許可（§12.1）。未配線だと CompanionActivityProvider は停止側へ倒れ、
            // 犬丸が一切動かない。P5-07 で Encounter を載せるまでは「このエリアに戦闘は無い」と明示する。
            var activityGo = new GameObject("CompanionActivity");
            activityGo.transform.SetParent(systems.transform, false);
            CompanionActivityContext activity = activityGo.AddComponent<CompanionActivityContext>();
            bool hasEncounter = fixtures != null && fixtures.Arena != null;
            if (!hasEncounter)
            {
                activity.MarkAreaWithoutEncounter();
            }

            // 追従の相手（主人公）を配線する。未割当だと犬丸は付いてこない。
            if (companionRoot != null && playerRoot != null)
            {
                var follow = companionRoot.GetComponentInChildren<CompanionFollowController>(true);
                if (follow != null)
                {
                    follow.Bind(playerRoot.transform, companionActor,
                        companionRoot.GetComponentInChildren<CompanionMotor>(true));
                }
            }

            // 主人公・犬丸はベイクから外す（動く物は地形ではない）。
            // 外さないと、生成時に立っていた場所に穴の空いた NavMesh が焼き込まれる。
            IgnoreFromNavMeshBuild(playerRoot);
            IgnoreFromNavMeshBuild(companionRoot);

            // Actor 値の採取・復元の窓口（§4.4〜§4.6）。明示参照で持つ（Find* を使わない）。
            AreaActorTransferPort port = systems.AddComponent<AreaActorTransferPort>();
            port.Bind(vitals, vitals != null ? vitals.GetComponentInChildren<PlayerHitReaction>(true) : null,
                companionActor, companionVitals, companionCombat, companionDefense,
                companionGuardian, companionStates, player);

            // 配置対象は Prefab の根。PlayerStateController は子に居ることがあるので、
            // 根を明示的に渡す（子の transform を動かしても Rigidbody に引き戻される）。
            port.BindRoots(
                playerRoot != null ? playerRoot.transform : null,
                companionRoot != null ? companionRoot.transform : null);

            // 開放出入口の駆動（§6.1）。
            AreaExitGateDriver gateDriver = systems.AddComponent<AreaExitGateDriver>();
            gateDriver.Bind(areaRoot, player);

            // 徳を映す HUD（§11 の必須 UI のうち、P5-03b で要る分）。
            CreateHud(systems.transform, vitals, player, progress);

            // ---- Interact の単一選択窓口（§7.1）----
            //
            // 押下を消費するのはこの 1 本だけ。P4 の InvestigationInteractInput は<b>置かない</b>
            // （両方生きていると、同じ押下が 2 回消費されるか、窓口が選んだ対象と別の地点へ依頼が飛ぶ）。
            var interactionGo = new GameObject("Interaction");
            interactionGo.transform.SetParent(systems.transform, false);
            AreaInteractionController interaction = interactionGo.AddComponent<AreaInteractionController>();
            interaction.Bind(context, playerRoot != null ? playerRoot.transform : null);
            AreaInteractInput interactInput = interactionGo.AddComponent<AreaInteractInput>();
            interactInput.Bind(interaction);

            // ---- 調査（§7.2）。P5 は指定地点モードで構成する（§7.1）----
            InvestigationCoordinator investigationCoordinator = null;
            if (fixtures != null && fixtures.Points.Count > 0)
            {
                var rosterGo = new GameObject("CompanionRoster");
                rosterGo.transform.SetParent(systems.transform, false);
                CompanionRosterContext roster = rosterGo.AddComponent<CompanionRosterContext>();
                roster.SetRecruited(CompanionIds.Inumaru);

                CompanionInvestigationController driver = companionRoot != null
                    ? companionRoot.GetComponentInChildren<CompanionInvestigationController>(true)
                    : null;

                var coordinatorGo = new GameObject("InvestigationCoordinator");
                coordinatorGo.transform.SetParent(systems.transform, false);
                InvestigationCoordinator coordinator = coordinatorGo.AddComponent<InvestigationCoordinator>();
                investigationCoordinator = coordinator;
                coordinator.ExplicitTargetOnly = true;
                coordinator.Bind(player, roster, record,
                    driver != null ? new[] { driver } : new CompanionInvestigationController[0]);

                // 地点ごとに Adapter を付ける。窓口が選んだ地点がそのまま依頼される。
                foreach (CompanionInvestigationPoint point in fixtures.Points)
                {
                    InvestigationInteractable adapter = point.gameObject.AddComponent<InvestigationInteractable>();
                    adapter.Bind(point, coordinator, definition.Id);
                }
            }

            // ---- Encounter（§8）----
            //
            // 既存の戦闘セッションをそのまま使い、P5 の調停（AreaEncounterRunner）が
            // 敵登録・勝敗遷移を駆動する。4 Wave の WaveRunner は載せない（§8.1 末尾）。
            AreaEncounterRunner encounterRunner = null;
            if (hasEncounter)
            {
                var encounterGo = new GameObject("Encounter");
                encounterGo.transform.SetParent(systems.transform, false);

                CombatSessionController combatSession = encounterGo.AddComponent<CombatSessionController>();

                // 撃破報酬は既存の受け手を使う（§12.1）。新 Scene で購読開始前に Bind する。
                CombatRewardCollector rewards = encounterGo.AddComponent<CombatRewardCollector>();
                rewards.Bind(combatSession, progress);

                var spawnerGo = new GameObject("Spawner");
                spawnerGo.transform.SetParent(encounterGo.transform, false);
                AreaEncounterSpawner spawner = spawnerGo.AddComponent<AreaEncounterSpawner>();
                spawner.Bind(combatSession, BuildEnemyPrefabEntries(), fixtures.SpawnPoints);

                AreaEncounterConditionsSource encounterConditions =
                    encounterGo.AddComponent<AreaEncounterConditionsSource>();
                encounterConditions.Bind(context, vitals);

                EncounterInterruptRelay interrupts = encounterGo.AddComponent<EncounterInterruptRelay>();
                interrupts.Bind(investigationCoordinator,
                    companionRoot != null
                        ? companionRoot.GetComponentInChildren<CompanionFollowController>(true)
                        : null);

                var encounterData = AssetDatabase.LoadAssetAtPath<EncounterData>(Phase5AreaIds.EncounterBDataPath);
                encounterRunner = encounterGo.AddComponent<AreaEncounterRunner>();
                encounterRunner.Bind(encounterData, combatSession, encounterConditions, spawner,
                    fixtures.Arena, interrupts);
                encounterRunner.BindPlayerVitals(vitals);

                fixtures.Arena.Bind(fixtures.ArenaBlockers, playerRoot != null ? playerRoot.transform : null,
                    companionRoot != null
                        ? companionRoot.GetComponentInChildren<CompanionFollowController>(true)
                        : null,
                    Phase5Layout.AreaBArenaSize);

                fixtures.EncounterTrigger.Bind(encounterRunner,
                    playerRoot != null ? playerRoot.GetComponent<PlayerRoot>() : null);

                // ---- 命中 Feedback（§8.2 手順 7「報酬と Feedback の購読を接続する」）----
                //
                // 既存の配信役・演出をそのまま組む（新しい Feedback 経路を作らない）。
                // 揺れは Camera 子の ShakePresenter を使い回す（§11 の書込み先の分離を崩さない）。
                var feedbackGo = new GameObject("CombatFeedback");
                feedbackGo.transform.SetParent(systems.transform, false);

                // 揺れは Camera 子に付いている既存の ShakePresenter（§11）。新しく足さない。
                CameraShakePresenter cameraShake = rig != null
                    ? rig.GetComponentInChildren<CameraShakePresenter>(true)
                    : null;

                CombatFeedbackDispatcher dispatcher = feedbackGo.AddComponent<CombatFeedbackDispatcher>();
                HitStopController hitStop = feedbackGo.AddComponent<HitStopController>();
                HitFlashPresenter flash = feedbackGo.AddComponent<HitFlashPresenter>();
                CombatFeedbackPresenter feedback = feedbackGo.AddComponent<CombatFeedbackPresenter>();
                feedback.HitStop = hitStop;
                feedback.Flash = flash;
                feedback.CameraShake = cameraShake;

                // 生成直後の最初の命中を取りこぼさない（周期の再探索を待たせない）。
                EncounterFeedbackBinder feedbackBinder = feedbackGo.AddComponent<EncounterFeedbackBinder>();
                feedbackBinder.Bind(spawner, dispatcher);

                // ---- 結果の短文（§8.4 手順 8）----
                var resultGo = new GameObject("EncounterResult");
                resultGo.transform.SetParent(systems.transform, false);
                AreaEncounterResultView resultView = resultGo.AddComponent<AreaEncounterResultView>();
                resultView.Bind(encounterRunner);

                // Interact も Starting から閉じる（§8.3 の競合表）。
                interaction.BindEncounter(encounterRunner);

                // 仲間の活動は<b>区画 Encounter</b>が正本（§8.4 末尾）。
                activity.BindAreaEncounter(encounterRunner);

                // 遷移の受付も戦闘中は閉じる（§6.1／§8.3 の競合表）。
                conditions.Bind(context, player, vitals, encounterRunner);
            }

            // ---- 経路の配線（§10.1／§10.2）----
            //
            // Gameplay は NavMesh を知らない。Adapter の注入と、門の開通を経路へ伝える purpose を
            // Infrastructure のこの部品が持つ。未配線だと長距離の迂回が行われない（Validator の対象）。
            if (companionRoot != null)
            {
                var follow = companionRoot.GetComponentInChildren<CompanionFollowController>(true);
                if (follow != null)
                {
                    var navGo = new GameObject("AreaNavigation");
                    navGo.transform.SetParent(systems.transform, false);
                    AreaNavigationBinder binder = navGo.AddComponent<AreaNavigationBinder>();
                    binder.Bind(areaRoot, follow);
                }
            }

            // ---- カメラ（§11）----
            //
            // 追従対象と領域を Rig へ渡す。初期化担当（Infrastructure）は Presentation を知らないので、
            // 到着時の即時配置は AreaContext の準備完了通知を Rig が購読して行う（§5.1 手順 7）。
            if (rig != null && fixtures != null && fixtures.DefaultCameraRegion != null)
            {
                rig.Bind(playerRoot != null ? playerRoot.transform : null, null,
                    fixtures.DefaultCameraRegion, fixtures.CameraRegions, context);

                // 出荷時点でも入口を映した位置にしておく（Scene を開いた瞬間に原点を向かない）。
                rig.SnapToTarget();
            }

            var catalog = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase5AreaIds.CatalogDataPath);

            // ---- 本編型死亡再開（§9.1）----
            //
            // <b>両エリアに置く。</b> 死は遭遇戦の中だけで起きるものではないので（飛び道具の残り、
            // 将来の地形ダメージ）、Encounter を置かない A にも同じ経路を用意する。
            var respawnGo = new GameObject("CampaignRespawn");
            respawnGo.transform.SetParent(systems.transform, false);
            CampaignRespawnRunner respawn = respawnGo.AddComponent<CampaignRespawnRunner>();
            respawn.Bind(vitals, context, investigationCoordinator, catalog);
            if (vitals != null)
            {
                respawn.BindPlayerDefeat(vitals.Defeats);
            }

            // 再開操作（UI/Submit）。押下の読み取りだけを行い、一度限りの判断は Session が持つ。
            RespawnSubmitInput respawnInput = respawnGo.AddComponent<RespawnSubmitInput>();
            respawnInput.Bind(respawn);

            // 「再開する」の仮表示（§9.1 の 1 行目。既存試遊の Retry とは別物）。
            CampaignRespawnView respawnView = respawnGo.AddComponent<CampaignRespawnView>();
            respawnView.Bind(respawn);

            AreaInitializer initializer = systems.AddComponent<AreaInitializer>();

            // private な SerializeField は SerializedObject で配線する（Builder の既存の作法）。
            var so = new SerializedObject(initializer);
            so.FindProperty("_encounter").objectReferenceValue = encounterRunner;
            so.FindProperty("_transferPort").objectReferenceValue = port;
            so.FindProperty("_areaRoot").objectReferenceValue = areaRoot;
            so.FindProperty("_context").objectReferenceValue = context;
            so.FindProperty("_conditions").objectReferenceValue = conditions;
            so.FindProperty("_progress").objectReferenceValue = progress;
            so.FindProperty("_record").objectReferenceValue = record;
            so.FindProperty("_catalog").objectReferenceValue = catalog;
            so.FindProperty("_respawn").objectReferenceValue = respawn;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// アリーナ境界と戦闘開始 Trigger を置く（P5-07。仕様書 v1.1 §8.2）。
        ///
        /// 封鎖 Collider は<b>既定で無効</b>。戦闘中だけ有効化する（§8.2 手順 5、§8.4 手順 5）。
        /// 有効なまま出荷すると、探索中に通れない壁が生えたエリアになる。
        ///
        /// Trigger は境界より<b>十分内側</b>へ置く（§8.2 末尾）。境界の上に置くと、
        /// 封鎖した瞬間に主人公が壁へ食い込んだ状態になり、物理が外へ弾き出す。
        /// </summary>
        private static void CreateArena(Transform parent, Fixtures fixtures)
        {
            Vector3 center = Phase5Layout.AreaBArenaCenter;
            Vector2 size = Phase5Layout.AreaBArenaSize;

            var boundaryGo = new GameObject("ArenaBoundary");
            boundaryGo.transform.SetParent(parent, false);
            boundaryGo.transform.position = center;

            float hx = size.x * 0.5f;
            float hz = size.y * 0.5f;
            float t = Phase5Layout.WallThickness;
            float h = Phase5Layout.WallHeight;

            AddBlocker(boundaryGo.transform, fixtures, "Blocker_North",
                center + new Vector3(0f, h * 0.5f, hz + t * 0.5f), new Vector3(size.x + t * 2f, h, t));
            AddBlocker(boundaryGo.transform, fixtures, "Blocker_South",
                center + new Vector3(0f, h * 0.5f, -hz - t * 0.5f), new Vector3(size.x + t * 2f, h, t));
            AddBlocker(boundaryGo.transform, fixtures, "Blocker_East",
                center + new Vector3(hx + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, size.y + t * 2f));
            AddBlocker(boundaryGo.transform, fixtures, "Blocker_West",
                center + new Vector3(-hx - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, size.y + t * 2f));

            fixtures.Arena = boundaryGo.AddComponent<AreaArenaBoundary>();

            // 境界は探索中の地形ではない。NavMesh のベイクから外す
            // （焼き込むと、閉じていない間も通れない床として残る）。
            IgnoreFromNavMeshBuild(boundaryGo);

            var triggerGo = new GameObject("EncounterTrigger");
            triggerGo.transform.SetParent(parent, false);
            triggerGo.transform.position = Phase5Layout.AreaBEncounterTrigger;
            BoxCollider trigger = triggerGo.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(
                Phase5Layout.AreaBEncounterTriggerSize.x, Phase5Layout.WallHeight,
                Phase5Layout.AreaBEncounterTriggerSize.y);
            trigger.center = new Vector3(0f, Phase5Layout.WallHeight * 0.5f, 0f);
            fixtures.EncounterTrigger = triggerGo.AddComponent<AreaEncounterTrigger>();
            Phase5Placeholder.CreateLabel("遭遇", triggerGo.transform,
                Phase5Layout.AreaBEncounterTrigger + new Vector3(0f, 0.2f, 0f),
                Phase5Placeholder.ArenaColor, 0.22f);
            IgnoreFromNavMeshBuild(triggerGo);
        }

        private static void AddBlocker(
            Transform parent, Fixtures fixtures, string name, Vector3 center, Vector3 size)
        {
            GameObject go = Phase5Placeholder.CreateBlocker(name, parent, center, size);
            var box = go.GetComponent<BoxCollider>();
            box.enabled = false; // 戦闘中だけ有効化する。
            fixtures.ArenaBlockers.Add(box);
        }

        /// <summary>
        /// EnemyId → Prefab の明示表を作る（§8.1 末尾）。
        /// Prefab 名や表示名では解決しない。ID 一致は Validator が Prefab 側の
        /// <c>EnemyArchetypeData.Id</c> と突き合わせる。
        /// </summary>
        private static List<EnemyPrefabTable.Entry> BuildEnemyPrefabEntries()
        {
            return new List<EnemyPrefabTable.Entry>
            {
                new EnemyPrefabTable.Entry
                {
                    EnemyId = Phase5AreaIds.EnemyMelee,
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Phase5AreaIds.EnemyMeleePrefabPath),
                },
                new EnemyPrefabTable.Entry
                {
                    EnemyId = Phase5AreaIds.EnemyRanged,
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Phase5AreaIds.EnemyRangedPrefabPath),
                },
            };
        }

        /// <summary>この GameObject 以下を NavMesh のベイク対象から外す。</summary>
        private static void IgnoreFromNavMeshBuild(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            NavMeshModifier modifier = target.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = true;
            modifier.applyToChildren = true;
        }

        /// <summary>既定入口の到着位置（主人公の初期配置に使う）。見つからなければ原点。</summary>
        private static Vector3 ResolveDefaultArrival(AreaRoot areaRoot, AreaDefinition definition)
        {
            return areaRoot.TryGetEntryPoint(definition.DefaultEntryId, out AreaEntryPoint point)
                ? point.ArrivalPosition
                : Vector3.zero;
        }

        /// <summary>
        /// 徳・HP を映す仮 HUD（§11）。完成 UI 素材は要求しない。
        /// <c>CombatPlayHud</c> は戦闘試遊用だが、徳と HP の表示契約は同じなので流用する。
        /// 戦闘固有の参照（Session・Wave・Outcome）は P5 では未配線のままでよい。
        /// </summary>
        private static void CreateHud(
            Transform parent, PlayerVitalsHolder vitals, PlayerStateController playerState,
            PlayerProgressHolder progress)
        {
            var hudGo = new GameObject("AreaHud");
            hudGo.transform.SetParent(parent, false);

            CombatPlayHud hud = hudGo.AddComponent<CombatPlayHud>();
            hud.Bind(vitals, playerState, null);
            hud.SetProgressSource(progress);
        }

        /// <summary>
        /// 開放出入口を作る（§6.1）。Trigger の Collider を持ち、範囲内で出口方向へ
        /// 0.15 秒連続入力されたら遷移を要求する。立っているだけでは遷移しない。
        /// </summary>
        private static AreaExitGate CreateExitGate(
            Transform root, string name, Vector3 position,
            StableId destinationArea, StableId destinationEntry, Vector3 exitDirection)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = position;

            BoxCollider trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.6f, Phase5Layout.WallHeight, Phase5Layout.CorridorWidth);

            AreaExitGate gate = go.AddComponent<AreaExitGate>();
            gate.Configure(destinationArea, destinationEntry, exitDirection);
            return gate;
        }
    }
}
