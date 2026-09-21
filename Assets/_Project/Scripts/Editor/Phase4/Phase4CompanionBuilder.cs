using System.Collections.Generic;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Characters;
using Momotaro.Presentation.Companion;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// P4-02／P4-03：犬丸（仮素材）の Data と Prefab を機械生成する。手で組んだ Prefab は配線漏れが起きやすく、再現もできないため、
    /// 試遊 Scene（<c>Phase35CombatTrialBuilder</c>）と同じく「生成し直せば必ず同じ構成に戻る」形にする。
    ///
    /// 構成は既存の敵 Prefab に合わせる：ルートに Gameplay（Actor／Motor／追従／索敵／戦闘）、子 <c>VisualRoot</c> に Billboard、
    /// その子に本体スプライト。方向インジケータだけはルート直下に置く（Billboard の回転を受けず、足元へ寝かせて 4 方向を示すため）。
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

        /// <summary>犬丸の通常攻撃 Data の既定の出力先（主人公・敵と共通の <see cref="AttackData"/>）。</summary>
        public const string InumaruAttackDataPath = "Assets/_Project/Data/Companions/SO_CompanionAttack_Inumaru.asset";

        /// <summary>本体シルエット（仮素材）。</summary>
        public const string BodySpritePath =
            "Assets/_Project/Art/Characters/Companions/Inumaru/Placeholder/Sprites/Inumaru_body.png";

        /// <summary>方向インジケータ（仮素材。猿・雉と共用）。</summary>
        public const string ArrowSpritePath =
            "Assets/_Project/Art/Characters/Companions/Shared/Placeholder/Sprites/direction_arrow.png";

        /// <summary>
        /// 攻撃力が未設定（0）の仲間 Data へ入れる既定値（P4-03）。主人公=100 に対し犬丸は 60。
        /// 攻撃力 0 のままだと、当たっても HP が 1 も減らず（＝獲得ヘイトも増えず）「攻撃しているのに何も起きない」ため、
        /// 0 は「未設定」とみなして補う。手で 0 以外へ調整した値は上書きしない。
        /// </summary>
        public const float DefaultAttackPower = 60f;

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
            BuildResult result = Build(InumaruPrefabPath, InumaruDataPath, InumaruAttackDataPath);
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
        /// 通常攻撃 Data も同様に用意し、Data 側が未配線・攻撃力 0 のときだけ補う（手で調整した値は保つ）。
        /// 仮素材が見つからない場合は失敗として報告する（無言で素材無しの Prefab を作らない）。
        ///
        /// <b>3 つのパスはすべて呼び出し側が明示する。</b><paramref name="attackPath"/> に既定値を持たせていたときは、
        /// 一時パスへ生成しているつもりのテストが<b>本番の攻撃 Data を触っていた</b>（生成・保存まで走る）。
        /// 出力先を省略できると、省略した側は自分がどこへ書いているか気付けない。
        /// </summary>
        /// <param name="prefabPath">Prefab の出力先。</param>
        /// <param name="dataPath">仲間 Data の出力先。</param>
        /// <param name="attackPath">通常攻撃 Data の出力先。テストは必ず一時パスを渡すこと。</param>
        public static BuildResult Build(string prefabPath, string dataPath, string attackPath)
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
            AttackData attack = EnsureAttack(attackPath);
            EnsureCombatWiring(data, attack);

            GameObject root = BuildHierarchy(data, body, arrow);

            EnsureFolderFor(prefabPath);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
            Object.DestroyImmediate(root);

            if (!saved || prefab == null)
            {
                return new BuildResult(false, "Prefab の保存に失敗しました: " + prefabPath, null);
            }

            AssetDatabase.SaveAssets();
            return new BuildResult(true, prefabPath + "\n" + dataPath + "\n" + attackPath, prefab);
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

        /// <summary>
        /// 通常攻撃 Data を取得し、無ければ犬丸の既定値で作成する（既存があれば内容は変更しない）。
        /// 値は仮の試作値で、調整は Inspector 側で行う（数値の正本は Data。コードへ直書きしない）。
        /// </summary>
        public static AttackData EnsureAttack(string attackPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AttackData>(attackPath);
            if (existing != null)
            {
                return existing;
            }

            var attack = ScriptableObject.CreateInstance<AttackData>();
            var so = new SerializedObject(attack);
            SetString(so, "_id._value", "companion_inumaru_attack");
            SetString(so, "_displayName", "犬丸 通常攻撃");
            SetFloat(so, "_cooldownSeconds", 1.2f);
            SetFloat(so, "_useRange", 1.6f);
            SetFloat(so, "_useAngle", 70f);
            SetFloat(so, "_startupSeconds", 0.25f);
            SetFloat(so, "_activeSeconds", 0.12f);
            SetFloat(so, "_recoverySeconds", 0.35f);
            SetFloat(so, "_hpMultiplier", 0.8f);
            SetFloat(so, "_poiseDamage", 6f);
            SetFloat(so, "_flinchPower", 10f);
            SetFloat(so, "_guardStaminaCost", 8f);
            SetFloat(so, "_justGuardPoiseDamage", 15f);
            SetFloat(so, "_hitbackDistance", 0.1f);
            SetFloat(so, "_hitbackSeconds", 0.1f);
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolderFor(attackPath);
            AssetDatabase.CreateAsset(attack, attackPath);
            AssetDatabase.SaveAssets();
            return attack;
        }

        /// <summary>
        /// 戦闘に必要な値のうち<b>未設定のものだけ</b>を補う。通常攻撃が未配線なら配線し、攻撃力が 0 なら
        /// <see cref="DefaultAttackPower"/> を入れる。既に値がある項目には触れない（手で調整した値を失わない）。
        /// </summary>
        public static void EnsureCombatWiring(CompanionData data, AttackData attack)
        {
            if (data == null)
            {
                return;
            }

            var so = new SerializedObject(data);
            bool changed = false;

            SerializedProperty basicAttack = so.FindProperty("_basicAttack");
            if (attack != null && basicAttack != null && basicAttack.objectReferenceValue == null)
            {
                basicAttack.objectReferenceValue = attack;
                changed = true;
            }

            SerializedProperty attackPower = so.FindProperty("_attackPower");
            if (attackPower != null && attackPower.floatValue <= 0f)
            {
                attackPower.floatValue = DefaultAttackPower;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
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

            var motor = root.AddComponent<CompanionMotor>();

            // 移動と向きの唯一の書き手（P4-FIX F02a）。Motor の RequireComponent で自動的に付くが、
            // 付いたことに依存せず明示的に取得して配線する（生成結果を読めば構成が分かるようにするため）。
            CompanionMovementArbiter arbiter = root.GetComponent<CompanionMovementArbiter>()
                ?? root.AddComponent<CompanionMovementArbiter>();
            arbiter.Bind(motor, actor);
            root.AddComponent<CompanionFollowController>();

            // 敵の認識・ヘイト候補として登録する（敵 AI は書き換えない。P4-03）。
            root.AddComponent<CompanionThreatBinder>().Bind(actor);

            // 索敵（誰を狙うか）。
            var tracker = root.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);

            // 戦闘（近づく・通常攻撃を出す）。戦闘中は追従が Motor を譲る。
            root.AddComponent<CompanionCombatController>().Bind(actor, motor, tracker);

            // 被弾（IDamageable）。敵の攻撃判定はここを見つけて命中を渡す（P4-04）。
            CompanionHitReceiver receiver = root.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            // 被弾結果を演出へ繋ぐ登録役（P4-FIX F04）。Prefab に同居させることで、
            // 動的に湧いた仲間でも有効化した瞬間から点滅・SE が出る（演出側が探し回らずに済む）。
            root.AddComponent<CompanionFeedbackBinder>().Bind(receiver);

            // ガード・回避の判断（観測した危険に反応する。P4-04b）。
            root.AddComponent<CompanionDefenseController>().Bind(actor);

            // 守護（かばう）。主人公への命中のうち防げなかったものを肩代わりする（P4-05）。
            root.AddComponent<CompanionGuardianController>().Bind(actor);

            // 探索（P4-07A）。自分では地点を探さず、主人公の Interact から調停役が渡す依頼を 1 件だけ実行する。
            CompanionInvestigationController investigation = root.AddComponent<CompanionInvestigationController>();
            investigation.Bind(actor);

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

            // --- 表示：探索の表示代理（P4-07B。v1.0 §5.2） ---
            // 同じ仮素材をもう 1 組、戦闘 Actor を持たない SpriteRenderer として持つ。Down／退場中の犬丸が調べに行く姿と、
            // 進行段の文字（移動／調査中／帰還）をここで描く。通常は非表示で、駆動が代理を出している間だけ描く。
            var proxyAnchor = new GameObject("InvestigationProxy");
            proxyAnchor.transform.SetParent(root.transform, false);

            var proxyVisual = new GameObject("ProxyVisual");
            proxyVisual.transform.SetParent(proxyAnchor.transform, false);
            proxyVisual.AddComponent<CameraFacingBillboard>();

            var proxyBodyGo = new GameObject("ProxyBody");
            proxyBodyGo.transform.SetParent(proxyVisual.transform, false);
            var proxyBody = proxyBodyGo.AddComponent<SpriteRenderer>();
            proxyBody.sprite = body;
            proxyBody.enabled = false;

            var proxyArrowGo = new GameObject("ProxyArrow");
            proxyArrowGo.transform.SetParent(root.transform, false);
            var proxyArrow = proxyArrowGo.AddComponent<SpriteRenderer>();
            proxyArrow.sprite = arrow;
            proxyArrow.color = new Color(0.55f, 0.95f, 1f, 0.9f);
            proxyArrow.enabled = false;

            TextMesh label = Phase4PlaceholderText.CreateLabel(
                "InvestigationLabel", root.transform, new Vector3(0f, 1.45f, 0f), new Color(0.9f, 1f, 0.9f, 1f));
            label.gameObject.SetActive(false);

            var proxyPresenter = root.AddComponent<CompanionInvestigationProxyPresenter>();
            proxyPresenter.Bind(investigation, presenter, proxyAnchor.transform, proxyBody, proxyArrow, label);

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

        private static void SetFloat(SerializedObject so, string path, float value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null)
            {
                p.floatValue = value;
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
