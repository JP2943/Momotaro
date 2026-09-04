using System.Collections.Generic;
using System.IO;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.SceneFlow;
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
        private const string JustGuardVfxFolder = "Assets/_Project/Art/VFX/JustGuard"; // 無方向フラットの JG 閃光（P3.5-08B）。
        private const string JustGuardSeId = "SE_JustGuard"; // CombatFeedbackMap の JG SeId と一致させる。
        private const string JustGuardSePath = "Assets/_Project/Audio/SE/Player/JustGuard/JustGuard.ogg";
        private const string JustEvadeSeId = "SE_JustEvade"; // CombatFeedbackMap の JustEvade SeId と一致させる（P3.5-09）。
        private const string JustEvadeSePath = "Assets/_Project/Audio/SE/Player/JustEvade/JustEvade.ogg";
        private const int JustGuardFrameCount = 4; // JG 閃光は無方向フラットの 4 コマ。

        // 主人公スイング SE（刀を振る音。P3.5-08C）。ヒット SE とは別系統で、Active 立ち上がりに同期して段別に鳴らす。
        private const string PlayerAudioRoot = "Assets/_Project/Audio/SE/Player";
        private const string SwingStage1SeId = "SE_Player_Attack1";
        private const string SwingStage2SeId = "SE_Player_Attack2";
        private const string SwingStage3SeId = "SE_Player_Attack3";
        private const string SwingSpecialSeId = "SE_Player_Special";
        private const string SwingStage1SePath = PlayerAudioRoot + "/Attack/Attack_01.ogg";
        private const string SwingStage2SePath = PlayerAudioRoot + "/Attack/Attack_02.ogg";
        private const string SwingStage3SePath = PlayerAudioRoot + "/Attack/Attack_03.ogg";
        private const string SwingSpecialSePath = PlayerAudioRoot + "/Special/Special_01.ogg";

        // 通常ガード成功 SE（ヒット結果系。P3.5-08B）。
        private const string GuardSeId = "SE_Guard"; // CombatFeedbackMap の Guard SeId と一致させる。
        private const string GuardSePath = PlayerAudioRoot + "/Guard/Guard.ogg";
        private const float GuardVolume = 0.15f; // 通常ガード SE は大幅に控えめ（試遊調整）。

        // 主人公ヒット音（攻撃命中時。段別に出し分け。1・2段=Hit1／3段・必殺技=Hit2）。
        private const string HitStage12SeId = "SE_Player_Hit1";
        private const string HitStage3SeId = "SE_Player_Hit2";
        private const string HitStage12SePath = PlayerAudioRoot + "/Attack/Hit_01.ogg";
        private const string HitStage3SePath = PlayerAudioRoot + "/Attack/Hit_02.ogg";
        private const float HitVolume = 0.7f; // ヒット音は手応えの主音（試遊調整）。

        // 主人公ステップ（回避）SE。
        private const string StepSeId = "SE_Player_Step";
        private const string StepSePath = PlayerAudioRoot + "/Step/Step.ogg";
        private const float StepVolume = 0.45f; // 移動アクション音（試遊調整）。

        // 敵攻撃スイング SE（P3.5-08C・敵側）。主人公より大幅に音量を抑える。侍骸骨の通常・強は共通 SE。
        private const string EnemyAudioRoot = "Assets/_Project/Audio/SE/Enemy";
        private const float EnemySwingVolume = 0.12f; // 主人公スイング(0.3)より大幅に小さく（§7.7 の敵SEは控えめ）。
        private const string EnemySwordsmanSeId = "SE_Enemy_Swordsman";
        private const string EnemySamuraiSeId = "SE_Enemy_Samurai";
        private const string EnemySamuraiThrustSeId = "SE_Enemy_Samurai_Thrust";
        private const string EnemyBowSeId = "SE_Enemy_Bow";
        private const string EnemySwordsmanSePath = EnemyAudioRoot + "/SkeletonSwordsman/Attack.ogg";
        private const string EnemySamuraiSePath = EnemyAudioRoot + "/SamuraiSkelton/Attack.ogg";
        private const string EnemySamuraiThrustSePath = EnemyAudioRoot + "/SamuraiSkelton/Thrust.ogg";
        private const string EnemyBowSePath = EnemyAudioRoot + "/SkeletonArcher/Bow.ogg";

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
                + "(CombatSession+WaveRunner+CombatOutcome+CombatSceneReloader+CombatRetryInput+CombatTrialHud"
                + "+EnemyDebugToggle+CombatFeedback+CombatVFX), 初期敵0体・4Wave構成。VFX 素材は全方向の期待枚数を満たしています。";
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

            // 進行データ（徳・付与済み Reward）の保持先（P4-00）。Scene 常駐＝Retry（Scene 再読込）で破棄されリセットされる。
            var progressGo = new GameObject("PlayerProgress");
            progressGo.transform.SetParent(systems.transform, false);
            var progress = progressGo.AddComponent<PlayerProgressHolder>();

            // 撃破報酬の受け手（P4-00）。Session の EnemyDefeated を購読し徳を付与する（敵の探索・再スキャンはしない）。
            var rewardGo = new GameObject("CombatRewardCollector");
            rewardGo.transform.SetParent(systems.transform, false);
            var rewardCollector = rewardGo.AddComponent<CombatRewardCollector>();
            rewardCollector.Bind(session, progress);

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

            // 勝敗・リトライ統合（P3.5-08）。最終Wave完了→Victory、Player死亡→Defeat、入力ロック・結果パネル遅延・Retry受付。
            var outcomeGo = new GameObject("CombatOutcome");
            outcomeGo.transform.SetParent(systems.transform, false);
            var outcome = outcomeGo.AddComponent<CombatOutcomeController>();
            outcome.Bind(session, waveRunner);

            // 現在Sceneの安全なAsync再読込Adapter（P3.5-08）。Session へ Runtime 結線（ICombatSceneReloader）。
            var reloaderGo = new GameObject("CombatSceneReloader");
            reloaderGo.transform.SetParent(systems.transform, false);
            var reloader = reloaderGo.AddComponent<CombatSceneReloader>();
            var reloaderSo = new SerializedObject(reloader);
            SetRef(reloaderSo, "_session", session);
            reloaderSo.ApplyModifiedPropertiesWithoutUndo();

            // Retry 入力（結果状態でのみ受付。Gameplay Map が閉じても効くよう Input System Device を直接読む。P3.5-08）。
            var retryGo = new GameObject("CombatRetryInput");
            retryGo.transform.SetParent(systems.transform, false);
            var retry = retryGo.AddComponent<CombatRetryInput>();
            retry.Bind(outcome);

            // 試遊 HUD（HP/Stamina/Special/GuardBreak/Wave/勝敗。Debug HUD と分離。P3.5-04）。
            var hudGo = new GameObject("CombatTrialHud");
            hudGo.transform.SetParent(systems.transform, false);
            var hud = hudGo.AddComponent<CombatPlayHud>();
            WireHud(hud, playerVitals, playerController, session, waveRunner, outcome);

            var toggleGo = new GameObject("EnemyDebugToggle");
            toggleGo.transform.SetParent(systems.transform, false);
            toggleGo.AddComponent<EnemyDebugToggle>();

            BuildFeedback(systems.transform, cameraGo.transform);
            BuildVfx(systems.transform, playerController, cam);
            BuildEnemyAudio(systems.transform);
            BuildPlayerHitAudio(systems.transform);
            BuildPlayerStepAudio(systems.transform, playerController);
        }

        private static Transform CreateSpawnPoint(string name, Vector3 pos, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos; // ルート Y=0。
            return go.transform;
        }

        /// <summary>試遊 HUD の Serialized 参照（Player/PlayerState/Session/Wave/Outcome）を設定する（Runtime の自動探索より決定的）。</summary>
        private static void WireHud(CombatPlayHud hud, PlayerVitalsHolder playerVitals,
            PlayerStateController playerState, CombatSessionController session, WaveRunner waveRunner,
            CombatOutcomeController outcome)
        {
            var so = new SerializedObject(hud);
            SetRef(so, "_player", playerVitals);
            SetRef(so, "_playerState", playerState);
            SetRef(so, "_session", session);
            SetRef(so, "_waves", waveRunner);
            SetRef(so, "_outcome", outcome);
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
            // ヒット結果 SE（P3.5-08B）。CombatFeedbackMap が種別→SeId を解決し、CombatFeedbackPresenter が Play する。
            // 実素材（OGG）をスロットへ差し込む。未 Import でも clip=null で無音・無例外（Play 側が安全）。ヒット音は後日追加予定。
            se.Slots = new[]
            {
                new CombatSePlayer.SeSlot
                {
                    seId = JustGuardSeId,
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(JustGuardSePath),
                    volume = 1f,
                },
                new CombatSePlayer.SeSlot
                {
                    seId = GuardSeId,
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(GuardSePath),
                    volume = GuardVolume, // 大幅に控えめ（他のヒット結果 SE より小さく）。
                },
                new CombatSePlayer.SeSlot
                {
                    seId = JustEvadeSeId, // ジャスト回避成立音（P3.5-09）。CombatFeedbackMap の JustEvade SeId と一致。
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(JustEvadeSePath),
                    volume = 1f,
                },
            };

            var coordinator = go.AddComponent<CombatFeedbackPresenter>();
            coordinator.HitStop = hitStop;
            coordinator.Flash = flash;
            coordinator.CameraShake = shake;
            coordinator.Se = se;

            go.AddComponent<EnemyDefeatFadePresenter>();
        }

        /// <summary>
        /// 敵攻撃スイング SE（P3.5-08C・敵側）。専用の <see cref="CombatSePlayer"/>（＝主人公 SE とは別インスタンス）へ敵タイプ別スロットを
        /// 差し込み、<see cref="EnemyAttackSwingSePresenter"/> が敵の Active 立ち上がりで鳴らす。音量は主人公より大幅に抑える（§7.7）。
        /// 侍骸骨の通常・強は共通 SE。未 Import でも clip=null で無音・無例外。
        /// </summary>
        private static void BuildEnemyAudio(Transform systems)
        {
            var go = new GameObject("EnemyCombatSE");
            go.transform.SetParent(systems, false);

            var se = go.AddComponent<CombatSePlayer>();
            se.Slots = new[]
            {
                new CombatSePlayer.SeSlot { seId = EnemySwordsmanSeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(EnemySwordsmanSePath), volume = EnemySwingVolume },
                new CombatSePlayer.SeSlot { seId = EnemySamuraiSeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(EnemySamuraiSePath), volume = EnemySwingVolume },
                new CombatSePlayer.SeSlot { seId = EnemySamuraiThrustSeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(EnemySamuraiThrustSePath), volume = EnemySwingVolume },
                new CombatSePlayer.SeSlot { seId = EnemyBowSeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(EnemyBowSePath), volume = EnemySwingVolume },
            };

            var presenter = go.AddComponent<EnemyAttackSwingSePresenter>();
            presenter.Se = se;
            presenter.ProjectileSeId = EnemyBowSeId;
            presenter.Entries = new[]
            {
                // 鍵は EnemyAttackController.SlashVfxKey（archetype）に一致：近接骸骨=Small／侍骸骨=Medium。
                new EnemyAttackSwingSePresenter.EnemySeEntry { key = "Small", normalSeId = EnemySwordsmanSeId },
                new EnemyAttackSwingSePresenter.EnemySeEntry
                {
                    key = "Medium",
                    normalSeId = EnemySamuraiSeId,
                    heavySeId = EnemySamuraiSeId,          // 通常・強は共通 SE。
                    unblockableSeId = EnemySamuraiThrustSeId,
                },
            };
        }

        /// <summary>
        /// 主人公ヒット音（攻撃命中時。P3.5-08B/09）。専用の <see cref="CombatSePlayer"/> へ段別スロットを差し込み、
        /// <see cref="PlayerHitSePresenter"/> が命中（Damage）時に段（1・2＝Hit1／3・必殺技＝Hit2）で出し分ける。未 Import でも無音・無例外。
        /// </summary>
        private static void BuildPlayerHitAudio(Transform systems)
        {
            var go = new GameObject("PlayerHitSE");
            go.transform.SetParent(systems, false);

            var se = go.AddComponent<CombatSePlayer>();
            se.Slots = new[]
            {
                new CombatSePlayer.SeSlot { seId = HitStage12SeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(HitStage12SePath), volume = HitVolume },
                new CombatSePlayer.SeSlot { seId = HitStage3SeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(HitStage3SePath), volume = HitVolume },
            };

            var presenter = go.AddComponent<PlayerHitSePresenter>();
            presenter.Se = se;
        }

        /// <summary>
        /// 主人公ステップ（回避）SE（P3.5-09）。専用 <see cref="CombatSePlayer"/> にステップ SE を差し込み、
        /// <see cref="PlayerStepSePresenter"/> がステップ開始の立ち上がりで鳴らす。未 Import でも無音・無例外。
        /// </summary>
        private static void BuildPlayerStepAudio(Transform systems, PlayerStateController playerController)
        {
            var go = new GameObject("PlayerStepSE");
            go.transform.SetParent(systems, false);

            var se = go.AddComponent<CombatSePlayer>();
            se.Slots = new[]
            {
                new CombatSePlayer.SeSlot { seId = StepSeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(StepSePath), volume = StepVolume },
            };

            var presenter = go.AddComponent<PlayerStepSePresenter>();
            presenter.Se = se;
            if (playerController != null)
            {
                var so = new SerializedObject(presenter);
                SerializedProperty prop = so.FindProperty("_player");
                if (prop != null)
                {
                    prop.objectReferenceValue = playerController;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
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
            // 必殺技は判定を長く持続し前方へ進む（P3.5-09）。剣閃も Active 秒（SO_Special_Momotaro=0.35）に合わせて長く残し、判定へ追従させる。
            playerVfx.SpecialFrames = PlayerSet("Special", 0.35f);
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
            // P3.5-09 視認性調整：頭上 HP／体幹バー（1.6m）より高く（2.9m）、ワールド VFX の最前面（sorting 100）へ Scene に焼き込む。
            var warnSo = new SerializedObject(warn);
            SerializedProperty warnHeight = warnSo.FindProperty("_height");
            SerializedProperty warnOrder = warnSo.FindProperty("_sortingOrder");
            if (warnHeight != null)
            {
                warnHeight.floatValue = 2.9f;
            }

            if (warnOrder != null)
            {
                warnOrder.intValue = 100;
            }

            warnSo.ApplyModifiedPropertiesWithoutUndo();

            // ジャストガード閃光（P3.5-08B。接触点へ無方向フラッシュ。枚数は ValidateVfx で保証済み）。
            var jgVfx = go.AddComponent<JustGuardVfxPresenter>();
            jgVfx.SetCamera(camera); // 正対（billboard）・深度補正の基準。
            jgVfx.FlashFrames = LoadFrames(JustGuardVfxFolder);

            // 主人公スイング SE（刀を振る音。P3.5-08C）。ヒット SE とは別系統のため専用 CombatSePlayer を持ち、段の出現（Startup）に段別発火。
            // clip 未 Import でも null で無音・無例外（未確定素材でも Scene は壊れない）。
            // 音量はヒット SE が後段で加わる前提で控えめにする（P3.5-08C 調整）。体感で約半分になるようリニア 0.3（≒ -10dB）とする
            // （0.5=-6dB は振幅半分でも体感差が小さいため）。
            const float swingVolume = 0.3f;
            var swingSe = go.AddComponent<CombatSePlayer>();
            swingSe.Slots = new[]
            {
                new CombatSePlayer.SeSlot { seId = SwingStage1SeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SwingStage1SePath), volume = swingVolume },
                new CombatSePlayer.SeSlot { seId = SwingStage2SeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SwingStage2SePath), volume = swingVolume },
                new CombatSePlayer.SeSlot { seId = SwingStage3SeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SwingStage3SePath), volume = swingVolume },
                new CombatSePlayer.SeSlot { seId = SwingSpecialSeId, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SwingSpecialSePath), volume = swingVolume },
            };

            var swingPresenter = go.AddComponent<PlayerAttackSwingSePresenter>();
            swingPresenter.Se = swingSe;
            if (playerController != null)
            {
                var so = new SerializedObject(swingPresenter);
                SerializedProperty prop = so.FindProperty("_player");
                if (prop != null)
                {
                    prop.objectReferenceValue = playerController;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
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
            CheckCount(JustGuardVfxFolder, JustGuardFrameCount, errors); // JG 閃光（無方向フラット。P3.5-08B）。
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
