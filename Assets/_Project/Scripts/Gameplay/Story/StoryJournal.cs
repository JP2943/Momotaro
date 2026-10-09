using System.Collections.Generic;
using Momotaro.Data.Story;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Story
{
    /// <summary>依頼一覧の 1 行（P7 02。仕様 §10「依頼名、依頼主、目的、条件ごとの進捗、報酬、報告可能または受領済み」）。</summary>
    public sealed class JournalQuestEntry
    {
        internal JournalQuestEntry(QuestInfo quest, string giverName, QuestStateKind state, bool[] objectivesMet)
        {
            Quest = quest;
            GiverName = giverName ?? string.Empty;
            State = state;
            ObjectivesMet = objectivesMet;
        }

        public QuestInfo Quest { get; }
        public string GiverName { get; }
        public QuestStateKind State { get; }
        public IReadOnlyList<bool> ObjectivesMet { get; }

        /// <summary>状態の表示文。</summary>
        public string StateLabel
        {
            get
            {
                switch (State)
                {
                    case QuestStateKind.InProgress:
                        return "進行中";
                    case QuestStateKind.Reportable:
                        return "報告できる（" + GiverName + "）";
                    case QuestStateKind.Rewarded:
                        return "報酬受領済み";
                    default:
                        return "未受注";
                }
            }
        }
    }

    /// <summary>章の進行の 1 行（P7 04。「章クリア状態は常設の進行表示でも確認できる」）。</summary>
    public readonly struct JournalChapterEntry
    {
        public JournalChapterEntry(ChapterInfo chapter, bool cleared, StoryRoute clearedRoute)
        {
            Chapter = chapter;
            Cleared = cleared;
            ClearedRoute = clearedRoute;
        }

        public ChapterInfo Chapter { get; }
        public bool Cleared { get; }
        public StoryRoute ClearedRoute { get; }
    }

    /// <summary>
    /// 依頼一覧と章の進行の表示内容を組む（P7。<b>読むだけ</b>）。表示は確定済みの状態から毎回導く（保存しない）。
    /// 受注済み・報告可能・受領済みの依頼を載せ、未受注の依頼は載せない（話を聞く前に一覧へ出さない）。
    /// </summary>
    public static class StoryJournal
    {
        public static List<JournalQuestEntry> Quests(GameSessionState session, StoryCatalog story)
        {
            var list = new List<JournalQuestEntry>();
            if (session == null || story == null)
            {
                return list;
            }

            foreach (QuestInfo quest in story.Quests)
            {
                QuestStateKind state = StoryRules.StateOf(session, quest);
                if (state == QuestStateKind.NotAccepted)
                {
                    continue;
                }

                var met = new bool[quest.Objectives.Count];
                for (int i = 0; i < met.Length; i++)
                {
                    met[i] = StoryRules.IsObjectiveMet(session, quest.Objectives[i]);
                }

                string giver = story.TryGetVillager(quest.GiverVillagerId, out VillagerInfo v) ? v.DisplayName : string.Empty;
                list.Add(new JournalQuestEntry(quest, giver, state, met));
            }

            return list;
        }

        public static List<JournalChapterEntry> Chapters(GameSessionState session, StoryCatalog story)
        {
            var list = new List<JournalChapterEntry>();
            if (session == null || story == null)
            {
                return list;
            }

            foreach (ChapterInfo chapter in story.Chapters)
            {
                list.Add(new JournalChapterEntry(chapter, session.Story.IsChapterCleared(chapter.ChapterId),
                    session.Story.ClearedRouteOf(chapter.ChapterId)));
            }

            return list;
        }

        /// <summary>経路の表示名。</summary>
        public static string RouteLabel(StoryRoute route) =>
            route == StoryRoute.Standard ? "標準の道" : route == StoryRoute.Hard ? "困難な道" : "記録なし";
    }
}
