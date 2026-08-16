namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 現在の試遊 Scene を再読込するための Adapter 契約（Phase3.5 P3.5-03）。Gameplay は Scene API に直接依存しないため、実際の
    /// Async 再読込は Infrastructure 側の実装（P3.5-08 で接続）へ委ねる。Session はこの契約を通じてのみ再読込を要求する。
    /// </summary>
    public interface ICombatSceneReloader
    {
        /// <summary>
        /// 現在の Scene を再読込する。新規に再読込を開始できたら true、既に進行中で無視したら false。
        /// 実装は多重要求を安全に無視すること（Session 側も Reloading 状態で二重要求を防ぐ）。
        /// </summary>
        bool ReloadCurrent();
    }
}
