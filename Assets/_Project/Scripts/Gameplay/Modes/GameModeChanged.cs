namespace Momotaro.Gameplay.Modes
{
    /// <summary>
    /// モード変更の型付き通知（仕様書 11.8「通知は型付き」）。
    /// 変更前後のモードを保持し、購読側が差分に応じた切り替えを行えるようにする。
    /// </summary>
    public readonly struct GameModeChanged
    {
        /// <summary>変更前のモード。</summary>
        public GameMode Previous { get; }

        /// <summary>変更後のモード。</summary>
        public GameMode Current { get; }

        public GameModeChanged(GameMode previous, GameMode current)
        {
            Previous = previous;
            Current = current;
        }
    }
}
