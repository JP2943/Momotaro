using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Data.Progression
{
    /// <summary>スキルツリーのノード雛形（仕様書 7 章）。徳コスト・階層・前提・排他を持つ。</summary>
    [CreateAssetMenu(fileName = "SO_Skill_New", menuName = "Momotaro/Data/Progression/Skill Node Data", order = 0)]
    public sealed class SkillNodeData : GameDataAsset
    {
        [Header("Skill Node")]
        [SerializeField] private int _virtueCost = 1;
        [SerializeField] private int _tier;
        [SerializeField] private List<SkillNodeData> _prerequisites = new List<SkillNodeData>();
        [SerializeField] private List<SkillNodeData> _mutuallyExclusive = new List<SkillNodeData>();

        [Header("効果（P6A の検証用）")]
        [Tooltip("取得で主人公の最大 HP に加える値。P6A は効果再適用の検証だけに使う（本編の数値ではない）。")]
        [SerializeField] private int _maxHpBonus;

        /// <summary>取得に必要な徳。</summary>
        public int VirtueCost => _virtueCost;

        /// <summary>階層（0 起点）。</summary>
        public int Tier => _tier;

        /// <summary>前提ノード（すべて取得済みであること）。</summary>
        public IReadOnlyList<SkillNodeData> Prerequisites => _prerequisites;

        /// <summary>排他ノード（どれかを取得済みなら取得できない）。P6A の仮項目は使わない。</summary>
        public IReadOnlyList<SkillNodeData> MutuallyExclusive => _mutuallyExclusive;

        /// <summary>最大 HP 加算（P6A の検証用効果）。</summary>
        public int MaxHpBonus => _maxHpBonus;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_virtueCost < 0)
            {
                report.Error(name + ": VirtueCost must be >= 0.");
            }

            if (_tier < 0)
            {
                report.Error(name + ": Tier must be >= 0.");
            }

            if (_maxHpBonus < 0)
            {
                report.Error(name + ": MaxHpBonus must be >= 0.");
            }

            if (_prerequisites.Contains(this))
            {
                report.Error(name + ": SkillNode references itself as a prerequisite.");
            }
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用。P6A）。</summary>
        public void EditorSet(int virtueCost, int tier, int maxHpBonus)
        {
            _virtueCost = virtueCost;
            _tier = tier;
            _maxHpBonus = maxHpBonus;
        }
#endif
    }
}
