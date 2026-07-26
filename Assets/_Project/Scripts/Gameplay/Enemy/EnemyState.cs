namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵の基本状態（Phase3 §2.4）。認識・追跡・攻撃・被弾・帰還・撃破を一つの語彙で表す。
    /// 遷移優先度は <see cref="EnemyStatePriority"/> が持ち、実際の遷移駆動（認識・移動・攻撃）は P3-02 以降で接続する。
    /// P3-01 では列挙と、被弾由来の Stagger／Stunned／Down の優先度・後始末までを対象とする。
    /// </summary>
    public enum EnemyState
    {
        /// <summary>待機（非戦闘）。</summary>
        Idle = 0,

        /// <summary>巡回。</summary>
        Patrol = 1,

        /// <summary>不審（短い視認・調査）。</summary>
        Suspicious = 2,

        /// <summary>警戒（戦闘開始）。</summary>
        Alert = 3,

        /// <summary>追跡。</summary>
        Chase = 4,

        /// <summary>間合い調整。</summary>
        Reposition = 5,

        /// <summary>攻撃予兆。</summary>
        AttackPrepare = 6,

        /// <summary>攻撃判定中。</summary>
        AttackActive = 7,

        /// <summary>攻撃後隙。</summary>
        AttackRecovery = 8,

        /// <summary>ガード。</summary>
        Guard = 9,

        /// <summary>回避。</summary>
        Evade = 10,

        /// <summary>ひるみ（のけぞり）。</summary>
        Stagger = 11,

        /// <summary>スタン（気絶）。</summary>
        Stunned = 12,

        /// <summary>帰還。</summary>
        Return = 13,

        /// <summary>撃破（ダウン）。</summary>
        Down = 14,

        /// <summary>イベント強制（会話・演出）。</summary>
        Event = 15,
    }
}
