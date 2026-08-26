using Momotaro.Data.Combat;
using Momotaro.Data.Progression;
using UnityEngine;

namespace Momotaro.Data.Characters
{
    /// <summary>
    /// 敵アーキタイプの確定 Data（Phase3 P3-01。§3.1）。Phase 0〜2 の雛形 <see cref="EnemyData"/> を置き換える確定構造で、
    /// 被弾数値（HP・防御・体幹・ひるみ・スタン）に加え、移動・認識・活動範囲・帰還・攻撃一覧・防御/回避能力・UI 方針の
    /// 「データ欄」を持つ。移動・認識・攻撃の実処理は P3-02〜04 以降で読み取って実装するもので、本 Task ではデータ定義と
    /// 検証のみを行う（未到達 Task を先回り実装しない）。共通 Runtime は <see cref="IEnemyVitalsConfig"/> 経由で構築する。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Enemy_Archetype_New", menuName = "Momotaro/Data/Character/Enemy Archetype", order = 3)]
    public sealed class EnemyArchetypeData : CharacterData, IEnemyVitalsConfig
    {
        [Header("Role")]
        [Tooltip("敵の役割（近接／遠距離／強敵）。")]
        [SerializeField] private EnemyRole _role = EnemyRole.Melee;

        [Tooltip("敵剣閃VFXの素材選択に用いる敵タイプ鍵（近接骸骨=Small／侍骸骨=Medium 等。P3.5-05/06）。Presentation の剣閃素材テーブルの引き当てに用いる。")]
        [SerializeField] private string _slashVfxKey = "Small";

        [Header("Poise / Flinch / Stun")]
        [SerializeField] private float _poiseMax = 100f;
        [Tooltip("体幹の回復開始遅延（秒）。")]
        [SerializeField] private float _poiseRecoveryDelaySeconds = 3f;
        [Tooltip("体幹の毎秒回復量（最大体幹比。0.08=8%/s）。")]
        [SerializeField] private float _poiseRecoveryRatioPerSecond = 0.08f;
        [Tooltip("被体幹ダメージ倍率（対象側。1.0=等倍）。")]
        [SerializeField] private float _poiseDamageMultiplier = 1f;
        [Tooltip("スタン時間（秒）。標準 3。")]
        [SerializeField] private float _stunSeconds = 3f;
        [Tooltip("ひるみ耐性値（この蓄積以上でひるみ）。標準 60。")]
        [SerializeField] private float _flinchResistance = 60f;
        [Tooltip("ひるみ（やられ）状態の持続時間（秒）。この間は移動・攻撃とも行動不能（Stagger）。標準 0.8。")]
        [SerializeField] private float _flinchSeconds = 0.8f;

        [Header("Movement (data only; logic in P3-03)")]
        [Tooltip("旋回速度（度/秒）。")]
        [SerializeField] private float _turnSpeedDegrees = 360f;
        [Tooltip("対象に対する停止距離（m）。")]
        [SerializeField] private float _stopDistance = 1.6f;
        [Tooltip("押し出し Weight（集団の押し合い。P3-03/07）。")]
        [SerializeField] private float _pushWeight = 1f;

        [Header("Perception (data only; logic in P3-02)")]
        [Tooltip("正面視野角（度、全角）。試作 120。")]
        [SerializeField] private float _viewAngleDegrees = 120f;
        [Tooltip("通常視認距離（m）。試作 8.0。")]
        [SerializeField] private float _viewDistance = 8.0f;
        [Tooltip("警戒中視認距離（m）。試作 10.0。")]
        [SerializeField] private float _alertViewDistance = 10.0f;
        [Tooltip("背後の近接認識半径（m）。試作 2.0。")]
        [SerializeField] private float _backAwarenessRadius = 2.0f;
        [Tooltip("完全認識までの蓄積秒。試作 0.25。")]
        [SerializeField] private float _fullRecognitionSeconds = 0.25f;
        [Tooltip("視認喪失後の追跡継続秒。試作 3.0。")]
        [SerializeField] private float _loseSightSeconds = 3.0f;

