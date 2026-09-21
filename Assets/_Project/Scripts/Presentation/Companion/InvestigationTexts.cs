using Momotaro.Gameplay.Companion.Investigation;

namespace Momotaro.Presentation.Companion
{
    /// <summary>
    /// 探索の仮 UI に出す短文（P4-07B。v1.0 §11「調べられる・移動中・調査中・発見・拒否理由・調査済み」の 6 つ）。
    /// 純粋関数のみ。文言は Presentation に閉じ、Gameplay は理由の列挙体だけを返す。
    /// 地点固有の文（「調べる」「犬がいれば…」「調べ終えた」）は地点 Data が持つので、ここでは持たない。
    /// </summary>
    public static class InvestigationTexts
    {
        /// <summary>Interact の割当を示す接頭（キーボード E／ゲームパッド南ボタン。IA_Momotaro.inputactions）。</summary>
        public const string InteractKeyLabel = "E / 南ボタン";

        /// <summary>試遊の戦闘開始操作（Enter／Start。P4-08R）。</summary>
        public const string CombatStartPrompt = "Enter / Start：戦闘を始める";

        /// <summary>受理直後の短文。</summary>
        public const string Accepted = "犬丸に調べさせる";

        /// <summary>発見の短文（§4.1 手順 5）。</summary>
        public const string Completed = "犬丸が痕跡を見つけた";

        /// <summary>調査済み地点のマーカー文字。</summary>
        public const string InvestigatedMark = "済";

        /// <summary>未調査地点の既定マーカー文字（近づくまでは何があるか分からない）。</summary>
        public const string UnknownMark = "？";

        /// <summary>「調べる」の案内（地点の Prompt に入力の割当を添える）。</summary>
        public static string Prompt(string pointPrompt)
        {
            string body = string.IsNullOrEmpty(pointPrompt) ? "調べる" : pointPrompt;
            return InteractKeyLabel + "：" + body;
        }

        /// <summary>依頼の進行段の表示（表示代理・本体の頭上ラベル）。</summary>
        public static string PhaseLabel(InvestigationPhase phase)
        {
            switch (phase)
            {
                case InvestigationPhase.Moving: return "移動";
                case InvestigationPhase.Investigating: return "調査中";
                case InvestigationPhase.Returning: return "帰還";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 拒否理由の短文。地点固有の文（未加入ヒント・調査済み）は通知の Text が優先される。
        /// 表示しない理由（対象なし・未配線等）は空文字。
        /// </summary>
        public static string Reject(InvestigationRejectReason reason, string pointText)
        {
            if (!string.IsNullOrEmpty(pointText))
            {
                return pointText;
            }

            switch (reason)
            {
                case InvestigationRejectReason.CompanionNotRecruited: return "仲間がいない";
                case InvestigationRejectReason.AlreadyInvestigated: return "ここは調べ終えた";
                case InvestigationRejectReason.Unreachable: return "そこへは行けない";
                case InvestigationRejectReason.InCombat: return "戦闘中は調べられない";
                case InvestigationRejectReason.CompanionBusy: return "いま別の場所を調べている";
                case InvestigationRejectReason.CompanionNotReady: return "いまは調べられない";
                case InvestigationRejectReason.PlayerBusy: return "いまは調べられない";
                case InvestigationRejectReason.PointUnavailable: return "ここは調べられない";
                case InvestigationRejectReason.DuplicateRequest: return string.Empty;
                default: return string.Empty;
            }
        }

        /// <summary>中断理由の短文（成功後の帰還中の撤収には通知が出ないので、ここへは来ない）。</summary>
        public static string Interrupt(InvestigationInterruptReason reason)
        {
            switch (reason)
            {
                case InvestigationInterruptReason.CombatStarted: return "戦闘が始まり、調査をやめた";
                case InvestigationInterruptReason.CompanionHit: return "攻撃を受け、調査をやめた";
                case InvestigationInterruptReason.PlayerLeftRange: return "離れすぎて、調査をやめた";
                case InvestigationInterruptReason.Blocked: return "道が塞がれ、調査をやめた";
                case InvestigationInterruptReason.MoveTimeout: return "たどり着けず、調査をやめた";
                case InvestigationInterruptReason.PointLost: return "痕跡が消え、調査をやめた";
                case InvestigationInterruptReason.RecruitLost: return "調査をやめた";
                case InvestigationInterruptReason.EventStarted: return "調査をやめた";
                case InvestigationInterruptReason.Disabled: return "調査をやめた";
                case InvestigationInterruptReason.CompletionRejected: return "調査を終えられなかった";
                default: return string.Empty;
            }
        }
    }
}
