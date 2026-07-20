using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class GuardMovementAndFacingTests
    {
        [Test]
        public void GuardSpeed_IsFullWhenNotGuarding()
        {
            Assert.AreEqual(1f, GuardMovement.SpeedMultiplier(false, 0.4f));
        }

        [Test]
        public void GuardSpeed_IsReducedWhenGuarding()
        {
            Assert.AreEqual(0.4f, GuardMovement.SpeedMultiplier(true, 0.4f), 1e-6f);
        }

        [Test]
        public void FacingLocked_KeepsCurrentRegardlessOfInput()
        {
            // ロック中は別方向入力でも向きが変わらない（ガード中の向き固定）。
            var result = FacingUpdate.Resolve(locked: true, new Vector2(1f, 0f), FacingDirection.Up, 0.2f);
            Assert.AreEqual(FacingDirection.Up, result);
        }

        [Test]
        public void FacingUnlocked_ResolvesFromInput()
        {
            var result = FacingUpdate.Resolve(locked: false, new Vector2(1f, 0f), FacingDirection.Up, 0.2f);
            Assert.AreEqual(FacingDirection.Right, result);
        }
    }
}
