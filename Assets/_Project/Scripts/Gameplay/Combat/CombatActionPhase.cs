namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 戦闘主体の攻撃行動フェーズ（Phase2 P2-05 受入修正）。体幹の攻撃中補正（×1.5）の対象は
    /// <see cref="Startup"/> / <see cref="Active"/> のみで、<see cref="Recovery"/> や <see cref="None"/> は対象外。
    /// Player 通常攻撃の内部フェーズ（AttackPhase）とは別に、被弾側（Dummy 検証状態・将来の敵）が共通に扱える語彙。
    /// </summary>
    public enum CombatActionPhase
    {
        /// <summary>非攻撃（Idle/Move/Guard 等）。</summary>
        None = 0,

        /// <summary>攻撃の予備動作（体幹補正対象）。</summary>
        Startup = 1,

        /// <summary>攻撃の判定中（体幹補正対象）。</summary>
        Active = 2,

        /// <summary>攻撃の後隙（体幹補正対象外）。</summary>
        Recovery = 3,
    }

    /// <summary>
    /// <see cref="CombatActionPhase"/> のヘルパ。体幹の攻撃中補正対象かを一元的に判定する。
    /// </summary>
    public static class CombatActionPhaseExtensions
    {
        /// <summary>Startup / Active のみ体幹の攻撃中補正対象。</summary>
        public static bool IsPoiseVulnerable(this CombatActionPhase phase)
        {
            return phase == CombatActionPhase.Startup || phase == CombatActionPhase.Active;
        }
    }
}
