using Momotaro.Data;
using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>
    /// 敵 1 攻撃の定義（Phase3 P3-01。§3.2 / §6.3 / Table 4・5）。基本・時間・数値・防御・照準・判定・表現の各群を
    /// 保持する Data 正本。Runtime は本 Asset を書き換えず、実行時は不変 Snapshot（EnemyAttackSnapshot）へ写して使う。
    /// P3-01 はデータ定義と検証のみで、Prepare／Active／Recovery の実行や照準・Hitbox 生成は P3-04 以降で行う。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_EnemyAttack_New", menuName = "Momotaro/Data/Combat/Enemy Attack Data", order = 20)]
    public sealed class EnemyAttackData : GameDataAsset
    {
        [Header("Basic")]
        [Tooltip("攻撃分類。予兆最低時間・防御可否・画面外開始可否の既定を導く。")]
        [SerializeField] private EnemyAttackClass _attackClass = EnemyAttackClass.Normal;
        [Tooltip("使用距離（この距離以内で候補成立）。")]
        [SerializeField] private float _useRange = 2.0f;
        [Tooltip("使用角度（対象がこの半角以内で候補成立、度）。")]
        [SerializeField] private float _useAngle = 60f;
        [Tooltip("再使用までの Cooldown（秒）。")]
        [SerializeField] private float _cooldownSeconds = 1.0f;
        [Tooltip("基礎 Score（選択評価の下駄）。")]
        [SerializeField] private float _baseScore = 10f;
        [Tooltip("必要な攻撃 Slot 種別（集団制御。P3-07）。")]
        [SerializeField] private AttackSlotKind _slotKind = AttackSlotKind.MeleeNormal;

        [Header("Timing")]
        [Tooltip("予兆（Prepare）秒。Table5：通常0.25/強0.50/ガード不能0.70 以上。")]
        [SerializeField] private float _prepareSeconds = 0.25f;
        [Tooltip("判定（Active）秒。")]
        [SerializeField] private float _activeSeconds = 0.10f;
        [Tooltip("後隙（Recovery）秒。")]
        [SerializeField] private float _recoverySeconds = 0.5f;
        [Tooltip("Prepare 内で追尾を停止するまでの秒（0..Prepare）。強・ガード不能は早めに停止。")]
        [SerializeField] private float _trackingStopSeconds = 0.15f;

        [Header("Numeric")]
        [Tooltip("HP ダメージ倍率（攻撃側寄与）。")]
        [SerializeField] private float _hpMultiplier = 1.0f;
        [Tooltip("体幹ダメージ。")]
        [SerializeField] private float _poiseDamage = 10f;
        [Tooltip("ひるませ値。")]
        [SerializeField] private float _flinchPower = 0f;
        [Tooltip("ガード時のスタミナ削り。")]
        [SerializeField] private float _guardStaminaCost = 10f;
        [Tooltip("ノックバック力（小型敵→大きい、ボスは受け側で無効）。")]
        [SerializeField] private float _knockback = 0f;
        [Tooltip("ジャストガード成立時に発射者／攻撃者へ返す体幹ダメージ（§9.1：15〜20）。")]
        [SerializeField] private float _justGuardPoiseReturn = 18f;

        [Header("Reaction (Phase3.5 P3.5-08A)")]
        [Tooltip("Damage 時に被弾者（主人公）を AttackDirection へ押し出す距離（m）。0 で無効。仕様書 §7.4（通常=0.16／強=0.25 目安）。")]
        [SerializeField] private float _hitbackDistance = 0.16f;
        [Tooltip("ヒットバック／ガードバックの所要時間（秒）。0 で無効。仕様書 §7.4（通常=0.12／強=0.16 目安）。")]
        [SerializeField] private float _hitbackSeconds = 0.12f;
        [Tooltip("通常 Guard 成立時に防御者を AttackDirection へ押し戻す距離（m）。0 で無効。仕様書 §7.4（0.10〜0.16）。JG は 0（踏み止まり）。")]
        [SerializeField] private float _guardbackDistance = 0.12f;

        [Header("Projectile (Projectile class only)")]
        [Tooltip("弾速（m/s）。Projectile のみ有効。")]
        [SerializeField] private float _projectileSpeed = 0f;
        [Tooltip("弾の最大飛距離（m）。Projectile のみ有効。")]
        [SerializeField] private float _projectileMaxDistance = 0f;
        [Tooltip("弾の寿命（秒）。Projectile のみ有効。")]
        [SerializeField] private float _projectileLifetimeSeconds = 0f;

        [Header("Charge (Charge class only)")]
        [Tooltip("突進速度（m/s）。Charge のみ有効。0 は移動速度×既定倍率へフォールバック（P3-09。§9.3）。")]
        [SerializeField] private float _chargeSpeed = 0f;

        [Header("Defense Interaction")]
        [Tooltip("ガード可能か。")]
        [SerializeField] private bool _guardable = true;
        [Tooltip("ジャストガード可能か。")]
        [SerializeField] private bool _justGuardable = true;
        [Tooltip("ステップ回避可能か。")]
        [SerializeField] private bool _steppable = true;
        [Tooltip("攻撃中のひるみ無効（この攻撃実行中は自身がひるまない）。")]
        [SerializeField] private bool _attackPoiseImmune = false;

        [Header("Aiming")]
        [Tooltip("照準方式（§6.1）。")]
        [SerializeField] private EnemyAimingMode _aimingMode = EnemyAimingMode.Tracking;
        [Tooltip("予測秒（予測位置型で使用、0.2〜0.5）。")]
        [SerializeField] private float _predictSeconds = 0.3f;
        [Tooltip("追尾角速度（度/秒。追尾型で Prepare 中の旋回上限）。")]
        [SerializeField] private float _trackingAngularSpeed = 180f;

        [Header("Hitbox")]
        [Tooltip("Hitbox の各軸 half extent（m）。")]
        [SerializeField] private Vector3 _hitboxHalfExtents = new Vector3(0.6f, 0.5f, 0.6f);
        [Tooltip("Hitbox の前方オフセット（m）。")]
        [SerializeField] private float _hitboxForwardOffset = 0.8f;
        [Tooltip("Hitbox の高さ（m）。")]
        [SerializeField] private float _hitboxHeight = 0.5f;

        [Header("Presentation")]
        [Tooltip("予兆種別（表示の識別。色のみに依存しない）。")]
        [SerializeField] private AttackTelegraph _telegraph = AttackTelegraph.Normal;
        [Tooltip("Animation／Presentation ID（表示側が解決。Gameplay 時間の正本にしない）。")]
        [SerializeField] private string _presentationId = "";
        [Tooltip("仮 VFX ID。")]
        [SerializeField] private string _vfxId = "";
        [Tooltip("仮 SE ID。")]
        [SerializeField] private string _seId = "";
        [Tooltip("ヒットストップ要求（秒）。")]
        [SerializeField] private float _hitStopSeconds = 0.05f;
        [Tooltip("画面外開始時に画面端警告を必要とするか（遠距離・強攻撃で使用）。")]
        [SerializeField] private bool _requiresOffscreenWarning = false;

        // ---- Basic ----
        /// <summary>攻撃分類。</summary>
        public EnemyAttackClass AttackClass => _attackClass;
        /// <summary>使用距離（m）。</summary>
        public float UseRange => _useRange;
        /// <summary>使用角度（度、半角）。</summary>
        public float UseAngle => _useAngle;
        /// <summary>Cooldown（秒）。</summary>
        public float CooldownSeconds => _cooldownSeconds;
        /// <summary>基礎 Score。</summary>
        public float BaseScore => _baseScore;
        /// <summary>必要 Slot 種別。</summary>
        public AttackSlotKind SlotKind => _slotKind;

        // ---- Timing ----
        /// <summary>予兆（Prepare）秒。</summary>
        public float PrepareSeconds => _prepareSeconds;
        /// <summary>判定（Active）秒。</summary>
        public float ActiveSeconds => _activeSeconds;
        /// <summary>後隙（Recovery）秒。</summary>
        public float RecoverySeconds => _recoverySeconds;
        /// <summary>Prepare 内の追尾停止秒。</summary>
        public float TrackingStopSeconds => _trackingStopSeconds;
        /// <summary>Prepare＋Active＋Recovery の合計秒。</summary>
        public float TotalSeconds => _prepareSeconds + _activeSeconds + _recoverySeconds;

        // ---- Numeric ----
        /// <summary>HP ダメージ倍率。</summary>
        public float HpMultiplier => _hpMultiplier;
        /// <summary>体幹ダメージ。</summary>
        public float PoiseDamage => _poiseDamage;
        /// <summary>ひるませ値。</summary>
        public float FlinchPower => _flinchPower;
        /// <summary>ガード時スタミナ削り。</summary>
        public float GuardStaminaCost => _guardStaminaCost;
        /// <summary>ノックバック力。</summary>
        public float Knockback => _knockback;
        /// <summary>JG 成立時に攻撃者へ返す体幹ダメージ。</summary>
        public float JustGuardPoiseReturn => _justGuardPoiseReturn;
        /// <summary>Damage 時ヒットバック距離（m。Phase3.5 §7.4）。</summary>
        public float HitbackDistance => _hitbackDistance;
        /// <summary>ヒットバック／ガードバック所要時間（秒。Phase3.5 §7.4）。</summary>
        public float HitbackSeconds => _hitbackSeconds;
        /// <summary>通常 Guard 時ガードバック距離（m。Phase3.5 §7.4）。JG は呼び出し側が 0 とする。</summary>
        public float GuardbackDistance => _guardbackDistance;

        // ---- Projectile ----
        /// <summary>弾速（m/s）。</summary>
        public float ProjectileSpeed => _projectileSpeed;
        /// <summary>弾の最大飛距離（m）。</summary>
        public float ProjectileMaxDistance => _projectileMaxDistance;
        /// <summary>弾の寿命（秒）。</summary>
        public float ProjectileLifetimeSeconds => _projectileLifetimeSeconds;

        /// <summary>突進速度（m/s）。Charge のみ有効。</summary>
        public float ChargeSpeed => _chargeSpeed;

        // ---- Defense Interaction ----
        /// <summary>ガード可能か。</summary>
        public bool Guardable => _guardable;
        /// <summary>ジャストガード可能か。</summary>
        public bool JustGuardable => _justGuardable;
        /// <summary>ステップ回避可能か。</summary>
        public bool Steppable => _steppable;
        /// <summary>攻撃中ひるみ無効か。</summary>
        public bool AttackPoiseImmune => _attackPoiseImmune;

        // ---- Aiming ----
        /// <summary>照準方式。</summary>
        public EnemyAimingMode AimingMode => _aimingMode;
        /// <summary>予測秒。</summary>
        public float PredictSeconds => _predictSeconds;
        /// <summary>追尾角速度（度/秒）。</summary>
        public float TrackingAngularSpeed => _trackingAngularSpeed;

        // ---- Hitbox ----
        /// <summary>Hitbox の各軸 half extent。</summary>
        public Vector3 HitboxHalfExtents => _hitboxHalfExtents;
        /// <summary>Hitbox の前方オフセット。</summary>
        public float HitboxForwardOffset => _hitboxForwardOffset;
        /// <summary>Hitbox の高さ。</summary>
        public float HitboxHeight => _hitboxHeight;

        // ---- Presentation ----
        /// <summary>予兆種別。</summary>
        public AttackTelegraph Telegraph => _telegraph;
        /// <summary>Presentation ID。</summary>
        public string PresentationId => _presentationId;
        /// <summary>仮 VFX ID。</summary>
        public string VfxId => _vfxId;
        /// <summary>仮 SE ID。</summary>
        public string SeId => _seId;
        /// <summary>ヒットストップ要求（秒）。</summary>
        public float HitStopSeconds => _hitStopSeconds;
        /// <summary>画面外開始時に画面端警告を必要とするか。</summary>
        public bool RequiresOffscreenWarning => _requiresOffscreenWarning;

        /// <summary>分類ごとの予兆最低秒（Table 5）。</summary>
        public static float MinimumPrepareSeconds(EnemyAttackClass attackClass)
        {
            switch (attackClass)
            {
                case EnemyAttackClass.Heavy:
                    return 0.50f;
                case EnemyAttackClass.Unblockable:
                    return 0.70f;
                default:
                    return 0.25f; // Normal / Charge / Projectile
            }
        }

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);

            if (_useRange <= 0f)
            {
                report.Error(name + ": UseRange must be > 0.");
            }

            if (_useAngle <= 0f || _useAngle > 360f)
            {
                report.Error(name + ": UseAngle must be in (0, 360].");
            }

            if (_cooldownSeconds < 0f)
            {
                report.Error(name + ": CooldownSeconds must be >= 0.");
            }

            if (_prepareSeconds < 0f || _activeSeconds < 0f || _recoverySeconds < 0f)
            {
                report.Error(name + ": Prepare/Active/Recovery seconds must be >= 0.");
            }

            // 予兆最低時間（分類ごと）。読み合いが成立しない短すぎる予兆を弾く。
            float minPrepare = MinimumPrepareSeconds(_attackClass);
            if (_prepareSeconds + 1e-4f < minPrepare)
            {
                report.Error(name + ": PrepareSeconds (" + _prepareSeconds + ") is below minimum " + minPrepare
                    + " for class " + _attackClass + ".");
            }

            // 追尾停止は Prepare 内（時間順序）。
            if (_trackingStopSeconds < 0f || _trackingStopSeconds > _prepareSeconds + 1e-4f)
            {
                report.Error(name + ": TrackingStopSeconds must be within [0, PrepareSeconds].");
            }

            // ガード不能は Guard／JG を無効化していること（表示と防御規則の整合）。Step は可能であること。
            if (_attackClass == EnemyAttackClass.Unblockable)
            {
                if (_guardable || _justGuardable)
                {
                    report.Error(name + ": Unblockable attack must set Guardable=false and JustGuardable=false.");
                }

                if (!_steppable)
                {
                    report.Error(name + ": Unblockable attack must be Steppable (Step is the counter-play).");
                }
            }

            // Projectile は弾パラメータが有効であること。
            if (_attackClass == EnemyAttackClass.Projectile)
            {
                if (_projectileSpeed <= 0f || _projectileMaxDistance <= 0f || _projectileLifetimeSeconds <= 0f)
                {
                    report.Error(name + ": Projectile attack requires Speed, MaxDistance and Lifetime > 0.");
                }
            }

            if (_hpMultiplier < 0f || _poiseDamage < 0f || _flinchPower < 0f || _guardStaminaCost < 0f
                || _knockback < 0f || _justGuardPoiseReturn < 0f)
            {
                report.Error(name + ": Numeric damage values must be >= 0.");
            }

            if (_hitboxHalfExtents.x <= 0f || _hitboxHalfExtents.y <= 0f || _hitboxHalfExtents.z <= 0f)
            {
                report.Error(name + ": HitboxHalfExtents must be > 0 on all axes.");
            }
        }
    }
}
