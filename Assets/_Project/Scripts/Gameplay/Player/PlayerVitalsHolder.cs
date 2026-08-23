using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の Runtime Vitals を保持し、共通の被弾契約 <see cref="IDamageable"/> を実装するコンポーネント
    /// （Phase1 P1-10 / Phase2 P2-04 受入修正）。割り当てた PlayerData の最大値から Vitals を生成する。
    ///
    /// 被弾は Dummy と同じ経路：<see cref="HitInfo"/> の攻撃側寄与へ自身（PlayerData）の防御を
    /// <see cref="DamageApplication"/> で適用し、HP を減算して実減少量を型付き <see cref="HitResult"/>
    /// （<see cref="HitResultKind.Damage"/>）として通知する。攻撃者としての同定（ICombatActor）は
    /// <see cref="PlayerStateController"/> が持ち、ここでは重複保持しない。
    ///
    /// P2-06：通常ガードの解決を追加する。被弾側のガード状態は共通契約 <see cref="IGuardState"/> から取得し、
    /// ガード中かつ Guardable かつ前方 180°以内なら防御成功（HP ダメージ 0・固定スタミナ消費）、背後・ガード不能・
    /// 非ガード中は貫通して従来どおり HP へ適用する。
    ///
    /// P2-07：スタミナ回復とガードブレイクを <see cref="StaminaState"/> で扱う。ガードの固定消費でスタミナ 0 に達すると
    /// ブレイク（<see cref="_data"/> の行動不能時間）へ移行し、その間の被 HP ダメージは倍率が掛かる。回復は <see cref="Tick"/>
    /// で進め、ガード中は停止する。表示・照会用に <see cref="PlayerVitals"/> の Stamina Vital を同期する。JG は対象外。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVitalsHolder : MonoBehaviour, IDamageable, IPlayerDefeatState, IPlayerDefeatSource
    {
        [SerializeField] private PlayerData _data;

        private bool _defeated;
        private PlayerVitals _vitals;
        private StaminaState _stamina;
        private IGuardState _guardState;
        private bool _guardStateResolved;
        private IJustGuardState _justGuardState;
        private bool _justGuardStateResolved;
        private IEvadeState _evadeState;
        private bool _evadeStateResolved;
        private ISpecialChargeCancel _specialCancel;
        private bool _specialCancelResolved;
        private IPlayerHurtReaction _hurtReaction;
        private bool _hurtReactionResolved;
        private IReactionMotor _reactionMotor;
        private bool _reactionMotorResolved;

        /// <summary>JG 成立時に近接攻撃者へ付与する強制ひるみ秒（Phase3.5 §7.5：0.30〜0.40 の中央）。</summary>
        private const float ForcedFlinchSeconds = 0.35f;

        /// <summary>生成された Runtime Vitals。data 未設定時は null。</summary>
        public PlayerVitals Vitals
        {
            get
            {
                EnsureVitals();
                return _vitals;
            }
        }

        /// <summary>被弾結果の通知チャネル（Dummy と同系統。HUD 等が購読）。</summary>
        public HitResultChannel Results { get; } = new HitResultChannel();

        /// <summary>プレイヤー死亡（致死確定）の型付き通知チャネル（Phase3.5 P3.5-02。Session/HUD が購読。1 回性）。</summary>
        public PlayerDefeatChannel Defeats { get; } = new PlayerDefeatChannel();

        /// <inheritdoc />
        /// <remarks>致死により死亡が確定したか。一度 true になったら復帰しない（Retry は Scene 再読込で初期化）。</remarks>
        public bool IsDefeated => _defeated;

        /// <inheritdoc />
        public int DamageableId => GetInstanceID();

        /// <summary>ガードブレイク（行動不能）中か。状態優先度で行動をロックするために参照する。</summary>
        public bool IsGuardBroken
        {
            get { EnsureVitals(); return _stamina != null && _stamina.IsBroken; }
        }

        private void Awake()
        {
            EnsureVitals();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// スタミナ回復・ブレイクの時間を進める（テストから直接駆動できるよう分離）。回復はガード中は停止する。
        /// </summary>
        public void Tick(float deltaTime)
        {
            EnsureVitals();
            if (_stamina == null)
            {
                return;
            }

            IGuardState guard = ResolveGuardState();
            bool regenBlocked = guard != null && guard.IsGuarding;
            _stamina.Tick(deltaTime, regenBlocked);
            SyncStaminaVital();
        }

        private void EnsureVitals()
        {
            if (_vitals == null && _data != null)
            {
                _vitals = PlayerVitals.FromData(_data);
            }

            if (_stamina == null && _data != null)
            {
                _stamina = new StaminaState(
                    _data.MaxStamina,
                    _data.StaminaRegenPerSecond,
                    _data.StaminaRegenDelaySeconds,
                    _data.StaminaZeroRegenDelaySeconds,
                    _data.GuardBreakSeconds,
                    _data.GuardBreakRestoreRatio,
                    _data.GuardBreakHpMultiplier);
            }
        }

        private void SyncStaminaVital()
        {
            if (_vitals != null && _stamina != null)
            {
                // 表示・照会用に整数へ丸めて同期（内部の正本は StaminaState の float）。
                _vitals.Stamina.SetCurrent((int)(_stamina.Current + 0.5f));
            }
        }

        /// <summary>
        /// スタミナ消費の共通入口（Phase2 P2-07）。ガード・将来の Step 等がここを通すことで、必ず同じ正本
        /// （<see cref="StaminaState"/>）を操作し、表示用 Vital も同期する。<see cref="PlayerVitals.Stamina"/> を
        /// 直接 <c>Change</c> すると次の Tick で StaminaState 値に上書きされるため、消費は必ず本 API を用いる。
        /// ブレイク中は 0（行動不能）。実際に減った量を返す。
        /// </summary>
        public int ConsumeStamina(float amount)
        {
            EnsureVitals();
            if (_stamina == null)
            {
                return 0;
            }

            int consumed = (int)(_stamina.Consume(amount) + 0.5f);
            SyncStaminaVital();
            return consumed;
        }

        /// <summary>現在スタミナ（表示・照会用 Vital と同期済み）。</summary>
        public int CurrentStamina
        {
            get { EnsureVitals(); return _vitals != null ? _vitals.Stamina.Current : 0; }
        }

        /// <summary>
        /// Wave 間の全回復（Phase3.5 P3.5-07。仕様書 §8.3 の試遊仮仕様）。HP とスタミナを最大へ戻し、GuardBreak（行動不能）を
        /// 解除する。各 Encounter を独立評価するための試遊専用回復であり、本編戦闘の回復仕様ではない。死亡確定（<see cref="IsDefeated"/>）は
        /// 対象外（Retry は Scene 再読込で初期化する）。
        /// </summary>
        public void RestoreForWaveRecovery()
        {
            EnsureVitals();
            if (_vitals != null)
            {
                _vitals.Health.SetCurrent(_vitals.Health.Max);
            }

            ResolveReactionMotor()?.ClearReaction(); // Intermission で進行中の押し出しを残さない（§7.4）。

            // スタミナの正本は StaminaState。Reset で満タン＋ブレイク解除し、表示用 Vital を同期する
            // （Vital を直接書くと次 Tick で StaminaState 値に上書きされるため、必ず正本経由で戻す）。
            _stamina?.Reset();
            SyncStaminaVital();
        }

        /// <summary>
        /// 条件付きスタミナ消費（Phase2 P2-09。ステップ等）。残量が <paramref name="amount"/> 以上でブレイク中でないときだけ消費し
        /// true を返す。不足時は消費せず false（ステップ不発）。ステップ消費はガードブレイクを誘発しない（<c>canTriggerBreak:false</c>）。
        /// </summary>
        public bool TryConsumeStamina(float amount)
        {
            EnsureVitals();
            if (_stamina == null || _stamina.IsBroken || _stamina.Current < amount)
            {
                return false;
            }

            _stamina.Consume(amount, canTriggerBreak: false);
            SyncStaminaVital();
            return true;
        }

        private IGuardState ResolveGuardState()
        {
            if (!_guardStateResolved)
            {
                _guardState = GetComponentInParent<IGuardState>();
                _guardStateResolved = true;
            }

            return _guardState;
        }

        private IJustGuardState ResolveJustGuardState()
        {
            if (!_justGuardStateResolved)
            {
                _justGuardState = GetComponentInParent<IJustGuardState>();
                _justGuardStateResolved = true;
            }

            return _justGuardState;
        }

        private IEvadeState ResolveEvadeState()
        {
            if (!_evadeStateResolved)
            {
                _evadeState = GetComponentInParent<IEvadeState>();
                _evadeStateResolved = true;
            }

            return _evadeState;
        }

        private ISpecialChargeCancel ResolveSpecialCancel()
        {
            if (!_specialCancelResolved)
            {
                _specialCancel = GetComponentInParent<ISpecialChargeCancel>();
                _specialCancelResolved = true;
            }

            return _specialCancel;
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

        private IReactionMotor ResolveReactionMotor()
        {
            if (!_reactionMotorResolved)
            {
                _reactionMotor = GetComponentInParent<IReactionMotor>();
                _reactionMotorResolved = true;
            }

            return _reactionMotor;
        }

        /// <summary>
        /// 通常ヒットバック／ガードバックを Motor へ要求する（Phase3.5 P3.5-08A。§7.4）。方向は攻撃方向（攻撃者→被弾者）を用い、
        /// 距離・時間は命中に載った <see cref="HitReaction"/> を正本とする。距離・時間・方向のいずれかが無効なら無処理（HP・状態は不変）。
        /// </summary>
        private void RequestReactionPush(in HitInfo hit, float distance)
        {
            if (distance <= 0f || hit.Reaction.HitbackSeconds <= 0f)
            {
                return;
            }

            Vector3 dir = hit.AttackDirection;
            if (dir.sqrMagnitude < 1e-6f)
            {
                return;
            }

            ResolveReactionMotor()?.PushReaction(dir, distance, hit.Reaction.HitbackSeconds);
        }

        /// <summary>
        /// ジャストガード成立時に攻撃者の体幹へ固定ダメージを反射する（Phase2 P2-08）。攻撃者が <see cref="IDamageable"/> の場合のみ、
        /// 体幹のみ（HP/ひるみ 0）・再ガード不可の逆方向 Hit を返す。攻撃者が存在しない/受け手でない場合は何もしない。
        /// </summary>
        private void ReflectJustGuardPoise(in HitInfo hit)
        {
            if (hit.JustGuardPoiseDamage <= 0f || !(hit.Attacker is IDamageable attackerDamageable))
            {
                return;
            }

            var reflect = new HitDamage(0f, hit.JustGuardPoiseDamage, 0f);
            var reverse = new HitInfo(
                null, attackerDamageable, -hit.AttackDirection, hit.HitPoint, reflect,
                0f, 0f, guardable: false, justGuardable: false, isJustGuardCounter: true, hit.HitId);
            attackerDamageable.ReceiveHit(reverse);
        }

        /// <inheritdoc />
        public void ReceiveHit(in HitInfo hit)
        {
            EnsureVitals();
            if (_vitals == null)
            {
                return;
            }

            // 死亡後は追加被弾を一切受け付けない（HP・結果・敗北通知を重複発行しない。Hurtbox 無効化に相当。仕様書 §4.1）。
            // 同一フレームの複数 Hit でも、致死を与えた最初の Hit 以降はここで即 return する。
            if (_defeated)
            {
                return;
            }

            // 被弾後無敵（Hurt 由来 I-frame。既定 0.50 秒）は、ステップ無敵より前に評価し、通常 Damage を種別に依らず無効化する
            // （ガード不能・Steppable=false を含む。仕様書 §3.2 / Table3）。将来の明示的 InvincibilityBypass はここへ条件を足す拡張点。
            IPlayerHurtReaction reaction = ResolveHurtReaction();
            if (reaction != null && reaction.IsPostHitInvincible)
            {
                Results.Publish(HitResult.Evade(hit.HitId, hit.Attacker, this));
                return;
            }

            // 無敵（ステップ I-frame 等）は最優先で命中を回避する（仕様書 §2/§10。無敵＞ガード＞JG＞被弾）。
            // ただし Steppable=false の攻撃はステップ無敵を貫通し、回避できない（Phase3 P3-04。§6.3）。
            IEvadeState evade = ResolveEvadeState();
            if (evade != null && evade.IsInvincible && hit.Steppable)
            {
                Results.Publish(HitResult.Evade(hit.HitId, hit.Attacker, this));
                return;
            }

            // 前方 180°判定はガード方向を用いる（通常ガード・JG 共通）。ブレイク中は行動不能でガード・JG 不可。
            IGuardState guard = ResolveGuardState();
            bool withinArc = guard != null && GuardGeometry.IsWithinGuardArc(guard.GuardForward, hit.AttackDirection);

            // ジャストガードは Hit 解決で通常ガードより先に評価する（仕様書 §2）。成立でスタミナ非消費・HP0・体幹反射。
            IJustGuardState jg = ResolveJustGuardState();
            if (!IsGuardBroken && hit.JustGuardable && withinArc && jg != null && jg.CanJustGuard)
            {
                ReflectJustGuardPoise(hit);
                jg.NotifyJustGuardSuccess();
                // P3.5-08A：近接攻撃者へ 0.30〜0.40 秒の強制ひるみを付与する（既存の体幹反射は上で維持。HP/Flinch の水増しはしない。§7.5）。
                // 飛び道具（Projectile）の JG では矢は解決するが遠方の射手本人はひるませない（IsProjectile で判別）。
                if (!hit.Reaction.IsProjectile && hit.Attacker is IForcedFlinchReceiver flinchTarget)
                {
                    flinchTarget.ForceFlinch(ForcedFlinchSeconds);
                }

                // JG は踏み止まり（ガードバック 0。§7.4）のため押し戻しは要求しない。
                // P3.5-08B：接触点・攻撃方向を結果へ載せ、JG VFX を弾いた位置へ表示できるようにする（表示専用。解決には不使用）。
                Results.Publish(HitResult.JustGuard(hit.HitId, hit.Attacker, this, HitDamage.None, hit.HitPoint, hit.AttackDirection));
                return;
            }

            // 通常ガード解決：ガード中かつ Guardable かつ前方 180°以内なら防御成功。
            // ブレイク中（行動不能）は同一フレームの後続 Hit でもガード不可（PlayerStateController 更新前でも安全側）。
            bool isGuarding = !IsGuardBroken && guard != null && guard.IsGuarding;

            if (GuardResolver.Resolve(isGuarding, hit.Guardable, withinArc) == GuardOutcome.Guarded)
            {
                // 防御成功：HP ダメージ 0。固定スタミナダメージのみ消費（残量超過でも 0 で止まり、0 到達でブレイク）。
                ConsumeStamina(hit.GuardStaminaDamage);
                // P3.5-08A：通常ガードは防御者を AttackDirection へ小さく押し戻す（ガード状態・スタミナは維持。Hurt は発生しない。§7.4）。
                RequestReactionPush(hit, hit.Reaction.GuardbackDistance);
                Results.Publish(HitResult.Guard(hit.HitId, hit.Attacker, this, HitDamage.None));
                return;
            }

            float defense = _data != null ? _data.Defense : 0f;

            // 貫通：ブレイク中は被 HP ダメージ倍率（×1.25 等）を掛ける。防御適用 → HP 減算 → 実減少量（Clamp 込み）。
            float breakMultiplier = _stamina != null ? _stamina.BreakHpMultiplier : 1f;
            int appliedHp = DamageApplication.ApplyHpDamage(_vitals.Health, hit.Damage.Hp, defense, breakMultiplier);

            // 通常被弾（実ダメージ）で必殺技チャージを中断する（Phase2 P2-10。仕様書 §3.6）。
            ResolveSpecialCancel()?.CancelSpecialChargeOnHit();

            // 実 HP ダメージが 1 以上入り、かつ致死でない（HP 残 > 0）ときだけ Hurt を起動する（Phase3.5 P3.5-01。§3.1/§3.2）。
            // Guard/JG/有効 Step は上で return 済みのため本経路に来ず、Hurt は発生しない。HP0（致死）は Hurt に入らず
            // Defeated を最優先とする準備境界（Defeated 状態自体は P3.5-02 で追加。本 Task では Hurt を起動しないことで境界を担保）。
            if (appliedHp >= 1 && _vitals.Health.Current > 0)
            {
                // GuardBreak 中の被弾でも Hurt へ遷移する。残存 Break 時間を破棄し、Hurt 終了後に GuardBreak へ戻さない（§3.3）。
                _stamina?.ClearBreak();
                reaction?.BeginHurt();
            }
            else if (_vitals.Health.Current <= 0)
            {
                // 致死（HP0 到達）：Hurt には入らず、同一命中解決内で一度だけ Defeated を確定・通知する（仕様書 §4.1）。
                DefeatOnce();
            }

            // P3.5-08A：被弾（実 Damage）で AttackDirection へヒットバック。致死（Defeated）時は押し出さない（死体が滑らない）。§7.4。
            if (!_defeated)
            {
                RequestReactionPush(hit, hit.Reaction.HitbackDistance);
            }

            // 実際に適用された HP のみ。体幹・ひるみは本 Task では未適用のため 0。致死を与えた Hit 自体は Damage 結果を出す
            // （撃破フィードバック用）。以後の追撃は上の _defeated ガードで結果・通知を出さない。
            var applied = new HitDamage(appliedHp, 0f, 0f);
            Results.Publish(HitResult.Damage(hit.HitId, hit.Attacker, this, applied));
        }

        /// <summary>致死を一度だけ確定し、型付き死亡通知を 1 回発行する（冪等）。接地 Collider は維持し、被弾無効化は ReceiveHit 先頭で担保。</summary>
        private void DefeatOnce()
        {
            if (_defeated)
            {
                return;
            }

            _defeated = true;
            ResolveReactionMotor()?.ClearReaction(); // 死亡確定で進行中の押し出しを打ち切る（死体が滑らない。§7.4）。
            Defeats.Publish(new PlayerDefeatedEvent(DamageableId, transform.position));
        }
    }
}
