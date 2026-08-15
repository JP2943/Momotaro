using System.Collections.Generic;
using System.Reflection;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-06 受入修正：脅威対象の収集・解決ヘルパの検証（§0.2「Find* 回避」／req6 攻撃者本人への確実な対応付け）。
    /// 在圏の敵対脅威対象の収集（範囲・敵対フィルタ）と、攻撃者→脅威対象の <b>同一 Transform ルート</b> による解決を確認する。
    /// 位置的近さでなくルート同一で対応付けるため、主人公と仲間が同座標でも攻撃者本人へ帰属する。
    /// </summary>
    public sealed class ThreatTargetRegistryTests
    {
        private readonly List<Object> _spawned = new List<Object>();

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

        private sealed class TestCombatActor : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
        }

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

        [TearDown]
        public void TearDown()
        {
            PerceptionTargetRegistry.Clear();
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        // ---- Collect（在圏・敵対フィルタ） ----

        [Test]
        public void Collect_InRangeHostileOnly()
        {
            var near = new FakeThreatTarget { ActorId = 1, Faction = CombatFaction.Player, Position = new Vector3(0, 0, 3f) };
            var far = new FakeThreatTarget { ActorId = 2, Faction = CombatFaction.Player, Position = new Vector3(0, 0, 20f) };
            var ally = new FakeThreatTarget { ActorId = 3, Faction = CombatFaction.Enemy, Position = Vector3.zero };
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

        // ---- Resolve（攻撃者本人＝同一ルート） ----

        private GameObject MakeEntity(string name, CombatFaction faction, Vector3 pos, bool withActor)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            go.transform.position = pos;
            var binder = go.AddComponent<PerceptionTargetBinder>();
            SetPrivate(binder, "_faction", faction);
            PerceptionTargetRegistry.Register(binder); // EditMode は OnEnable 非実行のため明示登録。
            if (withActor)
            {
                var actor = go.AddComponent<TestCombatActor>();
                actor.Faction = faction;
            }

            return go;
        }

        [Test]
        public void Resolve_MatchesAttackerRoot_NotNearestOrOrder()
        {
            // 仲間を先に、同座標で登録：位置的近さ／登録順で選ぶ旧実装なら仲間を選びうる状況を作る。
            GameObject ally = MakeEntity("Ally", CombatFaction.Ally, Vector3.zero, withActor: false);
            GameObject player = MakeEntity("Player", CombatFaction.Player, Vector3.zero, withActor: true);

            var attacker = player.GetComponent<TestCombatActor>();
            bool ok = PerceptionTargetRegistry.TryResolveThreatTarget(attacker, out IThreatTarget resolved);

            Assert.IsTrue(ok);
            Assert.AreSame(player.GetComponent<PerceptionTargetBinder>(), resolved,
                "同座標・仲間先登録でも、攻撃者本人（同一ルート）へ帰属する。");
        }

        [Test]
        public void Resolve_ReturnsFalse_WhenAttackerRootHasNoTarget()
        {
            MakeEntity("Player", CombatFaction.Player, Vector3.zero, withActor: false); // binder のみ（actor 別ルート）
            var lone = new GameObject("EnemyAttacker");
            _spawned.Add(lone);
            var attacker = lone.AddComponent<TestCombatActor>();
            attacker.Faction = CombatFaction.Enemy;

            Assert.IsFalse(PerceptionTargetRegistry.TryResolveThreatTarget(attacker, out _),
                "攻撃者ルートに脅威対象が無ければ解決しない。");
        }

        [Test]
        public void Resolve_ReturnsFalse_ForNonComponentAttacker()
        {
            MakeEntity("Player", CombatFaction.Player, Vector3.zero, withActor: true);
            Assert.IsFalse(PerceptionTargetRegistry.TryResolveThreatTarget(new FakeActor(), out _),
                "Transform を持たない攻撃者は対応付け不可。");
        }

        private sealed class FakeActor : ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Player;
            public int FloorId => 0;
            public Vector3 WorldPosition => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
        }

        private static void SetPrivate(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }
    }
}
