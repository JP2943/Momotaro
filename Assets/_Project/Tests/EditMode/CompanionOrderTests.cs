using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Gameplay.Modes;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-07B：プレイヤー指示（<see cref="CompanionOrders"/>）と自動判断の優先順位を検証する。
    ///
    /// 固定するのは境界そのもの。<b>指示は「何をしに行くか」を止められるが、「殴られたときどうするか」は
    /// 止められない。</b>「ここで待て」で追従・探索・敵への接近は止まるが、間合いに入ってきた敵への反撃、
    /// 危険に対する構え、主人公を庇うことは止まらない。ここを取り違えると、置いていかれた仲間が
    /// 黙って殴られ続ける（あるいは待てと言ったのに戻ってくる）。
    ///
    /// 表そのもの（純粋関数）と、表が実際に駆動へ効いていることを分けて置く。
    /// 後者は実物の追従・探索・戦闘を同じ GameObject に載せて確かめる（CLAUDE.md の「繋ぎ目」）。
    /// </summary>
    public sealed class CompanionOrderTests
    {
        private const float MoveSpeed = 5f;
        private const float StopDistance = 0.35f;
        private const float WarpDistance = 8f;
        private const float UseRange = 2f;
        private const float InvestigateRange = 6f;
        private const float LeashDistance = 6f;

        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            CompanionActivityProvider.Current = null;
            GameModeProvider.Current = null;
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
        }

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
            CompanionActivityProvider.Current = null;
            GameModeProvider.Current = null;
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
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

        /// <summary>危険を任意に出せる観測（防御が指示に縛られないことの確認用）。</summary>
        private sealed class FakeDanger : IEnemyDangerSense
        {
            public bool HasDanger { get; set; }

            public EnemyDangerStimulus Sense(Vector3 selfPosition, Vector3 selfForward, int selfDamageableId)
            {
                return HasDanger
                    ? new EnemyDangerStimulus(selfPosition + Vector3.forward * 2f, Vector3.back, false)
                    : EnemyDangerStimulus.None;
            }
        }

        private AttackData MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", UseRange);
            SetPrivateField(attack, "_useAngle", 90f);
            SetPrivateField(attack, "_cooldownSeconds", 1.5f);
            SetPrivateField(attack, "_startupSeconds", 0.2f);
            SetPrivateField(attack, "_activeSeconds", 0.1f);
            SetPrivateField(attack, "_recoverySeconds", 0.3f);
            SetPrivateField(attack, "_hpMultiplier", 0.8f);
            return attack;
        }

        private CompanionData MakeData(AttackData attack)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_moveSpeed", MoveSpeed);
            SetPrivateField(data, "_followStopDistance", StopDistance);
            SetPrivateField(data, "_followResumeDistance", 0.8f);
            SetPrivateField(data, "_warpDistance", WarpDistance);
            SetPrivateField(data, "_canInvestigate", true);
            SetPrivateField(data, "_investigateRange", InvestigateRange);
            SetPrivateField(data, "_investigateSeconds", 1f);
            SetPrivateField(data, "_investigateLeashDistance", LeashDistance);
            SetPrivateField(data, "_investigateCooldownSeconds", 0f);
            SetPrivateField(data, "_canGuard", true);
            SetPrivateField(data, "_guardCooldownSeconds", 3f);
            SetPrivateField(data, "_canEvade", true);
            SetPrivateField(data, "_evadeCooldownSeconds", 4f);
            if (attack != null)
            {
                SetPrivateField(data, "_attackPower", 60f);
                SetPrivateField(data, "_basicAttack", attack);
            }

            return data;
        }

        private sealed class Rig
        {
            public GameObject Root;
            public GameObject Leader;
            public CompanionActor Actor;
            public CompanionOrders Orders;
            public CompanionStateArbiter States;
            public CompanionMovementArbiter Movement;
            public CompanionMotor Motor;
            public CompanionFollowController Follow;
            public CompanionInvestigationController Investigation;
            public CompanionTargetTracker Tracker;
            public CompanionCombatController Combat;
            public CompanionDefenseController Defense;
            public FakeDanger Danger;
        }

        /// <summary>実物一式を載せた仲間（指示・追従・探索・戦闘・防御）。</summary>
        private Rig MakeCompanion(Vector3 position, Vector3 leaderPosition)
        {
            var leader = new GameObject("Player");
            _spawned.Add(leader);
            leader.transform.position = leaderPosition;

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData(MakeAttack()));
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var orders = go.AddComponent<CompanionOrders>();
            var motor = go.AddComponent<CompanionMotor>(); // Rigidbody・移動調停役は RequireComponent で付く。

            var follow = go.AddComponent<CompanionFollowController>();
            follow.Bind(leader.transform, actor, motor);

            var investigation = go.AddComponent<CompanionInvestigationController>();
            investigation.Bind(actor, follow);

            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);
            InvokePrivate(combat, "OnEnable");

            var defense = go.AddComponent<CompanionDefenseController>();
            defense.Bind(actor);
            var danger = new FakeDanger();
            defense.SetDangerSense(danger);

            return new Rig
            {
                Root = go,
                Leader = leader,
                Actor = actor,
                Orders = orders,
                States = go.GetComponent<CompanionStateArbiter>(),
                Movement = go.GetComponent<CompanionMovementArbiter>(),
                Motor = motor,
                Follow = follow,
                Investigation = investigation,
                Tracker = tracker,
                Combat = combat,
                Defense = defense,
                Danger = danger,
            };
        }

        private CompanionInvestigationPoint MakePoint(Vector3 position)
        {
            var go = new GameObject("InvestigationPoint");
            _spawned.Add(go);
            go.transform.position = position;
            var point = go.AddComponent<CompanionInvestigationPoint>();
            InvokePrivate(point, "Awake");
            InvokePrivate(point, "OnEnable");
            return point;
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

        // ================================================================
        // 1. 表そのもの
        // ================================================================

        /// <summary>
        /// 「ここで待て」は<b>行きたいこと</b>だけを止める。追従・探索・敵への接近は止まり、
        /// 自衛と守護は止まらない。この境界が指示の意味そのもの。
        /// </summary>
        [Test]
        public void WaitOrder_StopsGoingSomewhere_ButNeverSelfDefence()
        {
            Assert.IsFalse(CompanionOrderRules.MayFollowLeader(CompanionOrder.Wait), "隊列へは戻らない。");
            Assert.IsFalse(CompanionOrderRules.MayInvestigate(CompanionOrder.Wait), "調べに行かない。");
            Assert.IsFalse(CompanionOrderRules.MayApproachEnemies(CompanionOrder.Wait), "敵へ寄っていかない。");

            Assert.IsTrue(CompanionOrderRules.MayDefendSelf(CompanionOrder.Wait),
                "自衛は止めない（待てと言われたからといって殴られるに任せるのは指示の意味ではない）。");
            Assert.IsTrue(CompanionOrderRules.MayGuardPlayer(CompanionOrder.Wait),
                "主人公を庇うのも止めない（距離が離れれば守護の条件で自然に成立しなくなる）。");
        }

        /// <summary>既定（ついて来い）は何も止めない。</summary>
        [Test]
        public void FollowOrder_AllowsEverything()
        {
            Assert.IsTrue(CompanionOrderRules.MayFollowLeader(CompanionOrder.Follow));
            Assert.IsTrue(CompanionOrderRules.MayInvestigate(CompanionOrder.Follow));
            Assert.IsTrue(CompanionOrderRules.MayApproachEnemies(CompanionOrder.Follow));
            Assert.IsTrue(CompanionOrderRules.MayDefendSelf(CompanionOrder.Follow));
            Assert.IsTrue(CompanionOrderRules.MayGuardPlayer(CompanionOrder.Follow));
        }

        /// <summary>指示は同じものを送り直しても「変わった」ことにしない（無駄な通知を出さない）。</summary>
        [Test]
        public void SetOrder_IsIdempotent()
        {
            var go = new GameObject("Orders");
            _spawned.Add(go);
            var orders = go.AddComponent<CompanionOrders>();

            Assert.AreEqual(CompanionOrder.Follow, orders.Current, "既定はついて来い。");
            Assert.IsTrue(orders.SetOrder(CompanionOrder.Wait));
            Assert.IsFalse(orders.SetOrder(CompanionOrder.Wait), "同じ指示の再送は変更ではない。");
            Assert.AreEqual(1, orders.ChangeCount);

            orders.ResetOrders();
            Assert.AreEqual(CompanionOrder.Follow, orders.Current, "初期化で既定へ戻る。");
        }

        /// <summary>
        /// 判断関数のうえで、待機は<b>接近だけ</b>を止める。間合いの内側の攻撃・待機は変わらない。
        /// </summary>
        [Test]
        public void Engagement_WaitStopsOnlyTheApproach()
        {
            CompanionAttackSettings settings = CompanionAttackSettings.From(MakeAttack());

            Assert.AreEqual(CompanionEngageDecision.Chase,
                CompanionEngagement.Decide(true, true, 5f, 0f, settings, 0f, mayApproach: true),
                "既定は寄っていく。");
            Assert.AreEqual(CompanionEngageDecision.Idle,
                CompanionEngagement.Decide(true, true, 5f, 0f, settings, 0f, mayApproach: false),
                "待機中は遠い敵に関与しない（Hold にすると戦闘扱いのまま待機の姿勢へ入れない）。");

            Assert.AreEqual(CompanionEngageDecision.Attack,
                CompanionEngagement.Decide(true, true, 0.5f, 0f, settings, 0f, mayApproach: false),
                "間合いに入ってきた敵は待機中でも殴る。");
            Assert.AreEqual(CompanionEngageDecision.Hold,
                CompanionEngagement.Decide(true, true, 0.5f, 0f, settings, 1f, mayApproach: false),
                "クールダウン待ちなら、待機中でもその場で構えて待つ。");
        }

        // ================================================================
        // 2. 実物の駆動に効いているか
        // ================================================================

        /// <summary>
        /// 「ここで待て」で隊列へ戻らない。<b>主人公が離れてもワープで引き戻されない</b>のが要点。
        /// 移動意図を出さないだけでは、判断モデルが距離超過を見つけてワープを成立させてしまう。
        /// </summary>
        [Test]
        public void WaitOrder_DoesNotFollow_AndDoesNotWarpBack()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
            rig.Orders.SetOrder(CompanionOrder.Wait);

            // 主人公がワープ距離を大きく超えて離れる。
            rig.Leader.transform.position = new Vector3(0f, 0f, WarpDistance * 3f);
            Vector3 before = rig.Root.transform.position;

            for (int i = 0; i < 20; i++)
            {
                rig.Follow.TickFollow(0.05f);
            }

            Assert.IsTrue(rig.Follow.IsWaitingByOrder, "待機の指示を受けている。");
            Assert.AreEqual(before, rig.Root.transform.position,
                "待てと言われたら、主人公がどれだけ離れてもワープで戻らない。");
            Assert.AreEqual(CompanionState.Idle, rig.Actor.State, "待機の状態に出す。");
            Assert.AreEqual(CompanionMoveKind.Stop, rig.Movement.LastApplied, "移動は止まっている。");
        }

        /// <summary>指示を戻せば、また追従を始める（片道の指示にしない）。</summary>
        [Test]
        public void FollowOrderAgain_ResumesFollowing()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 5f));
            rig.Orders.SetOrder(CompanionOrder.Wait);
            rig.Follow.TickFollow(0.05f);
            Assert.AreEqual(CompanionState.Idle, rig.Actor.State, "前提：待機している。");

            rig.Orders.SetOrder(CompanionOrder.Follow);
            rig.Follow.TickFollow(0.05f);

            Assert.IsFalse(rig.Follow.IsWaitingByOrder);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "指示を戻せば追従へ復帰する。");
            Assert.AreEqual(CompanionMovementOwner.Follow, rig.Movement.Owner);
        }

        /// <summary>待機中は調べに行かない（探索より指示が強い）。</summary>
        [Test]
        public void WaitOrder_StopsInvestigation()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
            MakePoint(new Vector3(0f, 0f, 2f));

            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsTrue(rig.Investigation.IsInvestigating, "前提：指示が無ければ調べに行く。");

            rig.Orders.SetOrder(CompanionOrder.Wait);
            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsFalse(rig.Investigation.IsInvestigating, "待機を命じられたら調査を打ち切る。");
            Assert.IsFalse(rig.Investigation.MayInvestigateByOrder);

            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsFalse(rig.Investigation.IsInvestigating, "打ち切ったあとも始め直さない。");
        }

        /// <summary>待機中は遠い敵へ寄っていかない（実物の索敵・戦闘を通して確認）。</summary>
        [Test]
        public void WaitOrder_DoesNotChaseDistantEnemy()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
            rig.Orders.SetOrder(CompanionOrder.Wait);
            MakeEnemy(new Vector3(0f, 0f, 5f));

            rig.Tracker.TickTargeting();
            rig.Combat.TickCombat(0.05f);

            Assert.IsFalse(rig.Combat.MayApproachByOrder);
            Assert.AreEqual(CompanionEngageDecision.Idle, rig.Combat.Decision,
                "待機中は遠い敵に関与しない。");
            Assert.IsFalse(rig.Combat.IsEngaged, "戦闘扱いにならないので、追従が待機の姿勢を保てる。");
        }

        /// <summary>
        /// <b>待機中でも、間合いに入ってきた敵は殴る。</b>指示は自衛を止めない。
        /// 実物の索敵・戦闘・命中経路を通し、敵が実際に damage を受けるところまで確かめる。
        /// </summary>
        [Test]
        public void WaitOrder_StillFightsBackWhenEnemyComesClose()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
            rig.Orders.SetOrder(CompanionOrder.Wait);
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1f)); // 間合いの内側へ寄ってきた。

            rig.Tracker.TickTargeting();
            rig.Combat.TickCombat(0f);

            Assert.AreEqual(CompanionEngageDecision.Attack, rig.Combat.Decision,
                "待機中でも目の前の敵には攻撃を始める。");
            Assert.IsTrue(rig.Combat.IsAttacking);

            rig.Combat.TickCombat(0.2f); // 予兆 → 判定段。
            Assert.IsTrue(rig.Combat.TryApplyHit(enemy, enemy, Vector3.zero),
                "命中も通る（自衛は指示で止まらない）。");
            Assert.AreEqual(1, enemy.ReceivedHits);
        }

        /// <summary>待機中でも危険には構える（防御は指示に縛られない）。</summary>
        [Test]
        public void WaitOrder_StillGuardsAgainstDanger()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
            rig.Orders.SetOrder(CompanionOrder.Wait);
            rig.Danger.HasDanger = true;

            rig.Defense.TickDefense(0.05f);

            Assert.IsTrue(rig.Defense.IsGuarding, "待機を命じられていても、危険には構える。");
            Assert.AreEqual(CompanionState.Guard, rig.Actor.State);
        }

        /// <summary>
        /// 指示コンポーネントが無い仲間は、従来どおり「常について来い」で動く。
        /// 指示の仕組みを入れる前に組んだ構成・テストを壊さないための約束。
        /// </summary>
        [Test]
        public void WithoutOrdersComponent_BehavesAsFollow()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 5f));
            Object.DestroyImmediate(rig.Orders);
            SetPrivateField(rig.Follow, "_orders", null);
            SetPrivateField(rig.Combat, "_orders", null);
            SetPrivateField(rig.Investigation, "_orders", null);

            rig.Follow.TickFollow(0.05f);

            Assert.IsFalse(rig.Follow.IsWaitingByOrder, "指示が無ければ待機しない。");
            Assert.IsTrue(rig.Combat.MayApproachByOrder, "指示が無ければ寄っていける。");
            Assert.IsTrue(rig.Investigation.MayInvestigateByOrder, "指示が無ければ調べに行ける。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State);
        }
    }
}
