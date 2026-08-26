using System.Reflection;
using Momotaro.Data;
using Momotaro.Data.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-09：必殺技の使用感調整（タメ短縮・射程延長）で追加した <see cref="SpecialAttackData"/> の射程フィールドと既定値、
    /// および不正な half extent が検証で弾かれることを確認する。射程は通常攻撃（前方0.8・extents(0.6,0.5,0.6)）より広い既定とする。
    /// </summary>
    public sealed class SpecialAttackDataReachTests
    {
        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "field not found: " + field);
            f.SetValue(target, value);
        }

        [Test]
        public void Reach_Defaults_AreLongerThanNormalAttack()
        {
            var d = ScriptableObject.CreateInstance<SpecialAttackData>();
            try
            {
                // 既定（初期化子）：通常攻撃(0.8 / (0.6,0.5,0.6))より前方・広範囲。
                Assert.Greater(d.HitboxForwardOffset, 0.8f, "必殺技の前方オフセットは通常攻撃(0.8)より前へ。");
                Assert.Greater(d.HitboxHalfExtents.z, 0.6f, "前方(Z)の到達は通常攻撃(0.6)より長い。");
                Assert.Greater(d.HitboxHalfExtents.x, 0.6f, "横幅も通常攻撃(0.6)より広い。");
                Assert.Greater(d.HitboxHeight, 0f);
            }
            finally
            {
                Object.DestroyImmediate(d);
            }
        }

        [Test]
        public void Validate_FlagsNonPositiveHalfExtents()
        {
            var d = ScriptableObject.CreateInstance<SpecialAttackData>();
            try
            {
                SetPrivate(d, "_hitboxHalfExtents", new Vector3(0f, 0.5f, 1f));
                var report = new DataValidationReport();
                d.Validate(report);
                Assert.IsTrue(report.HasErrors, "half extent が 0 以下なら検証エラー。");
            }
            finally
            {
                Object.DestroyImmediate(d);
            }
        }

        [Test]
        public void Travel_Default_MovesHitboxForward()
        {
            var d = ScriptableObject.CreateInstance<SpecialAttackData>();
            try
            {
                // P3.5-09：発生から Active 終了まで判定中心を前方へ滑らせる。既定は前進あり（>0）。
                Assert.Greater(d.HitboxTravelDistance, 0f, "必殺技は Active 中に前方へ踏み込む（前進距離>0）。");
            }
            finally
            {
                Object.DestroyImmediate(d);
            }
        }

        [Test]
        public void Validate_FlagsNegativeTravelDistance()
        {
            var d = ScriptableObject.CreateInstance<SpecialAttackData>();
            try
            {
                SetPrivate(d, "_hitboxTravelDistance", -0.1f);
                var report = new DataValidationReport();
                d.Validate(report);
                Assert.IsTrue(report.HasErrors, "前進距離が負なら検証エラー。");
            }
            finally
            {
                Object.DestroyImmediate(d);
            }
        }
    }
}
