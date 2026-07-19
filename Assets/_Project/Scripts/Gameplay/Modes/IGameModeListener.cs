namespace Momotaro.Gameplay.Modes
{
    /// <summary>
    /// モード変更を受け取る接続枠。Input（Action Map 切替）や HUD（表示切替）などが実装する。
    /// P0-07 以降で具体実装を差し込む。
    /// </summary>
    public interface IGameModeListener
    {
        /// <summary>モードが変わったときに呼ばれる。</summary>
        void OnModeChanged(GameModeChanged change);
    }
}
