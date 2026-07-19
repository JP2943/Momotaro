using System.Collections.Generic;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Modes;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    public sealed class GameModeServiceTests
    {
        // IGameModeListener の記録用実装。
        private sealed class RecordingListener : IGameModeListener
        {
            public readonly List<GameModeChanged> Received = new List<GameModeChanged>();

            public void OnModeChanged(GameModeChanged change)
            {
                Received.Add(change);
            }
        }

        // 通知中に自分自身を購読解除するリスナー。
        private sealed class SelfRemovingListener : IGameModeListener
        {
            private readonly IGameModeService _service;
            public int CallCount { get; private set; }

            public SelfRemovingListener(IGameModeService service)
            {
                _service = service;
            }

            public void OnModeChanged(GameModeChanged change)
            {
                CallCount++;
                _service.RemoveListener(this);
            }
        }

        [SetUp]
        public void SetUp()
        {
            // Info ログを捨て、Console を汚さない。
            GameLog.SetSink(new TestLogSink());
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.SetSink(null);
        }

        [Test]
        public void ChangeMode_ToDifferentMode_RaisesEventWithPrevAndCurrent()
        {
            var service = new GameModeService(GameMode.Loading);
            GameModeChanged captured = default;
            int count = 0;
            service.ModeChanged += c => { captured = c; count++; };

            bool changed = service.ChangeMode(GameMode.Exploration);

            Assert.IsTrue(changed);
            Assert.AreEqual(1, count);
            Assert.AreEqual(GameMode.Loading, captured.Previous);
            Assert.AreEqual(GameMode.Exploration, captured.Current);
            Assert.AreEqual(GameMode.Exploration, service.Current);
        }

        [Test]
        public void ChangeMode_ToSameMode_DoesNotRaiseEvent()
        {
            var service = new GameModeService(GameMode.Combat);
            int count = 0;
            service.ModeChanged += _ => count++;

            bool changed = service.ChangeMode(GameMode.Combat);

            Assert.IsFalse(changed);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Listener_ReceivesChange_UntilRemoved()
        {
            var service = new GameModeService(GameMode.Loading);
            var listener = new RecordingListener();
            service.AddListener(listener);

            service.ChangeMode(GameMode.Exploration);
            service.RemoveListener(listener);
            service.ChangeMode(GameMode.Combat);

            Assert.AreEqual(1, listener.Received.Count, "解除後の変更は届かないべき");
            Assert.AreEqual(GameMode.Exploration, listener.Received[0].Current);
        }

        [Test]
        public void AddListener_SameListenerTwice_RegistersOnce()
        {
            var service = new GameModeService(GameMode.Loading);
            var listener = new RecordingListener();
            service.AddListener(listener);
            service.AddListener(listener);

            service.ChangeMode(GameMode.Event);

            Assert.AreEqual(1, listener.Received.Count);
        }

        [Test]
        public void Notify_ListenerRemovingItself_DoesNotSkipOtherListeners()
        {
            var service = new GameModeService(GameMode.Loading);
            var selfRemoving = new SelfRemovingListener(service);
            var other = new RecordingListener();
            service.AddListener(selfRemoving);
            service.AddListener(other);

            // 通知中に selfRemoving が自身を解除しても、後続の other は通知を受けるべき。
            service.ChangeMode(GameMode.Exploration);

            Assert.AreEqual(1, selfRemoving.CallCount);
            Assert.AreEqual(1, other.Received.Count, "自己解除で後続リスナーが飛ばされてはいけない");

            // 解除済みなので次回は selfRemoving に届かない。
            service.ChangeMode(GameMode.Combat);
            Assert.AreEqual(1, selfRemoving.CallCount);
            Assert.AreEqual(2, other.Received.Count);
        }

        [Test]
        public void CanPause_ReflectsCurrentModeProfile()
        {
            var service = new GameModeService(GameMode.Exploration);
            Assert.IsTrue(service.CanPause, "探索はポーズ可");

            service.ChangeMode(GameMode.Loading);
            Assert.IsFalse(service.CanPause, "読込中はポーズ不可");
        }

        [Test]
        public void Catalog_AllSevenModes_HaveProfileWithActionMap()
        {
            foreach (GameMode mode in System.Enum.GetValues(typeof(GameMode)))
            {
                GameModeProfile profile = GameModeCatalog.GetProfile(mode);
                Assert.AreEqual(mode, profile.Mode);
                Assert.IsFalse(string.IsNullOrEmpty(profile.ActionMap), mode + " に ActionMap が無い");
            }
        }
    }
}
