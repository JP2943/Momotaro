using System.Collections;
using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P4-02：仲間の追従を実行時ライフサイクルで検証する。実際の Update／FixedUpdate と物理を通して隊列位置へ近づくこと、
    /// 距離超過でワープして復帰すること、無効化・破棄で速度と指示が残らないことを見る。
    /// 判断規則そのもの（停止・再開・停滞判定の境界）は EditMode で決定的に検証済みで、ここは配管だけを見る。
    /// </summary>
    public sealed class CompanionFollowPlayTests
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

        private sealed class Rig
        {
            public Transform Leader;
            public CompanionActor Actor;
            public CompanionMotor Motor;
            public CompanionFollowController Controller;
            public Rigidbody Body;
        }

        private Rig MakeRig(Vector3 companionPosition)
        {
            var leaderGo = new GameObject("Leader");
            _spawned.Add(leaderGo);
            leaderGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = companionPosition;

            var actor = go.AddComponent<CompanionActor>();
            var motor = go.AddComponent<CompanionMotor>();
            var controller = go.AddComponent<CompanionFollowController>();
            controller.Bind(leaderGo.transform, actor, motor);

            return new Rig
            {
                Leader = leaderGo.transform,
                Actor = actor,
                Motor = motor,
                Controller = controller,
                Body = go.GetComponent<Rigidbody>(),
            };
        }

        private static float DistanceToSlot(Rig rig)
        {
            return FormationSlot.HorizontalDistance(rig.Controller.transform.position, rig.Controller.Model.SlotPosition);
        }

        [UnityTest]
        public IEnumerator Companion_ClosesDistanceToFormationSlot()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 4f));
            yield return null; // Awake/OnEnable。

            for (int i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(DistanceToSlot(rig), 0.6f,
                "実行時経路（Update の判断 → Motor の物理移動）で隊列位置まで詰められる。実測: " + DistanceToSlot(rig));
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State);
            Assert.AreEqual(0, rig.Motor.WarpCount, "移動で届く距離ではワープしない。");
        }

        [UnityTest]
        public IEnumerator Companion_WarpsBackWhenLeftBehind()
        {
            Rig rig = MakeRig(Vector3.zero);
            yield return null;
            yield return null;

            // 主人公が遠くへ移動した（エリア遷移・置き去り相当）。
            rig.Leader.position = new Vector3(0f, 0f, 60f);
            yield return null;
            yield return null;

            Assert.AreEqual(1, rig.Motor.WarpCount, "距離超過でワープする。");
            Assert.Less(DistanceToSlot(rig), 0.6f, "ワープ後は隊列位置に居る。");
        }

        [UnityTest]
        public IEnumerator DisabledCompanion_KeepsNoVelocity()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 4f));
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            rig.Controller.enabled = false;
            rig.Motor.enabled = false;
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(0f, rig.Body.linearVelocity.magnitude, 1e-3f, "無効化で速度を残さない（§2.3 後始末）。");
            Assert.IsFalse(rig.Motor.HasMoveTarget);
        }

        [UnityTest]
        public IEnumerator AwayCompanion_DoesNotMove()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 4f));
            yield return null;
            rig.Actor.ResetState(CompanionState.Away);
            Vector3 before = rig.Controller.transform.position;

            for (int i = 0; i < 30; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(before.x, rig.Controller.transform.position.x, 1e-3f, "退場中は動かない。");
            Assert.AreEqual(before.z, rig.Controller.transform.position.z, 1e-3f);
            Assert.AreEqual(0, rig.Motor.WarpCount);
        }

        [UnityTest]
        public IEnumerator DestroyedCompanion_DoesNotThrow()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, 4f));
            yield return null;

            Assert.DoesNotThrow(() => Object.Destroy(rig.Controller.gameObject));
            yield return null;
            yield return new WaitForFixedUpdate();

            LogAssert.NoUnexpectedReceived();
        }
    }
}
