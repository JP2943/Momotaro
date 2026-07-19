using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>必殺技のパラメータ雛形（仕様書 3.6）。</summary>
    [CreateAssetMenu(fileName = "SO_Special_New", menuName = "Momotaro/Data/Combat/Special Attack Data", order = 3)]
    public sealed class SpecialAttackData : GameDataAsset
    {
        [Header("Special")]
        [SerializeField] private float _gaugeCost = 100f;
        [SerializeField] private float _hpMultiplier = 3f;
        [SerializeField] private float _durationSeconds = 2f;
        [SerializeField] private bool _invincibleDuringCast = true;

        /// <summary>発動に必要なゲージ量。</summary>
        public float GaugeCost => _gaugeCost;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_gaugeCost <= 0f)
            {
                report.Error(name + ": GaugeCost must be > 0.");
            }

            if (_hpMultiplier < 0f)
            {
                report.Error(name + ": HpMultiplier must be >= 0.");
            }
        }
    }
}
