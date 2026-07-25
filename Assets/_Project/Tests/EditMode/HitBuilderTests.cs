using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-01：HitBuilder が既存 <see cref="AttackData"/> の防御フラグを写し取ること、
    /// 攻撃データ原本を実行時に書き換えないことを検証する。
    /// </summary>
    public sealed class HitBuilderTests
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
            public int DamageableId => 1;

            public void ReceiveHit(in HitInfo hit)
            {
            }
        }

        private static void SetPrivateBool(AttackData data, string field, bool value)
        {
            FieldInfo info = typeof(AttackData).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, "field not found: " + field);
            info.SetValue(data, value);
        }

        [Test]
        public void FromAttack_CopiesDefaultGuardFlags()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            try
            {
                HitInfo hit = HitBuilder.FromAttack(
                    data, new FakeActor(), new FakeTarget(),
                    Vector3.forward, Vector3.zero, HitDamage.None, HitId.Single(1));

                // AttackData の既定は Guardable=true, JustGuardable=true。
                Assert.IsTrue(hit.Guardable);
                Assert.IsTrue(hit.JustGuardable);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void FromAttack_CopiesUnblockableFlags()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            try
            {
                SetPrivateBool(data, "_guardable", false);
                SetPrivateBool(data, "_justGuardable", false);

                HitInfo hit = HitBuilder.FromAttack(
                    data, new FakeActor(), new FakeTarget(),
                    Vector3.forward, Vector3.zero, HitDamage.None, HitId.Single(1));

                Assert.IsFalse(hit.Guardable, "ガード不能フラグが保持される。");
                Assert.IsFalse(hit.JustGuardable, "ジャストガード不能フラグが保持される。");
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void FromAttack_PassesDamageThroughUnchanged()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            try
            {
                var damage = new HitDamage(11f, 22f, 33f);
                HitInfo hit = HitBuilder.FromAttack(
                    data, new FakeActor(), new FakeTarget(),
                    Vector3.forward, Vector3.zero, damage, HitId.Single(1));

                Assert.AreEqual(11f, hit.Damage.Hp);
                Assert.AreEqual(22f, hit.Damage.Poise);
                Assert.AreEqual(33f, hit.Damage.Flinch);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void FromAttack_DoesNotMutateAttackDataOriginal()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            try
            {
                bool guardableBefore = data.Guardable;
                bool justBefore = data.JustGuardable;
                float poiseBefore = data.PoiseDamage;
                float flinchBefore = data.FlinchPower;

                for (int i = 0; i < 5; i++)
                {
                    HitBuilder.FromAttack(
                        data, new FakeActor(), new FakeTarget(),
                        Vector3.forward, Vector3.zero,
                        new HitDamage(i, i, i), HitId.Single(i));
                }

                Assert.AreEqual(guardableBefore, data.Guardable, "原本 Guardable 不変。");
                Assert.AreEqual(justBefore, data.JustGuardable, "原本 JustGuardable 不変。");
                Assert.AreEqual(poiseBefore, data.PoiseDamage, "原本 PoiseDamage 不変。");
                Assert.AreEqual(flinchBefore, data.FlinchPower, "原本 FlinchPower 不変。");
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void FromAttack_NullData_IsSafeAndNonGuardable()
        {
            HitInfo hit = HitBuilder.FromAttack(
                null, new FakeActor(), new FakeTarget(),
                Vector3.forward, Vector3.zero, HitDamage.None, HitId.Single(1));

            Assert.IsFalse(hit.Guardable);
            Assert.IsFalse(hit.JustGuardable);
        }
    }
}
