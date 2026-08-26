using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-01：HP／体幹／ひるませの 3 系統が混同されないこと、防御フラグと HitId が保持されること、
    /// 発動識別子が一意に採番されることを検証する。
    /// </summary>
    public sealed class HitInfoTests
    {
        private sealed class FakeActor : ICombatActor
        {
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public int FloorId { get; set; }
            public Vector3 WorldPosition { get; set; }
            public Vector3 Forward { get; set; } = Vector3.forward;
        }

        private sealed class FakeTarget : IDamageable
        {
            public int DamageableId { get; set; }

            public void ReceiveHit(in HitInfo hit)
            {
            }
        }

        [Test]
        public void HitDamage_KeepsThreeChannelsDistinct()
        {
            var damage = new HitDamage(hp: 5f, poise: 20f, flinch: 7f);
            Assert.AreEqual(5f, damage.Hp);
            Assert.AreEqual(20f, damage.Poise);
            Assert.AreEqual(7f, damage.Flinch);
        }

        [Test]
        public void HitInfo_CarriesDamageChannelsWithoutMixing()
        {
            var hit = new HitInfo(
                new FakeActor(),
                new FakeTarget(),
                Vector3.forward,
                Vector3.zero,
                new HitDamage(hp: 12f, poise: 34f, flinch: 56f),
                guardable: true,
                justGuardable: true,
                hitId: HitId.Single(1));

            Assert.AreEqual(12f, hit.Damage.Hp, "HP が体幹・ひるませと取り違えられていない。");
            Assert.AreEqual(34f, hit.Damage.Poise);
            Assert.AreEqual(56f, hit.Damage.Flinch);
        }

        [Test]
        public void HitInfo_RetainsGuardFlags()
        {
            var unblockable = new HitInfo(
                new FakeActor(), new FakeTarget(), Vector3.forward, Vector3.zero,
                HitDamage.None, guardable: false, justGuardable: false, hitId: HitId.Single(1));
            Assert.IsFalse(unblockable.Guardable);
            Assert.IsFalse(unblockable.JustGuardable);

            var normal = new HitInfo(
                new FakeActor(), new FakeTarget(), Vector3.forward, Vector3.zero,
                HitDamage.None, guardable: true, justGuardable: true, hitId: HitId.Single(1));
            Assert.IsTrue(normal.Guardable);
            Assert.IsTrue(normal.JustGuardable);
        }

        [Test]
        public void HitInfo_RetainsHitId()
        {
            var hitId = new HitId(42, 2);
            var hit = new HitInfo(
                new FakeActor(), new FakeTarget(), Vector3.forward, Vector3.zero,
                HitDamage.None, true, true, hitId);

            Assert.AreEqual(hitId, hit.HitId);
            Assert.AreEqual(42, hit.HitId.InstanceId);
            Assert.AreEqual(2, hit.HitId.Stage);
        }

        [Test]
        public void HitInstanceAllocator_ProducesUniqueIncreasingIds()
        {
            var allocator = new HitInstanceAllocator();
            int a = allocator.Next();
            int b = allocator.Next();
            HitId c = allocator.NextSingle();

            Assert.AreEqual(0, a);
            Assert.AreEqual(1, b);
            Assert.AreEqual(2, c.InstanceId);
            Assert.AreEqual(0, c.Stage);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void HitId_EqualityAndHashing()
        {
            Assert.AreEqual(new HitId(1, 0), HitId.Single(1));
            Assert.IsTrue(new HitId(1, 0) == HitId.Single(1));
            Assert.IsTrue(new HitId(1, 0) != new HitId(1, 1));
            Assert.AreEqual(new HitId(3, 4).GetHashCode(), new HitId(3, 4).GetHashCode());
        }

        [Test]
        public void HitInfo_Reaction_DefaultsToNone_WhenNotSpecified()
        {
            // P3.5-08A：既存の生成経路（reaction を渡さない）は HitReaction.None（全 0・IsProjectile=false）で後方互換。
            var hit = new HitInfo(
                new FakeActor(), new FakeTarget(), Vector3.forward, Vector3.zero,
                HitDamage.None, true, true, HitId.Single(1));

            Assert.AreEqual(0f, hit.Reaction.HitbackDistance);
            Assert.AreEqual(0f, hit.Reaction.HitbackSeconds);
            Assert.AreEqual(0f, hit.Reaction.GuardbackDistance);
            Assert.IsFalse(hit.Reaction.IsProjectile);
        }

        [Test]
        public void WithReaction_ReplacesReaction_KeepsOtherFields()
        {
            // P3.5-08A：WithReaction は Reaction のみ差し替え、他フィールドは不変を保つ。
            var hitId = new HitId(7, 1);
            var baseHit = new HitInfo(
                new FakeActor(), new FakeTarget(), Vector3.forward, new Vector3(1f, 2f, 3f),
                new HitDamage(9f, 8f, 7f), guardStaminaDamage: 5f, justGuardPoiseDamage: 6f,
                guardable: true, justGuardable: false, isJustGuardCounter: false,
                defenseIgnoreRatio: 0f, stunHpMultiplierOverride: 0f, steppable: true, hitId: hitId);

            var reaction = new HitReaction(0.24f, 0.16f, 0.12f, isProjectile: true);
            HitInfo hit = baseHit.WithReaction(reaction);

            Assert.AreEqual(0.24f, hit.Reaction.HitbackDistance);
            Assert.AreEqual(0.16f, hit.Reaction.HitbackSeconds);
            Assert.AreEqual(0.12f, hit.Reaction.GuardbackDistance);
            Assert.IsTrue(hit.Reaction.IsProjectile);

            // 他フィールドは不変。
            Assert.AreEqual(hitId, hit.HitId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), hit.HitPoint);
            Assert.AreEqual(9f, hit.Damage.Hp);
            Assert.AreEqual(5f, hit.GuardStaminaDamage);
            Assert.IsTrue(hit.Guardable);
            Assert.IsFalse(hit.JustGuardable);
            Assert.AreEqual(Vector3.forward, hit.AttackDirection);
        }
    }
}
