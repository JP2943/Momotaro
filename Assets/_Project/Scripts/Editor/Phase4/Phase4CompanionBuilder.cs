using System.Collections.Generic;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Characters;
using Momotaro.Presentation.Companion;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// P4-02：犬丸（仮素材）の Data と Prefab を機械生成する。手で組んだ Prefab は配線漏れが起きやすく、再現もできないため、
    /// 試遊 Scene（<c>Phase35CombatTrialBuilder</c>）と同じく「生成し直せば必ず同じ構成に戻る」形にする。
    ///
    /// 構成は既存の敵 Prefab に合わせる：ルートに Gameplay（Actor／Motor／追従）、子 <c>VisualRoot</c> に Billboard、その子に
    /// 本体スプライト。方向インジケータだけはルート直下に置く（Billboard の回転を受けず、足元へ寝かせて 4 方向を示すため）。
    ///
    /// 仮素材は <c>/Placeholder/</c> 配下を参照する。正式素材の統合（P10a）では、この参照が残っていないことを
    /// 素材参照専用 Validator が検査する。
    /// </summary>
    public static class Phase4CompanionBuilder
    {
        /// <summary>犬丸 Prefab の既定の出力先。</summary>
        public const string InumaruPrefabPath = "Assets/_Project/Prefabs/Companions/PF_Companion_Inumaru.prefab";

        /// <summary>犬丸 Data の既定の出力先。</summary>
        public const string InumaruDataPath = "Assets/_Project/Data/Companions/SO_Companion_Inumaru.asset";

        /// <summary>本体シルエット（仮素材）。</summary>
        public const string BodySpritePath =
            "Assets/_Project/Art/Characters/Companions/Inumaru/Placeholder/Sprites/Inumaru_body.png";

        /// <summary>方向インジケータ（仮素材。猿・雉と共用）。</summary>
        public const string ArrowSpritePath =
            "Assets/_Project/Art/Characters/Companions/Shared/Placeholder/Sprites/direction_arrow.png";

        /// <summary>生成結果。</summary>
        public readonly struct BuildResult
        {
            /// <summary>成功したか。</summary>
            public bool Success { get; }

            /// <summary>結果メッセージ（失敗理由・出力先）。</summary>
            public string Message { get; }

            /// <summary>生成された Prefab（失敗時 null）。</summary>
            public GameObject Prefab { get; }

            public BuildResult(bool success, string message, GameObject prefab)
            {
                Success = success;
                Message = message;
                Prefab = prefab;
            }
        }

        [MenuItem("Momotaro/Phase 4/Generate Inumaru Prefab")]
        private static void GenerateInteractive()
        {
            BuildResult result = Build(InumaruPrefabPath, InumaruDataPath);
            if (result.Success)
            {
                Debug.Log("[Phase4] 犬丸 Prefab を生成しました: " + result.Message, result.Prefab);
                Selection.activeObject = result.Prefab;
                EditorGUIUtility.PingObject(result.Prefab);
            }
            else
            {
                Debug.LogError("[Phase4] 犬丸 Prefab の生成に失敗しました: " + result.Message);
            }

            EditorUtility.DisplayDialog("Phase 4 犬丸 Prefab 生成",
                (result.Success ? "生成しました。\n\n" : "失敗しました。\n\n") + result.Message, "OK");
        }

        /// <summary>
        /// 犬丸の Data（無ければ新規作成）と Prefab を生成する。既存の Prefab は上書きする（再生成で必ず同じ構成へ戻る）。
        /// 仮素材が見つからない場合は失敗として報告する（無言で素材無しの Prefab を作らない）。
        /// </summary>
        public static BuildResult Build(string prefabPath, string dataPath)
        {
            var errors = new List<string>();

            var body = AssetDatabase.LoadAssetAtPath<Sprite>(BodySpritePath);
            if (body == null)
            {
                errors.Add("本体の仮素材が見つかりません: " + BodySpritePath);
            }

            var arrow = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
            if (arrow == null)
            {
                errors.Add("方向インジケータの仮素材が見つかりません: " + ArrowSpritePath);
            }

            if (errors.Count > 0)
            {
                return new BuildResult(false, string.Join("\n", errors), null);
            }

            CompanionData data = EnsureData(dataPath);
            GameObject root = BuildHierarchy(data, body, arrow);

            EnsureFolderFor(prefabPath);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
            Object.DestroyImmediate(root);

            if (!saved || prefab == null)
            {
                return new BuildResult(false, "Prefab の保存に失敗しました: " + prefabPath, null);
            }

            AssetDatabase.SaveAssets();
            return new BuildResult(true, prefabPath + "\n" + dataPath, prefab);
        }

        /// <summary>Data を取得し、無ければ既定値で作成する（既存があれば内容は変更しない）。</summary>
        public static CompanionData EnsureData(string dataPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<CompanionData>(dataPath);
            if (existing != null)
            {
                return existing;
            }

            var data = ScriptableObject.CreateInstance<CompanionData>();
            var so = new SerializedObject(data);
            SetString(so, "_id._value", "companion_inumaru");
            SetString(so, "_displayName", "犬丸");
            SetEnum(so, "_role", (int)CompanionRole.Dog);
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolderFor(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>Prefab の階層を組み立てる（保存はしない）。</summary>
        private static GameObject BuildHierarchy(CompanionData data, Sprite body, Sprite arrow)
        {
            var root = new GameObject("PF_Companion_Inumaru");

            // --- 物理（壁でだけ止まる。レイヤーは CompanionActor が Awake で Ally へ設定する） ---
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = 1.2f;
            capsule.center = new Vector3(0f, 0.6f, 0f);

            // --- Gameplay ---
            var actor = root.AddComponent<CompanionActor>();
            var actorSo = new SerializedObject(actor);
            SetRef(actorSo, "_data", data);
            actorSo.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<CompanionMotor>();
            root.AddComponent<CompanionFollowController>();

            // 敵の認識・ヘイト候補として登録する（敵 AI は書き換えない。P4-03）。
            root.AddComponent<CompanionThreatBinder>().Bind(actor);

            // 索敵（誰を狙うか）。近づく・攻撃するのは後続タスク。
            root.AddComponent<CompanionTargetTracker>().Bind(actor);

            // --- 表示：本体は Billboard 配下（敵・主人公と同じ構成） ---
            var visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);
            visualRoot.AddComponent<CameraFacingBillboard>();

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(visualRoot.transform, false);
            var bodyRenderer = spriteGo.AddComponent<SpriteRenderer>();
            bodyRenderer.sprite = body;

            // --- 表示：方向インジケータはルート直下（Billboard の回転を受けず、足元へ寝かせる） ---
            var arrowGo = new GameObject("DirectionArrow");
            arrowGo.transform.SetParent(root.transform, false);
            var arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
            arrowRenderer.sprite = arrow;
            arrowRenderer.color = new Color(0.55f, 0.95f, 1f, 0.9f);

            var presenter = root.AddComponent<CompanionPlaceholderPresenter>();
            presenter.Bind(actor, bodyRenderer, arrowRenderer);

            return root;
        }

        [MenuItem("Momotaro/Phase 4/Add Inumaru To Open Scene")]
        private static void AddToOpenScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InumaruPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Phase 4 犬丸",
                    "犬丸 Prefab がありません。先に「Momotaro / Phase 4 / Generate Inumaru Prefab」を実行してください。", "OK");
                return;
            }

            var player = Object.FindFirstObjectByType<PlayerStateController>();
            if (player == null)
            {
                EditorUtility.DisplayDialog("Phase 4 犬丸",
                    "開いている Scene に主人公（PlayerStateController）が見つかりません。試遊 Scene を開いて実行してください。", "OK");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "Inumaru";

            var actor = instance.GetComponent<CompanionActor>();
            CompanionFollowSettings settings = CompanionFollowSettings.From(actor != null ? actor.Data : null);
            instance.transform.position = FormationSlot.Resolve(
                player.transform.position, player.transform.forward, actor != null ? actor.SlotIndex : 0, settings.Spacing);

            instance.GetComponent<CompanionFollowController>()?.Bind(player.transform);

            Undo.RegisterCreatedObjectUndo(instance, "Add Inumaru");
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            Debug.Log("[Phase4] 開いている Scene へ犬丸を追加し、主人公へ追従させました（Scene の保存は手動）。", instance);
        }

        // ---- ヘルパ ----

        private static void SetRef(SerializedObject so, string path, Object value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null)
            {
                p.objectReferenceValue = value;
            }
        }

        private static void SetString(SerializedObject so, string path, string value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null)
            {
                p.stringValue = value;
            }
        }

        private static void SetEnum(SerializedObject so, string path, int value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null)
            {
                p.enumValueIndex = value;
            }
        }

        /// <summary>指定アセットパスの親フォルダを（必要なら再帰的に）作成する。</summary>
        private static void EnsureFolderFor(string assetPath)
        {
            int slash = assetPath.LastIndexOf('/');
            if (slash <= 0)
            {
                return;
            }

            string folder = assetPath.Substring(0, slash);
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
