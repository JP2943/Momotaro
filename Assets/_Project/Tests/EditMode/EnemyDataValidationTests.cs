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

        /// <summary>
        /// P6C で変更（旧 <c>Attack_Unblockable_MustDisableGuardAndJustGuard</c>）：ガード不能は通常ガード不可だけを要求する。
        /// 旧「JustGuardable=false 必須」は P6C 仕様 §6 で撤去（打撃・斬撃は原則ジャスガ可）。
        /// </summary>
        [Test]
        public void Attack_Unblockable_MustDisableNormalGuard()
        {
            var a = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: true, jg: true, step: true);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors, "ガード不能は Guardable=false 必須。");
            StringAssert.Contains("Guardable=false", string.Join(", ", report.Errors));
            Object.DestroyImmediate(a);
        }

        /// <summary>P6C で変更：JG 可（新しい既定）にして、Step 不可だけがエラーの原因になるようにした。</summary>
        [Test]
        public void Attack_Unblockable_MustBeSteppable()
        {
            var a = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: false, jg: true, step: false);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors, "ガード不能は Step 可（対処手段）必須。");
            StringAssert.Contains("Steppable", string.Join(", ", report.Errors));
            Object.DestroyImmediate(a);
        }

        // ---- P6C 仕様 §6：通常ガード不可・ジャスガ可が原則、ジャスガも不可は理由を明記した例外だけ ----

        [Test]
        public void P6C_Unblockable_FalseTrueTrue_IsValid()
        {
            var a = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: false, jg: true, step: true);
            SetField(a, "_telegraph", AttackTelegraph.Unblockable);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsFalse(report.HasErrors, string.Join(", ", report.Errors));
            Assert.IsFalse(a.IsJustGuardException);
            Object.DestroyImmediate(a);
        }

        [Test]
        public void P6C_Unblockable_NoJustGuard_WithoutReason_IsError()
        {
            // 未分類の Data を黙って例外にしない。
            var a = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: false, jg: false, step: true);
            SetField(a, "_telegraph", AttackTelegraph.Unblockable);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors);
            StringAssert.Contains("JustGuardExceptionReason", string.Join(", ", report.Errors));
            Object.DestroyImmediate(a);
        }

        [Test]
        public void P6C_Exception_WithReasonAndDistinctTelegraph_IsValid_AndNoticeDiffers()
        {
            // 例外 fixture（つかみ）：理由を書き、予兆はガード不能の印。通常攻撃・通常のガード不能とは文言と記号で区別する。
            var a = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: false, jg: false, step: true);
            SetField(a, "_telegraph", AttackTelegraph.Unblockable);
            SetField(a, "_justGuardExceptionReason", "つかみ（P6C 検証 fixture）");
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsFalse(report.HasErrors, string.Join(", ", report.Errors));
            Assert.IsTrue(a.IsJustGuardException);

            var normalUnblockable = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: false, jg: true, step: true);
            var normal = MakeAttack(EnemyAttackClass.Normal, 0.30f, true, true, true);
            string ex = EnemyAttackDefenseNotice.Describe(a);
            string ub = EnemyAttackDefenseNotice.Describe(normalUnblockable);
            string nm = EnemyAttackDefenseNotice.Describe(normal);
            Assert.AreEqual("通常ガード不可・ジャスガ可能", ub);
            StringAssert.Contains("ジャスガ不可", ex);
            Assert.AreNotEqual(EnemyAttackDefenseNotice.Symbol(a), EnemyAttackDefenseNotice.Symbol(normalUnblockable),
                "色だけに頼らず記号も変える。");
            Assert.AreEqual(string.Empty, nm, "通常攻撃は注意書きなし。");
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(normalUnblockable);
            Object.DestroyImmediate(normal);
        }

        [Test]
        public void P6C_Exception_TelegraphMustBeUnblockable()
        {
            var a = MakeAttack(EnemyAttackClass.Unblockable, 0.75f, guardable: false, jg: false, step: true);
            SetField(a, "_justGuardExceptionReason", "つかみ");
            SetField(a, "_telegraph", AttackTelegraph.Normal);
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors, "例外の予兆は通常攻撃と区別する。");
            Object.DestroyImmediate(a);
        }

        [Test]
        public void P6C_ExceptionReason_OnNonException_IsError()
        {
            var a = MakeAttack(EnemyAttackClass.Normal, 0.30f, true, true, true);
            SetField(a, "_justGuardExceptionReason", "取り違え");
            var report = new DataValidationReport();
            a.Validate(report);
            Assert.IsTrue(report.HasErrors);
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
