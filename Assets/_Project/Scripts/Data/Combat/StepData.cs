using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>ステップ回避のパラメータ雛形（仕様書 3.4）。</summary>
    [CreateAssetMenu(fileName = "SO_Step_New", menuName = "Momotaro/Data/Combat/Step Data", order = 2)]
    public sealed class StepData : GameDataAsset
    {
        [Header("Step")]
        [SerializeField] private float _distance = 3f;
        [SerializeField] private float _invincibleSeconds = 0.2f;
        [SerializeField] private float _staminaCost = 20f;
        [SerializeField] private float _cooldownSeconds = 0.5f;

        /// <summary>無敵時間（秒）。</summary>
        public float InvincibleSeconds => _invincibleSeconds;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_invincibleSeconds < 0f)
            {
                report.Error(name + ": InvincibleSeconds must be >= 0.");
            }

            if (_staminaCost < 0f)
            {
                report.Error(name + ": StaminaCost must be >= 0.");
            }
        }
    }
}
