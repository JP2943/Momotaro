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
    /// <b>始めてよいかは自分で決めない</b>（P4-FIX F02c）。可否は許可表（<see cref="CompanionActionRules"/>）へ
    /// 尋ね、能力（クールダウン）を消費する前に判定する。以前は <see cref="CanDefend"/>（倒れている・ひるみ・退場だけ）
    /// しか見ておらず、<b>攻撃判定中でも構えを始められた</b>。条件式を駆動ごとに持つと必ずこうなる。
    ///
    /// 構え・回避のあいだは<b>移動と向きも握る</b>。ガードの成否は <c>Forward</c> と命中方向の角度で決まるので、
    /// 追従が主人公の向きへ回してしまうと、構えているのに素通りする。動作が終わったら追従へ戻す
    /// （戻さないと、許可表が次の行動を禁じたまま固まる）。
    ///
    /// 被弾側（<see cref="CompanionHitReceiver"/>）は本コンポーネントを <see cref="ICompanionDefenseState"/> として読み、
    /// 無敵とガードを解決順に反映する。判断（ここ）と解決（受け口）を分けているのは主人公・敵と同じ形。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionDefenseController : MonoBehaviour, ICompanionDefenseState
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("状態要求の唯一の窓口（未設定なら自動取得）。Actor へは直接書かない。")]
        [SerializeField] private CompanionStateArbiter _states;

        [Tooltip("移動と向きの書き手（未設定なら自動取得）。防御は意図を出すだけで、Motor へは直接書かない。")]
        [SerializeField] private CompanionMovementArbiter _arbiter;

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
        private CompanionActionHandle _action; // 構え・回避の引換券（F02b）。
        private Vector3 _holdFacing;           // 防御中に固定する向き（F02c）。
        private bool _hasHoldFacing;

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

            // 停止中は構えも回避も進めない（P4-FIX F05）。判断を Update 側だけに置くと、
            // 外から直接 Tick された瞬間に素通りする。会話・イベントでは構えも解く。
            CompanionActivity activity = CompanionActivityProvider.Activity;
            if (!activity.ClocksRun)
            {
                if (activity.DiscardOngoing)
                {
                    ReleaseGuard();
                    SawDanger = false;
                }

                return;
            }

            float dt = deltaTime < 0f ? 0f : deltaTime;
            _guard.Tick(dt);
            _evade.Tick(dt);

            if (!CanDefend(_actor.State))
            {
                // 倒れた・ひるんだ・退場した瞬間に構えを解く（構えたまま倒れない）。
                // 行動を先に手放すのは、ここから Follow へ戻さないため。倒れている・退場しているときの
                // 状態は被弾側・退場側が握っており、防御が勝手に復帰させてよい場面ではない。
                AbandonDefenseAction();
                ReleaseGuard();
                SawDanger = false;
                return;
            }

            // 動作が終わっていれば、次の判断より<b>先に</b>行動を返す（F02c）。
            // 後回しにすると、構えも回避もしていないのに状態だけ Guard／Evade のまま残る。
            // その状態では許可表が攻撃も次の構えも禁じるため、仲間が永久に固まる。
            SettleDefenseAction();

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
                if (!BeginDefenseAction(CompanionActionKind.AutoEvade, CompanionState.Evade, _actor.Forward))
                {
                    return; // 許可表が禁じた（判定中・守護中など）。能力の時計も進めない＝回避は起きなかった。
                }

                if (!_evade.TryStart())
                {
                    // 能力側が拒否した（クールダウン）。状態だけ先に変えてしまわないよう行動を返す。
                    AbandonDefenseAction();
                }

                return;
            }

            if (_canGuard && _guard.IsReady && !_evade.IsEvading)
            {
                // ガードは受ける向きが本体（判定は Forward と命中方向の角度で決まる）。危険源の方を向く。
                if (!BeginDefenseAction(CompanionActionKind.AutoGuard, CompanionState.Guard, -stimulus.IncomingDirection))
                {
                    return;
                }

                if (!_guard.TryStart())
                {
                    AbandonDefenseAction();
                }
            }

            if (_action.IsValid)
            {
                HoldDefensePose(); // 構え・回避のあいだは位置と向きを他へ渡さない。
            }
        }

        /// <summary>構え・回避を初期化する（加入・Retry・無効化）。</summary>
        public void ResetDefense()
        {
            Build();
            _guard.Reset();
            _evade.Reset();
            AbandonDefenseAction();
            SawDanger = false;
        }

        private void ReleaseGuard()
        {
            if (_guard != null && _guard.IsGuarding)
            {
                _guard.Release();
            }

            // 構えを解いても、回避モーションが残っていれば行動はまだ終わっていない。
            // 終わっているかどうかの判断は 1 か所（SettleDefenseAction）に置く。
            SettleDefenseAction();
        }

        /// <summary>
        /// 防御としての行動を始める（P4-FIX F02b／F02c）。始められたら true。
        ///
        /// 可否は許可表（<see cref="CompanionActionRules"/>）が決める。<b>能力より先に</b>ここを通すのは、
        /// 表に禁じられた場面でガード・回避のクールダウンだけ消費してしまわないため。
        /// </summary>
        private bool BeginDefenseAction(CompanionActionKind kind, CompanionState state, Vector3 facing)
        {
            if (_states == null || !_states.TryStartAction(
                    CompanionActionOwner.Defense, kind, state,
                    CompanionStateChangeReason.DefensiveAction, out _action))
            {
                return false;
            }

            _holdFacing = facing;
            _hasHoldFacing = facing.sqrMagnitude > 1e-6f;
            HoldDefensePose();
            return true;
        }

        /// <summary>
        /// 構えも回避も終わっていれば行動を返し、追従へ戻す（F02c）。
        ///
        /// <b>この復帰は省略できない。</b>許可表は Guard／Evade からの自動攻撃も次の構えも禁じるので、
        /// ここで戻さないと仲間はその状態のまま何もしなくなる。
        /// </summary>
        private void SettleDefenseAction()
        {
            if (!_action.IsValid)
            {
                return;
            }

            if ((_guard != null && _guard.IsGuarding) || (_evade != null && _evade.IsEvading))
            {
                return; // まだ動作中。
            }

            if (_states != null && _states.IsCurrent(_action))
            {
                _states.TryComplete(_action, CompanionState.Follow, CompanionStateChangeReason.FollowResumed);
            }

            ClearDefenseAction();
        }

        /// <summary>
        /// 行動を打ち切る（状態は変えない）。倒れた・ひるんだ場合は被弾側が状態を握っているので、
        /// ここから Follow へ戻そうとしてはいけない。
        /// </summary>
        private void AbandonDefenseAction()
        {
            if (_action.IsValid)
            {
                _states?.Release(_action);
            }

            ClearDefenseAction();
        }

        private void ClearDefenseAction()
        {
            _action = default;
            _hasHoldFacing = false;
            _arbiter?.Release(CompanionMovementOwner.Defense);
        }

        /// <summary>
        /// 防御の姿勢（その場・向き固定）を移動の調停役へ出す（F02c）。
        /// 追従が主人公の向きを、戦闘が対象の向きを書くと、受けているはずの方向がずれてガードが素通りする。
        /// </summary>
        private void HoldDefensePose()
        {
            _arbiter?.Submit(
                CompanionMovementOwner.Defense,
                _hasHoldFacing ? CompanionMoveRequest.StopFacing(_holdFacing) : CompanionMoveRequest.Stop());
        }

        private void Build()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }

            if (_states == null)
            {
                _states = GetComponent<CompanionStateArbiter>();
            }

            if (_arbiter == null)
            {
                _arbiter = GetComponent<CompanionMovementArbiter>();
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
            // 停止の判断は公開 Tick の入口へ移した（P4-FIX F05）。外から直接呼ばれる経路も塞ぐため。
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

            AbandonDefenseAction(); // 行動と移動の所有権も残さない。
            SawDanger = false;
        }

    }
}
