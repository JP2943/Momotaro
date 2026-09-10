using System.IO;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// P4-FIX F01：仲間の検証 Scene（<c>SCN_Phase4_CompanionField</c>）を決定的に生成する Editor ツール。
    /// Phase3 の敵検証フィールド・Phase3.5 の試遊 Scene と同じ形で、Scene YAML を手書きせず Editor API で組む。
    /// メニューを明示実行したときだけ動く（起動・コンパイル・Import では何もしない）。
    ///
    /// <b>なぜ専用 Scene が要るのか。</b>仲間の駆動は「Scene に何が置いてあるか」に強く依存する。
    /// 活動 Context（<see cref="CompanionActivityContext"/>）が無ければ Pause・会話の区別ができず、
    /// 調停役（<see cref="CompanionStateArbiter"/>／<see cref="CompanionMovementArbiter"/>）が無ければ
    /// 各駆動が Actor と Motor を直接書く移行期の経路へ落ちる。どちらも<b>コードのテストでは検出できない</b>
    /// （テストは自分で配線してしまうため）。手で組んだ Scene ではこの配線が静かに欠ける。
    /// そこで「正しい配線の Scene を機械で作れる」ようにし、その不変条件を
    /// <see cref="Phase4CompanionFieldValidator"/> が検査する。
    ///
    /// 構成：Environment(Floor+Wall×4) / Player(Prefab) / Inumaru(Prefab・隊列位置) /
    /// CameraRig(TopDownCameraFollow)+Main Camera(子) / Directional Light / SceneMode /
    /// SpawnCenter / Phase4Systems{ CombatSession, CompanionActivityContext(Session へ配線),
    /// EnemyTestField(+EnemyDebugToggle), CombatFeedback }。初期敵は 0 体で、敵は
    /// <see cref="EnemyTestFieldController"/> の Context Menu から必要な編成を出す（Phase3 と同じ手順）。
    ///
    /// 失敗方針：必要な Prefab（主人公・犬丸・敵 3 種）が欠ける場合は<b>Scene に一切触れず</b>失敗する。
    /// 壊れた Scene を保存しない。
    /// </summary>
    public static class Phase4CompanionFieldBuilder
    {
        /// <summary>既定の生成先。</summary>
        public const string DefaultScenePath = "Assets/_Project/Scenes/Tests/SCN_Phase4_CompanionField.unity";

        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";
        private const string MeleePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";
        private const string RangedPrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Ranged_Prototype.prefab";
        private const string ElitePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        /// <summary>主人公の初期位置（床の中央よりやや手前。敵の湧き位置と重ならない）。</summary>
        public static readonly Vector3 PlayerPosition = new Vector3(0f, 0f, -6f);

        /// <summary>敵を湧かせる中心（Phase3 と同じく原点）。</summary>
        public static readonly Vector3 SpawnCenterPosition = Vector3.zero;

        /// <summary>生成結果。</summary>
        public readonly struct BuildResult
        {
            /// <summary>成功したか。</summary>
            public bool Success { get; }

            /// <summary>生成先パス。</summary>
            public string ScenePath { get; }

            /// <summary>説明（失敗理由・構成の要約）。</summary>
            public string Message { get; }

            public BuildResult(bool success, string scenePath, string message)
            {
                Success = success;
                ScenePath = scenePath;
                Message = message;
            }
        }

        /// <summary>メニューから対話的に生成する（上書き確認あり）。</summary>
        [MenuItem("Momotaro/Phase 4/Generate Companion Field")]
        public static void GenerateInteractive()
        {
            if (File.Exists(DefaultScenePath))
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Phase 4 仲間検証 Scene 生成",
                    "既存の検証 Scene を上書きします:\n" + DefaultScenePath + "\n\n続行しますか？",
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

            var companion = Object.FindAnyObjectByType<CompanionActor>();
            if (companion != null)
            {
                Selection.activeGameObject = companion.gameObject;
                EditorGUIUtility.PingObject(companion.gameObject);
            }

            Debug.Log("[Phase4] 仲間検証 Scene を生成しました: " + DefaultScenePath + " — " + r.Message
                + " 敵は EnemyTestFieldController の Context Menu から出してください（初期は 0 体）。");
        }

        /// <summary>
        /// 内部 Builder（ダイアログ無し。テスト用に出力先を引数で受ける）。必要 Prefab が欠ければ Scene に一切触れず失敗を返す。
        /// 成功時は生成 Scene を保存し、開いたまま（Active・保存済み・非 Dirty）で返す。
        /// 呼び出し側は現在の作業 Scene を事前に保護すること。
        /// </summary>
        public static BuildResult Build(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath) || !outputPath.StartsWith("Assets/"))
            {
                return new BuildResult(false, outputPath, "出力先は Assets 配下である必要があります。");
            }

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var companionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Phase4CompanionBuilder.InumaruPrefabPath);
            var meleePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MeleePrefabPath);
            var rangedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RangedPrefabPath);
            var elitePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ElitePrefabPath);

            if (playerPrefab == null || meleePrefab == null || rangedPrefab == null || elitePrefab == null)
            {
                // ここまでで Scene には一切触れていない（壊れた Scene を残さない）。
                return new BuildResult(false, outputPath,
                    "必要な Prefab が見つかりません（主人公／近接／遠距離／強敵）。");
            }

            if (companionPrefab == null)
            {
                return new BuildResult(false, outputPath,
                    "犬丸 Prefab が見つかりません: " + Phase4CompanionBuilder.InumaruPrefabPath
                    + "\n先に「Momotaro / Phase 4 / Generate Inumaru Prefab」を実行してください。");
            }

            EnsureFolder(Path.GetDirectoryName(outputPath).Replace('\\', '/'));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Populate(playerPrefab, companionPrefab, meleePrefab, rangedPrefab, elitePrefab);
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

            return new BuildResult(true, outputPath,
                "Environment/Player/Inumaru/CameraRig+Main Camera/Light/SceneMode/SpawnCenter/"
                + "Phase4Systems(CombatSession+CompanionActivityContext+EnemyTestField+EnemyDebugToggle+CombatFeedback)、初期敵 0 体。");
        }

        private static void Populate(
            GameObject playerPrefab, GameObject companionPrefab,
            GameObject meleePrefab, GameObject rangedPrefab, GameObject elitePrefab)
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
            player.transform.position = PlayerPosition;
            var playerState = player.GetComponentInChildren<PlayerStateController>(true);

            // CameraRig（TopDownCameraFollow は自分の position を毎フレーム上書きするため、揺れは子カメラへ当てる形にしておく）。
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

            // SceneMode（既定で Exploration＝主人公を操作できる。活動 Context が読む正本でもある）。
            var sceneModeGo = new GameObject("SceneMode");
            sceneModeGo.AddComponent<GameplaySceneMode>();

            // SpawnCenter（敵を出す中心）。
            var spawnCenter = new GameObject("SpawnCenter");
            spawnCenter.transform.position = SpawnCenterPosition;

            // Phase4Systems。
            var systems = new GameObject("Phase4Systems");

            // 戦闘 Session。この Scene では Wave を回さないので状態は待機のままだが、
            // 活動 Context が「Encounter 中か」を読む先として必要（無いと Context は停止を返す）。
            var sessionGo = new GameObject("CombatSession");
            sessionGo.transform.SetParent(systems.transform, false);
            var session = sessionGo.AddComponent<CombatSessionController>();

            // 活動 Context（P4-FIX F05）。これが Scene に無いと、仲間は
            // CompanionActivityProvider の移行期フォールバックで動くことになり、Pause・会話の区別ができない。
            // Scene 側で必ず置くことを Validator が強制する（F01）。
            var activityGo = new GameObject("CompanionActivityContext");
            activityGo.transform.SetParent(systems.transform, false);
            activityGo.AddComponent<CompanionActivityContext>().Bind(session);

            // 敵の編成（Phase3 と同じ手順。初期 0 体で、Context Menu から出す）。
            var enemyFieldGo = new GameObject("EnemyTestField");
            enemyFieldGo.transform.SetParent(systems.transform, false);
            var enemyField = enemyFieldGo.AddComponent<EnemyTestFieldController>();
            AssignEnemyField(enemyField, meleePrefab, rangedPrefab, elitePrefab, spawnCenter.transform);

            var toggleGo = new GameObject("EnemyDebugToggle");
            toggleGo.transform.SetParent(systems.transform, false);
            toggleGo.AddComponent<EnemyDebugToggle>();

            // 被弾フィードバックの配信（P4-FIX F04）。仲間の被弾結果は
            // CompanionFeedbackRegistry 経由でここへ届く。置いておかないと仲間の被弾が何も出ない。
            var feedbackGo = new GameObject("CombatFeedback");
            feedbackGo.transform.SetParent(systems.transform, false);
            feedbackGo.AddComponent<CombatFeedbackDispatcher>();

            // 犬丸（隊列位置へ置き、主人公へ追従させる）。Prefab 側で調停役まで組んであるので、
            // ここで足すのは「誰について行くか」だけ。
            PlaceCompanion(companionPrefab, player.transform, playerState);
        }

        /// <summary>犬丸を隊列位置へ置き、追従・守護の相手を主人公に固定する。</summary>
        private static void PlaceCompanion(GameObject companionPrefab, Transform player, PlayerStateController playerState)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(companionPrefab);
            instance.name = "Inumaru";

            var actor = instance.GetComponent<CompanionActor>();
            CompanionFollowSettings settings = CompanionFollowSettings.From(actor != null ? actor.Data : null);
            instance.transform.position = FormationSlot.Resolve(
                player.position,
                playerState != null ? playerState.transform.forward : player.forward,
                actor != null ? actor.SlotIndex : 0,
                settings.Spacing);

            // 追従対象。Runtime の自動探索に頼らず Scene に焼く（Find* を使わない方針の一部でもある）。
            instance.GetComponent<CompanionFollowController>()?.Bind(player);

            // 守護の相手も同じ主人公に固定する。Runtime では追従対象から解決できるが、
            // Scene を読んだだけで「誰を庇うのか」が分かる状態にしておく。
            CompanionHitReceiver receiver = instance.GetComponent<CompanionHitReceiver>();
            instance.GetComponent<CompanionGuardianController>()?.Bind(actor, receiver, player);
        }

        private static void AssignEnemyField(
            EnemyTestFieldController controller, GameObject melee, GameObject ranged, GameObject elite, Transform spawnCenter)
        {
            var so = new SerializedObject(controller);
            SetRef(so, "_meleePrefab", melee);
            SetRef(so, "_rangedPrefab", ranged);
            SetRef(so, "_elitePrefab", elite);
            SetRef(so, "_spawnCenter", spawnCenter);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRef(SerializedObject so, string path, Object value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null)
            {
                p.objectReferenceValue = value;
            }
        }

        private static GameObject CreateBox(string name, Vector3 pos, Vector3 scale, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
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