        [Header("Activity / Return (data only; logic in P3-03)")]
        [Tooltip("活動半径（初期位置中心、m）。近接12/遠距離10/強敵15。")]
        [SerializeField] private float _activityRadius = 12f;
        [Tooltip("帰還速度（m/s）。")]
        [SerializeField] private float _returnSpeed = 4f;
        [Tooltip("帰還後の待機秒（到達後の再開待ち）。")]
        [SerializeField] private float _returnWaitSeconds = 1.0f;

        [Header("Post-Attack Wait")]
        [Tooltip("攻撃後待機の最小秒。近接/遠距離0.7、強敵0.5。")]
        [SerializeField] private float _postAttackWaitMin = 0.7f;
        [Tooltip("攻撃後待機の最大秒。近接/遠距離1.2、強敵0.9。")]
        [SerializeField] private float _postAttackWaitMax = 1.2f;

        [Header("Attacks")]
        [Tooltip("この敵が使用する攻撃 Data 一覧。")]
        [SerializeField] private EnemyAttackData[] _attacks = new EnemyAttackData[0];

        [Header("Guard / Evade ability (data only; logic in P3-10)")]
        [Tooltip("ガード能力を持つか。")]
        [SerializeField] private bool _canGuard = false;
        [Tooltip("ガードの Cooldown（秒）。")]
        [SerializeField] private float _guardCooldownSeconds = 3f;
        [Tooltip("回避能力を持つか。")]
        [SerializeField] private bool _canEvade = false;
        [Tooltip("回避の Cooldown（秒）。")]
        [SerializeField] private float _evadeCooldownSeconds = 4f;

        [Header("Reward (data only; granted by receiver, P4+)")]
        [Tooltip("撃破時に発行する報酬 Data（任意。実付与は本 Phase 対象外。null 可）。")]
        [SerializeField] private RewardData _reward;

        [Header("UI Policy")]
        [Tooltip("体幹を常時表示するか（false=被 Poise 時のみ。強敵は true 可）。")]
        [SerializeField] private bool _alwaysShowPoise = false;

        // ---- Role ----
        /// <summary>役割。</summary>
        public EnemyRole Role => _role;

        // ---- IEnemyVitalsConfig (MaxHp / Defense は CharacterData 由来) ----
        /// <inheritdoc />
        public float PoiseMax => _poiseMax;
        /// <inheritdoc />
        public float PoiseRecoveryDelaySeconds => _poiseRecoveryDelaySeconds;
        /// <inheritdoc />
        public float PoiseRecoveryRatioPerSecond => _poiseRecoveryRatioPerSecond;
        /// <inheritdoc />
        public float PoiseDamageMultiplier => _poiseDamageMultiplier;
        /// <inheritdoc />
        public float StunSeconds => _stunSeconds;
        /// <inheritdoc />
        public float FlinchResistance => _flinchResistance;
        /// <inheritdoc />
        public float FlinchSeconds => _flinchSeconds;

        // ---- Movement ----
        /// <summary>旋回速度（度/秒）。</summary>
        public float TurnSpeedDegrees => _turnSpeedDegrees;
        /// <summary>停止距離（m）。</summary>
        public float StopDistance => _stopDistance;
        /// <summary>押し出し Weight。</summary>
        public float PushWeight => _pushWeight;

        // ---- Perception ----
        /// <summary>正面視野角（度、全角）。</summary>
        public float ViewAngleDegrees => _viewAngleDegrees;
        /// <summary>通常視認距離（m）。</summary>
        public float ViewDistance => _viewDistance;
        /// <summary>警戒中視認距離（m）。</summary>
        public float AlertViewDistance => _alertViewDistance;
        /// <summary>背後の近接認識半径（m）。</summary>
        public float BackAwarenessRadius => _backAwarenessRadius;
        /// <summary>完全認識までの蓄積秒。</summary>
        public float FullRecognitionSeconds => _fullRecognitionSeconds;
        /// <summary>視認喪失後の追跡継続秒。</summary>
        public float LoseSightSeconds => _loseSightSeconds;

