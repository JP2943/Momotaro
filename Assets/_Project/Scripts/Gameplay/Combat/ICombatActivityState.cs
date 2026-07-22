namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 対象の「体幹の攻撃中補正を受ける状態か」を攻撃側から読み取るための小さな契約（Phase2 P2-05 受入修正）。
    /// 命中時に対象が予備動作（Startup）または攻撃判定中（Active）であれば体幹ダメージ ×1.5 の対象となる（仕様書 §3.1 / Table 9）。
    ///
    /// <see cref="IsPoiseVulnerableAction"/> が true になるのは「攻撃の Startup / Active」のみ。
    /// Recovery（後隙）・Idle・Move・Guard・Flinch・Stun・Defeated などは false（単なる IsAttacking と混同しないための名前）。
    /// Phase 3 の敵 AI も同じ契約を実装して接続できる。
    /// </summary>
    public interface ICombatActivityState
    {
        /// <summary>攻撃の予備動作/判定中（体幹の攻撃中補正の対象）なら true。それ以外は false。</summary>
        bool IsPoiseVulnerableAction { get; }
    }
}
