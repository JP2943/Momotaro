using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using UnityEngine;
using Momotaro.Gameplay.Transfer;

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
    public sealed class PlayerVitalsHolder : MonoBehaviour, IDamageable, IPlayerDefeatState, IPlayerDefeatSource, IGuardianHost,
        IIncomingHitSource, ITransferableRuntime<PlayerVitalsTransferSnapshot>
    {
        [SerializeField] private PlayerData _data;

        private readonly System.Collections.Generic.List<IIncomingHitObserver> _incomingObservers =
            new System.Collections.Generic.List<IIncomingHitObserver>();

        private bool _defeated;
        private PlayerVitals _vitals;
        private StaminaState _stamina;
        private IGuardState _guardState;
        private bool _guardStateResolved;
        private IJustGuardState _justGuardState;
        private bool _justGuardStateResolved;
        private IEvadeState _evadeState;
        private bool _evadeStateResolved;
        private IJustEvadeState _justEvadeState;
        private bool _justEvadeStateResolved;
        private ISpecialChargeCancel _specialCancel;
        private bool _specialCancelResolved;
        private IPlayerHurtReaction _hurtReaction;
        private bool _hurtReactionResolved;
        private IReactionMotor _reactionMotor;
        private bool _reactionMotorResolved;
        private IGuardianResolver _guardianResolver;   // 守護／かばうの判断先（P4-01。未配線なら肩代わりは起きない）。
        private bool _guardianResolverResolved;
        private bool _transferInProgress;              // 守護転送の再入ガード（P4 §8.5。1 回の転送で 1 回だけ成立させる）。
        private HitId _transferHitId;                  // 進行中の転送の命中 id（同じ命中の再送と別命中の再入を区別する。レビュー §2.3）。

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

        /// <summary>
        /// 守護／「かばう」による肩代わり成立の型付き通知チャネル（P4-01）。既存の <see cref="Results"/> へ代用結果を
        /// 流さない代わりに、専用イベントで肩代わりを伝える。Presentation は VFX／SE のみを再生し、HitStop は持たない
        /// （肩代わり時の HitStop は守護者側の通常 Damage 由来の 1 回だけ）。
        /// </summary>
        public GuardianTransferChannel GuardianTransfers { get; } = new GuardianTransferChannel();

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
            // 遷移中は Gameplay 時計を進めない。Update からでも直接呼ばれても同じ（§6.2 手順 3、P5-E07）。
            if (Session.GameplayClockProvider.IsFrozen)
            {
                return;
            }

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

        private IJustEvadeState ResolveJustEvadeState()
        {
            if (!_justEvadeStateResolved)
            {
                _justEvadeState = GetComponentInParent<IJustEvadeState>();
                _justEvadeStateResolved = true;
            }

            return _justEvadeState;
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
            ReflectPoiseCounter(hit, hit.JustGuardPoiseDamage);
        }

        /// <summary>
        /// 攻撃者の体幹（Poise）へ固定ダメージを反射する共通処理（JG／ジャスト回避で共用。P3.5-09）。攻撃者が <see cref="IDamageable"/>
        /// の場合のみ、体幹のみ（HP/ひるみ 0）・再ガード不可・カウンター扱いの逆方向 Hit を返す。量が 0 以下／攻撃者が受け手でなければ無処理。
        /// ジャスト回避はガード不能攻撃にも成立するため、反射量は命中側の <c>JustGuardPoiseDamage</c> ではなく回避側の設定値を渡す。
        /// </summary>
        private void ReflectPoiseCounter(in HitInfo hit, float poise)
        {
            if (poise <= 0f || !(hit.Attacker is IDamageable attackerDamageable))
            {
                return;
            }

            var reflect = new HitDamage(0f, poise, 0f);
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

            // 守護転送の解決中に、いま転送しているのと同じ命中が主人公へ戻ってきた場合は、外側の処理に統合して無視する。
            // 守護者の被弾処理は状態遷移・結果通知を伴うため、その途中で主人公の被弾入口へ再入し得る。
            // ここで通してしまうと、外側が成立していれば主人公が二重に解決され、外側が拒否で戻る場合は
            // 再入分と外側分で通常 Damage が 2 回入る。どちらも同じ 1 発の命中なので 1 回にまとめる（レビュー §2.3）。
            //
            // 判定は HitId の値比較。本プロジェクトの HitId に「無効値」の概念は無く（default は (0,0) という
            // 正当な組）、既存契約どおり値が等しいものを同じ命中として扱う。別 HitId の再入はここを素通りし、
            // 主人公の通常の無敵・防御・Damage 解決に従う（守護だけを TryTransferToGuardian で拒否する）。
            if (_transferInProgress && hit.HitId == _transferHitId)
            {
                return;
            }

            // ---- ここから「この命中を自分のものとして解決する」ことが確定した ----

            // 実命中が届いたことを、解決より先に同期で知らせる（R3-02。c8c0ddf §5）。
            // 探索中の仲間はここで解放される。無敵・Step・JG・ガードで結果が Damage にならない命中でも、
            // 一撃が届いたこと自体は変わらないので、解放は同じように起きる。
            // 以前は守護評価（TryTransferToGuardian）の中で解放していたため、主人公が防御に成功すると
            // そこへ到達せず、表示代理の探索は所有権を持たないので解放されなかった。
            NotifyIncomingHit(hit);

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
                // ジャスト回避（P3.5-09）：ステップ開始直後のタイト窓（CanJustEvade）で無敵回避したときは、JG と対称の報酬を与える。
                // 攻撃者の体幹へ固定反射＋近接攻撃者へ強制ひるみ（反撃猶予）を付与し、専用フィードバック（JustEvade）を発行する。
                // ガード不能は Guardable/JustGuardable=false・Steppable=true のため、この経路が「回避が正解」の報酬窓になる。
                // 窓外（無敵だが窓を過ぎた）の回避は従来どおりダメージ 0 のみの通常回避（Evade）。
                IJustEvadeState je = ResolveJustEvadeState();
                if (je != null && je.CanJustEvade)
                {
                    ReflectPoiseCounter(hit, je.JustEvadeCounterPoise);
                    je.NotifyJustEvadeSuccess();
                    if (!hit.Reaction.IsProjectile && hit.Attacker is IForcedFlinchReceiver flinchTarget)
                    {
                        flinchTarget.ForceFlinch(ForcedFlinchSeconds);
                    }

                    Results.Publish(HitResult.JustEvade(hit.HitId, hit.Attacker, this, HitDamage.None, hit.HitPoint, hit.AttackDirection));
                    return;
                }

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

            // 守護／「かばう」（P4-01）：回避・JG・ガードのいずれも成立しなかった攻撃だけを肩代わり対象とする。
            // 守護者が未配線・不在・引き受け不可なら false が返り、以降の通常 Damage へそのままフォールバックする
            // （＝守護を持たない現行 Scene の挙動は一切変わらない）。
            if (TryTransferToGuardian(hit))
            {
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

        /// <summary>
        /// 守護／「かばう」による肩代わりを試みる（P4-01）。成立時は主人公の HP・Hurt・ヒットバック・<see cref="Results"/> を
        /// 一切変化させず、守護者向けへ再構築した命中（<see cref="GuardianHitTransfer"/>）を 1 回だけ渡し、
        /// <see cref="GuardianTransfers"/> へ専用通知を 1 回発行する。守護者側の被害は通常の Damage 経路で解決されるため、
        /// 肩代わり時のヒットストップは守護者側の 1 回だけになる。
        ///
        /// 守護者が未配線・不在・引き受け不可（Down／退場／無効）なら false を返し、呼び出し元は通常 Damage へフォールバックする。
        /// 同一命中が判定から守護者へ直接も届く場合の二重被弾は、守護者側の被弾入口が持つ <see cref="ReceivedHitTracker"/> が
        /// <see cref="HitId"/> 単位で弾く（攻撃側 <see cref="MultiHitTracker"/> は転送を経由しないため相乗りしない）。
        /// </summary>
        private bool TryTransferToGuardian(in HitInfo hit)
        {
            // 取引ガード（§8.5）：転送の解決中に主人公へ<b>別の</b>命中が届いても、それを肩代わりへ回さない。
            // 同じ命中の再送は ReceiveHit の入口で外側へ統合済みなので、ここへ来るのは別 HitId だけ。
            // その別命中は守護を経ずに主人公の通常の解決（無敵・防御・Damage）へ進む（レビュー §2.3）。
            if (_transferInProgress)
            {
                return false;
            }

            IGuardianResolver resolver = ResolveGuardianResolver();
            if (resolver == null)
            {
                return false;
            }

            if (!resolver.TryResolveGuardian(hit, out IGuardianReceiver guardian) || !CanGuardianTakeOver(guardian))
            {
                return false;
            }

            HitInfo transferred = GuardianHitTransfer.Rebuild(hit, guardian);

            _transferInProgress = true;
            _transferHitId = hit.HitId; // 転送前の元 HitId。転送後も id は保たれるが、比較の基準は主人公が受けた命中に置く。
            try
            {
                // 引き受け手が実際に受理したときだけ肩代わりが成立する。
                // 受け口は CanTakeOver を通っても命中を捨てることがある（最も起きやすいのは、同じ 1 振りが
                // 主人公と仲間の両方に重なり、仲間が判定から直接受けた後に同一 HitId の転送が届く場合）。
                // これを成立扱いにすると主人公は自分に当たった命中を無傷でやり過ごし、守護の CD だけが減る。
                if (!guardian.TryReceiveTransferredHit(transferred))
                {
                    return false; // 通常 Damage へフォールバックする。
                }

                resolver.NotifyTransferred(transferred, guardian);
                GuardianTransfers.Publish(new GuardianTransferEvent(
                    hit.HitId, hit.Attacker, this, guardian, transferred.HitPoint));
                return true;
            }
            finally
            {
                // 例外・早期 return のどちらでも必ず解く。解き忘れると以後の肩代わりが無言で止まる。
                _transferInProgress = false;
                _transferHitId = default;
            }
        }

        /// <summary>守護者が肩代わりを引き受けられるか（null・破棄済み・引き受け不可を弾く）。</summary>
        private static bool CanGuardianTakeOver(IGuardianReceiver guardian)
        {
            if (guardian == null)
            {
                return false;
            }

            // interface 経由の参照は Unity の null 演算子が効かないため、破棄済み Object を明示的に弾く。
            if (guardian is UnityEngine.Object unityObject && unityObject == null)
            {
                return false;
            }

            return guardian.CanTakeOver;
        }

        /// <summary>守護判断先を 1 回だけ解決する（動的生成でも遅延解決。未配線なら null のまま）。</summary>
        private IGuardianResolver ResolveGuardianResolver()
        {
            if (!_guardianResolverResolved)
            {
                _guardianResolver = GetComponent<IGuardianResolver>();
                _guardianResolverResolved = true;
            }

            return _guardianResolver;
        }

        /// <summary>守護判断先を明示注入する（Scene 構築・テスト。null 指定で解除し、次回の遅延解決も行わない）。</summary>
        public void SetGuardianResolver(IGuardianResolver resolver)
        {
            _guardianResolver = resolver;
            _guardianResolverResolved = true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 登録者が一致するときだけ外す。仲間が複数居ると、後から有効化された守護者に登録が差し替わっている。
        /// そこで先に退場した仲間が無条件に解除すると、生き残っている守護者の登録まで消えて<b>誰も庇わなくなる</b>。
        /// 一致しない解除は「もう自分の登録ではない」として黙って無視するのが正しい（エラーではない）。
        /// </remarks>
        public void ClearGuardianResolver(IGuardianResolver expected)
        {
            if (expected == null || !ReferenceEquals(_guardianResolver, expected))
            {
                return;
            }

            _guardianResolver = null;
            _guardianResolverResolved = true;
        }

        /// <inheritdoc />
        public void AddIncomingHitObserver(IIncomingHitObserver observer)
        {
            if (observer != null && !_incomingObservers.Contains(observer))
            {
                _incomingObservers.Add(observer);
            }
        }

        /// <inheritdoc />
        public void RemoveIncomingHitObserver(IIncomingHitObserver observer)
        {
            if (observer != null)
            {
                _incomingObservers.Remove(observer);
            }
        }

        /// <summary>観測者数（診断・テスト用）。</summary>
        public int IncomingHitObserverCount => _incomingObservers.Count;

        /// <summary>実命中の入口を観測者へ配る（解決の前。発火中の購読増減に備え写しを回す）。</summary>
        private void NotifyIncomingHit(in HitInfo hit)
        {
            if (_incomingObservers.Count == 0)
            {
                return;
            }

            IIncomingHitObserver[] snapshot = _incomingObservers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] is Object destroyed && destroyed == null)
                {
                    continue; // 破棄済み（interface 越しなので明示的に弾く）。
                }

                snapshot[i].OnIncomingHit(hit);
            }
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

        /// <inheritdoc />
        public PlayerVitalsTransferSnapshot ExportTransferSnapshot()
        {
            EnsureVitals();
            VitalTransferSnapshot health = _vitals != null
                ? _vitals.Health.ExportTransferSnapshot()
                : new VitalTransferSnapshot(0);
            StaminaTransferSnapshot stamina = _stamina != null
                ? _stamina.ExportTransferSnapshot()
                : new StaminaTransferSnapshot(0f, 0f, 0f);
            return new PlayerVitalsTransferSnapshot(health, stamina);
        }

        /// <summary>
        /// HP とスタミナを復元する（§4.5）。Data から構築済みの最大値・設定秒はそのまま使い、
        /// Snapshot には現在値だけを載せる。どちらか一方でも値域が不正なら<b>両方とも適用しない</b>。
        /// </summary>
        public bool TryImportTransferSnapshot(in PlayerVitalsTransferSnapshot snapshot)
        {
            EnsureVitals();
            if (_vitals == null || _stamina == null)
            {
                return false;
            }

            // 先に両方を検査する。Vital 側は Import が検証込みなので、ここでは HP の値域だけ先読みする。
            if (!TransferValue.IsValidHp(snapshot.Health.Current, _vitals.Health.Max))
            {
                return false;
            }

            // スタミナは Import 自身が Break・値域を検証する。失敗するなら HP へ触れる前に返す。
            StaminaState probe = _stamina;
            StaminaTransferSnapshot before = probe.ExportTransferSnapshot();
            if (!probe.TryImportTransferSnapshot(snapshot.Stamina))
            {
                return false;
            }

            if (!_vitals.Health.TryImportTransferSnapshot(snapshot.Health))
            {
                probe.TryImportTransferSnapshot(before); // 部分適用を残さない。
                return false;
            }

            _defeated = _vitals.Health.Current <= 0;
            return true;
        }
    }
}
