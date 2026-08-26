namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// プレイヤー死亡通知チャネルの供給契約（Phase3.5 P3.5-02）。主人公（<see cref="Momotaro.Gameplay.Player.PlayerVitalsHolder"/>）が実装し、
    /// 購読側（例：敵 Projectile の一括 Cleanup）が具象へ依存せず <see cref="PlayerDefeatChannel"/> を取得して購読できるようにする。
    /// P3.5-03 の CombatSessionController も同じチャネルを購読する（先回り実装はしないが契約は維持する）。
    /// </summary>
    public interface IPlayerDefeatSource
    {
        /// <summary>プレイヤー死亡（致死確定）の型付き通知チャネル。</summary>
        PlayerDefeatChannel Defeats { get; }
    }
}
