using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Defense;
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
    public sealed class EnemyActor : MonoBehaviour, ICombatActor, IDamageable, ICombatActivityState, IKnockbackReceiver, IEnemyDefeatSource
    {
        [Tooltip("敵アーキタイプ Data（HP／防御／体幹／ひるみ／スタン等の数値と役割）。")]
        [SerializeField] private EnemyArchetypeData _archetype;

        private EnemyVitals _vitals;
        private EnemyStateMachine _machine;

        private IEnemyDefenseState _defense;          // ガード／回避の状態読み取り（P3-10。相互直接依存を避ける契約）。
        private IEnemyDefeatCleanup[] _defeatCleanups; // 撃破時の後始末（攻撃・Slot 解除等）を委ねる先。
        private bool _defenseResolved;
        private bool _defeatHandled;                   // 型付き撃破・報酬要求を 1 回だけ発行する。

        /// <summary>被弾結果の通知チャネル（HUD・Feedback が購読。Phase 2 と同系統）。</summary>
        public HitResultChannel Results { get; } = new HitResultChannel();

        /// <summary>状態遷移の通知チャネル（型付き。Presentation／Debug／将来の仲間 AI が購読）。</summary>
        public EnemyStateChannel States { get; } = new EnemyStateChannel();

        /// <summary>撃破（Down 確定）の通知チャネル（型付き。報酬要求を 1 回発行。P3-10）。</summary>
        public EnemyDefeatChannel Defeats { get; } = new EnemyDefeatChannel();

        // ---- ICombatActor ----
        /// <inheritdoc />
        public CombatFaction Faction => CombatFaction.Enemy;
        /// <inheritdoc />
        public int FloorId => 0;
        /// <inheritdoc />
        public Vector3 WorldPosition => transform.position;
        /// <inheritdoc />
        /// <remarks>
        /// 論理的な前方（XZ 平面）。物理ルートは接地・Collider の安定のため回転させない（Rigidbody で全回転を固定）ため、
        /// 向きは transform.forward ではなくこの論理値で保持する。移動・追跡・照準に応じて <see cref="SetFacing"/> で更新し、
        /// 認識コーン・攻撃照準・表示（4 方向スプライト）が参照する。既定は +Z。
        /// </remarks>
        public Vector3 Forward => _facing.sqrMagnitude > 1e-6f ? _facing : Vector3.forward;

        private Vector3 _facing = Vector3.forward;

        /// <summary>論理的な前方（XZ）を設定する。ルート Transform は回さず、向きだけを更新する。</summary>
        public void SetFacing(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-6f)
            {
                _facing = direction.normalized;
            }
        }

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

        /// <summary>アーキタイプ Data（読み取り専用。認識・移動・攻撃の各サービスが設定値を参照する）。</summary>
        public EnemyArchetypeData Archetype => _archetype;

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
            ResolveDefense();

            // 撃破後は追加被弾を受け付けない（型付き撃破は 1 回・Down 後の余計な命中を無効化。§9）。
            if (_vitals.IsDefeated)
            {
                return;
            }

            // 回避無敵：命中を無効化（ダメージ・状態遷移なし。§9「短い無敵」）。
            if (_defense != null && _defense.IsEvadeInvulnerable)
            {
                return;
            }

            // ガード軽減（§9）：構え中かつ前方 180°かつ Special 貫通でないとき HP×0.1／被体幹×1.5。背後・Special は貫通（等倍）。
            float hpScale = 1f;
            float poiseScale = 1f;
            if (_defense != null && _defense.IsGuarding)
            {
                bool front = EnemyGuardMath.IsWithinFrontArc(Forward, hit);
                EnemyGuardMath.Result g = EnemyGuardMath.Resolve(true, front, EnemyGuardMath.IsSpecialPierce(hit));
                hpScale = g.HpScale;
                poiseScale = g.PoiseScale;
            }

            EnemyVitals.HitApplication app = _vitals.Apply(hit, hpScale, poiseScale);

            // 被弾由来の状態遷移（優先度：Down > Stunned > Stagger）。攻撃中なら中断され、Cleanup 対象になる。
            if (app.NewlyDefeated)
            {
                _machine.ForceHitState(EnemyState.Down, EnemyStateChangeReason.Defeated);
                HandleDefeated(); // 攻撃・衝突・Slot 解除＋型付き撃破／報酬要求を 1 回発行（§9）。
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

        /// <summary>防御状態・撃破後始末先を 1 回解決する（動的生成でも遅延解決）。</summary>
        private void ResolveDefense()
        {
            if (_defenseResolved)
            {
                return;
            }

            _defense = GetComponent<IEnemyDefenseState>();
            _defeatCleanups = GetComponents<IEnemyDefeatCleanup>();
            _defenseResolved = true;
        }

        /// <summary>撃破確定時の後始末（1 回性）：攻撃・Slot 解除、衝突（Collider）解除、型付き撃破と報酬要求の発行。</summary>
        private void HandleDefeated()
        {
            if (_defeatHandled)
            {
                return;
            }

            _defeatHandled = true;

            // 攻撃中断・Slot 解放・能力リセット等は各コンポーネントへ委譲（1 回）。
            if (_defeatCleanups != null)
            {
                for (int i = 0; i < _defeatCleanups.Length; i++)
                {
                    _defeatCleanups[i]?.OnOwnerDefeated();
                }
            }

            // 衝突解除：自身の Collider を無効化（押し合い・被弾面を消す）。
            SetCollidersEnabled(false);

            EnemyRole role = _archetype != null ? _archetype.Role : EnemyRole.Melee;
            var request = new EnemyRewardRequest(DamageableId, role,
                _archetype != null ? _archetype.Reward : null, WorldPosition);
            Defeats.Publish(new EnemyDefeatedEvent(DamageableId, request));
        }

        private void SetCollidersEnabled(bool enabled)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = enabled;
                }
            }
        }

        /// <summary>
        /// AI サービス（認識・移動・攻撃）からの状態遷移要求（Phase3）。優先度・不正判定は状態機に従う（被弾由来の
        /// Down/Stunned/Stagger 中の呼び出しは不正として記録され適用されない）。適用できたら true。
        /// </summary>
        public bool RequestState(EnemyState state, EnemyStateChangeReason reason)
        {
            EnsureRuntime();
            return _machine.TryTransition(state, reason);
        }

        /// <summary>HP・体幹・ひるみと状態を初期へ戻す（検証の再試行用）。</summary>
        public void ResetState()
        {
            EnsureRuntime();
            _vitals.ResetState();
            _machine.Reset(EnemyState.Idle);
            LastKnockback = 0f;
            _defeatHandled = false;
            SetCollidersEnabled(true); // 検証の再試行で被弾面・押し合いを戻す。
        }
    }
}
