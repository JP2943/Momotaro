using Momotaro.Gameplay.Enemy.Combat.Projectile;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-08：直線 Projectile の純粋ロジック検証（§9.2）。<see cref="EnemyProjectileState"/> の直進・飛距離／寿命消滅と、
    /// <see cref="ProjectileHitDecision"/> の当たり判定（自分/発射者は通過、敵対のみ命中、味方陣営は通過、壁で消滅）を決定的に検証する。
    /// </summary>
    public sealed class EnemyProjectileTests
    {
        [Test]
        public void State_AdvancesAlongDirection_BySpeedTimesDt()
        {
            var s = new EnemyProjectileState(Vector3.zero, new Vector3(0, 0, 1), speed: 10f, maxDistance: 100f, lifetimeSeconds: 10f);
            Vector3 p = s.Advance(0.1f);
            Assert.AreEqual(1f, p.z, 1e-4f, "10m/s × 0.1s = 1m 前進。");
            Assert.AreEqual(1f, s.Traveled, 1e-4f);
            Assert.AreEqual(0.1f, s.Age, 1e-4f);
        }

        [Test]
        public void State_NormalizesDirection_AndFlattensY()
        {
            var s = new EnemyProjectileState(Vector3.zero, new Vector3(0, 5, 3), speed: 5f, maxDistance: 100f, lifetimeSeconds: 10f);
            Assert.AreEqual(0f, s.Direction.y, 1e-5f, "Y は 0 化（XZ 直進）。");
            Assert.AreEqual(1f, s.Direction.magnitude, 1e-4f, "方向は正規化。");
        }

        [Test]
        public void State_ExpiresAtMaxDistance()
        {
            var s = new EnemyProjectileState(Vector3.zero, Vector3.forward, speed: 10f, maxDistance: 15f, lifetimeSeconds: 100f);
            for (int i = 0; i < 14; i++) s.Advance(0.1f); // 14m
            Assert.IsFalse(s.ShouldExpire);
            s.Advance(0.1f); // 15m
            Assert.IsTrue(s.TraveledBeyondMax);
            Assert.IsTrue(s.ShouldExpire);
        }

        [Test]
        public void State_ExpiresAtLifetime()
        {
            var s = new EnemyProjectileState(Vector3.zero, Vector3.forward, speed: 1f, maxDistance: 1000f, lifetimeSeconds: 3f);
            s.Advance(2.9f);
            Assert.IsFalse(s.LifetimeExpired);
            s.Advance(0.1f);
            Assert.IsTrue(s.LifetimeExpired);
            Assert.IsTrue(s.ShouldExpire);
        }

        [Test]
        public void Decision_SelfOrOwner_Passes()
        {
            Assert.AreEqual(ProjectileImpact.Pass,
                ProjectileHitDecision.Decide(isSelfOrOwner: true, hasDamageable: true, hostile: true, isWall: false));
        }

        [Test]
        public void Decision_HostileDamageable_Hits()
        {
            Assert.AreEqual(ProjectileImpact.HitTarget,
                ProjectileHitDecision.Decide(false, hasDamageable: true, hostile: true, isWall: false));
        }

        [Test]
        public void Decision_FriendlyDamageable_Passes()
        {
            Assert.AreEqual(ProjectileImpact.Pass,
                ProjectileHitDecision.Decide(false, hasDamageable: true, hostile: false, isWall: false),
                "敵 Faction（味方陣営）は通過する。");
        }

        [Test]
        public void Decision_Wall_Destroys()
        {
            Assert.AreEqual(ProjectileImpact.DestroyOnWall,
                ProjectileHitDecision.Decide(false, hasDamageable: false, hostile: false, isWall: true));
        }

        [Test]
        public void Decision_EmptySpace_Passes()
        {
            Assert.AreEqual(ProjectileImpact.Pass,
                ProjectileHitDecision.Decide(false, hasDamageable: false, hostile: false, isWall: false));
        }
    }
}
