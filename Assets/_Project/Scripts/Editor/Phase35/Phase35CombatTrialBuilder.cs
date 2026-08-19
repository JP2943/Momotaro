using System.Collections.Generic;
using System.IO;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Combat;
using Momotaro.Presentation.Diagnostics;
using Momotaro.Presentation.Hud;
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
    /// SpawnPoints(×4)/Phase35Systems{ CombatSession(P3.5-03), WaveRunner(初期敵0・4Wave 連続進行 P3.5-07), CombatTrialHud(P3.5-04),
    /// EnemyDebugToggle, CombatFeedback( Dispatcher + HitStop/Flash/CameraShake/SE + CombatFeedbackPresenter + EnemyDefeatFade ),
    /// CombatVFX( PlayerSlashVfx + EnemySlashVfx + UnblockableWarning ) }。斬撃/警告素材は規約パスから割り当てる。
    ///
    /// 失敗方針（完成 VFX 前提）：必要 Prefab（Player/近接/遠距離/強敵）が欠ける場合、および斬撃/警告素材が方向ごとの期待枚数と一致しない
    /// （欠け・過不足・フォルダ不在）場合は、Scene に一切触れず失敗する（具体パス＋実枚数を Message に列挙）。壊れた Scene を保存しない。
    /// Pause 系は本 Phase 未実装のため HitStop の PausedQuery は未接続（将来接続）。
    /// </summary>
    public static class Phase35CombatTrialBuilder
    {
        /// <summary>既定の生成先。</summary>
        public const string DefaultScenePath = "Assets/_Project/Scenes/Tests/SCN_Phase35_CombatTrial.unity";

        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";
        private const string MeleePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";
        private const string RangedPrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Ranged_Prototype.prefab";
        private const string ElitePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        private const string PlayerSlashRoot = "Assets/_Project/Art/VFX/Slash/Player";
        private const string EnemySlashRoot = "Assets/_Project/Art/VFX/Slash/Enemy";
        private const string WarningFolder = "Assets/_Project/Art/VFX/Warning/Enemy/Medium/Unblockable";

        private static readonly string[] Directions = { "Down", "Up", "Left", "Right" };

        // 完成済み VFX の期待枚数（方向別セット：フォルダ相対名, 1 方向あたりの期待コマ数）。生成前検証に用いる。
        private static readonly (string folder, int perDir)[] PlayerVfxSpec =
        {
            ("Combo1", 3), ("Combo2", 3), ("Combo3", 4), ("Special", 5),
        };

        private static readonly (string folder, int perDir)[] EnemyVfxSpec =
        {
            ("Small/Normal", 3), ("Medium/Normal", 3), ("Medium/Heavy", 4), ("Medium/Unblockable", 4),
        };

        private const int WarningFrameCount = 4; // ガード不能予告は無方向フラットの 4 コマ。

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

            var waveRunner = Object.FindAnyObjectByType<WaveRunner>();
            if (waveRunner != null)
            {
                Selection.activeGameObject = waveRunner.gameObject;
            }

            Debug.Log("[Phase3.5] 試遊Scene生成: " + DefaultScenePath + " — " + r.Message
                + " Play すると Wave1 から連続ウェーブが開始します（§8.2）。");
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

            // 完成済み VFX 前提：方向ごとの期待枚数と一致しなければ、Scene を保存せず（一切触れず）失敗する。方向欠け・過不足も検出する。
            var vfxErrors = new List<string>();
            ValidateVfx(vfxErrors);
            if (vfxErrors.Count > 0)
            {
                return new BuildResult(false, outputPath,
                    "VFX 素材が期待枚数と一致しません（Scene は保存していません）:\n- " + string.Join("\n- ", vfxErrors));
            }

            EnsureFolder(Path.GetDirectoryName(outputPath).Replace('\\', '/'));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Populate(playerPrefab, meleePrefab, rangedPrefab, elitePrefab);
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

            string msg = "Environment/Player/CameraRig+Main Camera/Light/SceneMode/SpawnPoints(×4)/Phase35Systems"
                + "(CombatSession+WaveRunner+CombatTrialHud+EnemyDebugToggle+CombatFeedback+CombatVFX), 初期敵0体・4Wave構成。"
                + "VFX 素材は全方向の期待枚数を満たしています。";
            return new BuildResult(true, outputPath, msg);
        }

        private static void Populate(GameObject playerPrefab, GameObject meleePrefab, GameObject rangedPrefab, GameObject elitePrefab)
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
            var playerVitals = player.GetComponentInChildren<PlayerVitalsHolder>(true);
            var playerHurt = player.GetComponentInChildren<PlayerHitReaction>(true);

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

            // 固定 Spawn Point（Player=(0,0,-6) と重ならず Camera 内。連続 Wave の生成位置。§8.3）。
            var spawnPoints = new GameObject("SpawnPoints");
            Transform[] spawnTransforms =
            {
                CreateSpawnPoint("SpawnPoint_0", new Vector3(0f, 0f, 4f), spawnPoints.transform),
                CreateSpawnPoint("SpawnPoint_1", new Vector3(-4f, 0f, 5f), spawnPoints.transform),
                CreateSpawnPoint("SpawnPoint_2", new Vector3(4f, 0f, 5f), spawnPoints.transform),
                CreateSpawnPoint("SpawnPoint_3", new Vector3(0f, 0f, 7f), spawnPoints.transform),
            };

            // Phase35Systems（Session＋Wave 進行＋フィードバック＋VFX を一元管理）。
            var systems = new GameObject("Phase35Systems");

            // 戦闘 Session（状態・生存数・Player/Enemy 死亡購読の基盤。P3.5-03）。
            var sessionGo = new GameObject("CombatSession");
            sessionGo.transform.SetParent(systems.transform, false);
            var session = sessionGo.AddComponent<CombatSessionController>();

            // 連続 Wave 進行（P3.5-07）。Session/Player 死亡購読は WaveRunner が Runtime に結線する。
            var waveGo = new GameObject("WaveRunner");
            waveGo.transform.SetParent(systems.transform, false);
            var waveRunner = waveGo.AddComponent<WaveRunner>();
            waveRunner.ConfigurePrefabs(meleePrefab, rangedPrefab, elitePrefab);
            waveRunner.ConfigureSpawnPoints(spawnTransforms);
            waveRunner.ConfigureWaves(new[]
            {
                new WaveDefinition(1, 0, 0), // Wave1：骸骨剣士 ×1。
                new WaveDefinition(0, 1, 0), // Wave2：骸骨弓兵 ×1。
                new WaveDefinition(2, 1, 0), // Wave3：剣士 ×2＋弓兵 ×1（混成）。
                new WaveDefinition(0, 0, 1), // Wave4：侍骸骨 ×1（強敵）。
            });
            waveRunner.Bind(session, playerController, playerVitals, playerHurt);

            // 試遊 HUD（HP/Stamina/Special/GuardBreak/Wave/勝敗。Debug HUD と分離。P3.5-04）。
            var hudGo = new GameObject("CombatTrialHud");
            hudGo.transform.SetParent(systems.transform, false);
            var hud = hudGo.AddComponent<CombatPlayHud>();
            WireHud(hud, playerVitals, playerController, session, waveRunner);

            var toggleGo = new GameObject("EnemyDebugToggle");
            toggleGo.transform.SetParent(systems.transform, false);
            toggleGo.AddComponent<EnemyDebugToggle>();

            BuildFeedback(systems.transform, cameraGo.transform);
            BuildVfx(systems.transform, playerController, cam);
        }

        private static Transform CreateSpawnPoint(string name, Vector3 pos, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos; // ルート Y=0。
            return go.transform;
        }

        /// <summary>試遊 HUD の Serialized 参照（Player/PlayerState/Session/Wave）を設定する（Runtime の自動探索より決定的）。</summary>
        private static void WireHud(CombatPlayHud hud, PlayerVitalsHolder playerVitals,
            PlayerStateController playerState, CombatSessionController session, WaveRunner waveRunner)
        {
            var so = new SerializedObject(hud);
            SetRef(so, "_player", playerVitals);
            SetRef(so, "_playerState", playerState);
            SetRef(so, "_session", session);
            SetRef(so, "_waves", waveRunner);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRef(SerializedObject so, string prop, UnityEngine.Object value)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p != null)
            {
                p.objectReferenceValue = value;
            }
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

        private static void BuildVfx(Transform systems, PlayerStateController playerController, Camera camera)
        {
            var go = new GameObject("CombatVFX");
            go.transform.SetParent(systems, false);

            // 主人公の剣閃（通常1〜3段＋必殺技）。素材は Build 冒頭の ValidateVfx で期待枚数を保証済み。
            var playerVfx = go.AddComponent<PlayerSlashVfxPresenter>();
            playerVfx.SetCamera(camera); // 正対（billboard）・表示位置補正の基準（P3.5-06）。
            playerVfx.Stage1Frames = PlayerSet("Combo1", 0.12f);
            playerVfx.Stage2Frames = PlayerSet("Combo2", 0.12f);
            // 3段目はジャンプ切り下ろし。判定終了後も着地モーションまで剣閃を残す（holdThroughRecovery＋長め duration）。
            // 攻撃全体 ≈ startup0.18+active0.12+recovery0.35 ≈ 0.65s、VFX は Active 開始(0.18s)から再生 → 0.5s で着地後に消える。
            var combo3 = PlayerSet("Combo3", 0.5f);
            combo3.holdThroughRecovery = true;
            playerVfx.Stage3Frames = combo3;
            playerVfx.SpecialFrames = PlayerSet("Special", 0.2f);
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
                    // 近接骸骨（Small）は通常のみ。強・ガード不能は侍骸骨（Medium）が持つ（敵タイプ鍵は archetype 駆動。P3.5-06）。
                    key = "Small",
                    normal = EnemySet(EnemySlashRoot + "/Small/Normal", 0.12f),
                },
                new EnemySlashVfxPresenter.EnemySlashEntry
                {
                    key = "Medium",
                    normal = EnemySet(EnemySlashRoot + "/Medium/Normal", 0.12f),
                    heavy = EnemySet(EnemySlashRoot + "/Medium/Heavy", 0.14f),
                    unblockable = EnemySet(EnemySlashRoot + "/Medium/Unblockable", 0.18f),
                },
            };

            // ガード不能の頭上警告（枚数は ValidateVfx で保証済み）。
            var warn = go.AddComponent<EnemyUnblockableWarningPresenter>();
            warn.WarningFrames = LoadFrames(WarningFolder);
        }

        private static PlayerSlashVfxPresenter.SlashFrameSet PlayerSet(string setFolder, float duration)
        {
            string b = PlayerSlashRoot + "/" + setFolder;
            return new PlayerSlashVfxPresenter.SlashFrameSet
            {
                down = LoadFrames(b + "/Down"),
                up = LoadFrames(b + "/Up"),
                left = LoadFrames(b + "/Left"),
                right = LoadFrames(b + "/Right"),
                duration = duration,
            };
        }

        private static EnemySlashVfxPresenter.SlashFrameSet EnemySet(string baseFolder, float duration)
        {
            return new EnemySlashVfxPresenter.SlashFrameSet
            {
                down = LoadFrames(baseFolder + "/Down"),
                up = LoadFrames(baseFolder + "/Up"),
                left = LoadFrames(baseFolder + "/Left"),
                right = LoadFrames(baseFolder + "/Right"),
                duration = duration,
            };
        }

        /// <summary>
        /// 完成済み VFX の受入検証（P3.5-06。GPT 指摘対応）。方向別セットは各方向の期待枚数、警告は無方向フラットの期待枚数を検査し、
        /// 不足・過多・フォルダ不在を <paramref name="errors"/> へ具体パス＋実枚数で積む。呼び出し側は空でなければ Scene を保存せず失敗する。
        /// </summary>
        private static void ValidateVfx(List<string> errors)
        {
            for (int i = 0; i < PlayerVfxSpec.Length; i++)
            {
                CheckDirectional(PlayerSlashRoot + "/" + PlayerVfxSpec[i].folder, PlayerVfxSpec[i].perDir, errors);
            }

            for (int i = 0; i < EnemyVfxSpec.Length; i++)
            {
                CheckDirectional(EnemySlashRoot + "/" + EnemyVfxSpec[i].folder, EnemyVfxSpec[i].perDir, errors);
            }

            CheckCount(WarningFolder, WarningFrameCount, errors);
        }

        private static void CheckDirectional(string baseFolder, int perDir, List<string> errors)
        {
            for (int d = 0; d < Directions.Length; d++)
            {
                CheckCount(baseFolder + "/" + Directions[d], perDir, errors);
            }
        }

        private static void CheckCount(string folder, int expected, List<string> errors)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                errors.Add(folder + ": 期待 " + expected + " 枚 / 実際 0 枚（フォルダ不在）");
                return;
            }

            int actual = LoadFrames(folder).Length;
            if (actual != expected)
            {
                errors.Add(folder + ": 期待 " + expected + " 枚 / 実際 " + actual + " 枚");
            }
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
