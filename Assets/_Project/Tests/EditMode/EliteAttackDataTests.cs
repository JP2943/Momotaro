using System.Linq;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using NUnit.Framework;
using UnityEditor;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-09：強敵の 4 攻撃 Data（§9.3）を検証する。通常/強/ガード不能/突進の分類、予兆最低時間（Table5）、ガード不能が
    /// Guard／JG 不可かつ Step 可であること、Charge が突進分類であること、Prototype が高 HP・高 Poise・Stun 3 秒・4 攻撃を持つこと。
    /// 数値は P3-01 で確定済みの試作値を破壊しない前提の健全性チェック。
    /// </summary>
    public sealed class EliteAttackDataTests
    {
        private const string Dir = "Assets/_Project/Data/Enemies";

        private static EnemyAttackData Attack(string name)
        {
            var d = AssetDatabase.LoadAssetAtPath<EnemyAttackData>($"{Dir}/SO_EnemyAttack_Elite_{name}.asset");
            Assert.IsNotNull(d, "Elite 攻撃 Data が無い: " + name);
            return d;
        }

        [Test]
        public void FourAttacks_Classes_AndPrepareMinimums()
        {
            EnemyAttackData normal = Attack("Normal");
            EnemyAttackData heavy = Attack("Heavy");
            EnemyAttackData unb = Attack("Unblockable");
            EnemyAttackData charge = Attack("Charge");

            Assert.AreEqual(EnemyAttackClass.Normal, normal.AttackClass);
            Assert.AreEqual(EnemyAttackClass.Heavy, heavy.AttackClass);
            Assert.AreEqual(EnemyAttackClass.Unblockable, unb.AttackClass);
            Assert.AreEqual(EnemyAttackClass.Charge, charge.AttackClass);

            Assert.GreaterOrEqual(normal.PrepareSeconds, 0.25f - 1e-4f, "通常 予兆 >= 0.25。");
            Assert.GreaterOrEqual(heavy.PrepareSeconds, 0.50f - 1e-4f, "強 予兆 >= 0.50。");
            Assert.GreaterOrEqual(unb.PrepareSeconds, 0.70f - 1e-4f, "ガード不能 予兆 >= 0.70。");
        }

        [Test]
        public void Heavy_IsGuardableAndJustGuardable_AndSteppable()
        {
            EnemyAttackData heavy = Attack("Heavy");
            Assert.IsTrue(heavy.Guardable, "強は Guard 可。");
            Assert.IsTrue(heavy.JustGuardable, "強は JG 可。");
            Assert.IsTrue(heavy.Steppable, "強は Step 可。");
        }

        [Test]
        public void Unblockable_NotGuardable_NotJustGuardable_ButSteppable()
        {
            EnemyAttackData unb = Attack("Unblockable");
            Assert.IsFalse(unb.Guardable, "ガード不能は Guard 不可。");
            Assert.IsFalse(unb.JustGuardable, "ガード不能は JG 不可。");
            Assert.IsTrue(unb.Steppable, "ガード不能は Step 可（唯一の対処）。");
        }

        [Test]
        public void EliteArchetype_HighHpPoise_Stun3s_FourAttacks()
        {
            var a = AssetDatabase.LoadAssetAtPath<EnemyArchetypeData>($"{Dir}/SO_Enemy_Elite_Prototype.asset");
            Assert.IsNotNull(a, "Elite Prototype Data が無い。");
            Assert.GreaterOrEqual(a.MaxHp, 150, "高 HP。");
            Assert.GreaterOrEqual(a.PoiseMax, 150f, "高 Poise。");
            Assert.AreEqual(3f, a.StunSeconds, 1e-4f, "Stun 標準 3 秒。");
            Assert.AreEqual(4, a.AttackCount, "4 攻撃を持つ。");
            var classes = Enumerable.Range(0, a.AttackCount).Select(i => a.Attack(i).AttackClass).ToList();
            CollectionAssert.AreEquivalent(
                new[] { EnemyAttackClass.Normal, EnemyAttackClass.Heavy, EnemyAttackClass.Unblockable, EnemyAttackClass.Charge },
                classes, "通常/強/ガード不能/突進の 4 種。");
        }
    }
}
