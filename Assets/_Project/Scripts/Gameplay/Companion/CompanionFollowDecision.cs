namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 追従の判断結果（P4-02）。実際の移動・瞬間移動は Motor 側が行い、本判断は「何をすべきか」だけを返す
    /// （Gameplay は Transform を直接動かさず、判断と実行を分ける）。
    /// </summary>
    public enum CompanionFollowDecision
    {
        /// <summary>隊列位置に足りている。移動しない。</summary>
        Hold = 0,

        /// <summary>隊列位置へ移動する。</summary>
        Move = 1,

        /// <summary>距離超過または経路失敗のため、隊列位置へ瞬間移動する。</summary>
        Warp = 2,
    }
}
