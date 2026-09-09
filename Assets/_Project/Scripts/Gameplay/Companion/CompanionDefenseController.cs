using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の防御・回避の駆動（P4-04b）。能力 Data（<see cref="Data.Characters.CompanionData.CanGuard"/>／
    /// <see cref="Data.Characters.CompanionData.CanEvade"/>）が有効な仲間だけが構え・退避を使う。
    ///
    /// <b>入力ではなく観測可能な危険に反応する</b>（敵の防御 AI と同じ方針。§9）。半径内の敵で攻撃の予備動作／判定中の
    /// ものを危険とみなし、ガード不能な危険なら回避を、通常の危険ならガードを選ぶ。危険が消えたら構えを解く。
    ///
    /// 純粋ロジックは敵と共通のものを使う（<see cref="EnemyGuardAbility"/>／<see cref="EnemyEvadeAbility"/>／
    /// <see cref="IEnemyDangerSense"/>）。これらは名前こそ Enemy だが中身に敵固有の要素は無く、保持時間・クールダウン・
    /// 無敵時間を秒で受け取るだけの純粋クラスなので、仲間用に写経せず<b>そのまま再利用する</b>。
    ///
    /// 被弾側（<see cref="CompanionHitReceiver"/>）は本コンポーネントを <see cref="ICompanionDefenseState"/> として読み、
    /// 無敵とガードを解決順に反映する。判断（ここ）と解決（受け口）を分けているのは主人公・敵と同じ形。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionDefenseController : MonoBehaviour, ICompanionDefenseState
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("危険を観測する半径（m）。")]
        [SerializeField] private float _dangerRadius = 2.5f;

        [Tooltip("危険観測の対象レイヤー（既定は全レイヤー。陣営で敵に絞る）。")]
        [SerializeField] private LayerMask _dangerMask = ~0;

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

        /// <summary>回避モーション中か（無敵が切れた後の余韻を含む。診断用）。</summary>
        public bool IsEvading => _canEvade && _evade != null && _evade.IsEvading;

        /// <summary>ガード能力（テスト・Debug 用）。</summary>
        public EnemyGuardAbility Guard
        {
            get { Build(); return _guard; }
        }

        /// <summary>回避能力（テスト・Debug 用）。</summary>
        public EnemyEvadeAbility Evade
        {
            get { Build(); return _evade; }
        }

        /// <summary>直近に危険を観測したか（診断用）。</summary>
        public bool SawDanger { get; private set; }

        /// <summary>状態・Data の供給元を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionActor actor)
        {
            if (actor != null && !ReferenceEquals(_actor, actor))
            {
                _actor = actor;
                _built = false;
            }
        }

        /// <summary>危険観測を差し替える（テストで Fake を注入する）。</summary>
        public void SetDangerSense(IEnemyDangerSense sense)
        {
            _danger = sense;
        }

        /// <summary>この状態のとき防御行動を取れるか（倒れている・退場・ひるみ中は取れない）。</summary>
        public static bool CanDefend(CompanionState state)
        {
            return state != CompanionState.Away
                && state != CompanionState.Down
                && state != CompanionState.Recovering
                && state != CompanionState.Stagger;
        }

        /// <summary>
        /// 防御判断を 1 Tick 進める（Update から呼ばれるが、テストは決定的に直接呼べる）。
        /// </summary>
        public void TickDefense(float deltaTime)
        {
            Build();
            if (_actor == null)
            {
                return;
            }

            float dt = deltaTime < 0f ? 0f : deltaTime;
            _guard.Tick(dt);
            _evade.Tick(dt);

            if (!CanDefend(_actor.State))
            {
                // 倒れた・ひるんだ・退場した瞬間に構えを解く（構えたまま倒れない）。
                ReleaseGuard();
                SawDanger = false;
                return;
            }

            EnemyDangerStimulus stimulus = _danger != null
                ? _danger.Sense(_actor.WorldPosition, _actor.Forward, _actor.ActorId)
                : EnemyDangerStimulus.None;

            SawDanger = stimulus.HasDanger;

            if (!stimulus.HasDanger)
            {
                ReleaseGuard();
                return;
            }

            // ガード不能な危険は構えても意味が無いので回避を優先する（敵の防御 AI と同じ判断）。
            if (stimulus.Unblockable && _canEvade && _evade.IsReady)
            {
                if (_evade.TryStart())
                {
                    _actor.RequestState(CompanionState.Evade, CompanionStateChangeReason.DefensiveAction);
                }

                return;
            }

            if (_canGuard && _guard.IsReady && !_evade.IsEvading)
            {
                if (_guard.TryStart())
                {
                    _actor.RequestState(CompanionState.Guard, CompanionStateChangeReason.DefensiveAction);
                }
            }
        }

        /// <summary>構え・回避を初期化する（加入・Retry・無効化）。</summary>
        public void ResetDefense()
        {
            Build();
            _guard.Reset();
            _evade.Reset();
            SawDanger = false;
        }

        private void ReleaseGuard()
        {
            if (_guard.IsGuarding)
            {
                _guard.Release();
            }
        }

        private void Build()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }

            if (_built && _guard != null && _evade != null)
            {
                return;
            }

            Data.Characters.CompanionData data = _actor != null ? _actor.Data : null;
            _canGuard = data == null || data.CanGuard;
            _canEvade = data == null || data.CanEvade;

            float guardCooldown = data != null ? data.GuardCooldownSeconds : 3f;
            float evadeCooldown = data != null ? data.EvadeCooldownSeconds : 4f;

            _guard = new EnemyGuardAbility(guardCooldown);
            _evade = new EnemyEvadeAbility(evadeCooldown);

            if (_danger == null)
            {
                // 敵（CombatFaction.Enemy）の攻撃だけを危険源とみなす。
                _danger = new PhysicsEnemyDangerSense(_dangerRadius, _dangerMask, dangerFaction: CombatFaction.Enemy);
            }

            _built = true;
        }

        private void Update()
        {
            if (!IsGameplayActive())
            {
                return; // Pause／会話中は構え・クールダウンを進めない。
            }

            TickDefense(Time.deltaTime);
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱で構えを残さない（§2.3 後始末）。
            if (_guard != null)
            {
                _guard.Reset();
            }

            if (_evade != null)
            {
                _evade.Reset();
            }

            SawDanger = false;
        }

        private static bool IsGameplayActive()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return true; // 未初期化（単体テスト等）は許可。
            }

            GameMode mode = modes.Current;
            return mode == GameMode.Exploration || mode == GameMode.Combat;
        }
    }
}
