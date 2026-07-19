namespace Momotaro.Gameplay.Modes
{
    /// <summary>
    /// 各モードに紐づく切替情報（仕様書 13.6）。どの Input Action Map を有効にするか、
    /// ポーズ可能か、HUD を表示するかを表す。Input（P0-07）と HUD がこれを参照する接続枠。
    /// </summary>
    public readonly struct GameModeProfile
    {
        /// <summary>対象モード。</summary>
        public GameMode Mode { get; }

        /// <summary>有効にする Input Action Map 名。</summary>
        public string ActionMap { get; }

        /// <summary>このモードでポーズ操作を許可するか。</summary>
        public bool CanPause { get; }

        /// <summary>戦闘系 HUD を表示するか。</summary>
        public bool ShowsHud { get; }

        public GameModeProfile(GameMode mode, string actionMap, bool canPause, bool showsHud)
        {
            Mode = mode;
            ActionMap = actionMap;
            CanPause = canPause;
            ShowsHud = showsHud;
        }
    }
}
