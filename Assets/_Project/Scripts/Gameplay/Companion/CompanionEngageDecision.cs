namespace Momotaro.Gameplay.Companion
{
    /// <summary>仲間の戦闘判断（P4-03）。<see cref="CompanionEngagement"/> が 1 Tick ごとに返す。</summary>
    public enum CompanionEngageDecision
    {
        /// <summary>戦闘に関与しない（対象なし・参加できない状態・攻撃を持たない）。追従へ委ねる。</summary>
        Idle = 0,

        /// <summary>対象へ近づく。</summary>
        Chase = 1,

        /// <summary>間合いの内側で待つ（クールダウン待ち・角度待ち）。移動しない。</summary>
        Hold = 2,

        /// <summary>攻撃を開始する。</summary>
        Attack = 3,
    }
}
