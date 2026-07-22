using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-06：通常ガードの前方 180°判定（<see cref="GuardGeometry"/>）を検証する。ガード方向を中心に左右 90°（計 180°）
    /// 以内を防御対象とし、境界（ちょうど 90°）は含む。攻撃の進行方向（攻撃者→対象）を入力とするため、近接でも飛び道具でも
    /// 同じ判定になる。向き無しは安全側（前方＝防御可）へ落とす。
    /// </summary>
    public sealed class GuardGeometryTests
    {
        private static readonly Vector3 Forward = Vector3.forward; // +Z をガード方向とする

        // attackDirection は「攻撃者→対象」。正面からの攻撃は対象へ向かって -Forward 方向へ進む。
        [Test]
        public void FrontAttack_IsWithinArc()
        {
            Assert.IsTrue(GuardGeometry.IsWithinGuardArc(Forward, -Forward), "正面(0°)は防御範囲内。");
            Assert.AreEqual(0f, GuardGeometry.IncomingBearingDegrees(Forward, -Forward), 1e-3f);
        }

        [Test]
        public void FortyFiveDegrees_IsWithinArc()
        {
            // 対象→攻撃者が前方から 45°（斜め前）。attackDirection はその符号反転。
            Vector3 toAttacker = new Vector3(1f, 0f, 1f); // +Z から 45°
            Assert.IsTrue(GuardGeometry.IsWithinGuardArc(Forward, -toAttacker));
            Assert.AreEqual(45f, GuardGeometry.IncomingBearingDegrees(Forward, -toAttacker), 1e-3f);
        }

        [Test]
        public void NinetyDegrees_BoundaryIsWithinArc_Inclusive()
        {
            // 真横（90°）は境界で防御可（含む）。
            Assert.IsTrue(GuardGeometry.IsWithinGuardArc(Forward, -Vector3.right));
            Assert.AreEqual(90f, GuardGeometry.IncomingBearingDegrees(Forward, -Vector3.right), 1e-3f);
        }

        [Test]
        public void JustPastNinety_IsOutsideArc()
        {
            // 対象→攻撃者が前方から 135°（後方寄り）→ 防御範囲外。
            Vector3 toAttacker = new Vector3(1f, 0f, -1f); // +Z から 135°
            Assert.IsFalse(GuardGeometry.IsWithinGuardArc(Forward, -toAttacker));
            Assert.AreEqual(135f, GuardGeometry.IncomingBearingDegrees(Forward, -toAttacker), 1e-3f);
        }

        [Test]
        public void BackAttack_IsOutsideArc()
        {
            // 背後(180°)。attackDirection は対象へ向かって +Forward。
            Assert.IsFalse(GuardGeometry.IsWithinGuardArc(Forward, Forward), "背後(180°)は防御不可。");
            Assert.AreEqual(180f, GuardGeometry.IncomingBearingDegrees(Forward, Forward), 1e-3f);
        }

        [Test]
        public void FixedGuardDirection_IsRelativeToGuardForward_NotWorldAxis()
        {
            // ガード方向が +X のとき、同じ「+X から来る攻撃」は正面扱い。方向固定の確認。
            Vector3 guardForward = Vector3.right;
            Assert.IsTrue(GuardGeometry.IsWithinGuardArc(guardForward, -Vector3.right), "+X ガードなら +X 正面は防御可。");
            Assert.IsFalse(GuardGeometry.IsWithinGuardArc(guardForward, Vector3.right), "+X ガードで背後(-X から)は不可。");
        }

        [Test]
        public void CustomHalfAngle_NarrowsArc()
        {
            // 半角 60°にすると 90°は範囲外。
            Assert.IsFalse(GuardGeometry.IsWithinGuardArc(Forward, -Vector3.right, 60f));
            Assert.IsTrue(GuardGeometry.IsWithinGuardArc(Forward, -Vector3.right, 90f));
        }

        [Test]
        public void ZeroVectors_FallBackToFront_Safe()
        {
            Assert.IsTrue(GuardGeometry.IsWithinGuardArc(Vector3.zero, Vector3.zero), "向き無しは安全側（前方＝防御可）。");
        }
    }
}
