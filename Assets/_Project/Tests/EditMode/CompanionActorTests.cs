using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-02：<see cref="CompanionActor"/> が仲間共通契約（<see cref="ICompanionActor"/>）を満たし、状態機を正しく
    /// 委譲することを検証する。Data 未割当でも既定値で安全に動くこと、論理前方をルート回転なしで保持すること、
    /// 状態遷移が型付き通知として流れることを固定する。
    /// </summary>
    public sealed class CompanionActorTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
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

        private CompanionActor MakeActor(CompanionState initial = CompanionState.Follow)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            var actor = go.AddComponent<CompanionActor>();
            SetPrivateField(actor, "_initialState", initial);
            actor.ResetState(initial); // Awake は EditMode で走らない場合があるため明示的に初期化する。
            return actor;
        }

        private CompanionData MakeData(CompanionRole role)
        {
            var d = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(d);
            SetPrivateField(d, "_role", role);
            return d;
        }

        private sealed class Recorder : ICompanionStateListener
        {
            public readonly List<CompanionStateChanged> Received = new List<CompanionStateChanged>();
            public void OnCompanionStateChanged(in CompanionStateChanged change) => Received.Add(change);
        }

        [Test]
        public void Faction_IsAlwaysAlly()
        {
            CompanionActor a = MakeActor();

            Assert.AreEqual(CombatFaction.Ally, a.Faction, "仲間の陣営は常に Ally。");
        }

        [Test]
        public void WithoutData_UsesSafeDefaults()
        {
            CompanionActor a = MakeActor();

            Assert.IsNull(a.Data);
            Assert.AreEqual(CompanionRole.Dog, a.Role, "Data 未割当でも既定の役割で動く（例外を出さない）。");
            Assert.AreEqual(CompanionState.Follow, a.State);
        }

        [Test]
        public void Role_ComesFromData()
        {
            CompanionActor a = MakeActor();
            a.SetData(MakeData(CompanionRole.Monkey));

            Assert.AreEqual(CompanionRole.Monkey, a.Role);
        }

        [Test]
        public void SetData_IgnoresNull()
        {
            CompanionActor a = MakeActor();
            CompanionData data = MakeData(CompanionRole.Pheasant);
            a.SetData(data);

            a.SetData(null);

            Assert.AreSame(data, a.Data, "null 注入で既存の Data を失わない。");
        }

        [Test]
        public void SlotIndex_ClampsNegative()
        {
            CompanionActor a = MakeActor();

            a.SlotIndex = -5;

            Assert.AreEqual(0, a.SlotIndex);
        }

        [Test]
        public void RequestState_DelegatesToStateMachine()
        {
            CompanionActor a = MakeActor();
            var recorder = new Recorder();
            a.States.AddListener(recorder);

            Assert.IsTrue(a.RequestState(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget));

            Assert.AreEqual(CompanionState.Chase, a.State);
            Assert.AreEqual(CompanionStateChangeReason.EngagedTarget, a.LastReason);
            Assert.AreEqual(1, recorder.Received.Count, "遷移は型付き通知として流れる。");
            Assert.AreEqual(a.ActorId, recorder.Received[0].ActorId);
        }

        [Test]
        public void RequestState_IllegalTransition_IsRecorded()
        {
            CompanionActor a = MakeActor(CompanionState.Down);

            Assert.IsFalse(a.RequestState(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget));
            Assert.AreEqual(CompanionState.Down, a.State);
            Assert.AreEqual(1, a.IllegalTransitionCount);
        }

        [Test]
        public void ForceHitState_AppliesStaggerAndDown()
        {
            CompanionActor a = MakeActor();

            Assert.IsTrue(a.ForceHitState(CompanionState.Stagger, CompanionStateChangeReason.Staggered));
            Assert.AreEqual(CompanionState.Stagger, a.State);

            Assert.IsTrue(a.ForceHitState(CompanionState.Down, CompanionStateChangeReason.Defeated));
            Assert.IsTrue(a.IsDown);
        }

        [Test]
        public void IsAway_ReflectsState()
        {
            CompanionActor a = MakeActor();
            Assert.IsFalse(a.IsAway);

            a.RequestState(CompanionState.Away, CompanionStateChangeReason.Left);

            Assert.IsTrue(a.IsAway);
            Assert.IsFalse(a.IsDown);
        }

        [Test]
        public void SetFacing_KeepsRootRotationUnchanged()
        {
            CompanionActor a = MakeActor();
            Quaternion before = a.transform.rotation;

            a.SetFacing(new Vector3(1f, 5f, 0f));

            Assert.AreEqual(Vector3.right, a.Forward, "高さ成分は無視して XZ で正規化する。");
            Assert.AreEqual(before, a.transform.rotation, "ルート Transform は回さない（接地・Collider の安定のため）。");
        }

        [Test]
        public void SetFacing_IgnoresZeroDirection()
        {
            CompanionActor a = MakeActor();
            a.SetFacing(Vector3.right);

            a.SetFacing(Vector3.zero);

            Assert.AreEqual(Vector3.right, a.Forward, "方向不定は無視して直前の向きを保つ。");
        }

        [Test]
        public void Forward_DefaultsToWorldForward()
        {
            CompanionActor a = MakeActor();

            Assert.AreEqual(Vector3.forward, a.Forward);
        }

        [Test]
        public void ResetState_RestoresInitialStateAndFacing()
        {
            CompanionActor a = MakeActor();
            a.SetFacing(Vector3.right);
            a.ForceHitState(CompanionState.Down, CompanionStateChangeReason.Defeated);

            a.ResetState(CompanionState.Follow);

            Assert.AreEqual(CompanionState.Follow, a.State);
            Assert.AreEqual(Vector3.forward, a.Forward);
        }
    }
}
