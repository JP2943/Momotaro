namespace Momotaro.Gameplay.Modes
{
    /// <summary>
    /// Gameplay 層が現在の <see cref="IGameModeService"/> へアクセスするための提供点。
    /// Gameplay は Infrastructure（Bootstrap）を参照できないため、Infrastructure 側が起動時に
    /// ここへ常駐サービスを注入し、シーン側コンポーネント等はここからモード変更を要求する。
    /// </summary>
    public static class GameModeProvider
    {
        /// <summary>現在有効な GameMode サービス。未初期化時は null。</summary>
        public static IGameModeService Current { get; set; }
    }
}
