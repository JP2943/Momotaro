using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Player;
using Momotaro.Infrastructure.Input;
using Momotaro.Presentation.Companion;
using Momotaro.Presentation.Hud;
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
            ValidateInvestigationPresentation(scene, errors, warnings);
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

                // --- 探索（P4-07A） ---
                // 依頼を受ける駆動。無いと、調停役が地点の RequiredCompanion に一致する駆動を見つけられず、
                // 「加入済みなのに調べに行かない」という止まり方をする。
                if (go.GetComponent<CompanionInvestigationController>() == null)
                {
                    errors.Add(who + "：探索（CompanionInvestigationController）がありません。");
                }

                // 同一 Body を指す探索の駆動が 2 つ（暗黙の二重 Tick。E24）。
                // 同じ GameObject への 2 個目は DisallowMultipleComponent が AddComponent でも拒むが、
                // 別 GameObject に置いた駆動が _actor でこの Body を指す配線は防げないので、Scene 全体で Body ごとに数える。
                if (actor != null && CountInvestigationDrivers(scene, actor) > 1)
                {
                    errors.Add(who + "：探索の駆動（CompanionInvestigationController）が重複しています（同じ Body を 2 つ以上の駆動が動かす＝二重 Tick）。");
                }

                // --- 探索の表示代理（P4-07B。v1.0 §5.2・§11「必須の仮本体・方向表示・テキスト参照は厳格検査」） ---
                CompanionInvestigationProxyPresenter proxy = go.GetComponentInChildren<CompanionInvestigationProxyPresenter>(true);
                if (proxy == null)
                {
                    errors.Add(who + "：探索の表示代理（CompanionInvestigationProxyPresenter）がありません"
                        + "（Down／退場中の調査が見えず、通常表示の抑制もされません）。");
                }
                else
                {
                    if (proxy.Driver == null)
                    {
                        errors.Add(who + "：表示代理に探索の駆動が配線されていません。");
                    }

                    if (proxy.Normal == null)
                    {
                        errors.Add(who + "：表示代理に通常表示（CompanionPlaceholderPresenter）が配線されていません（表示が二重になります）。");
                    }

                    if (proxy.ProxyBody == null || proxy.ProxyBody.sprite == null)
                    {
                        errors.Add(who + "：表示代理の本体（SpriteRenderer と仮素材）が未設定です。");
                    }

                    if (proxy.ProxyArrow == null || proxy.ProxyArrow.sprite == null)
                    {
                        errors.Add(who + "：表示代理の方向インジケータ（SpriteRenderer と仮素材）が未設定です。");
                    }

                    if (proxy.Label == null)
                    {
                        errors.Add(who + "：表示代理の進行ラベル（TextMesh）が未設定です（移動／調査中／帰還を文字で示せません）。");
                    }
                    else if (proxy.Label.font == null)
                    {
                        errors.Add(who + "：表示代理の進行ラベルにフォントが未設定です（文字が描かれません）。");
                    }
                }

                if (go.GetComponents<CompanionCombatController>().Length > 1
                    || go.GetComponents<CompanionFollowController>().Length > 1
                    || go.GetComponents<CompanionDefenseController>().Length > 1
                    || go.GetComponents<CompanionGuardianController>().Length > 1)
                {
                    errors.Add(who + "：同じ駆動が重複しています（追従・戦闘・防御・守護のいずれか。二重 Tick）。");
                }
            }
        }

        /// <summary>
        /// 探索の配線（P4-07A。v1.0 §13.2「加入供給元・活動 Context・地点・参照一致・地点 ID 重複」）を検査する。
        /// 調停役・加入供給元・記録は Scene に 1 つずつ。地点は 1 つ以上で、PointId が有効かつ重複せず、
        /// 設定 Data と要求仲間が入っていること。犬丸 0 体のままでは合格しない（駆動の一致検査）。
        /// </summary>
        private static void ValidateInvestigationPoints(Scene scene, List<string> errors, List<string> warnings)
        {
            RequireOne<InvestigationCoordinator>(scene, "探索の調停役（InvestigationCoordinator）", errors);
            RequireOne<CompanionRosterContext>(scene, "加入資格の供給元（CompanionRosterContext）", errors);
            RequireOne<InvestigationRecordHolder>(scene, "調査記録（InvestigationRecordHolder）", errors);

            List<InvestigationCoordinator> coordinators = Components<InvestigationCoordinator>(scene);
            List<CompanionInvestigationController> drivers = Components<CompanionInvestigationController>(scene);
            List<CompanionRosterContext> rosters = Components<CompanionRosterContext>(scene);

            if (coordinators.Count == 1)
            {
                InvestigationCoordinator c = coordinators[0];
                if (c.Player == null)
                {
                    errors.Add("InvestigationCoordinator：主人公が配線されていません。");
                }

                if (c.Roster == null)
                {
                    errors.Add("InvestigationCoordinator：加入資格の供給元が配線されていません。");
                }

                if (c.RecordHolder == null)
                {
                    errors.Add("InvestigationCoordinator：調査記録が配線されていません。");
                }

                if (c.Companions == null || c.Companions.Count == 0)
                {
                    errors.Add("InvestigationCoordinator：探索の駆動が 1 つも配線されていません（犬丸 0 体のままでは合格しない）。");
                }
                else
                {
                    foreach (CompanionInvestigationController driver in drivers)
                    {
                        bool wired = false;
                        for (int i = 0; i < c.Companions.Count; i++)
                        {
                            if (ReferenceEquals(c.Companions[i], driver))
                            {
                                wired = true;
                                break;
                            }
                        }

                        if (!wired)
                        {
                            errors.Add(driver.gameObject.name + "：探索の駆動が InvestigationCoordinator に配線されていません（参照不一致）。");
                        }
                    }
                }
            }

            List<CompanionInvestigationPoint> points = Components<CompanionInvestigationPoint>(scene);
            if (points.Count == 0)
            {
                errors.Add("調査地点（CompanionInvestigationPoint）が 1 つもありません（探索を試せません）。");
                return;
            }

            var seenIds = new Dictionary<string, string>();
            foreach (CompanionInvestigationPoint point in points)
            {
                string who = point.gameObject.name;
                if (!point.PointId.IsValid)
                {
                    errors.Add(who + "：PointId が無効です（小文字 snake_case の StableId が必要。GetInstanceID で代用しない）。");
                }
                else if (seenIds.TryGetValue(point.PointId.Value, out string other))
                {
                    errors.Add(who + "：PointId '" + point.PointId.Value + "' が " + other + " と重複しています。");
                }
                else
                {
                    seenIds.Add(point.PointId.Value, who);
                }

                if (point.SettingsData == null)
                {
                    errors.Add(who + "：探索設定 Data（InvestigationSettingsData）が未設定です。");
                }

                if (!point.RequiredCompanion.IsValid)
                {
                    errors.Add(who + "：RequiredCompanion（仲間の StableId）が無効です。");
                }
                else
                {
                    bool anyDriver = false;
                    foreach (CompanionInvestigationController driver in drivers)
                    {
                        if (driver != null && driver.CompanionId.Equals(point.RequiredCompanion))
                        {
                            anyDriver = true;
                            break;
                        }
                    }

                    // 未加入の検証経路（Roster に無いが駆動は居る）は許す。駆動そのものが Scene に無いのは配線漏れ。
                    if (!anyDriver)
                    {
                        warnings.Add(who + "：RequiredCompanion '" + point.RequiredCompanion.Value
                            + "' に一致する探索の駆動が Scene にありません（未加入の検証地点なら意図どおり）。");
                    }
                }

                if (!point.DiscoveryId.IsValid)
                {
                    warnings.Add(who + "：DiscoveryId が無効です（発見通知の識別子が空のまま届きます）。");
                }
            }

            if (rosters.Count == 1 && rosters[0].Count == 0)
            {
                warnings.Add("CompanionRosterContext：加入済みの仲間が 0 体です（すべての地点が未加入ヒントになります）。");
            }
        }

        /// <summary>
        /// 探索の入力・表示の配線（P4-07B。v1.0 §12「入力仲介は Infrastructure」「表示代理・UI は Presentation」、§13.2「入力、UI」）を検査する。
        /// 入力仲介と短文 UI は Scene に 1 つずつで、同じ調停役を指す。地点にはマーカー（輪と文字）が 1 つずつ付き、UI がそれらを知っている。
        /// </summary>
        private static void ValidateInvestigationPresentation(Scene scene, List<string> errors, List<string> warnings)
        {
            RequireOne<InvestigationInteractInput>(scene, "探索の入力仲介（InvestigationInteractInput）", errors);
            RequireOne<InvestigationPromptHud>(scene, "探索の短文 UI（InvestigationPromptHud）", errors);

            List<InvestigationCoordinator> coordinators = Components<InvestigationCoordinator>(scene);
            InvestigationCoordinator coordinator = coordinators.Count == 1 ? coordinators[0] : null;

            List<InvestigationInteractInput> inputs = Components<InvestigationInteractInput>(scene);
            if (inputs.Count == 1)
            {
                if (inputs[0].Coordinator == null)
                {
                    errors.Add("InvestigationInteractInput：調停役が配線されていません（Interact を押しても依頼が出ません）。");
                }
                else if (coordinator != null && !ReferenceEquals(inputs[0].Coordinator, coordinator))
                {
                    errors.Add("InvestigationInteractInput：この Scene の調停役ではないものを指しています（参照不一致）。");
                }
            }

            List<CompanionInvestigationPoint> points = Components<CompanionInvestigationPoint>(scene);
            List<InvestigationPointMarker> markers = Components<InvestigationPointMarker>(scene);
            var markerByPoint = new Dictionary<CompanionInvestigationPoint, InvestigationPointMarker>();
            foreach (InvestigationPointMarker marker in markers)
            {
                string who = marker.gameObject.name;
                if (marker.Point == null)
                {
                    errors.Add(who + "：マーカーに地点が配線されていません。");
                    continue;
                }

                if (markerByPoint.ContainsKey(marker.Point))
                {
                    errors.Add(who + "：同じ地点にマーカーが 2 つ付いています（表示が二重になります）。");
                }
                else
                {
                    markerByPoint.Add(marker.Point, marker);
                }

                if (marker.Ring == null || marker.Ring.sprite == null)
                {
                    errors.Add(who + "：マーカーの輪（SpriteRenderer と Sprite）が未設定です（Gizmos を切ると地点が見えません）。");
                }

                if (marker.Label == null)
                {
                    errors.Add(who + "：マーカーの文字（TextMesh）が未設定です（済／調べる／理由を文字で示せません）。");
                }
                else if (marker.Label.font == null)
                {
                    errors.Add(who + "：マーカーの文字にフォントが未設定です（文字が描かれません）。");
                }
            }

            foreach (CompanionInvestigationPoint point in points)
            {
                if (!markerByPoint.ContainsKey(point))
                {
                    errors.Add(point.gameObject.name + "：地点にマーカー（InvestigationPointMarker）がありません（通常の Game 表示で識別できません）。");
                }
            }

            List<InvestigationPromptHud> huds = Components<InvestigationPromptHud>(scene);
            if (huds.Count == 1)
            {
                InvestigationPromptHud hud = huds[0];
                if (hud.Coordinator == null)
                {
                    errors.Add("InvestigationPromptHud：調停役が配線されていません（案内・通知が出ません）。");
                }
                else if (coordinator != null && !ReferenceEquals(hud.Coordinator, coordinator))
                {
                    errors.Add("InvestigationPromptHud：この Scene の調停役ではないものを指しています（参照不一致）。");
                }

                foreach (InvestigationPointMarker marker in markers)
                {
                    bool known = false;
                    for (int i = 0; i < hud.Markers.Count; i++)
                    {
                        if (ReferenceEquals(hud.Markers[i], marker))
                        {
                            known = true;
                            break;
                        }
                    }

                    if (!known)
                    {
                        errors.Add(marker.gameObject.name + "：マーカーが InvestigationPromptHud に配線されていません（候補の案内がその地点に出ません）。");
                    }
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
            Collect<CompanionInvestigationController>();

            return list;
        }

        /// <summary>Scene 内で <paramref name="actor"/> を Body として動かす探索の駆動の数（別 GameObject の駆動も含む。E24）。</summary>
        private static int CountInvestigationDrivers(Scene scene, CompanionActor actor)
        {
            int n = 0;
            foreach (CompanionInvestigationController driver in Components<CompanionInvestigationController>(scene))
            {
                if (driver != null && driver.BoundActor == actor)
                {
                    n++;
                }
            }

            return n;
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
