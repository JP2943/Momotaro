using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Locomotion
{
    /// <summary>
    /// 敵の行動判断（Phase3 P3-03。§2.2/§5）。認識（<see cref="EnemyPerception"/>）と活動範囲から追跡・間合い・帰還を
    /// <see cref="EnemyEngagementDecider"/> で決め、<see cref="EnemyMotor"/> を駆動し、公開状態を <see cref="EnemyActor.RequestState"/>
    /// で更新する（状態の所有者）。被弾由来（Down/Stunned/Stagger）中は移動せず状態を EnemyActor に委ねる。範囲超過で帰還し、
    /// 帰還中は認識を抑制、初期位置到達で待機してから通常へ復帰する。経路不能は停止＋Debug 理由（壁抜け・振動は物理で防止）。
    /// 攻撃・Slot は P3-04/07。Pause／会話中は判断しない。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyActor))]
    [RequireComponent(typeof(EnemyMotor))]
    public sealed class EnemyBrain : MonoBehaviour
    {
        [Tooltip("過近判定の比率（停止距離×比率 未満で後退）。")]
        [SerializeField] private float _tooCloseRatio = 0.6f;

        [Tooltip("到達判定の許容距離（m。初期位置到達・調査到達）。")]
        [SerializeField] private float _arriveEpsilon = 0.35f;

        private EnemyActor _actor;
        private EnemyMotor _motor;
        private EnemyPerception _perception;
        private EnemyAttackController _combat;
        private Vector3 _home;
        private bool _homeSet;
        private bool _configured;
        private EnemyEngagementMode _mode = EnemyEngagementMode.Idle;
        private float _returnWait;
        private bool _prevSuppress;
        private bool _loggedBlocked;
        private bool _wasAttacking;
        private readonly EnemyPostAttackWait _postAttack = new EnemyPostAttackWait();
        private readonly System.Random _waitRng = new System.Random();

        /// <summary>攻撃後待機の残り秒（Debug/テスト用。§9.1）。</summary>
        public float PostAttackWaitRemaining => _postAttack.Remaining;

        /// <summary>攻撃後待機中か。</summary>
        public bool IsPostAttackWaiting => _postAttack.IsWaiting;

        /// <summary>現在の交戦モード（Debug・テスト用）。</summary>
        public EnemyEngagementMode Mode => _mode;

        /// <summary>初期位置（活動範囲の中心）。</summary>
        public Vector3 Home => _home;

        private void Awake()
        {
            EnsureRefs();
        }

        private void OnEnable()
        {
            EnsureRefs();
            CaptureHome();
            ConfigureMotor();
        }

        private void EnsureRefs()
        {
            if (_actor == null) _actor = GetComponent<EnemyActor>();
            if (_motor == null) _motor = GetComponent<EnemyMotor>();
            if (_perception == null) _perception = GetComponent<EnemyPerception>();
            if (_combat == null) _combat = GetComponent<EnemyAttackController>();
        }

        private void CaptureHome()
        {
            if (!_homeSet)
            {
                _home = transform.position;
                _homeSet = true;
            }
        }

        private void ConfigureMotor()
        {
            if (_configured || _motor == null || _actor == null || _actor.Archetype == null)
            {
                return;
            }

            // 停止半径は小さく取り、停止帯での保持は Decider（Hold→Stop）で制御する。
            _motor.Configure(_actor.Archetype.MoveSpeed, _actor.Archetype.TurnSpeedDegrees, 0.1f);
            _configured = true;
        }

        private void Update()
        {
            if (!IsGameplayActive())
            {
                return; // Pause／会話／ローディング中は判断しない。
            }

            TickBrain(Time.deltaTime);
        }

        /// <summary>1 判断分を進める（Update から、またはテストが決定的に呼ぶ）。移動の物理適用は <see cref="EnemyMotor"/> の責務。</summary>
        public void TickBrain(float deltaTime)
        {
            EnsureRefs();
            if (_actor == null)
            {
                return;
            }

            CaptureHome();
            ConfigureMotor();

            // 被弾由来（Down/Stunned/Stagger）中は移動せず、状態は EnemyActor が管理する（優先度）。
            if (EnemyStatePriority.IsForcedByHit(_actor.State))
            {
                _motor?.Stop();
                return;
            }

            // 攻撃中は移動・状態を攻撃制御へ委譲する（EnemyAttackController が Prepare/Active/Recovery を駆動）。
            if (_combat != null && _combat.IsAttacking)
            {
                _wasAttacking = true;
                return;
            }

            EnemyArchetypeReadout r = ReadArchetype();

            // 攻撃終了フレーム：攻撃後待機を開始する（連打防止。§9.1「攻撃後待機 0.7〜1.2 秒」）。
            if (_wasAttacking)
            {
                _wasAttacking = false;
                _postAttack.Begin(EnemyPostAttackWait.PickDuration(r.PostAttackWaitMin, r.PostAttackWaitMax, (float)_waitRng.NextDouble()));
            }

            _postAttack.Tick(deltaTime);
            PerceptionPhase phase = _perception != null ? _perception.Phase : PerceptionPhase.Unaware;
            bool hasTarget = _perception != null && _perception.HasLastKnownPosition;
            Vector3 targetPos = _perception != null ? _perception.LastKnownPosition : transform.position;
            Vector3 selfPos = _actor.WorldPosition;

            var input = new EngagementInput(
                phase, hasTarget, targetPos, selfPos, _home,
                r.ActivityRadius, r.StopDistance, r.StopDistance * _tooCloseRatio, _arriveEpsilon,
                _mode, _returnWait, r.ReturnWaitSeconds, deltaTime);
            EngagementOutput output = EnemyEngagementDecider.Decide(input);

            _mode = output.Mode;
            _returnWait = output.ReturnWaitRemaining;

            // 帰還突入で認識をリセットしてから抑制（再認識しない）。復帰でセンサ再開（初期位置到達後待機明け）。
            if (_perception != null)
            {
                if (output.SuppressPerception && !_prevSuppress)
                {
                    _perception.ResetPerception();
                }

                _perception.SensingPaused = output.SuppressPerception;
            }

            _prevSuppress = output.SuppressPerception;

            _actor.RequestState(output.State, MapReason(output.State));

            if (_motor != null)
            {
                if (output.HasMoveTarget)
                {
                    _motor.SetMoveTarget(output.MoveTarget);
                    _motor.SetFacing(output.MoveTarget - selfPos);
                }
                else
                {
                    _motor.Stop();
                    if (hasTarget)
                    {
                        _motor.SetFacing(targetPos - selfPos); // 停止中も対象へ向き続ける（認識継続）。
                    }
                }

                UpdateBlockedLog(output.RepositionReason);
            }

            // 停止帯（Hold）で攻撃を試みる。攻撃後待機中は撃たない（連打防止）。開始したら次フレームから攻撃制御へ委譲する。
            if (output.Mode == EnemyEngagementMode.Hold && hasTarget && _combat != null && !_combat.IsAttacking
                && !_postAttack.IsWaiting)
            {
                _combat.TryStartAttack(targetPos, Vector3.zero);
            }
        }

        private void UpdateBlockedLog(RepositionReason reason)
        {
            if (_motor.IsBlocked)
            {
                if (!_loggedBlocked)
                {
                    _loggedBlocked = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning("EnemyBrain path blocked: actor=" + _actor.DamageableId
                        + " mode=" + _mode + " reason=" + reason + " (停止。壁抜け・振動は物理で防止)。");
#endif
                }
            }
            else
            {
                _loggedBlocked = false;
            }
        }

        private readonly struct EnemyArchetypeReadout
        {
            public float ActivityRadius { get; }
            public float StopDistance { get; }
            public float ReturnWaitSeconds { get; }
            public float PostAttackWaitMin { get; }
            public float PostAttackWaitMax { get; }

            public EnemyArchetypeReadout(float activityRadius, float stopDistance, float returnWaitSeconds,
                float postAttackWaitMin, float postAttackWaitMax)
            {
                ActivityRadius = activityRadius;
                StopDistance = stopDistance;
                ReturnWaitSeconds = returnWaitSeconds;
                PostAttackWaitMin = postAttackWaitMin;
                PostAttackWaitMax = postAttackWaitMax;
            }
        }

        private EnemyArchetypeReadout ReadArchetype()
        {
            var a = _actor.Archetype;
            if (a == null)
            {
                return new EnemyArchetypeReadout(12f, 1.6f, 1f, 0.7f, 1.2f);
            }

            return new EnemyArchetypeReadout(a.ActivityRadius, a.StopDistance, a.ReturnWaitSeconds,
                a.PostAttackWaitMin, a.PostAttackWaitMax);
        }

        private static EnemyStateChangeReason MapReason(EnemyState state)
        {
            switch (state)
            {
                case EnemyState.Chase:
                case EnemyState.Reposition:
                    return EnemyStateChangeReason.PerceivedTarget;
                case EnemyState.Alert:
                    return EnemyStateChangeReason.TargetInRange;
                case EnemyState.Suspicious:
                    return EnemyStateChangeReason.SuspiciousStimulus;
                case EnemyState.Return:
                    return EnemyStateChangeReason.LeftActivityRange;
                default:
                    return EnemyStateChangeReason.LostTarget;
            }
        }

        private static bool IsGameplayActive()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return true;
            }

            GameMode m = modes.Current;
            return m == GameMode.Exploration || m == GameMode.Combat;
        }
    }
}
