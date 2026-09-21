using System.Collections.Generic;
using Momotaro.Editor.Phase35;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// P4-08R：仲間試遊 Scene（<c>SCN_Phase4_CompanionTrial</c>）の統合受入 Validator。
    ///
    /// <b>3 つを重ねて検査する。</b>
    /// <list type="number">
    /// <item><description>Phase3.5 の試遊 Scene としての不変条件（<see cref="Phase35CombatTrialValidator"/>）。
    /// Wave・勝敗・Retry・HUD・徳の表示・フィードバック・VFX の配線。</description></item>
    /// <item><description>仲間まわりの不変条件（<see cref="Phase4CompanionSceneChecks"/>）。
    /// 活動 Context・調停役・追従対象・調査地点・指示。</description></item>
    /// <item><description>試遊としての操作（指示の入力が置かれ、相手が繋がっていること）。</description></item>
    /// </list>
    ///
    /// 1 と 2 を写経せずに呼ぶのが要点。どちらかを写せば、次に片方だけ直したとき静かに食い違う。
    /// </summary>
    public static class Phase4CompanionTrialValidator
    {
        /// <summary>仲間試遊 Scene の既定パス（メニュー実行時の対象一致チェックに用いる）。</summary>
        public const string TrialScenePath = Phase4CompanionTrialBuilder.DefaultScenePath;

        [MenuItem("Momotaro/Phase 4/Validate Companion Trial")]
        private static void ValidateActiveSceneMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            var errors = new List<string>();
            var warnings = new List<string>();

            if (scene.path != TrialScenePath)
            {
                warnings.Add("現在の Scene は仲間試遊 Scene ではありません（"
                    + (string.IsNullOrEmpty(scene.path) ? "無題" : scene.path)
                    + "）。" + TrialScenePath + " を開いて実行してください。");
            }

            Validate(scene, errors, warnings);

            string summary = "[Phase4] 仲間試遊 統合受入 Validator: "
                + (errors.Count == 0 ? "OK" : errors.Count + " 件のエラー")
                + (warnings.Count > 0 ? "（警告 " + warnings.Count + " 件）" : string.Empty);

            if (errors.Count > 0)
            {
                Debug.LogError(summary + "\n- " + string.Join("\n- ", errors)
                    + (warnings.Count > 0 ? "\n[警告]\n- " + string.Join("\n- ", warnings) : string.Empty));
            }
            else if (warnings.Count > 0)
            {
                Debug.LogWarning(summary + "\n[警告]\n- " + string.Join("\n- ", warnings));
            }
            else
            {
                Debug.Log(summary);
            }

            EditorUtility.DisplayDialog("Phase 4 仲間試遊 統合受入 Validator",
                summary + (errors.Count > 0 ? "\n\n最初のエラー:\n" + errors[0] : string.Empty)
                + "\n\n詳細は Console を参照してください。",
                "OK");
        }

        /// <summary>
        /// Scene を走査して不変条件を検査し、<paramref name="errors"/>／<paramref name="warnings"/> へ追記する
        /// （AssetDatabase 非依存・純走査）。
        /// </summary>
        public static void Validate(Scene scene, List<string> errors, List<string> warnings)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                errors.Add("Scene が無効／未読込です。");
                return;
            }

            // 1. Phase3.5 の試遊 Scene として成立しているか。
            Phase35CombatTrialValidator.Validate(scene, errors, warnings);

            // 2. 仲間まわり（検証フィールドと共通）。
            Phase4CompanionSceneChecks.Validate(scene, errors, warnings);

            // 3. 試遊の開始経路（P4-08R。v1.0 §13.1）：起動直後は自由探索、明示開始で 4 Wave。
            ValidateTrialStage(scene, errors, warnings);

            // 4. 出荷 Scene は Build Settings に登録されていること（Retry の再読込・PlayMode の実 Scene 検証が依存する）。
            if (scene.path == TrialScenePath && !Phase4CompanionTrialBuilder.IsRegisteredInBuildSettings(scene.path))
            {
                errors.Add("試遊 Scene が Build Settings に登録されていません（Retry で再読込できず、PlayMode の実 Scene 検証も走りません）。"
                    + "Builder（Generate Companion Trial）を実行すると登録されます。");
            }
        }

        /// <summary>
        /// 明示開始の配線を検査する。WaveRunner の自動開始が切れていて、試遊段階が Wave と探索の調停役に繋がり、
        /// 活動 Context がその段階を読んでいること。どれか 1 つ欠けると「Play した瞬間に敵が湧く」か
        /// 「開始操作をしても何も起きない」か「自由探索なのに探索を受け付けない」になる。
        /// </summary>
        private static void ValidateTrialStage(Scene scene, List<string> errors, List<string> warnings)
        {
            List<WaveRunner> waves = Phase4CompanionSceneChecks.Components<WaveRunner>(scene);
            List<TrialStageController> stages = Phase4CompanionSceneChecks.Components<TrialStageController>(scene);
            List<CompanionActivityContext> contexts = Phase4CompanionSceneChecks.Components<CompanionActivityContext>(scene);

            if (stages.Count != 1)
            {
                errors.Add("試遊段階（TrialStageController）は 1 つであるべきですが " + stages.Count + " です"
                    + (stages.Count > 1 ? "（重複）" : "（欠落）") + "。自由探索から戦闘を明示開始できません。");
                return;
            }

            TrialStageController stage = stages[0];
            if (stage.Waves == null)
            {
                errors.Add("TrialStageController：Wave（WaveRunner）が配線されていません（開始操作をしても敵が湧きません）。");
            }

            if (stage.Investigation == null)
            {
                errors.Add("TrialStageController：探索の調停役が配線されていません（戦闘開始で探索を解放できません）。");
            }

            foreach (WaveRunner runner in waves)
            {
                var so = new SerializedObject(runner);
                SerializedProperty auto = so.FindProperty("_autoStart");
                if (auto != null && auto.boolValue)
                {
                    errors.Add(runner.gameObject.name + "：WaveRunner の自動開始が有効です（P4 試遊 Scene は明示開始。Play した瞬間に敵が湧きます）。");
                }
            }

            if (contexts.Count == 1 && !ReferenceEquals(contexts[0].Stage, stage))
            {
                errors.Add("CompanionActivityContext：試遊段階が配線されていません（開始前の Preparing が戦闘扱いになり、自由探索で探索を受け付けません）。");
            }

            // 明示開始の入力（P4-07B／08R）。無いと Play しても戦闘へ進めない。
            List<TrialCombatStartInput> starts = Phase4CompanionSceneChecks.Components<TrialCombatStartInput>(scene);
            if (starts.Count != 1)
            {
                errors.Add("戦闘開始の入力（TrialCombatStartInput）は 1 つであるべきですが " + starts.Count + " です"
                    + (starts.Count > 1 ? "（重複）" : "（欠落。Enter／Start で戦闘を始められません）") + "。");
            }
            else if (!ReferenceEquals(starts[0].Stage, stage))
            {
                errors.Add("TrialCombatStartInput：この Scene の試遊段階が配線されていません（参照不一致）。");
            }
        }
    }
}
