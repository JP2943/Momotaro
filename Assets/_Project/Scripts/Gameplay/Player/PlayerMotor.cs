using Momotaro.Data.Player;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の移動実行（Phase1 P1-03）。<see cref="IPlayerInput"/> の Move を読み、
    /// <see cref="PlayerMovementCalculator"/> で XZ 平面速度を求め、Rigidbody を FixedUpdate で動かす。
    /// 通常移動に Transform 直接書換えは行わない。速度倍率でガード移動（P1-07）に対応する。
    ///
    /// Phase3.5 P3.5-08A：通常ヒットバック／ガードバックの外部変位（<see cref="IReactionMotor"/>）を受け付ける。反応中は入力・
    /// 抑制より優先して XZ 速度を上書きし、Y は物理値を保持（Y 座標不変）、壁は物理が停止する。反応は時間で自然減衰し、
    /// Disable で確実に打ち切る（残留変位を残さない）。必殺技の大きな Knockback とは別契約。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMotor : MonoBehaviour, IReactionMotor
    {
        [SerializeField] private PlayerRoot _root;
        [SerializeField] private PlayerMovementData _movement;

        private IPlayerInput _input;
        private readonly ExternalReactionMotion _reaction = new ExternalReactionMotion();

        /// <summary>速度倍率。ガード保持中は 0.4 等に設定される（P1-07）。既定 1。</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// 攻撃中の移動抑制（Phase2 P2-03B）。true の間は Move 入力による移動を行わず、
        /// <see cref="StepVelocity"/>（踏み込み）だけを XZ 速度として適用する。
        /// </summary>
        public bool MovementSuppressed { get; set; }

        /// <summary>踏み込みの XZ 速度（World）。抑制中に適用され、壁は物理が解決して滑る。既定ゼロ。</summary>
        public Vector3 StepVelocity { get; set; }

        /// <inheritdoc />
        public void PushReaction(Vector3 direction, float distance, float seconds)
        {
            _reaction.Begin(direction, distance, seconds);
        }

        /// <inheritdoc />
        public void ClearReaction()
        {
            _reaction.Clear();
        }

        private void Reset()
        {
            _root = GetComponent<PlayerRoot>();
        }

        private void OnDisable()
        {
            _reaction.Clear(); // Disable・Scene 離脱で残留変位を残さない（§7.4）。
        }

        private void FixedUpdate()
        {
            if (_root == null || _root.Body == null)
            {
                return;
            }

            Rigidbody body = _root.Body;

            // P3.5-08A：ヒットバック／ガードバックは入力・抑制より優先。XZ を反応速度で上書きし Y は物理値を保持（Y 不変）。
            if (_reaction.IsActive)
            {
                Vector3 rv = _reaction.CurrentVelocity;
                body.linearVelocity = new Vector3(rv.x, body.linearVelocity.y, rv.z);
                _reaction.Tick(Time.fixedDeltaTime);
                return;
            }

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
