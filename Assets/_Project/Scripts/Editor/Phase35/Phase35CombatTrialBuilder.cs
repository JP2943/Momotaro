using System.Collections.Generic;
using System.IO;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Combat;
using Momotaro.Presentation.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase35
{
    /// <summary>
    /// Phase 3.5 の試遊検証 Scene（SCN_Phase35_CombatTrial）を Unity Editor 上で決定的に生成する Editor ツール（Phase3.5 P3.5-06）。
    /// Scene YAML を手書きせず Editor API で生成する。ユーザーがメニューを明示実行したときだけ動き、起動・コンパイル・Import 時には自動生成しない。
    /// 同じプロジェクト状態から実行すれば毎回同等の構成（名前・階層・Transform・参照が一定）になる。既存の手動配置 Scene（SCN_VS_Field）には触れない。
    ///
    /// 構成：Environment(Floor+Wall×4)/Player(Prefab)/CameraRig(TopDownCameraFollow)+Main Camera(子・被 Shake)/Directional Light/SceneMode/
    /// SpawnCenter/Phase35Systems{ EnemyTestFieldController(初期敵0・Context Menu で編成), EnemyDebugToggle,
    /// CombatFeedback( Dispatcher + HitStop/Flash/CameraShake/SE + CombatFeedbackPresenter + EnemyDefeatFade ),
    /// CombatVFX( PlayerSlashVfx + EnemySlashVfx + UnblockableWarning ) }。斬撃/警告素材は規約パスから割り当てる。
    ///
    /// 失敗方針：必要 Prefab（Player/近接/遠距離/強敵）が欠ける場合は Scene に一切触れず失敗する。VFX 素材が欠けるフォルダは空割当（無表示・安全）
    /// とし、欠落一覧を Message/警告ログへ出す（黙って握り潰さない）。Pause 系は本 Phase 未実装のため HitStop の PausedQuery は未接続（将来接続）。
    /// </summary>
    public static class Phase35CombatTrialBuilder
    {
        /// <summary>既定の生成先。</summary>
        public const string DefaultScenePath = "Assets/_Project/Scenes/Tests/SCN_Phase35_CombatTrial.unity";

        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";
        private const string MeleePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";
        private const string RangedPrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Ranged_Prototype.prefab";
        private const string ElitePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        private const string SlashRoot = "Assets/_Project/Art/VFX/Slash";
        private const string ThrustRoot = "Assets/_Project/Art/VFX/Thrust";
        private const string WarningFolder = "Assets/_Project/Art/VFX/Warning/Warning_Enemy_Unguardable_A";

        /// <summary>生成結果。</summary>
        public readonly struct BuildResult
        {
            /// <summary>成功したか。</summary>
            public bool Success { get; }
            /// <summary>生成先パス。</summary>
            public string ScenePath { get; }
            /// <summary>説明（失敗理由・欠落素材など）。</summary>
            public string Message { get; }

            public BuildResult(bool success, string scenePath, string message)
            {
                Success = success;
                ScenePath = scenePath;
                Message = message;
            }
        }

        /// <summary>メニューから対話的に生成する（上書き確認あり）。</summary>
        [MenuItem("Momotaro/Phase 3.5/Generate Combat Trial")]
        public static void GenerateInteractive()
        {
            if (File.Exists(DefaultScenePath))
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Phase 3.5 試遊Scene生成",
                    "既存の試遊Sceneを上書きします:\n" + DefaultScenePath + "\n\n続行しますか？",
                    "上書き生成", "キャンセル");
                if (!ok)
                {
                    return; // キャンセル時は一切変更しない。
                }
            }

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

            var controller = Object.FindAnyObjectByType<EnemyTestFieldController>();
            if (controller != null)
            {
                Selection.activeGameObject = controller.gameObject;
            }

            Debug.Log("[Phase3.5] 試遊Scene生成: " + DefaultScenePath + " — " + r.Message
                + " Play 後 EnemyTestFieldController の Context Menu から編成を選択してください。");
        }

        /// <summary>
        /// 内部 Builder（ダイアログ無し。テスト用に出力先を引数で受ける）。必要 Prefab が欠ければ Scene に一切触れず失敗を返す。成功時は
        /// 生成 Scene を保存し、開いたまま（Active・保存済み・非 Dirty）で返す。呼び出し側は現在の作業 Scene を事前に保護すること。
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

            var missing = new List<string>();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Populate(playerPrefab, meleePrefab, rangedPrefab, elitePrefab, missing);
            }
            catch (System.Exception e)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return new BuildResult(false, outputPath, "生成中に例外: " + e.Message);
            }

            if (!EditorSceneManager.SaveScene(scene, outputPath))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return new BuildResult(false, outputPath, "Scene の保存に失敗しました。");
            }

            AssetDatabase.Refresh();

            string msg = "Environment/Player/CameraRig+Main Camera/Light/SceneMode/SpawnCenter/Phase35Systems"
                + "(EnemyTestFieldController+EnemyDebugToggle+CombatFeedback+CombatVFX), 初期敵0体。";
            if (missing.Count > 0)
            {
                string list = string.Join(", ", missing);
                msg += " ※未割当VFX素材(空表示): " + list;
                Debug.LogWarning("[Phase3.5] 一部 VFX 素材フォルダが空/不在のため空割当にしました: " + list);
            }

            return new BuildResult(true, outputPath, msg);
        }

        private static void Populate(GameObject playerPrefab, GameObject meleePrefab, GameObject rangedPrefab, GameObject elitePrefab, List<string> missing)
        {
            // Environment（Floor 上面 Y=0、壁は正スケールのみ）。
            var environment = new GameObject("Environment");
            CreateBox("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), environment.transform);
            CreateBox("Wall_North", new Vector3(0f, 1.5f, 15f), new Vector3(30f, 3f, 1f), environment.transform);
            CreateBox("Wall_South", new Vector3(0f, 1.5f, -15f), new Vector3(30f, 3f, 1f), environment.transform);
            CreateBox("Wall_East", new Vector3(15f, 1.5f, 0f), new Vector3(1f, 3f, 30f), environment.transform);
            CreateBox("Wall_West", new Vector3(-15f, 1.5f, 0f), new Vector3(1f, 3f, 30f), environment.transform);

            // Player（完成 Prefab を実体化。ルート Y=0）。
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 0f, -6f);
            var playerController = player.GetComponentInChildren<PlayerStateController>(true);

            // CameraRig（TopDownCameraFollow は自分の position を毎フレーム上書きするため、揺れは子カメラの localPosition に当てる）。
            var rig = new GameObject("CameraRig");
            rig.transform.position = new Vector3(0f, 12f, -14f);
            rig.AddComponent<TopDownCameraFollow>().SetTarget(player.transform);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(rig.transform, false);
            cameraGo.transform.localPosition = Vector3.zero;
            cameraGo.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
            cameraGo.AddComponent<AudioListener>();

            // Lighting。
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;

            // SceneMode（既定で Exploration＝Player 操作可能）。
            var sceneModeGo = new GameObject("SceneMode");
            sceneModeGo.AddComponent<GameplaySceneMode>();

            // SpawnCenter（生成の中心）。
            var spawnCenter = new GameObject("SpawnCenter");
            spawnCenter.transform.position = Vector3.zero;

            // Phase35Systems（編成＋フィードバック＋VFX を一元管理）。
            var systems = new GameObject("Phase35Systems");

            var controllerGo = new GameObject("EnemyTestFieldController");
            controllerGo.transform.SetParent(systems.transform, false);
            var controller = controllerGo.AddComponent<EnemyTestFieldController>();
            AssignController(controller, meleePrefab, rangedPrefab, elitePrefab, spawnCenter.transform);

            var toggleGo = new GameObject("EnemyDebugToggle");
            toggleGo.transform.SetParent(systems.transform, false);
            toggleGo.AddComponent<EnemyDebugToggle>();

            BuildFeedback(systems.transform, cameraGo.transform);
            BuildVfx(systems.transform, playerController, cam, missing);
        }

        private static void BuildFeedback(Transform systems, Transform cameraTransform)
        {
            // 手応え演出（P3.5-05B）を 1 つの GameObject に集約（各型は DisallowMultipleComponent だが別型なので共存可）。
            var go = new GameObject("CombatFeedback");
            go.transform.SetParent(systems, false);

            // 命中結果 → 仮 Cue 配信（主人公・ダミー・敵を購読）。
            go.AddComponent<CombatFeedbackDispatcher>();

            var hitStop = go.AddComponent<HitStopController>();
            var flash = go.AddComponent<HitFlashPresenter>();
            var shake = go.AddComponent<CameraShakePresenter>();
            shake.Target = cameraTransform; // 揺れは子カメラの localPosition に当てる（follow と非競合）。
            var se = go.AddComponent<CombatSePlayer>();

            var coordinator = go.AddComponent<CombatFeedbackPresenter>();
            coordinator.HitStop = hitStop;
            coordinator.Flash = flash;
            coordinator.CameraShake = shake;
            coordinator.Se = se;

            go.AddComponent<EnemyDefeatFadePresenter>();
        }

        private static void BuildVfx(Transform systems, PlayerStateController playerController, Camera camera, List<string> missing)
        {
            var go = new GameObject("CombatVFX");
            go.transform.SetParent(systems, false);

            // 主人公の剣閃（通常1〜3段＋必殺技）。
            var playerVfx = go.AddComponent<PlayerSlashVfxPresenter>();
            playerVfx.SetCamera(camera); // 正対（billboard）・表示位置補正の基準（P3.5-06）。
            playerVfx.Stage1Frames = PlayerSet("Slash_Small_A", 0.12f, missing);
            playerVfx.Stage2Frames = PlayerSet("Slash_Small_B", 0.12f, missing);
            playerVfx.Stage3Frames = PlayerSet("Slash_Small_C", 0.14f, missing);
            playerVfx.SpecialFrames = PlayerSet("Slash_Special_A", 0.2f, missing);
            if (playerController != null)
            {
                var so = new SerializedObject(playerVfx);
                SerializedProperty prop = so.FindProperty("_player");
                if (prop != null)
                {
                    prop.objectReferenceValue = playerController;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // 敵の剣閃（鍵：Small=近接骸骨／Medium=侍骸骨。Medium は強・ガード不能も持つ）。
            var enemyVfx = go.AddComponent<EnemySlashVfxPresenter>();
            enemyVfx.SetCamera(camera); // 正対（billboard）・表示位置補正の基準（P3.5-06）。
            enemyVfx.Entries = new[]
            {
                new EnemySlashVfxPresenter.EnemySlashEntry
                {
                    key = "Small",
                    normal = EnemySet(SlashRoot + "/Slash_Enemy_Small_A", 0.12f, missing),
                },
                new EnemySlashVfxPresenter.EnemySlashEntry
                {
                    key = "Medium",
                    normal = EnemySet(SlashRoot + "/Slash_Enemy_Medium_A", 0.12f, missing),
                    heavy = EnemySet(SlashRoot + "/Slash_Enemy_Heavy_A", 0.14f, missing),
                    unblockable = EnemySet(ThrustRoot + "/Thrust_Enemy_Unguardable_A", 0.18f, missing),
                },
            };

            // ガード不能の頭上警告。
            var warn = go.AddComponent<EnemyUnblockableWarningPresenter>();
            Sprite[] warnFrames = LoadFrames(WarningFolder);
            warn.WarningFrames = warnFrames;
            if (warnFrames.Length == 0)
            {
                missing.Add("Warning_Enemy_Unguardable_A");
            }
        }

        private static PlayerSlashVfxPresenter.SlashFrameSet PlayerSet(string setFolder, float duration, List<string> missing)
        {
            string b = SlashRoot + "/" + setFolder;
            var set = new PlayerSlashVfxPresenter.SlashFrameSet
            {
                down = LoadFrames(b + "/Down"),
                up = LoadFrames(b + "/Up"),
                left = LoadFrames(b + "/Left"),
                right = LoadFrames(b + "/Right"),
                duration = duration,
            };
            if (IsEmpty(set.down) && IsEmpty(set.up) && IsEmpty(set.left) && IsEmpty(set.right))
            {
                missing.Add(setFolder);
            }

            return set;
        }

        private static EnemySlashVfxPresenter.SlashFrameSet EnemySet(string baseFolder, float duration, List<string> missing)
        {
            var set = new EnemySlashVfxPresenter.SlashFrameSet
            {
                down = LoadFrames(baseFolder + "/Down"),
                up = LoadFrames(baseFolder + "/Up"),
                left = LoadFrames(baseFolder + "/Left"),
                right = LoadFrames(baseFolder + "/Right"),
                duration = duration,
            };
            if (IsEmpty(set.down) && IsEmpty(set.up) && IsEmpty(set.left) && IsEmpty(set.right))
            {
                missing.Add(Path.GetFileName(baseFolder));
            }

            return set;
        }

        private static bool IsEmpty(Sprite[] a)
        {
            return a == null || a.Length == 0;
        }

        /// <summary>規約フォルダ配下の Sprite を名前順（＝コマ順）に読み込む。フォルダ不在・空は空配列（無表示・安全）。</summary>
        private static Sprite[] LoadFrames(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                return System.Array.Empty<Sprite>();
            }

            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folder });
            var paths = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(p))
                {
                    paths.Add(p);
                }
            }

            paths.Sort(System.StringComparer.Ordinal); // ファイル名順＝コマ順（決定的）。
            var sprites = new List<Sprite>();
            for (int i = 0; i < paths.Count; i++)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
                if (s != null)
                {
                    sprites.Add(s);
                }
            }

            return sprites.ToArray();
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
