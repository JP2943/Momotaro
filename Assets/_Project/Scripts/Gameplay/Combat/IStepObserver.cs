namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// ステップ（回避行動）中かを Presentation が観測するための読み取り専用契約（Phase3.5 P3.5-09。ステップ SE 用）。
    /// <see cref="Momotaro.Gameplay.Player.PlayerStateController"/> が実装する。ステップ無敵（<see cref="IEvadeState"/>）とは別に、
    /// 「いまステップ移動中か」を表す（無敵でないフレームも含む）。Gameplay 挙動には一切影響しない（読み取りのみ）。
    /// </summary>
    public interface IStepObserver
    {
        /// <summary>いまステップ（回避）中か。false→true の立ち上がりがステップ開始。</summary>
        bool IsStepping { get; }
    }
}
