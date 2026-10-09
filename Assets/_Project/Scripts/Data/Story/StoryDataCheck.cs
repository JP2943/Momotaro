using System.Collections.Generic;
using Momotaro.Core.Identification;

namespace Momotaro.Data.Story
{
    /// <summary>
    /// <see cref="CampaignStoryData"/> の中だけで閉じる検査（P7。仕様 §10 の Validator の Data 部分）。
    ///
    /// 検出するもの：ID の重複・不正、未知参照（Data 内の住民・会話・依頼・イベント・章）、空本文・長すぎるページ、
    /// 住民ごとの無条件フォールバックの不足、同じ住民で同じ優先度の候補（曖昧）、無条件より低い優先度の候補（到達しない）、
    /// 負の報酬・一度きりでない報酬、空の達成条件、章とボスの対応の欠け、自己依存して完了できない必須イベント。
    /// Area・入口・遭遇戦・配置物の実在は campaign を組むとき（<c>StoryCatalog</c>）に見る。
    /// </summary>
    public static class StoryDataCheck
    {
        /// <summary>検査して、見つけた問題を <paramref name="errors"/> に足す。問題が無ければ true。</summary>
        public static bool Check(CampaignStoryData data, List<string> errors)
        {
            int before = errors.Count;
            if (data == null)
            {
                errors.Add("story data is null.");
                return false;
            }

            var villagers = new HashSet<string>();
            foreach (VillagerDefinition v in data.Villagers)
            {
                if (v == null || !v.VillagerId.IsValid)
                {
                    errors.Add("villager has an invalid id.");
                    continue;
                }

                if (!villagers.Add(v.VillagerId.Value))
                {
                    errors.Add("duplicate villager id '" + v.VillagerId.Value + "'.");
                }

                if (string.IsNullOrWhiteSpace(v.DisplayName))
                {
                    errors.Add("villager '" + v.VillagerId.Value + "' has no display name.");
                }

                if (!v.AreaId.IsValid)
                {
                    errors.Add("villager '" + v.VillagerId.Value + "' has an invalid area id.");
                }
            }

            var chapters = new HashSet<string>();
            foreach (ChapterDefinition c in data.Chapters)
            {
                CheckChapter(c, chapters, errors);
            }

            var events = new Dictionary<string, StoryEventDefinition>();
            foreach (StoryEventDefinition e in data.Events)
            {
                if (e == null || !e.EventId.IsValid)
                {
                    errors.Add("event has an invalid id.");
                    continue;
                }

                if (events.ContainsKey(e.EventId.Value))
                {
                    errors.Add("duplicate event id '" + e.EventId.Value + "'.");
                    continue;
                }

                events.Add(e.EventId.Value, e);
                if (e.OpensFlag && (!e.OpensAreaId.IsValid || !e.OpensFlagId.IsValid))
                {
                    errors.Add("event '" + e.EventId.Value + "' opens an invalid area/flag.");
                }

                if (string.IsNullOrWhiteSpace(e.LockedNotice))
                {
                    errors.Add("event '" + e.EventId.Value + "' has no locked notice.");
                }
            }

            var quests = new HashSet<string>();
            var rewards = new HashSet<string>();
            foreach (QuestDefinition q in data.Quests)
            {
                CheckQuest(q, villagers, quests, rewards, data.MaxPageLength, errors);
            }

            CheckDialogues(data, villagers, quests, events, chapters, errors);
            return errors.Count == before;
        }

