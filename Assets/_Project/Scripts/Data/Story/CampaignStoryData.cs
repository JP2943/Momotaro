using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Data.Story
{
    /// <summary>
    /// campaign の会話・依頼・必須イベント・章の定義（P7。仕様 §4〜§8・§10）。<c>AreaCatalogData</c> から参照する。
    ///
    /// <b>表示テキストや Scene 名を永続 ID に使わない。</b> 住民・会話・依頼・イベント・章はすべて StableId で引く。
    /// ここでの検査は Data の中だけで閉じるもの（重複・空・負・未設定）。Area・入口・遭遇戦・配置物・報酬・章の
    /// 存在は campaign を組むとき（<c>StoryCatalog</c>）に検査する。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Story_New", menuName = "Momotaro/Data/Story/Campaign Story Data", order = 1)]
    public sealed class CampaignStoryData : GameDataAsset
    {
        [Header("Story")]
        [SerializeField] private List<VillagerDefinition> _villagers = new List<VillagerDefinition>();
        [SerializeField] private List<DialogueDefinition> _dialogues = new List<DialogueDefinition>();
        [SerializeField] private List<QuestDefinition> _quests = new List<QuestDefinition>();
        [SerializeField] private List<StoryEventDefinition> _events = new List<StoryEventDefinition>();
        [SerializeField] private List<ChapterDefinition> _chapters = new List<ChapterDefinition>();

        [Tooltip("1 ページの本文の上限（文字数）。仮 UI の枠からはみ出さないよう、長文はページを分ける。")]
        [SerializeField] private int _maxPageLength = 120;

        public IReadOnlyList<VillagerDefinition> Villagers => _villagers;
        public IReadOnlyList<DialogueDefinition> Dialogues => _dialogues;
        public IReadOnlyList<QuestDefinition> Quests => _quests;
        public IReadOnlyList<StoryEventDefinition> Events => _events;
        public IReadOnlyList<ChapterDefinition> Chapters => _chapters;
        public int MaxPageLength => _maxPageLength > 0 ? _maxPageLength : 120;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            var errors = new List<string>();
            StoryDataCheck.Check(this, errors);
            for (int i = 0; i < errors.Count; i++)
            {
                report.Error(name + ": " + errors[i]);
            }
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てる（Editor 専用）。</summary>
        public void EditorSet(List<VillagerDefinition> villagers, List<DialogueDefinition> dialogues,
            List<QuestDefinition> quests, List<StoryEventDefinition> events, List<ChapterDefinition> chapters,
            int maxPageLength = 120)
        {
            _villagers = villagers ?? new List<VillagerDefinition>();
            _dialogues = dialogues ?? new List<DialogueDefinition>();
            _quests = quests ?? new List<QuestDefinition>();
            _events = events ?? new List<StoryEventDefinition>();
            _chapters = chapters ?? new List<ChapterDefinition>();
            _maxPageLength = maxPageLength;
        }
#endif
    }
}
