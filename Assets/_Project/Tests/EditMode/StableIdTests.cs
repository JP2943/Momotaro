using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    public sealed class StableIdTests
    {
        [SetUp]
        public void SetUp()
        {
            // Registry は不正/重複時に GameLog.Error を出すため、捕捉用 sink に差し替えて
            // Test Runner が予期せぬ LogError で失敗しないようにする。
            GameLog.SetSink(new TestLogSink());
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.SetSink(null);
        }

        [TestCase("attack_player_normal_01", true)]
        [TestCase("enemy_vs_melee_01", true)]
        [TestCase("a", true)]
        [TestCase("Attack_Player", false)]   // 大文字
        [TestCase("attack player", false)]   // 空白
        [TestCase("1attack", false)]          // 先頭数字
        [TestCase("攻撃_01", false)]           // 日本語
        [TestCase("", false)]                  // 空
        [TestCase("attack-player", false)]    // ハイフン
        public void IsValid_ReturnsExpected(string value, bool expected)
        {
            Assert.AreEqual(expected, StableIdFormat.IsValid(value));
        }

        [Test]
        public void StableId_Equality_IsByValue()
        {
            var a = new StableId("skill_player_guard_01");
            var b = new StableId("skill_player_guard_01");
            var c = new StableId("skill_player_guard_02");

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        [Test]
        public void Registry_UniqueValidIds_RegisterSuccessfully()
        {
            var registry = new StableIdRegistry();
            Assert.IsTrue(registry.TryRegister(new StableId("enemy_vs_melee_01"), "EnemyA", out _));
            Assert.IsTrue(registry.TryRegister(new StableId("enemy_vs_melee_02"), "EnemyB", out _));
            Assert.AreEqual(2, registry.Count);
        }

        [Test]
        public void Registry_DuplicateId_IsRejected()
        {
            var registry = new StableIdRegistry();
            registry.TryRegister(new StableId("reward_vs_hidden_chest"), "ChestA", out _);

            bool ok = registry.TryRegister(new StableId("reward_vs_hidden_chest"), "ChestB", out string error);

            Assert.IsFalse(ok);
            Assert.IsNotNull(error);
            StringAssert.Contains("Duplicate", error);
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void Registry_InvalidFormat_IsRejected()
        {
            var registry = new StableIdRegistry();

            bool ok = registry.TryRegister(new StableId("Invalid Format"), "Owner", out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("Invalid", error);
            Assert.AreEqual(0, registry.Count);
        }
    }
}
