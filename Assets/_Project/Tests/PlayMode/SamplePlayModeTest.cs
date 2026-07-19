using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// PlayMode テスト基盤が動作することを確認する最小のサンプル（P0-11）。
    /// 1 フレーム進めて Play ループが回ることを確かめる。
    /// </summary>
    public sealed class SamplePlayModeTest
    {
        [UnityTest]
        public IEnumerator PlayModeRunner_AdvancesOneFrame()
        {
            yield return null;
            Assert.Pass("PlayMode test runner is working.");
        }
    }
}
