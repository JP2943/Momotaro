using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Story;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Story
{
    /// <summary>依頼の段階の値（P7。既存のクエスト段階へ割り当てる。<c>P7_統合受入結果.md</c> 記録 001 §5.1）。</summary>
    public static class QuestStage
    {
        /// <summary>未受注（未設定と同じ）。</summary>
        public const int NotAccepted = 0;

        /// <summary>受注済み。</summary>
        public const int Accepted = 1;

        /// <summary>報酬受領済み。</summary>
        public const int Rewarded = 2;

        /// <summary>依頼の段階として有効な最大値。</summary>
        public const int Max = Rewarded;
    }

    /// <summary>
    /// 会話・依頼の判定（P7。仕様 §4・§5）。<b>読むだけ</b>で、Session を変えない。
    ///
    /// 依頼の「進行中」「報告可能」は<b>受注の記録と既存の確定済み世界状態から導く</b>（同じ事実を複数の可変フラグで持たない）。
    /// 受注前に満たした恒久条件も達成として数える（受注で「達成をやり直す」ことはない）。
    /// </summary>
    public static class StoryRules
    {
        /// <summary>達成条件 1 つが成り立っているか（世界の確定状態だけを見る）。</summary>
        public static bool IsObjectiveMet(GameSessionState session, in QuestObjectiveInfo objective)
        {
            if (session == null)
            {
                return false;
            }

            switch (objective.Kind)
            {
                case QuestObjectiveKind.AreaReached:
                    return session.HasVisited(objective.AreaId);
                case QuestObjectiveKind.Discovery:
                    if (!session.TryGetArea(objective.AreaId, out AreaRuntimeState area))
                    {
                        return false;
                    }

                    return objective.DiscoveryIsInvestigation
                        ? area.Investigation.IsInvestigated(objective.TargetId)
                        : area.IsPlacementPicked(objective.TargetId);
                case QuestObjectiveKind.EncounterCleared:
                    return session.TryGetArea(objective.AreaId, out AreaRuntimeState a)
                        && a.IsEncounterCleared(objective.TargetId, session.RespawnCycle);
                default:
                    return false;
            }
        }

        /// <summary>全条件が成り立っているか（AND）。条件が無い依頼は成り立たない（Data が拒否する）。</summary>
        public static bool AreObjectivesMet(GameSessionState session, QuestInfo quest)
        {
            if (session == null || quest == null || quest.Objectives.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                if (!IsObjectiveMet(session, quest.Objectives[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>依頼の状態。<b>受領済みを最優先</b>（段階 2）。受注済みなら条件の成立で報告可能。</summary>
        public static QuestStateKind StateOf(GameSessionState session, QuestInfo quest)
        {
            if (session == null || quest == null)
            {
                return QuestStateKind.NotAccepted;
            }

            int stage = session.QuestStageOf(quest.QuestId);
            if (stage >= QuestStage.Rewarded)
            {
                return QuestStateKind.Rewarded;
            }

            if (stage == QuestStage.Accepted)
            {
                return AreObjectivesMet(session, quest) ? QuestStateKind.Reportable : QuestStateKind.InProgress;
            }

            return QuestStateKind.NotAccepted;
        }

        /// <summary>会話の候補の条件 1 つが成り立つか。</summary>
        public static bool IsConditionMet(GameSessionState session, StoryCatalog story, StoryCondition condition)
        {
            if (session == null || story == null || condition == null)
            {
                return false;
            }

            bool value;
            switch (condition.Kind)
            {
                case StoryConditionKind.ChapterCleared:
                    value = session.Story.IsChapterCleared(condition.ChapterId);
                    break;
                case StoryConditionKind.QuestState:
                    value = story.TryGetQuest(condition.QuestId, out QuestInfo quest)
                        && StateOf(session, quest) == condition.QuestState;
                    break;
                case StoryConditionKind.EventCompleted:
                    value = session.Story.IsEventCompleted(condition.EventId);
                    break;
                case StoryConditionKind.RouteReached:
                    value = session.Story.RouteOf(condition.ChapterId).Reached(condition.Route);
                    break;
                case StoryConditionKind.RouteCompleted:
                    value = session.Story.RouteOf(condition.ChapterId).Completed(condition.Route);
                    break;
                default:
                    return false; // 種類の無い条件は成り立たせない（否定でも true にしない）。
            }

            return condition.Negate ? !value : value;
        }

        /// <summary>
        /// 住民の会話を 1 つ選ぶ（仕様 §4）。条件（AND）が成り立つ候補のうち優先度が最も高いもの。
        /// 同じ優先度の候補は Data が拒否しているので、ここでは定義順で先のものになる。無条件の候補が必ず残る。
        /// </summary>
        public static DialogueInfo SelectDialogue(GameSessionState session, StoryCatalog story, StableId villagerId)
        {
            if (story == null)
            {
                return null;
            }

            DialogueInfo best = null;
            IReadOnlyList<DialogueInfo> candidates = story.DialoguesOf(villagerId);
            for (int i = 0; i < candidates.Count; i++)
            {
                DialogueInfo d = candidates[i];
                if (best != null && d.Priority <= best.Priority)
                {
                    continue;
                }

                bool ok = true;
                for (int c = 0; c < d.Conditions.Count; c++)
                {
                    if (!IsConditionMet(session, story, d.Conditions[c]))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    best = d;
                }
            }

            return best;
        }
    }
}
