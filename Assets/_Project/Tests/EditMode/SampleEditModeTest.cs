using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// EditMode テスト基盤が動作することを確認する最小のサンプル（P0-11）。
    /// 実装対象のロジックは各機能別テストで検証する。
    /// </summary>
    public sealed class SampleEditModeTest
    {
        [Test]
        public void EditModeRunner_IsAvailable()
        {
            Assert.Pass("EditMode test runner is working.");
        }
    }
}
