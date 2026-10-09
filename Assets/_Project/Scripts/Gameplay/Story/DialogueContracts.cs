using Momotaro.Core.Identification;
using Momotaro.Gameplay.Interaction;

namespace Momotaro.Gameplay.Story
{
    /// <summary>
    /// 住民・立て札から呼ぶ会話の受け口（P7 01。<c>IShrineOperations</c> と同じ形）。実装は常駐の会話サービス（Infrastructure）。
    /// Scene 側は受け口を探さず、<see cref="DialogueOperationsProvider"/> から引く。
    /// </summary>
    public interface IDialogueOperations
    {
        /// <summary>住民を調べた。会話を始められなければ理由付きで断る（押下は捨てる。動作終了後に自動で開かない）。</summary>
        AreaInteractionOutcome OnVillagerInteracted(StableId villagerId, StableId areaId);

        /// <summary>必須イベントの門を調べた。未完了なら理由を返す（P7 03）。</summary>
        AreaInteractionOutcome OnGateInspected(StableId eventId);

        /// <summary>そのイベントが完了済みか（立て札の表示切り替え用）。campaign が無ければ false。</summary>
        bool IsEventCompleted(StableId eventId);
    }

    /// <summary>会話の受け口の供給点（P7 01）。</summary>
    public static class DialogueOperationsProvider
    {
        /// <summary>現在の受け口（未設定なら null）。</summary>
        public static IDialogueOperations Current { get; set; }

        /// <summary>自分が差したものなら外す（常駐の破棄・作り直しの後始末）。</summary>
        public static void ReleaseIfOwner(IDialogueOperations owned)
        {
            if (owned != null && ReferenceEquals(Current, owned))
            {
                Current = null;
            }
        }
    }
}
