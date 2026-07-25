using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-01 受入修正：型付き命中結果の全種別と、属性（HitId・攻撃者・対象・適用値）の保持を検証する。
    /// </summary>
    public sealed class HitResultTests
    {
        private sealed class FakeActor : ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Player;
            public int FloorId => 0;
            public Vector3 WorldPosition => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
        }

        private sealed class FakeTarget : IDamageable
        {
            public int DamageableId => 7;

            public void ReceiveHit(in HitInfo hit)
            {
            }
        }

        [Test]
        public void Damage_RetainsAllAttributes()
        {
            var attacker = new FakeActor();
            var target = new FakeTarget();
            var hitId = new HitId(3, 1);
            var applied = new HitDamage(12f, 34f, 56f);

            HitResult result = HitResult.Damage(hitId, attacker, target, applied);

            Assert.AreEqual(HitResultKind.Damage, result.Kind);
            Assert.AreEqual(hitId, result.HitId);
            Assert.AreSame(attacker, result.Attacker);
            Assert.AreSame(target, result.Target);
            Assert.AreEqual(12f, result.AppliedDamage.Hp);
            Assert.AreEqual(34f, result.AppliedDamage.Poise);
            Assert.AreEqual(56f, result.AppliedDamage.Flinch);
        }

        [Test]
        public void Guard_And_JustGuard_KeepKindAndApplied()
        {
            var a = new FakeActor();
            var t = new FakeTarget();

            HitResult guard = HitResult.Guard(HitId.Single(1), a, t, new HitDamage(0f, 5f, 0f));
            Assert.AreEqual(HitResultKind.Guard, guard.Kind);
            Assert.AreEqual(0f, guard.AppliedDamage.Hp);
            Assert.AreEqual(5f, guard.AppliedDamage.Poise);

            HitResult jg = HitResult.JustGuard(HitId.Single(2), a, t, new HitDamage(0f, 30f, 0f));
            Assert.AreEqual(HitResultKind.JustGuard, jg.Kind);
            Assert.AreEqual(30f, jg.AppliedDamage.Poise);
        }

        [Test]
        public void Evade_And_Rejected_HaveZeroApplied()
        {
            var a = new FakeActor();
            var t = new FakeTarget();

            HitResult evade = HitResult.Evade(HitId.Single(1), a, t);
            Assert.AreEqual(HitResultKind.Evade, evade.Kind);
            Assert.AreEqual(0f, evade.AppliedDamage.Hp);
            Assert.AreEqual(0f, evade.AppliedDamage.Poise);
            Assert.AreEqual(0f, evade.AppliedDamage.Flinch);

            HitResult rejected = HitResult.Rejected(HitId.Single(2), a, t);
            Assert.AreEqual(HitResultKind.Rejected, rejected.Kind);
            Assert.AreEqual(0f, rejected.AppliedDamage.Hp);
            Assert.AreSame(a, rejected.Attacker);
            Assert.AreSame(t, rejected.Target);
        }

        [Test]
        public void Constructor_RetainsAllFields()
        {
            var a = new FakeActor();
            var t = new FakeTarget();
            var result = new HitResult(HitResultKind.Damage, HitId.Single(9), a, t, new HitDamage(1f, 2f, 3f));

            Assert.AreEqual(HitResultKind.Damage, result.Kind);
            Assert.AreEqual(9, result.HitId.InstanceId);
            Assert.AreEqual(1f, result.AppliedDamage.Hp);
            Assert.AreEqual(2f, result.AppliedDamage.Poise);
            Assert.AreEqual(3f, result.AppliedDamage.Flinch);
        }
    }
}
