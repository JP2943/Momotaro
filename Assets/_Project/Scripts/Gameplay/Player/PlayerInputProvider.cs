namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Gameplay 層が現在の <see cref="IPlayerInput"/> を取得する提供点（Phase1 P1-03）。
    /// Gameplay は Infrastructure（Bootstrap）を参照できないため、Infrastructure 側の
    /// 入力アダプタが起動時にここへ注入し、Player コンポーネントはここから読む。
    /// </summary>
    public static class PlayerInputProvider
    {
        /// <summary>現在有効な入力。未初期化時は null。</summary>
        public static IPlayerInput Current { get; set; }
    }
}
