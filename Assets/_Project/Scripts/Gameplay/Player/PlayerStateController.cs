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
    public sealed class PlayerStateController : MonoBehaviour, ICombatActor, IGuardState, IJustGuardState, IEvadeState, IJustEvadeState, ISpecialChargeCancel, IAttackThreatSource, IAttackSwingSource, IStepObserver, IInteractActor
    {
        /// <inheritdoc />
        /// <remarks>
        /// 探索の「調べる」を受け付けられる状態か（P4-07A。v1.0 §4.3「新規調査は攻撃・Step・Hurt 等の実行中には受理しない。
        /// 通常の移動／待機から開始する」）。状態機の遷移そのものには触れない。
        /// </remarks>
        public bool CanInteract => _machine.Current == PlayerState.Idle || _machine.Current == PlayerState.Move;

        /// <inheritdoc />
        Vector3 IInteractActor.Position => transform.position;

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

        // P6C：ジャスト回避の受付終端・反撃強化の倍率と有効時間は StepData が正本（Prefab に二つ目の編集可能な値を持たない）。
        // 旧 P3.5-09 の _justEvadeWindowSeconds／_justEvadeCounterPoise（Prefab・Scene には直列化されていなかった）は撤去した。

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
        private JustEvadeCounterState _counter;
        private bool _currentSwingCountered;
        private float _currentSwingCounterMultiplier = 1f;
        private bool _stepChainBuffered;
        private SpecialChargeState _special;
        private float _specialAttackRemaining;
        private float _specialActiveRemaining;
        private HitId _specialSwing;
        private bool _specialRequiresRelease;
        private bool _prevGuardHeld;

        // 現在フレームの経過秒。Update から Tick へ渡された値を、駆動メソッド群が参照する（P4 受入）。
        private float _deltaTime;
        private IPlayerInput _input;
        private HitId _currentSwing;
        private PlayerVitalsHolder _vitals;
        private bool _vitalsResolved;
        private IPlayerHurtReaction _hurtReaction;
        private bool _hurtReactionResolved;
        private bool _wasHurt;
        private bool _wasDefeated;

        /// <summary>
        /// 成長による刀の HP ダメージ倍率（P6B 01。既定 1）。通常各段と必殺の<b>攻撃側寄与</b>に 1 回だけ掛ける。
        /// 仲間・環境・敵体幹・ガード消費には掛けない。整数化は既存どおり対象側。
        /// </summary>
        public float GrowthAttackHpMultiplier { get; private set; } = 1f;

        /// <summary>成長による通常攻撃の体幹倍率（P6B 01。既定 1）。JG 反射・必殺には掛けない。</summary>
        public float GrowthNormalPoiseMultiplier { get; private set; } = 1f;

        /// <summary>
        /// 成長の倍率を置き直す（何度呼んでも同じ結果。基礎値と取得 ID から作った値を渡す）。不正値は 1。
        /// </summary>
        public void SetGrowthMultipliers(float attackHpMultiplier, float normalPoiseMultiplier)
        {
            GrowthAttackHpMultiplier = Valid(attackHpMultiplier) ? attackHpMultiplier : 1f;
            GrowthNormalPoiseMultiplier = Valid(normalPoiseMultiplier) ? normalPoiseMultiplier : 1f;
        }

        private static bool Valid(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f;

        /// <summary>現在の Gameplay 状態（Visual が参照する）。</summary>
        public PlayerState Current => _machine.Current;

        /// <summary>現在の攻撃段（1..3、非攻撃時は直近値）。Visual がクリップ選択に用いる。</summary>
        public int AttackStage { get; private set; } = 1;

        // ---- IAttackSwingSource（近接攻撃 Active 区間の観測。剣閃VFX が参照。P3.5-05。読み取りのみ・挙動不変） ----

        /// <inheritdoc />
        /// <remarks>通常コンボの判定区間に加え、必殺技の判定区間（<see cref="_specialActiveRemaining"/> &gt; 0）も含める。</remarks>
        public bool IsSwingHitboxActive => (_combo != null && _combo.HitboxActive) || _specialActiveRemaining > 0f;

        /// <inheritdoc />
        /// <remarks>必殺技の判定区間中は <see cref="AttackSwing.SpecialStage"/> を返し、通常コンボ段（1..N）と区別する。</remarks>
        public int SwingStage => _specialActiveRemaining > 0f
            ? AttackSwing.SpecialStage
            : (_combo != null ? _combo.Stage : 0);

        /// <inheritdoc />
        /// <remarks>
        /// <see cref="PollHitbox"/> と同一の中心式（前方オフセット＋高さ）。剣閃の表示位置に用いる。必殺技の判定区間中は専用の
        /// 射程（<see cref="Momotaro.Data.Combat.SpecialAttackData"/>）に合わせ、剣閃も前方へ出す（P3.5-09。判定と見た目を一致させる）。
        /// 必殺技は Active 中に中心が前方へ進む（<see cref="SpecialHitboxCenter"/>）。剣閃 VFX はこの値を毎フレーム参照して追従する。
        /// </remarks>
        public Vector3 SwingCenter => _specialActiveRemaining > 0f && _specialData != null
            ? SpecialHitboxCenter()
            : transform.position + Forward * _hitboxForwardOffset + Vector3.up * _hitboxHeight;

        /// <inheritdoc />
        public Vector3 SwingHalfExtents => _hitboxHalfExtents;

        /// <inheritdoc />
        public Vector3 SwingForward => Forward;

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

        /// <summary>ステップ開始からの Gameplay 秒（ステップ中だけ意味を持つ。診断・テスト用。P6C）。</summary>
        public float StepElapsed => _step != null && _step.IsActive ? _step.Elapsed : 0f;

        // ---- IJustEvadeState（ジャスト回避。命中解決が参照。P6C で報酬を「次の通常攻撃一段の HP 強化」へ置換） ----

        /// <inheritdoc />
        public bool CanJustEvade => _step != null && _step.CanJustEvade && !IsDefeated;

        /// <inheritdoc />
        /// <remarks>
        /// 当該ステップの受付を閉じ（1 ステップ 1 回）、反撃強化を付与する（持っていれば残時間を更新するだけ。蓄積しない）。
        /// 体幹反射・強制ひるみ・無敵延長・ステップ性能の変更はしない（P6C 仕様 §2）。死亡確定後は付与しない。
        /// </remarks>
        public void NotifyJustEvadeSuccess()
        {
            EnsureRuntime();
            _step?.NotifyJustEvadeSuccess();
            if (IsDefeated)
            {
                return;
            }

            _counter.Grant();
            JustEvadeSuccessCount++;
        }

        /// <summary>ジャスト回避の成功回数（診断・テスト・表示用）。</summary>
        public int JustEvadeSuccessCount { get; private set; }

        /// <summary>反撃強化の権利を持っているか（未使用・未満了）。</summary>
        public bool HasJustEvadeCounter => _counter != null && _counter.IsCharged;

        /// <summary>反撃強化の残時間（Gameplay 秒。無ければ 0）。</summary>
        public float JustEvadeCounterRemaining => _counter != null ? _counter.Remaining : 0f;

        /// <summary>反撃強化の有効時間（設定値）。</summary>
        public float JustEvadeCounterDuration => _counter != null ? _counter.Seconds : 0f;

        /// <summary>反撃強化の倍率（設定値）。</summary>
        public float JustEvadeCounterMultiplier => _counter != null ? _counter.Multiplier : 1f;

        /// <summary>反撃強化を消費した回数（＝強化された段の数。診断・テスト用）。</summary>
        public int JustEvadeCounterConsumeCount => _counter != null ? _counter.ConsumeCount : 0;

        /// <summary>反撃強化が満了で消えた回数（診断・テスト用）。</summary>
        public int JustEvadeCounterExpireCount => _counter != null ? _counter.ExpireCount : 0;

        /// <summary>現在の通常攻撃段が反撃強化されているか（段の開始で決まり、次段・次の攻撃へ持ち越さない）。</summary>
        public bool IsCurrentSwingCountered => _currentSwingCountered;

        /// <summary>現在の通常攻撃段に掛かっている反撃倍率（強化なしなら 1）。</summary>
        public float CurrentSwingCounterMultiplier => _currentSwingCountered ? _currentSwingCounterMultiplier : 1f;

        /// <summary>強化された段が命中させた対象の数（診断・テスト用。同一段・同一対象は 1）。</summary>
        public int CounterBoostedHitCount { get; private set; }

        /// <summary>反撃強化の権利を消す（死亡・入場・休息・Load。P6C 仕様 §5・§8）。段に移した倍率も解放する。</summary>
        public void ClearJustEvadeCounter()
        {
            _counter?.Clear();
            ReleaseSwingCounter();
        }

        private void ReleaseSwingCounter()
        {
            _currentSwingCountered = false;
            _currentSwingCounterMultiplier = 1f;
        }

        // ---- 必殺技（Phase2 P2-10） ----

        /// <summary>必殺技チャージ中か。</summary>
        public bool IsSpecialCharging => _special != null && _special.IsActive;

        /// <summary>必殺技（発動・後隙）実行中か。</summary>
        public bool IsSpecialAttacking => _specialAttackRemaining > 0f;

        /// <summary>
        /// 必殺技の判定発生中（Active）か（P3.5-09）。この間は攻撃・ステップでキャンセルできない＝必ず出し切る
        /// （判定を持続・前進させる仕様と整合させ、自分の一撃を誤って潰さない）。
        /// </summary>
        private bool IsSpecialActive => _specialActiveRemaining > 0f;

        /// <summary>
        /// 必殺技の後隙中（Active 後・実行中）か（P3.5-09）。爽快感重視で、この間は攻撃・ステップ入力でキャンセルできる。
        /// </summary>
        private bool IsSpecialRecovery => _specialAttackRemaining > 0f && _specialActiveRemaining <= 0f;

        // ---- IAttackThreatSource（敵の防御 AI が観測する「危険の質」。入力ではなく現在の攻撃状態から公開する。P3-11/P3-10） ----
        /// <summary>
        /// 必殺技の「危険な期間」。判定（Active）中に加え、フル充填して発動待ちの終盤（観測可能な予兆）を含める。後隙（Recovery）は
        /// 判定が無く危険でないため除外する（<see cref="IsSpecialAttacking"/> は Recovery も含むため危険判定には使わない）。
        /// </summary>
        private bool IsSpecialDanger => _specialActiveRemaining > 0f || (_special != null && _special.IsActive && _special.IsCharged);

        /// <summary>攻撃中（通常コンボの Attack 状態、または必殺技の危険期間）＝観測可能な危険を出しているか（後隙は含めない）。</summary>
        public bool IsThreateningAttack => (_machine != null && _machine.Current == PlayerState.Attack) || IsSpecialDanger;

        /// <summary>ガード不能な危険か（必殺技＝防御一部無視。通常コンボはガード可能なので false。後隙は含めない）。</summary>
        public bool IsUnblockableThreat => IsSpecialDanger;

        /// <summary>攻撃方向（前方）。</summary>
        public Vector3 ThreatForward => Forward;

        /// <summary>チャージ経過秒（HUD/検証用）。</summary>
        public float SpecialChargeElapsed => _special != null ? _special.Elapsed : 0f;

        /// <summary>チャージが最大到達済みか（HUD/検証用）。</summary>
        public bool IsSpecialCharged => _special != null && _special.IsCharged;

        /// <summary>
        /// エリア遷移を受け付けてよい「移動可能な平常状態」か（P5-03b。仕様書 v1.1 §6.1）。
        ///
        /// 許すのは <see cref="PlayerState.Idle"/> と <see cref="PlayerState.Move"/> だけ。
        /// 攻撃・Guard・Step・Hurt・GuardBreak・必殺の溜め／発動・死亡は<b>すべて拒否</b>する。
        /// 判定を状態機の現在値に寄せてあるので、状態が増えたときに既定で拒否側へ倒れる
        /// （新しい行動状態を足したのに遷移が通ってしまう、という穴を作らない）。
        ///
        /// これは<b>受付条件の一部</b>であって、ここだけで遷移が決まるわけではない。
        /// AreaReady・モード・生存・戦闘は <c>AreaTransitionConditionsSource</c> が別に見る。
        /// </summary>
        public bool IsFreeToTravel
        {
            get
            {
                if (_machine == null)
                {
                    return false;
                }

                PlayerState state = _machine.Current;
                return state == PlayerState.Idle || state == PlayerState.Move;
            }
        }

        /// <inheritdoc />
        public void CancelSpecialChargeOnHit()
        {
            // P6B 03：主人公に有効な被弾が成立した（無敵・回避・ガード・かばうで成立しなかった接触はここへ来ない）。
            // 未確定の使用は中断する。確定は LateUpdate なので、同じフレームの被弾は 1.5 秒到達より優先される。
            if (_usingItem)
            {
                _itemHitDuringUse = true;
            }

            // 被弾（実ダメージ）で必殺技チャージを中断する（発動・後隙中は中断しない）。必殺技ボタンを離すまで再チャージ禁止。
            if (_special != null && _special.IsActive)
            {
                _special.Cancel();
                _specialRequiresRelease = true;
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

        private IPlayerHurtReaction ResolveHurtReaction()
        {
            if (!_hurtReactionResolved)
            {
                _hurtReaction = GetComponentInParent<IPlayerHurtReaction>();
                _hurtReactionResolved = true;
            }

            return _hurtReaction;
        }

        /// <summary>被弾硬直（Hurt）中か。<see cref="PlayerHitReaction"/> が無ければ常に false。</summary>
        private bool IsHurt
        {
            get
            {
                IPlayerHurtReaction r = ResolveHurtReaction();
                return r != null && r.IsHurt;
            }
        }

        /// <summary>死亡（Defeated）確定済みか。Vitals（<see cref="PlayerVitalsHolder"/>）が無ければ常に false。</summary>
        private bool IsDefeated
        {
            get
            {
                PlayerVitalsHolder v = ResolveVitals();
                return v != null && v.IsDefeated;
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
            _wasHurt = false;
            _wasDefeated = false;
        }

        /// <summary>
        /// 被弾（Hurt）開始 Frame で、被弾中断の共通中立化を行う（Phase3.5 P3.5-01。仕様書 §2.3）。通常攻撃 Hitbox・Step 速度/無敵・
        /// Guard/JG 受付・Special Charge/発動判定・入力 Buffer を同一経路で解除する。必殺技はスーパーアーマーを持たず、発動・後隙中でも
        /// 中断する（§3.2）。<see cref="ResetToNeutral"/> と異なり <see cref="_input"/> の破棄や状態機械の Reset は行わない
        /// （硬直終了後に入力状況へ自然復帰させるため）。被弾で必殺技は「要ボタン解除」ロックを立て、押しっぱなしで再チャージしない。
        /// </summary>
        private void NeutralizeForHurt()
        {
            if (_usingItem)
            {
                EndItemUse(interrupted: !_itemCommitted);
            }

            _combo?.Interrupt();
            _hitTracker.Clear();
            ReleaseSwingCounter(); // 攻撃段に移した倍率は攻撃の終了で解放（消費は戻さない）。
            _attackBuffer?.Clear();
            _justGuard?.Reset();
            _prevGuardHeld = false;
            _step?.Reset();
            _stepChainBuffered = false;

            if (_special != null && _special.IsActive)
            {
                _special.Cancel();
            }

            _specialRequiresRelease = true;
            _specialAttackRemaining = 0f;
            _specialActiveRemaining = 0f;

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

        /// <summary>
        /// <b>エリアへ入場するたび</b>の中立化（P5.5 §6.2 手順 5。工程 P55-10b）。
        ///
        /// 被弾中断の共通中立化に加えて<b>状態機械も Idle へ戻す</b>。
        /// 攻撃の進行・先行入力・回避の連鎖・必殺の溜め・向きのロック・移動抑制を捨て、
        /// 直前の Hurt／Defeated の記憶も忘れる。押しっぱなしの必殺ボタンは
        /// <b>ボタンを離すまで再チャージしない</b>（<see cref="NeutralizeForHurt"/> が立てるロック）。
        ///
        /// <b>値には触らない。</b> HP・スタミナ・CD は運ばれてきた Snapshot が正本で、
        /// ここで中立化してから <see cref="Session.AreaActorTransferPort.TryApply"/> が入れる。
        /// 順序が逆だと、中断で生じた CD が復元値を上書きする。
        ///
        /// <b>なぜ入場のたびに要るのか。</b> 旧実装では入場ごとに Scene を作り直していたので
        /// Actor が新品になり、持ち越しは起こらなかった。旧 Area を<b>保持して再利用する</b>と
        /// （§6.2 手順 11）同じ Actor へ戻ってくるので、出て行ったときの
        /// 攻撃モーションや先行入力がそのまま残る。**Scene が作り直されることを当てにしない。**
        ///
        /// <see cref="ResetToNeutral"/> と違って<b>入力の参照は捨てない</b>：
        /// あちらは Disable 用で、入場直後の主人公はそのまま操作を受け取らなければならない。
        /// </summary>
        public void ResetForAreaEntry()
        {
            // P6C（レビュー a24d92c R1）：入場の中立化では反撃強化を<b>消さない</b>。入場の準備は Commit より前に走り
            // （同じ Area の中の旅立ちでは出発と同じ主人公）、その後に失敗して戻ることがあるため。
            // 消すのは Commit（出発側の主人公。遷移役が呼ぶ）・死亡再開と休息（下の ResetForCampaignRespawn）・死亡。
            NeutralizeForHurt();
            _machine.Reset();
            _wasHurt = false;
            _wasDefeated = false;
        }

        /// <summary>
        /// 本編型死亡再開のための中立化（P5-08。仕様書 v1.1 §9.1 手順 6「短時間状態解除」）。
        ///
        /// <b>中身は入場ごとの中立化と同じもの</b>だった（工程 P55-10b で気付いた）——
        /// どちらも「進行中の行動と短時間状態を捨て、状態機械を Idle へ戻す」である。
        /// 違うのは<b>呼び出し側</b>で、再開ではこのあと全回復を適用し、入場では Snapshot を適用する。
        /// 同じ処理を 2 か所に書くと、片方だけ直る。
        /// </summary>
        public void ResetForCampaignRespawn()
        {
            // P6C：死亡再開・休息（成長・払い戻し・旅立ちの到着後の休息を含む）で反撃強化を消す。
            ClearJustEvadeCounter();
            ResetForAreaEntry();
        }

        /// <summary>状態・攻撃・ロック・移動抑制・先行入力を中立へ戻す（Disable 時）。</summary>
        public void ResetToNeutral()
        {
            if (_usingItem)
            {
                EndItemUse(interrupted: !_itemCommitted);
            }

            _guardRequiresRelease = false;
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
            _specialRequiresRelease = false;
            _prevGuardHeld = false;
            _hitTracker.Clear();
            // P6C（レビュー a24d92c R1）：Disable では反撃強化の<b>権利は消さない</b>。遷移の準備で出発側の活動ゲートが
            // 主人公を一時的に非 Active にし、準備の失敗・タイムアウトで Rollback すると同じ主人公へ戻るため。
            // 成功の Commit では遷移役が出発側の権利を消す（AreaActorTransferPort.ClearShortLivedCombatOnCommittedDeparture）。
            // 攻撃段へ移した倍率だけは攻撃の中断とともに解放する。
            ReleaseSwingCounter();

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
                        _stepData.InvincibleStartSeconds, _stepData.InvincibleEndSeconds, _stepData.ChainBufferSeconds,
                        _stepData.JustEvadeWindowSeconds)
                    : new StepState(3f);
            }

            if (_counter == null)
            {
                _counter = _stepData != null
                    ? new JustEvadeCounterState(_stepData.JustEvadeCounterHpMultiplier, _stepData.JustEvadeCounterSeconds)
                    : new JustEvadeCounterState();
            }

            if (_special == null && _specialData != null)
            {
                _special = new SpecialChargeState(_specialData.ChargeSeconds, _specialData.MaxHoldSeconds);
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void LateUpdate()
        {
            // 確定はこのフレームの命中解決がすべて終わってから（同フレームの被弾・死亡を優先する。P6B 仕様 §7）。
            ResolveItemUseCommit();
        }

        // ================================================================ きびだんご使用（P6B 03）

        private bool _usingItem;
        private float _itemElapsed;
        private bool _itemCommitted;
        private bool _itemCommitPending;
        private bool _itemHitDuringUse;
        private bool _guardRequiresRelease;
        private Momotaro.Gameplay.Session.KibidangoUseConfig _itemConfig;

        /// <summary>使用の受け口の差し替え（テスト用。null で <see cref="Session.KibidangoUseProvider"/>）。</summary>
        public Momotaro.Gameplay.Session.IKibidangoUseService ItemUseOverride { get; set; }

        /// <summary>きびだんごを使用中か（2 秒の全動作の間。確定後の 0.5 秒も含む）。</summary>
        public bool IsUsingItem => _usingItem;

        /// <summary>使用開始からの Gameplay 秒（使用中だけ意味を持つ）。</summary>
        public float ItemUseElapsed => _usingItem ? _itemElapsed : 0f;

        /// <summary>今回の使用が確定済みか（回復と残数 -1 が済んだ）。</summary>
        public bool ItemUseCommitted => _usingItem && _itemCommitted;

        /// <summary>今回の使用の全動作秒（HUD 表示用）。</summary>
        public float ItemUseDuration => _usingItem ? _itemConfig.UseSeconds : 0f;

        /// <summary>今回の使用の確定時刻（HUD 表示用）。</summary>
        public float ItemUseCommitTime => _usingItem ? _itemConfig.CommitSeconds : 0f;

        /// <summary>使用を開始した回数（診断・テスト用）。</summary>
        public int ItemUseStartCount { get; private set; }

        /// <summary>確定した回数（診断・テスト用）。</summary>
        public int ItemUseCommitCount { get; private set; }

        /// <summary>確定前に中断した回数（診断・テスト用）。</summary>
        public int ItemUseInterruptCount { get; private set; }

        /// <summary>最後まで終えた回数（診断・テスト用）。</summary>
        public int ItemUseCompleteCount { get; private set; }

        /// <summary>直近の確定で回復した量（診断・テスト用）。</summary>
        public int LastItemHealed { get; private set; }

        /// <summary>直近の開始拒否の理由（診断・テスト・HUD 用）。</summary>
        public ItemUseRejection LastItemUseRejection { get; private set; }

        /// <summary>直近の確定の結果（診断・テスト用）。</summary>
        public Momotaro.Gameplay.Session.KibidangoCommitResult LastItemCommitResult { get; private set; }

        private Momotaro.Gameplay.Session.IKibidangoUseService ItemService =>
            ItemUseOverride ?? Momotaro.Gameplay.Session.KibidangoUseProvider.Current;

        /// <summary>
        /// 押下を 1 回取り出し、開始条件を満たすなら使用を始める。押下は条件不成立でも捨てる（溜めない）。
        /// 開始条件：生存・入力有効・ブレイク外・直前が Idle／Move・同フレームの攻撃／ステップ押下なし・ガード／必殺の保持なし・
        /// HP が最大未満・残数 1 以上。
        /// </summary>
        private bool TryStartItemUse(bool active, bool broken)
        {
            if (!(_input is IItemUseInput use) || !use.ConsumeUseItemPressed())
            {
                return false;
            }

            ItemUseRejection reason = CheckItemUseStart(active, broken, use, out Momotaro.Gameplay.Session.KibidangoUseConfig config);
            LastItemUseRejection = reason;
            if (reason != ItemUseRejection.None)
            {
                return false;
            }

            _usingItem = true;
            _itemElapsed = 0f;
            _itemCommitted = false;
            _itemCommitPending = false;
            _itemHitDuringUse = false;
            _itemConfig = config;
            ItemUseStartCount++;
            return true;
        }

        private ItemUseRejection CheckItemUseStart(bool active, bool broken, IItemUseInput use,
            out Momotaro.Gameplay.Session.KibidangoUseConfig config)
        {
            config = default;
            Momotaro.Gameplay.Session.IKibidangoUseService service = ItemService;
            if (service == null || !service.TryGetConfig(out config))
            {
                return ItemUseRejection.NotAvailable;
            }

            if (!active || broken || IsDefeated || IsHurt)
            {
                return ItemUseRejection.Busy;
            }

            PlayerState current = _machine.Current;
            bool busy = (current != PlayerState.Idle && current != PlayerState.Move)
                || (_step != null && _step.IsActive)
                || (_special != null && _special.IsActive) || _specialAttackRemaining > 0f
                || (_combo != null && _combo.IsActive)
                || (_attackBuffer != null && _attackBuffer.HasBuffered)
                || use.HasPendingActionPress
                || _input.GuardHeld || _input.SpecialAttackHeld;
            if (busy)
            {
                return ItemUseRejection.Busy; // 同フレームの既存行動を優先する。
            }

            PlayerVitalsHolder vitals = ResolveVitals();
            if (vitals == null)
            {
                return ItemUseRejection.NotAvailable;
            }

            if (vitals.CurrentHp >= vitals.MaxHp)
            {
                return ItemUseRejection.HpFull;
            }

            if (service.Remaining < 1)
            {
                return ItemUseRejection.OutOfStock;
            }

            return ItemUseRejection.None;
        }

        /// <summary>
        /// 使用中の時間を進める（Gameplay 時計。入力が閉じている＝Pause・メニュー中は止める）。1.5 秒で確定を予約し
        /// （実行は LateUpdate）、確定済みで全動作に達したら終える。確定前の有効な被弾・ブレイクは中断。
        /// </summary>
        private void TickItemUse(bool active, bool broken)
        {
            if (broken || IsDefeated || (_itemHitDuringUse && !_itemCommitted))
            {
                EndItemUse(interrupted: !_itemCommitted);
                return;
            }

            if (active)
            {
                _itemElapsed += _deltaTime;
            }

            if (!_itemCommitted && !_itemCommitPending && _itemElapsed >= _itemConfig.CommitSeconds)
            {
                _itemCommitPending = true;
            }

            if (_itemCommitted && _itemElapsed >= _itemConfig.UseSeconds)
            {
                EndItemUse(interrupted: false);
            }
        }

        /// <summary>
        /// 予約した確定を実行する（LateUpdate。テストは直接呼べる）。<b>1 回の使用につき 1 回だけ</b>。
        /// 同じフレームに有効な被弾・死亡があれば確定せず中断する（被弾・死亡を優先）。
        /// </summary>
        public void ResolveItemUseCommit()
        {
            if (!_usingItem || !_itemCommitPending)
            {
                return;
            }

            _itemCommitPending = false;
            if (_itemHitDuringUse || IsDefeated || IsHurt)
            {
                EndItemUse(interrupted: true);
                return;
            }

            Momotaro.Gameplay.Session.IKibidangoUseService service = ItemService;
            int healed = 0;
            Momotaro.Gameplay.Session.KibidangoCommitResult result = service != null
                ? service.TryCommit(ResolveVitals(), out healed)
                : Momotaro.Gameplay.Session.KibidangoCommitResult.NotAvailable;
            LastItemCommitResult = result;
            if (result != Momotaro.Gameplay.Session.KibidangoCommitResult.Committed)
            {
                EndItemUse(interrupted: true);
                return;
            }

            LastItemHealed = healed;
            _itemCommitted = true;
            ItemUseCommitCount++;

            // 長いフレームで全動作も同時に越えていたら、ここで終える（確定は 1 回だけ済んでいる）。
            if (_itemElapsed >= _itemConfig.UseSeconds)
            {
                EndItemUse(interrupted: false);
            }
        }

        /// <summary>
        /// 使用中に届いた禁止行動（攻撃・ステップ・追加使用・Interact）の押下を捨てる。溜めて終了後に発火させない。
        /// 押しっぱなしのボタンは押下エッジが立たないので、受け付けるのは離して押し直した新しい入力だけになる。
        /// </summary>
        private void DiscardForbiddenPresses()
        {
            if (_input != null)
            {
                _input.ConsumeAttackPressed();
                _input.ConsumeStepPressed();
                (_input as IItemUseInput)?.ConsumeUseItemPressed();
                (_input as IInteractInput)?.DiscardInteractPressed();
            }

            _attackBuffer?.Clear();
            _stepChainBuffered = false;
        }

        /// <summary>使用中の 1 フレーム：禁止行動の押下を捨て、20% 移動・向き変更だけを許す。</summary>
        private void ApplyItemUseFrame(bool active)
        {
            DiscardForbiddenPresses();
            DriveJustGuard(false);

            bool isMoving = active && _input != null && _input.Move.sqrMagnitude > _moveThreshold * _moveThreshold;
            _machine.Tick(active, isMoving, false, false, false, false, false, false, false, false, usingItem: true);

            if (_facing != null)
            {
                _facing.IsLocked = false; // 向き変更は可。
            }

            if (_motor != null)
            {
                _motor.MovementSuppressed = false;
                _motor.StepVelocity = Vector3.zero;
                _motor.SpeedMultiplier = _itemConfig.MoveSpeedMultiplier; // 歩行だけ。ノックバックは Motor 側で別扱い。
            }
        }

        private void EndItemUse(bool interrupted)
        {
            if (!_usingItem)
            {
                return;
            }

            _usingItem = false;
            _itemCommitPending = false;
            _itemHitDuringUse = false;
            if (interrupted)
            {
                ItemUseInterruptCount++;
            }
            else
            {
                ItemUseCompleteCount++;
            }

            // 使用中に押された禁止行動を終了直後に発火させない：保持中のガード・必殺は一度離すまで受け付けない。
            _guardRequiresRelease = _input != null && _input.GuardHeld;
            _specialRequiresRelease = true;
            _attackBuffer?.Clear();

            if (_motor != null)
            {
                _motor.SpeedMultiplier = 1f;
            }
        }

        /// <summary>
        /// 状態を 1 フレーム進める（P4 受入：時間を外部から注入できるようにした入口）。
        ///
        /// 本クラスだけが <see cref="Time.deltaTime"/> を内部で直接読んでおり、そのため EditMode テストの結果が
        /// Editor の描画間隔に左右されていた（背景で走ると deltaTime が 0 になり、チャージも硬直も一切進まない）。
        /// 敵・仲間・コンボ・ヘイトの各系と同じく<b>時間を注入する</b>形へ揃え、テストを決定的にする。
        /// 実行時の挙動は変わらない（<see cref="Update"/> が <see cref="Time.deltaTime"/> を渡すだけ）。
        /// </summary>
        /// <param name="deltaTime">経過秒。負値は 0 として扱う。</param>
        public void Tick(float deltaTime)
        {
            // 遷移中は Gameplay 時計を進めない。Update からでも直接呼ばれても同じ（§6.2 手順 3、P5-E07）。
            if (Session.GameplayClockProvider.IsFrozen)
            {
                return;
            }

            _deltaTime = deltaTime < 0f ? 0f : deltaTime;
            EnsureRuntime();

            if (_input == null)
            {
                _input = PlayerInputProvider.Current;
            }

            // P6C：反撃強化の時計。Gameplay 時計（遷移凍結は上で return、ヒットストップは deltaTime が縮む、Pause・メニュー中は入力が閉じる）で
            // 減らす。被弾硬直中も減る。死亡では消す（同フレームの成功の後に死亡しても残さない）。
            if (IsDefeated)
            {
                ClearJustEvadeCounter();
            }
            else if (_input != null && _input.Active)
            {
                _counter.Tick(_deltaTime);
            }

            // 死亡（Defeated）：最優先・恒久状態（Defeated > Hurt > ...）。確定 Frame で全行動を中立化し、以後は入力を破棄・
            // 移動を凍結・Facing を保持したまま復帰しない（仕様書 §3.1/§4.1）。Retry は Scene 再読込で初期化する。
            if (IsDefeated)
            {
                if (!_wasDefeated)
                {
                    NeutralizeForHurt(); // 攻撃/Step/Guard/JG/Special/入力 Buffer を同一経路で解除（被弾中断と共通）。
                }

                _wasDefeated = true;

                if (_input != null)
                {
                    _input.ConsumeAttackPressed();
                    _input.ConsumeStepPressed();
                }

                _attackBuffer?.Clear();

                if (_motor != null)
                {
                    _motor.MovementSuppressed = true; // 移動停止（Facing 更新も止める）。
                    _motor.StepVelocity = Vector3.zero;
                    _motor.SpeedMultiplier = 1f;
                }

                if (_facing != null)
                {
                    _facing.IsLocked = true; // 死亡直前の向きを保持（Facing 更新停止）。
                }

                _machine.Tick(false, false, false, false, false, false, false, false, hurt: false, defeated: true);
                return;
            }

            // 被弾硬直（Hurt）：Defeated 未確定時の最優先状態。開始 Frame で全行動を中立化し、以後は
            // 入力を破棄・移動を凍結・向きを保持して硬直終了まで維持する（仕様書 §2.3/§3.1/Table3）。
            if (IsHurt)
            {
                if (!_wasHurt)
                {
                    NeutralizeForHurt();
                }

                _wasHurt = true;

                if (_input != null)
                {
                    _input.ConsumeAttackPressed();
                    _input.ConsumeStepPressed();
                }

                _attackBuffer?.Clear();

                if (_motor != null)
                {
                    _motor.MovementSuppressed = true; // 硬直中は移動不能（踏み込み速度も 0）。
                    _motor.StepVelocity = Vector3.zero;
                    _motor.SpeedMultiplier = 1f;
                }

                if (_facing != null)
                {
                    _facing.IsLocked = true; // 被弾直前の向きを保持（攻撃者方向へ振り向かない）。
                }

                _machine.Tick(false, false, false, false, false, false, false, false, hurt: true);
                return;
            }

            _wasHurt = false;

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

            // P6B 03：きびだんご使用。使用中は他の行動を受け付けず、ここで 1 フレームを終える。
            // 終了したフレームは通常処理へ落ちる（行動ボタンは解放後の新しい入力から）。
            if (_usingItem)
            {
                TickItemUse(active, broken);
                if (_usingItem)
                {
                    ApplyItemUseFrame(active);
                    return;
                }

                // 使用が<b>このフレームで</b>終わった（2 秒到達・中断）。フレーム開始時点では使用中だったので、
                // このフレームに届いた禁止行動の押下も使用中のものとして捨てる（レビュー ddb2d19 R1）。
                // 移動はこのまま通常処理へ（速度は EndItemUse で戻してある）。ガード・必殺は解放待ちのまま。
                DiscardForbiddenPresses();
            }
            else if (TryStartItemUse(active, broken))
            {
                ApplyItemUseFrame(active);
                return;
            }

            // 使用終了時に押されていたガードは、一度離すまで構えない（P6B 仕様 §6「解放後の新しい入力で受け付け」）。
            if (_guardRequiresRelease && (_input == null || !_input.GuardHeld))
            {
                _guardRequiresRelease = false;
            }

            // 先行入力の取り込みと時間経過。遮断中は預かった入力を破棄する。
            if (active)
            {
                if (_input.ConsumeAttackPressed())
                {
                    _attackBuffer.Buffer();
                }

                _attackBuffer.Tick(_deltaTime);
            }
            else
            {
                _attackBuffer.Clear();
            }

            bool guarding = active && _input.GuardHeld && !_guardRequiresRelease;
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

                // P6C：通常攻撃の段が<b>実際に開始した</b>ときに反撃強化を消費し、この段だけへ倍率を移す（入力予約時ではない）。
                // 開始が拒否された入力ではここへ来ないので消費しない。次段・次の攻撃は新しい段として 1 から決め直す。
                _currentSwingCountered = _counter.TryConsume(out _currentSwingCounterMultiplier);
                if (!_currentSwingCountered)
                {
                    _currentSwingCounterMultiplier = 1f;
                }
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

            // P6C：攻撃が終わった（完了・キャンセル・ステップ・必殺で中断）フレームで段の倍率を解放する。消費は戻さない。
            if (_combo == null || !_combo.IsActive || blockOther)
            {
                ReleaseSwingCounter();
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
            _step.Tick(_deltaTime);

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

            // 新規開始（非ブレイク・GameMode 有効・押下）。必殺技の判定発生中（Active）はステップを開始しない＝一撃を出し切る（P3.5-09）。
            // 後隙中は開始でき、TryStartStep が必殺技の後隙を打ち切る。
            if (!broken && stepPressed && !IsSpecialActive && TryStartStep(isMoving))
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

            // ステップ入力で必殺技チャージを完全キャンセル（経過 0 へ）。必殺技ボタンを一度離すまで再チャージ禁止（再開しない）。
            if (_special != null && _special.IsActive)
            {
                _special.Cancel();
                _specialRequiresRelease = true;
            }

            // 必殺技の後隙中ならステップで打ち切る（P3.5-09 爽快感重視）。Active 中は呼び出し側（DriveStep）で弾かれるため、
            // ここに来た時点で残っているのは後隙のみ。打ち切らないと _specialAttackRemaining が凍結しステップ後に後隙が再開してしまう。
            if (IsSpecialRecovery)
            {
                _specialAttackRemaining = 0f;
                _specialActiveRemaining = 0f;
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

            bool held = _input != null && _input.SpecialAttackHeld;

            // キャンセル後の再チャージ禁止：必殺技ボタンが一度解除されるまで新規チャージを開始しない。
            if (!held)
            {
                _specialRequiresRelease = false;
            }

            // 発動・後隙の実行フェーズ。判定発生中（Active）はキャンセル不可＝出し切る。後隙（Active 後）は攻撃でキャンセル可（P3.5-09）。
            if (_specialAttackRemaining > 0f)
            {
                float dt = _deltaTime;
                bool inActiveWindow = _specialActiveRemaining > 0f;

                // 後隙中の攻撃先行入力でキャンセル：必殺技を打ち切り、同フレームで DriveCombo にバッファ攻撃を拾わせる（爽快感重視）。
                // ステップによるキャンセルは DriveStep→TryStartStep 側で処理する（DriveStep が本メソッドより先に走るため）。
                if (!inActiveWindow && _combo != null && _attackBuffer != null && _attackBuffer.HasBuffered)
                {
                    _specialAttackRemaining = 0f;
                    _specialActiveRemaining = 0f;
                    _hitTracker.Clear();
                    return false; // 実行終了：この後 DriveCombo が動けるよう非ブロックに戻す。
                }

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

            // 遮断・行動不能・ガード入力ではチャージを中断（後隙なし）。ガードキャンセルは再チャージ禁止を立てる。
            if (!active || broken || guarding)
            {
                if (_special.IsActive)
                {
                    _special.Cancel();
                    _specialRequiresRelease = true;
                }

                return false;
            }

            if (_special.IsActive)
            {
                _special.Tick(_deltaTime);

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

            // 新規チャージ開始：保持中かつ「要解除」ロックが立っていないときのみ。
            if (held && !_specialRequiresRelease)
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
            // 1 回の長押しにつき 1 回発動：発動後は必殺技ボタンを一度離すまで次のチャージを禁止する
            // （自動発動・手動リリース発動の双方に同じ再入力規則を適用。§3.6）。
            _specialRequiresRelease = true;
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

        /// <summary>
        /// 必殺技の判定中心（P3.5-09：Active 中に前方へ進む「薙ぎ」）。発生時（残り＝ActiveSeconds）は
        /// <see cref="Momotaro.Data.Combat.SpecialAttackData.HitboxForwardOffset"/>、Active 終了時は
        /// ＋<see cref="Momotaro.Data.Combat.SpecialAttackData.HitboxTravelDistance"/> まで前方へ滑らせる。
        /// 当たり判定（<see cref="PollSpecialHitbox"/>）と剣閃 VFX（<see cref="SwingCenter"/>）で同一式を用い、見た目と判定を一致させる。
        /// 呼び出し側で <c>_specialData != null</c> を保証すること。
        /// </summary>
        private Vector3 SpecialHitboxCenter()
        {
            float active = _specialData.ActiveSeconds;
            // Active 経過率 t（発生 0 → 終了 1）。DriveSpecial で dt 減算後に評価されるため、発生直後は概ね 0、終了直前で 1 に近づく。
            float t = active > 0f ? Mathf.Clamp01(1f - _specialActiveRemaining / active) : 1f;
            float offset = _specialData.HitboxForwardOffset + _specialData.HitboxTravelDistance * t;
            return transform.position + Forward * offset + Vector3.up * _specialData.HitboxHeight;
        }

        /// <summary>必殺技の判定（Active 中）。7.0 倍・防御一部無視・スタン固有 1.5・ひるませ 100。ガード/JG 不能（貫通）。</summary>
        private void PollSpecialHitbox()
        {
            if (_specialData == null)
            {
                return;
            }

            // P3.5-09：必殺技は専用の射程（通常攻撃より前方・広範囲）を用い、Active 中は前方へ進む。通常攻撃の Hitbox 値は変えない。
            Vector3 center = SpecialHitboxCenter();
            // Physics.autoSyncTransforms=0 のため、Update 中の問い合わせ前に明示同期する。これが無いと移動中の
            // 敵（動的 Rigidbody）が最後の物理ステップの古い位置で判定され、Hitbox が命中を取りこぼす。
            Physics.SyncTransforms();
            int count = Physics.OverlapBoxNonAlloc(center, _specialData.HitboxHalfExtents, _overlapBuffer, Quaternion.identity, _targetMask, QueryTriggerInteraction.Collide);
            if (count == 0)
            {
                return;
            }

            float attackPower = _attackerStats != null ? _attackerStats.AttackPower : 0f;
            // P6B：成長の刀倍率は攻撃側寄与へ 1 回だけ（attackerMultiplier の位置）。
            float hpContribution = HpDamageCalculator.AttackContribution(attackPower, _specialData.HpMultiplier,
                GrowthAttackHpMultiplier);

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

                // 小型敵ノックバック（拡張点。ボスは受け手側で無効化）。
                col.GetComponentInParent<IKnockbackReceiver>()?.ReceiveKnockback(Forward, _specialData.Knockback);
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

            _justGuard.Tick(_deltaTime);

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

            _combo.Tick(_deltaTime);

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
            // Physics.autoSyncTransforms=0 のため、Update 中の問い合わせ前に明示同期する。これが無いと移動中の
            // 敵（動的 Rigidbody）が最後の物理ステップの古い位置で判定され、Hitbox が命中を取りこぼす。
            Physics.SyncTransforms();
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
                // P6B：成長の刀倍率は攻撃側寄与へ 1 回だけ（attackerMultiplier の位置）。
                // P6C：反撃強化はこの段だけ、成長倍率と乗算して同じ位置へ（防御・スタン・丸めは対象側の既存計算）。体幹・ひるみには掛けない。
                float hpContribution = HpDamageCalculator.AttackContribution(attackPower, d.HpMultiplier,
                    GrowthAttackHpMultiplier * CurrentSwingCounterMultiplier, hpBackMultiplier);
                if (_currentSwingCountered)
                {
                    CounterBoostedHitCount++;
                }

                // 対象が「攻撃の予備/判定中」か（体幹の攻撃中補正対象）を共通契約から取得。未実装ならフォールバック false。
                bool targetActing = false;
                var activity = col.GetComponentInParent<ICombatActivityState>();
                if (activity != null)
                {
                    targetActing = activity.IsPoiseVulnerableAction;
                }

                // 体幹は固定系統。状況補正（背後×1.5・攻撃中×1.5。乗算せず高い方だけ）を攻撃側で適用。
                float poiseSituational = PoiseDamageCalculator.SituationalMultiplier(isBackHit, targetActing);
                // P6B：成長の体幹倍率は通常攻撃だけ（必殺・JG 反射は別経路で掛からない）。
                float poiseContribution = PoiseDamageCalculator.Compute(d.PoiseDamage, poiseSituational,
                    GrowthNormalPoiseMultiplier, 1f);

                // ひるませ値は状況補正なし（背後・攻撃中の補正対象外）。
                float flinchValue = FlinchValueCalculator.Compute(d.FlinchPower, 1f, 1f);

                var damage = new HitDamage(hpContribution, poiseContribution, flinchValue);

                // P3.5-08A：この攻撃段のヒットバック値を載せる（被弾した敵を AttackDirection へ押し出す）。主人公攻撃は
                // 近接のみ・ガードバックなし（敵は Guard 結果を出さない）。値は攻撃開始時に確定した不変 Snapshot を正本とする
                // （ダメージ値と同じ時点の値を使う。§2.2 / §7.4。GPT レビュー対応：原本 AttackData を直接参照しない）。
                HitInfo hit = HitBuilder.FromSnapshot(snapshot, this, target, Forward, center, damage, _currentSwing)
                    .WithReaction(new HitReaction(snapshot.HitbackDistance, snapshot.HitbackSeconds, 0f, isProjectile: false));
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
