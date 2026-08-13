using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 敵の防御・回避の駆動（Phase3 P3-10。§9）。能力 Data（<see cref="Data.Characters.EnemyArchetypeData.CanGuard"/>／
    /// <see cref="Data.Characters.EnemyArchetypeData.CanEvade"/>）が有効な敵だけが Guard／Evade を使う。観測可能な危険
    /// （<see cref="IEnemyDangerSense"/>。既定は物理観測で入力を直接読まない）に反応してガードを構え、または短い無敵で退避する。
    /// 被弾軽減・無敵の反映は <see cref="EnemyActor"/> が <see cref="IEnemyDefenseState"/> 経由で読む。撃破時は能力をリセットする。
    /// 純粋ロジック（<see cref="EnemyGuardAbility"/>／<see cref="EnemyEvadeAbility"/>）を薄く駆動し、deltaTime 注入で決定的に検証できる。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyActor))]
    public sealed class EnemyDefenseController : MonoBehaviour, IEnemyDefenseState, IEnemyDefeatCleanup
    {
        [Tooltip("危険を観測する半径（m。既定の物理観測）。")]
        [SerializeField] private float _dangerRadius = 2.5f;

        [Tooltip("危険観測の対象レイヤー（既定は全レイヤー。Faction で Player に絞る）。")]
        [SerializeField] private LayerMask _dangerMask = ~0;

        [Tooltip("回避の退避距離（m）。危険源から離れる向きへ移動目標を置く。")]
        [SerializeField] private float _evadeDistance = 2.0f;

        private EnemyActor _actor;
        private EnemyMotor _motor;
        private EnemyAttackController _combat;
        private EnemyGuardAbility _guard;
        private EnemyEvadeAbility _evade;
        private IEnemyDangerSense _danger;
        private bool _canGuard;
        private bool _canEvade;
        private bool _built;

        /// <inheritdoc />
        public bool IsGuarding => _canGuard && _guard != null && _guard.IsGuarding;

        /// <inheritdoc />
        public bool IsEvadeInvulnerable => _canEvade && _evade != null && _evade.IsInvulnerable;

        /// <inheritdoc />
        public bool IsDefending => IsGuarding || (_canEvade && _evade != null && _evade.IsEvading);

        /// <summary>ガード能力（Debug/テスト用）。</summary>
        public EnemyGuardAbility Guard { get { Build(); return _guard; } }

        /// <summary>回避能力（Debug/テスト用）。</summary>
        public EnemyEvadeAbility Evade { get { Build(); return _evade; } }

        /// <summary>危険観測を差し替える（テストで Fake を注入する。§11）。</summary>
        public void SetDangerSense(IEnemyDangerSense sense)
        {
            _danger = sense;
        }

        private void Awake()
        {
            Build();
        }

        private void Build()
        {
            if (_built)
            {
                return;
            }

            if (_actor == null) _actor = GetComponent<EnemyActor>();
            if (_motor == null) _motor = GetComponent<EnemyMotor>();
            if (_combat == null) _combat = GetComponent<EnemyAttackController>();

            var a = _actor != null ? _actor.Archetype : null;
            _canGuard = a != null && a.CanGuard;
            _canEvade = a != null && a.CanEvade;
            float guardCd = a != null ? a.GuardCooldownSeconds : 3f;
            float evadeCd = a != null ? a.EvadeCooldownSeconds : 4f;
            _guard = new EnemyGuardAbility(guardCd);
            _evade = new EnemyEvadeAbility(evadeCd);
            if (_danger == null)
            {
                _danger = new PhysicsEnemyDangerSense(_dangerRadius, _dangerMask);
            }

            _built = true;
        }

        private void Update()
        {
            if (!IsGameplayActive())
            {
                return; // Pause／会話中は防御判断・時間経過を止める。
            }

            TickDefense(Time.deltaTime);
        }

        /// <summary>防御を 1 Tick 進める（Update から、またはテストが決定的に呼ぶ）。能力の時間経過と危険反応を行う。</summary>
        public void TickDefense(float deltaTime)
        {
            Build();
            if (_actor == null || (!_canGuard && !_canEvade))
            {
                return; // 能力 Data 無効な敵は防御しない（§9）。
            }

            _guard.Tick(deltaTime);
            _evade.Tick(deltaTime);

            // 被弾由来（Down/Stunned/Stagger）中は防御を解除して委ねる（優先度）。
            if (EnemyStatePriority.IsForcedByHit(_actor.State))
            {
                _guard.Release();
                return;
            }

            // 攻撃中は攻撃を優先（ガードは解除）。
            if (_combat != null && _combat.IsAttacking)
            {
                _guard.Release();
                return;
            }

            // 回避モーション継続：無敵の間は退避を続け状態を保持する。
            if (_evade.IsEvading)
            {
                _actor.RequestState(EnemyState.Evade, EnemyStateChangeReason.DefensiveAction);
                return;
            }

            Vector3 selfPos = _actor.WorldPosition;

            // ガード構え継続：危険が続く限り構え、消えたら解除する。
            if (_guard.IsGuarding)
            {
                EnemyDangerStimulus d = Sense(selfPos);
                if (!d.HasDanger)
                {
                    _guard.Release();
                }
                else
                {
                    _actor.RequestState(EnemyState.Guard, EnemyStateChangeReason.DefensiveAction);
                    _motor?.Stop();
                    _actor.SetFacing(-d.IncomingDirection); // 危険源へ正対して前方 180°で受ける。
                    _motor?.SetFacing(-d.IncomingDirection);
                }

                return;
            }

            // 新規：危険を観測したら能力に応じて回避またはガードを開始する。
            EnemyDangerStimulus danger = Sense(selfPos);
            if (!danger.HasDanger)
            {
                return;
            }

            // 回避優先条件：ガード不能な危険、またはガードを持たない／今構えられない場合。
            bool preferEvade = _canEvade && _evade.IsReady && (danger.Unblockable || !_canGuard || !_guard.IsReady);
            if (preferEvade && _evade.TryStart())
            {
                StartRetreat(selfPos, danger);
                _actor.RequestState(EnemyState.Evade, EnemyStateChangeReason.DefensiveAction);
                return;
            }

            if (_canGuard && _guard.IsReady && _guard.TryStart())
            {
                _actor.RequestState(EnemyState.Guard, EnemyStateChangeReason.DefensiveAction);
                _motor?.Stop();
                _actor.SetFacing(-danger.IncomingDirection);
                _motor?.SetFacing(-danger.IncomingDirection);
            }
        }

        private EnemyDangerStimulus Sense(Vector3 selfPos)
        {
            if (_danger == null)
            {
                return EnemyDangerStimulus.None;
            }

            return _danger.Sense(selfPos, _actor.Forward, _actor.DamageableId);
        }

        private void StartRetreat(Vector3 selfPos, in EnemyDangerStimulus danger)
        {
            // IncomingDirection＝危険源→自分。その向きへ進むと危険源から離れる。危険源へ正対しつつ後退する。
            Vector3 retreat = selfPos + danger.IncomingDirection * _evadeDistance;
            _motor?.SetMoveTarget(retreat);
            _actor.SetFacing(-danger.IncomingDirection);
            _motor?.SetFacing(-danger.IncomingDirection);
        }

        /// <inheritdoc />
        public void OnOwnerDefeated()
        {
            Build();
            _guard?.Reset();
            _evade?.Reset();
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
