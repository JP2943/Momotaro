using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Story
{
    /// <summary>章ボスの解決結果（P7 04）。</summary>
    public readonly struct ChapterBossInfo
    {
        public ChapterBossInfo(StableId chapterId, string displayName, int rightsPerChapter, int rightsMax)
        {
            ChapterId = chapterId;
            DisplayName = displayName ?? string.Empty;
            RightsPerChapter = rightsPerChapter;
            RightsMax = rightsMax;
        }

        public StableId ChapterId { get; }
        public string DisplayName { get; }
        public int RightsPerChapter { get; }
        public int RightsMax { get; }
        public bool IsValid => !ChapterId.IsEmpty;
    }

    /// <summary>
    /// 章の進行の受け口（P7 04。仕様 §8）。遭遇戦の実行役（Gameplay）が、勝利した遭遇戦が章ボスかを問い合わせ、
    /// 確定の結果を通知する。実装は常駐の会話サービス（campaign の Data を持つ側）。
    /// </summary>
    public interface IChapterProgress
    {
        /// <summary>その Area のその遭遇戦が、この campaign の章ボスか。</summary>
        bool TryGetChapterBoss(StableId areaId, StableId encounterId, out ChapterBossInfo info);

        /// <summary>章ボスの勝利を確定した（通知・表示用。確定そのものは Session が済ませている）。</summary>
        void OnChapterBossVictory(in ChapterBossInfo chapter, in ChapterClearCommit commit);
    }

    /// <summary>章の進行の受け口の供給点（P7 04）。未設定なら章ボスは無い（P6 までの仮ボスは従来どおり）。</summary>
    public static class ChapterProgressProvider
    {
        public static IChapterProgress Current { get; set; }

        public static void ReleaseIfOwner(IChapterProgress owned)
        {
            if (owned != null && ReferenceEquals(Current, owned))
            {
                Current = null;
            }
        }
    }
}
