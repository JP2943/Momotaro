using Momotaro.Data.Characters;
using Momotaro.Gameplay.Vitals;
using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 検証用の被弾ダミー（Phase2 P2-04/P2-05。仕様書 §13）。AI を持たず、共通の受け手契約 <see cref="IDamageable"/> /
    /// <see cref="ICombatActor"/> を実装する（Dummy 専用の Combat 経路は作らず、Phase 3 の敵 AI へそのまま発展できる）。
    ///
    /// P2-04：命中の攻撃側寄与（HP）へ自身の防御を適用し HP を減算。P2-05：体幹（<see cref="PoiseState"/>）・ひるみ
    /// （<see cref="FlinchState"/>）・スタンを追加。スタン中は被 HP ダメージ ×1.25。結果は型付き <see cref="HitResult"/>
    /// で通知（AppliedDamage は実際に適用された HP／体幹／ひるみ量）。死亡処理・敵 AI・攻撃は対象外。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDummy : MonoBehaviour, ICombatActor, IDamageable, ICombatActivityState
    {
        [Tooltip("HP・防御・体幹・ひるみ耐性などの基礎データ（EnemyData）。標準ダミーは HP100/防御20/体幹100/耐性60。")]
        [SerializeField] private EnemyData _data;

        [Tooltip("検証用の攻撃行動フェーズ。Startup/Active のとき体幹の攻撃中補正(×1.5)の対象になる。既定 None。")]
        [SerializeField] private CombatActionPhase _debugActionPhase = CombatActionPhase.None;

        private Vital _hp;
        private PoiseState _poise;
        private FlinchState _flinch;

        /// <summary>被弾結果の通知チャネル（HUD 等が購読）。</summary>
        public HitResultChannel Results { get; } = new HitResultChannel();

        /// <summary>現在 HP。</summary>
        public int CurrentHp
        {
            get { EnsureRuntime(); return _hp.Current; }
        }

        /// <summary>最大 HP。</summary>
        public int MaxHp
        {
            get { EnsureRuntime(); return _hp.Max; }
        }

        /// <summary>撃破済みか（HP0。死亡処理そのものは対象外）。</summary>
        public bool IsDefeated
        {
            get { EnsureRuntime(); return _hp.Current <= 0; }
        }

        /// <summary>現在体幹。</summary>
        public float CurrentPoise
        {
            get { EnsureRuntime(); return _poise.Current; }
        }

        /// <summary>最大体幹。</summary>
        public float MaxPoise
        {
            get { EnsureRuntime(); return _poise.Max; }
        }

        /// <summary>スタン中か。</summary>
        public bool IsStunned
        {
            get { EnsureRuntime(); return _poise.IsStunned; }
        }

        /// <summary>ひるみ中か。</summary>
        public bool IsFlinching
        {
            get { EnsureRuntime(); return _flinch.IsFlinching; }
        }

        /// <summary>現在のひるみ蓄積量。</summary>
        public float FlinchAccumulation
        {
            get { EnsureRuntime(); return _flinch.Accumulation; }
        }

        /// <inheritdoc />
        public int DamageableId => GetInstanceID();

        /// <inheritdoc />
        public CombatFaction Faction => CombatFaction.Enemy;

        /// <inheritdoc />
        public int FloorId => 0;

        /// <inheritdoc />
        public Vector3 WorldPosition => transform.position;

        /// <inheritdoc />
        public Vector3 Forward => transform.forward;

        /// <summary>検証用の攻撃行動フェーズ（切替可能）。</summary>
        public CombatActionPhase ActionPhase
        {
            get => _debugActionPhase;
            set => _debugActionPhase = value;
        }

        /// <summary>検証用の攻撃行動フェーズを設定する。</summary>
        public void SetActionPhase(CombatActionPhase phase)
        {
            _debugActionPhase = phase;
        }

        /// <inheritdoc />
        /// <remarks>
        /// スタン・ひるみ・撃破中は攻撃が中断されるため、たとえ <see cref="_debugActionPhase"/> が Startup/Active でも
        /// 攻撃中補正の対象にしない（状態競合の抑止）。通常状態の Startup/Active のみ true。
        /// </remarks>
        public bool IsPoiseVulnerableAction =>
            !IsStunned && !IsFlinching && !IsDefeated && _debugActionPhase.IsPoiseVulnerable();

        private void Awake()
        {
            EnsureRuntime();
            // 敵は Enemy レイヤーへ。Player は敵をすり抜け、壁（Default）では停止する（仕様書 §3.4 / P2-09）。
            CombatLayers.ConfigureEnemy(gameObject);
        }

        private void EnsureRuntime()
        {
            if (_hp == null)
            {
                int maxHp = _data != null ? _data.MaxHp : 1;
                _hp = new Vital(maxHp);
            }

            if (_poise == null)
            {
                float poiseMax = _data != null ? _data.PoiseMax : 1f;
                if (_data != null)
                {
                    _poise = new PoiseState(poiseMax, _data.PoiseRecoveryDelaySeconds, _data.PoiseRecoveryRatioPerSecond,
                        stunSeconds: _data.StunSeconds);
                }
                else
                {
                    _poise = new PoiseState(poiseMax);
                }
            }

            if (_flinch == null)
            {
                float resistance = _data != null ? _data.FlinchResistance : 1f;
                _flinch = new FlinchState(resistance);
            }
        }

        /// <summary>HP を最大まで戻す（検証の再試行用）。</summary>
        public void ResetHp()
        {
            EnsureRuntime();
            _hp.SetCurrent(_hp.Max);
        }

        /// <summary>HP・体幹・ひるみを最大/初期へ戻す（検証の再試行用）。</summary>
        public void ResetState()
        {
            EnsureRuntime();
            _hp.SetCurrent(_hp.Max);
            _poise.Reset();
            _flinch.Reset();
            _debugActionPhase = CombatActionPhase.None;
        }

        private void Update()
        {
            EnsureRuntime();
            _poise.Tick(Time.deltaTime);
            _flinch.Tick(Time.deltaTime);
        }

        /// <inheritdoc />
        public void ReceiveHit(in HitInfo hit)
        {
            EnsureRuntime();

            float defense = _data != null ? _data.Defense : 0f;
            float targetPoiseMult = _data != null ? _data.PoiseDamageMultiplier : 1f;

            bool wasStunned = _poise.IsStunned;
            bool wasFlinching = _flinch.IsFlinching;

            // HP：必殺技は防御一部無視（実効防御）＋固有スタン倍率の上書き（1.25 と乗算しない。Phase2 P2-10）。
            float effectiveDefense = defense * (1f - Mathf.Clamp01(hit.DefenseIgnoreRatio));
            float stunHpMultiplier = hit.StunHpMultiplierOverride > 0f ? hit.StunHpMultiplierOverride : _poise.StunHpMultiplier;
            int appliedHp = DamageApplication.ApplyHpDamage(_hp, hit.Damage.Hp, effectiveDefense, stunHpMultiplier);

            // 体幹：命中の Poise（攻撃側で状況補正済み）× 対象の被体幹倍率。実減少量を求める。
            // JG 反射（IsJustGuardCounter）は回復開始待機を延長（通常 3 秒→JG 4 秒。仕様書 §3.11）。
            float poiseDamage = hit.Damage.Poise * targetPoiseMult;
            float appliedPoise = _poise.ApplyPoiseDamage(poiseDamage, isJustGuard: hit.IsJustGuardCounter);

            // ひるみ：状況補正なしの値を蓄積。実際に蓄積へ加わった量。
            float appliedFlinch = _flinch.AddFlinch(hit.Damage.Flinch);

            // スタン／ひるみが新規発生したら攻撃行動は中断される。検証表示と内部状態を合わせるため
            // ActionPhase を None へ戻す（補正判定は上の IsPoiseVulnerableAction でも別途 false になる）。
            if ((!wasStunned && _poise.IsStunned) || (!wasFlinching && _flinch.IsFlinching))
            {
                _debugActionPhase = CombatActionPhase.None;
            }

            var applied = new HitDamage(appliedHp, appliedPoise, appliedFlinch);
            Results.Publish(HitResult.Damage(hit.HitId, hit.Attacker, this, applied));
        }
    }
}
