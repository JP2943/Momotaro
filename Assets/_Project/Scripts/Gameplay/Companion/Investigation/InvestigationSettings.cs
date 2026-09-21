using Momotaro.Data.Exploration;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// 探索設定の不変 Snapshot（P4-07A。v1.0 §10.2「全値を Data 化」・E21「実行中の SO 編集で現行依頼は不変」）。
    /// 依頼の受付時に <see cref="InvestigationSettingsData"/> から写し取り、以後その依頼はこれしか読まない。
    /// </summary>
    public readonly struct InvestigationSettings
    {
        public float InteractRange { get; }
        public float ContinueRange { get; }
        public float ArrivalDistance { get; }
        public float MoveTimeoutSeconds { get; }
        public float InvestigationSeconds { get; }
        public float ReturnVisualTimeoutSeconds { get; }

        public InvestigationSettings(
            float interactRange, float continueRange, float arrivalDistance,
            float moveTimeoutSeconds, float investigationSeconds, float returnVisualTimeoutSeconds)
        {
            InteractRange = Clamp(interactRange);
            ContinueRange = Clamp(continueRange);
            ArrivalDistance = Clamp(arrivalDistance);
            MoveTimeoutSeconds = Clamp(moveTimeoutSeconds);
            InvestigationSeconds = Clamp(investigationSeconds);
            ReturnVisualTimeoutSeconds = Clamp(returnVisualTimeoutSeconds);
        }

        /// <summary>依頼を成立させられる設定か（受付距離・調査時間・移動上限が正）。</summary>
        public bool IsUsable => InteractRange > 0f && InvestigationSeconds > 0f && MoveTimeoutSeconds > 0f;

        /// <summary>Data から写す（null なら使えない設定＝依頼は拒否される）。</summary>
        public static InvestigationSettings From(InvestigationSettingsData data)
        {
            if (data == null)
            {
                return default;
            }

            return new InvestigationSettings(
                data.InteractRange, data.ContinueRange, data.ArrivalDistance,
                data.MoveTimeoutSeconds, data.InvestigationSeconds, data.ReturnVisualTimeoutSeconds);
        }

        private static float Clamp(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }
    }
}
