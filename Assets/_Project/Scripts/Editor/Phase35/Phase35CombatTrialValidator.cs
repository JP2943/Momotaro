using System.Collections.Generic;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.SceneFlow;
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
    /// P3.5-10（配布・統合受入）：試遊 Scene（<c>SCN_Phase35_CombatTrial</c>）の整合性を機械検査する統合受入 Validator。
    /// <see cref="Phase35CombatTrialBuilder"/> が生成する構成を「配布前に満たすべき不変条件」として検査する：主要システムが単一で揃い
    /// （Session/HUD/Wave/勝敗/再読込/Retry/フィードバック）、重複・デバッグ専用物が混入せず（重複 HUD/Session、Debug HUD、手動編成ツール）、
    /// 相互参照（フィードバック各サブ効果・結果 SE スロット・Wave の Prefab/構成・CameraShake 対象）が配線され、初期敵 0・Missing Script 無し・
    /// 負スケール無しであること。検査本体（<see cref="Validate"/>）は AssetDatabase に依存せず Scene 走査のみで、EditMode テストが決定的に叩ける。
    ///
    /// メニュー「Momotaro/Phase 3.5/Validate Combat Trial」から現在開いている Scene を検査し、結果をログとダイアログで示す。
    /// 重複 HUD/Session の除去はこの Validator が回帰的に担保する（P3.5-10 ②：重複を作らない・混入させないことを検査で固定）。
    /// </summary>
    public static class Phase35CombatTrialValidator
    {
        /// <summary>試遊 Scene の既定パス（メニュー実行時の対象一致チェックに用いる）。</summary>
        public const string TrialScenePath = "Assets/_Project/Scenes/Tests/SCN_Phase35_CombatTrial.unity";

        /// <summary>結果 SE（<see cref="CombatFeedbackPresenter"/> が鳴らす）に最低限必要なスロット鍵。CombatFeedbackMap の SeId と一致させる。</summary>
        private static readonly string[] RequiredFeedbackSeIds = { "SE_JustGuard", "SE_Guard", "SE_JustEvade" };

        [MenuItem("Momotaro/Phase 3.5/Validate Combat Trial")]
        private static void ValidateActiveSceneMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            var errors = new List<string>();
            var warnings = new List<string>();

            if (scene.path != TrialScenePath)
            {
                warnings.Add("現在の Scene は試遊 Scene ではありません（" + (string.IsNullOrEmpty(scene.path) ? "無題" : scene.path)
                    + "）。試遊 Scene（" + TrialScenePath + "）を開いて実行してください。");
            }

            Validate(scene, errors, warnings);

            string summary = "[Phase3.5] 統合受入 Validator: "
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

            EditorUtility.DisplayDialog("Phase 3.5 統合受入 Validator",
                summary + (errors.Count > 0 ? "\n\n最初のエラー:\n" + errors[0] : string.Empty)
                + "\n\n詳細は Console を参照してください。",
                "OK");
        }

        /// <summary>
        /// Scene を走査して統合受入の不変条件を検査し、<paramref name="errors"/>／<paramref name="warnings"/> へ追記する
        /// （AssetDatabase 非依存・純走査。EditMode テストが Build 直後の Scene を渡して決定的に検証できる）。
        /// </summary>
        public static void Validate(Scene scene, List<string> errors, List<string> warnings)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                errors.Add("Scene が無効／未読込です。");
                return;
            }

            // --- 単一で存在すべき主要システム（P3.5-03/04/07/08） ---
            RequireOne<PlayerStateController>(scene, "主人公（PlayerStateController）", errors);
            RequireOne<PlayerVitalsHolder>(scene, "主人公 Vitals（PlayerVitalsHolder）", errors);
            RequireOne<CombatSessionController>(scene, "戦闘 Session（CombatSessionController）", errors);
            RequireOne<WaveRunner>(scene, "Wave 進行（WaveRunner）", errors);
            RequireOne<CombatOutcomeController>(scene, "勝敗統合（CombatOutcomeController）", errors);
            RequireOne<CombatSceneReloader>(scene, "Scene 再読込（CombatSceneReloader）", errors);
            RequireOne<CombatRetryInput>(scene, "Retry 入力（CombatRetryInput）", errors);
            RequireOne<CombatPlayHud>(scene, "試遊 HUD（CombatPlayHud）", errors);
            RequireOne<CombatFeedbackDispatcher>(scene, "フィードバック配信（CombatFeedbackDispatcher）", errors);
            RequireOne<CombatFeedbackPresenter>(scene, "フィードバック調停（CombatFeedbackPresenter）", errors);
            RequireOne<EnemyDefeatFadePresenter>(scene, "撃破フェード（EnemyDefeatFadePresenter）", errors);

            // --- 混入してはならないもの（P3.5-10 ②：重複 HUD/Session・デバッグ専用物の除去を回帰固定） ---
            ForbidAll<CombatDebugHud>(scene, "デバッグ HUD（CombatDebugHud）は試遊 Scene に含めない", errors);
            ForbidAll<EnemyTestFieldController>(scene, "手動編成ツール（EnemyTestFieldController）は Wave 駆動へ置換済み・含めない", errors);

            // --- 初期状態 ---
            int enemies = Count<EnemyActor>(scene);
            if (enemies != 0)
            {
                errors.Add("初期状態の有効な敵は 0 体であるべきですが " + enemies + " 体あります（Wave で動的生成する）。");
            }

            // --- Main Camera は 1 台（タグ一致） ---
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

            ValidateFeedbackWiring(scene, errors);
            ValidateWave(scene, errors);
            ValidateCameraShake(scene, errors);
            ValidateVfxFrames(scene, errors, warnings);
            ValidateSceneHygiene(scene, errors);
        }

        private static void ValidateFeedbackWiring(Scene scene, List<string> errors)
        {
            List<CombatFeedbackPresenter> coords = Components<CombatFeedbackPresenter>(scene);
            if (coords.Count != 1)
            {
                return; // 単一性は RequireOne が報告済み。
            }

            CombatFeedbackPresenter coord = coords[0];
            if (coord.HitStop == null)
            {
                errors.Add("CombatFeedbackPresenter に HitStop が未配線です。");
            }

            if (coord.Flash == null)
            {
                errors.Add("CombatFeedbackPresenter に Flash（被弾点滅）が未配線です。");
            }

            if (coord.CameraShake == null)
            {
                errors.Add("CombatFeedbackPresenter に CameraShake が未配線です。");
            }

            if (coord.Se == null)
            {
                errors.Add("CombatFeedbackPresenter に 結果 SE（CombatSePlayer）が未配線です。");
                return;
            }

            // 結果 SE スロットに必須の鍵（JG/Guard/JustEvade）が揃っているか（clip 未 Import は無音許容だが、スロット自体は必須）。
            var seIds = new HashSet<string>();
            if (coord.Se.Slots != null)
            {
                foreach (CombatSePlayer.SeSlot slot in coord.Se.Slots)
                {
                    if (slot != null && !string.IsNullOrEmpty(slot.seId))
                    {
                        seIds.Add(slot.seId);
                    }
                }
            }

            foreach (string required in RequiredFeedbackSeIds)
            {
                if (!seIds.Contains(required))
                {
                    errors.Add("結果 SE に必須スロット '" + required + "' がありません（CombatFeedbackMap の SeId と一致させる）。");
                }
            }
        }

        private static void ValidateWave(Scene scene, List<string> errors)
        {
            List<WaveRunner> runners = Components<WaveRunner>(scene);
            if (runners.Count != 1)
            {
                return;
            }

            WaveRunner runner = runners[0];
            if (runner.MeleePrefab == null)
            {
                errors.Add("WaveRunner に近接 Prefab が未割当です。");
            }

            if (runner.RangedPrefab == null)
            {
                errors.Add("WaveRunner に遠距離 Prefab が未割当です。");
            }

            if (runner.ElitePrefab == null)
            {
                errors.Add("WaveRunner に強敵 Prefab が未割当です。");
            }

            if (runner.WaveCount != 4)
            {
                errors.Add("Wave 構成は 4（§8.2）であるべきですが " + runner.WaveCount + " です。");
            }

            if (runner.SpawnPointCount != 4)
            {
                errors.Add("固定 Spawn Point は 4 つであるべきですが " + runner.SpawnPointCount + " です。");
            }
        }

        private static void ValidateCameraShake(Scene scene, List<string> errors)
        {
            List<CameraShakePresenter> shakes = Components<CameraShakePresenter>(scene);
            if (shakes.Count != 1)
            {
                errors.Add("CameraShakePresenter は 1 つであるべきですが " + shakes.Count + " です。");
                return;
            }

            CameraShakePresenter shake = shakes[0];
            if (shake.Target == null || shake.Target.GetComponent<Camera>() == null || !shake.Target.CompareTag("MainCamera"))
            {
                errors.Add("CameraShake の対象は子 Main Camera（follow に上書きされない localPosition）であるべきです。");
            }
        }

        private static void ValidateVfxFrames(Scene scene, List<string> errors, List<string> warnings)
        {
            List<PlayerSlashVfxPresenter> playerVfx = Components<PlayerSlashVfxPresenter>(scene);
            if (playerVfx.Count != 1)
            {
                errors.Add("主人公斬撃 VFX（PlayerSlashVfxPresenter）は 1 つであるべきですが " + playerVfx.Count + " です。");
            }
            else
            {
                PlayerSlashVfxPresenter pv = playerVfx[0];
                RequireFrameSet(pv.Stage1Frames, "主人公斬撃 1 段目", errors);
                RequireFrameSet(pv.Stage2Frames, "主人公斬撃 2 段目", errors);
                RequireFrameSet(pv.Stage3Frames, "主人公斬撃 3 段目", errors);
                RequireFrameSet(pv.SpecialFrames, "必殺技斬撃", errors);
            }

            List<EnemySlashVfxPresenter> enemyVfx = Components<EnemySlashVfxPresenter>(scene);
            if (enemyVfx.Count != 1)
            {
                errors.Add("敵斬撃 VFX（EnemySlashVfxPresenter）は 1 つであるべきですが " + enemyVfx.Count + " です。");
            }
            else if (enemyVfx[0].Entries == null || enemyVfx[0].Entries.Length == 0)
            {
                errors.Add("敵斬撃 VFX のエントリ（Small/Medium）が未設定です。");
            }

            List<EnemyUnblockableWarningPresenter> warns = Components<EnemyUnblockableWarningPresenter>(scene);
            if (warns.Count != 1)
            {
                errors.Add("ガード不能予告（EnemyUnblockableWarningPresenter）は 1 つであるべきですが " + warns.Count + " です。");
            }
            else if (warns[0].WarningFrames == null || warns[0].WarningFrames.Length == 0)
            {
                errors.Add("ガード不能予告の素材（WarningFrames）が未設定です。");
            }

            // JG 閃光は表示専用の任意演出。無い場合は警告のみ（配布は妨げない）。
            List<JustGuardVfxPresenter> jg = Components<JustGuardVfxPresenter>(scene);
            if (jg.Count == 1 && (jg[0].FlashFrames == null || jg[0].FlashFrames.Length == 0))
            {
                warnings.Add("ジャストガード閃光（JustGuardVfxPresenter）の素材が未設定です（演出のみ・任意）。");
            }
        }

        private static void RequireFrameSet(PlayerSlashVfxPresenter.SlashFrameSet set, string label, List<string> errors)
        {
            if (set == null)
            {
                errors.Add(label + " の素材セットが未設定です。");
                return;
            }

            bool anyDir = HasFrames(set.down) || HasFrames(set.up) || HasFrames(set.left) || HasFrames(set.right);
            if (!anyDir)
            {
                errors.Add(label + " の素材セットにコマがありません（4 方向いずれも空）。");
            }
        }

        private static bool HasFrames(Sprite[] frames) => frames != null && frames.Length > 0;

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

        // ---- 走査ヘルパ（AssetDatabase 非依存） ----

        private static List<T> Components<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                list.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return list;
        }

        private static int Count<T>(Scene scene) where T : Component => Components<T>(scene).Count;

        private static void RequireOne<T>(Scene scene, string label, List<string> errors) where T : Component
        {
            int n = Count<T>(scene);
            if (n != 1)
            {
                errors.Add(label + " は 1 つであるべきですが " + n + " です" + (n > 1 ? "（重複）" : "（欠落）") + "。");
            }
        }

        private static void ForbidAll<T>(Scene scene, string label, List<string> errors) where T : Component
        {
            int n = Count<T>(scene);
            if (n != 0)
            {
                errors.Add(label + "：" + n + " 個検出されました。");
            }
        }
    }
}
