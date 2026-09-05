using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data;
using Momotaro.Data.Characters;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-01：<see cref="CompanionData"/> が仲間共通契約に必要な値をすべて公開し、検証で不正値を弾くことを固定する。
    /// 数値は原則 Data へ集約する規約のため、役割・ヘイト補正・守護（かばう）の距離とクールダウンはここが正本になる。
    /// 追従・攻撃の数値は先回りせず、それぞれ P4-02／P4-03 で追加する。
    /// </summary>
    public sealed class CompanionDataTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _created)
            {
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        private CompanionData New(string id = "companion_inumaru")
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            data.name = "SO_Companion_Test";
            _created.Add(data);
            SetPrivateField(data, "_id", new Momotaro.Core.Identification.StableId(id));
            SetPrivateField(data, "_displayName", "Inumaru");
            return data;
        }

        [Test]
        public void Defaults_AreSaneForDog()
        {
            CompanionData d = New();

            Assert.AreEqual(CompanionRole.Dog, d.Role, "既定は犬（P4 の最初の仲間＝犬丸）。");
            Assert.Greater(d.SwitchCooldownSeconds, 0f);
            Assert.Greater(d.LeaveRecoverySeconds, 0f, "退場からの復帰秒が公開されている（P4-06 が参照）。");
            Assert.AreEqual(0f, d.BaseThreat, 1e-4f, "仲間の基礎ヘイトは 0（主人公=50 と対比。§7.1）。");
            Assert.AreEqual(1.5f, d.AcquiredThreatMultiplier, 1e-4f, "犬の獲得ヘイト補正は ×1.5（§7.1）。");
            Assert.Greater(d.GuardianRange, 0f, "守護の有効距離（P4-05 が参照）。");
            Assert.GreaterOrEqual(d.GuardianCooldownSeconds, 0f);
        }

        [Test]
        public void Role_IsReadFromData()
        {
            CompanionData d = New();
            SetPrivateField(d, "_role", CompanionRole.Pheasant);
            SetPrivateField(d, "_acquiredThreatMultiplier", 0.5f);

            Assert.AreEqual(CompanionRole.Pheasant, d.Role);
            Assert.AreEqual(0.5f, d.AcquiredThreatMultiplier, 1e-4f, "雉は ×0.5（§7.1）。");
        }

        [Test]
        public void Validate_AcceptsDefaults()
        {
            CompanionData d = New();
            var report = new DataValidationReport();

            d.Validate(report);

            Assert.IsFalse(report.HasErrors, "既定値は検証を通る:\n- " + string.Join("\n- ", report.Errors));
        }

        [Test]
        public void Validate_RejectsNegativeCooldownAndRecovery()
        {
            CompanionData d = New();
            SetPrivateField(d, "_switchCooldownSeconds", -1f);
            SetPrivateField(d, "_leaveRecoverySeconds", -1f);
            var report = new DataValidationReport();

            d.Validate(report);

            Assert.IsTrue(report.HasErrors);
        }

        [Test]
        public void Validate_RejectsNegativeThreatValues()
        {
            CompanionData d = New();
            SetPrivateField(d, "_baseThreat", -5f);
            var report = new DataValidationReport();
            d.Validate(report);
            Assert.IsTrue(report.HasErrors, "基礎ヘイトの負値は不正。");

            CompanionData d2 = New();
            SetPrivateField(d2, "_acquiredThreatMultiplier", -0.1f);
            var report2 = new DataValidationReport();
            d2.Validate(report2);
            Assert.IsTrue(report2.HasErrors, "獲得ヘイト補正の負値は不正。");
        }

        [Test]
        public void Validate_RejectsNegativeGuardianValues()
        {
            CompanionData d = New();
            SetPrivateField(d, "_guardianRange", -1f);
            var report = new DataValidationReport();
            d.Validate(report);
            Assert.IsTrue(report.HasErrors, "守護距離の負値は不正。");

            CompanionData d2 = New();
            SetPrivateField(d2, "_guardianCooldownSeconds", -1f);
            var report2 = new DataValidationReport();
            d2.Validate(report2);
            Assert.IsTrue(report2.HasErrors, "守護クールダウンの負値は不正。");
        }

        [Test]
        public void Validate_InheritsCharacterBaseRules()
        {
            CompanionData d = New();
            SetPrivateField(d, "_maxHp", 0);
            var report = new DataValidationReport();

            d.Validate(report);

            Assert.IsTrue(report.HasErrors, "基底（CharacterData）の検証も効く。");
        }
    }
}
