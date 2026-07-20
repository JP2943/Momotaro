using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class FacingResolverTests
    {
        private const float Deadzone = 0.2f;

        [Test]
        public void CardinalInputs_ResolveToMatchingDirection()
        {
            Assert.AreEqual(FacingDirection.Right, FacingResolver.Resolve(new Vector2(1f, 0f), FacingDirection.Down, Deadzone));
            Assert.AreEqual(FacingDirection.Left, FacingResolver.Resolve(new Vector2(-1f, 0f), FacingDirection.Down, Deadzone));
            Assert.AreEqual(FacingDirection.Up, FacingResolver.Resolve(new Vector2(0f, 1f), FacingDirection.Down, Deadzone));
            Assert.AreEqual(FacingDirection.Down, FacingResolver.Resolve(new Vector2(0f, -1f), FacingDirection.Up, Deadzone));
        }

        [Test]
        public void DiagonalInput_UsesDominantAxis()
        {
            Assert.AreEqual(FacingDirection.Right, FacingResolver.Resolve(new Vector2(0.9f, 0.3f), FacingDirection.Down, Deadzone));
            Assert.AreEqual(FacingDirection.Up, FacingResolver.Resolve(new Vector2(0.3f, 0.9f), FacingDirection.Down, Deadzone));
            Assert.AreEqual(FacingDirection.Left, FacingResolver.Resolve(new Vector2(-0.8f, 0.5f), FacingDirection.Down, Deadzone));
            Assert.AreEqual(FacingDirection.Down, FacingResolver.Resolve(new Vector2(0.2f, -0.9f), FacingDirection.Up, Deadzone));
        }

        [Test]
        public void ExactTie_PrefersHorizontal()
        {
            Assert.AreEqual(FacingDirection.Right, FacingResolver.Resolve(new Vector2(0.7f, 0.7f), FacingDirection.Down, Deadzone));
            Assert.AreEqual(FacingDirection.Left, FacingResolver.Resolve(new Vector2(-0.5f, 0.5f), FacingDirection.Down, Deadzone));
            Assert.AreEqual(FacingDirection.Left, FacingResolver.Resolve(new Vector2(-0.5f, -0.5f), FacingDirection.Up, Deadzone));
        }

        [Test]
        public void BelowDeadzone_HoldsCurrentDirection()
        {
            Assert.AreEqual(FacingDirection.Up, FacingResolver.Resolve(new Vector2(0.1f, 0.1f), FacingDirection.Up, Deadzone));
            Assert.AreEqual(FacingDirection.Left, FacingResolver.Resolve(Vector2.zero, FacingDirection.Left, Deadzone));
        }

        [Test]
        public void JustAboveDeadzone_UpdatesDirection()
        {
            // (0.2, 0) は大きさ 0.2 = deadzone。sqrMagnitude(0.04) < deadzone^2(0.04) は偽なので更新される。
            var result = FacingResolver.Resolve(new Vector2(0.25f, 0f), FacingDirection.Down, Deadzone);
            Assert.AreEqual(FacingDirection.Right, result);
        }
    }
}
