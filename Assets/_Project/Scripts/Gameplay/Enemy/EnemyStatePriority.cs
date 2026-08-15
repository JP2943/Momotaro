namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵状態の遷移優先度（Phase3 §2.4）。純粋関数として順位を与え、割り込み可否を判定する。
    /// 基準順（高いほど優先）：Down &gt; Event &gt; Stunned &gt; Stagger &gt; AttackActive &gt; AttackPrepare &gt;
    /// AttackRecovery &gt; Guard/Evade &gt; Return &gt; Chase/Reposition &gt; Alert/Suspicious &gt; Patrol/Idle。
    /// 攻撃の中断可否そのものは攻撃 Snapshot が決める（本表は状態の割り込み順のみ）。EditMode で再現可能。
    /// </summary>
    public static class EnemyStatePriority
    {
        /// <summary>状態の優先順位（高いほど強い）。</summary>
        public static int Rank(EnemyState state)
        {
            switch (state)
            {
                case EnemyState.Down: return 100;
                case EnemyState.Event: return 90;
                case EnemyState.Stunned: return 80;
                case EnemyState.Stagger: return 70;
                case EnemyState.AttackActive: return 60;
                case EnemyState.AttackPrepare: return 55;
                case EnemyState.AttackRecovery: return 50;
                case EnemyState.Guard: return 45;
                case EnemyState.Evade: return 45;
                case EnemyState.Return: return 40;
                case EnemyState.Chase: return 30;
                case EnemyState.Reposition: return 30;
                case EnemyState.Alert: return 20;
                case EnemyState.Suspicious: return 18;
                case EnemyState.Patrol: return 10;
                case EnemyState.Idle: return 5;
                default: return 0;
            }
        }

        /// <summary>
        /// <paramref name="incoming"/> が <paramref name="current"/> を割り込めるか。
        /// 厳密に上位（Rank が大きい）なら割り込み可。同順・下位は不可（同順の再入は呼び出し側の理由で扱う）。
        /// </summary>
        public static bool CanInterrupt(EnemyState incoming, EnemyState current)
        {
            return Rank(incoming) > Rank(current);
        }

        /// <summary>被弾由来の状態（Down/Stunned/Stagger）は現在状態に関わらず強制適用対象か。</summary>
        public static bool IsForcedByHit(EnemyState state)
        {
            return state == EnemyState.Down || state == EnemyState.Stunned || state == EnemyState.Stagger;
        }
    }
}
