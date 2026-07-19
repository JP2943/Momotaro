using UnityEngine;

namespace Momotaro.Data.Player
{
    /// <summary>
    /// Player の移動チューニング値（Phase1 P1-03）。速度等の数値を Component へ散在させず本 Data に集約する。
    /// ガード移動倍率（P1-07）等は後続タスクで追加する。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Player_Movement", menuName = "Momotaro/Data/Player/Movement Data", order = 0)]
    public sealed class PlayerMovementData : GameDataAsset
    {
        [Header("Movement")]
        [Tooltip("通常移動速度（m/s 相当）。")]
        [SerializeField] private float _moveSpeed = 5f;

        [Range(0f, 1f)]
        [Tooltip("ガード保持中の速度倍率（P1-07 で使用）。")]
        [SerializeField] private float _guardSpeedMultiplier = 0.4f;

        /// <summary>通常移動速度。</summary>
        public float MoveSpeed => _moveSpeed;

        /// <summary>ガード移動の速度倍率。</summary>
        public float GuardSpeedMultiplier => _guardSpeedMultiplier;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_moveSpeed <= 0f)
            {
                report.Error(name + ": MoveSpeed must be > 0.");
            }

            if (_guardSpeedMultiplier < 0f || _guardSpeedMultiplier > 1f)
            {
                report.Error(name + ": GuardSpeedMultiplier must be within [0, 1].");
            }
        }
    }
}
