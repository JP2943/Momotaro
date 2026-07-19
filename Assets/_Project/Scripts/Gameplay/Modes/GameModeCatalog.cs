namespace Momotaro.Gameplay.Modes
{
    /// <summary>
    /// 各 <see cref="GameMode"/> に対応する <see cref="GameModeProfile"/> の既定表。
    /// Action Map 名は P0-07 の 4 種（Gameplay／UI／Dialogue／Debug）に対応する。
    /// Phase 0 では既定値のみ。詳細な調整は後続 Phase で行う。
    /// </summary>
    public static class GameModeCatalog
    {
        /// <summary>探索・戦闘で用いる Action Map 名。</summary>
        public const string ActionMapGameplay = "Gameplay";

        /// <summary>メニュー・ポーズ等で用いる Action Map 名。</summary>
        public const string ActionMapUI = "UI";

        /// <summary>会話で用いる Action Map 名。</summary>
        public const string ActionMapDialogue = "Dialogue";

        /// <summary>
        /// 指定モードのプロファイルを返す。未定義のモードは安全側（UI・非ポーズ・HUD非表示）を返す。
        /// </summary>
        public static GameModeProfile GetProfile(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Exploration:
                    return new GameModeProfile(mode, ActionMapGameplay, canPause: true, showsHud: true);
                case GameMode.Combat:
                    return new GameModeProfile(mode, ActionMapGameplay, canPause: true, showsHud: true);
                case GameMode.Dialogue:
                    return new GameModeProfile(mode, ActionMapDialogue, canPause: false, showsHud: false);
                case GameMode.Event:
                    return new GameModeProfile(mode, ActionMapUI, canPause: true, showsHud: false);
                case GameMode.Paused:
                    return new GameModeProfile(mode, ActionMapUI, canPause: false, showsHud: true);
                case GameMode.Loading:
                    return new GameModeProfile(mode, ActionMapUI, canPause: false, showsHud: false);
                case GameMode.GameOver:
                    return new GameModeProfile(mode, ActionMapUI, canPause: false, showsHud: false);
                default:
                    return new GameModeProfile(mode, ActionMapUI, canPause: false, showsHud: false);
            }
        }
    }
}