        // ---- Activity / Return ----
        /// <summary>活動半径（m）。</summary>
        public float ActivityRadius => _activityRadius;
        /// <summary>帰還速度（m/s）。</summary>
        public float ReturnSpeed => _returnSpeed;
        /// <summary>帰還後の待機秒。</summary>
        public float ReturnWaitSeconds => _returnWaitSeconds;

        // ---- Post-Attack Wait ----
        /// <summary>攻撃後待機の最小秒。</summary>
        public float PostAttackWaitMin => _postAttackWaitMin;
        /// <summary>攻撃後待機の最大秒。</summary>
        public float PostAttackWaitMax => _postAttackWaitMax;

        // ---- Attacks ----
        /// <summary>攻撃 Data 一覧。</summary>
        public System.Collections.Generic.IReadOnlyList<EnemyAttackData> Attacks => _attacks;
        /// <summary>攻撃 Data 数。</summary>
        public int AttackCount => _attacks != null ? _attacks.Length : 0;
        /// <summary>指定 index の攻撃 Data。</summary>
        public EnemyAttackData Attack(int index) => _attacks[index];

        // ---- VFX ----
        /// <summary>敵剣閃VFXの素材選択に用いる敵タイプ鍵（Small/Medium 等。P3.5-05/06）。</summary>
        public string SlashVfxKey => _slashVfxKey;

        // ---- Guard / Evade ----
        /// <summary>ガード能力を持つか。</summary>
        public bool CanGuard => _canGuard;
        /// <summary>ガードの Cooldown（秒）。</summary>
        public float GuardCooldownSeconds => _guardCooldownSeconds;
        /// <summary>回避能力を持つか。</summary>
        public bool CanEvade => _canEvade;
        /// <summary>回避の Cooldown（秒）。</summary>
        public float EvadeCooldownSeconds => _evadeCooldownSeconds;

        // ---- Reward ----
        /// <summary>撃破時に発行する報酬 Data（任意。null 可）。</summary>
        public RewardData Reward => _reward;

        // ---- UI ----
        /// <summary>体幹を常時表示するか。</summary>
        public bool AlwaysShowPoise => _alwaysShowPoise;

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

            if (_turnSpeedDegrees < 0f || _stopDistance < 0f || _pushWeight < 0f)
            {
                report.Error(name + ": Movement values must be >= 0.");
            }

            if (_viewAngleDegrees <= 0f || _viewAngleDegrees > 360f)
            {
                report.Error(name + ": ViewAngleDegrees must be in (0, 360].");
            }

            if (_viewDistance <= 0f || _alertViewDistance <= 0f || _backAwarenessRadius < 0f)
            {
                report.Error(name + ": View distances must be > 0 (back awareness >= 0).");
            }

            if (_fullRecognitionSeconds < 0f || _loseSightSeconds < 0f)
            {
                report.Error(name + ": Recognition/lose seconds must be >= 0.");
            }

            if (_activityRadius <= 0f || _returnSpeed < 0f || _returnWaitSeconds < 0f)
            {
                report.Error(name + ": Activity radius must be > 0 and return values >= 0.");
            }

            if (_postAttackWaitMin < 0f || _postAttackWaitMax < _postAttackWaitMin)
            {
                report.Error(name + ": PostAttackWait range invalid (0 <= min <= max).");
            }

            if (_attacks == null || _attacks.Length == 0)
            {
                report.Error(name + ": Archetype must define at least one Attack.");
            }
            else
            {
                for (int i = 0; i < _attacks.Length; i++)
                {
                    if (_attacks[i] == null)
                    {
                        report.Error(name + ": Attacks[" + i + "] is a missing (null) reference.");
                    }
                }
            }

            if (_guardCooldownSeconds < 0f || _evadeCooldownSeconds < 0f)
            {
                report.Error(name + ": Guard/Evade cooldowns must be >= 0.");
            }
        }
    }
}