        private static void CheckChapter(ChapterDefinition c, HashSet<string> chapters, List<string> errors)
        {
            if (c == null || !c.ChapterId.IsValid)
            {
                errors.Add("chapter has an invalid id.");
                return;
            }

            string id = c.ChapterId.Value;
            if (!chapters.Add(id))
            {
                errors.Add("duplicate chapter id '" + id + "'.");
            }

            if (!c.BossAreaId.IsValid || !c.BossId.IsValid)
            {
                errors.Add("chapter '" + id + "' has no valid boss (area/boss id).");
            }

            CheckRoute(id, "standard", c.Standard, errors);
            CheckRoute(id, "hard", c.Hard, errors);
            if (c.Standard != null && c.Hard != null && c.Standard.TerminalEntryId.Equals(c.Hard.TerminalEntryId)
                && c.Standard.TerminalAreaId.Equals(c.Hard.TerminalAreaId))
            {
                errors.Add("chapter '" + id + "' standard and hard routes share the same terminal entry.");
            }

            if (!c.BossFrontAreaId.IsValid || c.BossFrontEntryIds.Count == 0)
            {
                errors.Add("chapter '" + id + "' has no boss-front area/entry.");
            }

            for (int i = 0; i < c.BossFrontEntryIds.Count; i++)
            {
                if (!c.BossFrontEntryIds[i].IsValid)
                {
                    errors.Add("chapter '" + id + "' has an invalid boss-front entry id.");
                }
            }
        }

        private static void CheckRoute(string chapterId, string label, ChapterRouteDefinition route, List<string> errors)
        {
            if (route == null || route.Areas.Count == 0)
            {
                errors.Add("chapter '" + chapterId + "' " + label + " route has no areas.");
                return;
            }

            for (int i = 0; i < route.Areas.Count; i++)
            {
                if (!route.Areas[i].IsValid)
                {
                    errors.Add("chapter '" + chapterId + "' " + label + " route has an invalid area id.");
                }
            }

            if (!route.TerminalAreaId.IsValid || !route.TerminalEntryId.IsValid)
            {
                errors.Add("chapter '" + chapterId + "' " + label + " route has no valid terminal (area/entry).");
            }
        }

        private static void CheckQuest(QuestDefinition q, HashSet<string> villagers, HashSet<string> quests,
            HashSet<string> rewards, int maxPage, List<string> errors)
        {
            if (q == null || !q.QuestId.IsValid)
            {
                errors.Add("quest has an invalid id.");
                return;
            }

            string id = q.QuestId.Value;
            if (!quests.Add(id))
            {
                errors.Add("duplicate quest id '" + id + "'.");
            }

            if (!villagers.Contains(q.GiverVillagerId.Value ?? string.Empty))
            {
                errors.Add("quest '" + id + "' giver '" + q.GiverVillagerId.Value + "' is unknown.");
            }

            if (string.IsNullOrWhiteSpace(q.DisplayName) || string.IsNullOrWhiteSpace(q.ObjectiveText))
            {
                errors.Add("quest '" + id + "' has no display name or objective text.");
            }

            CheckPages("quest '" + id + "' offer", q.OfferPages, maxPage, errors);
            if (string.IsNullOrWhiteSpace(q.ProgressText) || string.IsNullOrWhiteSpace(q.ReportText))
            {
                errors.Add("quest '" + id + "' has no progress/report text.");
            }

            if (q.Reward == null)
            {
                errors.Add("quest '" + id + "' has no reward.");
            }
            else
            {
                if (!q.Reward.Id.IsValid)
                {
                    errors.Add("quest '" + id + "' reward has an invalid id.");
                }
                else if (!rewards.Add(q.Reward.Id.Value))
                {
                    errors.Add("quest '" + id + "' reward id '" + q.Reward.Id.Value + "' is shared with another quest.");
                }

                if (q.Reward.VirtueAmount < 0)
                {
                    errors.Add("quest '" + id + "' reward is negative.");
                }

                if (!q.Reward.GrantOnce)
                {
                    errors.Add("quest '" + id + "' reward must be grant-once.");
                }
            }

            if (q.Objectives.Count == 0)
            {
                errors.Add("quest '" + id + "' has no objectives.");
            }

            for (int i = 0; i < q.Objectives.Count; i++)
            {
                QuestObjective o = q.Objectives[i];
                if (o == null || o.Kind == QuestObjectiveKind.None || !o.AreaId.IsValid)
                {
                    errors.Add("quest '" + id + "' objective " + i + " is invalid.");
                    continue;
                }

                if (o.Kind != QuestObjectiveKind.AreaReached && !o.TargetId.IsValid)
                {
                    errors.Add("quest '" + id + "' objective " + i + " has no target id.");
                }

                if (string.IsNullOrWhiteSpace(o.Label))
                {
                    errors.Add("quest '" + id + "' objective " + i + " has no label.");
                }
            }
        }

