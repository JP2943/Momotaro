using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-02：認識対象レジストリ（Find* 回避。§0.2）の登録・解除・敵対関係・最寄り取得を検証する。純粋・再現可能。
    /// </summary>
    public sealed class PerceptionTargetRegistryTests
    {
        private sealed class FakeTarget : IPerceptionTarget
        {
            public int ActorId { get; set; }
            public CombatFaction Faction { get; set; }
            public Vector3 Position { get; set; }
            public bool IsActive { get; set; } = true;
        }

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

        [TearDown]
        public void TearDown() => PerceptionTargetRegistry.Clear();

        [Test]
        public void RegisterUnregister_UpdatesCount()
        {
            var t = new FakeTarget { Faction = CombatFaction.Player };
            PerceptionTargetRegistry.Register(t);
            PerceptionTargetRegistry.Register(t); // 重複無視
            Assert.AreEqual(1, PerceptionTargetRegistry.Count);
            PerceptionTargetRegistry.Unregister(t);
            Assert.AreEqual(0, PerceptionTargetRegistry.Count);
        }

        [Test]
        public void Hostility_EnemySeesPlayerAndAlly_NotEnemyOrNeutral()
        {
            Assert.IsTrue(PerceptionTargetRegistry.IsHostile(CombatFaction.Enemy, CombatFaction.Player));
            Assert.IsTrue(PerceptionTargetRegistry.IsHostile(CombatFaction.Enemy, CombatFaction.Ally));
            Assert.IsFalse(PerceptionTargetRegistry.IsHostile(CombatFaction.Enemy, CombatFaction.Enemy));
            Assert.IsFalse(PerceptionTargetRegistry.IsHostile(CombatFaction.Enemy, CombatFaction.Neutral));
            Assert.IsTrue(PerceptionTargetRegistry.IsHostile(CombatFaction.Player, CombatFaction.Enemy));
        }

        [Test]
        public void NearestHostile_PicksClosestActivePlayer()
        {
            var near = new FakeTarget { Faction = CombatFaction.Player, Position = new Vector3(0, 0, 3f) };
            var far = new FakeTarget { Faction = CombatFaction.Player, Position = new Vector3(0, 0, 9f) };
            PerceptionTargetRegistry.Register(far);
            PerceptionTargetRegistry.Register(near);

            bool found = PerceptionTargetRegistry.TryGetNearestHostile(Vector3.zero, CombatFaction.Enemy, out IPerceptionTarget got);
            Assert.IsTrue(found);
            Assert.AreSame(near, got, "最寄りの対象を返す。");
        }

        [Test]
        public void NearestHostile_IgnoresInactiveAndNonHostile()
        {
            var inactive = new FakeTarget { Faction = CombatFaction.Player, Position = new Vector3(0, 0, 1f), IsActive = false };
            var ally = new FakeTarget { Faction = CombatFaction.Enemy, Position = new Vector3(0, 0, 2f) }; // 敵→敵は非対象
            PerceptionTargetRegistry.Register(inactive);
            PerceptionTargetRegistry.Register(ally);

            bool found = PerceptionTargetRegistry.TryGetNearestHostile(Vector3.zero, CombatFaction.Enemy, out _);
            Assert.IsFalse(found, "非アクティブ・非敵対は対象外。");
        }
    }
}
