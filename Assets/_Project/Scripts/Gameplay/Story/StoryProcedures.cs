using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Story
{
    /// <summary>
    /// 会話の決定を進行へ確定する手順（P7 02／03。仕様 §4・§6・§7）。<b>実行時に再検証する</b>——表示した時点の状態を信用しない。
    /// 不成立はすべて無変更。通常の表示や途中で閉じる操作はここを通らないので、報酬や進行フラグは変わらない。
    /// </summary>
    public static class StoryProcedures
    {
        /// <summary>依頼を受ける。<paramref name="villagerId"/> がその依頼の依頼主であること。</summary>
        public static QuestAcceptResult AcceptQuest(GameSessionState session, StoryCatalog story, StableId villagerId,
            StableId questId)
        {
            if (session == null || story == null || !story.TryGetQuest(questId, out QuestInfo quest))
            {
                return QuestAcceptResult.Unknown;
            }

            if (!quest.GiverVillagerId.Equals(villagerId))
            {
                return QuestAcceptResult.NotOfferedHere;
            }

            return session.CommitQuestAccepted(quest);
        }

        /// <summary>依頼を報告して報酬を受け取る。依頼主へだけ。</summary>
        public static QuestReportResult ReportQuest(GameSessionState session, StoryCatalog story, StableId villagerId,
            StableId questId, out int grantedVirtue)
        {
            grantedVirtue = 0;
            if (session == null || story == null || !story.TryGetQuest(questId, out QuestInfo quest))
            {
                return QuestReportResult.Unknown;
            }

            if (!quest.GiverVillagerId.Equals(villagerId))
            {
                return QuestReportResult.NotOfferedHere;
            }

            return session.CommitQuestReported(quest, out grantedVirtue);
        }

        /// <summary>
        /// 必須イベントを完了させる（会話の最後の決定）。<paramref name="dialogue"/> がそのイベントを完了させる会話であること。
        /// 既に完了済み・未知・別の会話からは false（無変更）。
        /// </summary>
        public static bool CompleteEvent(GameSessionState session, StoryCatalog story, DialogueInfo dialogue,
            StableId eventId, out bool flagOpened)
        {
            flagOpened = false;
            if (session == null || story == null || dialogue == null || eventId.IsEmpty
                || !dialogue.CompletesEventId.Equals(eventId) || !story.TryGetEvent(eventId, out StoryEventInfo info))
            {
                return false;
            }

            return session.CommitEventCompleted(info, out flagOpened);
        }
    }
}
