using System;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Screen;
using Momotaro.Gameplay.Enemy.Slots;
using Momotaro.Gameplay.Enemy.Threat;
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
    public sealed class EnemyAttackController : MonoBehaviour, ISlotOwner, IEnemyDefeatCleanup, IAttackSwingSource, IEnemySlashVisual, IEnemyUnblockableWarningSource
    {
        [Tooltip("同点時の tie-break 乱数シード（0 で TickCount。EditMode 再現用に固定可）。")]
        [SerializeField] private int _seed;

        [Tooltip("敵剣閃VFXの素材選択に用いる敵タイプ鍵（近接骸骨=Small／侍骸骨=Medium 等。P3.5-05）。")]
        [SerializeField] private string _slashVfxKey = "Small";

        [Tooltip("Hitbox の対象レイヤー（既定は全レイヤー。IDamageable と Faction で絞る）。")]
        [SerializeField] private LayerMask _targetMask = ~0;

        [Tooltip("ガード不能攻撃が全選択に占める割合の上限（§9.3「全選択の20%以下」。選択履歴で明示的に上限管理し、Score 乗算では保証しない）。")]
        [SerializeField, Range(0.05f, 1f)] private float _unblockableMaxRatio = 0.2f;

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
        private bool[] _chaseInitiable; // Charge のみ Chase（間合いの外）から開始できる（間合い詰め攻撃。§9.3）。
        private bool[] _selectMask;     // Evaluate へ渡す可否バッファ（毎回再利用。approach 制限・頻度上限ゲート）。
        private int _unblockableIndex = -1;
        private AttackFrequencyGovernor _freqGov; // ガード不能の ≤20% 上限を選択履歴で固定する（§9.3）。
        private System.Random _rng;
        private bool _built;

        private EnemyEncounter _encounter;
        private bool _encounterResolved;
        private bool _holdsSlot;
        private IEnemyProjectileLauncher _launcher;
        private bool _launcherResolved;
        private IEnemyFireLineProbe _fireLineProbe;

        private int _selectedIndex = -1;
        private int _lastUsedIndex = -1;
        private Vector3 _aimDir = Vector3.forward;
        private HitId _currentSwing;
        private IPerceptionTarget _attackTarget; // 攻撃開始時に確定した照準対象（req2/3）。Tracking はこれを追い、最寄り再取得しない。

        /// <summary>攻撃予兆の配信チャネル（表示側が購読）。</summary>
        public EnemyTelegraphChannel Telegraph { get; } = new EnemyTelegraphChannel();

        /// <summary>攻撃中か（Prepare/Active/Recovery）。Brain はこの間 移動・状態を委譲する。</summary>
        public bool IsAttacking => _machine.IsAttacking;

        /// <summary>現在段階（Debug/テスト用）。</summary>
        public EnemyAttackMachine.Phase Phase => _machine.Current;

        /// <summary>実行中の攻撃分類（非攻撃中は Normal）。表示（分類別攻撃モーション）解決に用いる。</summary>
        public EnemyAttackClass CurrentAttackClass => _machine.IsAttacking ? _machine.Snapshot.AttackClass : EnemyAttackClass.Normal;

        /// <summary>現在の狙い方向（XZ 正規化。Debug/テスト用）。</summary>
        public Vector3 AimDirection => _aimDir;

        // ---- IAttackSwingSource（近接攻撃 Active 区間の観測。敵剣閃VFX が参照。P3.5-05。読み取りのみ・挙動不変） ----

        /// <inheritdoc />
        /// <remarks>近接（Active）判定区間のみ true。Charge／Projectile は <see cref="SwingStage"/> が 0 になり剣閃は出ない。</remarks>
        public bool IsSwingHitboxActive => _machine.IsAttacking && _machine.Current == EnemyAttackMachine.Phase.Active;

        /// <inheritdoc />
        /// <remarks>§7.2 の識別：通常／強／ガード不能を段値へ写像する。突進・投射は 0（剣閃なし）。</remarks>
        public int SwingStage
        {
            get
            {
                if (!_machine.IsAttacking)
                {
                    return 0;
                }

                switch (_machine.Snapshot.AttackClass)
                {
                    case EnemyAttackClass.Normal: return AttackSwing.EnemyMeleeNormal;
                    case EnemyAttackClass.Heavy: return AttackSwing.EnemyMeleeHeavy;
                    case EnemyAttackClass.Unblockable: return AttackSwing.EnemyMeleeUnblockable;
                    case EnemyAttackClass.Projectile: return AttackSwing.EnemyProjectile; // 弓発射 SE 用（P3.5-08C）。剣閃 VFX は非対象（下記 default 相当）。
                    default: return 0; // Charge 等は剣閃・スイング SE を出さない。
                }
            }
        }

        /// <inheritdoc />
        /// <remarks><see cref="PollHitbox"/> と同一の中心式（AimDir オフセット＋高さ）。攻撃中のみ Snapshot を参照する。</remarks>
        public Vector3 SwingCenter => _actor == null
            ? transform.position
            : (_machine.IsAttacking
                ? _actor.WorldPosition + _aimDir * _machine.Snapshot.HitboxForwardOffset + Vector3.up * _machine.Snapshot.HitboxHeight
                : _actor.WorldPosition);

        /// <inheritdoc />
        public Vector3 SwingHalfExtents => _machine.IsAttacking ? _machine.Snapshot.HitboxHalfExtents : Vector3.zero;

        /// <inheritdoc />
        public Vector3 SwingForward => _aimDir;

        /// <inheritdoc />
        /// <remarks>
        /// §7.2 の敵タイプ鍵（Small/Medium 等）。Presentation が剣閃素材テーブルの引き当てに用いる。P3.5-06：鍵は archetype
        /// （<see cref="EnemyArchetypeData.SlashVfxKey"/>）を優先し、未設定（null/空）のときのみ本コンポーネントの直列化値へフォールバックする。
        /// 敵タイプごとの鍵をデータ（archetype）で一元管理するため（例：侍骸骨=Medium）。戦闘挙動には一切影響しない。
        /// </remarks>
        public string SlashVfxKey
        {
            get
            {
                string fromArchetype = _actor != null && _actor.Archetype != null ? _actor.Archetype.SlashVfxKey : null;
                return string.IsNullOrEmpty(fromArchetype) ? _slashVfxKey : fromArchetype;
            }
        }

        // ---- IEnemyUnblockableWarningSource（ガード不能予告の観測。P3.5-05。読み取りのみ・挙動不変） ----

        /// <inheritdoc />
        /// <remarks>ガード不能攻撃の予兆（Prepare）区間中か。Guard／JG 不可のため予告で Step 回避を促す。</remarks>
        public bool IsUnblockableTelegraphing => _machine.IsAttacking
            && _machine.Current == EnemyAttackMachine.Phase.Prepare
            && _machine.Snapshot.AttackClass == EnemyAttackClass.Unblockable;

        /// <inheritdoc />
        public Vector3 WarningPosition => _actor != null ? _actor.WorldPosition : transform.position;

        /// <summary>攻撃中に固定された照準対象の ActorId（無ければ 0。req8 検証用）。攻撃終了まで変わらない。</summary>
        public int AttackTargetId => _attackTarget != null ? _attackTarget.ActorId : 0;

        /// <inheritdoc />
        public int SlotOwnerId => _actor != null ? _actor.DamageableId : 0;

        /// <inheritdoc />
        public bool IsSlotOwnerActive => isActiveAndEnabled && _actor != null && !_actor.IsDown;

        /// <summary>攻撃 Slot を保持中か（Debug/テスト用）。</summary>
        public bool HoldsAttackSlot => _holdsSlot;

        /// <summary>直近に選択した攻撃 index（無選択は -1。Debug 表示用。P3-11）。</summary>
        public int DebugSelectedIndex { get; private set; } = -1;

        /// <summary>直近に選択した攻撃の Score（Debug 表示用。P3-11）。</summary>
        public float DebugSelectedScore { get; private set; }

        /// <summary>Chase（間合いの外）からでも開始できる攻撃（突進）を持つか。Brain が接近中の突進開始可否に用いる（§9.3）。</summary>
        public bool HasApproachAttack
        {
            get
            {
                Build();
                if (_chaseInitiable == null)
                {
                    return false;
                }

                for (int i = 0; i < _chaseInitiable.Length; i++)
                {
                    if (_chaseInitiable[i])
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>射線プローブを差し替える（テストで Fake を注入する。§9.2）。</summary>
        public void SetFireLineProbe(IEnemyFireLineProbe probe) => _fireLineProbe = probe;

        /// <summary>Projectile 生成器を差し替える（テストで Fake を注入する。§9.2）。</summary>
        public void SetProjectileLauncher(IEnemyProjectileLauncher launcher)
        {
            _launcher = launcher;
            _launcherResolved = true;
        }

        private void EnsureLauncher()
        {
            if (_launcherResolved)
            {
                return;
            }

            _launcher = GetComponent<IEnemyProjectileLauncher>();
            _launcherResolved = true;
        }

        private void LaunchProjectile(in EnemyAttackSnapshot snap)
        {
            EnsureLauncher();
            if (_launcher == null)
            {
                return; // Launcher 未装備なら発射しない（Gameplay は継続。Presentation 欠如に準ずる）。
            }

            float attackPower = _actor.Archetype != null ? _actor.Archetype.AttackPower : 0f;
            _launcher.TryLaunch(snap, _actor.WorldPosition, _aimDir, _actor, attackPower, _currentSwing);
        }

        private void Awake()
        {
            _actor = GetComponent<EnemyActor>();
            _motor = GetComponent<EnemyMotor>();
            EnsureEncounter();
            Build();
        }

        /// <summary>Encounter 親を 1 回だけ解決する（親があれば Slot を共有、無ければ制限なし）。ライフサイクルに依存せず遅延解決する。</summary>
        private void EnsureEncounter()
        {
            if (_encounterResolved)
            {
                return;
            }

            _encounter = GetComponentInParent<EnemyEncounter>();
            _encounterResolved = true;
        }

        private void OnDisable()
        {
            CancelAttack(); // Disable でも判定・予兆を解除（Cleanup）。
            ReleaseSlot();  // Scene 離脱・Disable で Slot を必ず解放（§8.1）。
        }

        /// <inheritdoc />
        /// <remarks>撃破（Down 確定）で攻撃を中断し、判定・予兆・Slot を即時に解除する（§9「Down 時に攻撃・Slot を解除」）。</remarks>
        public void OnOwnerDefeated()
        {
            CancelAttack(); // 中断で Slot 解放・予兆消灯まで行う（冪等）。
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
            _chaseInitiable = new bool[n];
            _selectMask = new bool[n];
            _unblockableIndex = -1;
            for (int i = 0; i < n; i++)
            {
                EnemyAttackData d = a.Attack(i);
                _snaps[i] = EnemyAttackSnapshot.From(d);
                _options[i] = new AttackOption(d.UseRange, d.UseAngle, d.BaseScore);
                _cooldownValues[i] = d.CooldownSeconds;
                _chaseInitiable[i] = d.AttackClass == EnemyAttackClass.Charge; // 突進のみ接近中に開始可（§9.3）。
                if (d.AttackClass == EnemyAttackClass.Unblockable && _unblockableIndex < 0)
                {
                    _unblockableIndex = i;
                }
            }

            _freqGov = new AttackFrequencyGovernor(_unblockableMaxRatio);
            _rng = new System.Random(_seed == 0 ? Environment.TickCount : _seed);
            _built = true;
        }

        /// <summary>
        /// 位置指定で攻撃開始を試みる（照準対象を確定しない簡易版。単発テスト・非追尾攻撃向け）。Tracking は追尾対象を持たないため
        /// 開始時方向で保持される。通常は <see cref="TryStartAttack(IPerceptionTarget, Vector3, Vector3)"/> を用いて対象を固定する。
        /// </summary>
        public bool TryStartAttack(Vector3 targetPos, Vector3 targetVelocity)
        {
            return TryStartAttack(null, targetPos, targetVelocity);
        }

        /// <summary>
        /// 攻撃開始を試みる（Brain が停止帯 Hold で呼ぶ）。<paramref name="target"/> に Threat 選択対象を渡すと、その対象を攻撃終了まで
        /// 照準対象として固定し（req2/3/4）、Tracking はこの対象の位置を追う（最寄り再取得しない）。<paramref name="target"/> が null／
        /// 無効なら <paramref name="fallbackTargetPos"/> を用いる。全分類を候補に含む（通常/強/ガード不能/突進）。選択に成功したら Prepare へ入り true。
        /// 攻撃中・候補なし・画面外・Slot 不足は false。
        /// </summary>
        public bool TryStartAttack(IPerceptionTarget target, Vector3 fallbackTargetPos, Vector3 targetVelocity)
        {
            return TryStartAttackInternal(target, fallbackTargetPos, targetVelocity, approachOnly: false);
        }

        /// <summary>
        /// 接近中（Chase）の攻撃開始を試みる（§9.3）。突進など「間合いの外から開始してよい」分類のみを候補にし、通常/強/ガード不能が
        /// 遠距離から始まらないよう制限する。使用射程・角度・Cooldown・Slot は通常どおり判定する。成立したら true。
        /// </summary>
        public bool TryStartApproachAttack(IPerceptionTarget target, Vector3 fallbackTargetPos, Vector3 targetVelocity)
        {
            return TryStartAttackInternal(target, fallbackTargetPos, targetVelocity, approachOnly: true);
        }

        private bool TryStartAttackInternal(IPerceptionTarget target, Vector3 fallbackTargetPos, Vector3 targetVelocity, bool approachOnly)
        {
            Build();
            EnsureEncounter(); // Awake 未実行（動的生成）でも Slot 調停を有効化する。
            if (!_built || _machine.IsAttacking || _snaps.Length == 0)
            {
                return false;
            }

            // 対象喪失後は新規攻撃を開始しない（P3.5-02 受入修正。§4.1「新しい攻撃・追跡を開始しない」）。固定対象が非活動／Down なら不開始。
            // 対象未指定（テスト用の対象なし攻撃）は従来どおり許可する。通常の探索は死亡プレイヤーを認識対象から外すため本経路には来ない。
            if (target != null && (!target.IsActive || (target is IThreatTarget downTarget && downTarget.IsDown)))
            {
                return false;
            }

            Vector3 targetPos = target != null && target.IsActive ? target.Position : fallbackTargetPos;
            Vector3 selfPos = _actor.WorldPosition;
            float distance = VisionCheck.PlanarDistance(selfPos, targetPos);
            float angle = AngleToTarget(selfPos, _actor.Forward, targetPos);

            int index = SelectAttackIndex(distance, angle, approachOnly);
            if (index < 0)
            {
                return false;
            }

            EnemyAttackSnapshot snap = _snaps[index];

            // 画面内制御（§8.2／§9.2）：分類別に開始可否を判定（開始済みは継続。ここは開始時のみ評価）。画面外の遠距離は、
            // 画面端警告を表示できたときだけ開始可能。表示できなければ射撃候補から除外する（P3-08 仮警告）。
            bool onScreen = ScreenBoundsProvider.IsOnScreen(selfPos);
            bool warningAvailable = false;
            if (!onScreen && snap.RequiresOffscreenWarning)
            {
                warningAvailable = OffscreenWarningProvider.TryShowWarning(selfPos, targetPos);
            }

            if (!OffscreenAttackPolicy.CanStart(snap.AttackClass, snap.RequiresOffscreenWarning, onScreen, warningAvailable))
            {
                return false;
            }

            // 射線確認（§9.2）：Projectile は射線に別の敵がいると発射せず、位置調整（Reposition）に回す。
            if (snap.AttackClass == EnemyAttackClass.Projectile)
            {
                if (_fireLineProbe == null)
                {
                    _fireLineProbe = new PhysicsEnemyFireLineProbe();
                }

                if (_fireLineProbe.AllyBlocksLine(selfPos, targetPos, _actor.DamageableId))
                {
                    return false;
                }
            }

            // 攻撃 Slot（§8.1）：AttackPrepare 直前に取得。取得できなければ開始しない（Brain が Reposition する）。
            AttackSlotCoordinator coordinator = _encounter != null ? _encounter.Coordinator : null;
            if (coordinator != null && !coordinator.TryAcquire(this, snap.SlotKind))
            {
                return false;
            }

            _holdsSlot = coordinator != null && snap.SlotKind != AttackSlotKind.None;

            _selectedIndex = index;
            _freqGov?.RecordSelection(index == _unblockableIndex); // ガード不能の ≤20% 上限を選択履歴で管理（§9.3）。
            _attackTarget = target; // 照準対象を固定（攻撃終了まで別対象へ切替えない。req2/3/4）。
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

        /// <summary>
        /// 使用する攻撃 index を決める（-1 で該当なし）。<paramref name="approachOnly"/> のときは Chase 開始可能分類（突進）のみを候補にする。
        /// ガード不能は頻度ガバナが解禁したときだけ候補にし（上限側）、解禁かつ使用可能なら強制選択して出現を保証する（下限側＝0%回避。§9.3）。
        /// それ以外は距離・角度・Cooldown・連続使用減点に基づく通常の Score 選択（<see cref="EnemyAttackSelector"/>）。
        /// </summary>
        private int SelectAttackIndex(float distance, float angle, bool approachOnly)
        {
            using var _perf = EnemyProfilerMarkers.Selection.Auto(); // P3-11：攻撃選択の負荷計測。
            int n = _options.Length;
            bool unblockableDue = _unblockableIndex >= 0 && (_freqGov == null || _freqGov.CappedEligible);
            bool forceUnblockable = false;

            for (int i = 0; i < n; i++)
            {
                bool allowed = !approachOnly || _chaseInitiable[i]; // approach 中は突進系のみ。
                if (i == _unblockableIndex)
                {
                    allowed = allowed && unblockableDue; // 未解禁ガード不能は「唯一の候補」でも許可しない（上限を無条件に破らない）。
                    if (allowed && !approachOnly && IsUsable(i, distance, angle))
                    {
                        forceUnblockable = true; // 解禁済みかつ使用可能：この回はガード不能を確定（≤20% 枠での出現保証）。
                    }
                }

                _selectMask[i] = allowed;
            }

            if (forceUnblockable)
            {
                DebugSelectedIndex = _unblockableIndex;
                DebugSelectedScore = _unblockableIndex >= 0 ? _options[_unblockableIndex].BaseScore : 0f;
                return _unblockableIndex;
            }

            int idx = EnemyAttackSelector.Evaluate(distance, angle, _options, _cooldown, _lastUsedIndex,
                count => _rng.Next(count), out float[] scores, _selectMask);
            DebugSelectedIndex = idx;
            DebugSelectedScore = idx >= 0 && idx < scores.Length ? scores[idx] : 0f;
            return idx;
        }

        /// <summary>候補 i が距離・角度・Cooldown の観点で使用可能か（可否ゲートとは独立の素の使用可否）。</summary>
        private bool IsUsable(int i, float distance, float angle)
        {
            if (i < 0 || i >= _options.Length)
            {
                return false;
            }

            AttackOption o = _options[i];
            bool cool = _cooldown == null || i >= _cooldown.Length || _cooldown[i] <= 0f;
            return cool && distance <= o.UseRange && angle <= o.UseAngle;
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

            // 対象喪失 Cleanup（P3.5-02 受入修正。§4.1）：開始時に固定した攻撃対象（_attackTarget）が非活動／Down になったら、
            // 進行中攻撃を Prepare／Active／Recovery を問わず即座に安全終了する。既存 CancelAttack を再利用し、攻撃中断・Hitbox 無効化・
            // _hitTracker クリア・Telegraph Cancel・Slot 解放・突進停止・_attackTarget 解除を同一経路で行う（別 Cleanup 経路を増やさない）。
            // 対象未指定（テスト用の対象なし攻撃）は _attackTarget==null のため対象外。別対象へ途中で切り替えず、そのまま中断する。
            if (_attackTarget != null && !IsAttackTargetTrackable())
            {
                CancelAttack();
                return;
            }

            EnemyAttackSnapshot snap = _machine.Snapshot;

            // 照準の分離（§6.1／req2/3）：Tracking のみ Prepare 中に角速度制限で漸進旋回し、追尾停止で固定する。追尾先は開始時に
            // 確定した照準対象（_attackTarget）で、最寄り再取得（TryGetNearestHostile）はしない。対象が Down／Disable／離脱で無効化した
            // 場合は追尾を止め、その時点の方向を保持する（別対象へ急旋回せず空振りさせる。攻撃自体は被弾状態で中断されるか通常どおり
            // 終了する。req1/3/4）。CurrentPosition／PredictedPosition は開始時に確定した方向を更新しない。
            if (snap.AimingMode == EnemyAimingMode.Tracking && _machine.IsTrackingActive && IsAttackTargetTrackable())
            {
                Vector3 desired = EnemyAimingResolver.Resolve(EnemyAimingMode.CurrentPosition, _actor.WorldPosition,
                    _attackTarget.Position, Vector3.zero, 0f);
                _aimDir = EnemyAimingResolver.RotateToward(_aimDir, desired, snap.TrackingAngularSpeed * deltaTime);
                _motor?.SetFacing(_aimDir);
            }

            EnemyAttackMachine.TickResult r = _machine.Tick(deltaTime);

            if (r.EnteredActive)
            {
                _actor.RequestState(EnemyState.AttackActive, EnemyStateChangeReason.AttackAdvanced);
                PublishTelegraph(EnemyTelegraphPhase.Fire, snap);
                if (snap.AttackClass == EnemyAttackClass.Projectile)
                {
                    LaunchProjectile(snap); // Projectile は Active 突入で 1 発生成（1 発 1Hit）。
                }
            }

            // 近接系のみ Active 中に OverlapBox で判定する。Projectile は生成した弾が独立に判定するため Hitbox を出さない。
            if (_machine.IsHitboxActive && snap.AttackClass != EnemyAttackClass.Projectile)
            {
                PollHitbox(snap);
            }

            // 突進：Active 中は早期固定した狙い方向へ前進する（§9.3）。壁は Enemy↔Default 衝突で停止し貫通しない。同一対象1Hitは Swing で担保。
            if (snap.AttackClass == EnemyAttackClass.Charge && _machine.IsHitboxActive)
            {
                float chargeSpeed = snap.ChargeSpeed > 0f
                    ? snap.ChargeSpeed
                    : (_actor.Archetype != null ? _actor.Archetype.MoveSpeed * 3f : 3f);
                _motor?.SetCharge(_actor.WorldPosition + _aimDir * 10f, chargeSpeed);
            }

            if (r.EnteredRecovery)
            {
                _actor.RequestState(EnemyState.AttackRecovery, EnemyStateChangeReason.AttackAdvanced);
                _motor?.Stop();
                // 判定停止＝予兆表示も消灯（後隙で「まだ判定中」に見せない）。攻撃全体の終了 End とは分ける。
                PublishTelegraph(EnemyTelegraphPhase.Recovery, snap);
            }

            if (r.Finished)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _cooldown.Length)
                {
                    _cooldown[_selectedIndex] = _cooldownValues[_selectedIndex];
                }

                _lastUsedIndex = _selectedIndex;
                _hitTracker.Clear();
                _attackTarget = null; // 攻撃終了で照準対象の固定を解除（次回再評価。req4）。
                ReleaseSlot(); // 攻撃終了で Slot を解放（§8.1）。
                PublishTelegraph(EnemyTelegraphPhase.End, snap);
                _actor.RequestState(EnemyState.Alert, EnemyStateChangeReason.AttackFinished);
            }
        }

        private void PollHitbox(in EnemyAttackSnapshot snap)
        {
            Vector3 fwd = _aimDir;
            Vector3 center = _actor.WorldPosition + fwd * snap.HitboxForwardOffset + Vector3.up * snap.HitboxHeight;
            Quaternion rot = Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z), Vector3.up);
            // Physics.autoSyncTransforms=0 のため、移動中の対象を確実に判定するよう問い合わせ前に同期する。
            Physics.SyncTransforms();
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
            _attackTarget = null; // 中断で照準対象の固定を解除。
            _motor?.Stop();       // 突進中の中断で前進を止める（§9.3）。
            ReleaseSlot(); // 中断（Stagger/Stunned/Down/Disable/Scene 離脱）で Slot を解放（§8.1）。
            PublishTelegraph(EnemyTelegraphPhase.Cancel, snap);
        }

        /// <summary>
        /// 固定した照準対象を今も追尾してよいか（req1/3）。有効（<see cref="IPerceptionTarget.IsActive"/>）かつ、
        /// <see cref="IThreatTarget"/> であれば Down でないこと。Down／Disable／離脱で無効化した対象は追尾を止め、Tracking は
        /// 現在方向を保持する（別対象へ急旋回しない）。EnemyThreatTable が Down を即時無効化する挙動と照準を整合させる。
        /// </summary>
        private bool IsAttackTargetTrackable()
        {
            if (_attackTarget == null || !_attackTarget.IsActive)
            {
                return false;
            }

            return !(_attackTarget is IThreatTarget threat) || !threat.IsDown;
        }

        /// <summary>保持中の攻撃 Slot を解放する（冪等。二重解放でも数が壊れない）。</summary>
        private void ReleaseSlot()
        {
            if (!_holdsSlot)
            {
                return;
            }

            _holdsSlot = false;
            AttackSlotCoordinator coordinator = _encounter != null ? _encounter.Coordinator : null;
            coordinator?.Release(SlotOwnerId);
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
