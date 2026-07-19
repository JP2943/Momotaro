using UnityEngine;

namespace Momotaro.Data.Characters
{
    /// <summary>仲間（犬・猿・雉）の基礎データ雛形（仕様書 4 章 / 5 章）。</summary>
    [CreateAssetMenu(fileName = "SO_Companion_New", menuName = "Momotaro/Data/Character/Companion Data", order = 1)]
    public sealed class CompanionData : CharacterData
    {
        [Header("Companion")]
        [SerializeField] private float _switchCooldownSeconds = 3f;
        [SerializeField] private float _leaveRecoverySeconds = 5f;

        /// <summary>交代のクールダウン秒。</summary>
        public float SwitchCooldownSeconds => _switchCooldownSeconds;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_switchCooldownSeconds < 0f || _leaveRecoverySeconds < 0f)
            {
                report.Error(name + ": Cooldown/Recovery must be >= 0.");
            }
        }
    }
}
