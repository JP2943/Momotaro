using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Story;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Story
{
    /// <summary>会話の選択肢の種類（P7。仕様 §4「受ける・今は受けない・報告して受け取る・閉じる」＋必須イベントの決定）。</summary>
    public enum DialogueChoiceKind
    {
        /// <summary>閉じる（何も変えない）。</summary>
        Close = 0,

        /// <summary>依頼の話を聞く（依頼の内容の画面へ。何も変えない）。</summary>
        ListenQuest = 1,

        /// <summary>依頼を受ける（受注の確定）。</summary>
        AcceptQuest = 2,

        /// <summary>今は受けない（何も変えない。恒久フラグにしない）。</summary>
        DeclineQuest = 3,

        /// <summary>報告して報酬を受け取る（報告の確定）。</summary>
        ReportQuest = 4,

        /// <summary>必須イベントを完了させる決定。</summary>
        ConfirmEvent = 5,
    }

    /// <summary>会話の選択肢 1 つ（不変）。</summary>
    public readonly struct DialogueChoice
    {
        public DialogueChoice(DialogueChoiceKind kind, StableId targetId, string label)
        {
            Kind = kind;
            TargetId = targetId;
            Label = label ?? string.Empty;
        }

        public DialogueChoiceKind Kind { get; }

        /// <summary>依頼 ID かイベント ID（閉じる・断るでは依頼 ID か空）。</summary>
        public StableId TargetId { get; }

        public string Label { get; }

        /// <summary>選ぶと進行を変える選択肢か（受注・報告・イベント）。</summary>
        public bool Commits =>
            Kind == DialogueChoiceKind.AcceptQuest || Kind == DialogueChoiceKind.ReportQuest || Kind == DialogueChoiceKind.ConfirmEvent;
    }

    /// <summary>会話の 1 画面（話者・ページ・最後のページの選択肢。不変）。</summary>
    public sealed class DialogueScreen
    {
        internal DialogueScreen(string speaker, string[] pages, DialogueChoice[] choices)
        {
            Speaker = speaker ?? string.Empty;
            Pages = pages ?? Array.Empty<string>();
            Choices = choices ?? Array.Empty<DialogueChoice>();
        }

        public string Speaker { get; }
        public IReadOnlyList<string> Pages { get; }

        /// <summary>最後のページで示す選択肢（空なら、最後のページの「次へ」で閉じる）。</summary>
        public IReadOnlyList<DialogueChoice> Choices { get; }
    }

    /// <summary>
    /// 開いている会話（P7 01。仕様 §3・§4）。<b>会話の入口で最新の確定状態から内容を 1 回だけ組み</b>、途中で本文を差し替えない。
    /// 進行の確定（受注・報告・イベント）はここでは行わない——選択肢を返すだけで、実行は <see cref="StoryProcedures"/> が再検証して行う。
    ///
    /// 保存しない（会話ウィンドウ・表示中の行・選択カーソルは中断の対象外。仕様 §9）。
    /// </summary>
    public sealed class DialogueConversation
    {
        private readonly Dictionary<string, DialogueScreen> _offers = new Dictionary<string, DialogueScreen>();

        private DialogueConversation(VillagerInfo villager, DialogueInfo dialogue, DialogueScreen main)
        {
            Villager = villager;
            Dialogue = dialogue;
            Main = main;
            Current = main;
        }

        public VillagerInfo Villager { get; }

        /// <summary>入口で選んだ会話の候補。</summary>
        public DialogueInfo Dialogue { get; }

        /// <summary>入口で組んだ最初の画面。</summary>
        public DialogueScreen Main { get; }

        /// <summary>いま表示している画面。</summary>
        public DialogueScreen Current { get; private set; }

        /// <summary>いま表示しているページ。</summary>
        public int PageIndex { get; private set; }

        public string CurrentPage => Current.Pages.Count == 0 ? string.Empty : Current.Pages[PageIndex];

        public bool IsOnLastPage => PageIndex >= Current.Pages.Count - 1;

        /// <summary>最後のページで選択肢を示しているか。</summary>
        public bool ShowsChoices => IsOnLastPage && Current.Choices.Count > 0;

        /// <summary>
        /// 会話を組む。住民が未知・会話の候補が無ければ null。
        /// </summary>
        public static DialogueConversation Open(GameSessionState session, StoryCatalog story, StableId villagerId)
        {
            if (session == null || story == null || !story.TryGetVillager(villagerId, out VillagerInfo villager))
            {
                return null;
            }

            DialogueInfo dialogue = StoryRules.SelectDialogue(session, story, villagerId);
            if (dialogue == null)
            {
                return null;
            }

            var pages = new List<string>(dialogue.Pages);
            var choices = new List<DialogueChoice>();
            var offers = new List<QuestInfo>();
            var quests = new List<QuestInfo>();
            story.CopyQuestsOf(villagerId, quests);
            for (int i = 0; i < quests.Count; i++)
            {
                QuestInfo q = quests[i];
                switch (StoryRules.StateOf(session, q))
                {
                    case QuestStateKind.NotAccepted:
                        choices.Add(new DialogueChoice(DialogueChoiceKind.ListenQuest, q.QuestId,
                            "依頼「" + q.DisplayName + "」の話を聞く"));
                        offers.Add(q);
                        break;
                    case QuestStateKind.InProgress:
                        pages.Add("（依頼「" + q.DisplayName + "」）" + q.ProgressText);
                        break;
                    case QuestStateKind.Reportable:
                        // 経路差分・章クリア後の会話を選んでいても、報告の選択肢は依頼主の会話に必ず付ける（仕様 §4 末尾）。
                        choices.Add(new DialogueChoice(DialogueChoiceKind.ReportQuest, q.QuestId,
                            "「" + q.DisplayName + "」を報告して報酬を受け取る（徳 " + q.Reward.VirtueAmount + "）"));
                        break;
                }
            }

            if (!dialogue.CompletesEventId.IsEmpty && !session.Story.IsEventCompleted(dialogue.CompletesEventId))
            {
                choices.Add(new DialogueChoice(DialogueChoiceKind.ConfirmEvent, dialogue.CompletesEventId, dialogue.ConfirmLabel));
            }

            if (choices.Count > 0)
            {
                choices.Add(new DialogueChoice(DialogueChoiceKind.Close, default, "閉じる"));
            }

            var conversation = new DialogueConversation(villager, dialogue,
                new DialogueScreen(dialogue.Speaker, pages.ToArray(), choices.ToArray()));
            for (int i = 0; i < offers.Count; i++)
            {
                QuestInfo q = offers[i];
                var offerPages = new List<string>(q.OfferPages)
                {
                    "目的：" + q.ObjectiveText + "\n報酬：徳 " + q.Reward.VirtueAmount,
                };
                conversation._offers[q.QuestId.Value] = new DialogueScreen(dialogue.Speaker, offerPages.ToArray(), new[]
                {
                    new DialogueChoice(DialogueChoiceKind.AcceptQuest, q.QuestId, "依頼を受ける"),
                    new DialogueChoice(DialogueChoiceKind.DeclineQuest, q.QuestId, "今は受けない"),
                });
            }

            return conversation;
        }

        /// <summary>次のページへ（最後のページでは何もしない）。進んだら true。</summary>
        public bool Advance()
        {
            if (IsOnLastPage)
            {
                return false;
            }

            PageIndex++;
            return true;
        }

        /// <summary>依頼の内容の画面へ移る（「話を聞く」）。その依頼の画面が無ければ false。</summary>
        public bool ShowQuestOffer(StableId questId)
        {
            if (questId.IsEmpty || !_offers.TryGetValue(questId.Value, out DialogueScreen screen))
            {
                return false;
            }

            Current = screen;
            PageIndex = 0;
            return true;
        }
    }
}
