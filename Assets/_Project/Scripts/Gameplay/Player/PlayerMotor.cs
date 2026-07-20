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

        private void Reset()
        {
            _root = GetComponent<PlayerRoot>();
        }

        private void FixedUpdate()
        {
            if (_input == null)
            {
                _input = PlayerInputProvider.Current;
            }

            if (_input == null || _root == null || _root.Body == null || _movement == null)
            {
                return;
            }

            float speed = _movement.MoveSpeed * SpeedMultiplier;
            Vector3 velocity = PlayerMovementCalculator.ToPlanarVelocity(_input.Move, speed);

            Rigidbody body = _root.Body;
            // Y は物理側の値を保持（XZ のみ制御）。壁との衝突は物理が解決し、接線方向へ滑る。
            body.linearVelocity = new Vector3(velocity.x, body.linearVelocity.y, velocity.z);
        }
    }
}
