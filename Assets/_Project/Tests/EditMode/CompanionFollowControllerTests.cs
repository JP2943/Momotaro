using System.Collections.Generic;
using System.Reflection;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-02：追従の結線（<see cref="CompanionFollowController"/>）を検証する。判断（Move／Hold／Warp）が Motor への
    /// 正しい指示と状態遷移に変換されること、退場・ダウン・ひるみ中は追従しないこと、未配線でも例外を出さないことを固定する。
    ///
    /// 物理を待たずに済むよう、Update を明示的に駆動して「Motor へ何を指示したか」で検証する（実際に移動して縮まることは
    /// PlayMode の <c>CompanionFollowPlayTests</c> が見る）。
    /// </summary>
    public sealed class CompanionFollowControllerTests
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

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        private sealed class Rig
        {
            public Transform Leader;
            public CompanionActor Actor;
            public CompanionMotor Motor;
            public CompanionFollowController Controller;
        }

        private Rig MakeRig(Vector3 companionPosition)
        {
            var leaderGo = new GameObject("Leader");
            _spawned.Add(leaderGo);
            leaderGo.transform.position = Vector3.zero;
            leaderGo.transform.rotation = Quaternion.identity; // 前方 +Z。

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = companionPosition;

            var actor = go.AddComponent<CompanionActor>();
            actor.ResetState(CompanionState.Follow);
            var motor = go.AddComponent<CompanionMotor>(); // RequireComponent で Rigidbody が付く。
            var controller = go.AddComponent<CompanionFollowController>();
            controller.Bind(leaderGo.transform, actor, motor);
            InvokePrivate(controller, "OnEnable");

            return new Rig { Leader = leaderGo.transform, Actor = actor, Motor = motor, Controller = controller };
        }

        private static Vector3 Slot(Rig rig)
        {
            CompanionFollowSettings s = CompanionFollowSettings.From(rig.Actor.Data);
            return FormationSlot.Resolve(rig.Leader.position, rig.Leader.forward, rig.Actor.SlotIndex, s.Spacing);
        }

        [Test]
        public void FarFromSlot_OrdersMoveToSlot()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 3f));

            InvokePrivate(rig.Controller, "Update");

            Assert.AreEqual(CompanionFollowDecision.Move, rig.Controller.Decision);
            Assert.IsTrue(rig.Motor.HasMoveTarget, "隊列位置へ移動を指示する。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State);
            Vector3 slot = Slot(rig);
            Assert.AreEqual(slot.x, rig.Controller.Model.SlotPosition.x, 1e-3f);
            Assert.AreEqual(slot.z, rig.Controller.Model.SlotPosition.z, 1e-3f);
        }

        [Test]
        public void AtSlot_StopsAndStaysFollowing()
        {
            Rig rig = MakeRig(Vector3.zero);
            rig.Controller.transform.position = Slot(rig);

            InvokePrivate(rig.Controller, "Update");

            Assert.AreEqual(CompanionFollowDecision.Hold, rig.Controller.Decision);
            Assert.IsFalse(rig.Motor.HasMoveTarget, "到着していれば移動を指示しない。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "停止しても追従状態のままにする（状態を往復させない）。");
        }

        [Test]
        public void BeyondWarpDistance_WarpsToSlot()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 30f));

            InvokePrivate(rig.Controller, "Update");

            Assert.AreEqual(CompanionFollowDecision.Warp, rig.Controller.Decision);
            Assert.AreEqual(1, rig.Motor.WarpCount);
            Assert.AreEqual(CompanionState.Warp, rig.Actor.State);

            Vector3 slot = Slot(rig);
            Assert.AreEqual(slot.x, rig.Controller.transform.position.x, 1e-3f, "隊列位置へ瞬間移動する。");
            Assert.AreEqual(slot.z, rig.Controller.transform.position.z, 1e-3f);
        }

        [Test]
        public void AfterWarp_ReturnsToFollow()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 30f));
            InvokePrivate(rig.Controller, "Update"); // ワープ。

            InvokePrivate(rig.Controller, "Update");

            Assert.AreEqual(CompanionFollowDecision.Hold, rig.Controller.Decision);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "ワープ後は追従へ戻る。");
            Assert.AreEqual(1, rig.Motor.WarpCount, "ワープは 1 回だけ。");
        }

        [Test]
        public void WarpKeepsHeight()
        {
            Rig rig = MakeRig(new Vector3(0f, 2.5f, 30f));

            InvokePrivate(rig.Controller, "Update");

            Assert.AreEqual(2.5f, rig.Controller.transform.position.y, 1e-3f, "ワープで高さを変えない（接地を崩さない）。");
        }

        [TestCase(CompanionState.Away)]
        [TestCase(CompanionState.Down)]
        [TestCase(CompanionState.Stagger)]
        public void NonFollowingStates_DoNotMove(CompanionState state)
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 30f));
            rig.Actor.ResetState(state);

            InvokePrivate(rig.Controller, "Update");

            Assert.IsFalse(rig.Motor.HasMoveTarget, state + " 中は移動しない。");
            Assert.AreEqual(0, rig.Motor.WarpCount, state + " 中はワープもしない。");
            Assert.AreEqual(state, rig.Actor.State, "状態を勝手に変えない。");
        }

        [Test]
        public void RecoveringFromDown_DoesNotInheritStaleDecision()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 3f));
            InvokePrivate(rig.Controller, "Update"); // Move に入る。
            Assert.AreEqual(CompanionFollowDecision.Move, rig.Controller.Decision);

            rig.Actor.ResetState(CompanionState.Down);
            InvokePrivate(rig.Controller, "Update"); // 停止 & 判断リセット。

            Assert.AreEqual(CompanionFollowDecision.Hold, rig.Controller.Decision, "復帰後に古い判断を引きずらない。");
            Assert.AreEqual(0f, rig.Controller.Model.StuckSeconds, 1e-4f);
        }

        [Test]
        public void FacesTowardsSlot_WhileMoving()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 3f));

            InvokePrivate(rig.Controller, "Update");

            // 隊列位置は主人公の後方（-Z 側）にあるため、-Z 寄りを向く。
            Assert.Less(rig.Actor.Forward.z, 0f, "進行方向を向く。");
            Assert.AreEqual(0f, rig.Actor.Forward.y, 1e-4f);
        }

        [TestCase(CompanionState.Away)]
        [TestCase(CompanionState.Down)]
        [TestCase(CompanionState.Stagger)]
        public void StateChange_StopsMotorImmediately(CompanionState state)
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 3f));
            InvokePrivate(rig.Controller, "Update");
            Assert.IsTrue(rig.Motor.HasMoveTarget, "前提：移動を指示している。");

            rig.Actor.ResetState(state); // 状態遷移の通知だけで止まること（Update を回さない）。

            Assert.IsFalse(rig.Motor.HasMoveTarget,
                state + " へ入った瞬間に停止する（次の Update を待つと物理ステップで滑る）。");
            Assert.AreEqual(CompanionFollowDecision.Hold, rig.Controller.Decision);
        }

        [Test]
        public void StateChangeAfterDisable_DoesNotResumeSubscription()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 3f));
            InvokePrivate(rig.Controller, "Update");
            InvokePrivate(rig.Controller, "OnDisable");

            // 購読解除後の状態変化で例外を出さない（対称管理）。
            Assert.DoesNotThrow(() => rig.Actor.ResetState(CompanionState.Down));
            Assert.AreEqual(0, rig.Actor.States.ListenerCount, "Disable で購読を残さない。");
        }

        [Test]
        public void Unbound_DoesNotThrow()
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.AddComponent<CompanionActor>();
            go.AddComponent<CompanionMotor>();
            var controller = go.AddComponent<CompanionFollowController>();
            InvokePrivate(controller, "OnEnable");

            Assert.DoesNotThrow(() => InvokePrivate(controller, "Update"), "追従対象が未配線でも例外を出さない。");
        }

        [Test]
        public void Disable_StopsMotorAndResetsModel()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 3f));
            InvokePrivate(rig.Controller, "Update");
            Assert.IsTrue(rig.Motor.HasMoveTarget);

            InvokePrivate(rig.Controller, "OnDisable");

            Assert.IsFalse(rig.Motor.HasMoveTarget, "Disable で移動指示を残さない。");
            Assert.AreEqual(CompanionFollowDecision.Hold, rig.Controller.Decision);
        }
    }
}
