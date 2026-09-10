namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 行動の持ち主。数字が大きいほど強い。
    ///
    /// 「誰の行動か」を持たないと、<b>終わったはずの行動の後始末が、新しい行動を壊す</b>のを止められない。
    /// 実際に起こりうるのは、攻撃中にひるんで攻撃が中断されたあと、遅れて届いた「攻撃終了」が
    /// Stagger を Chase へ書き換えてしまう類の事故である。
    /// </summary>
    public enum CompanionActionOwner
    {
        /// <summary>誰も行動していない。</summary>
        None = 0,

        /// <summary>追従（主人公について歩く・その場で向く）。</summary>
        Follow = 1,

        /// <summary>
        /// 探索（気になる地点を調べに行く。P4-07A）。追従より強く、戦闘より弱い。
        /// 追従より強くしないと、調べに行くそばから隊列へ引き戻される。
        /// </summary>
        Investigate = 2,

        /// <summary>戦闘（接近・通常攻撃）。</summary>
        Combat = 3,

        /// <summary>防御（構え・回避）。</summary>
        Defense = 4,

        /// <summary>守護（かばう）。</summary>
        Guardian = 5,

        /// <summary>全体イベント・演出。</summary>
        Event = 6,
    }

    /// <summary>
    /// 1 回の行動を指す引換券。<b>持ち主と実行 ID の組</b>で、これが今の行動と一致する場合だけ
    /// 「段を進める」「正常終了する」が受理される。
    ///
    /// 実行 ID を持つ理由は、持ち主が同じでも<b>別の行動</b>なら別物として扱う必要があるため。
    /// 例：攻撃 → 中断 → 同じ戦闘側が接近を開始、のあとに古い「攻撃終了」が届いても弾きたい。
    /// 持ち主だけで判定すると、どちらも Combat なので通ってしまう。
    /// </summary>
    public readonly struct CompanionActionHandle
    {
        /// <summary>この行動の持ち主。</summary>
        public CompanionActionOwner Owner { get; }

        /// <summary>この行動の実行 ID（行動を開始するたびに増える）。</summary>
        public int RunId { get; }

        public CompanionActionHandle(CompanionActionOwner owner, int runId)
        {
            Owner = owner;
            RunId = runId;
        }

        /// <summary>有効な引換券か（開始に失敗すると既定値が返る）。</summary>
        public bool IsValid => Owner != CompanionActionOwner.None;

        /// <inheritdoc />
        public override string ToString() => "CompanionAction(" + Owner + "#" + RunId + ")";
    }
}
