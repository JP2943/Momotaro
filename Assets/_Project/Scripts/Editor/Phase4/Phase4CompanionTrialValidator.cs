using System.Collections.Generic;
using Momotaro.Editor.Phase35;
using Momotaro.Gameplay.Companion;
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

            // 3. 試遊としての操作。
            ValidateOrderInput(scene, errors, warnings);
        }

        /// <summary>
        /// 指示の入力（P4-07B）が置かれ、相手が繋がっているかを検査する。
        ///
        /// 置いてあるだけでは足りない。相手が 0 体の入力はキーを押しても何も起きず、
        /// 「指示が壊れている」のか「繋ぎ忘れ」なのかが実機では区別できない。
        /// </summary>
        private static void ValidateOrderInput(Scene scene, List<string> errors, List<string> warnings)
        {
            List<CompanionOrderInput> inputs = Phase4CompanionSceneChecks.Components<CompanionOrderInput>(scene);
            if (inputs.Count != 1)
            {
                errors.Add("指示の入力（CompanionOrderInput）は 1 つであるべきですが " + inputs.Count + " です"
                    + (inputs.Count > 1 ? "（重複）" : "（欠落）") + "。試遊で待機・追従を切り替えられません。");
                return;
            }

            CompanionOrderInput input = inputs[0];
            if (input.CompanionCount == 0)
            {
                errors.Add("指示の入力に相手が 1 体も繋がっていません（キーを押しても何も起きません）。");
                return;
            }

            int companions = Phase4CompanionSceneChecks.Count<CompanionOrders>(scene);
            if (input.CompanionCount < companions)
            {
                warnings.Add("指示の入力が繋がっているのは " + input.CompanionCount + " 体で、Scene には "
                    + companions + " 体います（繋がっていない仲間はキーで切り替わりません）。");
            }
        }
    }
}
