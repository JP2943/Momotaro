using Momotaro.Data.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の状態と、ガード・攻撃に伴う効果（向き固定・速度倍率）を統括する（Phase1 P1-06/P1-07・Phase2 P2-02）。
    /// 入力から <see cref="PlayerStateMachine"/> を駆動し、Motor（速度倍率）と Facing（ロック）へ反映する。
    /// 移動そのものは <see cref="PlayerMotor"/>、向き決定は <see cref="PlayerFacing"/> が担い、責務を分離する。
    ///
    /// P2-02 では攻撃の「入力・状態・向き固定・先行入力（Buffer）・遮断/Reset」までを扱う。
    /// 実 Hitbox・踏み込み・3 段コンボ・ダメージは後続 Task（P2-03B 以降）。
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

        [Min(0f)]
        [Tooltip("攻撃の先行入力（Buffer）を保持する秒数。試作値 0.30（本書 §4.2）。段別詳細は P2-03B。")]
        [SerializeField] private float _attackBufferSeconds = 0.30f;

        [Min(0f)]
        [Tooltip("攻撃状態を維持する秒数の暫定値。P2-03B で段別の予備/判定/後隙へ置き換える。")]
        [SerializeField] private float _attackStateSeconds = 0.40f;

        private readonly PlayerStateMachine _machine = new PlayerStateMachine();
        private AttackInputBuffer _attackBuffer;
        private IPlayerInput _input;

        /// <summary>現在の Gameplay 状態（Visual が参照する）。</summary>
        public PlayerState Current => _machine.Current;

        private void Awake()
        {
            _attackBuffer = new AttackInputBuffer(_attackBufferSeconds);
        }

        private void OnDisable()
        {
            ResetToNeutral();
        }

        /// <summary>
        /// 状態・向きロック・速度倍率・先行入力を中立へ戻す（Disable 時）。次回有効化時に入力を取り直す。
        /// </summary>
        public void ResetToNeutral()
        {
            _input = null;
            _machine.Reset();
            _attackBuffer?.Clear();

            if (_facing != null)
            {
                _facing.IsLocked = false;
            }

            if (_motor != null)
            {
                _motor.SpeedMultiplier = 1f;
            }
        }

        private void Update()
        {
            if (_attackBuffer == null)
            {
                _attackBuffer = new AttackInputBuffer(_attackBufferSeconds);
            }

            if (_input == null)
            {
                _input = PlayerInputProvider.Current;
            }

            bool active = _input != null && _input.Active;

            // 先行入力の取り込みと時間経過。遮断中は預かった入力を破棄する。
            if (active)
            {
                if (_input.ConsumeAttackPressed())
                {
                    _attackBuffer.Buffer();
                }

                _attackBuffer.Tick(Time.deltaTime);
            }
            else
            {
                _attackBuffer.Clear();
            }

            bool guarding = active && _input.GuardHeld;
            bool isMoving = active && _input.Move.sqrMagnitude > _moveThreshold * _moveThreshold;

            // 攻撃中でなく、有効な先行入力があれば攻撃を実行要求する（消費は 1 押下 1 回）。
            bool wasAttacking = _machine.IsAttacking;
            bool attackRequested = active && !wasAttacking && _attackBuffer.HasBuffered && _attackBuffer.TryConsume();

            _machine.Tick(active, isMoving, guarding, attackRequested, Time.deltaTime, _attackStateSeconds);

            bool attacking = _machine.Current == PlayerState.Attack;

            // 攻撃開始のフレームで、その時点の Move 入力から向きを明示確定する。
            // PlayerFacing.Update との実行順に依存させず、以降は攻撃終了まで固定する。
            if (attacking && !wasAttacking && _facing != null)
            {
                _facing.ConfirmFromInput(active ? _input.Move : Vector2.zero);
            }

            ApplyStateEffects(guarding, attacking);
        }

        /// <summary>
        /// ガード・攻撃に応じて向きロックと速度倍率を反映する。向きはガード中または攻撃中に固定する
        /// （攻撃は「開始時 Facing 固定」＝継続中ロック）。速度倍率はガードのみ（攻撃中の移動制御は P2-03B）。
        /// </summary>
        private void ApplyStateEffects(bool guarding, bool attacking)
        {
            if (_facing != null)
            {
                _facing.IsLocked = guarding || attacking;
            }

            if (_motor != null && _movement != null)
            {
                _motor.SpeedMultiplier = GuardMovement.SpeedMultiplier(guarding, _movement.GuardSpeedMultiplier);
            }
        }
    }
}
