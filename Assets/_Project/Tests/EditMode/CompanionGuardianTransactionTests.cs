using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-FIX-R2：守護の成立を<b>1 つの同期取引</b>として検証する（v1.0 §8.5、c8c0ddf §2.4、レビュー R2-06）。
    ///
    /// 受け口・守護・防御・戦闘の<b>本物同士</b>を 1 体に載せ、主人公側（<c>PlayerVitalsHolder</c>）が行う手順
    /// （可否確定 → 転送の受理 → 通知）をそのまま踏む。固定するのは次の順序と結果。
    /// <list type="number">
    /// <item><description>受理が確定した直後・被害を解決する前に旧攻撃／防御が止まる（旧ガードが転送を防がない）</description></item>
    /// <item><description>拒否（同一 HitId の二重）なら旧行動・CD・通知が不変</description></item>
    /// <item><description>非致死・非 Stagger の成立後、Protect と Guardian の所有権が残らず通常行動へ戻れる</description></item>
    /// <item><description>致死・Stagger の成立では Down／Stagger を Protect で上書きしない</description></item>
    /// </list>
    /// 要求 ID：E15・E16・E17・E18・E19・R04。
    /// </summary>
    public sealed class CompanionGuardianTransactionTests : CompanionActivityFixture
    {
        private const float Startup = 0.2f;
        private const float Active = 0.2f;
        private const float Recovery = 0.3f;
        private const float Cooldown = 1.5f;
        private const float UseRange = 2f;
        private const float GuardianRange = 3f;
        private const float GuardianCooldown = 6f;
        private const float GuardCooldown = 3f;
        private const float EvadeCooldown = 4f;

        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

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
            PerceptionTargetRegistry.Clear();
        }

        // ---- 補助 ----

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
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        private sealed class FakeHost : MonoBehaviour, IGuardianHost
        {
            public IGuardianResolver Resolver { get; private set; }
            public void SetGuardianResolver(IGuardianResolver resolver) => Resolver = resolver;

            public void ClearGuardianResolver(IGuardianResolver expected)
            {
                if (expected != null && ReferenceEquals(Resolver, expected))
                {
                    Resolver = null;
                }
            }
        }

        private sealed class FakeEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward { get; set; } = Vector3.back;
            public int DamageableId => GetInstanceID();
            public int ActorId => GetInstanceID();
            public Vector3 Position => transform.position;
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat => 0f;
            public float AcquiredThreatMultiplier => 1f;
            public int ReceivedHits { get; private set; }
            public void ReceiveHit(in HitInfo hit) => ReceivedHits++;
        }

        private sealed class FakeDanger : IEnemyDangerSense
        {
            public bool HasDanger { get; set; }
            public bool Unblockable { get; set; }

            public EnemyDangerStimulus Sense(Vector3 selfPosition, Vector3 selfForward, int selfDamageableId)
            {
                return HasDanger
                    ? new EnemyDangerStimulus(selfPosition + Vector3.forward * 2f, Vector3.back, Unblockable)
                    : EnemyDangerStimulus.None;
            }
        }

        /// <summary>状態遷移を順に記録する（Protect が取引の中だけに現れることを見る）。</summary>
        private sealed class StateLog : ICompanionStateListener
        {
            public readonly List<CompanionState> Entered = new List<CompanionState>();
            public void OnCompanionStateChanged(in CompanionStateChanged change) => Entered.Add(change.Current);
        }

        private sealed class Rig
        {
            public GameObject Root;
            public CompanionActor Actor;
            public CompanionStateArbiter States;
            public CompanionHitReceiver Receiver;
            public CompanionGuardianController Guardian;
            public CompanionDefenseController Defense;
            public CompanionCombatController Combat;
            public CompanionTargetTracker Tracker;
            public FakeDanger Danger;
            public FakeHost Host;
            public StateLog Log;

            public void Tick(float dt)
            {
                Tracker.TickTargeting();
                Combat.TickCombat(dt);
                Defense.TickDefense(dt);
                Guardian.TickGuardian(dt);
                Receiver.TickVitals(dt);
            }
        }

        private AttackData MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", UseRange);
            SetPrivateField(attack, "_useAngle", 90f);
            SetPrivateField(attack, "_cooldownSeconds", Cooldown);
            SetPrivateField(attack, "_startupSeconds", Startup);
            SetPrivateField(attack, "_activeSeconds", Active);
            SetPrivateField(attack, "_recoverySeconds", Recovery);
            SetPrivateField(attack, "_hpMultiplier", 1f);
            SetPrivateField(attack, "_poiseDamage", 6f);
            SetPrivateField(attack, "_flinchPower", 10f);
            return attack;
        }

        private CompanionData MakeData()
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_attackPower", 50f);
            SetPrivateField(data, "_basicAttack", MakeAttack());
            SetPrivateField(data, "_guardianRange", GuardianRange);
            SetPrivateField(data, "_guardianCooldownSeconds", GuardianCooldown);
            SetPrivateField(data, "_canGuard", true);
            SetPrivateField(data, "_canEvade", true);
            SetPrivateField(data, "_guardCooldownSeconds", GuardCooldown);
            SetPrivateField(data, "_evadeCooldownSeconds", EvadeCooldown);
            return data;
        }

        private Rig MakeRig()
        {
            var hostGo = new GameObject("Player");
            _spawned.Add(hostGo);
            FakeHost host = hostGo.AddComponent<FakeHost>();

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = new Vector3(0f, 0f, 1f);

            var actor = go.AddComponent<CompanionActor>(); // StateArbiter は RequireComponent で付く。
            actor.SetData(MakeData());
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var states = go.GetComponent<CompanionStateArbiter>();
            var motor = go.AddComponent<CompanionMotor>();
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);

            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            var defense = go.AddComponent<CompanionDefenseController>();
            defense.Bind(actor);
            var danger = new FakeDanger();
            defense.SetDangerSense(danger);
            InvokePrivate(defense, "OnEnable");

            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);
            InvokePrivate(combat, "OnEnable");

            var guardian = go.AddComponent<CompanionGuardianController>();
            guardian.Bind(actor, receiver, host.transform);
            InvokePrivate(guardian, "OnEnable");

            var log = new StateLog();
            actor.States.AddListener(log);

            return new Rig
            {
                Root = go, Actor = actor, States = states, Receiver = receiver, Guardian = guardian,
                Defense = defense, Combat = combat, Tracker = tracker, Danger = danger, Host = host, Log = log,
            };
        }

        private FakeEnemy MakeEnemy(Vector3 position)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = position;
            var enemy = go.AddComponent<FakeEnemy>();
            PerceptionTargetRegistry.Register(enemy);
            return enemy;
        }

        private static HitInfo MakePlayerHit(float hp, float flinch = 0f, int id = 777)
        {
            // 主人公へ向いた命中（前方から）。守護側は Rebuild で自分向けに組み替える。
            return new HitInfo(
                null, null, Vector3.back, Vector3.zero,
                new HitDamage(hp, 0f, flinch),
                guardable: true, justGuardable: false,
                hitId: HitId.Single(id));
        }

        /// <summary>主人公側が行う手順そのまま：可否確定 → 転送受理 → 通知。成立したら true。</summary>
        private static bool TransferLikePlayer(Rig rig, in HitInfo original, out HitInfo transferred)
        {
            transferred = default;
            if (!rig.Guardian.TryResolveGuardian(original, out IGuardianReceiver guardian) || !guardian.CanTakeOver)
            {
                return false;
            }

            transferred = GuardianHitTransfer.Rebuild(original, guardian);
            if (!guardian.TryReceiveTransferredHit(transferred))
            {
                return false;
            }

            rig.Guardian.NotifyTransferred(transferred, guardian);
            return true;
        }

        private static void EnterActiveAttack(Rig rig)
        {
            rig.Tick(0f);
            Assert.IsTrue(rig.Combat.IsAttacking, "前提：攻撃が始まっている。");
            rig.Tick(Startup);
            Assert.AreEqual(CompanionAttackPhase.Active, rig.Combat.AttackState.Phase, "前提：判定中。");
            Assert.AreEqual(CompanionState.AttackActive, rig.Actor.State);
        }

        // ---- 順序：受理確定 → 旧行動中断 → 被害解決 ----

        /// <summary>
        /// ガード中の守護。旧ガードは受理確定の直後に解かれ、転送された命中を<b>防がない</b>。
        /// 以前は転送の解決が先で、解除すべき旧ガードが転送を弾いていた（R2-06 影響 A）。
        /// </summary>
        [Test]
        public void GuardingCompanion_AcceptedTransfer_ReleasesGuardBeforeDamage()
        {
            Rig rig = MakeRig();
            rig.Danger.HasDanger = true;
            rig.Tick(0.1f);
            Assert.IsTrue(rig.Defense.IsGuarding, "前提：構えている。");
            Assert.AreEqual(CompanionState.Guard, rig.Actor.State);

            int hpBefore = rig.Receiver.CurrentHp;
            bool transferred = TransferLikePlayer(rig, MakePlayerHit(20f), out HitInfo _);

            Assert.IsTrue(transferred, "ガード中でも守護は成立する（表：Guard 中の守護成立は「旧行動解除して可」）。");
            Assert.IsFalse(rig.Defense.IsGuarding, "受理確定でガード能力を解いている。");
            Assert.AreEqual(hpBefore - 20, rig.Receiver.CurrentHp, "解かれたガードは転送を防がない。肩代わりのぶん HP が減る。");
            Assert.AreEqual(1, rig.Guardian.AcceptedCount);
            Assert.AreEqual(1, rig.Guardian.TransferCount);
            Assert.AreEqual(GuardianCooldown, rig.Guardian.CooldownRemaining, 1e-3f);
        }

        /// <summary>E17：判定中の守護成立。旧攻撃は<b>同じ呼び出しの中で</b>止まり、以後の Tick で判定を出さない。</summary>
        [Test]
        public void ActiveAttack_AcceptedTransfer_CancelsAttackInTheSameCall()
        {
            Rig rig = MakeRig();
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 2f));
            EnterActiveAttack(rig);
            int hitsBefore = enemy.ReceivedHits;

            Assert.IsTrue(rig.Guardian.TryResolveGuardian(MakePlayerHit(10f), out IGuardianReceiver guardian));
            HitInfo transferred = GuardianHitTransfer.Rebuild(MakePlayerHit(10f), guardian);
            Assert.IsTrue(guardian.TryReceiveTransferredHit(transferred));

            // 通知（NotifyTransferred）より前、受理の呼び出しが戻った時点で既に止まっている。
            Assert.IsFalse(rig.Combat.IsAttacking, "受理確定で旧攻撃が中断されている（次の Tick を待たない）。");
            Assert.AreEqual(Cooldown, rig.Combat.CooldownRemaining, 1e-3f, "中断でも開始時 Snapshot の規定 CD が始まる。");

            rig.Guardian.NotifyTransferred(transferred, guardian);
            rig.Tick(0.05f);
            rig.Tick(0.05f);
            Assert.AreEqual(hitsBefore, enemy.ReceivedHits, "中断した攻撃の判定は復活しない（§8.5「中断した攻撃の Active を復活させない」）。");
        }

        /// <summary>E17：判定中の守護<b>不成立</b>（同一 HitId を既に直接受理）。旧攻撃・CD・通知は不変。</summary>
        [Test]
        public void ActiveAttack_RejectedTransfer_LeavesAttackAndCooldownUntouched()
        {
            Rig rig = MakeRig();
            MakeEnemy(new Vector3(0f, 0f, 2f));
            EnterActiveAttack(rig);

            // 同じ 1 振りが先に直撃した（直撃先行）。
            HitInfo direct = new HitInfo(
                null, rig.Receiver, Vector3.back, Vector3.zero, new HitDamage(5f, 0f, 0f),
                guardable: false, justGuardable: false, hitId: HitId.Single(4242));
            rig.Receiver.ReceiveHit(direct);
            Assert.IsTrue(rig.Combat.IsAttacking, "前提：非 Stagger の直撃では攻撃は続く。");

            bool transferred = TransferLikePlayer(rig, MakePlayerHit(20f, id: 4242), out HitInfo _);

            Assert.IsFalse(transferred, "直撃先行なら転送は重複として不成立（c8c0ddf §2.2）。呼び出し側は主人公の通常 Damage へ戻す。");
            Assert.IsTrue(rig.Combat.IsAttacking, "不成立では旧攻撃を止めない。");
            Assert.AreEqual(CompanionState.AttackActive, rig.Actor.State);
            Assert.AreEqual(0, rig.Guardian.AcceptedCount, "受理確定フックは呼ばれない。");
            Assert.AreEqual(0, rig.Guardian.TransferCount);
            Assert.AreEqual(0f, rig.Guardian.CooldownRemaining, 1e-3f, "不成立で CD を消費しない。");
        }

        /// <summary>転送先行なら、あとから届く同一 HitId の直撃は重複として弾かれる（犬丸の二重被害なし。R04）。</summary>
        [Test]
        public void TransferFirst_LaterDirectHitWithSameId_IsRejected()
        {
            Rig rig = MakeRig();
            int hpBefore = rig.Receiver.CurrentHp;

            Assert.IsTrue(TransferLikePlayer(rig, MakePlayerHit(20f, id: 5151), out HitInfo _));
            rig.Receiver.ReceiveHit(new HitInfo(
                null, rig.Receiver, Vector3.back, Vector3.zero, new HitDamage(20f, 0f, 0f),
                guardable: false, justGuardable: false, hitId: HitId.Single(5151)));

            Assert.AreEqual(hpBefore - 20, rig.Receiver.CurrentHp, "同一 HitId の受理は最大 1 回。");
            Assert.AreEqual(1, rig.Guardian.TransferCount);
        }

        // ---- 所有権：Protect は取引の中で完結する ----

        /// <summary>
        /// 非致死・非 Stagger の成立後、Protect と Guardian の所有権が残らない。以前は券を捨てていて、
        /// 以後の攻撃・防御・探索がすべて「強い持ち主が行動中」として拒否され続けた（R2-06 影響 B）。
        /// </summary>
        [Test]
        public void NonStaggerTransfer_LeavesNoProtectOwnership_AndNormalActionsResume()
        {
            Rig rig = MakeRig();

            Assert.IsTrue(TransferLikePlayer(rig, MakePlayerHit(10f), out HitInfo _));

            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "取引が終われば通常状態へ戻っている。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "Guardian の所有権が残らない。");
            Assert.IsFalse(rig.Guardian.IsTransferInProgress);
            CollectionAssert.Contains(rig.Log.Entered, CompanionState.Protect, "取引の中では Protect を通っている（表示・通知の根拠）。");

            // 通常行動へ戻れる：敵が来れば攻撃を始める。
            MakeEnemy(new Vector3(0f, 0f, 2f));
            rig.Tick(0f);
            Assert.IsTrue(rig.Combat.IsAttacking, "守護のあと通常攻撃を始められる。");
            Assert.AreEqual(1, rig.Combat.AttackCount);
        }

        /// <summary>守護成立の専用通知は犬丸側のチャネルから 1 回だけ出る（表示側はこれで成立表示を出す）。</summary>
        [Test]
        public void Transfer_PublishesOneCompanionSideNotification()
        {
            Rig rig = MakeRig();
            var listener = new TransferCounter();
            rig.Guardian.Transfers.AddListener(listener);

            Assert.IsTrue(TransferLikePlayer(rig, MakePlayerHit(10f, id: 31), out HitInfo _));
            Assert.AreEqual(1, listener.Count);

            // 同じ命中の再送は受理されず、通知も増えない。
            Assert.IsFalse(TransferLikePlayer(rig, MakePlayerHit(10f, id: 31), out HitInfo _));
            Assert.AreEqual(1, listener.Count);
        }

        private sealed class TransferCounter : IGuardianTransferListener
        {
            public int Count;
            public void OnGuardianTransfer(in GuardianTransferEvent transfer) => Count++;
        }

        // ---- E18：致死・Stagger を Protect で上書きしない ----

        [Test]
        public void LethalTransfer_KeepsDown()
        {
            Rig rig = MakeRig();

            Assert.IsTrue(TransferLikePlayer(rig, MakePlayerHit(rig.Receiver.MaxHp + 10f), out HitInfo _));

            Assert.AreEqual(CompanionState.Down, rig.Actor.State, "転送で倒れたなら Down のまま。Protect や Follow で上書きしない。");
            Assert.AreEqual(1, rig.Guardian.TransferCount, "成立は成立として CD・通知を確定する。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner);
        }

        [Test]
        public void StaggeringTransfer_KeepsStagger()
        {
            Rig rig = MakeRig();

            Assert.IsTrue(TransferLikePlayer(rig, MakePlayerHit(10f, flinch: 100f), out HitInfo _));

            Assert.AreEqual(CompanionState.Stagger, rig.Actor.State, "転送でひるんだなら Stagger のまま。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner);
        }

        // ---- E15／E16：攻撃中の防御要求 ----

        /// <summary>E15：判定中（AttackActive）は自動 Guard／Evade を始めない。攻撃は続く。</summary>
        [Test]
        public void ActiveAttack_Danger_DoesNotStartGuardOrEvade()
        {
            Rig rig = MakeRig();
            MakeEnemy(new Vector3(0f, 0f, 2f));
            EnterActiveAttack(rig);

            rig.Danger.HasDanger = true;
            rig.Defense.TickDefense(0.01f);
            Assert.IsFalse(rig.Defense.IsGuarding, "判定中は構えない。");
            Assert.IsFalse(rig.Defense.IsEvading);
            Assert.IsTrue(rig.Combat.IsAttacking, "攻撃は続く（二重行動なし）。");

            rig.Danger.Unblockable = true;
            rig.Defense.TickDefense(0.01f);
            Assert.IsFalse(rig.Defense.IsEvading, "判定中はガード不能な危険でも回避に化けない。");
            Assert.AreEqual(CompanionState.AttackActive, rig.Actor.State);
        }

        /// <summary>E16：予兆（Startup）中の防御要求は旧攻撃を中断して防御へ入り、規定 CD が始まる。</summary>
        [Test]
        public void StartupAttack_Danger_InterruptsAttackAndGuards_WithSnapshotCooldown()
        {
            Rig rig = MakeRig();
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 2f));
            rig.Tick(0f);
            Assert.AreEqual(CompanionState.AttackPrepare, rig.Actor.State, "前提：予兆中。");

            rig.Danger.HasDanger = true;
            rig.Defense.TickDefense(0.01f);

            Assert.IsTrue(rig.Defense.IsGuarding, "予兆中は攻撃を中断して構える。");
            Assert.AreEqual(CompanionState.Guard, rig.Actor.State);
            Assert.IsFalse(rig.Combat.IsAttacking, "奪われた瞬間に攻撃側も止まっている（次の Tick を待たない）。");
            Assert.AreEqual(Cooldown, rig.Combat.CooldownRemaining, 1e-3f, "中断でも規定 CD（開始時 Snapshot）。");

            // 続けて時間を進めても、中断した攻撃の判定は出ない。
            rig.Tick(Startup);
            rig.Tick(Active);
            Assert.AreEqual(0, enemy.ReceivedHits);
        }

        // ---- E19：古い終了通知 ----

        /// <summary>割り込まれた攻撃の残り時間を進めても、古い終了が状態を Chase へ書き換えない。</summary>
        [Test]
        public void InterruptedAttack_LaterTimeDoesNotProduceStaleCompletion()
        {
            Rig rig = MakeRig();
            MakeEnemy(new Vector3(0f, 0f, 2f));
            EnterActiveAttack(rig);
            Assert.IsTrue(TransferLikePlayer(rig, MakePlayerHit(10f), out HitInfo _));
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State);
            int rejectedBefore = rig.States.RejectedCount;

            // 攻撃の残り時間ぶん戦闘だけを進める（追従・索敵の判断は混ぜない）。
            rig.Combat.TickCombat(Active);
            rig.Combat.TickCombat(Recovery);

            Assert.AreNotEqual(CompanionState.AttackRecovery, rig.Actor.State, "中断した攻撃の段が進まない。");
            Assert.AreEqual(1, rig.Combat.AttackCount, "新しい攻撃は CD 中なので始まらない（古い攻撃の続きでもない）。");
            Assert.AreEqual(rejectedBefore, rig.States.RejectedCount, "古い券で状態を書こうとする経路が残っていない。");
        }

        // ---- Disable ----

        /// <summary>取引の途中で無効化されても、Guardian の所有権を残さない。</summary>
        [Test]
        public void Disable_DuringTransaction_ReleasesProtectOwnership()
        {
            Rig rig = MakeRig();
            rig.Guardian.OnTransferAccepted(MakePlayerHit(10f)); // 受理確定だけを模す（被害解決・通知の前）。
            Assert.AreEqual(CompanionActionOwner.Guardian, rig.States.CurrentOwner, "前提：取引中は Guardian が持つ。");

            InvokePrivate(rig.Guardian, "OnDisable");

            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "無効化で所有権を返す。");
            Assert.IsFalse(rig.Guardian.IsTransferInProgress);
            Assert.IsNull(rig.Host.Resolver, "守護対象への登録も外れている。");
        }
    }
}
