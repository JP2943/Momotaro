using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>通常ガード／ジャストガードのパラメータ雛形（仕様書 3.2 / 3.3）。</summary>
    [CreateAssetMenu(fileName = "SO_Guard_New", menuName = "Momotaro/Data/Combat/Guard Data", order = 1)]
    public sealed class GuardData : GameDataAsset
    {
        [Header("Guard")]
        [SerializeField] private float _guardAngle = 120f;
        [SerializeField] private float _staminaRegenPerSecond = 15f;

        [Header("Just Guard")]
        [SerializeField] private float _justGuardWindowSeconds = 0.15f;

        /// <summary>ジャストガード受付時間（秒）。</summary>
        public float JustGuardWindowSeconds => _justGuardWindowSeconds;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_justGuardWindowSeconds <= 0f)
            {
                report.Error(name + ": JustGuardWindowSeconds must be > 0.");
            }

            if (_guardAngle <= 0f || _guardAngle > 360f)
            {
                report.Error(name + ": GuardAngle must be within (0, 360].");
            }
        }
    }
}
