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

        /// <summary>取得に必要な徳。</summary>
        public int VirtueCost => _virtueCost;

        /// <summary>階層（0 起点）。</summary>
        public int Tier => _tier;

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

            if (_prerequisites.Contains(this))
            {
                report.Error(name + ": SkillNode references itself as a prerequisite.");
            }
        }
    }
}
