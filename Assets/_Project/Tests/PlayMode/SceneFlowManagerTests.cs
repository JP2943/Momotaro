using System.Collections;
using Momotaro.Core.Logging;
using Momotaro.Infrastructure.SceneFlow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    public sealed class SceneFlowManagerTests
    {
        private sealed class NullLogSink : ILogSink
        {
            public void Write(LogLevel level, string message) { }
        }

        private GameObject _host;
        private SceneFlowManager _manager;

        [SetUp]
        public void SetUp()
        {
            GameLog.SetSink(new NullLogSink());
            _host = new GameObject("[SceneFlowHost]");
            // Single ロードで破棄されないよう常駐させ、コルーチンを継続させる。
            Object.DontDestroyOnLoad(_host);
            _manager = _host.AddComponent<SceneFlowManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            GameLog.SetSink(null);
        }

        [UnityTest]
        public IEnumerator LoadScene_WhileTransitioning_RejectsSecondRequest()
        {
            bool first = _manager.LoadScene(SceneNames.VsField, useLoadingScreen: false);
            bool second = _manager.LoadScene(SceneNames.Launcher, useLoadingScreen: false);

            Assert.IsTrue(first, "最初の要求は受理されるべき");
            Assert.IsFalse(second, "遷移中の2つ目の要求は無視されるべき");

            // 遷移完了まで待つ（固定秒待機を避け、状態で待つ）。
            float timeout = 10f;
            while (_manager.IsTransitioning && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsFalse(_manager.IsTransitioning, "遷移が完了しているべき");
            Assert.AreEqual(SceneNames.VsField, SceneManager.GetActiveScene().name,
                "最初に要求した Scene がアクティブであるべき");
        }
    }
}
