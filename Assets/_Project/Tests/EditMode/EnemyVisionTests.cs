using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-02：視覚判定 <see cref="VisionCheck"/> の視野角・距離境界・背後近接・LOS 遮蔽・警戒中距離を検証する（§4.1/Table 7）。
    /// 純粋関数として物理・yield に依存せず再現する。
    /// </summary>
    public sealed class EnemyVisionTests
    {
        // 試作値：視野角120 / 通常8 / 警戒10 / 背後2 / 完全認識0.25 / 喪失3。
        private static PerceptionSettings Settings()
        {
            return new PerceptionSettings(120f, 8f, 10f, 2f, 0.25f, 3f);
        }

        private static readonly Vector3 Origin = Vector3.zero;
        private static readonly Vector3 Forward = Vector3.forward; // +Z

        [Test]
        public void FrontWithinConeAndDistance_WithLos_IsSensed()
        {
            var s = Settings();
            Assert.IsTrue(VisionCheck.CanSense(Origin, Forward, new Vector3(0, 0, 5f), s, isAlert: false, hasLineOfSight: true));
        }

        [Test]
        public void BeyondViewDistance_NotAlert_IsNotSensed_ButAlertDistanceSees()
        {
            var s = Settings();
            var far = new Vector3(0, 0, 9f); // 8<9<=10
            Assert.IsFalse(VisionCheck.CanSense(Origin, Forward, far, s, isAlert: false, hasLineOfSight: true), "通常視認距離外は感知しない。");
            Assert.IsTrue(VisionCheck.CanSense(Origin, Forward, far, s, isAlert: true, hasLineOfSight: true), "警戒中は視認距離が伸びる。");
        }

        [Test]
        public void OutsideHalfAngle_IsNotSensed()
        {
            var s = Settings();
            // 真横（90°）で背後圏(2m)より遠い距離 → コーン外・背後圏外。
            Assert.IsFalse(VisionCheck.CanSense(Origin, Forward, new Vector3(5f, 0, 0f), s, isAlert: false, hasLineOfSight: true));
        }

        [Test]
        public void WithinHalfAngleBoundary_IsSensed()
        {
            var s = Settings();
            // half=60°。60°方向・距離5 → 角度境界内。
            Vector3 dir = Quaternion.AngleAxis(59f, Vector3.up) * Forward;
            Assert.IsTrue(VisionCheck.CanSense(Origin, Forward, Origin + dir * 5f, s, isAlert: false, hasLineOfSight: true));
        }

        [Test]
        public void BehindWithinBackAwareness_IsSensed_ButBeyondIsNot()
        {
            var s = Settings();
            Assert.IsTrue(VisionCheck.CanSense(Origin, Forward, new Vector3(0, 0, -1.5f), s, isAlert: false, hasLineOfSight: true),
                "背後2.0m以内は感知（近接）。");
            Assert.IsFalse(VisionCheck.CanSense(Origin, Forward, new Vector3(0, 0, -5f), s, isAlert: false, hasLineOfSight: true),
                "背後2.0m超は感知しない。");
        }

        [Test]
        public void NoLineOfSight_BlocksSensing_EvenInCone()
        {
            var s = Settings();
            Assert.IsFalse(VisionCheck.CanSense(Origin, Forward, new Vector3(0, 0, 5f), s, isAlert: false, hasLineOfSight: false),
                "壁で視線が通らなければ感知しない。");
        }

        [Test]
        public void PlanarDistance_IgnoresY()
        {
            float d = VisionCheck.PlanarDistance(new Vector3(0, 10f, 0), new Vector3(3f, -5f, 4f));
            Assert.AreEqual(5f, d, 1e-4f, "XZ 平面距離（Y を無視）。");
        }
    }
}
