using Momotaro.Data.Characters;
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
    public sealed class PlayerStateController : MonoBehaviour, ICombatActor, IGuardState, IJustGuardState, IEvadeState, ISpecialChargeCancel
    {
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private PlayerFacing _facing;
        [SerializeField] private PlayerMovementData _movement;

        [Range(0f, 1f)]
        [Tooltip("この大きさ未満の移動入力は静止扱い。")]
        [SerializeField] private float _moveThreshold = 0.1f;

        [Tooltip("ガード／ジャストガードのパラメータ（JG 受付窓・解除窓・連打ペナルティ）。未割当なら既定値。P2-08。")]
        [SerializeField] private GuardData _guardData;

        [Tooltip("ステップ回避のパラメータ（距離・移動/後硬直秒・無敵区間・消費・連続窓）。未割当なら既定値。P2-09。")]
        [SerializeField] private StepData _stepData;

        [Tooltip("必殺技のパラメータ（チャージ2.0/保持0.75/7.0倍/防御無視/スタン1.5/ひるませ100/後隙）。未割当なら必殺技不可。P2-10。")]
        [SerializeField] private SpecialAttackData _specialData;

        [Header("Attack (P2-03B)")]
        [Tooltip("通常攻撃コンボ構成（段順 AttackData ＋ 先行入力秒）。未割当なら攻撃不可。")]
        [SerializeField] private PlayerAttackComboData _attackCombo;

        [Tooltip("攻撃者の基礎データ（攻撃力＝HP ダメージ計算に使用。主人公=SO_Player_Momotaro）。P2-04。")]
        [SerializeField] private CharacterData _attackerStats;

        [Tooltip("Hitbox 中心の Facing 方向オフセット（m）。")]
        [SerializeField] private float _hitboxForwardOffset = 0.8f;

        [Tooltip("Hitbox の半径（各軸の half extent, m）。")]
        [SerializeField] private Vector3 _hitboxHalfExtents = new Vector3(0.6f, 0.5f, 0.6f);

        [Tooltip("Hitbox 中心の高さ（m）。")]
        [SerializeField] private float _hitboxHeight = 0.5f;

        [Tooltip("命中対象の Layer。")]
        [SerializeField] private LayerMask _targetMask = ~0;

        [Header("Debug")]
        [Tooltip("攻撃 Hitbox を Scene ビューにギズモ表示する（判定中は赤、それ以外は黄）。検証用。")]
        [SerializeField] private bool _debugDrawHitbox = true;

        private readonly PlayerStateMachine _machine = new PlayerStateMachine();
        private readonly HitInstanceAllocator _hitAllocator = new HitInstanceAllocator();
        private readonly MultiHitTracker _hitTracker = new MultiHitTracker();
        private readonly Collider[] _overlapBuffer = new Collider[16];

        private AttackComboMachine _combo;
        private AttackInputBuffer _attackBuffer;
        private JustGuardState _justGuard;
        private StepState _step;
        private bool _stepChainBuffered;
        private SpecialChargeState _special;
        private float _specialAttackRemaining;
        private float _specialActiveRemaining;
        private HitId _specialSwing;
        private bool _prevGuardHeld;
        private IPlayerInput _input;
        private HitId _currentSwing;
        private PlayerVitalsHolder _vitals;
        private bool _vitalsResolved;

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

        // ---- IGuardState（被弾側のガード状態。命中解決が参照） ----

        /// <inheritdoc />
        public bool IsGuarding => _machine.Current == PlayerState.GuardIdle || _machine.Current == PlayerState.GuardMove;

        /// <inheritdoc />
        /// <remarks>ガード中は Facing がロックされるため、押下時に固定した前方をそのまま返す。</remarks>
        public Vector3 GuardForward => Forward;

        // ---- IJustGuardState（JG 受付状態。命中解決が参照） ----

        /// <inheritdoc />
        public bool CanJustGuard => _justGuard != null && _justGuard.CanJustGuard;

        /// <inheritdoc />
        public void NotifyJustGuardSuccess() => _justGuard?.NotifySuccess();

        /// <summary>JG 入力状態（HUD 等の検証表示用）。未初期化時は Normal。</summary>
        public JustGuardPhase JustGuardPhase => _justGuard != null ? _justGuard.Phase : JustGuardPhase.Normal;

        // ---- IEvadeState（ステップ無敵。命中解決が参照） ----

        /// <inheritdoc />
        public bool IsInvincible => _step != null && _step.IsInvincible;

        /// <summary>ステップ回避中か（検証表示用）。</summary>
        public bool IsStepping => _step != null && _step.IsActive;

        // ---- 必殺技（Phase2 P2-10） ----

        /// <summary>必殺技チャージ中か。</summary>
        public bool IsSpecialCharging => _special != null && _special.IsActive;

        /// <summary>必殺技（発動・後隙）実行中か。</summary>
        public bool IsSpecialAttacking => _specialAttackRemaining > 0f;

        /// <summary>チャージ経過秒（HUD/検証用）。</summary>
        public float SpecialChargeElapsed => _special != null ? _special.Elapsed : 0f;

        /// <summary>チャージが最大到達済みか（HUD/検証用）。</summary>
        public bool IsSpecialCharged => _special != null && _special.IsCharged;

        /// <inheritdoc />
        public void CancelSpecialChargeOnHit()
        {
            // 被弾（実ダメージ）で必殺技チャージを中断する（発動・後隙中は中断しない）。
            if (_special != null && _special.IsActive)
            {
                _special.Cancel();
            }
        }

        private PlayerVitalsHolder ResolveVitals()
        {
            if (!_vitalsResolved)
            {
                _vitals = GetComponentInParent<PlayerVitalsHolder>();
                _vitalsResolved = true;
            }

            return _vitals;
        }

        /// <summary>ガードブレイク（行動不能）中か。Vitals（<see cref="PlayerVitalsHolder"/>）が無ければ常に false。</summary>
        private bool IsGuardBroken
        {
            get
            {
                PlayerVitalsHolder v = ResolveVitals();
                return v != null && v.IsGuardBroken;
            }
        }

        private void Awake()
        {
            EnsureRuntime();
            // 主人公を Player レイヤーへ（配下 Collider 含む）。Player↔Enemy のみ衝突無効（敵すり抜け）、壁(Default)は停止（P2-09）。
            // 本コンポーネントはプレイヤー Prefab のルートに付くため、自身の GameObject を基点にする（Scene 親の他 Collider を巻き込まない）。
            CombatLayers.ConfigurePlayer(gameObject);
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
            _justGuard?.Reset();
            _step?.Reset();
            _stepChainBuffered = false;
            _special?.Reset();
            _specialAttackRemaining = 0f;
            _specialActiveRemaining = 0f;
            _prevGuardHeld = false;
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

            if (_justGuard == null)
            {
                _justGuard = _guardData != null
                    ? new JustGuardState(_guardData.JustGuardWindowSeconds, _guardData.JustGuardReleaseWindowSeconds, _guardData.ReleasePenaltySeconds)
                    : new JustGuardState();
            }

            if (_step == null)
            {
                _step = _stepData != null
                    ? new StepState(_stepData.Distance, _stepData.MoveSeconds, _stepData.RecoverySeconds,
                        _stepData.InvincibleStartSeconds, _stepData.InvincibleEndSeconds, _stepData.ChainBufferSeconds)
                    : new StepState(3f);
            }

            if (_special == null && _specialData != null)
            {
                _special = new SpecialChargeState(_specialData.ChargeSeconds, _specialData.MaxHoldSeconds);
            }
        }

        private void Update()
        {
            EnsureRuntime();

            if (_input == null)
            {
                _input = PlayerInputProvider.Current;
            }

            // 状態優先度：ガードブレイク（行動不能）中は入力を無効化し、ガード・攻撃・移動・Buffer を受け付けない。
            // 状態機械へは guardBroken を最優先で渡し、独立状態 GuardBreak を表現する（仕様書 §3.2 / P2-07）。
            bool broken = IsGuardBroken;
            bool active = _input != null && _input.Active && !broken;

            // ブレイク中に発生した攻撃・ステップ押下は破棄し、復帰後へ残さない（P2-07/P2-09）。
            if (broken && _input != null)
            {
                _input.ConsumeAttackPressed();
                _input.ConsumeStepPressed();
            }

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

            // ステップ回避（ガードブレイク未満・攻撃/ガード/移動より優先。§3/§10）。開始・時間経過・連続予約を処理する。
            bool stepping = DriveStep(active, broken, isMoving);

            // 必殺技（チャージ・発動）。ステップ未満・攻撃/ガードより優先。ガード押下でキャンセル→JG受付、被弾で中断（§3.6）。
            bool special = !stepping && DriveSpecial(active, broken, guarding, isMoving);
            bool charging = !stepping && _special != null && _special.IsActive;
            bool specialAttacking = _specialAttackRemaining > 0f;

            bool blockOther = stepping || charging || specialAttacking;
            if (!blockOther)
            {
                DriveCombo(active, guarding);
            }
            else if (_combo != null && _combo.IsActive)
            {
                _combo.Interrupt();
                _hitTracker.Clear();
            }

            bool attacking = !blockOther && _combo != null && _combo.IsActive;

            // JG 受付は「ガードが実際に有効化できる」ときだけ開く（仕様書 §3.3 / 攻撃キャンセル規則）。
            // ステップ/必殺技中はガード・JG 不可。非攻撃、またはキャンセル窓到達で攻撃を中断できたときに限る。
            bool guardEffective = !blockOther && guarding && !attacking;
            DriveJustGuard(guardEffective);

            _machine.Tick(active, isMoving, guarding, attacking, broken, stepping, charging, specialAttacking);

            // 段開始フレーム：向き再確定・新 Swing Token・段番号更新（踏み込みは ApplyAttackMotion）。
            if (!blockOther && _combo != null && _combo.StageJustStarted)
            {
                AttackStage = _combo.Stage;
                _currentSwing = _hitAllocator.NextSingle();
                if (_facing != null)
                {
                    _facing.ConfirmFromInput(active ? _input.Move : Vector2.zero);
                }
            }

            // チャージ中は移動不可・方向転換のみ（入力方向へ Facing を確定）。
            if (charging && !specialAttacking && _facing != null && isMoving)
            {
                _facing.ConfirmFromInput(_input.Move);
            }

            // 移動抑制はチャージ中も含める（移動不可）。ただし Facing はチャージ中も回せる（方向転換のみ）ためロックには含めない。
            ApplyMotion(stepping || charging || specialAttacking, attacking);
            ApplyStateEffects(guarding, attacking, stepping || specialAttacking);

            if (attacking && _combo.HitboxActive)
            {
                PollHitbox();
            }
        }

        /// <summary>
        /// ステップ回避を駆動する（Phase2 P2-09）。時間を進め、終了直前の先行入力で連続ステップを予約し、押下で新規開始する。
        /// ステップ中は true を返す。攻撃中でも優先して開始でき、開始時に攻撃を中断する。
        /// </summary>
        private bool DriveStep(bool active, bool broken, bool isMoving)
        {
            if (_step == null)
            {
                return false;
            }

            // GameMode 遮断／行動不能（active=false）では、実行中ステップ・無敵・連続予約・入力を即時解除する（P2-09）。
            // これにより PlayerState.Step・無敵・StepVelocity・MovementSuppressed が次段の処理で解除される。
            if (!active)
            {
                if (_step.IsActive)
                {
                    _step.Reset();
                }

                _stepChainBuffered = false;
                _input?.ConsumeStepPressed();
                return false;
            }

            bool wasStepping = _step.IsActive;
            _step.Tick(Time.deltaTime);

            bool stepPressed = active && _input != null && _input.ConsumeStepPressed();

            if (_step.IsActive)
            {
                // 終了直前の先行入力で連続ステップを予約（残スタミナが必要）。
                if (stepPressed && _step.CanChain && CanAffordStep())
                {
                    _stepChainBuffered = true;
                }

                return true;
            }

            // 非ステップ。直前がステップなら終了フレーム：予約があれば連続ステップ。
            if (wasStepping && _stepChainBuffered)
            {
                _stepChainBuffered = false;
                if (!broken && TryStartStep(isMoving))
                {
                    return true;
                }
            }

            // 新規開始（非ブレイク・GameMode 有効・押下）。
            if (!broken && stepPressed && TryStartStep(isMoving))
            {
                return true;
            }

            return false;
        }

        /// <summary>ステップ開始を試みる。方向確定・スタミナ消費に成功したら攻撃を中断して開始する。</summary>
        private bool TryStartStep(bool isMoving)
        {
            Vector3 dir = ComputeStepDirection(isMoving);
            if (dir.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            if (!TryConsumeStepStamina())
            {
                return false; // スタミナ不足はステップ不発（消費なし・ブレイクなし）。
            }

            if (_combo != null && _combo.IsActive)
            {
                _combo.Interrupt();
                _hitTracker.Clear();
            }

            _justGuard?.Reset();
            _prevGuardHeld = false;
            _step.Begin(dir);
            return true;
        }

        /// <summary>ステップ方向。移動入力があればその方向、無入力なら現在向きの後方（仕様書 3.4）。</summary>
        private Vector3 ComputeStepDirection(bool isMoving)
        {
            if (isMoving && _input != null)
            {
                Vector3 d = PlayerMovementCalculator.ToPlanarVelocity(_input.Move, 1f);
                if (d.sqrMagnitude > 1e-6f)
                {
                    return d.normalized;
                }
            }

            return -Forward;
        }

        private float StepStaminaCost => _stepData != null ? _stepData.StaminaCost : 25f;

        private bool CanAffordStep()
        {
            PlayerVitalsHolder v = ResolveVitals();
            return v == null || v.CurrentStamina >= StepStaminaCost;
        }

        private bool TryConsumeStepStamina()
        {
            float cost = StepStaminaCost;
            if (cost <= 0f)
            {
                return true;
            }

            PlayerVitalsHolder v = ResolveVitals();
            return v == null || v.TryConsumeStamina(cost);
        }

        /// <summary>ステップ中は移動抑制＋ステップ速度、そうでなければ攻撃踏み込みを適用する。</summary>
        private void ApplyMotion(bool stepping, bool attacking)
        {
            if (stepping)
            {
                if (_motor != null)
                {
                    _motor.MovementSuppressed = true;
                    _motor.StepVelocity = _step.CurrentVelocity; // 壁は物理が停止、敵は Layer ですり抜け
                }

                return;
            }

            ApplyAttackMotion(attacking);
        }

        /// <summary>
        /// 必殺技のチャージ・発動を駆動する（Phase2 P2-10）。長押しで開始、最大未満 Release は不発、最大後 0.75 秒で自動発動。
        /// 遮断・行動不能・ガード入力・被弾で中断（ガードは呼び出し側で JG 受付が開く）。チャージ/発動中は true を返す。
        /// </summary>
        private bool DriveSpecial(bool active, bool broken, bool guarding, bool isMoving)
        {
            if (_special == null)
            {
                return false; // 必殺技未設定（SpecialAttackData 未割当）
            }

            // 発動・後隙の実行フェーズ（この間はキャンセル不可）。
            if (_specialAttackRemaining > 0f)
            {
                float dt = Time.deltaTime;
                bool inActiveWindow = _specialActiveRemaining > 0f;
                _specialAttackRemaining -= dt;
                if (_specialActiveRemaining > 0f)
                {
                    _specialActiveRemaining -= dt;
                }

                if (inActiveWindow)
                {
                    PollSpecialHitbox();
                }

                if (_specialAttackRemaining <= 0f)
                {
                    _specialAttackRemaining = 0f;
                    _specialActiveRemaining = 0f;
                    _hitTracker.Clear();
                }

                return true;
            }

            // 遮断・行動不能・ガード入力ではチャージを中断（後隙なし）。
            if (!active || broken || guarding)
            {
                if (_special.IsActive)
                {
                    _special.Cancel();
                }

                return false;
            }

            bool held = _input != null && _input.SpecialAttackHeld;

            if (_special.IsActive)
            {
                _special.Tick(Time.deltaTime);

                if (_special.ShouldAutoFire)
                {
                    FireSpecial();
                    return true;
                }

                if (!held)
                {
                    if (_special.Release() == SpecialReleaseResult.Fire)
                    {
                        FireSpecial();
                        return true;
                    }

                    return false; // 最大未満で離した：不発
                }

                return true; // チャージ継続
            }

            if (held)
            {
                _special.Begin();
                return true;
            }

            return false;
        }

        /// <summary>必殺技を発動する（チャージ終了→発動＋後隙へ）。攻撃中なら中断して開始する。</summary>
        private void FireSpecial()
        {
            _special.Cancel();
            if (_combo != null && _combo.IsActive)
            {
                _combo.Interrupt();
            }

            float activeSeconds = _specialData != null ? _specialData.ActiveSeconds : 0.15f;
            float recoverySeconds = _specialData != null ? _specialData.RecoverySeconds : 0.9f;
            _specialActiveRemaining = activeSeconds;
            _specialAttackRemaining = activeSeconds + recoverySeconds;
            _specialSwing = _hitAllocator.NextSingle();
            _hitTracker.Clear();
        }

        /// <summary>必殺技の判定（Active 中）。7.0 倍・防御一部無視・スタン固有 1.5・ひるませ 100。ガード/JG 不能（貫通）。</summary>
        private void PollSpecialHitbox()
        {
            if (_specialData == null)
            {
                return;
            }

            Vector3 center = transform.position + Forward * _hitboxForwardOffset + Vector3.up * _hitboxHeight;
            int count = Physics.OverlapBoxNonAlloc(center, _hitboxHalfExtents, _overlapBuffer, Quaternion.identity, _targetMask, QueryTriggerInteraction.Collide);
            if (count == 0)
            {
                return;
            }

            float attackPower = _attackerStats != null ? _attackerStats.AttackPower : 0f;
            float hpContribution = HpDamageCalculator.AttackContribution(attackPower, _specialData.HpMultiplier);

            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                var target = col.GetComponentInParent<IDamageable>();
                if (target == null)
                {
                    continue;
                }

                if (target is Component tc && tc.transform.root == transform.root)
                {
                    continue;
                }

                if (!_hitTracker.TryRegisterHit(_specialSwing, target))
                {
                    continue;
                }

                var damage = new HitDamage(hpContribution, _specialData.PoiseDamage, _specialData.FlinchPower);
                var hit = new HitInfo(this, target, Forward, center, damage,
                    0f, 0f, guardable: false, justGuardable: false, isJustGuardCounter: false,
                    _specialData.DefenseIgnoreRatio, _specialData.StunHpMultiplier, _specialSwing);
                target.ReceiveHit(hit);
            }
        }

        /// <summary>
        /// ガード押下／解除のエッジからジャストガード受付状態を駆動する（Phase2 P2-08）。時間を進めてから押下/解除を反映する。
        /// ブレイク中や遮断中は <paramref name="guardHeld"/> が false になり、解除エッジとして扱われる。
        /// </summary>
        private void DriveJustGuard(bool guardHeld)
        {
            if (_justGuard == null)
            {
                return;
            }

            _justGuard.Tick(Time.deltaTime);

            if (guardHeld && !_prevGuardHeld)
            {
                _justGuard.Press();
            }
            else if (!guardHeld && _prevGuardHeld)
            {
                _justGuard.Release();
            }

            _prevGuardHeld = guardHeld;
        }

        private void DriveCombo(bool active, bool cancelRequested)
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

            // Guard/Step キャンセル：許可窓（AttackComboMachine.CanCancel）に到達していれば、
            // 連鎖・継続より優先して攻撃を中断する。窓より前にキャンセル入力を保持していても、
            // 窓到達時点で成立する（CanCancel が true になった最初の Tick で中断）。
            if (TryCancelAttack(cancelRequested))
            {
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

        /// <summary>
        /// 攻撃中に、キャンセル要求（Guard 保持／将来の Step）とキャンセル窓（<see cref="AttackComboMachine.CanCancel"/>）が
        /// 揃えば攻撃を中断する。成立したら true。Guard 固有処理と分離しているため、後続の Step 実装から同じ判定・中断を
        /// 再利用できる（Step 本体は先回り実装しない）。
        /// </summary>
        private bool TryCancelAttack(bool cancelRequested)
        {
            if (_combo == null || !_combo.IsActive || !cancelRequested || !_combo.CanCancel)
            {
                return false;
            }

            CancelAttack();
            return true;
        }

        /// <summary>
        /// 攻撃を中断し、攻撃由来の状態を中立化する：コンボ停止・Hitbox 無効化（判定は次フレームから走らない）・
        /// 多重ヒット履歴クリア・踏み込み速度ゼロ・移動抑制解除・攻撃の向きロック解除・先行入力クリア。
        /// これにより同フレームで <see cref="PlayerStateMachine"/> が GuardIdle/GuardMove へ遷移できる。
        /// </summary>
        private void CancelAttack()
        {
            _combo.Interrupt();
            _hitTracker.Clear();
            _attackBuffer.Clear();

            if (_motor != null)
            {
                _motor.MovementSuppressed = false;
                _motor.StepVelocity = Vector3.zero;
            }

            if (_facing != null)
            {
                _facing.IsLocked = false;
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
            float attackPower = _attackerStats != null ? _attackerStats.AttackPower : 0f;

            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                var target = col.GetComponentInParent<IDamageable>();
                if (target == null)
                {
                    continue;
                }

                // 自分自身（同一 Player 階層の PlayerVitalsHolder 等）は除外する。
                if (target is Component targetComponent && targetComponent.transform.root == transform.root)
                {
                    continue;
                }

                // 同一 Swing（段）で同一対象は 1 回だけ。段が変われば新 Token で再命中可。
                if (!_hitTracker.TryRegisterHit(_currentSwing, target))
                {
                    continue;
                }

                // 背後判定（対象の Forward を参照。対象が ICombatActor でなければ補正なし）。
                bool isBackHit = false;
                var targetActor = col.GetComponentInParent<ICombatActor>();
                if (targetActor != null &&
                    CombatGeometry.IsBackHit(targetActor.Forward, transform.position - targetActor.WorldPosition))
                {
                    isBackHit = true;
                }

                // HP は攻撃側寄与（防御適用前）＝攻撃力 × 技倍率 × 0.1 × 背後(×1.1)。防御・スタン倍率は対象側で適用。
                float hpBackMultiplier = isBackHit ? HpDamageCalculator.BackMultiplier : 1f;
                float hpContribution = HpDamageCalculator.AttackContribution(attackPower, d.HpMultiplier, 1f, hpBackMultiplier);

                // 対象が「攻撃の予備/判定中」か（体幹の攻撃中補正対象）を共通契約から取得。未実装ならフォールバック false。
                bool targetActing = false;
                var activity = col.GetComponentInParent<ICombatActivityState>();
                if (activity != null)
                {
                    targetActing = activity.IsPoiseVulnerableAction;
                }

                // 体幹は固定系統。状況補正（背後×1.5・攻撃中×1.5。乗算せず高い方だけ）を攻撃側で適用。
                float poiseSituational = PoiseDamageCalculator.SituationalMultiplier(isBackHit, targetActing);
                float poiseContribution = PoiseDamageCalculator.Compute(d.PoiseDamage, poiseSituational, 1f, 1f);

                // ひるませ値は状況補正なし（背後・攻撃中の補正対象外）。
                float flinchValue = FlinchValueCalculator.Compute(d.FlinchPower, 1f, 1f);

                var damage = new HitDamage(hpContribution, poiseContribution, flinchValue);

                HitInfo hit = HitBuilder.FromSnapshot(snapshot, this, target, Forward, center, damage, _currentSwing);
                target.ReceiveHit(hit);
            }
        }

        private void ApplyStateEffects(bool guarding, bool attacking, bool stepping)
        {
            if (_facing != null)
            {
                // ステップ中も向きを固定（回避方向へ滑るが向きは回さない）。
                _facing.IsLocked = guarding || attacking || stepping;
            }

            if (_motor != null && _movement != null)
            {
                _motor.SpeedMultiplier = GuardMovement.SpeedMultiplier(guarding, _movement.GuardSpeedMultiplier);
            }
        }

        private void OnDrawGizmos()
        {
            // 攻撃 Hitbox の位置・大きさ・有効タイミングを Scene ビューで確認するデバッグ表示。
            if (!_debugDrawHitbox)
            {
                return;
            }

            Vector3 center = transform.position + Forward * _hitboxForwardOffset + Vector3.up * _hitboxHeight;
            bool active = _combo != null && _combo.IsActive && _combo.HitboxActive;

            Gizmos.color = active ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(1f, 0.9f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(center, _hitboxHalfExtents * 2f);
            if (active)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
                Gizmos.DrawCube(center, _hitboxHalfExtents * 2f);
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
