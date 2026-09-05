using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-03：仲間が敵の認識・ヘイト候補として載ること（<see cref="CompanionThreatBinder"/> と索敵
    /// <see cref="CompanionTargetTracker"/>）を検証する。<b>敵 AI を書き換えずに</b>候補へ追加できること、
    /// ダウン・退場で即座に脅威 0 になること、無効化で登録・対象参照を残さないことを固定する。
    /// </summary>
    public sealed class CompanionThreatBinderTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
            PerceptionTargetRegistry.Clear();
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            System.Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { f.SetValue(target, value); return; }
                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        private sealed class FakeEnemyTarget : MonoBehaviour, Momotaro.Gameplay.Enemy.Threat.IThreatTarget
        {
            public int ActorId => GetInstanceID();
            public CombatFaction Faction => CombatFaction.Enemy;
            public Vector3 Position => transform.position;
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat => 0f;
            public float AcquiredThreatMultiplier => 1f;
        }

        private FakeEnemyTarget MakeEnemy(Vector3 position)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = position;
            var target = go.AddComponent<FakeEnemyTarget>();
            PerceptionTargetRegistry.Register(target);
            return target;
        }

        private (CompanionActor actor, CompanionThreatBinder binder) MakeCompanion(Vector3 position = default)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;
            var actor = go.AddComponent<CompanionActor>();
            actor.ResetState(CompanionState.Follow);
            var binder = go.AddComponent<CompanionThreatBinder>();
            binder.Bind(actor);
            InvokePrivate(binder, "OnEnable");
            return (actor, binder);
        }

        private CompanionData MakeData(float baseThreat, float multiplier)
        {
            var d = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(d);
            SetPrivateField(d, "_baseThreat", baseThreat);
            SetPrivateField(d, "_acquiredThreatMultiplier", multiplier);
            return d;
        }

        // ---- 敵から狙われる側 ----

        [Test]
        public void Companion_IsRegisteredAsAllyTarget()
        {
            (CompanionActor _, CompanionThreatBinder binder) = MakeCompanion();

            Assert.AreEqual(1, PerceptionTargetRegistry.Count, "有効化で自己登録する。");
            Assert.AreEqual(CombatFaction.Ally, binder.Faction);
        }

        [Test]
        public void EnemyTreatsCompanionAsHostile_WithoutAiChanges()
        {
            MakeCompanion(new Vector3(0f, 0f, 3f));

            // 敵側の既存 API（書き換えていない）から、仲間が敵対対象として見えること。
            bool found = PerceptionTargetRegistry.TryGetNearestHostile(
                Vector3.zero, CombatFaction.Enemy, out IPerceptionTarget nearest);

            Assert.IsTrue(found, "敵から見て仲間は敵対対象になる。");
            Assert.AreEqual(CombatFaction.Ally, nearest.Faction);
        }

        [Test]
        public void ThreatProfile_ComesFromData()
        {
            (CompanionActor actor, CompanionThreatBinder binder) = MakeCompanion();
            actor.SetData(MakeData(baseThreat: 0f, multiplier: 1.5f));

            Assert.AreEqual(0f, binder.BaseThreat, 1e-4f, "仲間の基礎ヘイトは 0（主人公=50 と対比）。");
            Assert.AreEqual(1.5f, binder.AcquiredThreatMultiplier, 1e-4f, "犬は獲得ヘイト ×1.5。");
        }

        [Test]
        public void ThreatProfile_FallsBackWithoutData()
        {
            (CompanionActor _, CompanionThreatBinder binder) = MakeCompanion();

            Assert.AreEqual(CompanionThreatBinder.DefaultBaseThreat, binder.BaseThreat, 1e-4f);
            Assert.AreEqual(CompanionThreatBinder.DefaultAcquiredThreatMultiplier, binder.AcquiredThreatMultiplier, 1e-4f);
        }

        [TestCase(CompanionState.Down)]
        [TestCase(CompanionState.Away)]
        public void DownOrAway_MakesThreatInactive(CompanionState state)
        {
            (CompanionActor actor, CompanionThreatBinder binder) = MakeCompanion();
            Assert.IsTrue(binder.IsActive, "前提：通常時は有効。");

            actor.ResetState(state);

            Assert.IsTrue(binder.IsDown, state + " は脅威 0 として扱う。");
            Assert.IsFalse(binder.IsActive, "敵が新規に捕捉しない。");
        }

        [Test]
        public void Disable_UnregistersFromRegistry()
        {
            (CompanionActor _, CompanionThreatBinder binder) = MakeCompanion();

            InvokePrivate(binder, "OnDisable");

            Assert.AreEqual(0, PerceptionTargetRegistry.Count, "無効化・Scene 離脱で登録を残さない。");
        }

        // ---- 狙う側（索敵） ----

        private CompanionTargetTracker MakeTracker(CompanionActor actor)
        {
            var tracker = actor.gameObject.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            return tracker;
        }

        [Test]
        public void Tracker_AcquiresNearestEnemy()
        {
            (CompanionActor actor, CompanionThreatBinder _) = MakeCompanion();
            CompanionTargetTracker tracker = MakeTracker(actor);
            MakeEnemy(new Vector3(0f, 0f, 6f));
            FakeEnemyTarget near = MakeEnemy(new Vector3(0f, 0f, 2f));

            tracker.TickTargeting();

            Assert.IsTrue(tracker.HasTarget);
            Assert.AreSame(near, tracker.CurrentTarget, "最寄りの敵を狙う。");
        }

        [Test]
        public void Tracker_IgnoresOtherCompanions()
        {
            (CompanionActor actor, CompanionThreatBinder _) = MakeCompanion();
            CompanionTargetTracker tracker = MakeTracker(actor);
            MakeCompanion(new Vector3(0f, 0f, 1f)); // 仲間同士は敵対しない。

            tracker.TickTargeting();

            Assert.IsFalse(tracker.HasTarget, "味方は候補にならない。");
        }

        [TestCase(CompanionState.Down)]
        [TestCase(CompanionState.Away)]
        [TestCase(CompanionState.Stagger)]
        public void Tracker_DropsTarget_WhenCannotEngage(CompanionState state)
        {
            (CompanionActor actor, CompanionThreatBinder _) = MakeCompanion();
            CompanionTargetTracker tracker = MakeTracker(actor);
            MakeEnemy(new Vector3(0f, 0f, 2f));
            tracker.TickTargeting();
            Assert.IsTrue(tracker.HasTarget, "前提：捕捉している。");

            actor.ResetState(state);
            tracker.TickTargeting();

            Assert.IsFalse(tracker.HasTarget, state + " 中は対象を持たない。");
            Assert.IsNull(tracker.CurrentTarget);
        }

        [Test]
        public void Tracker_KeepsTargetUntilLost()
        {
            (CompanionActor actor, CompanionThreatBinder _) = MakeCompanion();
            CompanionTargetTracker tracker = MakeTracker(actor);
            FakeEnemyTarget first = MakeEnemy(new Vector3(0f, 0f, 5f));
            tracker.TickTargeting();
            Assert.AreSame(first, tracker.CurrentTarget);

            MakeEnemy(new Vector3(0f, 0f, 1f)); // より近い敵が現れても乗り換えない。
            tracker.TickTargeting();

            Assert.AreSame(first, tracker.CurrentTarget);
            Assert.AreEqual(1, tracker.TargetChanges, "対象の入れ替わりは 1 回だけ。");
        }

        [Test]
        public void Tracker_Disable_ClearsTarget()
        {
            (CompanionActor actor, CompanionThreatBinder _) = MakeCompanion();
            CompanionTargetTracker tracker = MakeTracker(actor);
            MakeEnemy(new Vector3(0f, 0f, 2f));
            tracker.TickTargeting();

            InvokePrivate(tracker, "OnDisable");

            Assert.IsNull(tracker.CurrentTarget, "無効化で対象参照を残さない。");
        }
    }
}