        private static void CheckDialogues(CampaignStoryData data, HashSet<string> villagers, HashSet<string> quests,
            Dictionary<string, StoryEventDefinition> events, HashSet<string> chapters, List<string> errors)
        {
            var ids = new HashSet<string>();
            var unconditional = new Dictionary<string, int>();
            var priorities = new Dictionary<string, HashSet<int>>();
            var completing = new Dictionary<string, List<DialogueDefinition>>();
            foreach (DialogueDefinition d in data.Dialogues)
            {
                if (d == null || !d.DialogueId.IsValid)
                {
                    errors.Add("dialogue has an invalid id.");
                    continue;
                }

                string id = d.DialogueId.Value;
                if (!ids.Add(id))
                {
                    errors.Add("duplicate dialogue id '" + id + "'.");
                }

                string villager = d.VillagerId.Value ?? string.Empty;
                if (!villagers.Contains(villager))
                {
                    errors.Add("dialogue '" + id + "' villager '" + villager + "' is unknown.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(d.Speaker))
                {
                    errors.Add("dialogue '" + id + "' has no speaker.");
                }

                CheckPages("dialogue '" + id + "'", d.Pages, data.MaxPageLength, errors);

                if (!priorities.TryGetValue(villager, out HashSet<int> used))
                {
                    used = new HashSet<int>();
                    priorities.Add(villager, used);
                }

                if (!used.Add(d.Priority))
                {
                    errors.Add("dialogue '" + id + "' has the same priority " + d.Priority + " as another candidate of '"
                        + villager + "' (ambiguous).");
                }

                if (d.IsUnconditional)
                {
                    if (!unconditional.TryGetValue(villager, out int best) || d.Priority > best)
                    {
                        unconditional[villager] = d.Priority;
                    }
                }

                for (int i = 0; i < d.Conditions.Count; i++)
                {
                    CheckCondition("dialogue '" + id + "'", d.Conditions[i], quests, events, chapters, errors);
                }

                if (!d.CompletesEventId.IsEmpty)
                {
                    if (!events.ContainsKey(d.CompletesEventId.Value))
                    {
                        errors.Add("dialogue '" + id + "' completes unknown event '" + d.CompletesEventId.Value + "'.");
                    }
                    else
                    {
                        if (!completing.TryGetValue(d.CompletesEventId.Value, out List<DialogueDefinition> list))
                        {
                            list = new List<DialogueDefinition>();
                            completing.Add(d.CompletesEventId.Value, list);
                        }

                        list.Add(d);
                    }

                    if (string.IsNullOrWhiteSpace(d.ConfirmLabel))
                    {
                        errors.Add("dialogue '" + id + "' completes an event but has no confirm label.");
                    }
                }
            }

            foreach (string villager in villagers)
            {
                if (!unconditional.ContainsKey(villager))
                {
                    errors.Add("villager '" + villager + "' has no unconditional (fallback) dialogue.");
                }
            }

            // 無条件の候補より優先度が低い（同じ住民の）条件付き候補は、決して選ばれない。
            foreach (DialogueDefinition d in data.Dialogues)
            {
                if (d == null || d.IsUnconditional || !d.DialogueId.IsValid)
                {
                    continue;
                }

                if (unconditional.TryGetValue(d.VillagerId.Value ?? string.Empty, out int fallback) && d.Priority < fallback)
                {
                    errors.Add("dialogue '" + d.DialogueId.Value + "' is below the unconditional fallback (never chosen).");
                }
            }

            CheckEventsSolvable(events, completing, errors);
        }

        /// <summary>
        /// 必須イベントが完了できるか（自己依存・循環依存の検出）。イベントは、それを完了させる会話のどれかが
        /// <b>完了できるイベントだけ</b>を前提（<see cref="StoryConditionKind.EventCompleted"/> の肯定）にしているときに完了できる。
        /// </summary>
        private static void CheckEventsSolvable(Dictionary<string, StoryEventDefinition> events,
            Dictionary<string, List<DialogueDefinition>> completing, List<string> errors)
        {
            var solvable = new HashSet<string>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (KeyValuePair<string, List<DialogueDefinition>> pair in completing)
                {
                    if (solvable.Contains(pair.Key))
                    {
                        continue;
                    }

                    foreach (DialogueDefinition d in pair.Value)
                    {
                        bool ok = true;
                        for (int i = 0; i < d.Conditions.Count; i++)
                        {
                            StoryCondition c = d.Conditions[i];
                            if (c != null && c.Kind == StoryConditionKind.EventCompleted && !c.Negate
                                && !solvable.Contains(c.EventId.Value ?? string.Empty))
                            {
                                ok = false;
                                break;
                            }
                        }

                        if (ok)
                        {
                            solvable.Add(pair.Key);
                            changed = true;
                            break;
                        }
                    }
                }
            }

            foreach (string id in events.Keys)
            {
                if (!completing.ContainsKey(id))
                {
                    errors.Add("event '" + id + "' has no dialogue that completes it.");
                }
                else if (!solvable.Contains(id))
                {
                    errors.Add("event '" + id + "' depends on itself (cannot be completed).");
                }
            }
        }

        private static void CheckCondition(string owner, StoryCondition c, HashSet<string> quests,
            Dictionary<string, StoryEventDefinition> events, HashSet<string> chapters, List<string> errors)
        {
            if (c == null)
            {
                errors.Add(owner + " has a null condition.");
                return;
            }

            switch (c.Kind)
            {
                case StoryConditionKind.ChapterCleared:
                    RequireKnown(owner, "chapter", c.ChapterId, chapters, errors);
                    break;
                case StoryConditionKind.QuestState:
                    RequireKnown(owner, "quest", c.QuestId, quests, errors);
                    break;
                case StoryConditionKind.EventCompleted:
                    if (!events.ContainsKey(c.EventId.Value ?? string.Empty))
                    {
                        errors.Add(owner + " refers to unknown event '" + c.EventId.Value + "'.");
                    }

                    break;
                case StoryConditionKind.RouteReached:
                case StoryConditionKind.RouteCompleted:
                    RequireKnown(owner, "chapter", c.ChapterId, chapters, errors);
                    if (c.Route == StoryRoute.None)
                    {
                        errors.Add(owner + " has a route condition without a route.");
                    }

                    break;
                default:
                    errors.Add(owner + " has a condition without a kind.");
                    break;
            }
        }

        private static void RequireKnown(string owner, string label, StableId id, HashSet<string> known, List<string> errors)
        {
            if (!known.Contains(id.Value ?? string.Empty))
            {
                errors.Add(owner + " refers to unknown " + label + " '" + id.Value + "'.");
            }
        }

        private static void CheckPages(string owner, IReadOnlyList<string> pages, int maxPage, List<string> errors)
        {
            if (pages == null || pages.Count == 0)
            {
                errors.Add(owner + " has no pages.");
                return;
            }

            for (int i = 0; i < pages.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(pages[i]))
                {
                    errors.Add(owner + " page " + i + " is empty.");
                }
                else if (pages[i].Length > maxPage)
                {
                    errors.Add(owner + " page " + i + " is longer than " + maxPage + " characters (split it).");
                }
            }
        }
    }
}
