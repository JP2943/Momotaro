using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>
    /// 攻撃データ雛形（仕様書 6.7）。判定・時間・数値・防御関連・予兆種別を保持する。
    /// AI コードへ固有値を直書きせず、本データへ集約する。詳細値は後続 Phase で拡張。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Attack_New", menuName = "Momotaro/Data/Combat/Attack Data", order = 0)]
    public sealed class AttackData : GameDataAsset
    {
        [Header("Timing / Range")]
        [SerializeField] private float _cooldownSeconds;
        [SerializeField] private float _useRange = 1.5f;
        [SerializeField] private float _useAngle = 60f;

        [Header("Phase Timing (Phase2 P2-03B)")]
        [Tooltip("予備動作（Startup）秒。判定開始まで。")]
        [SerializeField] private float _startupSeconds = 0.1f;
        [Tooltip("判定（Active/Hitbox 有効）秒。")]
        [SerializeField] private float _activeSeconds = 0.1f;
        [Tooltip("後隙（Recovery）秒。次段が来なければこの後 Idle へ戻る。")]
        [SerializeField] private float _recoverySeconds = 0.2f;
        [Tooltip("踏み込み距離（Facing 方向・壁は貫通しない）。試作値。")]
        [SerializeField] private float _stepDistance;
        [Tooltip("この段開始からの経過秒がこれ以上で Guard/Step キャンセルを許可する（0=判定終了後）。")]
        [SerializeField] private float _cancelWindowStartSeconds;

        [Header("Numbers")]
        [SerializeField] private float _hpMultiplier = 1f;
        [SerializeField] private float _poiseDamage = 10f;
        [SerializeField] private float _flinchPower;
        [SerializeField] private float _guardStaminaCost = 10f;
        [Tooltip("ジャストガード成立時に攻撃者の体幹へ反射する固定ダメージ。仕様書 3.3（軽15/通常20/強30/ボス大技40）。")]
        [SerializeField] private float _justGuardPoiseDamage = 20f;

        [Header("Defense / Telegraph")]
        [SerializeField] private bool _guardable = true;
        [SerializeField] private bool _justGuardable = true;
        [SerializeField] private bool _stepAvoidable = true;
        [SerializeField] private AttackTelegraph _telegraph = AttackTelegraph.Normal;

        /// <summary>HP技倍率。</summary>
        public float HpMultiplier => _hpMultiplier;

        /// <summary>体幹ダメージ（固定系統。攻撃力・防御の影響を受けない。仕様書 3.11）。</summary>
        public float PoiseDamage => _poiseDamage;

        /// <summary>ひるませ値（仕様書 3.12。状況補正の対象外）。</summary>
        public float FlinchPower => _flinchPower;

        /// <summary>クールダウン（秒）。</summary>
        public float CooldownSeconds => _cooldownSeconds;

        /// <summary>使用距離。</summary>
        public float UseRange => _useRange;

        /// <summary>使用角度（度）。</summary>
        public float UseAngle => _useAngle;

        /// <summary>予備動作（Startup）秒。</summary>
        public float StartupSeconds => _startupSeconds;

        /// <summary>判定（Active）秒。Hitbox 有効時間。</summary>
        public float ActiveSeconds => _activeSeconds;

        /// <summary>後隙（Recovery）秒。</summary>
        public float RecoverySeconds => _recoverySeconds;

        /// <summary>この段の総時間（予備＋判定＋後隙）。</summary>
        public float TotalSeconds => _startupSeconds + _activeSeconds + _recoverySeconds;

        /// <summary>踏み込み距離（Facing 方向）。</summary>
        public float StepDistance => _stepDistance;

        /// <summary>段開始からのキャンセル許可開始秒（Guard/Step）。</summary>
        public float CancelWindowStartSeconds => _cancelWindowStartSeconds;

        /// <summary>ガードで消費するスタミナ量（仕様書 3.2）。</summary>
        public float GuardStaminaCost => _guardStaminaCost;

        /// <summary>ジャストガード成立時に攻撃者の体幹へ反射する固定ダメージ（仕様書 3.3）。</summary>
        public float JustGuardPoiseDamage => _justGuardPoiseDamage;

        /// <summary>予兆種別。</summary>
        public AttackTelegraph Telegraph => _telegraph;

        /// <summary>通常ガード可能か。</summary>
        public bool Guardable => _guardable;

        /// <summary>ジャストガード可能か。</summary>
        public bool JustGuardable => _justGuardable;

        /// <summary>ステップ回避可能か（仕様書 3.4）。</summary>
        public bool StepAvoidable => _stepAvoidable;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_hpMultiplier < 0f)
            {
                report.Error(name + ": HpMultiplier must be >= 0.");
            }

            if (_cooldownSeconds < 0f)
            {
                report.Error(name + ": CooldownSeconds must be >= 0.");
            }

            if (_startupSeconds < 0f || _recoverySeconds < 0f)
            {
                report.Error(name + ": Startup/Recovery seconds must be >= 0.");
            }

            if (_activeSeconds <= 0f)
            {
                report.Error(name + ": ActiveSeconds must be > 0.");
            }

            if (_stepDistance < 0f)
            {
                report.Error(name + ": StepDistance must be >= 0.");
            }

            if (_telegraph == AttackTelegraph.Unblockable && _guardable)
            {
                report.Warning(name + ": Unblockable telegraph but Guardable is true.");
            }
        }
    }
}
