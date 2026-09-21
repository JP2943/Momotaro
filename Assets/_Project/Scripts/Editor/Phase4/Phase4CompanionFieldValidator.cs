using System.Collections.Generic;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// P4-FIX F01：仲間を含む Scene が満たすべき不変条件を機械検査する。
    ///
    /// <b>何のための検査か。</b>仲間の実装には、旧 Scene でも動くようにするための<b>移行期の保険</b>が
    /// 2 つ残っている。どちらも「配線が無ければ従来どおり動く」ため、欠けていても<b>静かに通ってしまう</b>。
    ///
    /// <list type="number">
    /// <item><description><b>活動 Context が無いとき</b>、<see cref="CompanionActivityProvider"/> は
    /// 停止を返し（P4-FIX-R2）、仲間は一切動かない。Scene の配線漏れは実機で「何もしない」として現れるので、
    /// ここで先に検出する。</description></item>
    /// <item><description><b>調停役が無いとき</b>、各駆動は <see cref="CompanionActor"/>・
    /// <see cref="CompanionMotor"/> を直接書く経路へ落ちる。F02a／F02b／F02c で入れた
    /// 「書き手を 1 つにする」規則がまるごと効かなくなる。</description></item>
    /// </list>
    ///
    /// コードのテストではこれを検出できない。テストは自分で配線してしまうからで、
    /// <b>「配線し忘れた Scene」だけがテストの外にある</b>。だから Scene 側で検査する。
    /// これを通した Scene では保険の経路が使われないことが保証される。
    ///
    /// 検査本体（<see cref="Validate"/>）は AssetDatabase に依存せず Scene 走査のみで、
    /// EditMode テストが生成直後の Scene を渡して決定的に叩ける（Phase3.5 の Validator と同じ形）。
    /// </summary>
    public static class Phase4CompanionFieldValidator
    {
        /// <summary>仲間検証 Scene の既定パス（メニュー実行時の対象一致チェックに用いる）。</summary>
        public const string FieldScenePath = Phase4CompanionFieldBuilder.DefaultScenePath;

        [MenuItem("Momotaro/Phase 4/Validate Companion Field")]
        private static void ValidateActiveSceneMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            var errors = new List<string>();
            var warnings = new List<string>();

            Validate(scene, errors, warnings);

            string summary = "[Phase4] 仲間 Scene Validator: "
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

            EditorUtility.DisplayDialog("Phase 4 仲間 Scene Validator",
                summary + (errors.Count > 0 ? "\n\n最初のエラー:\n" + errors[0] : string.Empty)
                + "\n\n詳細は Console を参照してください。",
                "OK");
        }

        /// <summary>
        /// Scene を走査して不変条件を検査し、<paramref name="errors"/>／<paramref name="warnings"/> へ追記する。
        /// </summary>
        public static void Validate(Scene scene, List<string> errors, List<string> warnings)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                errors.Add("Scene が無効／未読込です。");
                return;
            }

            // --- この Scene 固有の構成（検証フィールドとして必要なもの） ---
            RequireOne<PlayerStateController>(scene, "主人公（PlayerStateController）", errors);
            RequireOne<PlayerVitalsHolder>(scene, "主人公 Vitals（PlayerVitalsHolder）", errors);
            RequireOne<GameplaySceneMode>(scene, "モードの正本（GameplaySceneMode）", errors);
            RequireOne<CombatSessionController>(scene, "戦闘 Session（CombatSessionController）", errors);
            RequireOne<CombatFeedbackDispatcher>(scene, "被弾フィードバック配信（CombatFeedbackDispatcher）", errors);
            RequireOne<EnemyTestFieldController>(scene, "敵の編成（EnemyTestFieldController）", errors);

            // --- 仲間まわり（試遊環境と共通。P4-08R で切り出した） ---
            Phase4CompanionSceneChecks.Validate(scene, errors, warnings);

            ValidateInitialEnemies(scene, errors);
            ValidateCamera(scene, errors);
            ValidateSceneHygiene(scene, errors);
        }

        /// <summary>初期状態の敵は 0 体（編成は EnemyTestFieldController から出す。Phase3 と同じ手順）。</summary>
        private static void ValidateInitialEnemies(Scene scene, List<string> errors)
        {
            int enemies = Count<EnemyActor>(scene);
            if (enemies != 0)
            {
                errors.Add("初期状態の敵は 0 体であるべきですが " + enemies + " 体あります"
                    + "（編成は EnemyTestFieldController の Context Menu から出します）。");
            }
        }

        private static void ValidateCamera(Scene scene, List<string> errors)
        {
            int mainCams = 0;
            foreach (Camera c in Components<Camera>(scene))
            {
                if (c != null && c.CompareTag("MainCamera"))
                {
                    mainCams++;
                }
            }

            if (mainCams != 1)
            {
                errors.Add("Main Camera（タグ MainCamera）は 1 台であるべきですが " + mainCams + " 台です。");
            }
        }

        private static void ValidateSceneHygiene(Scene scene, List<string> errors)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    {
                        errors.Add("Missing Script があります: " + t.name);
                    }

                    Vector3 s = t.localScale;
                    if (s.x < 0f || s.y < 0f || s.z < 0f)
                    {
                        errors.Add("負スケールがあります: " + t.name);
                    }
                }
            }
        }

        // ---- 走査ヘルパは共通クラスのものを使う（2 か所に持たない） ----

        private static List<T> Components<T>(Scene scene) where T : Component =>
            Phase4CompanionSceneChecks.Components<T>(scene);

        private static int Count<T>(Scene scene) where T : Component =>
            Phase4CompanionSceneChecks.Count<T>(scene);

        private static void RequireOne<T>(Scene scene, string label, List<string> errors) where T : Component =>
            Phase4CompanionSceneChecks.RequireOne<T>(scene, label, errors);
    }
}
