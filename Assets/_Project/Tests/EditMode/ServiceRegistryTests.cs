using System.Collections.Generic;
using Momotaro.Core.Logging;
using Momotaro.Infrastructure.Bootstrap;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    public sealed class ServiceRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            // Boot ログで Console を汚さないよう捕捉用 sink に差し替える。
            GameLog.SetSink(new TestLogSink());
            GameLog.ResetSuppression();
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.SetSink(null);
        }

        [Test]
        public void InitializeAll_AllSucceed_InitializesInRegistrationOrder()
        {
            var order = new List<string>();
            var registry = new ServiceRegistry();
            registry.Register(new StubGameService("A", ServiceInitResult.Ok(), order));
            registry.Register(new StubGameService("B", ServiceInitResult.Ok(), order));
            registry.Register(new StubGameService("C", ServiceInitResult.Ok(), order));

            bool ok = registry.InitializeAll();

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, order);
        }

        [Test]
        public void InitializeAll_CriticalFailure_StopsAndReturnsFalse()
        {
            var order = new List<string>();
            var registry = new ServiceRegistry();
            registry.Register(new StubGameService("A", ServiceInitResult.Ok(), order));
            registry.Register(new StubGameService("B", ServiceInitResult.Fail("boom", isCritical: true), order));
            registry.Register(new StubGameService("C", ServiceInitResult.Ok(), order));

            bool ok = registry.InitializeAll();

            Assert.IsFalse(ok);
            CollectionAssert.AreEqual(new[] { "A", "B" }, order, "Critical 失敗後は C を初期化しない");
        }

        [Test]
        public void InitializeAll_NonCriticalFailure_ContinuesAndReturnsTrue()
        {
            var order = new List<string>();
            var registry = new ServiceRegistry();
            registry.Register(new StubGameService("A", ServiceInitResult.Fail("minor", isCritical: false), order));
            registry.Register(new StubGameService("B", ServiceInitResult.Ok(), order));

            bool ok = registry.InitializeAll();

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { "A", "B" }, order);
        }

        [Test]
        public void InitializeAll_ServiceThrows_TreatedAsCriticalFailure()
        {
            var order = new List<string>();
            var registry = new ServiceRegistry();
            registry.Register(new StubGameService("A", ServiceInitResult.Ok(), order, throws: true));
            registry.Register(new StubGameService("B", ServiceInitResult.Ok(), order));

            bool ok = registry.InitializeAll();

            Assert.IsFalse(ok);
            CollectionAssert.AreEqual(new[] { "A" }, order);
        }
    }
}
