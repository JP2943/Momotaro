using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class PlayerInputStateTests
    {
        [Test]
        public void SetMove_WhenActive_ReflectsValue()
        {
            var state = new PlayerInputState();
            state.SetMove(new Vector2(1f, -0.5f));
            Assert.AreEqual(new Vector2(1f, -0.5f), state.Move);
        }

        [Test]
        public void Guard_PressAndRelease_RaisesEdgeEventsOnce()
        {
            var state = new PlayerInputState();
            int started = 0;
            int canceled = 0;
            state.GuardStarted += () => started++;
            state.GuardCanceled += () => canceled++;

            state.SetGuard(true);
            state.SetGuard(true); // 保持中の再通知は起きない
            Assert.IsTrue(state.GuardHeld);
            Assert.AreEqual(1, started);

            state.SetGuard(false);
            state.SetGuard(false);
            Assert.IsFalse(state.GuardHeld);
            Assert.AreEqual(1, canceled);
        }

        [Test]
        public void SetActive_False_ZeroesMoveAndReleasesGuard()
        {
            var state = new PlayerInputState();
            int canceled = 0;
            state.GuardCanceled += () => canceled++;

            state.SetMove(new Vector2(1f, 1f));
            state.SetGuard(true);

            state.SetActive(false);

            Assert.AreEqual(Vector2.zero, state.Move, "ゲート閉で Move はゼロ");
            Assert.IsFalse(state.GuardHeld, "ゲート閉で Guard は解除");
            Assert.AreEqual(1, canceled, "解除通知が1回発火");
        }

        [Test]
        public void WhileInactive_InputIsIgnored()
        {
            var state = new PlayerInputState();
            state.SetActive(false);

            state.SetMove(new Vector2(1f, 0f));
            int started = 0;
            state.GuardStarted += () => started++;
            state.SetGuard(true);

            Assert.AreEqual(Vector2.zero, state.Move);
            Assert.IsFalse(state.GuardHeld);
            Assert.AreEqual(0, started, "非アクティブ中は Guard 押下も無効");
        }

        [Test]
        public void SetActive_ReenabledThenInput_Works()
        {
            var state = new PlayerInputState();
            state.SetActive(false);
            state.SetActive(true);

            state.SetMove(new Vector2(0.3f, 0.4f));
            state.SetGuard(true);

            Assert.AreEqual(new Vector2(0.3f, 0.4f), state.Move);
            Assert.IsTrue(state.GuardHeld);
        }
    }
}
