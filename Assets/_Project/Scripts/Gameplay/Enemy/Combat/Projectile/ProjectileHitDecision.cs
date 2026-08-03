namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>Projectile が 1 つの Collider に対して取るべき挙動（Phase3 P3-08。§9.2）。</summary>
    public enum ProjectileImpact
    {
        /// <summary>無視して通過（自分・発射者・味方陣営の敵）。</summary>
        Pass = 0,

        /// <summary>命中させて消滅（敵対対象＝主人公・仲間）。1 発 1Hit。</summary>
        HitTarget = 1,

        /// <summary>壁で消滅（Default レイヤー等の遮蔽）。</summary>
        DestroyOnWall = 2,
    }

    /// <summary>
    /// Projectile の当たり判定の純粋な決定（Phase3 P3-08。§9.2「壁で消滅、敵 Faction には命中せず、主人公には命中する」）。
    /// 物理 Query の結果（自分/発射者か・被弾契約を持つか・敵対か・壁レイヤーか）から挙動を決める。Unity 非依存で EditMode 再現可能。
    /// </summary>
    public static class ProjectileHitDecision
    {
        /// <summary>
        /// <paramref name="isSelfOrOwner"/>：自分または発射者の階層。<paramref name="hasDamageable"/>：被弾契約を持つ。
        /// <paramref name="hostile"/>：発射者から見て敵対（主人公・仲間）。<paramref name="isWall"/>：壁レイヤー。
        /// </summary>
        public static ProjectileImpact Decide(bool isSelfOrOwner, bool hasDamageable, bool hostile, bool isWall)
        {
            if (isSelfOrOwner)
            {
                return ProjectileImpact.Pass; // 発射者・自分は素通り。
            }

            if (hasDamageable)
            {
                return hostile ? ProjectileImpact.HitTarget : ProjectileImpact.Pass; // 敵対のみ命中。味方陣営の敵は通過。
            }

            return isWall ? ProjectileImpact.DestroyOnWall : ProjectileImpact.Pass; // 壁で消滅、その他は通過。
        }
    }
}
