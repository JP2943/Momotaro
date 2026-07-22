using UnityEngine;

namespace Momotaro.Data.Characters
{
    /// <summary>敵の基礎データ雛形（仕様書 6 章）。体幹値やボスフラグを持つ。</summary>
    [CreateAssetMenu(fileName = "SO_Enemy_New", menuName = "Momotaro/Data/Character/Enemy Data", order = 2)]
    public sealed class EnemyData : CharacterData
    {
        [Header("Enemy")]
        [SerializeField] private float _poiseMax = 100f;
        [SerializeField] private bool _isBoss;

        [Header("Poise / Flinch / Stun (Phase2 P2-05)")]
        [Tooltip("体幹の回復開始までの秒（最後の体幹ダメージから）。仕様書 3.11.2。")]
        [SerializeField] private float _poiseRecoveryDelaySeconds = 3f;
        [Tooltip("体幹の毎秒回復量（最大体幹に対する割合。0.08 = 8%/s）。")]
        [SerializeField] private float _poiseRecoveryRatioPerSecond = 0.08f;
        [Tooltip("被体幹ダメージ倍率（対象側。1.0=等倍）。")]
        [SerializeField] private float _poiseDamageMultiplier = 1f;
        [Tooltip("スタン時間（秒）。仕様書 3.11.2。")]
        [SerializeField] private float _stunSeconds = 3f;
        [Tooltip("ひるみ耐性値（この蓄積以上でひるみ）。標準 60。仕様書 §7.2。")]
        [SerializeField] private float _flinchResistance = 60f;

        /// <summary>体幹の最大値。</summary>
        public float PoiseMax => _poiseMax;

        /// <summary>ボスか。</summary>
        public bool IsBoss => _isBoss;

        /// <summary>体幹の回復開始遅延（秒）。</summary>
        public float PoiseRecoveryDelaySeconds => _poiseRecoveryDelaySeconds;

        /// <summary>体幹の毎秒回復割合（最大体幹比）。</summary>
        public float PoiseRecoveryRatioPerSecond => _poiseRecoveryRatioPerSecond;

        /// <summary>被体幹ダメージ倍率。</summary>
        public float PoiseDamageMultiplier => _poiseDamageMultiplier;

        /// <summary>スタン時間（秒）。</summary>
        public float StunSeconds => _stunSeconds;

        /// <summary>ひるみ耐性値。</summary>
        public float FlinchResistance => _flinchResistance;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_poiseMax <= 0f)
            {
                report.Error(name + ": PoiseMax must be > 0.");
            }

            if (_flinchResistance <= 0f)
            {
                report.Error(name + ": FlinchResistance must be > 0.");
            }

            if (_stunSeconds < 0f || _poiseRecoveryDelaySeconds < 0f || _poiseRecoveryRatioPerSecond < 0f)
            {
                report.Error(name + ": Stun/PoiseRecovery values must be >= 0.");
            }
        }
    }
}
