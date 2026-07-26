using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-01：<see cref="EnemyAttackSnapshot"/> が攻撃開始時の値を写し取り、以降に原本 Data が変更されても
    /// 実行中の攻撃（Snapshot）は不変であることを検証する（§2.3/§6.3）。
    /// </summary>
    public sealed class EnemyAttackSnapshotTests
    {
        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        [Test]
        public void From_CopiesKeyValues()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            SetField(d, "_attackClass", EnemyAttackClass.Heavy);
            SetField(d, "_prepareSeconds", 0.55f);
            SetField(d, "_poiseDamage", 25f);
            SetField(d, "_guardable", false);

            EnemyAttackSnapshot snap = EnemyAttackSnapshot.From(d);
            Assert.AreEqual(EnemyAttackClass.Heavy, snap.AttackClass);
            Assert.AreEqual(0.55f, snap.PrepareSeconds, 1e-4f);
            Assert.AreEqual(25f, snap.PoiseDamage, 1e-4f);
            Assert.IsFalse(snap.Guardable);
            Object.DestroyImmediate(d);
        }

        [Test]
        public void Snapshot_IsImmutable_WhenDataChangesMidAttack()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            SetField(d, "_prepareSeconds", 0.30f);
            SetField(d, "_poiseDamage", 10f);

            EnemyAttackSnapshot snap = EnemyAttackSnapshot.From(d);

            // 実行中に原本 Data を書き換える（Asset 変更相当）。
            SetField(d, "_prepareSeconds", 99f);
            SetField(d, "_poiseDamage", 999f);

            Assert.AreEqual(0.30f, snap.PrepareSeconds, 1e-4f, "Snapshot は原本変更の影響を受けない。");
            Assert.AreEqual(10f, snap.PoiseDamage, 1e-4f, "Snapshot の数値は開始時のまま。");
            Object.DestroyImmediate(d);
        }
    }
}
