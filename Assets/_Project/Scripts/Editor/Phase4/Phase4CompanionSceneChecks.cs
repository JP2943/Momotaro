using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Companion;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// <b>仲間が居る Scene なら必ず満たすべきこと</b>をまとめた検査（P4-08R で共通化）。
    ///
    /// 検証フィールド（<see cref="Phase4CompanionFieldValidator"/>）と試遊環境
    /// （<see cref="Phase4CompanionTrialValidator"/>）は、Scene としての目的が違うので
    /// 要求する構成も違う（片方は敵編成ツール、もう片方は Wave と HUD）。
    /// だが<b>仲間まわりの不変条件は同じ</b>であり、そこを 2 か所に書くと必ず片方が古くなる。
    ///
    /// ここが守るのは、コードのテストでは捕まえられない 2 つの穴（記録 005 参照）。
    /// <list type="number">
    /// <item><description>活動 Context が無い／繋がっていない Scene では、仲間は移行期フォールバックで動き、
    /// Pause・会話の区別ができない。</description></item>
    /// <item><description>調停役が無い仲間では、各駆動が Actor と Motor を直接書く経路へ落ち、
    /// F02a〜F02c の「書き手を 1 つにする」規則がまるごと効かない。</description></item>
    /// </list>
    /// テストは自分で配線してしまうので、<b>配線し忘れた Scene だけがテストの外にある</b>。だから Scene 側で見る。
    /// </summary>
    public static class Phase4CompanionSceneChecks
    {
        /// <summary>仲間まわりの不変条件を検査して追記する（AssetDatabase 非依存・純走査）。</summary>
        public static void Validate(Scene scene, List<string> errors, List<string> warnings)
        {
            RequireOne<CompanionActivityContext>(scene, "仲間の活動 Context（CompanionActivityContext）", errors);

            ValidateActivityContext(scene, errors);
            ValidateCompanions(scene, errors, warnings);
            ValidateInvestigationPoints(scene, errors, warnings);
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

                // --- プレイヤーの指示（P4-07B） ---
                // 無くても「常について来い」で動くが、それでは指示を試せない。検証 Scene としては必須にする。
                CompanionOrders orders = go.GetComponent<CompanionOrders>();
                if (orders == null)
                {
                    errors.Add(who + "：指示の保持（CompanionOrders）がありません（待機・追従を切り替えられません）。");
                }
                else if (orders.Current != CompanionOrder.Follow)
                {
                    warnings.Add(who + "：初期の指示が「" + orders.Current
                        + "」になっています（Scene を開いた直後から待機したままになります）。");
                }

                // --- 探索行動（P4-07A） ---
                // Data で探索を切っている仲間（猿・雉の想定）には求めない。切っていないのに駆動が無いと、
                // 「Data では探索できることになっているのに一生調べない」という食い違いになる。
                if (actor != null && actor.Data != null && actor.Data.CanInvestigate
                    && go.GetComponent<CompanionInvestigationController>() == null)
                {
                    errors.Add(who + "：探索（CompanionInvestigationController）がありません。"
                        + "Data では探索できることになっているのに、調べに行く駆動が載っていません。");
                }
            }
        }

        /// <summary>
        /// 調査地点（P4-07A）を検査する。探索できる仲間が居るのに地点が 1 つも無ければ、
        /// 探索は「動かない」のか「試せていない」のか区別が付かない。検証 Scene としては後者を許さない。
        /// </summary>
        private static void ValidateInvestigationPoints(Scene scene, List<string> errors, List<string> warnings)
        {
            bool anyInvestigator = false;
            foreach (CompanionActor actor in Components<CompanionActor>(scene))
            {
                if (actor != null && actor.Data != null && actor.Data.CanInvestigate)
                {
                    anyInvestigator = true;
                    break;
                }
            }

            if (!anyInvestigator)
            {
                return;
            }

            List<CompanionInvestigationPoint> points = Components<CompanionInvestigationPoint>(scene);
            if (points.Count == 0)
            {
                errors.Add("調査地点（CompanionInvestigationPoint）が 1 つもありません"
                    + "（探索できる仲間が居るのに、調べに行く先が無く探索を試せません）。");
                return;
            }

            List<PlayerStateController> players = Components<PlayerStateController>(scene);
            if (players.Count != 1)
            {
                return; // 単一性は RequireOne が報告済み。紐の起点が定まらないので距離は見ない。
            }

            // 紐の外にしか地点が無いと、探索は有効なのに一度も動かない。
            // 「壊れている」と「そういう配置」の区別が付かない止まり方なので、Scene の時点で気付けるようにする。
            Vector3 leader = players[0].transform.position;
            foreach (CompanionActor actor in Components<CompanionActor>(scene))
            {
                if (actor == null || actor.Data == null || !actor.Data.CanInvestigate)
                {
                    continue;
                }

                float leash = actor.Data.InvestigateLeashDistance;
                bool anyReachable = false;
                foreach (CompanionInvestigationPoint point in points)
                {
                    if (point != null
                        && FormationSlot.HorizontalDistance(leader, point.transform.position) <= leash)
                    {
                        anyReachable = true;
                        break;
                    }
                }

                if (!anyReachable)
                {
                    warnings.Add(actor.gameObject.name + "：主人公から紐（" + leash.ToString("F1")
                        + "m）の内側に調査地点がありません（探索は有効ですが一度も動きません）。");
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

        // ---- 走査ヘルパ（AssetDatabase 非依存） ----

        internal static List<T> Components<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                list.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return list;
        }

        internal static int Count<T>(Scene scene) where T : Component => Components<T>(scene).Count;

        internal static void RequireOne<T>(Scene scene, string label, List<string> errors) where T : Component
        {
            int n = Count<T>(scene);
            if (n != 1)
            {
                errors.Add(label + " は 1 つであるべきですが " + n + " です" + (n > 1 ? "（重複）" : "（欠落）") + "。");
            }
        }
    }
}
