using UnityEngine;

namespace Momotaro.Data.Exploration
{
    /// <summary>
    /// 探索（調査依頼）の距離・時間の設定（P4-07A。v1.0 §10.1「Settings」・§10.2 承認済み試遊初期値）。
    ///
    /// 数値は Data が正本で、コードへ直書きしない。地点（<c>CompanionInvestigationPoint</c>）がこの Data を参照し、
    /// 依頼の<b>受付時に不変の Snapshot へ写す</b>ため、実行中に Inspector で値を変えても現行の依頼は揺れない
    /// （次の依頼から反映。§10.2、E21）。
    ///
    /// 既定値は §10.2 の試遊初期値であり、完成版のバランス確定値ではない（犬丸スプライト完成後にオーナーが再検証する）。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Investigation", menuName = "Momotaro/Exploration/Investigation Settings")]
    public sealed class InvestigationSettingsData : GameDataAsset
    {
        [Header("受付（主人公からの距離）")]
        [Tooltip("「調べる」を受け付ける主人公と地点の XZ 距離（m）。試遊初期値 1.5。")]
        [SerializeField] private float _interactRange = 1.5f;

        [Tooltip("調査中に主人公がこの XZ 距離を超えて地点から離れたら中断する（m）。試遊初期値 3.0。InteractRange 以上。")]
        [SerializeField] private float _continueRange = 3.0f;

        [Header("移動")]
        [Tooltip("調査位置へ到着したとみなす許容差（m）。試遊初期値 0.20。")]
        [SerializeField] private float _arrivalDistance = 0.2f;

        [Tooltip("調査位置へこの秒数で到達できなければ中断する。試遊初期値 3.0。")]
        [SerializeField] private float _moveTimeoutSeconds = 3.0f;

        [Header("調査・帰還")]
        [Tooltip("到着してから発見を確定するまでの秒数。試遊初期値 1.0。")]
        [SerializeField] private float _investigationSeconds = 1.0f;

        [Tooltip("成功後、表示代理の帰還を待つ上限秒。試遊初期値 1.0。")]
        [SerializeField] private float _returnVisualTimeoutSeconds = 1.0f;

        public float InteractRange => _interactRange;
        public float ContinueRange => _continueRange;
        public float ArrivalDistance => _arrivalDistance;
        public float MoveTimeoutSeconds => _moveTimeoutSeconds;
        public float InvestigationSeconds => _investigationSeconds;
        public float ReturnVisualTimeoutSeconds => _returnVisualTimeoutSeconds;

        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);

            // 負値・非有限値・不正な大小関係を検査する（§10.2）。
            if (!IsFiniteNonNegative(_interactRange) || !IsFiniteNonNegative(_continueRange)
                || !IsFiniteNonNegative(_arrivalDistance) || !IsFiniteNonNegative(_moveTimeoutSeconds)
                || !IsFiniteNonNegative(_investigationSeconds) || !IsFiniteNonNegative(_returnVisualTimeoutSeconds))
            {
                report.Error(name + ": Investigation values must be finite and >= 0.");
                return;
            }

            if (_interactRange <= 0f)
            {
                report.Error(name + ": InteractRange must be > 0 (otherwise nothing can be investigated).");
            }

            if (_continueRange < _interactRange)
            {
                report.Error(name + ": ContinueRange must be >= InteractRange (the request would be cancelled the moment it is accepted).");
            }

            if (_arrivalDistance >= _continueRange)
            {
                report.Error(name + ": ArrivalDistance must be < ContinueRange.");
            }

            if (_investigationSeconds <= 0f)
            {
                report.Error(name + ": InvestigationSeconds must be > 0 (a zero-length investigation cannot be seen).");
            }

            if (_moveTimeoutSeconds <= 0f)
            {
                report.Error(name + ": MoveTimeoutSeconds must be > 0 (an unreachable point would hold the request forever).");
            }
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }
}
