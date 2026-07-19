namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// Scene 遷移の抽象。Bootstrap は初期化完了後にこの経由で Launcher へ遷移する。
    /// 実装は <see cref="Momotaro.Infrastructure.SceneFlow.SceneFlowManager"/>（P0-08）。
    /// </summary>
    public interface ISceneFlow
    {
        /// <summary>Launcher（タイトル前段）Scene へ遷移する。</summary>
        void LoadLauncher();
    }
}
