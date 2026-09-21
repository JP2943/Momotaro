using System.Collections.Generic;
using System.IO;
using Momotaro.Editor.Phase35;
using Momotaro.Core.Identification;
using Momotaro.Data.Exploration;
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
    /// <item><description>探索の層（P4-07A／P4-08R。地点・加入供給元・記録・調停役・試遊段階。起動直後は自由探索）</description></item>
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

        /// <summary>調査地点の数（標準配置：正常 ×2、壁 ×1、未加入 ×1。v1.0 §13.1）。</summary>
        public static int InvestigationPointCount =>
            Phase4InvestigationLayerBuilder.StandardPoints(default).Length;

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

            // 出荷パスだけ Build Settings へ登録する（Retry の Scene 再読込と PlayMode の実 Scene 検証に必要。
            // テストの一時パスは登録しない＝本番設定を汚さない）。
            string registered = string.Empty;
            if (outputPath == DefaultScenePath && EnsureRegisteredInBuildSettings(outputPath))
            {
                AssetDatabase.SaveAssets(); // ProjectSettings/EditorBuildSettings.asset へ書き出す（コミット対象）。
                registered = " Build Settings へ登録しました。";
            }

            return new BuildResult(true, outputPath,
                "Phase3.5 試遊 Scene ＋ Inumaru／CompanionActivityContext／Investigation(地点×"
                + InvestigationPointCount + "・加入供給元・記録・調停役・試遊段階・入力仲介・マーカー・短文UI・開始入力)。"
                + "Play すると敵の居ない自由探索から始まり、E／南ボタンで調査、Enter／Start で Wave1 が起動します。" + registered);
        }

        /// <summary>Scene が Build Settings に有効な状態で登録されているか。</summary>
        public static bool IsRegisteredInBuildSettings(string scenePath)
        {
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path == scenePath)
                {
                    return s.enabled;
                }
            }

            return false;
        }

        /// <summary>Scene を Build Settings に登録（無効なら有効化）する。変更したら true。</summary>
        public static bool EnsureRegisteredInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == scenePath)
                {
                    if (scenes[i].enabled)
                    {
                        return false;
                    }

                    scenes[i] = new EditorBuildSettingsScene(scenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return true;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return true;
        }

        /// <summary>3.5 の試遊 Scene へ仲間の層を足す（保存はしない）。</summary>
        private static void AddCompanionLayer(Scene scene, GameObject companionPrefab)
        {
            PlayerStateController player = FindSingle<PlayerStateController>(scene, "主人公（PlayerStateController）");
            CombatSessionController session = FindSingle<CombatSessionController>(scene, "戦闘 Session（CombatSessionController）");
            WaveRunner waves = FindSingle<WaveRunner>(scene, "Wave（WaveRunner）");

            InvestigationSettingsData settings = Phase4InvestigationLayerBuilder.LoadSettings();
            if (settings == null)
            {
                throw new System.InvalidOperationException(
                    "探索設定 Data が見つかりません: " + Phase4InvestigationLayerBuilder.SettingsAssetPath);
            }

            var layer = new GameObject("Phase4CompanionLayer");

            // 活動 Context：Pause・会話・Wave 幕間の区別の正本。無いと仲間は停止する（供給元が無ければ止まる）。
            var activityGo = new GameObject("CompanionActivityContext");
            activityGo.transform.SetParent(layer.transform, false);
            var context = activityGo.AddComponent<CompanionActivityContext>();
            context.Bind(session);

            // 犬丸。
            CompanionActor inumaru = PlaceCompanion(companionPrefab, player.transform);
            StableId inumaruId = inumaru != null && inumaru.Data != null ? inumaru.Data.Id : default;

            // P4 側だけ明示開始にする（P3.5 の既定の自動開始は変えない。v1.0 §13.1）。
            Phase4InvestigationLayerBuilder.DisableAutoStart(waves);

            // 探索の層（地点・加入供給元・記録・調停役・試遊段階）。
            Phase4InvestigationLayerBuilder.Build(
                layer.transform, player, new[] { inumaru }, new[] { inumaruId },
                Phase4InvestigationLayerBuilder.StandardPoints(inumaruId), settings, context, waves);
        }

        /// <summary>犬丸を隊列位置へ置き、追従・守護の相手を主人公へ固定する。置いた本体を返す。</summary>
        private static CompanionActor PlaceCompanion(GameObject companionPrefab, Transform player)
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

            if (instance.GetComponent<CompanionInvestigationController>() == null)
            {
                throw new System.InvalidOperationException(
                    "犬丸 Prefab に CompanionInvestigationController がありません（Prefab を再生成してください）。");
            }

            return actor;
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
