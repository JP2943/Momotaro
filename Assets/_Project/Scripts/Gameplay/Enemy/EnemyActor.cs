using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵の Identity・Faction・Vitals・姿勢・戦闘参加状態の窓口（Phase3 P3-01。§2.2）。Phase 2 と同じ命中経路
    /// （<see cref="IDamageable"/> / <see cref="ICombatActor"/> / <see cref="HitResultChannel"/>）で被弾し、共通 Runtime
    /// <see cref="EnemyVitals"/> で HP・体幹・ひるみ・スタン・Down を処理する。被弾由来の Stagger／Stunned／Down は
    /// <see cref="EnemyStateMachine"/> の優先度で適用し、型付き <see cref="EnemyStateChanged"/> を発行する。
    /// 認識・移動・敵攻撃は本 Task 対象外（EnemyBrain／Motor／CombatController が後続 Task で接続する）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyActor : MonoBehaviour, ICombatActor, IDamageable, ICombatActivityState, IKnockbackReceiver
    {
        [Tooltip("敵アーキタイプ Data（HP／防御／体幹／ひるみ／スタン等の数値と役割）。")]
        [SerializeField] private EnemyArchetypeData _archetype;

        private EnemyVitals _vitals;
        private EnemyStateMachine _machine;

        /// <summary>被弾結果の通知チャネル（HUD・Feedback が購読。Phase 2 と同系統）。</summary>
        public HitResultChannel Results { get; } = new HitResultChannel();

        /// <summary>状態遷移の通知チャネル（型付き。Presentation／Debug／将来の仲間 AI が購読）。</summary>
        public EnemyStateChannel States { get; } = new EnemyStateChannel();

        // ---- ICombatActor ----
        /// <inheritdoc />
        public CombatFaction Faction => CombatFaction.Enemy;
        /// <inheritdoc />
        public int FloorId => 0;
        /// <inheritdoc />
        public Vector3 WorldPosition => transform.position;
        /// <inheritdoc />
        public Vector3 Forward => transform.forward;

        // ---- IDamageable ----
        /// <inheritdoc />
        public int DamageableId => GetInstanceID();

        /// <summary>現在状態。</summary>
        public EnemyState State
        {
            get { EnsureRuntime(); return _machine.Current; }
        }

        /// <summary>現在 HP。</summary>
        public int CurrentHp { get { EnsureRuntime(); return _vitals.CurrentHp; } }
        /// <summary>最大 HP。</summary>
        public int MaxHp { get { EnsureRuntime(); return _vitals.MaxHp; } }
        /// <summary>撃破済み（HP0）か。</summary>
        public bool IsDefeated { get { EnsureRuntime(); return _vitals.IsDefeated; } }
        /// <summary>ダウン状態か（撃破後の状態）。</summary>
        public bool IsDown => State == EnemyState.Down;
        /// <summary>現在体幹。</summary>
        public float CurrentPoise { get { EnsureRuntime(); return _vitals.CurrentPoise; } }
        /// <summary>最大体幹。</summary>
        public float MaxPoise { get { EnsureRuntime(); return _vitals.MaxPoise; } }
        /// <summary>スタン中か。</summary>
        public bool IsStunned { get { EnsureRuntime(); return _vitals.IsStunned; } }
        /// <summary>ひるみ中か。</summary>
        public bool IsFlinching { get { EnsureRuntime(); return _vitals.IsFlinching; } }
        /// <summary>ひるみ蓄積量。</summary>
        public float FlinchAccumulation { get { EnsureRuntime(); return _vitals.FlinchAccumulation; } }

        /// <summary>ボス（大型敵）か（役割由来）。ボスはノックバック無効。</summary>
        public bool IsBoss => _archetype != null && _archetype.Role == EnemyRole.Boss;

        /// <summary>直近に受けたノックバック力（検証用。ボスは常に 0）。</summary>
        public float LastKnockback { get; private set; }

        /// <inheritdoc />
        /// <remarks>
        /// 体幹の攻撃中補正（×1.5）の対象。攻撃予兆／判定中（AttackPrepare／AttackActive）かつ、スタン・ひるみ・
        /// ダウン中でないときのみ true。攻撃の実駆動は P3-04 だが、状態を語彙として先に公開しておく。
        /// </remarks>
        public bool IsPoiseVulnerableAction
        {
            get
            {
                EnsureRuntime();
                if (_vitals.IsStunned || _vitals.IsFlinching || _vitals.IsDefeated)
                {
                    return false;
                }

                return _machine.Current == EnemyState.AttackPrepare || _machine.Current == EnemyState.AttackActive;
            }
        }

        private void Awake()
        {
            EnsureRuntime();
            // 敵は Enemy レイヤーへ（Player はすり抜け、壁で停止。§3.4 / P2-09）。
            CombatLayers.ConfigureEnemy(gameObject);
        }

        private void EnsureRuntime()
        {
            if (_vitals == null)
            {
                _vitals = new EnemyVitals(_archetype);
            }

            if (_machine == null)
            {
                _machine = new EnemyStateMachine(
                    GetInstanceID(),
                    EnemyState.Idle,
                    OnStateChanged,
                    IllegalTransitionLogger());
            }
        }

        private void OnStateChanged(EnemyStateChanged change)
        {
            States.Publish(change);
        }

        private static System.Action<string> IllegalTransitionLogger()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return message => Debug.LogWarning(message);
#else
            return null;
#endif
        }

        private void Update()
        {
            EnsureRuntime();
            // 体幹回復・ひるみの時間経過（AI・攻撃は後続 Task。Pause 連動も Brain 追加時に接続する）。
            _vitals.Tick(Time.deltaTime);

            // スタン・ひるみが自然回復したら通常状態へ戻す（観測可能な復帰）。Down は復活処理まで維持。
            if (_machine.Current == EnemyState.Stunned && !_vitals.IsStunned)
            {
                _machine.TryTransition(EnemyState.Idle, EnemyStateChangeReason.Recovered);
            }
            else if (_machine.Current == EnemyState.Stagger && !_vitals.IsFlinching)
            {
                _machine.TryTransition(EnemyState.Idle, EnemyStateChangeReason.Recovered);
            }
        }

        /// <inheritdoc />
        /// <remarks>Phase 2 の仮反応：物理は動かさず受けた力を記録するだけ。ボスは無効（0）。実反応は後続 Task。</remarks>
        public void ReceiveKnockback(Vector3 direction, float force)
        {
            LastKnockback = IsBoss ? 0f : force;
        }

        /// <inheritdoc />
        public void ReceiveHit(in HitInfo hit)
        {
            EnsureRuntime();

            EnemyVitals.HitApplication app = _vitals.Apply(hit);

            // 被弾由来の状態遷移（優先度：Down > Stunned > Stagger）。攻撃中なら中断され、後続 Task の Cleanup 対象になる。
            if (app.NewlyDefeated)
            {
                _machine.ForceHitState(EnemyState.Down, EnemyStateChangeReason.Defeated);
            }
            else if (app.NewlyStunned)
            {
                _machine.ForceHitState(EnemyState.Stunned, EnemyStateChangeReason.Stunned);
            }
            else if (app.NewlyFlinching)
            {
                _machine.ForceHitState(EnemyState.Stagger, EnemyStateChangeReason.Staggered);
            }

            Results.Publish(HitResult.Damage(hit.HitId, hit.Attacker, this, app.Applied));
        }

        /// <summary>HP・体幹・ひるみと状態を初期へ戻す（検証の再試行用）。</summary>
        public void ResetState()
        {
            EnsureRuntime();
            _vitals.ResetState();
            _machine.Reset(EnemyState.Idle);
            LastKnockback = 0f;
        }
    }
}
