using Momotaro.Core.Logging;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    public sealed class GameLogTests
    {
        private TestLogSink _sink;

        [SetUp]
        public void SetUp()
        {
            _sink = new TestLogSink();
            GameLog.SetSink(_sink);
            GameLog.ResetSuppression();
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.ResetSuppression();
            GameLog.SetSink(null); // 既定の Unity Console へ戻す
        }

        [Test]
        public void Warning_WithIdAndScene_IncludesCategoryIdAndScene()
        {
            GameLog.Warning(LogCategory.Combat, "hit missing", id: "attack_player_normal_01", scene: "SCN_VS_Field");

            Assert.AreEqual(1, _sink.Entries.Count);
            string msg = _sink.Entries[0].Message;
            StringAssert.Contains("[Combat]", msg);
            StringAssert.Contains("[id:attack_player_normal_01]", msg);
            StringAssert.Contains("[scene:SCN_VS_Field]", msg);
            StringAssert.Contains("hit missing", msg);
        }

        [Test]
        public void Warning_WithoutIdOrScene_OmitsThoseSegments()
        {
            GameLog.Warning(LogCategory.Boot, "plain message");

            string msg = _sink.Entries[0].Message;
            StringAssert.Contains("[Boot]", msg);
            StringAssert.DoesNotContain("[id:", msg);
            StringAssert.DoesNotContain("[scene:", msg);
        }

        [Test]
        public void WarningOnce_SameKey_EmitsOnlyOnce()
        {
            const string key = "companion_target_null";
            bool first = GameLog.WarningOnce(LogCategory.AI, key, "target is null");
            bool second = GameLog.WarningOnce(LogCategory.AI, key, "target is null");
            bool third = GameLog.WarningOnce(LogCategory.AI, key, "target is null");

            Assert.IsTrue(first, "初回は出力されるべき");
            Assert.IsFalse(second, "2回目は抑制されるべき");
            Assert.IsFalse(third, "3回目は抑制されるべき");
            Assert.AreEqual(1, _sink.CountOf(LogLevel.Warning));
        }

        [Test]
        public void WarningOnce_DifferentKeys_EmitEach()
        {
            GameLog.WarningOnce(LogCategory.AI, "key_a", "a");
            GameLog.WarningOnce(LogCategory.AI, "key_b", "b");

            Assert.AreEqual(2, _sink.CountOf(LogLevel.Warning));
        }

        [Test]
        public void ResetSuppression_AllowsWarningAgain()
        {
            const string key = "same_key";
            GameLog.WarningOnce(LogCategory.Save, key, "once");
            GameLog.ResetSuppression();
            bool afterReset = GameLog.WarningOnce(LogCategory.Save, key, "again");

            Assert.IsTrue(afterReset);
            Assert.AreEqual(2, _sink.CountOf(LogLevel.Warning));
        }
    }
}
