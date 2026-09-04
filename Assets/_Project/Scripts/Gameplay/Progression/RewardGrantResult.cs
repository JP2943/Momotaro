namespace Momotaro.Gameplay.Progression
{
    /// <summary>
    /// 報酬付与の結果種別（P4-00）。付与側（<see cref="PlayerProgressState"/>）が、要求をどう処理したかを型で返す。
    /// 呼び出し側（受け手・HUD・テスト）が「無報酬」「重複」「付与」を区別できるようにし、bool 返しの曖昧さを避ける。
    /// </summary>
    public enum RewardGrantResult
    {
        /// <summary>報酬が指定されていない（<see cref="RewardSnapshot.HasReward"/> が false）。正常系であり、何も付与しない。</summary>
        NoReward = 0,

        /// <summary>付与した（徳を加算し、GrantOnce の場合は付与済みとして記録した）。</summary>
        Granted = 1,

        /// <summary>GrantOnce の報酬が既に付与済みだったため、何も付与しなかった。</summary>
        AlreadyGranted = 2,

        /// <summary>
        /// GrantOnce だが Reward の安定 ID が空で、付与済み記録の鍵を作れなかった。付与自体は行うが重複排除ができない
        /// （Data 側の不備であり、<see cref="Momotaro.Data.GameDataAsset.Validate"/> がエラーとして報告する対象）。
        /// </summary>
        GrantedWithoutId = 3,
    }
}
