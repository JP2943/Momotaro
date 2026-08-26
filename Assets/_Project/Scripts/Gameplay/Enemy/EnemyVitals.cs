using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Vitals;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵・検証ダミー共通の被弾 Runtime（Phase3 P3-01）。<see cref="CombatDummy"/> の被弾数値ロジック（HP・体幹・ひるみ・
    /// スタン）を抽出した正本で、<see cref="IEnemyVitalsConfig"/> から構築する。MonoBehaviour 非依存の純粋状態にして
    /// EditMode で再現でき、CombatDummy と <see cref="EnemyActor"/> の双方が本 Runtime を用いて別戦闘系を作らない。
    /// 結果の <see cref="HitResult"/> 発行と ActionPhase 等の表示連動は、対象参照を持つ所有側（MonoBehaviour）が行う。
    /// </summary>
    public sealed class EnemyVitals
    {
        private readonly Vital _hp;
        private readonly PoiseState _poise;
        private readonly FlinchState _flinch;
        private readonly float _defense;
        private readonly float _targetPoiseMultiplier;

        /// <summary>被弾 1 回で実際に適用された結果と、新規に発生した状態遷移の有無。</summary>
        public readonly struct HitApplication
        {
            /// <summary>実適用ダメージ（HP／体幹／ひるみ）。</summary>
            public HitDamage Applied { get; }
            /// <summary>この命中でスタンが新規発生したか。</summary>
            public bool NewlyStunned { get; }
            /// <summary>この命中でひるみが新規発生したか。</summary>
            public bool NewlyFlinching { get; }
            /// <summary>この命中で撃破（HP0）が新規発生したか。</summary>
            public bool NewlyDefeated { get; }

            public HitApplication(HitDamage applied, bool newlyStunned, bool newlyFlinching, bool newlyDefeated)
            {
                Applied = applied;
                NewlyStunned = newlyStunned;
                NewlyFlinching = newlyFlinching;
                NewlyDefeated = newlyDefeated;
            }
        }

        /// <param name="config">数値の出所（<see cref="EnemyArchetypeData"/> または雛形 <see cref="EnemyData"/>）。null なら最小既定。</param>
        public EnemyVitals(IEnemyVitalsConfig config)
        {
            int maxHp = config != null ? config.MaxHp : 1;
            _hp = new Vital(maxHp <= 0 ? 1 : maxHp);

            float poiseMax = config != null ? config.PoiseMax : 1f;
            if (config != null)
            {
                _poise = new PoiseState(poiseMax, config.PoiseRecoveryDelaySeconds, config.PoiseRecoveryRatioPerSecond,
                    stunSeconds: config.StunSeconds);
            }
            else
            {
                _poise = new PoiseState(poiseMax);
            }

            float resistance = config != null ? config.FlinchResistance : 1f;
            // ひるみ（やられ）持続時間は Data 由来（未設定・0 以下なら 0.8 秒へフォールバック。P3.5 調整）。この間は行動不能。
            float flinchSeconds = config != null && config.FlinchSeconds > 0f ? config.FlinchSeconds : 0.8f;
            _flinch = new FlinchState(resistance, flinchSeconds: flinchSeconds);

            _defense = config != null ? config.Defense : 0f;
            _targetPoiseMultiplier = config != null ? config.PoiseDamageMultiplier : 1f;
        }

        /// <summary>現在 HP。</summary>
        public int CurrentHp => _hp.Current;
        /// <summary>最大 HP。</summary>
        public int MaxHp => _hp.Max;
        /// <summary>撃破済みか（HP0）。</summary>
        public bool IsDefeated => _hp.Current <= 0;
        /// <summary>現在体幹。</summary>
        public float CurrentPoise => _poise.Current;
        /// <summary>最大体幹。</summary>
        public float MaxPoise => _poise.Max;
        /// <summary>スタン中か。</summary>
        public bool IsStunned => _poise.IsStunned;
        /// <summary>ひるみ中か。</summary>
        public bool IsFlinching => _flinch.IsFlinching;
        /// <summary>ひるみ蓄積量。</summary>
        public float FlinchAccumulation => _flinch.Accumulation;

        /// <summary>時間経過（体幹回復・ひるみ）。Game Time で呼ぶ（Pause 中は呼ばない）。</summary>
        public void Tick(float deltaTime)
        {
            _poise.Tick(deltaTime);
            _flinch.Tick(deltaTime);
        }

        /// <summary>
        /// 命中を数値適用する（<see cref="CombatDummy"/> と同一手順）。必殺技の防御一部無視・スタン中 HP 倍率
        /// （上書きは置き換え）・対象被体幹倍率・JG 反射の回復延長を反映し、実適用量と新規発生状態を返す。
        /// </summary>
        public HitApplication Apply(in HitInfo hit)
        {
            return Apply(hit, 1f, 1f);
        }

        /// <summary>
        /// 敵ガードの倍率（<paramref name="hpDamageScale"/>＝HP 90% 軽減で 0.1、<paramref name="poiseDamageScale"/>＝被体幹×1.5）を
        /// 併せて数値適用する（Phase3 P3-10。§9）。倍率は Special 貫通・背後で 1.0（貫通）となるよう呼び出し側が決める。
        /// </summary>
        public HitApplication Apply(in HitInfo hit, float hpDamageScale, float poiseDamageScale)
        {
            bool wasStunned = _poise.IsStunned;
            bool wasFlinching = _flinch.IsFlinching;
            bool wasDefeated = IsDefeated;

            float effectiveDefense = _defense * (1f - Mathf.Clamp01(hit.DefenseIgnoreRatio));
            float stunHpMultiplier = _poise.IsStunned
                ? (hit.StunHpMultiplierOverride > 0f ? hit.StunHpMultiplierOverride : _poise.StunHpMultiplier)
                : 1f;
            float scaledHp = hit.Damage.Hp * Mathf.Max(0f, hpDamageScale);
            int appliedHp = DamageApplication.ApplyHpDamage(_hp, scaledHp, effectiveDefense, stunHpMultiplier);

            float poiseDamage = hit.Damage.Poise * _targetPoiseMultiplier * Mathf.Max(0f, poiseDamageScale);
            float appliedPoise = _poise.ApplyPoiseDamage(poiseDamage, isJustGuard: hit.IsJustGuardCounter);

            float appliedFlinch = _flinch.AddFlinch(hit.Damage.Flinch);

            return new HitApplication(
                new HitDamage(appliedHp, appliedPoise, appliedFlinch),
                !wasStunned && _poise.IsStunned,
                !wasFlinching && _flinch.IsFlinching,
                !wasDefeated && IsDefeated);
        }

        /// <summary>HP を最大へ戻す（検証の再試行用）。</summary>
        public void ResetHp() => _hp.SetCurrent(_hp.Max);

        /// <summary>HP・体幹・ひるみを初期へ戻す（検証の再試行用）。</summary>
        public void ResetState()
        {
            _hp.SetCurrent(_hp.Max);
            _poise.Reset();
            _flinch.Reset();
        }
    }
}
