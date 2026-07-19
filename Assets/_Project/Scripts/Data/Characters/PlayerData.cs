using UnityEngine;

namespace Momotaro.Data.Characters
{
    /// <summary>主人公（桃太郎）の基礎データ雛形（仕様書 3.8.1）。</summary>
    [CreateAssetMenu(fileName = "SO_Player_New", menuName = "Momotaro/Data/Character/Player Data", order = 0)]
    public sealed class PlayerData : CharacterData
    {
        [Header("Player")]
        [SerializeField] private int _maxStamina = 100;

        /// <summary>最大スタミナ。</summary>
        public int MaxStamina => _maxStamina;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_maxStamina <= 0)
            {
                report.Error(name + ": MaxStamina must be > 0.");
            }
        }
    }
}
