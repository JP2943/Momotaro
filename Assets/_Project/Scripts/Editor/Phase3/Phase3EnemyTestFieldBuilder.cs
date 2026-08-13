using System.IO;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase3
{
    /// <summary>
    /// Phase 3 専用の検証 Scene（SCN_Phase3_EnemyTest）を Unity Editor 上で決定的に生成する Editor ツール（Phase3 P3-12。§2/§3/§6）。
    /// Scene YAML を手書きせず Editor API で生成する。ユーザーがメニューを明示実行したときだけ動き、起動時・コンパイル時・Import 時には
    /// 自動生成しない。同じプロジェクト状態から実行すれば毎回同等の構成（名前・階層・Transform・参照が一定）になる。既存の手動配置 Scene
    /// （SCN_VS_Field）には触れない。必要な Prefab/Data が欠けている場合は壊れた Scene を保存せず失敗する。
    /// </summary>
    public static class Phase3EnemyTestFieldBuilder
    {
        /// <summary>既定の生成先。</summary>
        public const string DefaultScenePath = "Assets/_Project/Scenes/Tests/SCN_Phase3_EnemyTest.unity";

        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";
        private const string MeleePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";
        private const string RangedPrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Ranged_Prototype.prefab";
        private const string ElitePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        /// <summary>生成結果。</summary>
        public readonly struct BuildResult
        {
            /// <summary>成功したか。</summary>
            public bool Success { get; }
            /// <summary>生成先パス。</summary>
            public string ScenePath { get; }
            /// <summary>説明（失敗理由など）。</summary>
            public string Message { get; }

            public BuildResult(bool success, string scenePath, string message)
            {
                Success = success;
                ScenePath = scenePath;
                Message = message;
            }
        }

        /// <summary>メニューから対話的に生成する（上書き確認あり）。</summary>
        [MenuItem("Momotaro/Phase 3/Generate Enemy Test Field")]
        public static void GenerateInteractive()
        {
            if (File.Exists(DefaultScenePath))
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Phase 3 検証Scene生成",
                    "既存の検証Sceneを上書きします:\n" + DefaultScenePath + "\n\n続行しますか？",
                    "上書き生成", "キャンセル");
                if (!ok)
                {
                    return; // キャンセル時は一切変更しない。
                }
            }

            // 現在の作業 Scene を保護（生成は Single で開き直すため、未保存があればユーザーに保存機会を与える）。
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return; // キャンセル時は一切変更しない。
            }

            BuildResult r = Build(DefaultScenePath);
            if (!r.Success)
            {
                EditorUtility.DisplayDialog("生成失敗", r.Message + "\n（Scene は保存していません）", "OK");
                return;
            }

            // Build が生成 Scene を開いたまま保存済みで残す（Dirty ではない）。改めて開き直す必要はない。
            var controller = Object.FindAnyObjectByType<EnemyTestFieldController>();
            if (controller != null)
            {
                Selection.activeGameObject = controller.gameObject;
            }

            Debug.Log("[Phase3] 検証Scene生成: " + DefaultScenePath
                + " — Environment(Floor+Wall×4)/Player/Main Camera/Directional Light/Phase3TestSystems(EnemyTestFieldController+EnemyDebugToggle)/SpawnCenter, 初期敵0体。"
                + " Play 後 EnemyTestFieldController の Context Menu から編成を選択してください。");
        }

        /// <summary>
        /// 内部 Builder（ダイアログ無し。テスト用に出力先を引数で受ける）。必要資産が欠けていれば Scene に一切触れず失敗を返す。成功時は
        /// 生成 Scene を保存し、開いたまま（Active・保存済み・非 Dirty）で返す。呼び出し側は現在の作業 Scene を事前に保護すること
        /// （<see cref="GenerateInteractive"/> は <c>SaveCurrentModifiedScenesIfUserWantsTo</c> を使う）。Single 生成なので未保存の
        /// 無題 Scene があっても失敗しない（Additive の「untitled unsaved」制約を回避）。
        /// </summary>
        public static BuildResult Build(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath) || !outputPath.StartsWith("Assets/"))
            {
                return new BuildResult(false, outputPath, "出力先は Assets 配下である必要があります。");
            }

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject meleePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MeleePrefabPath);
            GameObject rangedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangedPrefabPath);
            GameObject elitePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ElitePrefabPath);
            if (playerPrefab == null || meleePrefab == null || rangedPrefab == null || elitePrefab == null)
            {
                // ここまでで Scene には一切触れていない（壊れた Scene を残さない）。
                return new BuildResult(false, outputPath,
                    "必要な Prefab が見つかりません（Player/近接/遠距離/強敵）。");
            }

            EnsureFolder(Path.GetDirectoryName(outputPath).Replace('\\', '/'));

            // Single で新規空 Scene を開く（全 Scene を置換）。未保存の無題 Scene があっても Additive のような制約に当たらない。
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Populate(playerPrefab, meleePrefab, rangedPrefab, elitePrefab);
            }
            catch (System.Exception e)
            {
                // 途中失敗：壊れた Scene を保存せず、空 Scene に戻して失敗を返す。
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return new BuildResult(false, outputPath, "生成中に例外: " + e.Message);
            }

            if (!EditorSceneManager.SaveScene(scene, outputPath))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return new BuildResult(false, outputPath, "Scene の保存に失敗しました。");
            }

            AssetDatabase.Refresh();
            return new BuildResult(true, outputPath, "OK"); // scene は開いたまま・保存済み。
        }

        private static void Populate(GameObject playerPrefab, GameObject meleePrefab, GameObject rangedPrefab, GameObject elitePrefab)
        {
            // Environment（Floor 上面 Y=0、壁は正スケールのみ）。
            var environment = new GameObject("Environment");

            GameObject floor = CreateBox("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), environment.transform);
            CreateBox("Wall_North", new Vector3(0f, 1.5f, 15f), new Vector3(30f, 3f, 1f), environment.transform);
            CreateBox("Wall_South", new Vector3(0f, 1.5f, -15f), new Vector3(30f, 3f, 1f), environment.transform);
            CreateBox("Wall_East", new Vector3(15f, 1.5f, 0f), new Vector3(1f, 3f, 30f), environment.transform);
            CreateBox("Wall_West", new Vector3(-15f, 1.5f, 0f), new Vector3(1f, 3f, 30f), environment.transform);

            // Player（完成 Prefab を実体化。ルート Y=0）。
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 0f, -6f);

            // Main Camera（Orthographic 見下ろし、Player 追従）。
            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 12f, -14f);
            cameraGo.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            var cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<TopDownCameraFollow>().SetTarget(player.transform);

            // Lighting。
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;

            // SceneMode（既定で Exploration を要求＝Player を操作可能にする）。
            var sceneModeGo = new GameObject("SceneMode");
            sceneModeGo.AddComponent<GameplaySceneMode>();

            // SpawnCenter（生成の中心）。
            var spawnCenter = new GameObject("SpawnCenter");
            spawnCenter.transform.position = Vector3.zero;

            // Phase3TestSystems（編成の一元管理＋デバッグ切替）。
            var systems = new GameObject("Phase3TestSystems");

            var controllerGo = new GameObject("EnemyTestFieldController");
            controllerGo.transform.SetParent(systems.transform, false);
            var controller = controllerGo.AddComponent<EnemyTestFieldController>();
            AssignController(controller, meleePrefab, rangedPrefab, elitePrefab, spawnCenter.transform);

            var toggleGo = new GameObject("EnemyDebugToggle");
            toggleGo.transform.SetParent(systems.transform, false);
            toggleGo.AddComponent<EnemyDebugToggle>();
        }

        private static GameObject CreateBox(string name, Vector3 pos, Vector3 scale, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale; // 正スケールのみ。
            return go;
        }

        private static void AssignController(EnemyTestFieldController controller, GameObject melee, GameObject ranged, GameObject elite, Transform spawnCenter)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("_meleePrefab").objectReferenceValue = melee;
            so.FindProperty("_rangedPrefab").objectReferenceValue = ranged;
            so.FindProperty("_elitePrefab").objectReferenceValue = elite;
            so.FindProperty("_spawnCenter").objectReferenceValue = spawnCenter;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
