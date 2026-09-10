using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 守護転送における<b>命中の到達順</b>の期待結果を、実物の受け口同士で固定する
    /// （N02・N03。レビュー §2.2）。スタブではなく <see cref="PlayerVitalsHolder"/> と
    /// <see cref="CompanionHitReceiver"/> ／ <see cref="CompanionGuardianController"/> を繋ぐ。
    ///
    /// 敵の 1 振りが主人公と犬丸の両方に重なると、両者へ届くのは<b>同じ <see cref="HitId"/></b> になる
    /// （id は攻撃側で決まるため）。現行の敵は重なった Collider へ逐次配送するので、どちらが先に届くかで結果が変わる。
    ///
    /// 保証するのは「どちらの順でも犬丸が二重被弾しない」ことだけで、
    /// <b>「どちらの順でも主人公の HP が同じ」ではない</b>。P4 試遊ではこの逐次解決を維持する既知の仕様であり、
    /// 同時命中をまとめて再配分する仕組みは入れない（本編の守護契約見直しで評価する）。
    /// その差がここで固定されていれば、あとから「順序で結果が変わるのは不具合か」を悩まずに済む。
    /// </summary>
    public sealed class GuardianHitOrderTests
    {
        private const int PlayerMaxHp = 100;
        private const int CompanionMaxHp = 80;
        private const float HitHp = 20f;
        private const float GuardianRange = 3f;
        private const float GuardianCooldown = 6f;

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

        // ---- 補助 ----

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

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        private sealed class FakeAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;
        }

        private sealed class TransferRecorder : IGuardianTransferListener
        {
            public readonly List<GuardianTransferEvent> Received = new List<GuardianTransferEvent>();
            public void OnGuardianTransfer(in GuardianTransferEvent transfer) => Received.Add(transfer);
        }

        private PlayerVitalsHolder MakePlayer(Vector3 position)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.transform.position = position;

            var holder = go.AddComponent<PlayerVitalsHolder>();
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetPrivateField(data, "_maxHp", PlayerMaxHp);
            SetPrivateField(data, "_defense", 0f);
            SetPrivateField(holder, "_data", data);
            return holder;
        }

        private sealed class Companion
        {
            public CompanionActor Actor;
            public CompanionHitReceiver Receiver;
            public CompanionGuardianController Guardian;
        }

        private Companion MakeCompanion(Vector3 position, PlayerVitalsHolder protectedPlayer)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_maxHp", CompanionMaxHp);
            SetPrivateField(data, "_defense", 0f);
            SetPrivateField(data, "_guardianRange", GuardianRange);
            SetPrivateField(data, "_guardianCooldownSeconds", GuardianCooldown);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.back); // 主人公（後方）を向いていない＝ガード弧に頼らない構成。

            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            var guardian = go.AddComponent<CompanionGuardianController>();
            guardian.Bind(actor, receiver, protectedPlayer.transform);
            InvokePrivate(guardian, "OnEnable");

            return new Companion { Actor = actor, Receiver = receiver, Guardian = guardian };
        }

        /// <summary>主人公と犬丸の双方に重なった 1 振り（同一 HitId、ガード不可・回避不可の素の Damage）。</summary>
        private HitInfo SweepHit(IDamageable target, ICombatActor attacker, int instanceId)
        {
            return new HitInfo(
                attacker, target, Vector3.forward, Vector3.zero,
                new HitDamage(HitHp, 0f, 0f),
                guardable: false, justGuardable: false,
                hitId: HitId.Single(instanceId));
        }

        // ---- N02：犬丸への直撃が先 ----

        /// <summary>
        /// 犬丸が判定から直接受けた後に、主人公からの転送が同じ HitId で届く場合。
        /// 転送は受け口の二重受理排除に弾かれるので<b>成立しない</b>。主人公は自分に当たった分を通常どおり受ける。
        /// ここで転送を成立扱いにすると、主人公は無傷ですり抜け、守護 CD だけが減る。
        /// </summary>
        [Test]
        public void DirectThenTransfer_DamagesPlayerWithoutGuardianCooldown()
        {
            PlayerVitalsHolder player = MakePlayer(Vector3.zero);
            Companion companion = MakeCompanion(new Vector3(0f, 0f, 1f), player);
            var attacker = new GameObject("Enemy").AddComponent<FakeAttacker>();
            _spawned.Add(attacker.gameObject);
            attacker.transform.position = new Vector3(0f, 0f, -2f);

            var transfers = new TransferRecorder();
            player.GuardianTransfers.AddListener(transfers);

            // 1) 判定が犬丸に当たる。
            companion.Receiver.ReceiveHit(SweepHit(companion.Receiver, attacker, instanceId: 31));

            // 2) 同じ振りが主人公にも当たる（同一 HitId）。
            player.ReceiveHit(SweepHit(player, attacker, instanceId: 31));

            Assert.AreEqual(CompanionMaxHp - (int)HitHp, companion.Receiver.CurrentHp,
                "犬丸は直撃を 1 回だけ受ける（転送分は重複として捨てられる）。");
            Assert.AreEqual(PlayerMaxHp - (int)HitHp, player.Vitals.Health.Current,
                "主人公は自分に当たった分を通常どおり受ける。肩代わりは成立していない。");
            Assert.AreEqual(0, companion.Guardian.TransferCount, "成立していないので肩代わり回数は増えない。");
            Assert.AreEqual(0f, companion.Guardian.CooldownRemaining, 1e-4f, "守護 CD を消費しない。");
            Assert.AreEqual(0, transfers.Received.Count, "成立通知も出さない。");
        }

        // ---- N03：主人公からの転送が先 ----

        /// <summary>
        /// 主人公が先に受けて転送が成立し、その後に同じ HitId の直撃が犬丸へ届く場合。
        /// 犬丸の受理は 1 回だけ。主人公はこの命中では削れない。
        /// </summary>
        [Test]
        public void TransferThenDirect_DoesNotDamageCompanionTwice()
        {
            PlayerVitalsHolder player = MakePlayer(Vector3.zero);
            Companion companion = MakeCompanion(new Vector3(0f, 0f, 1f), player);
            var attacker = new GameObject("Enemy").AddComponent<FakeAttacker>();
            _spawned.Add(attacker.gameObject);
            attacker.transform.position = new Vector3(0f, 0f, -2f);

            var transfers = new TransferRecorder();
            player.GuardianTransfers.AddListener(transfers);

            // 1) 主人公に当たり、犬丸へ転送される。
            player.ReceiveHit(SweepHit(player, attacker, instanceId: 32));

            // 2) 同じ振りが犬丸にも直接当たる（同一 HitId）。
            companion.Receiver.ReceiveHit(SweepHit(companion.Receiver, attacker, instanceId: 32));

            Assert.AreEqual(CompanionMaxHp - (int)HitHp, companion.Receiver.CurrentHp,
                "犬丸の受理は 1 回だけ（後から届いた直撃は重複として捨てられる）。");
            Assert.AreEqual(PlayerMaxHp, player.Vitals.Health.Current,
                "肩代わりが成立したので、主人公はこの命中で削れない。");
            Assert.AreEqual(1, companion.Guardian.TransferCount, "肩代わりは 1 回。");
            Assert.AreEqual(GuardianCooldown, companion.Guardian.CooldownRemaining, 1e-4f,
                "成立したときだけ CD が始まる。");
            Assert.AreEqual(1, transfers.Received.Count, "成立通知は 1 回だけ。");
        }

        // ---- 到達順で主人公の HP が変わることを、既知の仕様として明示的に固定する ----

        /// <summary>
        /// 上の 2 本は<b>同じ 1 振り</b>を扱っているのに主人公の HP が違う。これは不具合ではなく、
        /// 逐次解決を採る限り避けられない差である（同時命中をまとめて再配分する仕組みを入れていない）。
        /// 暗黙のままにすると、後から「順序で結果が変わる」ことを不具合と読み違える。ここで言葉にして固定しておく。
        /// </summary>
        [Test]
        public void ArrivalOrder_ChangesPlayerDamage_AndThatIsTheAcceptedTradeoff()
        {
            PlayerVitalsHolder playerA = MakePlayer(Vector3.zero);
            Companion companionA = MakeCompanion(new Vector3(0f, 0f, 1f), playerA);
            companionA.Receiver.ReceiveHit(SweepHit(companionA.Receiver, null, instanceId: 41));
            playerA.ReceiveHit(SweepHit(playerA, null, instanceId: 41));

            PlayerVitalsHolder playerB = MakePlayer(new Vector3(20f, 0f, 0f));
            Companion companionB = MakeCompanion(new Vector3(20f, 0f, 1f), playerB);
            playerB.ReceiveHit(SweepHit(playerB, null, instanceId: 42));
            companionB.Receiver.ReceiveHit(SweepHit(companionB.Receiver, null, instanceId: 42));

            Assert.AreEqual(companionA.Receiver.CurrentHp, companionB.Receiver.CurrentHp,
                "犬丸の被害は到達順に依存しない（ここは保証する）。");
            Assert.AreNotEqual(playerA.Vitals.Health.Current, playerB.Vitals.Health.Current,
                "主人公の HP は到達順に依存する（P4 試遊で受け入れている既知の差。保証していない）。");
        }
    }
}
