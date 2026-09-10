using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Companion;
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
    /// <see cref="CompanionActivityProvider.Fallback"/>（＝自由行動）へ落ちる。Pause・会話・イベントの
    /// 区別ができないので、会話中に仲間が歩き回る。</description></item>
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

            // --- 単一で存在すべきもの ---
            RequireOne<PlayerStateController>(scene, "主人公（PlayerStateController）", errors);
            RequireOne<PlayerVitalsHolder>(scene, "主人公 Vitals（PlayerVitalsHolder）", errors);
            RequireOne<GameplaySceneMode>(scene, "モードの正本（GameplaySceneMode）", errors);
            RequireOne<CombatSessionController>(scene, "戦闘 Session（CombatSessionController）", errors);
            RequireOne<CompanionActivityContext>(scene, "仲間の活動 Context（CompanionActivityContext）", errors);
            RequireOne<CombatFeedbackDispatcher>(scene, "被弾フィードバック配信（CombatFeedbackDispatcher）", errors);
            RequireOne<EnemyTestFieldController>(scene, "敵の編成（EnemyTestFieldController）", errors);

            ValidateActivityContext(scene, errors);
            ValidateCompanions(scene, errors, warnings);
            ValidateInitialEnemies(scene, errors);
            ValidateCamera(scene, errors);
            ValidateSceneHygiene(scene, errors);
        }

        /// <summary>
        /// 活動 Context が「読める状態」になっているかを検査する（穴 1 を塞ぐ本体）。
        ///
        /// 置いてあるだけでは足りない。セッション未配線かつ「無い区画」宣言も無い Context は
        /// 常に停止を返すので、仲間はその Scene で一切動かない。置き忘れと同じくらい分かりにくいので、
        /// 置いてあること・繋がっていることの両方を要求する。
        /// </summary>
        private static void ValidateActivityContext(Scene scene, List<string> errors)
        {
            List<CompanionActivityContext> contexts = Components<CompanionActivityContext>(scene);
            if (contexts.Count != 1)
            {
                return; // 単一性は RequireOne が報告済み。
            }

            CompanionActivityContext context = contexts[0];
            if (!context.IsWired)
            {
                errors.Add("活動 Context が未配線です（戦闘 Session を割り当てるか、"
                    + "戦闘の無い区画なら MarkAreaWithoutEncounter を立ててください）。"
                    + "このままでは常に停止を返し、仲間が一切動きません。");
                return;
            }

            if (context.Session != null && context.Session.gameObject.scene != scene)
            {
                errors.Add("活動 Context の戦闘 Session が別の Scene のものです（この Scene の Session を割り当ててください）。");
            }
        }

        /// <summary>
        /// 仲間 1 体ごとの配線を検査する（穴 2 を塞ぐ本体）。
        /// 調停役が無ければ各駆動が Actor・Motor を直接書く経路へ落ちるため、あることを Scene 側で要求する。
        /// </summary>
        private static void ValidateCompanions(Scene scene, List<string> errors, List<string> warnings)
        {
            List<GameObject> companions = CollectCompanionObjects(scene);
            if (companions.Count == 0)
            {
                errors.Add("仲間（CompanionActor）が 1 体もいません（仲間の検証 Scene として成立しません）。");
                return;
            }

            List<PlayerStateController> players = Components<PlayerStateController>(scene);
            Transform player = players.Count == 1 ? players[0].transform : null;

            foreach (GameObject go in companions)
            {
                string who = go.name;

                CompanionActor actor = go.GetComponent<CompanionActor>();
                if (actor == null)
                {
                    errors.Add(who + "：仲間の本体（CompanionActor）がありません（状態を持たないまま駆動だけが載っています）。");
                }
                else if (actor.Data == null)
                {
                    errors.Add(who + "：仲間 Data（CompanionData）が未割当です（数値の正本が無く、既定値で動きます）。");
                }

                // --- 状態の書き手（F02b／F02c） ---
                if (go.GetComponent<CompanionStateArbiter>() == null)
                {
                    errors.Add(who + "：状態の調停役（CompanionStateArbiter）がありません。"
                        + "各駆動が Actor へ直接状態を書く移行期の経路へ落ち、行動の所有権も許可表も効きません。");
                }

                // --- 移動と向きの書き手（F02a） ---
                // 2 つを独立に見る。Motor が無いことを理由に調停役の欠落を隠すと、
                // 「Motor を足したら今度は調停役が無かった」と 2 回に分けて気付くことになる。
                if (go.GetComponent<CompanionMotor>() == null)
                {
                    errors.Add(who + "：移動（CompanionMotor）がありません。");
                }

                if (go.GetComponent<CompanionMovementArbiter>() == null)
                {
                    errors.Add(who + "：移動の調停役（CompanionMovementArbiter）がありません。"
                        + "追従と戦闘が同じフレームに Motor を書き合い、向きも位置も後勝ちになります。");
                }

                // --- 追従の相手（Find* を使わないので、Scene に焼いていないと解決できない） ---
                CompanionFollowController follow = go.GetComponent<CompanionFollowController>();
                if (follow == null)
                {
                    errors.Add(who + "：追従（CompanionFollowController）がありません。");
                }
                else if (follow.Leader == null)
                {
                    errors.Add(who + "：追従対象が未設定です（主人公を割り当ててください）。");
                }
                else if (player != null && follow.Leader.root != player.root)
                {
                    errors.Add(who + "：追従対象がこの Scene の主人公ではありません（" + follow.Leader.name + "）。");
                }

                // --- 被弾と演出（F04） ---
                CompanionHitReceiver receiver = go.GetComponent<CompanionHitReceiver>();
                if (receiver == null)
                {
                    errors.Add(who + "：被弾の受け口（CompanionHitReceiver）がありません（敵の攻撃が当たりません）。");
                }
                else if (go.GetComponent<CompanionFeedbackBinder>() == null)
                {
                    warnings.Add(who + "：被弾演出の登録役（CompanionFeedbackBinder）がありません"
                        + "（被弾しても点滅・SE が出ません。演出のみ）。");
                }

                // --- 守護の相手（P4-05） ---
                CompanionGuardianController guardian = go.GetComponent<CompanionGuardianController>();
                if (guardian != null && guardian.ProtectedTarget == null)
                {
                    warnings.Add(who + "：守護対象が Scene に焼かれていません"
                        + "（Runtime では追従対象から解決されますが、Scene を読んだだけでは誰を庇うか分かりません）。");
                }

                // --- 索敵・戦闘（P4-03） ---
                if (go.GetComponent<CompanionTargetTracker>() == null)
                {
                    errors.Add(who + "：索敵（CompanionTargetTracker）がありません（誰も狙いません）。");
                }

                if (go.GetComponent<CompanionCombatController>() == null)
                {
                    errors.Add(who + "：戦闘（CompanionCombatController）がありません。");
                }
            }
        }

        /// <summary>
        /// 仲間らしき GameObject を集める。<b>本体（<see cref="CompanionActor"/>）の有無で絞らない。</b>
        /// Actor が抜け落ちた仲間こそ検査したい対象なので、駆動が 1 つでも載っていれば候補にする。
        /// </summary>
        private static List<GameObject> CollectCompanionObjects(Scene scene)
        {
            var seen = new HashSet<int>();
            var list = new List<GameObject>();

            void Collect<T>() where T : Component
            {
                foreach (T c in Components<T>(scene))
                {
                    if (c != null && seen.Add(c.gameObject.GetInstanceID()))
                    {
                        list.Add(c.gameObject);
                    }
                }
            }

            Collect<CompanionActor>();
            Collect<CompanionMotor>();
            Collect<CompanionFollowController>();
            Collect<CompanionCombatController>();
            Collect<CompanionHitReceiver>();

            return list;
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
    }
}
