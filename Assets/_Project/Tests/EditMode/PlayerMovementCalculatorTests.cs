using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class PlayerMovementCalculatorTests
    {
        private const float Speed = 5f;

        [Test]
        public void ZeroInput_ProducesZeroVelocity()
        {
            Vector3 v = PlayerMovementCalculator.ToPlanarVelocity(Vector2.zero, Speed);
            Assert.AreEqual(Vector3.zero, v);
        }

        [Test]
        public void Input_MapsXToWorldX_AndYToWorldZ_WithNoYComponent()
        {
            Vector3 right = PlayerMovementCalculator.ToPlanarVelocity(new Vector2(1f, 0f), Speed);
            Vector3 forward = PlayerMovementCalculator.ToPlanarVelocity(new Vector2(0f, 1f), Speed);

            Assert.AreEqual(new Vector3(Speed, 0f, 0f), right);
            Assert.AreEqual(new Vector3(0f, 0f, Speed), forward);
            Assert.AreEqual(0f, right.y);
            Assert.AreEqual(0f, forward.y);
        }

        [Test]
        public void DiagonalInput_IsNotFasterThanCardinal()
        {
            Vector3 cardinal = PlayerMovementCalculator.ToPlanarVelocity(new Vector2(1f, 0f), Speed);
            Vector3 diagonal = PlayerMovementCalculator.ToPlanarVelocity(new Vector2(1f, 1f), Speed);

            Assert.AreEqual(Speed, cardinal.magnitude, 1e-4f);
            Assert.AreEqual(Speed, diagonal.magnitude, 1e-4f, "斜め入力でも速度は一定であるべき");
            Assert.AreEqual(0f, diagonal.y);
        }

        [Test]
        public void SmallStickInput_IsNotNormalizedUp()
        {
            // 大きさ 1 未満の入力はそのまま速度に反映（Stick の弱い倒し込み）。
            Vector3 v = PlayerMovementCalculator.ToPlanarVelocity(new Vector2(0.5f, 0f), Speed);
            Assert.AreEqual(Speed * 0.5f, v.magnitude, 1e-4f);
        }

        [Test]
        public void Integration_IsTimeStepIndependent()
        {
            var input = new Vector2(1f, 0.5f);
            Vector3 velocity = PlayerMovementCalculator.ToPlanarVelocity(input, Speed);

            // 異なる dt でも合計変位は同じ（フレームレート非依存）。
            const float total = 1f;
            Vector3 dispA = IntegrateDisplacement(velocity, 0.02f, total);
            Vector3 dispB = IntegrateDisplacement(velocity, 0.1f, total);

            Assert.AreEqual(dispA.x, dispB.x, 1e-4f);
            Assert.AreEqual(dispA.z, dispB.z, 1e-4f);
            Assert.AreEqual(0f, dispA.y);
        }

        private static Vector3 IntegrateDisplacement(Vector3 velocity, float dt, float totalTime)
        {
            Vector3 pos = Vector3.zero;
            for (float t = 0f; t < totalTime - 1e-6f; t += dt)
            {
                pos += velocity * dt;
            }

            return pos;
        }
    }
}
