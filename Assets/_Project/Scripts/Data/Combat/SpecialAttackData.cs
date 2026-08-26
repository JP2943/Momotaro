using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>必殺技のパラメータ雛形（仕様書 3.6 / Phase2 P2-10）。独立ゲージは持たず、安全なチャージ時間の確保が使用条件。</summary>
    [CreateAssetMenu(fileName = "SO_Special_New", menuName = "Momotaro/Data/Combat/Special Attack Data", order = 3)]
    public sealed class SpecialAttackData : GameDataAsset
    {
        [Header("Charge")]
        [Tooltip("チャージ（タメ）秒。短いほど使いやすい。P3.5-09 試遊調整で 2.0→1.0 に短縮。")]
        [SerializeField] private float _chargeSeconds = 1.0f;
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
        [Tooltip("小型敵ノックバック力（拡張点）。ボスは無効。仕様書 3.6。")]
        [SerializeField] private float _knockback = 6f;

        [Header("Timing")]
        [Tooltip("判定（Active）秒。P3.5-09 拡張：判定を長めに持続させ、その間 Hitbox を前方へ滑らせる（0.15→0.35）。")]
        [SerializeField] private float _activeSeconds = 0.35f;
        [Tooltip("発動後の後隙秒。仕様書 3.6（0.8〜1.0）。")]
        [SerializeField] private float _recoverySeconds = 0.9f;

        [Header("Reach (射程・必殺技専用。P3.5-09 で通常攻撃より広く長く)")]
        [Tooltip("判定中心の前方オフセット（m）。通常攻撃(0.8)より前へ。")]
        [SerializeField] private float _hitboxForwardOffset = 1.2f;
        [Tooltip("判定中心の高さ（m）。")]
        [SerializeField] private float _hitboxHeight = 0.5f;
        [Tooltip("判定の各軸 half extent（m）。通常攻撃(0.6,0.5,0.6)より広く・前方(Z)を長く。")]
        [SerializeField] private Vector3 _hitboxHalfExtents = new Vector3(0.9f, 0.6f, 1.1f);
        [Tooltip("判定中心が Active 中に前方へ進む距離（m）。P3.5-09：発生から Active 終了までに前方 offset を "
            + "HitboxForwardOffset＋この値まで滑らせ、踏み込む「薙ぎ」の手応えを出す。0 で従来どおり固定。剣閃 VFX も同じ式で追従する。")]
        [SerializeField] private float _hitboxTravelDistance = 1.2f;

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

        /// <summary>小型敵ノックバック力（拡張点。ボスは無効）。</summary>
        public float Knockback => _knockback;

        /// <summary>判定（Active）秒。</summary>
        public float ActiveSeconds => _activeSeconds;

        /// <summary>発動後の後隙秒。</summary>
        public float RecoverySeconds => _recoverySeconds;

        /// <summary>判定中心の前方オフセット（m。必殺技専用の射程。P3.5-09）。</summary>
        public float HitboxForwardOffset => _hitboxForwardOffset;

        /// <summary>判定中心の高さ（m）。</summary>
        public float HitboxHeight => _hitboxHeight;

        /// <summary>判定の各軸 half extent（m。必殺技専用の射程）。</summary>
        public Vector3 HitboxHalfExtents => _hitboxHalfExtents;

        /// <summary>判定中心が Active 中に前方へ進む距離（m。P3.5-09。0 で固定）。</summary>
        public float HitboxTravelDistance => _hitboxTravelDistance;

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

            if (_hitboxHalfExtents.x <= 0f || _hitboxHalfExtents.y <= 0f || _hitboxHalfExtents.z <= 0f)
            {
                report.Error(name + ": HitboxHalfExtents must be > 0 on all axes.");
            }

            if (_hitboxTravelDistance < 0f)
            {
                report.Error(name + ": HitboxTravelDistance must be >= 0.");
            }
        }
    }
}
