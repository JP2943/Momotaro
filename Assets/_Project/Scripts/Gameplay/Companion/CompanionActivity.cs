namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// いま仲間に何を許すかをまとめた 1 枚の答え（P4-FIX F05）。
    ///
    /// これを型にした理由は 2 つある。
    ///
    /// 1 つは <b><c>GameMode</c> だけでは戦闘中かどうかが決まらない</b>こと。Wave の幕間も、開始待ちの Encounter も
    /// 戦闘中で、そこで探索を許すと戦闘の最中に犬丸が調べに行ってしまう。逆に自由探索区画は
    /// <c>Exploration</c> かつ Encounter 未開始として区別しなければならない。
    ///
    /// もう 1 つは <b>「止める」に複数の意味がある</b>こと。Pause は<b>凍結</b>（進行中の攻撃の段・HitId・既命中集合を
    /// 保ったまま時計だけ止める）であり、会話やイベントは<b>破棄</b>（古い攻撃を捨てる）である。
    /// これを 1 つの真偽値で表そうとすると、必ずどちらかが壊れる。
    ///
    /// 各軸は独立に読む。呼び出し側は「自分が使う軸だけ」を見ればよい。
    /// </summary>
    public readonly struct CompanionActivity
    {
        /// <summary>新しい行動（攻撃開始・自動防御・接近）を始めてよいか。</summary>
        public bool CanAct { get; }

        /// <summary>時計を進めてよいか（クールダウン・ひるみ・被弾後無敵・復帰待ち・攻撃の段）。</summary>
        public bool ClocksRun { get; }

        /// <summary>進行中の行動を<b>破棄</b>すべきか（会話・イベント・退場）。Pause は false（保持して凍結）。</summary>
        public bool DiscardOngoing { get; }

        /// <summary>戦闘セッションが継続中か（開始待ち・Wave 幕間を含む）。</summary>
        public bool EncounterActive { get; }

        /// <summary>探索依頼を受け付けてよいか（戦闘中は不可。P4-07 が読む）。</summary>
        public bool CanInvestigate { get; }

        public CompanionActivity(
            bool canAct, bool clocksRun, bool discardOngoing, bool encounterActive, bool canInvestigate)
        {
            CanAct = canAct;
            ClocksRun = clocksRun;
            DiscardOngoing = discardOngoing;
            EncounterActive = encounterActive;
            CanInvestigate = canInvestigate;
        }

        /// <summary>
        /// 何も許さない（安全側）。供給元が無い・未配線のときの既定。
        /// 「分からないから通す」にすると、未配線のまま動いてしまい、配線漏れが最後まで表に出ない。
        /// </summary>
        public static CompanionActivity Stopped =>
            new CompanionActivity(false, false, false, false, false);

        /// <summary>自由探索（Encounter 未開始）。行動も時計も動き、探索を受け付ける。</summary>
        public static CompanionActivity FreeRoam =>
            new CompanionActivity(true, true, false, false, true);

        /// <summary>戦闘中（Wave 幕間・開始待ちを含む）。探索は受け付けない。</summary>
        public static CompanionActivity Fighting =>
            new CompanionActivity(true, true, false, true, false);

        /// <summary>
        /// 戦闘の終端（勝敗の結果画面・再読込待ち）。Encounter は終わっているが、結果画面は入力不可なので
        /// 探索も受け付けない（v1.0 §7.1「結果画面は戦闘終端でも入力不可なので調査不可」）。
        /// </summary>
        public static CompanionActivity Concluded =>
            new CompanionActivity(true, true, false, false, false);

        /// <summary>Pause：凍結する。<b>進行中の行動は捨てない</b>（復帰時に同じ段から続ける）。</summary>
        public static CompanionActivity Paused(bool encounterActive) =>
            new CompanionActivity(false, false, false, encounterActive, false);

        /// <summary>会話・イベント：古い行動を破棄して止める。</summary>
        public static CompanionActivity Interrupted(bool encounterActive) =>
            new CompanionActivity(false, false, true, encounterActive, false);

        /// <inheritdoc />
        public override string ToString()
        {
            return "CompanionActivity(act=" + CanAct + " clocks=" + ClocksRun
                + " discard=" + DiscardOngoing + " encounter=" + EncounterActive
                + " investigate=" + CanInvestigate + ")";
        }
    }
}
