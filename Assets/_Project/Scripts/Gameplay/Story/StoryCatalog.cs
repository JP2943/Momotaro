using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Story;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Story
{
    /// <summary>住民（不変。P7）。</summary>
    public sealed class VillagerInfo
    {
        internal VillagerInfo(StableId villagerId, string displayName, StableId areaId)
        {
            VillagerId = villagerId;
            DisplayName = displayName ?? string.Empty;
            AreaId = areaId;
        }

        public StableId VillagerId { get; }
        public string DisplayName { get; }
        public StableId AreaId { get; }
    }

    /// <summary>会話の候補（不変。P7）。条件は Data の <see cref="StoryCondition"/>（読み取り専用の値）をそのまま持つ。</summary>
    public sealed class DialogueInfo
    {
        internal DialogueInfo(DialogueDefinition d)
        {
            DialogueId = d.DialogueId;
            VillagerId = d.VillagerId;
            Priority = d.Priority;
            Speaker = d.Speaker;
            Pages = Copy(d.Pages);
            Conditions = d.Conditions != null ? new List<StoryCondition>(d.Conditions).ToArray() : Array.Empty<StoryCondition>();
            CompletesEventId = d.CompletesEventId;
            ConfirmLabel = d.ConfirmLabel;
        }

        public StableId DialogueId { get; }
        public StableId VillagerId { get; }
        public int Priority { get; }
        public string Speaker { get; }
        public IReadOnlyList<string> Pages { get; }
        public IReadOnlyList<StoryCondition> Conditions { get; }
        public StableId CompletesEventId { get; }
        public string ConfirmLabel { get; }

        internal static string[] Copy(IReadOnlyList<string> source)
        {
            if (source == null)
            {
                return Array.Empty<string>();
            }

            var list = new string[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                list[i] = source[i] ?? string.Empty;
            }

            return list;
        }
    }

    /// <summary>依頼の達成条件（不変。P7）。発見は対象が配置物か調査点かを campaign の構築時に決める。</summary>
    public readonly struct QuestObjectiveInfo
    {
        internal QuestObjectiveInfo(QuestObjectiveKind kind, StableId areaId, StableId targetId, bool discoveryIsInvestigation,
            string label)
        {
            Kind = kind;
            AreaId = areaId;
            TargetId = targetId;
            DiscoveryIsInvestigation = discoveryIsInvestigation;
            Label = label ?? string.Empty;
        }

        public QuestObjectiveKind Kind { get; }
        public StableId AreaId { get; }
        public StableId TargetId { get; }

        /// <summary>発見の対象が調査点か（false なら配置物）。</summary>
        public bool DiscoveryIsInvestigation { get; }

        public string Label { get; }
    }

    /// <summary>依頼（不変。P7）。</summary>
    public sealed class QuestInfo
    {
        internal QuestInfo(QuestDefinition q, QuestObjectiveInfo[] objectives)
        {
            QuestId = q.QuestId;
            DisplayName = q.DisplayName;
            GiverVillagerId = q.GiverVillagerId;
            ObjectiveText = q.ObjectiveText;
            OfferPages = DialogueInfo.Copy(q.OfferPages);
            ProgressText = q.ProgressText;
            ReportText = q.ReportText;
            Reward = RewardSnapshot.From(q.Reward);
            Objectives = objectives;
        }

        public StableId QuestId { get; }
        public string DisplayName { get; }
        public StableId GiverVillagerId { get; }
        public string ObjectiveText { get; }
        public IReadOnlyList<string> OfferPages { get; }
        public string ProgressText { get; }
        public string ReportText { get; }

        /// <summary>依頼報酬（既存の報酬台帳へ GrantOnce で付与する）。</summary>
        public RewardSnapshot Reward { get; }

        public IReadOnlyList<QuestObjectiveInfo> Objectives { get; }
    }

    /// <summary>必須イベント（不変。P7）。</summary>
    public sealed class StoryEventInfo
    {
        internal StoryEventInfo(StoryEventDefinition e)
        {
            EventId = e.EventId;
            DisplayName = e.DisplayName;
            OpensAreaId = e.OpensAreaId;
            OpensFlagId = e.OpensFlagId;
            LockedNotice = e.LockedNotice;
        }

        public StableId EventId { get; }
        public string DisplayName { get; }
        public StableId OpensAreaId { get; }
        public StableId OpensFlagId { get; }
        public string LockedNotice { get; }
        public bool OpensFlag => !OpensFlagId.IsEmpty;
    }

    /// <summary>章の 1 経路（不変。P7）。</summary>
    public sealed class ChapterRouteInfo
    {
        private readonly HashSet<string> _areas = new HashSet<string>();

        internal ChapterRouteInfo(ChapterRouteDefinition r)
        {
            for (int i = 0; i < r.Areas.Count; i++)
            {
                _areas.Add(r.Areas[i].Value);
            }

            TerminalAreaId = r.TerminalAreaId;
            TerminalEntryId = r.TerminalEntryId;
        }

        public StableId TerminalAreaId { get; }
        public StableId TerminalEntryId { get; }
        public bool Contains(StableId areaId) => !areaId.IsEmpty && _areas.Contains(areaId.Value);
        public bool IsTerminal(StableId areaId, StableId entryId) =>
            TerminalAreaId.Equals(areaId) && TerminalEntryId.Equals(entryId);
    }

    /// <summary>章（不変。P7）。</summary>
    public sealed class ChapterInfo
    {
        private readonly HashSet<string> _bossFrontEntries = new HashSet<string>();

        internal ChapterInfo(ChapterDefinition c)
        {
            ChapterId = c.ChapterId;
            DisplayName = c.DisplayName;
            BossAreaId = c.BossAreaId;
            BossId = c.BossId;
            Standard = new ChapterRouteInfo(c.Standard);
            Hard = new ChapterRouteInfo(c.Hard);
            BossFrontAreaId = c.BossFrontAreaId;
            for (int i = 0; i < c.BossFrontEntryIds.Count; i++)
            {
                _bossFrontEntries.Add(c.BossFrontEntryIds[i].Value);
            }
        }

        public StableId ChapterId { get; }
        public string DisplayName { get; }
        public StableId BossAreaId { get; }
        public StableId BossId { get; }
        public ChapterRouteInfo Standard { get; }
        public ChapterRouteInfo Hard { get; }
        public StableId BossFrontAreaId { get; }

        public ChapterRouteInfo RouteOf(StoryRoute route) =>
            route == StoryRoute.Standard ? Standard : route == StoryRoute.Hard ? Hard : null;

        /// <summary>ボス前の地点へ、通常の移動で入る入口か。</summary>
        public bool IsBossFrontArrival(StableId areaId, StableId entryId) =>
            BossFrontAreaId.Equals(areaId) && !entryId.IsEmpty && _bossFrontEntries.Contains(entryId.Value);
    }

    /// <summary>
    /// campaign の会話・依頼・イベント・章（不変。P7。<see cref="CampaignCatalog.Story"/>）。
    ///
    /// <b>Data を実行時に書き換えない。</b> 構築時に読み取り専用の値へ写し、以後は Data を見ない。
    /// 構築は <b>1 件でも不整合があれば作らない</b>（<see cref="CampaignCatalog"/> と同じ方針）。
    /// </summary>
    public sealed class StoryCatalog
    {
        private readonly Dictionary<string, VillagerInfo> _villagers = new Dictionary<string, VillagerInfo>();
        private readonly Dictionary<string, List<DialogueInfo>> _dialogues = new Dictionary<string, List<DialogueInfo>>();
        private readonly Dictionary<string, QuestInfo> _quests = new Dictionary<string, QuestInfo>();
        private readonly List<QuestInfo> _questOrder = new List<QuestInfo>();
        private readonly Dictionary<string, StoryEventInfo> _events = new Dictionary<string, StoryEventInfo>();
        private readonly List<StoryEventInfo> _eventOrder = new List<StoryEventInfo>();
        private readonly Dictionary<string, ChapterInfo> _chapters = new Dictionary<string, ChapterInfo>();
        private readonly List<ChapterInfo> _chapterOrder = new List<ChapterInfo>();
        private readonly Dictionary<string, ChapterInfo> _chapterByBoss = new Dictionary<string, ChapterInfo>();
        private readonly HashSet<string> _allTextCharacters = new HashSet<string>();

        private StoryCatalog()
        {
        }

        public IReadOnlyList<QuestInfo> Quests => _questOrder;
        public IReadOnlyList<StoryEventInfo> Events => _eventOrder;
        public IReadOnlyList<ChapterInfo> Chapters => _chapterOrder;

        /// <summary>表示に使う全文字（仮 UI の文字の準備用。P6A F04 と同じ理由）。</summary>
        public string TextCharacters { get; private set; } = string.Empty;

        public bool TryGetVillager(StableId id, out VillagerInfo villager)
        {
            villager = null;
            return !id.IsEmpty && _villagers.TryGetValue(id.Value, out villager);
        }

        /// <summary>住民の会話の候補（定義順）。無ければ空。</summary>
        public IReadOnlyList<DialogueInfo> DialoguesOf(StableId villagerId)
        {
            return !villagerId.IsEmpty && _dialogues.TryGetValue(villagerId.Value, out List<DialogueInfo> list)
                ? (IReadOnlyList<DialogueInfo>)list
                : Array.Empty<DialogueInfo>();
        }

        public bool TryGetQuest(StableId id, out QuestInfo quest)
        {
            quest = null;
            return !id.IsEmpty && _quests.TryGetValue(id.Value, out quest);
        }

        public bool TryGetEvent(StableId id, out StoryEventInfo info)
        {
            info = null;
            return !id.IsEmpty && _events.TryGetValue(id.Value, out info);
        }

        public bool TryGetChapter(StableId id, out ChapterInfo chapter)
        {
            chapter = null;
            return !id.IsEmpty && _chapters.TryGetValue(id.Value, out chapter);
        }

        /// <summary>その Area のその遭遇戦（ボス ID）を章ボスとする章を引く。</summary>
        public bool TryGetChapterByBoss(StableId areaId, StableId bossId, out ChapterInfo chapter)
        {
            chapter = null;
            return !areaId.IsEmpty && !bossId.IsEmpty
                && _chapterByBoss.TryGetValue(areaId.Value + "/" + bossId.Value, out chapter);
        }

        /// <summary>その住民が依頼主の依頼（定義順）。</summary>
        public void CopyQuestsOf(StableId villagerId, List<QuestInfo> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < _questOrder.Count; i++)
            {
                if (_questOrder[i].GiverVillagerId.Equals(villagerId))
                {
                    buffer.Add(_questOrder[i]);
                }
            }
        }

        /// <summary>
        /// Data から構築する（P7）。Data の中の検査（<see cref="StoryDataCheck"/>）に加えて、Area・入口・遭遇戦・配置物・調査点・
        /// 仕掛け・ボスの実在を <paramref name="areas"/> と <paramref name="content"/> で検査する。
        /// </summary>
        internal static StoryCatalog Build(CampaignStoryData data, AreaCatalog areas,
            Func<StableId, AreaContentIds> content, List<string> errors)
        {
            int before = errors.Count;
            if (!StoryDataCheck.Check(data, errors))
            {
                return null;
            }

            var built = new StoryCatalog();
            var chars = new System.Text.StringBuilder();

            foreach (VillagerDefinition v in data.Villagers)
            {
                RequireArea(areas, v.AreaId, "villager '" + v.VillagerId.Value + "'", errors);
                built._villagers.Add(v.VillagerId.Value, new VillagerInfo(v.VillagerId, v.DisplayName, v.AreaId));
                chars.Append(v.DisplayName);
            }

            foreach (DialogueDefinition d in data.Dialogues)
            {
                var info = new DialogueInfo(d);
                if (!built._dialogues.TryGetValue(d.VillagerId.Value, out List<DialogueInfo> list))
                {
                    list = new List<DialogueInfo>();
                    built._dialogues.Add(d.VillagerId.Value, list);
                }

                list.Add(info);
                chars.Append(info.Speaker).Append(info.ConfirmLabel);
                for (int i = 0; i < info.Pages.Count; i++)
                {
                    chars.Append(info.Pages[i]);
                }
            }

            foreach (QuestDefinition q in data.Quests)
            {
                var objectives = new QuestObjectiveInfo[q.Objectives.Count];
                for (int i = 0; i < q.Objectives.Count; i++)
                {
                    QuestObjective o = q.Objectives[i];
                    string owner = "quest '" + q.QuestId.Value + "' objective " + i;
                    bool investigation = false;
                    if (RequireArea(areas, o.AreaId, owner, errors))
                    {
                        AreaContentIds ids = content(o.AreaId);
                        switch (o.Kind)
                        {
                            case QuestObjectiveKind.Discovery:
                                bool pickup = ids != null && ids.Pickups.Contains(o.TargetId.Value);
                                investigation = ids != null && ids.InvestigationPoints.Contains(o.TargetId.Value);
                                if (!pickup && !investigation)
                                {
                                    errors.Add(owner + " target '" + o.TargetId.Value + "' is neither a pickup nor an investigation point of '"
                                        + o.AreaId.Value + "'.");
                                }

                                break;
                            case QuestObjectiveKind.EncounterCleared:
                                if (ids == null || !ids.Encounters.Contains(o.TargetId.Value))
                                {
                                    errors.Add(owner + " encounter '" + o.TargetId.Value + "' is not in '" + o.AreaId.Value + "'.");
                                }

                                break;
                        }
                    }

                    objectives[i] = new QuestObjectiveInfo(o.Kind, o.AreaId, o.TargetId, investigation, o.Label);
                    chars.Append(o.Label);
                }

                var quest = new QuestInfo(q, objectives);
                built._quests.Add(q.QuestId.Value, quest);
                built._questOrder.Add(quest);
                chars.Append(quest.DisplayName).Append(quest.ObjectiveText).Append(quest.ProgressText).Append(quest.ReportText);
                for (int i = 0; i < quest.OfferPages.Count; i++)
                {
                    chars.Append(quest.OfferPages[i]);
                }
            }

            foreach (StoryEventDefinition e in data.Events)
            {
                if (e.OpensFlag && RequireArea(areas, e.OpensAreaId, "event '" + e.EventId.Value + "'", errors))
                {
                    AreaContentIds ids = content(e.OpensAreaId);
                    if (ids == null || !ids.Flags.Contains(e.OpensFlagId.Value))
                    {
                        errors.Add("event '" + e.EventId.Value + "' flag '" + e.OpensFlagId.Value + "' is not in '"
                            + e.OpensAreaId.Value + "'.");
                    }
                }

                var info = new StoryEventInfo(e);
                built._events.Add(e.EventId.Value, info);
                built._eventOrder.Add(info);
                chars.Append(info.DisplayName).Append(info.LockedNotice);
            }

            foreach (ChapterDefinition c in data.Chapters)
            {
                string owner = "chapter '" + c.ChapterId.Value + "'";
                if (RequireArea(areas, c.BossAreaId, owner + " boss", errors))
                {
                    AreaContentIds ids = content(c.BossAreaId);
                    if (ids == null || !ids.Bosses.Contains(c.BossId.Value) || !ids.Encounters.Contains(c.BossId.Value))
                    {
                        errors.Add(owner + " boss '" + c.BossId.Value + "' is not a boss encounter of '" + c.BossAreaId.Value + "'.");
                    }
                }

                CheckRouteRefs(areas, owner + " standard", c.Standard, errors);
                CheckRouteRefs(areas, owner + " hard", c.Hard, errors);
                for (int i = 0; i < c.BossFrontEntryIds.Count; i++)
                {
                    if (!areas.TryGetEntry(c.BossFrontAreaId, c.BossFrontEntryIds[i], out _))
                    {
                        errors.Add(owner + " boss-front entry '" + c.BossFrontAreaId.Value + "/" + c.BossFrontEntryIds[i].Value
                            + "' cannot be resolved.");
                    }
                }

                var info = new ChapterInfo(c);
                built._chapters.Add(c.ChapterId.Value, info);
                built._chapterOrder.Add(info);
                string key = c.BossAreaId.Value + "/" + c.BossId.Value;
                if (built._chapterByBoss.ContainsKey(key))
                {
                    errors.Add(owner + " shares its boss with another chapter.");
                }
                else
                {
                    built._chapterByBoss.Add(key, info);
                }

                chars.Append(info.DisplayName);
            }

            foreach (char ch in chars.ToString())
            {
                built._allTextCharacters.Add(ch.ToString());
            }

            var sb = new System.Text.StringBuilder();
            foreach (string ch in built._allTextCharacters)
            {
                sb.Append(ch);
            }

            built.TextCharacters = sb.ToString();
            return errors.Count == before ? built : null;
        }

        private static void CheckRouteRefs(AreaCatalog areas, string owner, ChapterRouteDefinition route, List<string> errors)
        {
            for (int i = 0; i < route.Areas.Count; i++)
            {
                RequireArea(areas, route.Areas[i], owner, errors);
            }

            if (!areas.TryGetEntry(route.TerminalAreaId, route.TerminalEntryId, out _))
            {
                errors.Add(owner + " terminal entry '" + route.TerminalAreaId.Value + "/" + route.TerminalEntryId.Value
                    + "' cannot be resolved.");
            }
        }

        private static bool RequireArea(AreaCatalog areas, StableId areaId, string owner, List<string> errors)
        {
            if (areas.TryGetScenePath(areaId, out _))
            {
                return true;
            }

            errors.Add(owner + " refers to unknown area '" + areaId.Value + "'.");
            return false;
        }
    }
}
