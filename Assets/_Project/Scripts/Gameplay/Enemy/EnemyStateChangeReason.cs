namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵状態が変わった理由（Phase3 §2.4「状態変更は型付き理由を伴う」）。デバッグ記録・イベント購読側が
    /// 数値ログではなく意味で扱えるようにする。不正遷移も黙らせず型付き理由で 1 回記録する（§2.4）。
    /// </summary>
    public enum EnemyStateChangeReason
    {
        /// <summary>初期化・出現。</summary>
        Spawned = 0,

        /// <summary>対象を認識した。</summary>
        PerceivedTarget = 1,

        /// <summary>不審な刺激を検知した。</summary>
        SuspiciousStimulus = 2,

        /// <summary>対象を見失った。</summary>
        LostTarget = 3,

        /// <summary>攻撃候補が成立する間合いに入った。</summary>
        TargetInRange = 4,

        /// <summary>攻撃を開始した（Prepare）。</summary>
        AttackStarted = 5,

        /// <summary>攻撃が次フェーズへ進んだ（Active/Recovery）。</summary>
        AttackAdvanced = 6,

        /// <summary>攻撃が終了した。</summary>
        AttackFinished = 7,

        /// <summary>ひるみが発生した。</summary>
        Staggered = 8,

        /// <summary>スタンが発生した。</summary>
        Stunned = 9,

        /// <summary>ひるみ・スタンから復帰した。</summary>
        Recovered = 10,

        /// <summary>撃破された（HP0）。</summary>
        Defeated = 11,

        /// <summary>活動範囲を離脱した。</summary>
        LeftActivityRange = 12,

        /// <summary>初期位置へ帰還完了した。</summary>
        Returned = 13,

        /// <summary>イベントにより強制された。</summary>
        ForcedByEvent = 14,

        /// <summary>ガード／回避を開始した。</summary>
        DefensiveAction = 15,

        /// <summary>不正遷移（記録用。抑制せず 1 回記録する）。</summary>
        IllegalTransition = 16,
    }
}
