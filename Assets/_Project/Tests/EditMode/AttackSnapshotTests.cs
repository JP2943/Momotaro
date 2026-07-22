using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-01 受入修正：AttackSnapshot が原本の必要値を全て複製し、生成後の SO 変更に影響されない不変であることを検証する。
    /// </summary>
    public sealed class AttackSnapshotTests
    {
        private static void SetPrivate(AttackData data, string field, object value)
        {
            FieldInfo info = typeof(AttackData).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, "field not found: " + field);
            info.SetValue(data, value);
        }

        [Test]
        public void FromData_CopiesAllRequiredAttributes()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            try
            {
                SetPrivate(data, "_hpMultiplier", 1.5f);
                SetPrivate(data, "_poiseDamage", 15f);
                SetPrivate(data, "_flinchPower", 50f);
                SetPrivate(data, "_guardStaminaCost", 20f);
                SetPrivate(data, "_guardable", true);
                SetPrivate(data, "_justGuardable", false);
                SetPrivate(data, "_stepAvoidable", true);
                SetPrivate(data, "_telegraph", AttackTelegraph.Heavy);

                AttackSnapshot snap = AttackSnapshot.FromData(data);

                Assert.AreEqual(1.5f, snap.HpMultiplier);
                Assert.AreEqual(15f, snap.PoiseDamage);
                Assert.AreEqual(50f, snap.FlinchPower);
                Assert.AreEqual(20f, snap.GuardStaminaCost);
                Assert.IsTrue(snap.Guardable);
                Assert.IsFalse(snap.JustGuardable);
                Assert.IsTrue(snap.StepAvoidable);
                Assert.AreEqual(AttackTelegraph.Heavy, snap.Telegraph);
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void Snapshot_IsImmutable_WhenSourceDataChangesAfterCapture()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            try
            {
                SetPrivate(data, "_hpMultiplier", 1.0f);
                SetPrivate(data, "_poiseDamage", 8f);
                SetPrivate(data, "_guardable", true);
                SetPrivate(data, "_telegraph", AttackTelegraph.Normal);

                AttackSnapshot snap = AttackSnapshot.FromData(data);

                // 生成後に SO 原本を変更しても Snapshot は不変。
                SetPrivate(data, "_hpMultiplier", 9.9f);
                SetPrivate(data, "_poiseDamage", 999f);
                SetPrivate(data, "_guardable", false);
                SetPrivate(data, "_telegraph", AttackTelegraph.Unblockable);

                Assert.AreEqual(1.0f, snap.HpMultiplier, "HP 倍率は捕捉時の値を保持。");
                Assert.AreEqual(8f, snap.PoiseDamage, "体幹は捕捉時の値を保持。");
                Assert.IsTrue(snap.Guardable, "Guardable は捕捉時の値を保持。");
                Assert.AreEqual(AttackTelegraph.Normal, snap.Telegraph, "Telegraph は捕捉時の値を保持。");
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void FromData_Null_ReturnsSafeDefaults()
        {
            AttackSnapshot snap = AttackSnapshot.FromData(null);
            Assert.IsFalse(snap.Guardable);
            Assert.IsFalse(snap.JustGuardable);
            Assert.IsFalse(snap.StepAvoidable);
            Assert.AreEqual(0f, snap.HpMultiplier);
            Assert.AreEqual(0f, snap.PoiseDamage);
            Assert.AreEqual(AttackTelegraph.Normal, snap.Telegraph);
        }

        [Test]
        public void HitBuilder_FromSnapshot_UsesSnapshotFlags()
        {
            var snap = new AttackSnapshot(1f, 0f, 0f, 0f, 0f, guardable: false, justGuardable: true, stepAvoidable: false, telegraph: AttackTelegraph.Normal);
            HitInfo hit = HitBuilder.FromSnapshot(snap, null, null, Vector3.forward, Vector3.zero, HitDamage.None, HitId.Single(1));

            Assert.IsFalse(hit.Guardable);
            Assert.IsTrue(hit.JustGuardable);
        }
    }
}
