using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 移動と論理的な向きの<b>書き手が 1 つである</b>ことを固定する（N18。P4-FIX F02a）。
    ///
    /// 追従と戦闘がそれぞれ Motor を直接触っていたころは、同じフレームに両方が書くと
    /// 「どちらが後だったか」で結果が決まっていた。条件式を足して回るのではなく、
    /// 誰の意図かを明示して<b>強い持ち主が勝つ・古い持ち主は上書きできない</b>という規則にする。
    ///
    /// ここで守りたい失敗は具体的に 3 つ。
    /// <list type="number">
    /// <item><description>戦闘へ移ったのに、追従の Stop が上書きして近づけない。</description></item>
    /// <item><description>戦闘から抜けた仲間が、古い所有権のせいで追従へ戻れない。</description></item>
    /// <item><description>ひるんだ瞬間に通常の移動決定が勝ってしまい、1 フレーム分だけ滑る。</description></item>
    /// </list>
    /// </summary>
    public sealed class CompanionMotorArbitrationTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            System.Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        private sealed class Rig
        {
            public GameObject Root;
            public CompanionActor Actor;
            public CompanionMotor Motor;
            public CompanionMovementArbiter Arbiter;
        }

        private Rig MakeRig()
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            // Motor は調停役を必須にしている（RequireComponent）。手で足さなくても付く。
            var motor = go.AddComponent<CompanionMotor>();
            var arbiter = go.GetComponent<CompanionMovementArbiter>();
            Assert.IsNotNull(arbiter, "Motor を足せば調停役も付く（書き手が 1 つであることを構成で担保する）。");
            arbiter.Bind(motor, actor);

            return new Rig { Root = go, Actor = actor, Motor = motor, Arbiter = arbiter };
        }

        // ---- 所有権 ----

        [Test]
        public void OnlyCurrentOwnerCanWriteMovement()
        {
            Rig rig = MakeRig();
            var combatTarget = new Vector3(0f, 0f, 5f);

            // 戦闘が握る。
            Assert.IsTrue(rig.Arbiter.Submit(
                CompanionMovementOwner.Combat, CompanionMoveRequest.Move(combatTarget, 4.5f, 0.3f)));
            Assert.AreEqual(CompanionMovementOwner.Combat, rig.Arbiter.Owner);
            Assert.IsTrue(rig.Motor.HasMoveTarget);

            // 追従が同じフレームに割り込んでも通らない（これが「上書きして近づけない」不具合の正体）。
            Assert.IsFalse(rig.Arbiter.Submit(CompanionMovementOwner.Follow, CompanionMoveRequest.Stop()),
                "弱い持ち主の意図は受理しない。");
            Assert.AreEqual(CompanionMovementOwner.Combat, rig.Arbiter.Owner, "所有権も奪われない。");
            Assert.IsTrue(rig.Motor.HasMoveTarget, "戦闘の移動指示が生きている。");

            // 追従が「手放す」と言っても、自分が持っていないので何も起きない。
            Assert.IsFalse(rig.Arbiter.Release(CompanionMovementOwner.Follow),
                "自分のものでない所有権は手放せない。");
            Assert.AreEqual(CompanionMovementOwner.Combat, rig.Arbiter.Owner);

            // 戦闘が返せば、追従が取れる。
            Assert.IsTrue(rig.Arbiter.Release(CompanionMovementOwner.Combat));
            Assert.AreEqual(CompanionMovementOwner.None, rig.Arbiter.Owner);

            Assert.IsTrue(rig.Arbiter.Submit(
                CompanionMovementOwner.Follow, CompanionMoveRequest.Move(Vector3.left, 4.5f, 0.3f)));
            Assert.AreEqual(CompanionMovementOwner.Follow, rig.Arbiter.Owner);
        }

        [Test]
        public void StrongerOwner_PreemptsWeakerOne()
        {
            Rig rig = MakeRig();

            rig.Arbiter.Submit(CompanionMovementOwner.Follow, CompanionMoveRequest.Move(Vector3.left, 4.5f, 0.3f));
            Assert.AreEqual(CompanionMovementOwner.Follow, rig.Arbiter.Owner);

            Assert.IsTrue(rig.Arbiter.Submit(
                CompanionMovementOwner.Combat, CompanionMoveRequest.Move(Vector3.right, 4.5f, 0.3f)));

            Assert.AreEqual(CompanionMovementOwner.Combat, rig.Arbiter.Owner, "強い持ち主は割り込める。");
        }

        [Test]
        public void SameOwner_CanUpdateItsOwnRequest()
        {
            Rig rig = MakeRig();

            rig.Arbiter.Submit(CompanionMovementOwner.Combat, CompanionMoveRequest.Move(Vector3.forward, 4.5f, 0.3f));
            Assert.IsTrue(rig.Motor.HasMoveTarget);

            Assert.IsTrue(rig.Arbiter.Submit(CompanionMovementOwner.Combat, CompanionMoveRequest.Stop()));
            Assert.IsFalse(rig.Motor.HasMoveTarget, "同じ持ち主は自分の意図を更新できる。");
        }

        // ---- 強制停止 ----

        [Test]
        public void ForceStop_BeatsNormalMovementInTheSameFrame()
        {
            Rig rig = MakeRig();
            rig.Arbiter.Submit(CompanionMovementOwner.Combat, CompanionMoveRequest.Move(Vector3.forward, 4.5f, 0.3f));

            // ひるみ・ダウン・退場・活動停止。
            rig.Arbiter.ForceStop();

            Assert.IsFalse(rig.Motor.HasMoveTarget, "強制停止は所有権に関わらず通る。");
            Assert.IsTrue(rig.Arbiter.ForcedThisFrame);

            // 同じフレームに通常の移動決定が来ても、停止を優先する
            //（1 フレームでも動くと、倒れたはずの仲間が滑る）。
            Assert.IsFalse(rig.Arbiter.Submit(
                CompanionMovementOwner.Combat, CompanionMoveRequest.Move(Vector3.forward, 4.5f, 0.3f)));
            Assert.IsFalse(rig.Motor.HasMoveTarget, "同フレームの移動決定は通さない。");
            Assert.IsFalse(rig.Arbiter.Submit(
                CompanionMovementOwner.Follow, CompanionMoveRequest.Move(Vector3.left, 4.5f, 0.3f)));
        }

        [Test]
        public void ForceStop_ReleasesTheLatchOnTheNextFrame()
        {
            Rig rig = MakeRig();
            rig.Arbiter.ForceStop();
            Assert.IsTrue(rig.Arbiter.ForcedThisFrame);

            rig.Arbiter.EndFrame(); // 実機では LateUpdate。

            Assert.IsFalse(rig.Arbiter.ForcedThisFrame, "止めっぱなしにはしない。");
            Assert.IsTrue(rig.Arbiter.Submit(
                CompanionMovementOwner.Follow, CompanionMoveRequest.Move(Vector3.left, 4.5f, 0.3f)),
                "次のフレームからは誰でも取り直せる。");
        }

        // ---- 向き ----

        [Test]
        public void Facing_IsWrittenOnlyThroughTheArbiter()
        {
            Rig rig = MakeRig();

            rig.Arbiter.Submit(CompanionMovementOwner.Combat, CompanionMoveRequest.StopFacing(Vector3.right));
            Assert.AreEqual(Vector3.right.normalized, rig.Actor.Forward.normalized, "強い持ち主の向きが入る。");

            // 追従が違う方向を向かせようとしても通らない（攻撃中にガード方向がずれる不具合を塞ぐ）。
            rig.Arbiter.Submit(CompanionMovementOwner.Follow, CompanionMoveRequest.FaceOnly(Vector3.back));
            Assert.AreEqual(Vector3.right.normalized, rig.Actor.Forward.normalized,
                "弱い持ち主は向きも書けない。");
        }

        [Test]
        public void FaceOnly_DoesNotTakeMovementOwnership()
        {
            Rig rig = MakeRig();

            rig.Arbiter.Submit(CompanionMovementOwner.Follow, CompanionMoveRequest.FaceOnly(Vector3.right));

            Assert.AreEqual(CompanionMovementOwner.None, rig.Arbiter.Owner,
                "向きだけの指定で移動の所有権は動かない。");
        }

        // ---- 後始末 ----

        [Test]
        public void Disable_ReleasesOwnershipAndStops()
        {
            Rig rig = MakeRig();
            rig.Arbiter.Submit(CompanionMovementOwner.Combat, CompanionMoveRequest.Move(Vector3.forward, 4.5f, 0.3f));

            MethodInfo onDisable = typeof(CompanionMovementArbiter)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(onDisable);
            onDisable.Invoke(rig.Arbiter, null);

            Assert.AreEqual(CompanionMovementOwner.None, rig.Arbiter.Owner, "無効化で所有権を残さない。");
            Assert.IsFalse(rig.Motor.HasMoveTarget, "移動指示も残さない。");
        }
    }
}
