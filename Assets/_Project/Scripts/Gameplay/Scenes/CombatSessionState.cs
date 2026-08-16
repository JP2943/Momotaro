namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 戦闘試遊セッションの状態（Phase3.5 P3.5-03。仕様書 §5.1 / Table4）。勝敗・Wave・Retry が依存する型付き状態。
    /// 遷移は <see cref="CombatSessionMachine"/> が検証し、不正・重複遷移を拒否する。
    /// </summary>
    public enum CombatSessionState
    {
        /// <summary>Scene 初期化・説明表示。移動可・攻撃不可・開始入力可。</summary>
        Preparing = 0,

        /// <summary>Wave 戦闘中。通常 Gameplay 入力。</summary>
        Playing = 1,

        /// <summary>Wave 間休止。敵なし・移動可・攻撃不可。</summary>
        Intermission = 2,

        /// <summary>全 Wave 完了。戦闘入力停止・Retry 可。</summary>
        Victory = 3,

        /// <summary>Player 死亡。全入力停止・Retry のみ可。</summary>
        Defeat = 4,

        /// <summary>Scene 再読込中。全入力拒否・二重要求拒否。</summary>
        Reloading = 5,
    }
}
