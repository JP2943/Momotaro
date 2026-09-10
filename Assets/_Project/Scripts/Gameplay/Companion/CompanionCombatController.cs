using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の戦闘駆動（P4-03 後半）。索敵（<see cref="CompanionTargetTracker"/>）が決めた対象へ近づき、間合いで通常攻撃を出す。
    /// 判断は <see cref="CompanionEngagement"/>、進行は <see cref="CompanionAttackState"/>、命中生成は
    /// <see cref="CompanionHitFactory"/> に委ね、本コンポーネントは結線と物理問い合わせだけを担う。
    ///
    /// 命中経路は主人公・敵と同一：Active 中に OverlapBox で対象を集め、同一 Swing（<see cref="HitId"/>）で 1 対象 1Hit、
    /// <see cref="IDamageable.ReceiveHit"/> へ渡す（無敵＞JG＞Guard＞Damage の解決順は被弾側が担保する）。仲間専用の
    /// ダメージ経路は作らない。これにより敵側の <c>EnemyThreatTracker</c> が仲間の与ダメージを既存のまま獲得ヘイトに変換し、
    /// 犬丸が敵に狙われるようになる（敵 AI は一行も書き換えない）。
    ///
    /// 移動は戦闘中だけ本コンポーネントが握り、追従（<see cref="CompanionFollowController"/>）は
    /// <see cref="ICompanionEngagementSource.IsEngaged"/> を見て譲る。攻撃中は移動しない（振り向きだけ行う）。
    ///
    /// 中断は 2 経路で行う。状態遷移の通知（ひるみ・ダウン・退場）を購読して<b>その場で</b>判定を消し、
    /// 継続中の保険として毎 Tick も確認する。判定が 1 フレーム残ると、倒れたはずの犬丸が敵を殴ってしまう。
    /// Pause／会話中（<see cref="GameMode"/>）は時間を進めない。
    ///
    /// 被弾側（<see cref="IDamageable"/>）・ガード／回避の判断は本 Task の対象外（P4-04 以降）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionCombatController : MonoBehaviour, ICompanionEngagementSource, ICompanionStateListener
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("移動実行（未設定なら自動取得）。接近・停止に用いる。")]
        [SerializeField] private CompanionMotor _motor;

        [Tooltip("移動と向きの書き手（未設定なら自動取得）。戦闘は意図を出すだけで、Motor へは直接書かない。")]
        [SerializeField] private CompanionMovementArbiter _arbiter;

        [Tooltip("索敵（誰を狙うか。未設定なら自動取得）。")]
        [SerializeField] private CompanionTargetTracker _tracker;

        [Tooltip("Hitbox の対象レイヤー（既定は全レイヤー。IDamageable と Faction で絞る）。")]
        [SerializeField] private LayerMask _targetMask = ~0;

        [Tooltip("判定 Box の左右の広さ（half extent, m）。前方の長さは攻撃 Data の使用距離から自動で決まるため設定しない。")]
        [SerializeField] private float _hitboxHalfWidth = 0.5f;

        [Tooltip("判定 Box 中心の高さ（m）。")]
        [SerializeField] private float _hitboxHeight = 0.6f;

        [Tooltip("判定 Box の上下の広さ（half extent, m）。")]
        [SerializeField] private float _hitboxHalfHeight = 0.6f;

        [Header("Diagnostics")]
        [Tooltip("判断の入力値（対象・距離・角度・クールダウン・攻撃設定）を Console へ出す。実機で"
            + "「近づくが攻撃しない」等を切り分けるための開発用スイッチ。既定は無効で、出荷 Build では何もしない。")]
        [SerializeField] private bool _logDecision;

        private readonly CompanionAttackState _attack = new CompanionAttackState();
        private readonly HitInstanceAllocator _allocator = new HitInstanceAllocator();
        private readonly MultiHitTracker _hitTracker = new MultiHitTracker();
        private readonly Collider[] _overlapBuffer = new Collider[16];

        private ICompanionDefenseState _defense; // 防御中は攻撃を始めない（同一 GameObject。未装備なら null）。
        private CompanionActor _subscribedActor; // 状態通知の購読先（対称管理・重複購読防止）。
        private HitId _currentSwing;
        private AttackSnapshot _snapshot; // 攻撃開始時に確定する不変値（実行中に原本が変わっても揺れない）。
        private float _attackPower;       // 同上（攻撃開始時の攻撃力を固定する）。
        private CompanionAttackPlan _plan; // 同上（間合い・秒数・CD・判定寸法。攻撃中はこれしか読まない）。
        private float _cooldownRemaining;
        private bool _wasEngaged;

        private const float LogIntervalSeconds = 0.5f;
        private CompanionEngageDecision _loggedDecision = CompanionEngageDecision.Idle;
        private float _lastLogTime = float.NegativeInfinity;

        /// <summary>攻撃進行（テスト・Debug 用）。</summary>
        public CompanionAttackState AttackState => _attack;

        /// <summary>直近の判断（テスト・Debug 用）。</summary>
        public CompanionEngageDecision Decision { get; private set; } = CompanionEngageDecision.Idle;

        /// <inheritdoc />
        public bool IsEngaged => _attack.IsAttacking || Decision != CompanionEngageDecision.Idle;

        /// <summary>攻撃中か（予兆・判定・後隙）。</summary>
        public bool IsAttacking => _attack.IsAttacking;

        /// <summary>クールダウンの残り秒（テスト・Debug 用）。</summary>
        public float CooldownRemaining => _cooldownRemaining;

        /// <summary>
        /// いま進行中の攻撃が開始時に確定した内容（攻撃していなければ <see cref="CompanionAttackPlan.None"/>。
        /// テスト・診断用）。攻撃中に Data を書き換えてもここは変わらないことが F06 の受入条件。
        /// </summary>
        public CompanionAttackPlan ActivePlan => _plan;

        /// <summary>これまでに命中を与えた回数（テスト・診断用）。</summary>
        public int HitCount { get; private set; }

        /// <summary>これまでに開始した攻撃の回数（テスト・診断用）。</summary>
        public int AttackCount { get; private set; }

        /// <summary>直近の判断で用いた対象までの水平距離（対象が無ければ <see cref="float.MaxValue"/>。診断用）。</summary>
        public float LastDistance { get; private set; } = float.MaxValue;

        /// <summary>直近の判断で用いた対象方向との角度（度。診断用）。</summary>
        public float LastAngle { get; private set; } = 180f;

        /// <summary>攻撃 Data が配線され、攻撃できる構成か（診断用）。false なら接近も攻撃もしない。</summary>
        public bool HasAttackData => ResolveSettings().HasAttack;

        /// <summary>現在の対象（索敵の結果。無ければ null）。</summary>
        public IPerceptionTarget CurrentTarget => _tracker != null ? _tracker.CurrentTarget : null;

        /// <summary>Actor・Motor・索敵を注入する（Prefab 構築・テスト。null は無視して既存を保つ）。</summary>
        public void Bind(CompanionActor actor, CompanionMotor motor = null, CompanionTargetTracker tracker = null)
        {
            if (actor != null && !ReferenceEquals(_actor, actor))
            {
                _actor = actor;
                if (isActiveAndEnabled)
                {
                    SubscribeState(); // Actor を差し替えたら購読も張り替える。
                }
            }

            if (motor != null)
            {
                _motor = motor;
            }

            if (tracker != null)
            {
                _tracker = tracker;
            }
        }

        /// <summary>
        /// 戦闘を 1 Tick 進める（Update から呼ばれるが、テストは決定的に直接呼べる）。物理 Hitbox は
        /// <c>PollHitbox</c> が担い、命中の適用は <see cref="TryApplyHit"/> から行う。
        /// </summary>
        public void TickCombat(float deltaTime)
        {
            ResolveComponents();
            if (_actor == null)
            {
                return;
            }

            // 活動の許可を先に見る（P4-FIX F05）。停止の判断を Update 側だけに置くと、
            // 別の駆動から TickCombat が呼ばれた瞬間に素通りしてしまう。入口で決める。
            CompanionActivity activity = CompanionActivityProvider.Activity;

            if (activity.DiscardOngoing)
            {
                // 会話・イベント：古い攻撃を捨てる。復帰時に途中の Active から再開させない。
                CancelAttack();
                Decision = CompanionEngageDecision.Idle;
                _wasEngaged = false;
                return;
            }

            if (!activity.ClocksRun)
            {
                // Pause：凍結する。段・HitId・既命中集合は保ったまま、時計も判定も進めない。
                // dt=0 で呼ばれたときに「判定だけ出る」抜け道を作らないため、ここで丸ごと返す。
                ForceStopMovement();
                return;
            }

            float dt = deltaTime < 0f ? 0f : deltaTime;
            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - dt);
            }

            // 参加できない状態（ダウン・ひるみ・退場・復帰待ち）は、攻撃を中断して移動も手放す。
            if (!CompanionTargetTracker.CanEngage(_actor.State))
            {
                CancelAttack();
                Decision = CompanionEngageDecision.Idle;
                _wasEngaged = false;
                return;
            }

            // 攻撃中は攻撃だけを進める（移動しない）。
            // ここでは Data を読み直さず、開始時に確定した _plan だけを使う。攻撃の途中で間合い・秒数・
            // 判定寸法が変わると、振り始めた条件と違う条件で終わることになる（F06）。
            if (_attack.IsAttacking)
            {
                CompanionAttackPhase previous = _attack.Phase;
                _attack.Tick(dt);
                ApplyPhaseState(previous, _attack.Phase);

                if (_attack.IsHitboxActive)
                {
                    PollHitbox(_plan);
                }

                if (_attack.Finished)
                {
                    FinishAttack(_plan);
                }

                _wasEngaged = true;
                return;
            }

            CompanionAttackSettings settings = ResolveSettings();

            // 構え・回避の最中は攻撃を始めない。状態も奪わない（防御側が Guard／Evade 状態を持っている）。
            if (IsDefending())
            {
                Decision = CompanionEngageDecision.Hold;
                SubmitStop();
                _wasEngaged = true;
                return;
            }

            IPerceptionTarget target = CurrentTarget;
            bool hasTarget = CompanionTargetSelection.IsUsable(target);
            Vector3 selfPosition = _actor.WorldPosition;
            Vector3 toTarget = hasTarget ? target.Position - selfPosition : Vector3.zero;
            float distance = hasTarget ? FormationSlot.HorizontalDistance(selfPosition, target.Position) : float.MaxValue;
            float angle = hasTarget ? CombatGeometry.BearingDegrees(_actor.Forward, toTarget) : 180f;

            LastDistance = distance;
            LastAngle = angle;
            // 新しい行動を始めてよいかは活動 Context が決める（時計は動くが行動は始めない状況を表せるようにしておく）。
            Decision = CompanionEngagement.Decide(
                hasTarget, activity.CanAct, distance, angle, settings, _cooldownRemaining);
            LogDecision(settings, hasTarget, target);

            switch (Decision)
            {
                case CompanionEngageDecision.Attack:
                    _actor.SetFacing(toTarget);
                    SubmitStop();
                    BeginAttack(settings);
                    break;

                case CompanionEngageDecision.Chase:
                    _actor.SetFacing(toTarget);
                    RequestChase();
                    MoveToward(target.Position, settings);
                    break;

                case CompanionEngageDecision.Hold:
                    _actor.SetFacing(toTarget);
                    RequestChase();
                    SubmitStop();
                    break;

                default:
                    // 戦闘から抜けた瞬間だけ移動を手放す。以降は追従が握るため触らない。
                    if (_wasEngaged)
                    {
                        SubmitStop();
                        ReleaseMovement();
                    }

                    break;
            }

            _wasEngaged = IsEngaged;
        }

        /// <summary>
        /// 1 対象へ命中を適用する（Active 中のみ）。自身・味方は除外し、同一 Swing で 1 対象 1Hit。適用したら true。
        /// 物理に依存せずテストから直接呼べる（<paramref name="targetActor"/> は Faction・背後判定に用いる）。
        /// </summary>
        public bool TryApplyHit(IDamageable target, ICombatActor targetActor, Vector3 hitPoint)
        {
            if (target == null || !_attack.IsHitboxActive || _actor == null)
            {
                return false;
            }

            // 停止中は命中も出さない（P4-FIX F05）。Update を止めるだけでは、外から直接呼ばれたときに素通りする。
            if (!CompanionActivityProvider.Activity.ClocksRun)
            {
                return false;
            }

            // 自身除外（同一ルートの被弾受け口）。
            if (target is Component targetComponent && targetComponent.transform.root == transform.root)
            {
                return false;
            }

            // Faction フィルタ：仲間は敵にだけ当てる。主人公・他の仲間・中立へは当てない（誤射しない）。
            // 陣営が分からない対象（ICombatActor 未実装）も対象外とする（曖昧なものを殴らない）。
            if (targetActor == null || targetActor.Faction != CombatFaction.Enemy)
            {
                return false;
            }

            // 同一 Swing で 1 対象 1Hit。
            if (!_hitTracker.TryRegisterHit(_currentSwing, target))
            {
                return false;
            }

            bool isBackHit = CombatGeometry.IsBackHit(targetActor.Forward, _actor.WorldPosition - targetActor.WorldPosition);

            bool targetActing = false;
            if (targetActor is Component actorComponent)
            {
                var activity = actorComponent.GetComponentInParent<ICombatActivityState>();
                if (activity != null)
                {
                    targetActing = activity.IsPoiseVulnerableAction;
                }
            }

            HitInfo hit = CompanionHitFactory.Build(
                _snapshot, _attackPower, _actor, target, _actor.Forward, hitPoint, _currentSwing, isBackHit, targetActing);
            target.ReceiveHit(hit);
            HitCount++;
            return true;
        }

        /// <summary>攻撃を中断し、判定を消す（ひるみ・ダウン・退場・無効化・Scene 離脱で共通。冪等）。</summary>
        public void CancelAttack()
        {
            if (!_attack.IsAttacking)
            {
                return;
            }

            _attack.Cancel();
            _plan = CompanionAttackPlan.None;
            _hitTracker.Clear();

            // 中断は「行動を奪われた」側なので強制停止で通す。同じフレームに追従が歩き出すのを防ぐ
            // （1 フレームでも動くと、倒れたはずの仲間が滑る）。
            ForceStopMovement();
        }

        /// <inheritdoc />
        /// <remarks>
        /// ひるみ・ダウン・退場へ入った瞬間に判定を消す。Tick を待つと、その 1 フレームのあいだ判定が残り、
        /// 倒れたはずの仲間が敵を殴ってしまう（追従の即時停止と同じ理由）。
        /// </remarks>
        public void OnCompanionStateChanged(in CompanionStateChanged change)
        {
            if (CompanionTargetTracker.CanEngage(change.Current))
            {
                return;
            }

            CancelAttack();
            Decision = CompanionEngageDecision.Idle;
            _wasEngaged = false;
        }

        // ---- 内部 ----

        /// <summary>戦闘としての移動意図を出す（受理されるかは調停役が決める。P4-FIX F02a）。</summary>
        private void SubmitMove(in CompanionMoveRequest request)
        {
            if (_arbiter != null)
            {
                _arbiter.Submit(CompanionMovementOwner.Combat, request);
                return;
            }

            ApplyDirectly(request); // 調停役が無い構成（旧 Scene）でも動くようにする。
        }

        /// <summary>戦闘として止まる（所有権は握ったまま。追従に取り返させない）。</summary>
        private void SubmitStop()
        {
            SubmitMove(CompanionMoveRequest.Stop());
        }

        /// <summary>
        /// 強制的に止める（中断・活動停止）。所有権に関わらず通り、同じフレームの通常の移動決定より優先される。
        /// </summary>
        private void ForceStopMovement()
        {
            if (_arbiter != null)
            {
                _arbiter.ForceStop();
                return;
            }

            _motor?.Stop(); // 調停役が無い構成（旧 Scene）でも止まるようにする。
        }

        /// <summary>戦闘の所有権を手放す（追従へ返す）。</summary>
        private void ReleaseMovement()
        {
            _arbiter?.Release(CompanionMovementOwner.Combat);
        }

        /// <summary>調停役が居ない構成のための直接適用（移行期の保険。新しい Scene では通らない）。</summary>
        private void ApplyDirectly(in CompanionMoveRequest request)
        {
            if (_motor == null)
            {
                return;
            }

            switch (request.Kind)
            {
                case CompanionMoveKind.Move:
                    _motor.Configure(request.Speed, request.StopRadius);
                    _motor.SetMoveTarget(request.Target);
                    break;

                case CompanionMoveKind.Warp:
                    _motor.WarpTo(request.Target);
                    break;

                case CompanionMoveKind.Stop:
                    _motor.Stop();
                    break;
            }
        }

        private CompanionAttackSettings ResolveSettings()
        {
            AttackData data = _actor != null && _actor.Data != null ? _actor.Data.BasicAttack : null;
            return CompanionAttackSettings.From(data);
        }

        private void BeginAttack(in CompanionAttackSettings settings)
        {
            AttackData data = _actor.Data != null ? _actor.Data.BasicAttack : null;

            // 攻撃開始時に数値を確定する（実行中に SO 原本が変わっても揺れない。§2.2）。
            _snapshot = AttackSnapshot.FromData(data);
            _attackPower = _actor.Data != null ? _actor.Data.AttackPower : 0f;

            // 間合い・秒数・クールダウン・判定寸法も同じ瞬間に写し取る。以降この攻撃が終わるまで Data も
            // Inspector も読み直さない（F06。読み直していたころは、Play 中の数値調整が振っている最中の
            // 攻撃に割り込み、判定の届く距離だけが伸びるといった再現できない挙動になった）。
            _plan = new CompanionAttackPlan(settings, _hitboxHalfWidth, _hitboxHeight, _hitboxHalfHeight);

            _currentSwing = _allocator.NextSingle();
            _hitTracker.Clear();

            if (!_attack.Begin(settings.StartupSeconds, settings.ActiveSeconds, settings.RecoverySeconds))
            {
                _plan = CompanionAttackPlan.None;
                return; // 長さゼロの攻撃は成立しない（Data の設定ミス。無言で判定を出さない）。
            }

            AttackCount++;
            _actor.RequestState(CompanionState.AttackPrepare, CompanionStateChangeReason.AttackStarted);

            // 予兆 0 の攻撃は開始と同時に判定段へ入る。段に対応する状態をその場で合わせる。
            ApplyPhaseState(CompanionAttackPhase.Startup, _attack.Phase);
            if (_attack.IsHitboxActive)
            {
                PollHitbox(_plan);
            }
        }

        private void FinishAttack(in CompanionAttackPlan plan)
        {
            _cooldownRemaining = plan.CooldownSeconds;
            _plan = CompanionAttackPlan.None;
            _hitTracker.Clear();
            _actor.RequestState(CompanionState.Chase, CompanionStateChangeReason.AttackFinished);
        }

        /// <summary>攻撃の段に対応する状態へ移す（段が変わったときだけ要求する）。</summary>
        private void ApplyPhaseState(CompanionAttackPhase previous, CompanionAttackPhase current)
        {
            if (previous == current)
            {
                return;
            }

            switch (current)
            {
                case CompanionAttackPhase.Active:
                    _actor.RequestState(CompanionState.AttackActive, CompanionStateChangeReason.AttackAdvanced);
                    break;

                case CompanionAttackPhase.Recovery:
                    _actor.RequestState(CompanionState.AttackRecovery, CompanionStateChangeReason.AttackAdvanced);
                    SubmitStop();
                    break;
            }
        }

        private void RequestChase()
        {
            if (_actor.State != CompanionState.Chase)
            {
                _actor.RequestState(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget);
            }
        }

        /// <summary>対象の手前（停止距離）を目指して移動する。対象の位置そのものへ向かうと押し込みすぎる。</summary>
        private void MoveToward(Vector3 targetPosition, in CompanionAttackSettings settings)
        {
            float speed = _actor.Data != null ? _actor.Data.MoveSpeed : 4.5f;
            float stopRadius = settings.AttackStartDistance > 0f ? settings.AttackStartDistance : 0.35f;
            SubmitMove(CompanionMoveRequest.Move(targetPosition, speed, stopRadius));
        }

        /// <summary>
        /// 判定 Box の中心・回転・半径（Gizmo と判定で同じ式を使い、見た目と判定を一致させる）。
        ///
        /// <b>前方の長さは攻撃 Data の使用距離そのもの</b>とし、足元から真正面 <see cref="CompanionAttackSettings.UseRange"/> までを
        /// 覆う。判定の届く距離を別の値で持つと、判断が「間合い」と言っている距離に判定が届かず、延々と空振りする
        /// （P4-03 受入で実際に起きた：開始 1.6m に対し判定 1.2m）。数値の正本は Data 側の 1 箇所だけにする。
        /// </summary>
        private void ResolveHitbox(in CompanionAttackPlan plan, out Vector3 center, out Quaternion rotation,
            out Vector3 halfExtents)
        {
            Vector3 forward = _actor.Forward;
            float reach = plan.Reach;
            center = _actor.WorldPosition + forward * (reach * 0.5f) + Vector3.up * plan.HitboxHeight;
            rotation = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z), Vector3.up);
            halfExtents = new Vector3(plan.HitboxHalfWidth, plan.HitboxHalfHeight, reach * 0.5f);
        }

        private void PollHitbox(in CompanionAttackPlan plan)
        {
            if (plan.Reach <= 0f)
            {
                return;
            }

            ResolveHitbox(plan, out Vector3 center, out Quaternion rotation, out Vector3 halfExtents);

            // Physics.autoSyncTransforms=0 のため、問い合わせ前に明示同期する（移動中の敵を取りこぼさない）。
            Physics.SyncTransforms();
            int count = Physics.OverlapBoxNonAlloc(
                center, halfExtents, _overlapBuffer, rotation, _targetMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlapBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                var target = collider.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    TryApplyHit(target, collider.GetComponentInParent<ICombatActor>(), center);
                }
            }
        }

        /// <summary>
        /// 判断の入力値を Console へ出す（<c>_logDecision</c> が有効なときだけ）。判断が変わった瞬間と、
        /// 同じ判断が続く間は 0.5 秒ごとに出す（毎フレーム出して Console を埋めない）。
        /// 「近づくが攻撃しない」「攻撃しているのに当たらない」のどちらなのかを、憶測ではなく値で切り分けるための窓。
        /// </summary>
        private void LogDecision(in CompanionAttackSettings settings, bool hasTarget, IPerceptionTarget target)
        {
            if (!_logDecision)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (Decision == _loggedDecision && now - _lastLogTime < LogIntervalSeconds)
            {
                return;
            }

            _loggedDecision = Decision;
            _lastLogTime = now;

            string targetName = hasTarget && target is Component component ? component.name : "(none)";
            Debug.Log(
                "[Companion] " + name + " decision=" + Decision
                + " state=" + _actor.State
                + " target=" + targetName
                + " distance=" + LastDistance.ToString("F2")
                + " angle=" + LastAngle.ToString("F1")
                + " cooldown=" + _cooldownRemaining.ToString("F2")
                + " | hasAttack=" + settings.HasAttack
                + " useRange=" + settings.UseRange.ToString("F2")
                + " useAngle=" + settings.UseAngle.ToString("F0")
                + " attackStart=" + settings.AttackStartDistance.ToString("F2")
                + " attackPower=" + (_actor.Data != null ? _actor.Data.AttackPower : 0f).ToString("F0")
                + " | attacks=" + AttackCount + " hits=" + HitCount,
                this);
        }

        /// <summary>判定 Box を Scene ビューへ描く（選択中のみ）。判定中は赤、それ以外は薄い灰色。</summary>
        private void OnDrawGizmosSelected()
        {
            if (_actor == null)
            {
                return;
            }

            CompanionAttackSettings settings = ResolveSettings();
            if (!settings.HasAttack)
            {
                return;
            }

            // 描画は「いま Data に入っている値」で見せる（調整中の値をその場で確認したいため）。
            // 攻撃中に実際に使われるのは開始時に確定した _plan なので、振っている最中は両者がずれ得る。
            CompanionAttackPlan preview = _attack.IsAttacking
                ? _plan
                : new CompanionAttackPlan(settings, _hitboxHalfWidth, _hitboxHeight, _hitboxHalfHeight);

            ResolveHitbox(preview, out Vector3 center, out Quaternion rotation, out Vector3 halfExtents);
            Gizmos.color = _attack.IsHitboxActive ? new Color(1f, 0.2f, 0.1f, 0.9f) : new Color(1f, 1f, 1f, 0.25f);
            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
            Gizmos.matrix = Matrix4x4.identity;

            // 攻撃を始める間合い（この内側に入ってから振る）。判定 Box は必ずこれより遠くまで届く。
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.5f);
            Gizmos.DrawWireSphere(_actor.WorldPosition, settings.AttackStartDistance);
        }

        private void Update()
        {
            // 停止の判断は TickCombat の入口に移した（P4-FIX F05）。ここで返してしまうと、
            // Pause 中の「凍結して Motor を止める」処理まで走らなくなる。
            TickCombat(Time.deltaTime);
        }

        private void OnEnable()
        {
            ResolveComponents();
            SubscribeState();
            _cooldownRemaining = 0f;
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱で判定・購読・移動指示を残さない（§2.3 後始末）。
            UnsubscribeState();
            _attack.Cancel();
            _plan = CompanionAttackPlan.None;
            _hitTracker.Clear();
            Decision = CompanionEngageDecision.Idle;
            _wasEngaged = false;
        }

        /// <summary>
        /// 防御中（構え・回避の無敵中）か。防御と攻撃はどちらも同じ 1 体の行動なので、同時には成立させない。
        /// 防御コンポーネントが付いていない構成では常に false（従来どおり攻撃する）。
        /// </summary>
        private bool IsDefending()
        {
            if (_defense is Object destroyed && destroyed == null)
            {
                _defense = null;
            }

            if (_defense == null)
            {
                _defense = GetComponent<ICompanionDefenseState>();
            }

            return _defense != null && (_defense.IsGuarding || _defense.IsEvadeInvulnerable);
        }

        private void SubscribeState()
        {
            if (ReferenceEquals(_subscribedActor, _actor))
            {
                return;
            }

            UnsubscribeState();
            _subscribedActor = _actor;
            _subscribedActor?.States.AddListener(this);
        }

        private void UnsubscribeState()
        {
            _subscribedActor?.States.RemoveListener(this);
            _subscribedActor = null;
        }

        private void ResolveComponents()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }

            if (_motor == null)
            {
                _motor = GetComponent<CompanionMotor>();
            }

            if (_arbiter == null)
            {
                _arbiter = GetComponent<CompanionMovementArbiter>();
            }

            if (_tracker == null)
            {
                _tracker = GetComponent<CompanionTargetTracker>();
            }

            // 自動取得で Actor が後から解決された場合にも購読を張る（Bind 経由でない Scene 構成の保険）。
            if (isActiveAndEnabled && !ReferenceEquals(_subscribedActor, _actor))
            {
                SubscribeState();
            }
        }

    }
}
