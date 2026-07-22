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
    public sealed class PlayerVitalsHolder : MonoBehaviour, IDamageable
    {
        [SerializeField] private PlayerData _data;

        private PlayerVitals _vitals;
        private StaminaState _stamina;
        private IGuardState _guardState;
        private bool _guardStateResolved;

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

        private IGuardState ResolveGuardState()
        {
            if (!_guardStateResolved)
            {
                _guardState = GetComponentInParent<IGuardState>();
                _guardStateResolved = true;
            }

            return _guardState;
        }

        /// <inheritdoc />
        public void ReceiveHit(in HitInfo hit)
        {
            EnsureVitals();
            if (_vitals == null)
            {
                return;
            }

            // 通常ガード解決：ガード中かつ Guardable かつ前方 180°以内なら防御成功。
            IGuardState guard = ResolveGuardState();
            bool isGuarding = guard != null && guard.IsGuarding;
            bool withinArc = guard != null && GuardGeometry.IsWithinGuardArc(guard.GuardForward, hit.AttackDirection);

            if (GuardResolver.Resolve(isGuarding, hit.Guardable, withinArc) == GuardOutcome.Guarded)
            {
                // 防御成功：HP ダメージ 0。固定スタミナダメージのみ消費（残量超過でも 0 で止まり、0 到達でブレイク）。
                _stamina.Consume(hit.GuardStaminaDamage);
                SyncStaminaVital();
                Results.Publish(HitResult.Guard(hit.HitId, hit.Attacker, this, HitDamage.None));
                return;
            }

            float defense = _data != null ? _data.Defense : 0f;

            // 貫通：ブレイク中は被 HP ダメージ倍率（×1.25 等）を掛ける。防御適用 → HP 減算 → 実減少量（Clamp 込み）。
            float breakMultiplier = _stamina != null ? _stamina.BreakHpMultiplier : 1f;
            int appliedHp = DamageApplication.ApplyHpDamage(_vitals.Health, hit.Damage.Hp, defense, breakMultiplier);

            // 実際に適用された HP のみ。体幹・ひるみは本 Task では未適用のため 0。
            var applied = new HitDamage(appliedHp, 0f, 0f);
            Results.Publish(HitResult.Damage(hit.HitId, hit.Attacker, this, applied));
        }
    }
}
