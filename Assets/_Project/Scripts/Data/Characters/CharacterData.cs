using UnityEngine;

namespace Momotaro.Data.Characters
{
    /// <summary>
    /// 主人公・仲間・敵に共通するキャラクター基礎データの抽象基底（仕様書 3.8 / 4.5 / 6 章）。
    /// 具体的な役割は派生型が担う。数値は雛形であり、詳細は後続 Phase で拡張する。
    /// </summary>
    public abstract class CharacterData : GameDataAsset
    {
        [Header("Character Base")]
        [SerializeField] private int _maxHp = 100;
        [SerializeField] private float _moveSpeed = 5f;

        [Header("Combat Stats (Phase2 P2-04)")]
        [Tooltip("攻撃力（HP ダメージ基礎に用いる。主人公=100。仕様書 Table 12）。")]
        [SerializeField] private float _attackPower;
        [Tooltip("防御力（防御補正 max(0.1, 100/(100+防御)) に用いる。主人公=20）。")]
        [SerializeField] private float _defense;

        /// <summary>最大HP。</summary>
        public int MaxHp => _maxHp;

        /// <summary>移動速度。</summary>
        public float MoveSpeed => _moveSpeed;

        /// <summary>攻撃力（仕様書 §6.1 / Table 12）。</summary>
        public float AttackPower => _attackPower;

        /// <summary>防御力（仕様書 §6.1 / Table 11）。</summary>
        public float Defense => _defense;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_maxHp <= 0)
            {
                report.Error(name + ": MaxHp must be > 0.");
            }

            if (_moveSpeed < 0f)
            {
                report.Error(name + ": MoveSpeed must be >= 0.");
            }

            if (_attackPower < 0f)
            {
                report.Error(name + ": AttackPower must be >= 0.");
            }

            if (_defense < 0f)
            {
                report.Error(name + ": Defense must be >= 0.");
            }
        }
    }
}
