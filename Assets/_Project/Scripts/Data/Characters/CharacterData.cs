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

        /// <summary>最大HP。</summary>
        public int MaxHp => _maxHp;

        /// <summary>移動速度。</summary>
        public float MoveSpeed => _moveSpeed;

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
        }
    }
}
