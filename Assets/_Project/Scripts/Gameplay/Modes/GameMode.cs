namespace Momotaro.Gameplay.Modes
{
    /// <summary>
    /// ゲーム全体の動作モード（仕様書 13.6）。モード変更で Input Action Map・AI・HUD・
    /// ポーズ可否をまとめて切り替える。値は 7 種。
    /// </summary>
    public enum GameMode
    {
        /// <summary>探索。フィールド移動・調査。</summary>
        Exploration = 0,

        /// <summary>戦闘。</summary>
        Combat = 1,

        /// <summary>会話。</summary>
        Dialogue = 2,

        /// <summary>イベント（複合演出・カットシーン）。</summary>
        Event = 3,

        /// <summary>ポーズ中。</summary>
        Paused = 4,

        /// <summary>Scene 読込中。</summary>
        Loading = 5,

        /// <summary>ゲームオーバー（主人公死亡等）。</summary>
        GameOver = 6,
    }
}
