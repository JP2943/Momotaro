using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-01：World XZ 平面での命中方向（前方／側面／背後）判定と、不正入力の安全処理を検証する。
    /// </summary>
    public sealed class CombatGeometryTests
    {
        private static readonly Vector3 Forward = Vector3.forward; // (0,0,1)

        [Test]
        public void AttackerInFront_ClassifiedAsFront()
        {
            Vector3 toAttacker = new Vector3(0f, 0f, 1f);
            Assert.AreEqual(HitBearing.Front, CombatGeometry.ClassifyBearing(Forward, toAttacker));
            Assert.IsFalse(CombatGeometry.IsBackHit(Forward, toAttacker));
        }

        [Test]
        public void AttackerToSide_ClassifiedAsSide()
        {
            Assert.AreEqual(HitBearing.Side, CombatGeometry.ClassifyBearing(Forward, new Vector3(1f, 0f, 0f)));
            Assert.AreEqual(HitBearing.Side, CombatGeometry.ClassifyBearing(Forward, new Vector3(-1f, 0f, 0f)));
        }

        [Test]
        public void AttackerBehind_ClassifiedAsBack()
        {
            Vector3 toAttacker = new Vector3(0f, 0f, -1f);
            Assert.AreEqual(HitBearing.Back, CombatGeometry.ClassifyBearing(Forward, toAttacker));
            Assert.IsTrue(CombatGeometry.IsBackHit(Forward, toAttacker));
        }

        [Test]
        public void BackThreshold_Is135Degrees_Inclusive()
        {
            // ちょうど 135 度：dir = (sin135, 0, cos135)
            float rad = 135f * Mathf.Deg2Rad;
            Vector3 at135 = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            Assert.IsTrue(CombatGeometry.IsBackHit(Forward, at135), "135度は背後（境界含む）。");

            // 120 度は背後ではなく側面。
            float rad120 = 120f * Mathf.Deg2Rad;
            Vector3 at120 = new Vector3(Mathf.Sin(rad120), 0f, Mathf.Cos(rad120));
            Assert.IsFalse(CombatGeometry.IsBackHit(Forward, at120), "120度は背後ではない。");
            Assert.AreEqual(HitBearing.Side, CombatGeometry.ClassifyBearing(Forward, at120));
        }

        [Test]
        public void BearingDegrees_MatchesExpectedAngles()
        {
            Assert.AreEqual(0f, CombatGeometry.BearingDegrees(Forward, new Vector3(0f, 0f, 1f)), 0.01f);
            Assert.AreEqual(90f, CombatGeometry.BearingDegrees(Forward, new Vector3(1f, 0f, 0f)), 0.01f);
            Assert.AreEqual(180f, CombatGeometry.BearingDegrees(Forward, new Vector3(0f, 0f, -1f)), 0.01f);
        }

        [Test]
        public void YComponentIgnored_OnlyXZMatters()
        {
            // Y を大きくしても XZ 成分だけで評価される（背後成分 -Z）。
            Vector3 toAttacker = new Vector3(0f, 99f, -1f);
            Assert.IsTrue(CombatGeometry.IsBackHit(Forward, toAttacker));

            // XZ がゼロ（純 Y）は「向き無し」＝安全側 Front。
            Assert.AreEqual(HitBearing.Front, CombatGeometry.ClassifyBearing(Forward, new Vector3(0f, 5f, 0f)));
            Assert.IsFalse(CombatGeometry.IsBackHit(Forward, new Vector3(0f, 5f, 0f)));
        }

        [Test]
        public void ZeroVectors_HandledSafely_NoNaN()
        {
            Assert.AreEqual(0f, CombatGeometry.BearingDegrees(Vector3.zero, Vector3.zero));
            Assert.IsFalse(float.IsNaN(CombatGeometry.BearingDegrees(Vector3.zero, new Vector3(0f, 0f, -1f))));
            Assert.AreEqual(HitBearing.Front, CombatGeometry.ClassifyBearing(Vector3.zero, new Vector3(0f, 0f, -1f)));
            Assert.AreEqual(HitBearing.Front, CombatGeometry.ClassifyBearing(Forward, Vector3.zero));
            Assert.IsFalse(CombatGeometry.IsBackHit(Vector3.zero, Vector3.zero));

            Assert.IsFalse(CombatGeometry.TryGetDirectionXZ(Vector3.zero, out Vector3 dir));
            Assert.AreEqual(Vector3.zero, dir);
        }

        [Test]
        public void DefenderToAttacker_FromAttackDirection_IsNegatedXZ()
        {
            // 攻撃が -Z 方向へ進む（前から後ろ）なら、被弾側→攻撃者は +Z（前）。
            Vector3 result = CombatGeometry.DefenderToAttackerFromAttackDirection(new Vector3(0f, 0f, -1f));
            Assert.AreEqual(0f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
            Assert.AreEqual(1f, result.z, 0.001f);

            Assert.AreEqual(Vector3.zero, CombatGeometry.DefenderToAttackerFromAttackDirection(Vector3.zero));
        }
    }
}
