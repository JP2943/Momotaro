using System;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat
{
    /// <summary>
    /// 敵攻撃パイプライン（Phase3 P3-04。§6）。全攻撃を共通の Prepare／Active／Recovery（<see cref="EnemyAttackMachine"/>）と
    /// 不変 <see cref="EnemyAttackSnapshot"/> で実行する。選択（<see cref="EnemyAttackSelector"/>）→照準（<see cref="EnemyAimingResolver"/>）
    /// →Active で Hitbox（OverlapBox）を出し、Phase 2 と同じ <see cref="IDamageable.ReceiveHit"/> へ渡す（無敵＞JG＞Guard＞Damage は
    /// 被弾側が担保）。同一 Swing で 1 対象 1Hit、敵 Faction には当てない。Stagger／Stunned／Down／Disable で即 Cleanup（判定・予兆解除）。
    /// Gameplay 時間は deltaTime で進め、Animator Event に依存しない。攻撃 Slot・Projectile・敵別完成 Data は後続（P3-07/08）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyActor))]
    public sealed class EnemyAttackController : MonoBehaviour
    {
        [Tooltip("同点時の tie-break 乱数シード（0 で TickCount。EditMode 再現用に固定可）。")]
        [SerializeField] private int _seed;

        [Tooltip("Hitbox の対象レイヤー（既定は全レイヤー。IDamageable と Faction で絞る）。")]
        [SerializeField] private LayerMask _targetMask = ~0;

        private EnemyActor _actor;
        private EnemyMotor _motor;
        private readonly EnemyAttackMachine _machine = new EnemyAttackMachine();
        private readonly HitInstanceAllocator _allocator = new HitInstanceAllocator();
        private readonly MultiHitTracker _hitTracker = new MultiHitTracker();
        private readonly Collider[] _overlapBuffer = new Collider[16];

        private EnemyAttackSnapshot[] _snaps;
        private AttackOption[] _options;
        private float[] _cooldownValues;
        private float[] _cooldown;
        private System.Random _rng;
        private bool _built;

        private int _selectedIndex = -1;
        private int _lastUsedIndex = -1;
        private Vector3 _aimDir = Vector3.forward;
        private HitId _currentSwing;

        /// <summary>攻撃予兆の配信チャネル（表示側が購読）。</summary>
        public EnemyTelegraphChannel Telegraph { get; } = new EnemyTelegraphChannel();

        /// <summary>攻撃中か（Prepare/Active/Recovery）。Brain はこの間 移動・状態を委譲する。</summary>
        public bool IsAttacking => _machine.IsAttacking;

        /// <summary>現在段階（Debug/テスト用）。</summary>
        public EnemyAttackMachine.Phase Phase => _machine.Current;

        /// <summary>現在の狙い方向（XZ 正規化。Debug/テスト用）。</summary>
        public Vector3 AimDirection => _aimDir;

        private void Awake()
        {
            _actor = GetComponent<EnemyActor>();
            _motor = GetComponent<EnemyMotor>();
            Build();
        }

        private void OnDisable()
        {
            CancelAttack(); // Disable でも判定・予兆を解除（Cleanup）。
        }

        private void Build()
        {
            if (_built)
            {
                return;
            }

            if (_actor == null)
            {
                _actor = GetComponent<EnemyActor>();
            }

            var a = _actor != null ? _actor.Archetype : null;
            if (a == null)
            {
                return;
            }

            int n = a.AttackCount;
            _snaps = new EnemyAttackSnapshot[n];
            _options = new AttackOption[n];
            _cooldownValues = new float[n];
            _cooldown = new float[n];
            for (int i = 0; i < n; i++)
            {
                EnemyAttackData d = a.Attack(i);
                _snaps[i] = EnemyAttackSnapshot.From(d);
                _options[i] = new AttackOption(d.UseRange, d.UseAngle, d.BaseScore);
                _cooldownValues[i] = d.CooldownSeconds;
            }

            _rng = new System.Random(_seed == 0 ? Environment.TickCount : _seed);
            _built = true;
        }

        /// <summary>
        /// 攻撃開始を試みる（Brain が停止帯で呼ぶ）。選択に成功したら Prepare へ入り true。攻撃中・候補なしは false。
        /// </summary>
        public bool TryStartAttack(Vector3 targetPos, Vector3 targetVelocity)
        {
            Build();
            if (!_built || _machine.IsAttacking || _snaps.Length == 0)
            {
                return false;
            }

            Vector3 selfPos = _actor.WorldPosition;
            float distance = VisionCheck.PlanarDistance(selfPos, targetPos);
            float angle = AngleToTarget(selfPos, _actor.Forward, targetPos);

            int index = EnemyAttackSelector.Evaluate(distance, angle, _options, _cooldown, _lastUsedIndex,
                count => _rng.Next(count), out _);
            if (index < 0)
            {
                return false;
            }

            _selectedIndex = index;
            EnemyAttackSnapshot snap = _snaps[index];
            _aimDir = EnemyAimingResolver.Resolve(snap.AimingMode, selfPos, targetPos, targetVelocity, snap.PredictSeconds);
            _currentSwing = _allocator.NextSingle();
            _hitTracker.Clear();
            _machine.Begin(snap);

            _actor.RequestState(EnemyState.AttackPrepare, EnemyStateChangeReason.AttackStarted);
            _motor?.Stop();
            _motor?.SetFacing(_aimDir);
            PublishTelegraph(EnemyTelegraphPhase.Begin, snap);
            return true;
        }

        private void Update()
        {
            if (!IsGameplayActive())
            {
                return; // Pause／会話中は攻撃時間を進めない。
            }

            Build();
            TickCooldowns(Time.deltaTime);

            if (!_machine.IsAttacking)
            {
                return;
            }

            // 中断：被弾由来（Down/Stunned/Stagger）は Cleanup し、判定・予兆を解除する。
            if (EnemyStatePriority.IsForcedByHit(_actor.State))
            {
                CancelAttack();
                return;
            }

            TickAttack(Time.deltaTime);
        }

        /// <summary>攻撃を 1 Tick 進める（Update から、またはテストが決定的に呼ぶ）。物理 Hitbox は <see cref="PollHitbox"/>。</summary>
        public void TickAttack(float deltaTime)
        {
            if (!_machine.IsAttacking)
            {
                return;
            }

            EnemyAttackSnapshot snap = _machine.Snapshot;

            // 照準の分離（§6.1）：Tracking のみ Prepare 中に角速度制限で漸進旋回し、追尾停止で固定する。
            // CurrentPosition／PredictedPosition は開始時（TryStartAttack）に確定した方向を更新しない。
            if (snap.AimingMode == EnemyAimingMode.Tracking && _machine.IsTrackingActive
                && PerceptionTargetRegistry.TryGetNearestHostile(_actor.WorldPosition, _actor.Faction, out IPerceptionTarget t))
            {
                Vector3 desired = EnemyAimingResolver.Resolve(EnemyAimingMode.CurrentPosition, _actor.WorldPosition, t.Position, Vector3.zero, 0f);
                _aimDir = EnemyAimingResolver.RotateToward(_aimDir, desired, snap.TrackingAngularSpeed * deltaTime);
                _motor?.SetFacing(_aimDir);
            }

            EnemyAttackMachine.TickResult r = _machine.Tick(deltaTime);

            if (r.EnteredActive)
            {
                _actor.RequestState(EnemyState.AttackActive, EnemyStateChangeReason.AttackAdvanced);
                PublishTelegraph(EnemyTelegraphPhase.Fire, snap);
            }

            if (_machine.IsHitboxActive)
            {
                PollHitbox(snap);
            }

            if (r.EnteredRecovery)
            {
                _actor.RequestState(EnemyState.AttackRecovery, EnemyStateChangeReason.AttackAdvanced);
                _motor?.Stop();
            }

            if (r.Finished)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _cooldown.Length)
                {
                    _cooldown[_selectedIndex] = _cooldownValues[_selectedIndex];
                }

                _lastUsedIndex = _selectedIndex;
                _hitTracker.Clear();
                PublishTelegraph(EnemyTelegraphPhase.End, snap);
                _actor.RequestState(EnemyState.Alert, EnemyStateChangeReason.AttackFinished);
            }
        }

        private void PollHitbox(in EnemyAttackSnapshot snap)
        {
            Vector3 fwd = _aimDir;
            Vector3 center = _actor.WorldPosition + fwd * snap.HitboxForwardOffset + Vector3.up * snap.HitboxHeight;
            Quaternion rot = Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z), Vector3.up);
            int count = Physics.OverlapBoxNonAlloc(center, snap.HitboxHalfExtents, _overlapBuffer, rot, _targetMask,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                var target = col.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    TryApplyHit(target, col.GetComponentInParent<ICombatActor>(), center);
                }
            }
        }

        /// <summary>
        /// 1 対象へ命中を適用する（Active 中のみ）。自身・味方（敵 Faction）は除外し、同一 Swing で 1 対象 1Hit。適用したら true。
        /// 物理に依存せずテストから直接呼べる（<paramref name="targetActor"/> は Faction 判定用。null 可）。
        /// </summary>
        public bool TryApplyHit(IDamageable target, ICombatActor targetActor, Vector3 hitPoint)
        {
            if (target == null || !_machine.IsHitboxActive)
            {
                return false;
            }

            // 自身除外。
            if (target is Component tc && tc.transform.root == transform.root)
            {
                return false;
            }

            // Faction フィルタ：敵は敵に当てない（対象が敵陣営なら除外）。
            if (targetActor != null && targetActor.Faction == CombatFaction.Enemy)
            {
                return false;
            }

            // 同一 Swing で 1 対象 1Hit。
            if (!_hitTracker.TryRegisterHit(_currentSwing, target))
            {
                return false;
            }

            float attackPower = _actor.Archetype != null ? _actor.Archetype.AttackPower : 0f;
            HitInfo hit = EnemyHitFactory.Build(_machine.Snapshot, attackPower, _actor, target, _aimDir, hitPoint, _currentSwing);
            target.ReceiveHit(hit);
            return true;
        }

        /// <summary>攻撃を中断し、判定・予兆を解除する（Cleanup。Stagger/Stunned/Down/Disable/Scene 離脱共通）。</summary>
        public void CancelAttack()
        {
            if (!_machine.IsAttacking)
            {
                return;
            }

            EnemyAttackSnapshot snap = _machine.Snapshot;
            _machine.Cancel();
            _hitTracker.Clear();
            PublishTelegraph(EnemyTelegraphPhase.Cancel, snap);
        }

        private void TickCooldowns(float dt)
        {
            if (_cooldown == null)
            {
                return;
            }

            for (int i = 0; i < _cooldown.Length; i++)
            {
                if (_cooldown[i] > 0f)
                {
                    _cooldown[i] = Mathf.Max(0f, _cooldown[i] - dt);
                }
            }
        }

        private void PublishTelegraph(EnemyTelegraphPhase phase, in EnemyAttackSnapshot snap)
        {
            Telegraph.Publish(new EnemyTelegraphEvent(
                _actor.DamageableId, phase, snap.Telegraph, _actor.WorldPosition, _aimDir, snap.PrepareSeconds));
        }

        private static float AngleToTarget(Vector3 selfPos, Vector3 forward, Vector3 targetPos)
        {
            Vector3 to = targetPos - selfPos;
            to.y = 0f;
            Vector3 fwd = forward;
            fwd.y = 0f;
            if (to.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-6f)
            {
                return 0f;
            }

            return Vector3.Angle(fwd, to);
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
