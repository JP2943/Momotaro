using Momotaro.Data.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の状態と、ガードに伴う効果（向き固定・速度倍率）を統括する（Phase1 P1-06/P1-07）。
    /// 入力から <see cref="PlayerStateMachine"/> を駆動し、Motor（速度倍率）と Facing（ロック）へ反映する。
    /// 移動そのものは <see cref="PlayerMotor"/>、向き決定は <see cref="PlayerFacing"/> が担い、責務を分離する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStateController : MonoBehaviour
    {
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private PlayerFacing _facing;
        [SerializeField] private PlayerMovementData _movement;

        [Range(0f, 1f)]
        [Tooltip("この大きさ未満の移動入力は静止扱い。")]
        [SerializeField] private float _moveThreshold = 0.1f;

        private readonly PlayerStateMachine _machine = new PlayerStateMachine();
        private IPlayerInput _input;
        private bool _guarding;

        /// <summary>現在の Gameplay 状態（Visual が参照する）。</summary>
        public PlayerState Current => _machine.Current;

        private void Update()
        {
            if (_input == null)
            {
                _input = PlayerInputProvider.Current;
            }

            bool enabled = _input != null;
            bool guarding = enabled && _input.GuardHeld;
            if (guarding != _guarding)
            {
                _guarding = guarding;
                ApplyGuard(guarding);
            }

            bool isMoving = enabled && _input.Move.sqrMagnitude > _moveThreshold * _moveThreshold;
            _machine.Tick(enabled, isMoving, guarding);
        }

        private void ApplyGuard(bool guarding)
        {
            if (_facing != null)
            {
                _facing.IsLocked = guarding;
            }

            if (_motor != null && _movement != null)
            {
                _motor.SpeedMultiplier = GuardMovement.SpeedMultiplier(guarding, _movement.GuardSpeedMultiplier);
            }
        }
    }
}
