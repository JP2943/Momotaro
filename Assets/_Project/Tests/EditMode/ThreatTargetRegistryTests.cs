using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-06：脅威対象の収集・解決ヘルパ（<see cref="PerceptionTargetRegistry"/> 拡張。§0.2「Find* 回避」）の検証。
    /// 在圏の敵対脅威対象の収集（範囲・敵対フィルタ）と、攻撃者→脅威対象の解決（同陣営・最近傍）を Fake で確認する。
    /// </summary>
    public sealed class ThreatTargetRegistryTests
    {
        private sealed class FakeThreatTarget : IThreatTarget
        {
            public int ActorId { get; set; }
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public Vector3 Position { get; set; }
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat { get; set; }
            public float AcquiredThreatMultiplier { get; set; } = 1f;
        }

        private sealed class FakeAttacker : ICombatActor
        {
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public int FloorId => 0;
            public Vector3 WorldPosition { get; set; }
            public Vector3 Forward => Vector3.forward;
        }

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

        [TearDown]
        public void TearDown() => PerceptionTargetRegistry.Clear();

        [Test]
        public void Collect_InRangeHostileOnly()
        {
            var near = new FakeThreatTarget { ActorId = 1, Faction = CombatFaction.Player, Position = new Vector3(0, 0, 3f) };
            var far = new FakeThreatTarget { ActorId = 2, Faction = CombatFaction.Player, Position = new Vector3(0, 0, 20f) };
            var ally = new FakeThreatTarget { ActorId = 3, Faction = CombatFaction.Enemy, Position = Vector3.zero }; // 敵→敵は非対象
            PerceptionTargetRegistry.Register(near);
            PerceptionTargetRegistry.Register(far);
            PerceptionTargetRegistry.Register(ally);

            var buffer = new List<IThreatTarget>();
            PerceptionTargetRegistry.CollectHostileThreatTargets(Vector3.zero, CombatFaction.Enemy, 10f, buffer);

            Assert.AreEqual(1, buffer.Count, "在圏（10m 以内）の敵対のみ。");
            Assert.AreSame(near, buffer[0]);
        }

        [Test]
        public void Collect_UnlimitedRange_WhenMaxRangeZero()
        {
            var far = new FakeThreatTarget { ActorId = 2, Faction = CombatFaction.Player, Position = new Vector3(0, 0, 50f) };
            PerceptionTargetRegistry.Register(far);

            var buffer = new List<IThreatTarget>();
            PerceptionTargetRegistry.CollectHostileThreatTargets(Vector3.zero, CombatFaction.Enemy, 0f, buffer);
            Assert.AreEqual(1, buffer.Count, "maxRange<=0 は距離無制限。");
        }

        [Test]
        public void Collect_ClearsBufferFirst()
        {
            var buffer = new List<IThreatTarget> { null, null };
            PerceptionTargetRegistry.CollectHostileThreatTargets(Vector3.zero, CombatFaction.Enemy, 10f, buffer);
            Assert.AreEqual(0, buffer.Count, "先頭で Clear する。");
        }

        [Test]
        public void Resolve_AttackerToNearestSameFactionTarget()
        {
            var p1 = new FakeThreatTarget { ActorId = 1, Faction = CombatFaction.Player, Position = new Vector3(0, 0, 0f) };
            var p2 = new FakeThreatTarget { ActorId = 2, Faction = CombatFaction.Player, Position = new Vector3(0, 0, 10f) };
            var enemy = new FakeThreatTarget { ActorId = 3, Faction = CombatFaction.Enemy, Position = new Vector3(0, 0, 0.1f) };
            PerceptionTargetRegistry.Register(p1);
            PerceptionTargetRegistry.Register(p2);
            PerceptionTargetRegistry.Register(enemy);

            var attacker = new FakeAttacker { Faction = CombatFaction.Player, WorldPosition = new Vector3(0, 0, 0.2f) };
            bool ok = PerceptionTargetRegistry.TryResolveThreatTarget(attacker, out IThreatTarget resolved);

            Assert.IsTrue(ok);
            Assert.AreSame(p1, resolved, "同陣営で最も近い脅威対象へ帰属。");
        }

        [Test]
        public void Resolve_ReturnsFalse_WhenNoSameFaction()
        {
            var enemy = new FakeThreatTarget { ActorId = 3, Faction = CombatFaction.Enemy, Position = Vector3.zero };
            PerceptionTargetRegistry.Register(enemy);
            var attacker = new FakeAttacker { Faction = CombatFaction.Player, WorldPosition = Vector3.zero };
            Assert.IsFalse(PerceptionTargetRegistry.TryResolveThreatTarget(attacker, out _));
        }
    }
}
