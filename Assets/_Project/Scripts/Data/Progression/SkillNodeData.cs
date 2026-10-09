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

        [Header("効果（P6B の浅いツリー。加算は基礎値＋取得済み効果から毎回作り直す）")]
        [Tooltip("主人公の刀による HP ダメージ倍率への加算（通常各段と必殺。1.0 に足す。0.10 なら +10%）。")]
        [SerializeField] private float _attackHpMultiplierBonus;

        [Tooltip("主人公の最大スタミナへの加算。")]
        [SerializeField] private int _maxStaminaBonus;

        [Tooltip("主人公の通常攻撃による敵体幹ダメージ倍率への加算（JG・必殺は対象外）。")]
        [SerializeField] private float _normalPoiseMultiplierBonus;

        [Tooltip("きびだんご 1 個の回復量への加算。")]
        [SerializeField] private int _kibidangoHealBonus;

        [Tooltip("きびだんごの最大数への加算。")]
        [SerializeField] private int _kibidangoCapacityBonus;

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

        /// <summary>刀の HP ダメージ倍率への加算（P6B）。</summary>
        public float AttackHpMultiplierBonus => _attackHpMultiplierBonus;

        /// <summary>最大スタミナへの加算（P6B）。</summary>
        public int MaxStaminaBonus => _maxStaminaBonus;

        /// <summary>通常攻撃の体幹倍率への加算（P6B）。</summary>
        public float NormalPoiseMultiplierBonus => _normalPoiseMultiplierBonus;

        /// <summary>きびだんご回復量への加算（P6B）。</summary>
        public int KibidangoHealBonus => _kibidangoHealBonus;

        /// <summary>きびだんご最大数への加算（P6B）。</summary>
        public int KibidangoCapacityBonus => _kibidangoCapacityBonus;

        /// <summary>何かしらの効果を持つか（ゼロ効果ノードを作らない。P6B 仕様 §3）。</summary>
        public bool HasAnyEffect =>
            _maxHpBonus != 0 || _attackHpMultiplierBonus != 0f || _maxStaminaBonus != 0
            || _normalPoiseMultiplierBonus != 0f || _kibidangoHealBonus != 0 || _kibidangoCapacityBonus != 0;

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

            for (int i = 0; i < _prerequisites.Count; i++)
            {
                if (_prerequisites[i] == null)
                {
                    report.Error(name + ": Prerequisites[" + i + "] is null.");
                }
            }

            CheckBonus(report, _attackHpMultiplierBonus, "AttackHpMultiplierBonus");
            CheckBonus(report, _normalPoiseMultiplierBonus, "NormalPoiseMultiplierBonus");
            if (_maxStaminaBonus < 0 || _kibidangoHealBonus < 0 || _kibidangoCapacityBonus < 0)
            {
                report.Error(name + ": Stamina / kibidango bonuses must be >= 0.");
            }
        }

        private void CheckBonus(DataValidationReport report, float value, string label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                report.Error(name + ": " + label + " must be a finite value >= 0.");
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

        /// <summary>P6B の効果と前提を設定する（Editor 専用）。前提は呼び出し側の順序のまま置き換える。</summary>
        public void EditorSetP6B(int virtueCost, int tier, int maxHpBonus, float attackHpMultiplierBonus,
            int maxStaminaBonus, float normalPoiseMultiplierBonus, int kibidangoHealBonus, int kibidangoCapacityBonus,
            IReadOnlyList<SkillNodeData> prerequisites)
        {
            _virtueCost = virtueCost;
            _tier = tier;
            _maxHpBonus = maxHpBonus;
            _attackHpMultiplierBonus = attackHpMultiplierBonus;
            _maxStaminaBonus = maxStaminaBonus;
            _normalPoiseMultiplierBonus = normalPoiseMultiplierBonus;
            _kibidangoHealBonus = kibidangoHealBonus;
            _kibidangoCapacityBonus = kibidangoCapacityBonus;
            _prerequisites = new List<SkillNodeData>();
            if (prerequisites != null)
            {
                _prerequisites.AddRange(prerequisites);
            }

            _mutuallyExclusive = new List<SkillNodeData>();
        }
#endif
    }
}
