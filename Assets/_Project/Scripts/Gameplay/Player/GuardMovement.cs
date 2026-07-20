namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// ガード移動の速度倍率を求める純粋ロジック（Phase1 P1-07）。
    /// ガード保持中は指定倍率（通常 0.4）、非ガード時は等倍。
    /// </summary>
    public static class GuardMovement
    {
        /// <summary>ガード状態に応じた速度倍率を返す。</summary>
        public static float SpeedMultiplier(bool guarding, float guardMultiplier)
        {
            return guarding ? guardMultiplier : 1f;
        }
    }
}
