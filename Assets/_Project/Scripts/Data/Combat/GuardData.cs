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
        [Tooltip("ガード解除直後の JG 受付時間（秒。試作 0.075）。仕様書 3.3。")]
        [SerializeField] private float _justGuardReleaseWindowSeconds = 0.075f;
        [Tooltip("ガード解除時の連打ペナルティ時間（秒。試作 0.20）。")]
        [SerializeField] private float _releasePenaltySeconds = 0.20f;

        /// <summary>ジャストガード受付時間（押下窓。秒）。</summary>
        public float JustGuardWindowSeconds => _justGuardWindowSeconds;

        /// <summary>ガード解除直後の JG 受付時間（秒）。</summary>
        public float JustGuardReleaseWindowSeconds => _justGuardReleaseWindowSeconds;

        /// <summary>ガード解除時の連打ペナルティ時間（秒）。</summary>
        public float ReleasePenaltySeconds => _releasePenaltySeconds;

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
