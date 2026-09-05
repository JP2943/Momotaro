using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Gameplay.Companion;
using Momotaro.Presentation.Companion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-02：仮表示（<see cref="CompanionPlaceholderPresenter"/> と <see cref="CompanionStateColors"/>）を検証する。
    /// 状態を色と透明度で見分けられること、方向インジケータが論理前方を向いて足元へ寝ること、退場中は描かないこと、
    /// 素材・参照が未割当でも例外を出さないこと、購読を残さないことを固定する。
    /// </summary>
    public sealed class CompanionPlaceholderPresenterTests
    {
        private readonly List<UnityEngine.Object> _spawned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _spawned)
            {
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
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
            public CompanionActor Actor;
            public SpriteRenderer Body;
            public SpriteRenderer Arrow;
            public CompanionPlaceholderPresenter Presenter;
        }

        private Rig MakeRig(CompanionState initial = CompanionState.Follow)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            var actor = go.AddComponent<CompanionActor>();
            actor.ResetState(initial);

            var bodyGo = new GameObject("Sprite");
            bodyGo.transform.SetParent(go.transform, false);
            SpriteRenderer body = bodyGo.AddComponent<SpriteRenderer>();

            var arrowGo = new GameObject("DirectionArrow");
            arrowGo.transform.SetParent(go.transform, false);
            SpriteRenderer arrow = arrowGo.AddComponent<SpriteRenderer>();

            var presenter = go.AddComponent<CompanionPlaceholderPresenter>();
            presenter.Bind(actor, body, arrow);
            InvokePrivate(presenter, "OnEnable");

            return new Rig { Actor = actor, Body = body, Arrow = arrow, Presenter = presenter };
        }

        // ---- 状態色 ----

        [Test]
        public void Colors_ResolveForEveryState()
        {
            foreach (CompanionState state in Enum.GetValues(typeof(CompanionState)))
            {
                Color c = CompanionStateColors.Resolve(state);
                Assert.GreaterOrEqual(c.a, 0f, state + " の色が定義されている。");
                Assert.LessOrEqual(c.a, 1f);
            }
        }

        [Test]
        public void Colors_AwayIsTransparentAndHidden()
        {
            Assert.AreEqual(0f, CompanionStateColors.Resolve(CompanionState.Away).a, 1e-4f);
            Assert.IsFalse(CompanionStateColors.IsVisible(CompanionState.Away), "退場中は描かない。");
            Assert.IsTrue(CompanionStateColors.IsVisible(CompanionState.Follow));
            Assert.IsTrue(CompanionStateColors.IsVisible(CompanionState.Down), "ダウンは半透明で描く（居場所は分かる）。");
        }

        [Test]
        public void Colors_KeyStatesAreDistinguishable()
        {
            Color follow = CompanionStateColors.Resolve(CompanionState.Follow);
            Color protect = CompanionStateColors.Resolve(CompanionState.Protect);
            Color down = CompanionStateColors.Resolve(CompanionState.Down);

            Assert.AreNotEqual(follow, protect, "追従と守護（かばう）は見分けられる。");
            Assert.AreNotEqual(follow, down);
            Assert.Less(down.a, follow.a, "ダウンは透過で区別する。");
        }

        // ---- 状態の反映 ----

        [Test]
        public void OnEnable_AppliesCurrentStateImmediately()
        {
            Rig rig = MakeRig(CompanionState.Guard);

            Assert.AreEqual(CompanionState.Guard, rig.Presenter.AppliedState, "有効化時点の状態を必ず一度反映する。");
            Assert.AreEqual(CompanionStateColors.Resolve(CompanionState.Guard), rig.Body.color);
        }

        [Test]
        public void StateChange_TintsBodyImmediately()
        {
            Rig rig = MakeRig();

            rig.Actor.RequestState(CompanionState.Protect, CompanionStateChangeReason.Protected);

            Assert.AreEqual(CompanionStateColors.Resolve(CompanionState.Protect), rig.Body.color,
                "状態通知だけで色が変わる（LateUpdate を待たない）。");
        }

        [Test]
        public void AwayState_HidesRenderers()
        {
            Rig rig = MakeRig();
            Assert.IsTrue(rig.Body.enabled);

            rig.Actor.RequestState(CompanionState.Away, CompanionStateChangeReason.Left);

            Assert.IsFalse(rig.Body.enabled, "退場中は本体を描かない。");
            Assert.IsFalse(rig.Arrow.enabled, "退場中は方向インジケータも描かない。");
        }

        [Test]
        public void ReturningFromAway_ShowsRenderersAgain()
        {
            Rig rig = MakeRig();
            rig.Actor.RequestState(CompanionState.Away, CompanionStateChangeReason.Left);

            rig.Actor.ResetState(CompanionState.Follow);

            Assert.IsTrue(rig.Body.enabled);
            Assert.IsTrue(rig.Arrow.enabled);
        }

        [Test]
        public void LateUpdate_RecoversFromMissedNotification()
        {
            Rig rig = MakeRig();
            InvokePrivate(rig.Presenter, "OnDisable"); // 購読解除（通知を取りこぼす状況）。
            rig.Actor.RequestState(CompanionState.Chase, CompanionStateChangeReason.EngagedTarget);
            Assert.AreNotEqual(CompanionState.Chase, rig.Presenter.AppliedState, "前提：通知を受けていない。");

            InvokePrivate(rig.Presenter, "LateUpdate");

            Assert.AreEqual(CompanionState.Chase, rig.Presenter.AppliedState, "毎フレームの整合で表示がずれ続けない。");
        }

        // ---- 方向インジケータ ----

        [Test]
        public void Arrow_LiesFlatAndPointsAlongFacing()
        {
            Rig rig = MakeRig();
            rig.Actor.SetFacing(Vector3.right);

            InvokePrivate(rig.Presenter, "LateUpdate");

            Assert.AreEqual(Vector3.up.x, rig.Arrow.transform.forward.x, 1e-3f);
            Assert.AreEqual(Vector3.up.y, rig.Arrow.transform.forward.y, 1e-3f, "面は真上を向く（見下ろしで見える）。");
            Assert.AreEqual(Vector3.right.x, rig.Arrow.transform.up.x, 1e-3f, "矢印は論理前方を指す。");
            Assert.AreEqual(Vector3.right.z, rig.Arrow.transform.up.z, 1e-3f);
        }

        [Test]
        public void Arrow_FollowsActorPosition_JustAboveGround()
        {
            Rig rig = MakeRig();
            rig.Actor.transform.position = new Vector3(3f, 0f, -2f);

            InvokePrivate(rig.Presenter, "LateUpdate");

            Assert.AreEqual(3f, rig.Arrow.transform.position.x, 1e-3f);
            Assert.AreEqual(-2f, rig.Arrow.transform.position.z, 1e-3f);
            Assert.Greater(rig.Arrow.transform.position.y, 0f, "地面と重ならないよう僅かに浮かせる。");
            Assert.Less(rig.Arrow.transform.position.y, 0.2f);
        }

        // ---- 安全性・後始末 ----

        [Test]
        public void MissingRenderers_DoNotThrow()
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            var actor = go.AddComponent<CompanionActor>();
            actor.ResetState(CompanionState.Follow);
            var presenter = go.AddComponent<CompanionPlaceholderPresenter>();

            Assert.DoesNotThrow(() => InvokePrivate(presenter, "OnEnable"), "素材・参照が未割当でも例外を出さない。");
            Assert.DoesNotThrow(() => InvokePrivate(presenter, "LateUpdate"));
            Assert.DoesNotThrow(() => actor.RequestState(CompanionState.Down, CompanionStateChangeReason.Defeated));
        }

        [Test]
        public void Disable_LeavesNoSubscription()
        {
            Rig rig = MakeRig();

            InvokePrivate(rig.Presenter, "OnDisable");

            Assert.AreEqual(0, rig.Actor.States.ListenerCount, "Disable で購読を残さない（対称管理）。");
        }

        [Test]
        public void EnableTwice_DoesNotDuplicateSubscription()
        {
            Rig rig = MakeRig();

            InvokePrivate(rig.Presenter, "OnEnable");

            Assert.AreEqual(1, rig.Actor.States.ListenerCount);
        }
    }
}
