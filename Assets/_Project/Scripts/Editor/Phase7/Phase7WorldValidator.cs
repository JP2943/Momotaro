using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Progression;
using Momotaro.Data.Story;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using Momotaro.Editor.Phase6;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase7
{
    /// <summary>
    /// P7 の検証ワールドの検査（P7 仕様 §10・§17）。P6C と同じ配置・campaign・成長・戦闘の検査に加えて、
    /// 会話 Data（<see cref="StoryDataCheck"/> と campaign の構築時の実在検査）と、Scene の配線を見る：
    /// 住民が Data と 1 対 1 で所属 Area に居ること、必須イベントの門（仕掛け）と立て札が所属 Area にあること、
    /// 扉の行き先が解決できること、依頼報酬が試遊値（30）であること、H が Build Settings にあること。
    /// Builder を再生成した後も参照と操作が有効か（P7 17）をここで固定する。
    /// </summary>
    public static class Phase7WorldValidator
    {
        [MenuItem("Momotaro/Phase 7/Validate Dialogue Quest World")]
        private static void ValidateInteractive()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Validate(errors, warnings);
            EditorUtility.DisplayDialog("P7 検証ワールドの検査",
                errors.Count == 0
                    ? "検査を通りました（警告 " + warnings.Count + " 件）。"
                    : "エラー " + errors.Count + " 件。\n" + string.Join("\n", errors), "OK");
        }

        /// <summary>全部見る。</summary>
        public static void Validate(List<string> errors, List<string> warnings)
        {
            Phase6Profile previous = Phase6WorldIds.Profile;
            Phase6WorldIds.Profile = Phase6Profile.P7;
            try
            {
                Phase6WorldValidator.Validate(errors, warnings);
                int start = errors.Count;
                Phase6WorldValidator.ValidateP6BCampaign(errors);
                Phase6WorldValidator.ValidateP6CCombat(errors);
                ValidateStory(errors);
                for (int i = start; i < errors.Count; i++)
                {
                    errors[i] = "【P7】" + errors[i];
                }
            }
            finally
            {
                Phase6WorldIds.Profile = previous;
            }
        }

        private static void ValidateStory(List<string> errors)
        {
            var data = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase6WorldIds.CatalogDataPath);
            if (data == null || data.Story == null)
            {
                errors.Add("カタログに会話 Data（Story）がありません。");
                return;
            }

            if (AssetDatabase.GetAssetPath(data.Story) != Phase7WorldIds.StoryDataPath)
            {
                errors.Add("カタログの会話 Data が P7 の Data ではありません（" + AssetDatabase.GetAssetPath(data.Story) + "）。");
            }

            var dataErrors = new List<string>();
            if (!StoryDataCheck.Check(data.Story, dataErrors))
            {
                foreach (string e in dataErrors)
                {
                    errors.Add("会話 Data: " + e);
                }
            }

            if (!AreaCatalog.TryBuild(data, out AreaCatalog catalog, out IReadOnlyList<string> buildErrors)
                || catalog.Campaign == null || catalog.Campaign.Story == null)
            {
                foreach (string e in buildErrors ?? new List<string>())
                {
                    errors.Add("会話 Data を campaign として組めません: " + e);
                }

                return;
            }

            StoryCatalog story = catalog.Campaign.Story;
            if (story.Quests.Count != 3)
            {
                errors.Add("依頼が " + story.Quests.Count + " 件です（試遊は到達・発見・遭遇戦の 3 件）。");
            }

            var kinds = new HashSet<QuestObjectiveKind>();
            bool hasMulti = false;
            foreach (QuestInfo quest in story.Quests)
            {
                if (quest.Reward.VirtueAmount != Phase7TrialValues.QuestVirtue)
                {
                    errors.Add("依頼 '" + quest.QuestId.Value + "' の報酬が " + quest.Reward.VirtueAmount + " 徳です（試遊の初期値は "
                        + Phase7TrialValues.QuestVirtue + "）。");
                }

                hasMulti |= quest.Objectives.Count > 1;
                foreach (QuestObjectiveInfo o in quest.Objectives)
                {
                    kinds.Add(o.Kind);
                }
            }

            if (!kinds.Contains(QuestObjectiveKind.AreaReached) || !kinds.Contains(QuestObjectiveKind.Discovery)
                || !kinds.Contains(QuestObjectiveKind.EncounterCleared) || !hasMulti)
            {
                errors.Add("依頼の条件が到達・発見・遭遇戦の 3 種類と複数条件の依頼を揃えていません。");
            }

            if (story.Chapters.Count != 1)
            {
                errors.Add("章が " + story.Chapters.Count + " 件です（試遊は 1 章）。");
            }

            // ---- Scene の配線 ----
            var villagersByArea = new Dictionary<string, List<VillagerPoint>>();
            var noticesByArea = new Dictionary<string, List<StoryGateNotice>>();
            var flagsByArea = new Dictionary<string, HashSet<string>>();
            var doorProblems = new List<string>();
            foreach (AreaDefinition area in data.Areas)
            {
                if (area == null || !catalog.TryGetScenePath(area.Id, out string scenePath)
                    || AssetDatabase.LoadAssetAtPath<Object>(scenePath) == null)
                {
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                villagersByArea[area.Id.Value] = Phase5ExplorationValidator.Components<VillagerPoint>(scene);
                noticesByArea[area.Id.Value] = Phase5ExplorationValidator.Components<StoryGateNotice>(scene);
                var flags = new HashSet<string>();
                foreach (AreaFlagDoor door in Phase5ExplorationValidator.Components<AreaFlagDoor>(scene))
                {
                    flags.Add(door.FlagId.Value);
                }

                flagsByArea[area.Id.Value] = flags;
                foreach (AreaTransitionDoor door in Phase5ExplorationValidator.Components<AreaTransitionDoor>(scene))
                {
                    if (!catalog.TryGetEntry(door.DestinationAreaId, door.DestinationEntryId, out _))
                    {
                        doorProblems.Add(scenePath + " の扉の行き先 '" + door.DestinationAreaId.Value + "/" + door.DestinationEntryId.Value
                            + "' を解決できません。");
                    }

                    if (!door.AreaId.Equals(area.Id))
                    {
                        doorProblems.Add(scenePath + " の扉の所属 Area が違います（" + door.AreaId.Value + "）。");
                    }
                }
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            errors.AddRange(doorProblems);

            var seenVillagers = new Dictionary<string, int>();
            foreach (KeyValuePair<string, List<VillagerPoint>> pair in villagersByArea)
            {
                foreach (VillagerPoint point in pair.Value)
                {
                    seenVillagers.TryGetValue(point.VillagerId.Value, out int n);
                    seenVillagers[point.VillagerId.Value] = n + 1;
                    if (!story.TryGetVillager(point.VillagerId, out VillagerInfo info))
                    {
                        errors.Add("Area '" + pair.Key + "' の住民 '" + point.VillagerId.Value + "' が会話 Data にありません。");
                    }
                    else if (!info.AreaId.Value.Equals(pair.Key) || !point.AreaId.Value.Equals(pair.Key))
                    {
                        errors.Add("住民 '" + point.VillagerId.Value + "' の所属 Area が Data と Scene で違います。");
                    }
                }
            }

            foreach (StableId id in new[] { Phase7WorldIds.Guide, Phase7WorldIds.Giver })
            {
                seenVillagers.TryGetValue(id.Value, out int n);
                if (n != 1)
                {
                    errors.Add("住民 '" + id.Value + "' が Scene に " + n + " 人います（1 人であること）。");
                }
            }

            foreach (StoryEventInfo e in story.Events)
            {
                if (!e.OpensFlag)
                {
                    continue;
                }

                string area = e.OpensAreaId.Value;
                if (!(flagsByArea.TryGetValue(area, out HashSet<string> flags) && flags.Contains(e.OpensFlagId.Value)))
                {
                    errors.Add("必須イベント '" + e.EventId.Value + "' が開ける門 '" + e.OpensFlagId.Value + "' が '" + area + "' にありません。");
                }

                int notices = 0;
                if (noticesByArea.TryGetValue(area, out List<StoryGateNotice> list))
                {
                    foreach (StoryGateNotice n in list)
                    {
                        if (n.EventId.Equals(e.EventId))
                        {
                            notices++;
                        }
                    }
                }

                if (notices != 1)
                {
                    errors.Add("必須イベント '" + e.EventId.Value + "' の立て札が '" + area + "' に " + notices + " 個です（1 個であること）。");
                }
            }

            bool inBuild = false;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                inBuild |= s.enabled && s.path == Phase7WorldIds.AreaHScenePath;
            }

            if (!inBuild)
            {
                errors.Add("エリア H が Build Settings にありません: " + Phase7WorldIds.AreaHScenePath);
            }
        }
    }
}
