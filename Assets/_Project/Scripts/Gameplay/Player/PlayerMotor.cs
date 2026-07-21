using Momotaro.Data.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の移動実行（Phase1 P1-03）。<see cref="IPlayerInput"/> の Move を読み、
    /// <see cref="PlayerMovementCalculator"/> で XZ 平面速度を求め、Rigidbody を FixedUpdate で動かす。
    /// 通常移動に Transform 直接書換えは行わない。速度倍率でガード移動（P1-07）に対応する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private PlayerRoot _root;
        [SerializeField] private PlayerMovementData _movement;

        private IPlayerInput _input;

        /// <summary>速度倍率。ガード保持中は 0.4 等に設定される（P1-07）。既定 1。</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// 攻撃中の移動抑制（Phase2 P2-03B）。true の間は Move 入力による移動を行わず、
        /// <see cref="StepVelocity"/>（踏み込み）だけを XZ 速度として適用する。
        /// </summary>
        public bool MovementSuppressed { get; set; }

        /// <summary>踏み込みの XZ 速度（World）。抑制中に適用され、壁は物理が解決して滑る。既定ゼロ。</summary>
        public Vector3 StepVelocity { get; set; }

        private void Reset()
        {
            _root = GetComponent<PlayerRoot>();
        }

        private void FixedUpdate()
        {
            if (_root == null || _root.Body == null)
            {
                return;
            }

            Rigidbody body = _root.Body;

            // 攻撃中：自由移動を止め、踏み込み速度のみ適用（壁との衝突は物理が解決）。
            if (MovementSuppressed)
            {
                body.linearVelocity = new Vector3(StepVelocity.x, body.linearVelocity.y, StepVelocity.z);
                return;
            }

            if (_input == null)
            {
                _input = PlayerInputProvider.Current;
            }

            if (_input == null || _movement == null)
            {
                return;
            }

            float speed = _movement.MoveSpeed * SpeedMultiplier;
            Vector3 velocity = PlayerMovementCalculator.ToPlanarVelocity(_input.Move, speed);

            // Y は物理側の値を保持（XZ のみ制御）。壁との衝突は物理が解決し、接線方向へ滑る。
            body.linearVelocity = new Vector3(velocity.x, body.linearVelocity.y, velocity.z);
        }
    }
}
