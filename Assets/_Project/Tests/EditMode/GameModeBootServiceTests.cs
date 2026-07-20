using Momotaro.Core.Logging;
using Momotaro.Gameplay.Modes;
using Momotaro.Infrastructure.Bootstrap;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    public sealed class GameModeBootServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            GameLog.SetSink(new TestLogSink());
            GameModeProvider.Current = null;
        }

        [TearDown]
        public void TearDown()
        {
            GameModeProvider.Current = null;
            GameLog.SetSink(null);
        }

        [Test]
        public void Initialize_InjectsServiceIntoProvider()
        {
            var boot = new GameModeBootService();
            boot.Initialize();

            Assert.AreSame(boot.Modes, GameModeProvider.Current);
        }

        [Test]
        public void Dispose_ClearsProvider()
        {
            var boot = new GameModeBootService();
            boot.Initialize();
            boot.Dispose();

            Assert.IsNull(GameModeProvider.Current);
        }

        [Test]
        public void Provider_CanRequestModeChange()
        {
            var boot = new GameModeBootService();
            boot.Initialize();

            GameModeProvider.Current.ChangeMode(GameMode.Exploration);

            Assert.AreEqual(GameMode.Exploration, boot.Modes.Current);
            boot.Dispose();
        }
    }
}
