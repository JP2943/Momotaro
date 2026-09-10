using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-05：守護（かばう）を検証する。P4-01 で定義した <see cref="IGuardianResolver"/> ／
    /// <see cref="IGuardianReceiver"/> の契約を、仲間側の実装が満たしているかを固定する。
    ///
    /// 特に固定するのは次の 4 点。
    /// <list type="number">
    /// <item><description>距離・状態・クールダウンのいずれかを満たさなければ引き受けないこと</description></item>
    /// <item><description>成立しなかった肩代わりでクールダウンを消費しないこと（判断と副作用の分離）</description></item>
    /// <item><description>肩代わりした命中が守護者へ実際に入ること（転送後の命中が守護者を向いていること）</description></item>
    /// <item><description>守護対象へ参照を残さないこと（無効化・Scene 離脱）</description></item>
    /// </list>
    /// </summary>
    public sealed class CompanionGuardianControllerTests
    {
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

        /// <summary>守護される側の役（登録先だけを持つ）。</summary>
        private sealed class FakeHost : MonoBehaviour, IGuardianHost
        {
            public IGuardianResolver Resolver { get; private set; }
            public int SetCount { get; private set; }

            public void SetGuardianResolver(IGuardianResolver resolver)
            {
                Resolver = resolver;
                SetCount++;
            }

            /// <summary>本物（<c>PlayerVitalsHolder</c>）と同じく、登録者が一致するときだけ外す。</summary>
            public void ClearGuardianResolver(IGuardianResolver expected)
            {
                if (expected == null || !ReferenceEquals(Resolver, expected))
                {
                    return;
                }

                Resolver = null;
            }
        }

        private sealed class FakeAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;
        }

        private CompanionData MakeData()
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_guardianRange", GuardianRange);
            SetPrivateField(data, "_guardianCooldownSeconds", GuardianCooldown);
            return data;
        }

        private FakeHost MakeHost(Vector3 position)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.transform.position = position;
            return go.AddComponent<FakeHost>();
        }

        private (CompanionActor actor, CompanionHitReceiver receiver, CompanionGuardianController guardian)
            MakeCompanion(Vector3 position, FakeHost host)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData());
            actor.ResetState(CompanionState.Follow);

            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            var guardian = go.AddComponent<CompanionGuardianController>();
            guardian.Bind(actor, receiver, host != null ? host.transform : null);
            InvokePrivate(guardian, "OnEnable");
            return (actor, receiver, guardian);
        }

        private HitInfo MakeHit(IDamageable target, ICombatActor attacker = null, float hp = 20f)
        {
            return new HitInfo(
                attacker, target, Vector3.back, Vector3.zero,
                new HitDamage(hp, 0f, 0f),
                guardable: true, justGuardable: false,
                hitId: HitId.Single(Random.Range(1, 100000)));
        }

        // ---- 登録 ----

        [Test]
        public void Enable_RegistersItselfWithProtectedTarget()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver _, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.forward, host);

            Assert.AreSame(guardian, host.Resolver, "守護対象へ自分を登録する（Scene 側の配線変更を要さない）。");
        }

        [Test]
        public void Disable_UnregistersFromProtectedTarget()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver _, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.forward, host);

            InvokePrivate(guardian, "OnDisable");

            Assert.IsNull(host.Resolver, "無効化・Scene 離脱で守護対象に参照を残さない。");
        }

        /// <summary>
        /// 守護対象は判断先を 1 つしか持てないため、2 体目が有効化されると登録は 2 体目に差し替わる。
        /// このとき 1 体目が退場して<b>無条件に</b>解除すると、生き残っている 2 体目の登録まで消えて誰も庇わなくなる。
        /// 犬丸 1 体の現状では表に出ないが、猿若・雉代が加わった瞬間に無言で起きる（P4-FIX F03）。
        /// </summary>
        [Test]
        public void DisablingAnOlderGuardian_KeepsTheCurrentRegistration()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver _, CompanionGuardianController first) =
                MakeCompanion(Vector3.forward, host);
            (CompanionActor _, CompanionHitReceiver _, CompanionGuardianController second) =
                MakeCompanion(Vector3.back, host);

            Assert.AreSame(second, host.Resolver, "後から有効化した守護者へ登録が移る（現状の 1 枠契約）。");

            InvokePrivate(first, "OnDisable");

            Assert.AreSame(second, host.Resolver,
                "先に退場した守護者は、自分のものでない登録を外さない。");
        }

        [Test]
        public void DisablingTheCurrentGuardian_ClearsTheRegistration()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver _, CompanionGuardianController first) =
                MakeCompanion(Vector3.forward, host);
            (CompanionActor _, CompanionHitReceiver _, CompanionGuardianController second) =
                MakeCompanion(Vector3.back, host);

            InvokePrivate(second, "OnDisable");

            Assert.IsNull(host.Resolver,
                "いま登録されている守護者が退場したら外れる（一致判定が「常に外さない」に倒れていない）。");
            Assert.IsNotNull(first, "1 体目は生きているが、登録は戻らない（誰が庇うかの調停は P4-07）。");
        }

        // ---- 引き受けの判断 ----

        [Test]
        public void WithinRange_TakesOver()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(new Vector3(0f, 0f, GuardianRange - 0.5f), host);

            Assert.IsTrue(guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver resolved));
            Assert.AreSame(receiver, resolved);
        }

        [Test]
        public void BeyondRange_DoesNotTakeOver()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(new Vector3(0f, 0f, GuardianRange + 0.5f), host);

            Assert.IsFalse(guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver _),
                "離れた場所から瞬間移動して庇うことはしない。");
        }

        [TestCase(CompanionState.Down)]
        [TestCase(CompanionState.Away)]
        [TestCase(CompanionState.Stagger)]
        public void CannotTakeOver_WhileDownAwayOrStaggered(CompanionState state)
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor actor, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.forward, host);

            actor.ResetState(state);

            Assert.IsFalse(guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver _),
                state + " 中は庇えない。");
        }

        [Test]
        public void WithoutProtectedTarget_DoesNotTakeOver()
        {
            (CompanionActor _, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.zero, null);

            Assert.IsFalse(guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver _),
                "守護対象が分からないうちは庇わない。");
        }

        // ---- クールダウン ----

        [Test]
        public void Transfer_StartsCooldown()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor actor, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.forward, host);

            guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver resolved);
            guardian.NotifyTransferred(MakeHit(receiver), resolved);

            Assert.AreEqual(GuardianCooldown, guardian.CooldownRemaining, 1e-3f);
            Assert.AreEqual(CompanionState.Protect, actor.State, "庇ったことを状態に出す。");
            Assert.IsFalse(guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver _),
                "クールダウン中は連続で庇わない（主人公が実質無敵になるのを防ぐ）。");
        }

        [Test]
        public void Cooldown_ExpiresOverTime()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.forward, host);
            guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver resolved);
            guardian.NotifyTransferred(MakeHit(receiver), resolved);

            guardian.TickGuardian(GuardianCooldown + 0.1f);

            Assert.AreEqual(0f, guardian.CooldownRemaining, 1e-3f);
            Assert.IsTrue(guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver _),
                "クールダウンが明ければまた庇える。");
        }

        [Test]
        public void RejectedResolution_DoesNotConsumeCooldown()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(new Vector3(0f, 0f, GuardianRange + 1f), host);

            guardian.TryResolveGuardian(MakeHit(receiver), out IGuardianReceiver _);

            Assert.AreEqual(0f, guardian.CooldownRemaining, 1e-3f,
                "成立しなかった肩代わりでクールダウンを消費しない（判断と副作用を分けている）。");
        }

        // ---- 受け口 ----

        [Test]
        public void Receiver_CanTakeOver_ReflectsState()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor actor, CompanionHitReceiver receiver, CompanionGuardianController _) =
                MakeCompanion(Vector3.forward, host);

            Assert.IsTrue(receiver.CanTakeOver, "通常時は引き受けられる。");

            actor.ResetState(CompanionState.Away);
            Assert.IsFalse(receiver.CanTakeOver, "退場中は引き受けられない。");
        }

        [Test]
        public void TransferredHit_LandsOnGuardian()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.forward, host);

            var attackerGo = new GameObject("Enemy");
            _spawned.Add(attackerGo);
            attackerGo.transform.position = new Vector3(0f, 0f, -2f);
            FakeAttacker attacker = attackerGo.AddComponent<FakeAttacker>();

            // 主人公向けの命中を、契約どおりに守護者向けへ組み替えて渡す（PlayerVitalsHolder が行う手順と同じ）。
            HitInfo original = MakeHit(null, attacker, hp: 20f);
            Assert.IsTrue(guardian.TryResolveGuardian(original, out IGuardianReceiver resolved));
            Assert.IsTrue(resolved.CanTakeOver);

            HitInfo transferred = GuardianHitTransfer.Rebuild(original, resolved);
            int before = receiver.CurrentHp;
            resolved.ReceiveHit(transferred);
            guardian.NotifyTransferred(transferred, resolved);

            Assert.AreSame(receiver, transferred.Target, "転送後の命中は守護者を向いている。");
            Assert.Less(receiver.CurrentHp, before, "肩代わりしたぶんは守護者の HP が減る。");
            Assert.AreEqual(1, guardian.TransferCount);
        }

        [Test]
        public void TransferredHit_IsAcceptedOnlyOnce()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.forward, host);

            HitInfo original = MakeHit(null, null, hp: 20f);
            guardian.TryResolveGuardian(original, out IGuardianReceiver resolved);
            HitInfo transferred = GuardianHitTransfer.Rebuild(original, resolved);

            int before = receiver.CurrentHp;
            resolved.ReceiveHit(transferred);
            resolved.ReceiveHit(transferred); // 直接命中と転送が二重に届いた場合。

            Assert.AreEqual(before - 20, receiver.CurrentHp,
                "同一命中は 1 回だけ受理する（転送と直接命中がどちらの順で届いても二重に入らない）。");
        }

        /// <summary>
        /// 二重受理を弾いたとき、受け口は「受理しなかった」と答えなければならない。
        /// ここが true を返すと、主人公側は肩代わりが成立したと信じて自分の被弾を飛ばし、
        /// <b>誰も痛みを引き受けないまま命中が消える</b>（P4-FIX F03 の転送の原子性）。
        /// </summary>
        [Test]
        public void DroppedTransfer_ReportsNotAccepted()
        {
            FakeHost host = MakeHost(Vector3.zero);
            (CompanionActor _, CompanionHitReceiver receiver, CompanionGuardianController guardian) =
                MakeCompanion(Vector3.forward, host);

            HitInfo original = MakeHit(null, null, hp: 20f);
            guardian.TryResolveGuardian(original, out IGuardianReceiver resolved);
            HitInfo transferred = GuardianHitTransfer.Rebuild(original, resolved);

            Assert.IsTrue(resolved.TryReceiveTransferredHit(transferred), "1 回目は受理する。");
            Assert.IsFalse(resolved.TryReceiveTransferredHit(transferred),
                "同一命中の 2 回目は受理していない。呼び出し側は通常ダメージへ戻す必要がある。");
        }
    }
}
