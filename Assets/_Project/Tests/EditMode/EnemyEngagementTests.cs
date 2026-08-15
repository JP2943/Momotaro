using Momotaro.Gameplay.Enemy.Locomotion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-03：活動範囲 <see cref="ActivityBounds"/> と接近計算 <see cref="ApproachCalculator"/> を検証する（§5）。純粋・再現可能。
    /// </summary>
    public sealed class EnemyEngagementTests
    {
        [Test]
        public void ActivityBounds_IsOutside_UsesXZDistance()
        {
            var b = new ActivityBounds(new Vector3(0, 5f, 0), 12f);
            Assert.IsFalse(b.IsOutside(new Vector3(11.9f, 100f, 0f)), "Y は無視。半径内。");
            Assert.IsTrue(b.IsOutside(new Vector3(12.1f, 0f, 0f)), "半径超過で範囲外。");
            Assert.AreEqual(3f, b.DistanceFromCenter(new Vector3(3f, 9f, 0f)), 1e-4f);
        }

        [Test]
        public void DesiredVelocity_ZeroWithinStopRadius_ElseTowardTargetAtSpeed()
        {
            Vector3 self = Vector3.zero;
            Vector3 near = new Vector3(0.05f, 0, 0);
            Assert.AreEqual(Vector3.zero, ApproachCalculator.DesiredVelocity(self, near, 3f, 0.1f), "停止半径以内は動かない。");

            Vector3 far = new Vector3(5f, 0, 0);
            Vector3 v = ApproachCalculator.DesiredVelocity(self, far, 3f, 0.1f);
            Assert.AreEqual(3f, v.magnitude, 1e-4f, "速度の大きさは moveSpeed。");
            Assert.Greater(v.x, 0f, "対象方向（+X）へ向かう。");
            Assert.AreEqual(0f, v.y, 1e-6f, "XZ のみ。");
        }

        [Test]
        public void InStopBand_And_TooClose()
        {
            Vector3 self = Vector3.zero;
            Assert.IsTrue(ApproachCalculator.InStopBand(self, new Vector3(1.2f, 0, 0), stopDistance: 1.6f, tooCloseDistance: 0.96f));
            Assert.IsFalse(ApproachCalculator.InStopBand(self, new Vector3(2.0f, 0, 0), 1.6f, 0.96f), "遠すぎは帯外。");
            Assert.IsTrue(ApproachCalculator.IsTooClose(self, new Vector3(0.5f, 0, 0), 0.96f));
            Assert.IsFalse(ApproachCalculator.IsTooClose(self, new Vector3(1.2f, 0, 0), 0.96f));
        }

        [Test]
        public void BackAwayTarget_MovesAwayFromTarget()
        {
            Vector3 self = new Vector3(1f, 0, 0);
            Vector3 target = Vector3.zero;
            Vector3 back = ApproachCalculator.BackAwayTarget(self, target, 2f);
            Assert.Greater(back.x, self.x, "対象（原点）の反対（+X）へ退避。");
        }
    }
}
