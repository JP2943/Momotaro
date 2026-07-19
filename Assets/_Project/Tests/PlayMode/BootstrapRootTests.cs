using System.Collections;
using Momotaro.Core.Logging;
using Momotaro.Infrastructure.Bootstrap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    public sealed class BootstrapRootTests
    {
        // Console を汚さないための捨てログ先。
        private sealed class NullLogSink : ILogSink
        {
            public void Write(LogLevel level, string message) { }
        }

        [SetUp]
        public void SetUp()
        {
            GameLog.SetSink(new NullLogSink());
        }

        [TearDown]
        public void TearDown()
        {
            if (BootstrapRoot.HasInstance)
            {
                Object.Destroy(BootstrapRoot.Instance.gameObject);
            }

            GameLog.SetSink(null);
        }

        [UnityTest]
        public IEnumerator FirstBootstrapRoot_BecomesInstance_AndSucceeds()
        {
            var go = new GameObject("Root1");
            go.AddComponent<BootstrapRoot>();

            // Awake と Start を通す。
            yield return null;

            Assert.IsTrue(BootstrapRoot.HasInstance);
            Assert.AreSame(go.GetComponent<BootstrapRoot>(), BootstrapRoot.Instance);
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded, "既定サービスは初期化に成功するべき");
        }

        [UnityTest]
        public IEnumerator DuplicateBootstrapRoot_IsDestroyed_OnlyOneRemains()
        {
            var first = new GameObject("Root1");
            first.AddComponent<BootstrapRoot>();
            yield return null;

            BootstrapRoot firstInstance = BootstrapRoot.Instance;

            var second = new GameObject("Root2");
            second.AddComponent<BootstrapRoot>();
            // 破棄が反映されるまで待つ。
            yield return null;

            Assert.AreSame(firstInstance, BootstrapRoot.Instance, "先発が Instance のまま残るべき");
            Assert.IsTrue(second == null || second.GetComponent<BootstrapRoot>() == null,
                "後発の BootstrapRoot は破棄されるべき");
        }
    }
}
