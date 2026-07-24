using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>必殺技のパラメータ雛形（仕様書 3.6 / Phase2 P2-10）。独立ゲージは持たず、安全なチャージ時間の確保が使用条件。</summary>
    [CreateAssetMenu(fileName = "SO_Special_New", menuName = "Momotaro/Data/Combat/Special Attack Data", order = 3)]
    public sealed class SpecialAttackData : GameDataAsset
    {
        [Header("Charge")]
        [Tooltip("最大チャージ秒（ボタン長押し）。仕様書 3.6（2.0）。")]
        [SerializeField] private float _chargeSeconds = 2.0f;
        [Tooltip("最大チャージ後に保持できる秒。超えると自動発動。仕様書 3.6（0.75）。")]
        [SerializeField] private float _maxHoldSeconds = 0.75f;

        [Header("Power")]
        [Tooltip("HP 技倍率（通常攻撃1段目の700%＝7.0）。仕様書 3.6。")]
        [SerializeField] private float _hpMultiplier = 7.0f;
        [Tooltip("防御一部無視率（0..1）。実効防御 = 防御×(1-率)。Data 化・試遊前提。")]
        [Range(0f, 1f)]
        [SerializeField] private float _defenseIgnoreRatio = 0.5f;
        [Tooltip("スタン中の対象への固有 HP 倍率。1.25 とは乗算しない（置き換え）。仕様書 §6（1.5）。")]
        [SerializeField] private float _stunHpMultiplier = 1.5f;
        [Tooltip("体幹ダメージ（固定系統）。")]
        [SerializeField] private float _poiseDamage = 30f;
        [Tooltip("ひるませ値（非常に高い）。P2-10（100）。")]
        [SerializeField] private float _flinchPower = 100f;

        [Header("Timing")]
        [Tooltip("判定（Active）秒。")]
        [SerializeField] private float _activeSeconds = 0.15f;
        [Tooltip("発動後の後隙秒。仕様書 3.6（0.8〜1.0）。")]
        [SerializeField] private float _recoverySeconds = 0.9f;

        /// <summary>最大チャージ秒。</summary>
        public float ChargeSeconds => _chargeSeconds;

        /// <summary>最大チャージ後の保持可能秒。</summary>
        public float MaxHoldSeconds => _maxHoldSeconds;

        /// <summary>HP 技倍率（7.0）。</summary>
        public float HpMultiplier => _hpMultiplier;

        /// <summary>防御一部無視率（0..1）。</summary>
        public float DefenseIgnoreRatio => _defenseIgnoreRatio;

        /// <summary>スタン中の固有 HP 倍率（1.5・非乗算）。</summary>
        public float StunHpMultiplier => _stunHpMultiplier;

        /// <summary>体幹ダメージ。</summary>
        public float PoiseDamage => _poiseDamage;

        /// <summary>ひるませ値（100）。</summary>
        public float FlinchPower => _flinchPower;

        /// <summary>判定（Active）秒。</summary>
        public float ActiveSeconds => _activeSeconds;

        /// <summary>発動後の後隙秒。</summary>
        public float RecoverySeconds => _recoverySeconds;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_chargeSeconds <= 0f)
            {
                report.Error(name + ": ChargeSeconds must be > 0.");
            }

            if (_maxHoldSeconds < 0f)
            {
                report.Error(name + ": MaxHoldSeconds must be >= 0.");
            }

            if (_hpMultiplier < 0f)
            {
                report.Error(name + ": HpMultiplier must be >= 0.");
            }

            if (_defenseIgnoreRatio < 0f || _defenseIgnoreRatio > 1f)
            {
                report.Error(name + ": DefenseIgnoreRatio must be within [0, 1].");
            }

            if (_activeSeconds <= 0f || _recoverySeconds < 0f)
            {
                report.Error(name + ": Active/Recovery seconds invalid.");
            }
        }
    }
}
