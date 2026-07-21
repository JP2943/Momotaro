using Momotaro.Data.Combat;
using Momotaro.Data.Player;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の状態と、ガード・攻撃に伴う効果を統括する（Phase1 P1-06/P1-07・Phase2 P2-02/P2-03B）。
    /// 入力から <see cref="PlayerStateMachine"/> と <see cref="AttackComboMachine"/> を駆動し、Motor（移動抑制・
    /// 踏み込み・速度倍率）と Facing（段開始時の向き確定・ロック）へ反映する。判定中は Hitbox（OverlapBox）で
    /// 対象を検出し、段ごとの Swing Token（<see cref="HitId"/>）で同一対象への多重ヒットを防ぐ。
    ///
    /// P2-03B の範囲：3 段コンボ・踏み込み（壁貫通なし）・時間駆動 Hitbox・段間方向再確定・キャンセル窓・
    /// 中断時 Hitbox 消去まで。HP/体幹/ひるみの実適用は対象外（対象側 <see cref="IDamageable"/> と後続 Task）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStateController : MonoBehaviour, ICombatActor
    {
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private PlayerFacing _facing;
        [SerializeField] private PlayerMovementData _movement;

        [Range(0f, 1f)]
        [Tooltip("この大きさ未満の移動入力は静止扱い。")]
        [SerializeField] private float _moveThreshold = 0.1f;

        [Header("Attack (P2-03B)")]
        [Tooltip("通常攻撃コンボ構成（段順 AttackData ＋ 先行入力秒）。未割当なら攻撃不可。")]
        [SerializeField] private PlayerAttackComboData _attackCombo;

        [Tooltip("Hitbox 中心の Facing 方向オフセット（m）。")]
        [SerializeField] private float _hitboxForwardOffset = 0.8f;

        [Tooltip("Hitbox の半径（各軸の half extent, m）。")]
        [SerializeField] private Vector3 _hitboxHalfExtents = new Vector3(0.6f, 0.5f, 0.6f);

        [Tooltip("Hitbox 中心の高さ（m）。")]
        [SerializeField] private float _hitboxHeight = 0.5f;

        [Tooltip("命中対象の Layer。")]
        [SerializeField] private LayerMask _targetMask = ~0;

        private readonly PlayerStateMachine _machine = new PlayerStateMachine();
        private readonly HitInstanceAllocator _hitAllocator = new HitInstanceAllocator();
        private readonly MultiHitTracker _hitTracker = new MultiHitTracker();
        private readonly Collider[] _overlapBuffer = new Collider[16];

        private AttackComboMachine _combo;
        private AttackInputBuffer _attackBuffer;
        private IPlayerInput _input;
        private HitId _currentSwing;

        /// <summary>現在の Gameplay 状態（Visual が参照する）。</summary>
        public PlayerState Current => _machine.Current;

        /// <summary>現在の攻撃段（1..3、非攻撃時は直近値）。Visual がクリップ選択に用いる。</summary>
        public int AttackStage { get; private set; } = 1;

        // ---- ICombatActor（攻撃者としての同定） ----

        /// <inheritdoc />
        public CombatFaction Faction => CombatFaction.Player;

        /// <inheritdoc />
        public int FloorId => 0;

        /// <inheritdoc />
        public Vector3 WorldPosition => transform.position;

        /// <inheritdoc />
        public Vector3 Forward => FacingToVector(_facing != null ? _facing.Current : FacingDirection.Down);

        private void Awake()
        {
            EnsureRuntime();
        }

        private void OnDisable()
        {
            ResetToNeutral();
        }

        /// <summary>状態・攻撃・ロック・移動抑制・先行入力を中立へ戻す（Disable 時）。</summary>
        public void ResetToNeutral()
        {
            _input = null;
            _machine.Reset();
            _combo?.Interrupt();
            _attackBuffer?.Clear();
            _hitTracker.Clear();

            if (_facing != null)
            {
                _facing.IsLocked = false;
            }

            if (_motor != null)
            {
                _motor.SpeedMultiplier = 1f;
                _motor.MovementSuppressed = false;
                _motor.StepVelocity = Vector3.zero;
            }
        }

        private void EnsureRuntime()
        {
            if (_combo == null && _attackCombo != null && _attackCombo.StageCount > 0)
            {
                int n = _attackCombo.StageCount;
                var timings = new StageTiming[n];
                for (int i = 0; i < n; i++)
                {
                    AttackData d = _attackCombo.Stage(i);
                    timings[i] = new StageTiming(d.StartupSeconds, d.ActiveSeconds, d.RecoverySeconds, d.CancelWindowStartSeconds);
                }

                _combo = new AttackComboMachine(timings);
            }

            if (_attackBuffer == null)
            {
                float bufferSeconds = _attackCombo != null ? _attackCombo.BufferSeconds : 0.30f;
                _attackBuffer = new AttackInputBuffer(bufferSeconds);
            }
        }

        private void Update()
        {
            EnsureRuntime();

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

            DriveCombo(active);

            bool attacking = _combo != null && _combo.IsActive;
            _machine.Tick(active, isMoving, guarding, attacking);

            // 段開始フレーム：向き再確定・新 Swing Token・段番号更新（踏み込みは ApplyAttackMotion）。
            if (_combo != null && _combo.StageJustStarted)
            {
                AttackStage = _combo.Stage;
                _currentSwing = _hitAllocator.NextSingle();
                if (_facing != null)
                {
                    _facing.ConfirmFromInput(active ? _input.Move : Vector2.zero);
                }
            }

            ApplyAttackMotion(attacking);
            ApplyStateEffects(guarding, attacking);

            if (attacking && _combo.HitboxActive)
            {
                PollHitbox();
            }
        }

        private void DriveCombo(bool active)
        {
            if (_combo == null)
            {
                return;
            }

            _combo.Tick(Time.deltaTime);

            if (!active)
            {
                if (_combo.IsActive)
                {
                    _combo.Interrupt();
                    _hitTracker.Clear();
                }

                return;
            }

            if (_combo.IsActive)
            {
                // 連鎖（次段）は後隙満了による終了より優先する。
                if (_combo.AcceptingChain && _attackBuffer.HasBuffered && _combo.TryAdvance())
                {
                    _attackBuffer.Clear();
                }
                else if (_combo.IsComplete)
                {
                    _combo.End();
                    _hitTracker.Clear();
                }
            }

            if (!_combo.IsActive && _attackBuffer.HasBuffered && _combo.TryStart())
            {
                _attackBuffer.Clear();
                _hitTracker.Clear();
            }
        }

        private void ApplyAttackMotion(bool attacking)
        {
            if (_motor == null)
            {
                return;
            }

            if (!attacking)
            {
                _motor.MovementSuppressed = false;
                _motor.StepVelocity = Vector3.zero;
                return;
            }

            _motor.MovementSuppressed = true;

            // 踏み込みは予備動作（Startup）中のみ、Facing 方向へ。壁は物理が解決して滑る。
            Vector3 step = Vector3.zero;
            if (_combo.Phase == AttackPhase.Startup && _attackCombo != null)
            {
                AttackData d = _attackCombo.Stage(_combo.Stage - 1);
                if (d.StepDistance > 0f && d.StartupSeconds > 0f)
                {
                    step = Forward * (d.StepDistance / d.StartupSeconds);
                }
            }

            _motor.StepVelocity = step;
        }

        private void PollHitbox()
        {
            if (_attackCombo == null)
            {
                return;
            }

            Vector3 center = transform.position + Forward * _hitboxForwardOffset + Vector3.up * _hitboxHeight;
            int count = Physics.OverlapBoxNonAlloc(center, _hitboxHalfExtents, _overlapBuffer, Quaternion.identity, _targetMask, QueryTriggerInteraction.Collide);

            if (count == 0)
            {
                return;
            }

            AttackData d = _attackCombo.Stage(_combo.Stage - 1);
            AttackSnapshot snapshot = AttackSnapshot.FromData(d);
            // 体幹・ひるませは固定系統でここで確定。HP は攻撃力算入後（P2-04）のため 0。
            var damage = new HitDamage(0f, d.PoiseDamage, d.FlinchPower);

            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || ReferenceEquals(target, this))
                {
                    continue;
                }

                // 同一 Swing（段）で同一対象は 1 回だけ。段が変われば新 Token で再命中可。
                if (!_hitTracker.TryRegisterHit(_currentSwing, target))
                {
                    continue;
                }

                HitInfo hit = HitBuilder.FromSnapshot(snapshot, this, target, Forward, center, damage, _currentSwing);
                target.ReceiveHit(hit);
            }
        }

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

        private static Vector3 FacingToVector(FacingDirection facing)
        {
            switch (facing)
            {
                case FacingDirection.Up:
                    return Vector3.forward;
                case FacingDirection.Left:
                    return Vector3.left;
                case FacingDirection.Right:
                    return Vector3.right;
                default:
                    return Vector3.back;
            }
        }
    }
}
