namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の状態遷移理由（P4-01）。不正遷移の記録と、Down／Stagger からの離脱可否の判定に用いる。
    /// 「なぜ変わったか」を型で残すことで、AI の暴走（意図しない離脱・復帰）をテストで固定できる。
    /// </summary>
    public enum CompanionStateChangeReason
    {
        /// <summary>生成・初期化・復活。</summary>
        Spawned = 0,

        /// <summary>プレイヤーの指示（待機／追従の切り替え等。P4-07）。</summary>
        OrderedByPlayer = 1,

        /// <summary>追従へ復帰した（隊列位置へ戻る）。</summary>
        FollowResumed = 2,

        /// <summary>距離超過・経路失敗でワープした（P4-02）。</summary>
        Warped = 3,

        /// <summary>戦闘対象を捕捉した。</summary>
        EngagedTarget = 4,

        /// <summary>戦闘対象を見失った／対象が消滅した。</summary>
        LostTarget = 5,

        /// <summary>攻撃を開始した。</summary>
        AttackStarted = 6,

        /// <summary>攻撃が次の段へ進んだ。</summary>
        AttackAdvanced = 7,

        /// <summary>攻撃が終了した。</summary>
        AttackFinished = 8,

        /// <summary>防御行動（ガード・回避）を選択した。</summary>
        DefensiveAction = 9,

        /// <summary>守護（かばう）を成立させた（P4-05）。</summary>
        Protected = 10,

        /// <summary>被弾でひるんだ。</summary>
        Staggered = 11,

        /// <summary>ひるみ・ダウンから復帰した。</summary>
        Recovered = 12,

        /// <summary>戦闘不能になった。</summary>
        Defeated = 13,

        /// <summary>退場した（未加入・交代・Scene 離脱）。</summary>
        Left = 14,

        /// <summary>イベントによる強制。</summary>
        ForcedByEvent = 15,

        /// <summary>不正遷移として記録された（診断用。実際の適用は行われない）。</summary>
        IllegalTransition = 16,
    }
}
