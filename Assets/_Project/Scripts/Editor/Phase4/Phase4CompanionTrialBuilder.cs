using System.Collections.Generic;
using System.IO;
using Momotaro.Editor.Phase35;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// P4-08R：仲間を入れた<b>試遊</b>環境（<c>SCN_Phase4_CompanionTrial</c>）を決定的に生成する。
    ///
    /// <b>Phase3.5 の試遊 Scene を作り直さず、その上に載せる。</b>
    /// <see cref="Phase35CombatTrialBuilder"/> がすでに Wave・勝敗・Retry・HUD・フィードバック・VFX・SE を
    /// 組み上げており、それらは Phase3.5 の受入で通っている。写経すれば必ず片方が古くなるので、
    /// 3.5 の Builder をそのまま呼び、<b>差分（仲間の層）だけ</b>を足して保存し直す。
    ///
    /// 足すのは 4 つ。
    /// <list type="number">
    /// <item><description>犬丸（Prefab。隊列位置・追従と守護の相手を焼く）</description></item>
    /// <item><description>活動 Context（試遊 Scene の <see cref="CombatSessionController"/> へ配線。
    /// これが無いと Pause・会話・Wave 幕間の区別ができない）</description></item>
    /// <item><description>調査地点（P4-07A を実機で試せるように）</description></item>
    /// <item><description>指示の入力（P4-07B。<see cref="CompanionOrderInput"/>。試遊で待機／追従を切り替える）</description></item>
    /// </list>
    ///
    /// <b>検証フィールド（F01）との違い。</b>あちらは仲間の駆動を落ち着いて確かめるための場で、
    /// 敵は Context Menu から任意に出す。こちらは<b>通しで遊ぶ</b>ための場で、Wave が敵を出し、
    /// 勝敗と Retry があり、徳が HUD に出る。P4 の終了条件はこちらで確認する。
    /// </summary>
    public static class Phase4CompanionTrialBuilder
    {
        /// <summary>既定の生成先。</summary>
        public const string DefaultScenePath = "Assets/_Project/Scenes/Tests/SCN_Phase4_CompanionTrial.unity";

        /// <summary>
        /// 調査地点の位置（P4-07A）。3.5 の試遊 Scene は主人公が (0,0,-6)、敵の湧きが +Z 側なので、
        /// 地点は主人公の周りかつ敵の湧き位置から離れた側へ置く。
        /// 犬丸の紐（既定 6m）の内側に収める（外だと探索は有効なのに一度も動かず、壊れて見える）。
        /// </summary>
        public static readonly Vector3[] InvestigationPointPositions =
        {
            new Vector3(-3.5f, 0f, -8f),
            new Vector3(3.5f, 0f, -8f),
            new Vector3(0f, 0f, -10f),
        };

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
        [MenuItem("Momotaro/Phase 4/Generate Companion Trial")]
        public static void GenerateInteractive()
        {
            if (File.Exists(DefaultScenePath))
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Phase 4 仲間試遊 Scene 生成",
                    "既存の試遊 Scene を上書きします:\n" + DefaultScenePath + "\n\n続行しますか？",
                    "上書き生成", "キャンセル");
                if (!ok)
                {
                    return;
                }
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
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

            Debug.Log("[Phase4] 仲間試遊 Scene を生成しました: " + DefaultScenePath + " — " + r.Message);
        }

        /// <summary>
        /// 内部 Builder（ダイアログ無し。テスト用に出力先を引数で受ける）。
        /// 3.5 の Builder が失敗したらそのまま失敗を返す（Scene は 3.5 側が後始末する）。
        /// </summary>
        public static BuildResult Build(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath) || !outputPath.StartsWith("Assets/"))
            {
                return new BuildResult(false, outputPath, "出力先は Assets 配下である必要があります。");
            }

            var companionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Phase4CompanionBuilder.InumaruPrefabPath);
            if (companionPrefab == null)
            {
                // 3.5 の Builder を呼ぶ前に弾く。呼んでしまうと、仲間の入っていない Scene が
                // 「試遊 Scene」の名前で保存され、あとから中身を疑うことになる。
                return new BuildResult(false, outputPath,
                    "犬丸 Prefab が見つかりません: " + Phase4CompanionBuilder.InumaruPrefabPath
                    + "\n先に「Momotaro / Phase 4 / Generate Inumaru Prefab」を実行してください。");
            }

            Phase35CombatTrialBuilder.BuildResult trial = Phase35CombatTrialBuilder.Build(outputPath);
            if (!trial.Success)
            {
                return new BuildResult(false, outputPath, "Phase3.5 試遊 Scene の生成に失敗しました: " + trial.Message);
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != outputPath)
            {
                return new BuildResult(false, outputPath, "生成 Scene が開いていません（" + scene.path + "）。");
            }

            try
            {
                AddCompanionLayer(scene, companionPrefab);
            }
            catch (System.Exception e)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return new BuildResult(false, outputPath, "仲間の層を足すところで例外: " + e.Message);
            }

            if (!EditorSceneManager.SaveScene(scene, outputPath))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return new BuildResult(false, outputPath, "Scene の保存に失敗しました。");
            }

            AssetDatabase.Refresh();

            return new BuildResult(true, outputPath,
                "Phase3.5 試遊 Scene ＋ Inumaru／CompanionActivityContext／InvestigationPoints(×"
                + InvestigationPointPositions.Length + ")／CompanionOrderInput。"
                + "Play すると Wave1 から始まり、犬丸が同行します。");
        }

        /// <summary>3.5 の試遊 Scene へ仲間の層を足す（保存はしない）。</summary>
        private static void AddCompanionLayer(Scene scene, GameObject companionPrefab)
        {
            PlayerStateController player = FindSingle<PlayerStateController>(scene, "主人公（PlayerStateController）");
            CombatSessionController session = FindSingle<CombatSessionController>(scene, "戦闘 Session（CombatSessionController）");

            var layer = new GameObject("Phase4CompanionLayer");

            // 活動 Context：Pause・会話・Wave 幕間の区別の正本。ここが抜けると仲間は
            // 移行期フォールバック（常に自由行動）で動き、会話中に歩き回る。
            var activityGo = new GameObject("CompanionActivityContext");
            activityGo.transform.SetParent(layer.transform, false);
            activityGo.AddComponent<CompanionActivityContext>().Bind(session);

            // 調査地点（P4-07A）。
            var points = new GameObject("InvestigationPoints");
            points.transform.SetParent(layer.transform, false);
            for (int i = 0; i < InvestigationPointPositions.Length; i++)
            {
                var pointGo = new GameObject("InvestigationPoint_" + i);
                pointGo.transform.SetParent(points.transform, false);
                pointGo.transform.position = InvestigationPointPositions[i];
                pointGo.AddComponent<CompanionInvestigationPoint>();
            }

            // 犬丸。
            CompanionOrders orders = PlaceCompanion(companionPrefab, player.transform);

            // 指示の入力（P4-07B）。相手は Scene 構築時に注入する（Find* で探し回らない）。
            var orderInputGo = new GameObject("CompanionOrderInput");
            orderInputGo.transform.SetParent(layer.transform, false);
            var orderInput = orderInputGo.AddComponent<CompanionOrderInput>();
            orderInput.Bind(orders);
        }

        /// <summary>犬丸を隊列位置へ置き、追従・守護の相手を主人公へ固定する。指示コンポーネントを返す。</summary>
        private static CompanionOrders PlaceCompanion(GameObject companionPrefab, Transform player)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(companionPrefab);
            instance.name = "Inumaru";

            var actor = instance.GetComponent<CompanionActor>();
            CompanionFollowSettings settings = CompanionFollowSettings.From(actor != null ? actor.Data : null);
            instance.transform.position = FormationSlot.Resolve(
                player.position, player.forward, actor != null ? actor.SlotIndex : 0, settings.Spacing);

            instance.GetComponent<CompanionFollowController>()?.Bind(player);

            CompanionHitReceiver receiver = instance.GetComponent<CompanionHitReceiver>();
            instance.GetComponent<CompanionGuardianController>()?.Bind(actor, receiver, player);

            CompanionOrders orders = instance.GetComponent<CompanionOrders>();
            if (orders == null)
            {
                throw new System.InvalidOperationException(
                    "犬丸 Prefab に CompanionOrders がありません（Prefab を再生成してください）。");
            }

            return orders;
        }

        private static T FindSingle<T>(Scene scene, string label) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<T>(true));
            }

            if (found.Count != 1)
            {
                throw new System.InvalidOperationException(
                    label + " は 1 つであるべきですが " + found.Count + " です（3.5 の Builder の出力が想定と違います）。");
            }

            return found[0];
        }
    }
}
