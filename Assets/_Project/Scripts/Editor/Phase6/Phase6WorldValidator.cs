using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Events;
using Momotaro.Data.Progression;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using Momotaro.Editor.Phase55;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase6
{
    /// <summary>
    /// P6A の検証 campaign を検査する（P6 仕様 §11。工程 P6A-06）。
    ///
    /// 2 本立て：
    /// <list type="number">
    /// <item><b>配置</b>：A–B・B–C の 2 つの隣接を P5.5 の配置検査（<see cref="Phase55WorldValidator"/>）へそのまま通す。
    /// 境界・通路・並び・カメラ軸・覆い・接続 Data を P5.5 と同じ基準で見る（P5 の Scene 検査も内側で走る）。</item>
    /// <item><b>campaign</b>：カタログが実行時と同じ手順で組めること、お地蔵様の入口と ShrinePoint が実在して戦闘区域の外にあること、
    /// Area Data の保存対象一覧（Manifest）が<b>Scene に実在する内容と一致</b>すること、保存 ID が campaign 内で一意であること。</item>
    /// </list>
    ///
    /// 期待値は Builder の定数ではなく、生成された Scene と Data から採る（P5.5 の方針と同じ）。
    /// </summary>
    public static class Phase6WorldValidator
    {
        [MenuItem("Momotaro/Phase 6A/Validate Progression World")]
        private static void ValidateInteractive()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Validate(errors, warnings);
            EditorUtility.DisplayDialog("P6A 検証ワールドの検査",
                errors.Count == 0
                    ? "検査を通りました（警告 " + warnings.Count + " 件）。"
                    : "エラー " + errors.Count + " 件。\n" + string.Join("\n", errors), "OK");
        }

        /// <summary>A–B・B–C の配置（P5.5 の検査へ渡す形）。</summary>
        public static IReadOnlyList<Phase55Arrangement> Arrangements() => new List<Phase55Arrangement>
        {
            new Phase55Arrangement
            {
                Label = Phase6WorldIds.Profile.Tag + " A–B",
                ASide = Phase5SeamSide.East,
                AreaAScenePath = Phase6WorldIds.AreaAScenePath,
                AreaBScenePath = Phase6WorldIds.AreaBScenePath,
                CatalogDataPath = Phase6WorldIds.CatalogDataPath,
                ConnectionDataPath = Phase6WorldIds.ConnectionDataPath,
                AreaAId = Phase6WorldIds.AreaA,
                AreaBId = Phase6WorldIds.AreaB,
                ConnectionsId = Phase6WorldIds.Connections,
                ConnectionsDisplayName = Phase6WorldIds.Profile.Tag + " エリア接続（東西 A–B–C）",
                ExitFromA = Phase6WorldIds.ExitAEast,
                ExitFromB = Phase6WorldIds.ExitBWest,
                EntryInA = Phase5AreaIds.AreaAFromB,
                EntryInB = Phase5AreaIds.AreaBFromA,
                ConnectionAToB = Phase6WorldIds.ConnectionAToB,
                ConnectionBToA = Phase6WorldIds.ConnectionBToA,
                ForwardDirection = AreaConnectionDirection.East,
                BackwardDirection = AreaConnectionDirection.West,
                PassageCenter = Phase6WorldLayout.SeamZ,
                PassageWidth = Phase6WorldLayout.PassageWidth,
                Targets = Phase6WorldBuilder.Targets,
                SharedConnectionList = true,
            },
            new Phase55Arrangement
            {
                Label = Phase6WorldIds.Profile.Tag + " B–C",
                ASide = Phase5SeamSide.East,
                AreaAScenePath = Phase6WorldIds.AreaBScenePath,
                AreaBScenePath = Phase6WorldIds.AreaCScenePath,
                CatalogDataPath = Phase6WorldIds.CatalogDataPath,
                ConnectionDataPath = Phase6WorldIds.ConnectionDataPath,
                AreaAId = Phase6WorldIds.AreaB,
                AreaBId = Phase6WorldIds.AreaC,
                ConnectionsId = Phase6WorldIds.Connections,
                ConnectionsDisplayName = Phase6WorldIds.Profile.Tag + " エリア接続（東西 A–B–C）",
                ExitFromA = Phase6WorldIds.ExitBEast,
                ExitFromB = Phase6WorldIds.ExitCWest,
                EntryInA = Phase6WorldIds.EntryBFromC,
                EntryInB = Phase6WorldIds.EntryCFromB,
                ConnectionAToB = Phase6WorldIds.ConnectionBToC,
                ConnectionBToA = Phase6WorldIds.ConnectionCToB,
                ForwardDirection = AreaConnectionDirection.East,
                BackwardDirection = AreaConnectionDirection.West,
                PassageCenter = Phase6WorldLayout.SeamZ,
                PassageWidth = Phase6WorldLayout.PassageWidth,
                Targets = Phase6WorldBuilder.Targets,
                SharedConnectionList = true,
            },
        };

        [MenuItem("Momotaro/Phase 6B/Validate Growth World")]
        private static void ValidateP6BInteractive()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            ValidateP6B(errors, warnings);
            EditorUtility.DisplayDialog("P6B 検証ワールドの検査",
                errors.Count == 0
                    ? "検査を通りました（警告 " + warnings.Count + " 件）。"
                    : "エラー " + errors.Count + " 件。\n" + string.Join("\n", errors), "OK");
        }

        /// <summary>
        /// P6B の検証ワールドを見る（P6B 04／17）。P6A と同じ配置・campaign の検査に加え、9 ノード・効果値・前提・
        /// きびだんご使用・払い戻し・章・専用スロット・入力割当を検査する。
        /// </summary>
        public static void ValidateP6B(List<string> errors, List<string> warnings)
        {
            Phase6Profile previous = Phase6WorldIds.Profile;
            Phase6WorldIds.Profile = Phase6Profile.P6B;
            try
            {
                Validate(errors, warnings);
                int start = errors.Count;
                ValidateP6BCampaign(errors);
                for (int i = start; i < errors.Count; i++)
                {
                    errors[i] = "【P6B】" + errors[i];
                }
            }
            finally
            {
                Phase6WorldIds.Profile = previous;
            }
        }

        [MenuItem("Momotaro/Phase 6C/Validate Just Evade World")]
        private static void ValidateP6CInteractive()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            ValidateP6C(errors, warnings);
            EditorUtility.DisplayDialog("P6C 検証ワールドの検査",
                errors.Count == 0
                    ? "検査を通りました（警告 " + warnings.Count + " 件）。"
                    : "エラー " + errors.Count + " 件。\n" + string.Join("\n", errors), "OK");
        }

        /// <summary>
        /// P6C の検証ワールドを見る（P6C 15）。P6B と同じ配置・campaign・成長・きびだんごの検査に加え、
        /// ジャスト回避の Data（受付・倍率・時間と無敵区間の関係）、ガード不能攻撃の可否（通常ガード不可・ジャスガ可）、
        /// 各 Area の試遊表示（1 個・同じ Scene）と命中結果 SE（ジャスト回避の音が配線されている）を検査する。
        /// </summary>
        public static void ValidateP6C(List<string> errors, List<string> warnings)
        {
            Phase6Profile previous = Phase6WorldIds.Profile;
            Phase6WorldIds.Profile = Phase6Profile.P6C;
            try
            {
                Validate(errors, warnings);
                int start = errors.Count;
                ValidateP6BCampaign(errors);
                ValidateP6CCombat(errors);
                for (int i = start; i < errors.Count; i++)
                {
                    errors[i] = "【P6C】" + errors[i];
                }
            }
            finally
            {
                Phase6WorldIds.Profile = previous;
            }
        }

        /// <summary>P6C だけの検査（Data の値の関係と、生成した Scene の表示・音の配線）。</summary>
        internal static void ValidateP6CCombat(List<string> errors)
        {
            var step = AssetDatabase.LoadAssetAtPath<Momotaro.Data.Combat.StepData>("Assets/_Project/Data/Combat/SO_Step_Momotaro.asset");
            if (step == null)
            {
                errors.Add("SO_Step_Momotaro がありません。");
            }
            else if (!Momotaro.Data.Combat.StepData.TryValidateJustEvade(step.InvincibleStartSeconds, step.InvincibleEndSeconds,
                         step.JustEvadeWindowSeconds, step.JustEvadeCounterHpMultiplier, step.JustEvadeCounterSeconds, out string reason))
            {
                errors.Add("ジャスト回避の設定: " + reason);
            }

            // 試遊で使う敵の攻撃（A・B の普通敵・遠距離、C の精鋭）の可否。ガード不能は通常ガード不可・ジャスガ可・ステップ可。
            string[] attackFolders = { "Assets/_Project/Data/Enemies" };
            foreach (string guid in AssetDatabase.FindAssets("t:EnemyAttackData", attackFolders))
            {
                var attack = AssetDatabase.LoadAssetAtPath<Momotaro.Data.Combat.EnemyAttackData>(AssetDatabase.GUIDToAssetPath(guid));
                if (attack == null || attack.AttackClass != Momotaro.Data.Combat.EnemyAttackClass.Unblockable)
                {
                    continue;
                }

                if (attack.Guardable || !attack.Steppable || (!attack.JustGuardable && !attack.IsJustGuardException))
                {
                    errors.Add(attack.name + ": ガード不能の可否が P6C の規則（通常ガード不可・ジャスガ可・ステップ可、例外は理由つき）に合いません。");
                }

                if (attack.JustGuardable && attack.JustGuardPoiseReturn <= 0f)
                {
                    errors.Add(attack.name + ": ジャスガ可なのに体幹反射が 0 です（ジャスガで体幹を崩せない）。");
                }
            }

            foreach (string scenePath in new[] { Phase6WorldIds.AreaAScenePath, Phase6WorldIds.AreaBScenePath, Phase6WorldIds.AreaCScenePath })
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(scenePath) == null)
                {
                    errors.Add("Scene がありません: " + scenePath);
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                RequireExactlyOne<Momotaro.Presentation.Combat.JustEvadeHudPresenter>(scene, "ジャスト回避の試遊表示", errors);
                bool anyJustEvadeSe = false;
                foreach (var feedback in Phase5ExplorationValidator.Components<Momotaro.Presentation.Combat.CombatFeedbackPresenter>(scene))
                {
                    var se = feedback.Se;
                    if (se == null || se.gameObject.scene != scene || se.Slots == null)
                    {
                        errors.Add(scenePath + ": 手応え演出に命中結果 SE が繋がっていません。");
                        continue;
                    }

                    foreach (var slot in se.Slots)
                    {
                        if (slot != null && slot.seId == "SE_JustEvade" && slot.clip != null)
                        {
                            anyJustEvadeSe = true;
                        }
                    }
                }

                if (!anyJustEvadeSe)
                {
                    errors.Add(scenePath + ": ジャスト回避の音（SE_JustEvade）が配線されていません。");
                }
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>P6B だけの検査（生成された Data から採る。Builder の表とも突き合わせる）。</summary>
        internal static void ValidateP6BCampaign(List<string> errors)
        {
            var data = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase6WorldIds.CatalogDataPath);
            if (data == null)
            {
                errors.Add("カタログがありません: " + Phase6WorldIds.CatalogDataPath + "（build-phase6b-world を先に実行する）。");
                return;
            }

            if (data.GrowthNodes.Count != Phase6BTrialValues.Nodes.Length)
            {
                errors.Add("成長ノードが " + data.GrowthNodes.Count + " 件です（9 件）。");
            }

            var byId = new Dictionary<string, SkillNodeData>();
            foreach (SkillNodeData node in data.GrowthNodes)
            {
                if (node != null && node.Id.IsValid)
                {
                    byId[node.Id.Value] = node;
                }
            }

            int total = 0;
            foreach (Phase6BTrialValues.Node n in Phase6BTrialValues.Nodes)
            {
                string nodeId = Phase6WorldIds.GrowthNodeId(n.Id); // P6C は growth_p6c_*（Data の ID はプロジェクト一意）
                string prerequisiteId = Phase6WorldIds.GrowthNodeId(n.Prerequisite);
                if (!byId.TryGetValue(nodeId, out SkillNodeData node))
                {
                    errors.Add("成長ノード '" + nodeId + "' がカタログにありません。");
                    continue;
                }

                total += node.VirtueCost;
                if (node.VirtueCost != n.Cost || node.MaxHpBonus != n.MaxHp || node.MaxStaminaBonus != n.Stamina
                    || !Mathf.Approximately(node.AttackHpMultiplierBonus, n.Attack)
                    || !Mathf.Approximately(node.NormalPoiseMultiplierBonus, n.Poise)
                    || node.KibidangoHealBonus != n.Heal || node.KibidangoCapacityBonus != n.Capacity)
                {
                    errors.Add("成長ノード '" + n.Id + "' の費用・効果が仕様表と一致しません。");
                }

                bool expectRoot = string.IsNullOrEmpty(n.Prerequisite);
                if (expectRoot ? node.Prerequisites.Count != 0
                        : node.Prerequisites.Count != 1 || node.Prerequisites[0] == null
                          || node.Prerequisites[0].Id.Value != prerequisiteId)
                {
                    errors.Add("成長ノード '" + n.Id + "' の前提が仕様表と一致しません。");
                }

                if (node.MutuallyExclusive.Count != 0)
                {
                    errors.Add("成長ノード '" + n.Id + "' に排他があります（P6B は排他なし）。");
                }
            }

            if (total != 270)
            {
                errors.Add("全取得の費用が " + total + " です（270）。");
            }

            var report = new Momotaro.Data.DataValidationReport();
            data.Validate(report);
            foreach (string e in report.Errors)
            {
                errors.Add("Data 検証: " + e);
            }

            if (data.KibidangoBaseCapacity != 3 || data.KibidangoBaseHeal <= 0
                || !Mathf.Approximately(data.KibidangoUseSeconds, 2f) || !Mathf.Approximately(data.KibidangoCommitSeconds, 1.5f)
                || !Mathf.Approximately(data.KibidangoMoveSpeedMultiplier, 0.2f))
            {
                errors.Add("きびだんごの設定（最大 3・回復量・2.0 秒・1.5 秒・20%）が仕様と一致しません。");
            }

            var player = AssetDatabase.LoadAssetAtPath<Momotaro.Data.Characters.PlayerData>(
                "Assets/_Project/Data/Player/SO_Player_Momotaro.asset");
            if (player != null && data.KibidangoBaseHeal != (player.MaxHp + 1) / 2)
            {
                errors.Add("きびだんごの基礎回復量 " + data.KibidangoBaseHeal + " が基礎最大 HP " + player.MaxHp
                    + " の半分の切り上げではありません。");
            }

            if (data.RefundRightsInitial != 3 || data.RefundRightsPerChapter != 3 || data.RefundRightsMax != 6)
            {
                errors.Add("払い戻し権利（初期 3・章 +3・上限 6）が仕様と一致しません。");
            }

            if (data.ChapterIds.Count == 0)
            {
                errors.Add("章の接続 fixture がありません。");
            }

            if (data.SaveSlotName == "slot0" || data.SaveSlotName != Phase6WorldIds.SaveSlot)
            {
                errors.Add("保存スロットが '" + data.SaveSlotName + "' です（" + Phase6WorldIds.SaveSlot + " のはず。P6A の slot0 と分ける）。");
            }

            if (!Mathf.Approximately(data.TestEnemyAttackScale, 1f) || !Mathf.Approximately(data.TestPlayerMaxHpScale, 1f))
            {
                errors.Add("P6B campaign にテスト用の倍率が掛かっています（1 のはず）。");
            }

            // 入力割当（記録 001 §4：F／LT）。Editor の asmdef は Input System を参照しないので定義ファイルを読む。
            string inputPath = "Assets/_Project/Settings/Input/IA_Momotaro.inputactions";
            string inputText = System.IO.File.Exists(inputPath) ? System.IO.File.ReadAllText(inputPath) : string.Empty;
            int bindings = CountOf(inputText, "\"action\": \"UseKibidango\"");
            if (!inputText.Contains("\"name\": \"UseKibidango\"") || bindings != 2
                || !inputText.Contains("\"path\": \"<Keyboard>/f\"") || !inputText.Contains("\"path\": \"<Gamepad>/leftTrigger\""))
            {
                errors.Add("入力 Gameplay/UseKibidango（F／LT の 2 割当）がありません（割当 " + bindings + " 件）。");
            }

            // 有効な Data から実行時と同じ手順でカタログが組めること（9 ノードの前提循環・未知参照も含む）。
            if (!AreaCatalog.TryBuild(data, out AreaCatalog built, out IReadOnlyList<string> buildErrors)
                || built.Campaign == null)
            {
                errors.Add("カタログを組めません: " + string.Join(" / ", buildErrors ?? new List<string>()));
            }
            else if (!built.Campaign.HasKibidangoUse)
            {
                errors.Add("カタログにきびだんご使用がありません。");
            }
        }

        private static int CountOf(string text, string token)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(token, at, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += token.Length;
            }

            return count;
        }

        /// <summary>全部見る（現在の <see cref="Phase6WorldIds.Profile"/>。既定 P6A）。</summary>
        public static void Validate(List<string> errors, List<string> warnings)
        {
            foreach (Phase55Arrangement arrangement in Arrangements())
            {
                int before = errors.Count;
                Phase55WorldValidator.Validate(arrangement, errors, warnings);
                for (int i = before; i < errors.Count; i++)
                {
                    errors[i] = "【" + arrangement.Label + "】" + errors[i];
                }
            }

            int campaignStart = errors.Count;
            ValidateCampaign(errors, warnings);
            for (int i = campaignStart; i < errors.Count; i++)
            {
                errors[i] = "【campaign】" + errors[i];
            }
        }

        /// <summary>Scene 1 枚から採った campaign の事実（値だけ。Scene を閉じても使える）。</summary>
        public sealed class AreaContentFacts
        {
            public string ScenePath;
            public readonly HashSet<string> Encounters = new HashSet<string>();
            public readonly HashSet<string> Bosses = new HashSet<string>();
            public readonly HashSet<string> FieldPlacements = new HashSet<string>();
            public readonly HashSet<string> Pickups = new HashSet<string>();
            public readonly HashSet<string> Flags = new HashSet<string>();
            public readonly HashSet<string> InvestigationPoints = new HashSet<string>();
            public readonly Dictionary<string, Vector3> Entries = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, Vector3> Shrines = new Dictionary<string, Vector3>();
            public readonly List<Bounds> Arenas = new List<Bounds>();
            public readonly List<string> Duplicates = new List<string>();
        }

        private static void ValidateCampaign(List<string> errors, List<string> warnings)
        {
            var data = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase6WorldIds.CatalogDataPath);
            if (data == null)
            {
                errors.Add("カタログがありません: " + Phase6WorldIds.CatalogDataPath + "（build-phase6-world を先に実行する）。");
                return;
            }

            // 実行時と同じ手順で組めること（campaign の整合は TryBuild の中で見る）。
            if (!AreaCatalog.TryBuild(data, out AreaCatalog catalog, out IReadOnlyList<string> buildErrors))
            {
                foreach (string e in buildErrors)
                {
                    errors.Add("カタログを組めません: " + e);
                }

                return;
            }

            if (catalog.Campaign == null)
            {
                errors.Add("カタログが campaign として組まれていません（P6 の進行を持たない）。");
                return;
            }

            if (catalog.EncounterPolicy != EncounterClearPolicy.Permanent)
            {
                errors.Add("P6A campaign の遭遇戦クリア方針が Permanent ではありません（実際=" + catalog.EncounterPolicy + "）。");
            }

            var areaFacts = new Dictionary<string, AreaContentFacts>();
            foreach (AreaDefinition area in data.Areas)
            {
                if (area == null)
                {
                    continue;
                }

                if (!catalog.TryGetScenePath(area.Id, out string scenePath))
                {
                    errors.Add("エリア " + area.Id.Value + " の Scene をカタログから引けません。");
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<Object>(scenePath) == null)
                {
                    errors.Add("Scene がありません: " + scenePath);
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                AreaContentFacts facts = Collect(scene);
                areaFacts[area.Id.Value] = facts;
                foreach (string dup in facts.Duplicates)
                {
                    errors.Add(scenePath + ": " + dup);
                }

                CompareManifest(area, facts, errors);
                ValidateCombatVfx(scene, errors);
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ValidateShrines(catalog.Campaign, areaFacts, errors);
            ValidateTitle(errors);
            ValidateUniqueness(data, areaFacts, errors);

            if (!catalog.Campaign.TryGetShrine(catalog.Campaign.InitialShrineId, out _))
            {
                errors.Add("初期お地蔵様 '" + catalog.Campaign.InitialShrineId.Value + "' がお地蔵様一覧にありません。");
            }

            ValidateKnownIds(data, catalog.Campaign, errors);

            if (catalog.Campaign.GrowthNodes.Count == 0)
            {
                warnings.Add("成長ノードが 0 件です（P6A の休息・成長の検証ができない）。");
            }
        }

        /// <summary>タイトルの起動役がこの campaign のカタログを指していること（実ビルドで null だった）。</summary>
        /// <summary>
        /// 保存の検証が使う既知 ID（レビュー R4）。この campaign の Data にある一度きり報酬は<b>すべて</b>既知であること
        /// （漏れると、その報酬を得た冒険の Continue が「未知 ID」で拒否される）。一覧に Data の無い ID が残っていないこと。
        /// 同行する犬丸が既知の仲間であること。
        /// </summary>
        private static void ValidateKnownIds(AreaCatalogData data, CampaignCatalog campaign, List<string> errors)
        {
            var grantOnce = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:RewardData", new[] { Phase6WorldIds.DataFolder }))
            {
                var reward = AssetDatabase.LoadAssetAtPath<RewardData>(AssetDatabase.GUIDToAssetPath(guid));
                if (reward == null || !reward.GrantOnce)
                {
                    continue;
                }

                grantOnce.Add(reward.Id.Value);
                if (!campaign.IsKnownGrantOnceReward(reward.Id))
                {
                    errors.Add("一度きり報酬 '" + reward.Id.Value + "' が campaign の既知報酬一覧にありません（保存を Continue できなくなる。R4）。");
                }
            }

            foreach (StableId id in data.GrantOnceRewardIds)
            {
                if (!grantOnce.Contains(id.Value))
                {
                    errors.Add("既知報酬一覧の '" + id.Value + "' に対応する一度きり報酬の Data がありません。");
                }
            }

            if (!campaign.IsKnownCompanion(CompanionIds.Inumaru))
            {
                errors.Add("同行する犬丸 '" + CompanionIds.Inumaru.Value + "' が campaign の既知仲間一覧にありません。");
            }
        }

        private static void ValidateTitle(List<string> errors)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(Phase6WorldIds.TitleScenePath) == null)
            {
                errors.Add("タイトル Scene がありません: " + Phase6WorldIds.TitleScenePath);
                return;
            }

            Scene title = EditorSceneManager.OpenScene(Phase6WorldIds.TitleScenePath, OpenSceneMode.Single);
            var launchers = Phase5ExplorationValidator.Components<Momotaro.Infrastructure.World.Phase6CampaignLauncher>(title);
            if (launchers.Count != 1)
            {
                errors.Add("タイトルの起動役（Phase6CampaignLauncher）が " + launchers.Count + " 個です（1 個であること）。");
            }

            foreach (var launcher in launchers)
            {
                if (launcher.Catalog == null
                    || AssetDatabase.GetAssetPath(launcher.Catalog) != Phase6WorldIds.CatalogDataPath)
                {
                    errors.Add("タイトルの起動役がこの campaign のカタログを指していません（実際="
                        + (launcher.Catalog != null ? AssetDatabase.GetAssetPath(launcher.Catalog) : "null") + "）。");
                }
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>Scene から保存対象を採る。</summary>
        public static AreaContentFacts Collect(Scene scene)
        {
            var facts = new AreaContentFacts { ScenePath = scene.path };

            foreach (AreaEncounterRunner runner in Phase5ExplorationValidator.Components<AreaEncounterRunner>(scene))
            {
                Add(facts, facts.Encounters, runner.EncounterId, "遭遇戦");
                var so = new SerializedObject(runner);
                if (so.FindProperty("_encounter")?.objectReferenceValue is EncounterData d && d.IsBossEncounter)
                {
                    facts.Bosses.Add(d.Id.Value);
                }
            }

            foreach (AreaFieldEnemyDirector director in Phase5ExplorationValidator.Components<AreaFieldEnemyDirector>(scene))
            {
                foreach (AreaFieldEnemyDirector.Placement p in director.Placements)
                {
                    Add(facts, facts.FieldPlacements, p.PlacementId, "普通敵の配置");
                }
            }

            foreach (AreaPickupPoint pickup in Phase5ExplorationValidator.Components<AreaPickupPoint>(scene))
            {
                Add(facts, facts.Pickups, pickup.PlacementId, "配置物");
            }

            // フラグは「立てるもの」と「見るもの」の和。同じフラグを両方が持つのは正常なので重複扱いしない。
            foreach (AreaFlagLever lever in Phase5ExplorationValidator.Components<AreaFlagLever>(scene))
            {
                if (!lever.FlagId.IsEmpty)
                {
                    facts.Flags.Add(lever.FlagId.Value);
                }
            }

            foreach (AreaFlagDoor door in Phase5ExplorationValidator.Components<AreaFlagDoor>(scene))
            {
                if (!door.FlagId.IsEmpty)
                {
                    facts.Flags.Add(door.FlagId.Value);
                }
            }

            foreach (CompanionInvestigationPoint point in Phase5ExplorationValidator.Components<CompanionInvestigationPoint>(scene))
            {
                Add(facts, facts.InvestigationPoints, point.PointId, "調査地点");
            }

            foreach (AreaEntryPoint entry in Phase5ExplorationValidator.Components<AreaEntryPoint>(scene))
            {
                facts.Entries[entry.EntryId.Value] = entry.ArrivalPosition;
            }

            foreach (ShrinePoint shrine in Phase5ExplorationValidator.Components<ShrinePoint>(scene))
            {
                if (facts.Shrines.ContainsKey(shrine.ShrineId.Value))
                {
                    facts.Duplicates.Add("お地蔵様の ID が Scene 内で重複しています: " + shrine.ShrineId.Value);
                }

                facts.Shrines[shrine.ShrineId.Value] = shrine.InteractionAnchor;

                // 受付の錨が固体の中・すぐ脇にあると、遮蔽判定（Default 層へ半径 0.25m の球）が像や壁に当たって
                // 一度も調べられない（実際に踏んだ）。錨の高さ 0.5m の点から球の半径＋余裕の範囲に固体が無いこと。
                Vector3 probePoint = shrine.InteractionAnchor + Vector3.up * 0.5f;
                foreach (Collider c in Phase5ExplorationValidator.Components<Collider>(scene))
                {
                    if (c == null || c.isTrigger)
                    {
                        continue;
                    }

                    if (c.gameObject.layer != Momotaro.Gameplay.Combat.CombatLayers.WallLayer)
                    {
                        continue;
                    }

                    Bounds b = BoundsOf(c);
                    if (b.size == Vector3.zero)
                    {
                        continue;
                    }

                    if (Vector3.Distance(b.ClosestPoint(probePoint), probePoint) < 0.3f)
                    {
                        facts.Duplicates.Add("お地蔵様 " + shrine.ShrineId.Value + " の受付の錨が固体 '" + c.name
                            + "' に近すぎます（遮蔽判定に当たって調べられない）");
                    }
                }
            }

            foreach (AreaArenaBoundary arena in Phase5ExplorationValidator.Components<AreaArenaBoundary>(scene))
            {
                facts.Arenas.Add(arena.SafeBounds);
            }

            return facts;
        }

        /// <summary>
        /// Collider の範囲。活動ゲートが無効化した Collider は <c>bounds</c> が空になるので、
        /// Box は寸法から組み立てる（P5.5 の検査が踏んだのと同じ）。
        /// </summary>
        private static Bounds BoundsOf(Collider c)
        {
            if (c is BoxCollider box)
            {
                Transform t = box.transform;
                Vector3 center = t.TransformPoint(box.center);
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                return new Bounds(center, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
            }

            return c.enabled ? c.bounds : new Bounds(c.transform.position, Vector3.zero);
        }

        private static void Add(AreaContentFacts facts, HashSet<string> set, StableId id, string label)
        {
            if (id.IsEmpty)
            {
                facts.Duplicates.Add("保存 ID が空です: " + label);
                return;
            }

            if (!set.Add(id.Value))
            {
                facts.Duplicates.Add("保存 ID が Scene 内で重複しています: " + label + " " + id.Value);
            }
        }

        /// <summary>
        /// 戦闘 VFX（P6A 27）。P5 系の Builder は剣閃を置いていなかった（試遊で攻撃の VFX が出なかった原因）。
        /// 置かれていること・素材が全段全方向に入っていること・<b>この Area の主人公</b>を見ていることを確かめる
        /// （未割当だと全 Scene 検索へ落ち、2 Area 在留中に隣の主人公を拾いうる）。
        /// </summary>
        private static void ValidateCombatVfx(Scene scene, List<string> errors)
        {
            List<Momotaro.Presentation.Combat.PlayerSlashVfxPresenter> slashes =
                Phase5ExplorationValidator.Components<Momotaro.Presentation.Combat.PlayerSlashVfxPresenter>(scene);
            if (slashes.Count != 1)
            {
                errors.Add(scene.path + ": 主人公の剣閃（PlayerSlashVfxPresenter）が " + slashes.Count + " 個です（1 個であること。P6A 27）。");
            }

            foreach (var slash in slashes)
            {
                CheckSet(scene, "1 段目", slash.Stage1Frames, errors);
                CheckSet(scene, "2 段目", slash.Stage2Frames, errors);
                CheckSet(scene, "3 段目", slash.Stage3Frames, errors);
                CheckSet(scene, "必殺技", slash.SpecialFrames, errors);
                var so = new SerializedObject(slash);
                Object player = so.FindProperty("_player")?.objectReferenceValue;
                if (!(player is Component c) || c.gameObject.scene != scene)
                {
                    errors.Add(scene.path + ": 剣閃がこの Area の主人公を見ていません（_player 未割当または別 Scene）。");
                }
            }

            RequireExactlyOne<Momotaro.Presentation.Combat.EnemySlashVfxPresenter>(scene, "敵の剣閃", errors);
            RequireExactlyOne<Momotaro.Presentation.Combat.EnemyUnblockableWarningPresenter>(scene, "ガード不能の警告", errors);
            RequireExactlyOne<Momotaro.Presentation.Combat.JustGuardVfxPresenter>(scene, "ジャストガードの閃光", errors);

            // 表示役は配信役を購読して初めて出る。配信役が無い Area では JG 閃光・手応えが一切出ない（A で実際に起きた）。
            RequireExactlyOne<Momotaro.Presentation.Diagnostics.CombatFeedbackDispatcher>(scene, "命中 Feedback の配信役", errors);
        }

        private static void CheckSet(Scene scene, string label,
            Momotaro.Presentation.Combat.PlayerSlashVfxPresenter.SlashFrameSet set, List<string> errors)
        {
            if (set == null || Empty(set.down) || Empty(set.up) || Empty(set.left) || Empty(set.right))
            {
                errors.Add(scene.path + ": 剣閃の " + label + " の素材が欠けています（方向のどれかが空）。");
            }
        }

        private static bool Empty(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0)
            {
                return true;
            }

            foreach (Sprite f in frames)
            {
                if (f == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RequireExactlyOne<T>(Scene scene, string label, List<string> errors) where T : Component
        {
            int n = Phase5ExplorationValidator.Components<T>(scene).Count;
            if (n != 1)
            {
                errors.Add(scene.path + ": " + label + "（" + typeof(T).Name + "）が " + n + " 個です（1 個であること。P6A 27）。");
            }
        }

        /// <summary>Area Data の保存対象一覧が Scene の実在と一致すること（読み込みの検査がこの一覧を正本にするため）。</summary>
        private static void CompareManifest(AreaDefinition area, AreaContentFacts facts, List<string> errors)
        {
            AreaContentManifest m = area.Content;
            CompareSet(area, "遭遇戦", m.Encounters, facts.Encounters, errors);
            CompareSet(area, "ボス", m.Bosses, facts.Bosses, errors);
            CompareSet(area, "普通敵の配置", m.FieldPlacements, facts.FieldPlacements, errors);
            CompareSet(area, "配置物", m.Pickups, facts.Pickups, errors);
            CompareSet(area, "フラグ", m.Flags, facts.Flags, errors);
            CompareSet(area, "調査地点", m.InvestigationPoints, facts.InvestigationPoints, errors);
        }

        private static void CompareSet(AreaDefinition area, string label, IReadOnlyList<StableId> declared,
            HashSet<string> actual, List<string> errors)
        {
            var declaredSet = new HashSet<string>();
            foreach (StableId id in declared)
            {
                declaredSet.Add(id.Value);
                if (!actual.Contains(id.Value))
                {
                    errors.Add(area.Id.Value + ": 保存対象一覧の" + label + " '" + id.Value
                        + "' が Scene にありません（保存しても復元先が無い）。");
                }
            }

            foreach (string id in actual)
            {
                if (!declaredSet.Contains(id))
                {
                    errors.Add(area.Id.Value + ": Scene の" + label + " '" + id
                        + "' が保存対象一覧にありません（読み込みで未知の ID として断られる）。");
                }
            }
        }

        /// <summary>お地蔵様：入口と像が実在し、近く、戦闘区域の外にある（休息・再開の安全地点）。</summary>
        private static void ValidateShrines(CampaignCatalog campaign, Dictionary<string, AreaContentFacts> areas,
            List<string> errors)
        {
            var declared = new HashSet<string>();
            foreach (ShrineInfo shrine in campaign.Shrines)
            {
                declared.Add(shrine.ShrineId.Value);
                if (!areas.TryGetValue(shrine.AreaId.Value, out AreaContentFacts facts))
                {
                    errors.Add("お地蔵様 " + shrine.ShrineId.Value + " のエリア '" + shrine.AreaId.Value + "' を検査できません。");
                    continue;
                }

                if (!facts.Shrines.TryGetValue(shrine.ShrineId.Value, out Vector3 statue))
                {
                    errors.Add("お地蔵様 " + shrine.ShrineId.Value + " の ShrinePoint が " + facts.ScenePath + " にありません。");
                    continue;
                }

                if (!facts.Entries.TryGetValue(shrine.Entry.EntryId.Value, out Vector3 entry))
                {
                    errors.Add("お地蔵様 " + shrine.ShrineId.Value + " の入口 '" + shrine.Entry.EntryId.Value
                        + "' が " + facts.ScenePath + " にありません。");
                    continue;
                }

                float distance = Vector2.Distance(new Vector2(statue.x, statue.z), new Vector2(entry.x, entry.z));
                if (distance > 3f)
                {
                    errors.Add("お地蔵様 " + shrine.ShrineId.Value + " の入口が像から離れすぎています（" + distance + "m）。");
                }

                foreach (Bounds arena in facts.Arenas)
                {
                    var flat = new Vector3(entry.x, arena.center.y, entry.z);
                    if (arena.Contains(flat))
                    {
                        errors.Add("お地蔵様 " + shrine.ShrineId.Value + " の入口が戦闘区域の中にあります（再開直後に戦闘へ入る）。");
                    }
                }
            }

            foreach (KeyValuePair<string, AreaContentFacts> area in areas)
            {
                foreach (string shrineId in area.Value.Shrines.Keys)
                {
                    if (!declared.Contains(shrineId))
                    {
                        errors.Add(area.Value.ScenePath + ": ShrinePoint '" + shrineId + "' がカタログのお地蔵様一覧にありません。");
                    }
                }
            }
        }

        /// <summary>保存 ID が campaign 内で一意（別エリアの同じ ID は保存データ上で区別できない）。</summary>
        private static void ValidateUniqueness(AreaCatalogData data, Dictionary<string, AreaContentFacts> areas,
            List<string> errors)
        {
            var owner = new Dictionary<string, string>();
            foreach (KeyValuePair<string, AreaContentFacts> area in areas)
            {
                AreaContentFacts f = area.Value;
                foreach (HashSet<string> set in new[] { f.Encounters, f.FieldPlacements, f.Pickups, f.InvestigationPoints })
                {
                    foreach (string id in set)
                    {
                        if (owner.TryGetValue(id, out string other) && other != area.Key)
                        {
                            errors.Add("保存 ID '" + id + "' が " + other + " と " + area.Key + " の両方にあります。");
                        }

                        owner[id] = area.Key;
                    }
                }
            }
        }
    }
}
