namespace Momotaro.Gameplay.Story
{
    /// <summary>受注の結果（P7 02）。</summary>
    public enum QuestAcceptResult
    {
        /// <summary>受注した（段階 0→1、保存要求 1 件）。</summary>
        Accepted = 0,

        /// <summary>受注済み（無変更）。</summary>
        AlreadyAccepted = 1,

        /// <summary>受領済み（再受注不可。無変更）。</summary>
        AlreadyRewarded = 2,

        /// <summary>未知の依頼・campaign が無い（無変更）。</summary>
        Unknown = 3,

        /// <summary>依頼主以外から受けようとした等、その場で受けられない（無変更）。</summary>
        NotOfferedHere = 4,
    }

    /// <summary>報告の結果（P7 02）。</summary>
    public enum QuestReportResult
    {
        /// <summary>報告した（段階 1→2・報酬付与、保存要求 1 件）。</summary>
        Reported = 0,

        /// <summary>未受注（無変更）。</summary>
        NotAccepted = 1,

        /// <summary>条件が未成立（無変更）。</summary>
        NotReportable = 2,

        /// <summary>受領済み（無変更）。</summary>
        AlreadyRewarded = 3,

        /// <summary>未知の依頼・campaign が無い（無変更）。</summary>
        Unknown = 4,

        /// <summary>依頼主以外へ報告しようとした（無変更）。</summary>
        NotOfferedHere = 5,
    }
}
