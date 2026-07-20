using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class CameraAndVisualTests
    {
        [Test]
        public void CameraFollow_AddsOffsetToTarget()
        {
            var pos = CameraFollowMath.ComputePosition(new Vector3(3f, 0f, 5f), new Vector3(0f, 12f, -8f));
            Assert.AreEqual(new Vector3(3f, 12f, -3f), pos);
        }

        [Test]
        public void VisualNames_MapStatesAndFacingToClipNames()
        {
            Assert.AreEqual("AN_Player_Idle_Down", PlayerVisualNames.ClipName(PlayerState.Idle, FacingDirection.Down));
            Assert.AreEqual("AN_Player_Move_Left", PlayerVisualNames.ClipName(PlayerState.Move, FacingDirection.Left));
            Assert.AreEqual("AN_Player_Guard_Up", PlayerVisualNames.ClipName(PlayerState.GuardIdle, FacingDirection.Up));
            Assert.AreEqual("AN_Player_Guard_Right", PlayerVisualNames.ClipName(PlayerState.GuardMove, FacingDirection.Right));
        }
    }
}
