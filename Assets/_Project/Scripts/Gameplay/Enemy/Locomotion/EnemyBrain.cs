using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Slots;
using Momotaro.Gameplay.Enemy.Threat;
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

        [Tooltip("Slot 待ち・画面外で攻撃できない時に対象周りを周回する 1 フレームの角度（度。棒立ち回避＝包囲・威嚇）。単体敵の予備動作。")]
        [SerializeField] private float _slotWaitStepDegrees = 25f;

        [Tooltip("複数敵の包囲リング半径＝停止距離×この比率（攻撃帯の内側で到達後そのまま攻撃を試みられる値。§8.1）。")]
        [SerializeField] private float _surroundRadiusRatio = 0.9f;

        private EnemyActor _actor;
        private EnemyMotor _motor;
        private EnemyPerception _perception;
        private EnemyAttackController _combat;
        private EnemyEncounter _encounter;
        private EnemyThreatTracker _threat;
        private bool _encounterResolved;
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
            ResolveEncounter();
        }

        private void OnDisable()
        {
            if (_encounter != null && _actor != null)
            {
                _encounter.Surround.Unregister(_actor.DamageableId); // 無効化で包囲参加を外す。
            }
        }

        private void ResolveEncounter()
        {
            if (_encounterResolved)
            {
                return;
            }

            _encounter = GetComponentInParent<EnemyEncounter>(); // 1 回だけ解決（毎フレーム探索しない）。
            _threat = GetComponent<EnemyThreatTracker>();        // Threat 選択対象を攻撃開始対象へ渡す（req1）。
            _encounterResolved = true;
        }

        private void EnsureRefs()
        {
            if (_actor == null) _actor = GetComponent<EnemyActor>();
            if (_motor == null) _motor = GetComponent<EnemyMotor>();
            if (_perception == null) _perception = GetComponent<EnemyPerception>();
            if (_combat == null) _combat = GetComponent<EnemyAttackController>();
            ResolveEncounter(); // Awake/OnEnable 未実行（動的生成）でも Threat/Encounter を解決する。
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

            // 包囲参加の登録／解除（交戦中のみ）。複数敵は対象周囲へ均等配置して単縦列を防ぐ（§8.1）。
            bool engaged = output.Mode == EnemyEngagementMode.Chase || output.Mode == EnemyEngagementMode.Hold
                || output.Mode == EnemyEngagementMode.Reposition;
            UpdateSurroundMembership(engaged);

            // 停止帯（Hold）で攻撃を試みる。攻撃後待機中は撃たない（連打防止）。開始したら次フレームから攻撃制御へ委譲する。
            bool canTryAttack = output.Mode == EnemyEngagementMode.Hold && hasTarget && _combat != null
                && !_combat.IsAttacking && !_postAttack.IsWaiting;
            // Threat 選択対象を照準対象として渡し、攻撃終了まで固定させる（req1/2）。位置は最終確認位置をフォールバックに使う。
            IPerceptionTarget attackTarget = _threat != null ? _threat.CurrentTarget : null;
            bool started = canTryAttack && _combat.TryStartAttack(attackTarget, targetPos, Vector3.zero);

            // 突進のみ Chase（間合いの外＝停止距離より遠い）でも、使用射程（5m）・角度を満たせば開始し、離れた対象へ間合いを詰める（§9.3）。
            // 通常/強/ガード不能は上の Hold 帯でのみ開始する（TryStartApproachAttack が突進系以外を除外する）。
            if (!started && output.Mode == EnemyEngagementMode.Chase && hasTarget && _combat != null
                && !_combat.IsAttacking && !_postAttack.IsWaiting && _combat.HasApproachAttack)
            {
                started = _combat.TryStartApproachAttack(attackTarget, targetPos, Vector3.zero);
            }

            if (started || _motor == null)
            {
                return; // 攻撃開始（motor は攻撃制御が Stop／Facing 済み）／motor 無しは以降の移動指示なし。
            }

            // 複数敵の包囲：交戦中の非攻撃敵は割り当てられたリング位置へ向かい対象を取り囲む（列にならない）。到達後は攻撃帯内で待機し、
            // Slot が空けば次フレームの Hold 判定から攻撃を試みる。敵同士は物理衝突するが目標点が分散するため停滞しにくい。
            if (engaged && hasTarget && _encounter != null && _encounter.Surround.Count >= 2
                && _encounter.Surround.TryGetIndex(_actor.DamageableId, out int ringIndex))
            {
                Vector3 ring = SurroundRing.RingPosition(targetPos, r.StopDistance * _surroundRadiusRatio,
                    ringIndex, _encounter.Surround.Count);
                _motor.SetMoveTarget(ring);
                _motor.SetFacing(targetPos - selfPos); // 囲みつつ対象へ向き続ける。
                float dPlayer = VisionCheck.PlanarDistance(selfPos, targetPos);
                _actor.RequestState(dPlayer <= r.StopDistance ? EnemyState.Reposition : EnemyState.Chase,
                    EnemyStateChangeReason.PerceivedTarget);
                UpdateBlockedLog(RepositionReason.SlotWait);
                return;
            }

            // 単体敵で攻撃できない停止帯（Slot 待ち・画面外）は棒立ちにせず対象周りを周回して威嚇する（§8.1）。
            if (canTryAttack)
            {
                float sign = SlotWaitReposition.DirectionSign(_actor.DamageableId);
                Vector3 orbit = SlotWaitReposition.OrbitTarget(selfPos, targetPos, r.StopDistance, sign, _slotWaitStepDegrees);
                _motor.SetMoveTarget(orbit);
                _motor.SetFacing(targetPos - selfPos); // 周回しても対象へ向き続ける。
                _actor.RequestState(EnemyState.Reposition, EnemyStateChangeReason.PerceivedTarget);
                UpdateBlockedLog(RepositionReason.SlotWait);
                return;
            }

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

        private void UpdateSurroundMembership(bool engaged)
        {
            if (_encounter == null || _actor == null)
            {
                return;
            }

            if (engaged)
            {
                _encounter.Surround.Register(_actor.DamageableId);
            }
            else
            {
                _encounter.Surround.Unregister(_actor.DamageableId);
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
