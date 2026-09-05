namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間状態の遷移優先度（P4-01）。純粋関数として順位を与え、割り込み可否を判定する。
    ///
    /// 基準順（高いほど優先）：Away &gt; Down &gt; Event &gt; Recovering &gt; Stagger &gt; Protect &gt;
    /// AttackActive &gt; AttackPrepare &gt; AttackRecovery &gt; Guard/Evade &gt; Warp &gt; Chase &gt; Follow &gt; Idle。
    ///
    /// 退場（Away）を最上位に置くのは、Scene 離脱・交代・イベントによる退場が、ダウン中でも必ず成立しなければ
    /// 残留（購読・対象参照・判定の置き去り）になるため。守護（Protect）を攻撃より上に置くのは、「かばう」が
    /// 自分の攻撃を中断してでも割り込む行動だから（P4-05）。
    /// </summary>
    public static class CompanionStatePriority
    {
        /// <summary>状態の優先順位（高いほど強い）。</summary>
        public static int Rank(CompanionState state)
        {
            switch (state)
            {
                case CompanionState.Away: return 110;
                case CompanionState.Down: return 100;
                case CompanionState.Event: return 90;
                case CompanionState.Recovering: return 75;
                case CompanionState.Stagger: return 70;
                case CompanionState.Protect: return 65;
                case CompanionState.AttackActive: return 60;
                case CompanionState.AttackPrepare: return 55;
                case CompanionState.AttackRecovery: return 50;
                case CompanionState.Guard: return 45;
                case CompanionState.Evade: return 45;
                case CompanionState.Warp: return 40;
                case CompanionState.Chase: return 30;
                case CompanionState.Follow: return 20;
                case CompanionState.Idle: return 5;
                default: return 0;
            }
        }

        /// <summary>
        /// <paramref name="incoming"/> が <paramref name="current"/> を割り込めるか。厳密に上位（Rank が大きい）なら可。
        /// 同順・下位は不可（同順の再入は呼び出し側の理由で扱う）。
        /// </summary>
        public static bool CanInterrupt(CompanionState incoming, CompanionState current)
        {
            return Rank(incoming) > Rank(current);
        }

        /// <summary>被弾由来の強制状態（Stagger／Down）か。仲間にスタンは無い（敵専用）。</summary>
        public static bool IsForcedByHit(CompanionState state)
        {
            return state == CompanionState.Down || state == CompanionState.Stagger;
        }
    }
}
