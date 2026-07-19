using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>状態異常のパラメータ雛形（仕様書 4.16）。</summary>
    [CreateAssetMenu(fileName = "SO_Status_New", menuName = "Momotaro/Data/Combat/Status Effect Data", order = 4)]
    public sealed class StatusEffectData : GameDataAsset
    {
        [Header("Status Effect")]
        [SerializeField] private float _durationSeconds = 5f;
        [SerializeField] private float _tickIntervalSeconds = 1f;
        [SerializeField] private bool _stackable;

        /// <summary>効果の持続秒。</summary>
        public float DurationSeconds => _durationSeconds;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_durationSeconds <= 0f)
            {
                report.Error(name + ": DurationSeconds must be > 0.");
            }

            if (_tickIntervalSeconds <= 0f)
            {
                report.Error(name + ": TickIntervalSeconds must be > 0.");
            }
        }
    }
}
