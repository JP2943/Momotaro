using UnityEngine;

namespace Momotaro.Data.Characters
{
    /// <summary>
    /// 仲間（犬・猿・雉）の基礎データ（仕様書 4 章 / 5 章。P4-01 で仲間共通契約に必要な値を確定）。
    /// 役割・ヘイト補正・守護（かばう）の距離とクールダウンはここが正本で、Gameplay 側は必ず本 Data を読む。
    /// 追従・攻撃の数値は先回りせず、それぞれ P4-02／P4-03 で追加する。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Companion_New", menuName = "Momotaro/Data/Character/Companion Data", order = 1)]
    public sealed class CompanionData : CharacterData
    {
        [Header("Companion")]
        [Tooltip("役割（犬・猿・雉）。ヘイト補正と戦闘上の役割差、仮素材の識別に用いる。")]
        [SerializeField] private CompanionRole _role = CompanionRole.Dog;
        [SerializeField] private float _switchCooldownSeconds = 3f;
        [SerializeField] private float _leaveRecoverySeconds = 5f;

        [Header("Threat (§7.1 対象補正)")]
        [Tooltip("基礎ヘイト。減衰の対象外で対象が有効な限り維持される下限。主人公=50 に対し仲間は 0 が基本。")]
        [SerializeField] private float _baseThreat;
        [Tooltip("獲得ヘイトへ掛ける対象補正（犬×1.5／猿×1.2／雉×0.5）。行動由来の加算にのみ乗る。")]
        [SerializeField] private float _acquiredThreatMultiplier = 1.5f;

        [Header("Follow (追従・隊列・ワープ。P4-02)")]
        [Tooltip("隊列の基準間隔（m）。主人公の後方 V 字配置の 1 単位。")]
        [SerializeField] private float _followSpacing = 1.6f;
        [Tooltip("隊列位置へこの距離まで近づいたら停止する（m）。")]
        [SerializeField] private float _followStopDistance = 0.35f;
        [Tooltip("停止中にこの距離以上離れたら再び追従を始める（m）。停止距離より大きいこと（境目での往復防止）。")]
        [SerializeField] private float _followResumeDistance = 0.8f;
        [Tooltip("隊列位置からこの距離以上離れたらワープする（m。Scene 遷移・置き去り）。0 で無効。")]
        [SerializeField] private float _warpDistance = 8f;
        [Tooltip("移動しているのに近づけない状態がこの秒数続いたらワープする（経路失敗）。0 で無効。")]
        [SerializeField] private float _stuckSeconds = 1.5f;

        [Header("Combat Targeting (索敵・対象選択。P4-03)")]
        [Tooltip("新規に敵を捕捉できる距離（m）。0 で無制限。")]
        [SerializeField] private float _targetAcquireRange = 8f;
        [Tooltip("捕捉中の敵を維持できる距離（m）。捕捉距離以上にすること（境目での対象往復を防ぐ）。0 で無制限。")]
        [SerializeField] private float _targetLoseRange = 12f;

        [Header("Guardian (守護／かばう。契約は P4-01、実装は P4-05)")]
        [Tooltip("守護の有効距離（m）。主人公からこの距離以内に居るときだけ肩代わりを引き受ける。")]
        [SerializeField] private float _guardianRange = 3f;
        [Tooltip("肩代わり成立後のクールダウン秒。連続で肩代わりし続けないようにする。")]
        [SerializeField] private float _guardianCooldownSeconds = 6f;

        /// <summary>役割（犬・猿・雉）。</summary>
        public CompanionRole Role => _role;

        /// <summary>交代のクールダウン秒。</summary>
        public float SwitchCooldownSeconds => _switchCooldownSeconds;

        /// <summary>退場（Down・交代）から復帰するまでの秒。</summary>
        public float LeaveRecoverySeconds => _leaveRecoverySeconds;

        /// <summary>基礎ヘイト（<c>IThreatTarget.BaseThreat</c> へ供給する）。</summary>
        public float BaseThreat => _baseThreat;

        /// <summary>獲得ヘイト補正（<c>IThreatTarget.AcquiredThreatMultiplier</c> へ供給する）。</summary>
        public float AcquiredThreatMultiplier => _acquiredThreatMultiplier;

        /// <summary>隊列の基準間隔（m）。</summary>
        public float FollowSpacing => _followSpacing;

        /// <summary>隊列位置での停止距離（m）。</summary>
        public float FollowStopDistance => _followStopDistance;

        /// <summary>追従を再開する距離（m）。</summary>
        public float FollowResumeDistance => _followResumeDistance;

        /// <summary>距離超過でワープする距離（m。0 で無効）。</summary>
        public float WarpDistance => _warpDistance;

        /// <summary>経路失敗と判定するまでの停滞秒（0 で無効）。</summary>
        public float StuckSeconds => _stuckSeconds;

        /// <summary>新規に敵を捕捉できる距離（m。0 で無制限）。</summary>
        public float TargetAcquireRange => _targetAcquireRange;

        /// <summary>捕捉中の敵を維持できる距離（m。0 で無制限）。</summary>
        public float TargetLoseRange => _targetLoseRange;

        /// <summary>守護の有効距離（m）。</summary>
        public float GuardianRange => _guardianRange;

        /// <summary>肩代わり成立後のクールダウン秒。</summary>
        public float GuardianCooldownSeconds => _guardianCooldownSeconds;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_switchCooldownSeconds < 0f || _leaveRecoverySeconds < 0f)
            {
                report.Error(name + ": Cooldown/Recovery must be >= 0.");
            }

            if (_baseThreat < 0f)
            {
                report.Error(name + ": BaseThreat must be >= 0.");
            }

            if (_acquiredThreatMultiplier < 0f)
            {
                report.Error(name + ": AcquiredThreatMultiplier must be >= 0.");
            }

            if (_guardianRange < 0f)
            {
                report.Error(name + ": GuardianRange must be >= 0.");
            }

            if (_guardianCooldownSeconds < 0f)
            {
                report.Error(name + ": GuardianCooldownSeconds must be >= 0.");
            }

            if (_followSpacing < 0f || _followStopDistance < 0f || _followResumeDistance < 0f
                || _warpDistance < 0f || _stuckSeconds < 0f)
            {
                report.Error(name + ": Follow distances/seconds must be >= 0.");
            }

            // 停止と再開が逆転していると、隊列の境目で毎フレーム「進む・止まる」を往復する。
            if (_followResumeDistance < _followStopDistance)
            {
                report.Error(name + ": FollowResumeDistance must be >= FollowStopDistance.");
            }

            // ワープ距離が再開距離以下だと、追従で戻る前にワープしてしまい移動が成立しない。
            if (_warpDistance > 0f && _warpDistance <= _followResumeDistance)
            {
                report.Error(name + ": WarpDistance must be > FollowResumeDistance (or 0 to disable).");
            }

            if (_targetAcquireRange < 0f || _targetLoseRange < 0f)
            {
                report.Error(name + ": Target ranges must be >= 0.");
            }

            // 見失い距離が捕捉距離より短いと、捕捉した次の瞬間に見失う（対象が定まらない）。
            if (_targetLoseRange > 0f && _targetLoseRange < _targetAcquireRange)
            {
                report.Error(name + ": TargetLoseRange must be >= TargetAcquireRange (or 0 for unlimited).");
            }
        }
    }
}
