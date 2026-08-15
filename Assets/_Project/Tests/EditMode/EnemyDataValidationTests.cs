using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-01：敵 Data の必須値・時間順序・距離/角度・分類整合・参照欠落を検証する（§3.1/§3.2/Table 4・5）。
    /// 合成 Asset を用いて Validate の各エラー条件を確認する（純粋・AssetDatabase 非依存）。
    /// </summary>
    public sealed class EnemyDataValidationTests
    {
        private static void SetField(object target, string name, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        private static void SetId(GameDataAsset asset, string id)
        {
            SetField(asset, "_id", new StableId(id));
            SetField(asset, "_displayName", "Test");
        }

        private static EnemyAttackData MakeAttack(EnemyAttackClass cls, float prepare, bool guardable, bool jg, bool step)
        {
            var a = ScriptableObject.CreateInstance<EnemyAttackData>();
            SetId(a, "enemy_atk_test");
            SetField(a, "_attackClass", cls);
            SetField(a, "_prepareSeconds", prepare);
            SetField(a, "_guardable", guardable);
            SetField(a, "_justGuardable", jg);
            SetField(a, "_steppable", step);
            return a;
        }

        [Test]
        public void Attack_ValidNormal_HasNoErrors()
        {
            var a = MakeAttack(EnemyAttackClass.Normal, 0.30f, true, true, true);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsFalse(report.HasErrors, "正常な通常攻撃はエラーなし: " + string.Join(", ", report.Errors));
            Object.DestroyImmediate(a);
        }

        [Test]
        public void Attack_PrepareBelowClassMinimum_IsError()
        {
            // 強は 0.50 秒以上が必要。0.40 はエラー。
            var a = MakeAttack(EnemyAttackClass.Heavy, 0.40f, true, true, true);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors, "予兆が分類の最低時間未満はエラーになるべき。");
            Object.DestroyImmediate(a);
        }

        [Test]
        public void Attack_Unblockable_MustDisableGuardAndJustGuard()
        {
            // ガード不能なのに Guardable=true はエラー。
            var a = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: true, jg: false, step: true);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors, "ガード不能は Guardable/JustGuardable=false 必須。");
            Object.DestroyImmediate(a);
        }

        [Test]
        public void Attack_Unblockable_MustBeSteppable()
        {
            var a = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: false, jg: false, step: false);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors, "ガード不能は Step 可（対処手段）必須。");
            Object.DestroyImmediate(a);
        }

        [Test]
        public void Attack_Projectile_RequiresProjectileParams()
        {
            var a = MakeAttack(EnemyAttackClass.Projectile, 0.30f, true, true, true);
            // 速度/距離/寿命を 0 のままにする → エラー。
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors, "Projectile は Speed/MaxDistance/Lifetime > 0 必須。");
            Object.DestroyImmediate(a);
        }

        [Test]
        public void Attack_MinimumPrepareSeconds_MatchesTable5()
        {
            Assert.AreEqual(0.25f, EnemyAttackData.MinimumPrepareSeconds(EnemyAttackClass.Normal), 1e-4f);
            Assert.AreEqual(0.50f, EnemyAttackData.MinimumPrepareSeconds(EnemyAttackClass.Heavy), 1e-4f);
            Assert.AreEqual(0.70f, EnemyAttackData.MinimumPrepareSeconds(EnemyAttackClass.Unblockable), 1e-4f);
            Assert.AreEqual(0.25f, EnemyAttackData.MinimumPrepareSeconds(EnemyAttackClass.Projectile), 1e-4f);
        }

        [Test]
        public void Archetype_ValidWithOneAttack_HasNoErrors()
        {
            var atk = MakeAttack(EnemyAttackClass.Normal, 0.30f, true, true, true);
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            SetId(arch, "enemy_test_archetype");
            SetField(arch, "_attacks", new[] { atk });

            var report = new DataValidationReport();
            arch.Validate(report);
            Assert.IsFalse(report.HasErrors, "攻撃を 1 つ持つ正常なアーキタイプはエラーなし: " + string.Join(", ", report.Errors));
            Object.DestroyImmediate(arch);
            Object.DestroyImmediate(atk);
        }

        [Test]
        public void Archetype_NoAttacks_IsError()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            SetId(arch, "enemy_test_archetype");
            SetField(arch, "_attacks", new EnemyAttackData[0]);

            var report = new DataValidationReport();
            arch.Validate(report);
            Assert.IsTrue(report.HasErrors, "攻撃を 1 つも持たないアーキタイプはエラー。");
            Object.DestroyImmediate(arch);
        }

        [Test]
        public void Archetype_MissingAttackReference_IsError()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            SetId(arch, "enemy_test_archetype");
            SetField(arch, "_attacks", new EnemyAttackData[] { null });

            var report = new DataValidationReport();
            arch.Validate(report);
            Assert.IsTrue(report.HasErrors, "攻撃参照が欠落（null）はエラー。");
            Object.DestroyImmediate(arch);
        }

        [Test]
        public void Archetype_InvalidViewAngle_IsError()
        {
            var atk = MakeAttack(EnemyAttackClass.Normal, 0.30f, true, true, true);
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            SetId(arch, "enemy_test_archetype");
            SetField(arch, "_attacks", new[] { atk });
            SetField(arch, "_viewAngleDegrees", 0f);

            var report = new DataValidationReport();
            arch.Validate(report);
            Assert.IsTrue(report.HasErrors, "視野角 0 は不正（(0,360]）。");
            Object.DestroyImmediate(arch);
            Object.DestroyImmediate(atk);
        }

        [Test]
        public void Archetype_ImplementsVitalsConfig()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            Assert.IsInstanceOf<IEnemyVitalsConfig>(arch, "アーキタイプは共通 Vitals 契約を実装する。");
            Object.DestroyImmediate(arch);
        }
    }
}
