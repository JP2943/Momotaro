using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Gameplay.Modes;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-07A：探索行動（<see cref="CompanionInvestigationController"/>）を検証する。
    ///
    /// 固定するのは 3 つ。<b>調べに行って調べ終えること</b>、<b>暇なときにしかやらないこと</b>
    /// （戦闘・会話・Pause では譲る／止まる）、そして<b>紐から出ないこと</b>。
    ///
    /// 探索は「調停役に譲る」ことで成り立っているので、駆動を単体で叩くだけでは足りない。
    /// 本物の <see cref="CompanionStateArbiter"/>・<see cref="CompanionMovementArbiter"/>・
    /// <see cref="CompanionFollowController"/>・<see cref="CompanionCombatController"/> を同じ
    /// GameObject に載せ、実際に譲り合わせて確かめる（CLAUDE.md の「繋ぎ目」）。
    /// 調査地点も本物のレジストリ経由で見つけさせる（手で渡さない）。
    /// </summary>
    public sealed class CompanionInvestigationTests
    {
        private const float InvestigateRange = 6f;
        private const float InvestigateSeconds = 1.0f;
        private const float LeashDistance = 6f;
        private const float InvestigateCooldown = 2f;
        private const float MoveSpeed = 5f;
        private const float StopDistance = 0.35f;

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

        /// <summary>活動許可を任意に差し替える供給元（戦闘中・会話中・Pause を作る）。</summary>
        private sealed class FakeActivity : ICompanionActivitySource
        {
            public CompanionActivity Value { get; set; } = CompanionActivity.FreeRoam;
            public CompanionActivity Current => Value;
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

            public void ReceiveHit(in HitInfo hit)
            {
            }
        }

        private AttackData MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", 2f);
            SetPrivateField(attack, "_useAngle", 90f);
            SetPrivateField(attack, "_cooldownSeconds", 1.5f);
            SetPrivateField(attack, "_startupSeconds", 0.2f);
            SetPrivateField(attack, "_activeSeconds", 0.1f);
            SetPrivateField(attack, "_recoverySeconds", 0.3f);
            SetPrivateField(attack, "_hpMultiplier", 0.8f);
            return attack;
        }

        private CompanionData MakeData(bool canInvestigate = true, AttackData attack = null)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_moveSpeed", MoveSpeed);
            SetPrivateField(data, "_followStopDistance", StopDistance);
            SetPrivateField(data, "_canInvestigate", canInvestigate);
            SetPrivateField(data, "_investigateRange", InvestigateRange);
            SetPrivateField(data, "_investigateSeconds", InvestigateSeconds);
            SetPrivateField(data, "_investigateLeashDistance", LeashDistance);
            SetPrivateField(data, "_investigateCooldownSeconds", InvestigateCooldown);
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
            public CompanionStateArbiter States;
            public CompanionMovementArbiter Movement;
            public CompanionMotor Motor;
            public CompanionFollowController Follow;
            public CompanionInvestigationController Investigation;
            public CompanionCombatController Combat;
            public CompanionTargetTracker Tracker;
        }

        /// <summary>実物を同じ GameObject に載せた仲間（調停役は RequireComponent で付く）。</summary>
        private Rig MakeCompanion(
            Vector3 position = default, bool canInvestigate = true, bool withCombat = false)
        {
            var leader = new GameObject("Player");
            _spawned.Add(leader);
            leader.transform.position = Vector3.zero;

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData(canInvestigate, withCombat ? MakeAttack() : null));
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var motor = go.AddComponent<CompanionMotor>(); // Rigidbody・移動調停役は RequireComponent で付く。
            var follow = go.AddComponent<CompanionFollowController>();
            follow.Bind(leader.transform, actor, motor);

            var investigation = go.AddComponent<CompanionInvestigationController>();
            investigation.Bind(actor, follow);

            var rig = new Rig
            {
                Root = go,
                Leader = leader,
                Actor = actor,
                States = go.GetComponent<CompanionStateArbiter>(),
                Movement = go.GetComponent<CompanionMovementArbiter>(),
                Motor = motor,
                Follow = follow,
                Investigation = investigation,
            };

            if (withCombat)
            {
                rig.Tracker = go.AddComponent<CompanionTargetTracker>();
                rig.Tracker.Bind(actor);
                rig.Combat = go.AddComponent<CompanionCombatController>();
                rig.Combat.Bind(actor, motor, rig.Tracker);
                InvokePrivate(rig.Combat, "OnEnable");
            }

            return rig;
        }

        /// <summary>本物の調査地点を Scene へ置く（レジストリへは自分で登録される）。</summary>
        private CompanionInvestigationPoint MakePoint(Vector3 position)
        {
            var go = new GameObject("InvestigationPoint");
            _spawned.Add(go);
            go.transform.position = position;
            var point = go.AddComponent<CompanionInvestigationPoint>();

            // EditMode では Awake／OnEnable が走らないので明示的に呼ぶ（登録がここで起きる）。
            InvokePrivate(point, "Awake");
            InvokePrivate(point, "OnEnable");
            return point;
        }

        /// <summary>地点に着くまで進める（着けなければ失敗させる）。</summary>
        private static void WalkToPoint(Rig rig, CompanionInvestigationPoint point)
        {
            for (int i = 0; i < 200 && !rig.Investigation.IsExamining; i++)
            {
                rig.Investigation.TickInvestigation(0.05f);

                // Motor は物理で動くので、テストでは調停役が受理した目標へ手で寄せる
                //（移動の正しさは F02a のテストが持っている。ここで見たいのは探索の進行）。
                if (rig.Movement.LastApplied == CompanionMoveKind.Move)
                {
                    rig.Root.transform.position = Vector3.MoveTowards(
                        rig.Root.transform.position, point.transform.position, MoveSpeed * 0.05f);
                }
            }

            Assert.IsTrue(rig.Investigation.IsExamining, "前提：調査地点まで到達して調べ始めている。");
        }

        // ================================================================
        // 1. 調べに行って、調べ終える
        // ================================================================

        /// <summary>
        /// <b>レジストリ経由で地点を見つけ、行って、調べ終える。</b>地点は手で渡さない
        /// （敵をレジストリへ登録し忘れて仲間の候補が常に 0 件だった事故と同じ形なので、本物を通す）。
        /// </summary>
        [Test]
        public void FindsPointThroughRegistry_WalksThere_AndFinishes()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            CompanionInvestigationPoint point = MakePoint(new Vector3(0f, 0f, 3f));

            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsTrue(rig.Investigation.IsInvestigating, "レジストリから地点を見つけて調査を始める。");
            Assert.AreEqual(CompanionState.Investigate, rig.Actor.State, "状態にも出す。");
            Assert.AreEqual(CompanionMovementOwner.Investigate, rig.Movement.Owner, "移動も探索が握る。");

            WalkToPoint(rig, point);
            rig.Investigation.TickInvestigation(InvestigateSeconds);

            Assert.AreEqual(1, point.InvestigatedCount, "地点へ「調べ終えた」と伝える。");
            Assert.AreEqual(rig.Actor.ActorId, point.LastInvestigatedBy, "誰が調べたかも伝える。");
            Assert.IsFalse(point.IsAvailable, "調べ済みの地点はもう候補にならない。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "終わったら追従へ戻る。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "行動の所有権を返す。");
            Assert.AreEqual(CompanionMovementOwner.None, rig.Movement.Owner, "移動の所有権も返す。");
            Assert.AreEqual(1, rig.Investigation.CompletedCount);
        }

        /// <summary>調べ終えた直後は次を探さない（クールダウン）。連続で走り回らせない。</summary>
        [Test]
        public void AfterFinishing_WaitsForCooldownBeforeNextPoint()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            CompanionInvestigationPoint first = MakePoint(new Vector3(0f, 0f, 1f));
            MakePoint(new Vector3(0f, 0f, 2f));

            rig.Investigation.TickInvestigation(0.05f);
            WalkToPoint(rig, first);
            rig.Investigation.TickInvestigation(InvestigateSeconds);
            Assert.AreEqual(1, rig.Investigation.CompletedCount, "前提：1 箇所調べ終えた。");

            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsFalse(rig.Investigation.IsInvestigating, "クールダウン中は次を始めない。");

            rig.Investigation.TickInvestigation(InvestigateCooldown);
            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsTrue(rig.Investigation.IsInvestigating, "明けたら次の地点を調べに行く。");
        }

        /// <summary>Data で探索を切れる（猿・雉のように調べない仲間を作れる）。</summary>
        [Test]
        public void WithoutInvestigateAbility_NeverStarts()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f), canInvestigate: false);
            MakePoint(new Vector3(0f, 0f, 2f));

            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsFalse(rig.Investigation.IsInvestigating, "Data で探索を切れる。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State);
        }

        // ================================================================
        // 2. 紐（主人公から離れすぎない）
        // ================================================================

        /// <summary>
        /// 主人公から紐の外にある地点へは行かない。行かせると追従のワープ距離を超え、
        /// 「調べに行ったのに隊列へ瞬間移動して戻る」という壊れ方をする。
        /// </summary>
        [Test]
        public void PointBeyondLeashFromLeader_IsNotChosen()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            MakePoint(new Vector3(0f, 0f, LeashDistance + 2f)); // 主人公（原点）から紐の外。

            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsFalse(rig.Investigation.IsInvestigating,
                "紐の外の地点は選ばない（置き去りとワープを誘発する）。");
        }

        /// <summary>自分の探索半径の外にある地点も選ばない（気付いていない）。</summary>
        [Test]
        public void PointBeyondOwnRange_IsNotChosen()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, -InvestigateRange - 2f));
            MakePoint(new Vector3(0f, 0f, 1f));

            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsFalse(rig.Investigation.IsInvestigating, "遠すぎる地点には気付かない。");
        }

        /// <summary>候補が複数あるときは最寄りを選ぶ。</summary>
        [Test]
        public void ChoosesNearestAvailablePoint()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 0f));
            MakePoint(new Vector3(0f, 0f, 4f));
            CompanionInvestigationPoint near = MakePoint(new Vector3(0f, 0f, 1f));

            rig.Investigation.TickInvestigation(0.05f);

            Assert.AreSame(near, rig.Investigation.CurrentPoint, "最寄りを選ぶ。");
        }

        // ================================================================
        // 3. 暇なときだけやる
        // ================================================================

        /// <summary>戦闘中（Wave 幕間・開始待ちを含む）は探索しない。活動 Context が判断の正本。</summary>
        [Test]
        public void DuringCombatActivity_DoesNotStart()
        {
            var activity = new FakeActivity { Value = CompanionActivity.Fighting };
            CompanionActivityProvider.Current = activity;

            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            MakePoint(new Vector3(0f, 0f, 2f));

            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsFalse(rig.Investigation.IsInvestigating, "戦闘中は調べ始めない。");
        }

        /// <summary>調査の途中で戦闘が始まったら中断して追従へ戻る。</summary>
        [Test]
        public void WhenCombatStarts_AbandonsAndReturnsToFollow()
        {
            var activity = new FakeActivity { Value = CompanionActivity.FreeRoam };
            CompanionActivityProvider.Current = activity;

            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            MakePoint(new Vector3(0f, 0f, 3f));
            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsTrue(rig.Investigation.IsInvestigating, "前提：調査中。");

            activity.Value = CompanionActivity.Fighting;
            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsFalse(rig.Investigation.IsInvestigating, "調査を中断する。");
            Assert.AreEqual(1, rig.Investigation.InterruptedCount);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "追従へ戻る。");
            Assert.AreEqual(CompanionMovementOwner.None, rig.Movement.Owner, "移動も返す。");
        }

        /// <summary>会話・イベント（DiscardOngoing）では進行中の調査を捨てる。</summary>
        [Test]
        public void DuringDialogue_DiscardsOngoingInvestigation()
        {
            var activity = new FakeActivity { Value = CompanionActivity.FreeRoam };
            CompanionActivityProvider.Current = activity;

            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            MakePoint(new Vector3(0f, 0f, 3f));
            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsTrue(rig.Investigation.IsInvestigating, "前提：調査中。");

            activity.Value = CompanionActivity.Interrupted(encounterActive: false);
            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsFalse(rig.Investigation.IsInvestigating, "会話中は調査を捨てる。");
        }

        /// <summary>Pause は凍結（捨てない・進めない）。時計も止まる。</summary>
        [Test]
        public void DuringPause_FreezesWithoutLosingProgress()
        {
            var activity = new FakeActivity { Value = CompanionActivity.FreeRoam };
            CompanionActivityProvider.Current = activity;

            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            CompanionInvestigationPoint point = MakePoint(new Vector3(0f, 0f, 1f));
            rig.Investigation.TickInvestigation(0.05f);
            WalkToPoint(rig, point);
            rig.Investigation.TickInvestigation(0.2f);
            float elapsed = rig.Investigation.ExamineElapsed;
            Assert.Greater(elapsed, 0f, "前提：調べ始めている。");

            activity.Value = CompanionActivity.Paused(encounterActive: false);
            rig.Investigation.TickInvestigation(1f);

            Assert.IsTrue(rig.Investigation.IsInvestigating, "Pause では調査を捨てない。");
            Assert.AreEqual(elapsed, rig.Investigation.ExamineElapsed, 1e-4f, "時計も進めない。");
            Assert.AreEqual(0, point.InvestigatedCount, "Pause 中に調べ終わらない。");
        }

        // ================================================================
        // 4. 他の駆動との譲り合い（実物同士）
        // ================================================================

        /// <summary>
        /// <b>調査中は追従が引き戻さない。</b>追従のほうが弱い持ち主なので、意図を出しても通らない。
        /// 判断そのものも止めておかないと、離れているあいだに距離超過のワープが成立してしまう。
        /// </summary>
        [Test]
        public void WhileInvestigating_FollowDoesNotPullBack()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            MakePoint(new Vector3(0f, 0f, 4f));

            rig.Investigation.TickInvestigation(0.05f);
            Assert.AreEqual(CompanionMovementOwner.Investigate, rig.Movement.Owner, "前提：探索が移動を握った。");

            rig.Follow.TickFollow(0.05f);

            Assert.IsTrue(rig.Follow.IsYieldingToStrongerMovementOwner, "追従は譲る。");
            Assert.AreEqual(CompanionMovementOwner.Investigate, rig.Movement.Owner, "所有権を奪われない。");
            Assert.AreEqual(CompanionState.Investigate, rig.Actor.State, "状態も追従に書き換えられない。");
        }

        /// <summary>
        /// <b>実物の戦闘駆動が敵を見つけたら、調査を中断して戦いに行ける。</b>
        /// 許可表（レビュー §5）で調査中の攻撃開始が「中断」として通ることを、駆動同士で確かめる。
        /// </summary>
        [Test]
        public void RealCombat_InterruptsInvestigation()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f), withCombat: true);
            MakePoint(new Vector3(0f, 0f, 3f));

            rig.Investigation.TickInvestigation(0.05f);
            Assert.AreEqual(CompanionState.Investigate, rig.Actor.State, "前提：調査中。");

            var enemyGo = new GameObject("Enemy");
            _spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 2f);
            PerceptionTargetRegistry.Register(enemyGo.AddComponent<FakeEnemy>());

            rig.Tracker.TickTargeting();
            rig.Combat.TickCombat(0f);

            Assert.AreNotEqual(CompanionState.Investigate, rig.Actor.State,
                "敵が出たら調べている場合ではない（§5：調査中でも戦闘は割り込める）。");
            Assert.AreEqual(CompanionActionOwner.Combat, rig.States.CurrentOwner, "行動は戦闘が握る。");

            // 探索側は次の Tick で「取られた」と気付き、後始末する。
            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsFalse(rig.Investigation.IsInvestigating, "調査側は自分が中断されたことを認識する。");
            Assert.AreEqual(1, rig.Investigation.InterruptedCount);
        }

        // ================================================================
        // 5. 後始末
        // ================================================================

        /// <summary>無効化で行動・移動の所有権を残さない。</summary>
        [Test]
        public void Disable_LeavesNoOwnership()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            MakePoint(new Vector3(0f, 0f, 3f));
            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsTrue(rig.Investigation.IsInvestigating, "前提：調査中。");

            InvokePrivate(rig.Investigation, "OnDisable");

            Assert.IsFalse(rig.Investigation.IsInvestigating);
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "行動の所有権を残さない。");
            Assert.AreEqual(CompanionMovementOwner.None, rig.Movement.Owner, "移動の所有権も残さない。");
        }

        /// <summary>地点が無効化されたらレジストリから外れる（破棄済み参照を持ち越さない）。</summary>
        [Test]
        public void DisabledPoint_LeavesRegistry()
        {
            CompanionInvestigationPoint point = MakePoint(new Vector3(0f, 0f, 1f));
            Assert.AreEqual(1, InvestigationPointRegistry.Count, "前提：登録されている。");

            InvokePrivate(point, "OnDisable");

            Assert.AreEqual(0, InvestigationPointRegistry.Count, "無効化・Scene 離脱で参照を残さない。");
        }

        /// <summary>調べている途中で地点が消えたら空振りとして戻る（例外にしない）。</summary>
        [Test]
        public void PointDisappearsMidway_ReturnsToFollow()
        {
            Rig rig = MakeCompanion(new Vector3(0f, 0f, 1f));
            CompanionInvestigationPoint point = MakePoint(new Vector3(0f, 0f, 3f));
            rig.Investigation.TickInvestigation(0.05f);
            Assert.IsTrue(rig.Investigation.IsInvestigating, "前提：調査中。");

            // EditMode では SetActive で OnDisable は走らないが、isActiveAndEnabled が落ちるので
            // 地点は「調べに行けない」状態になる（レジストリからの離脱は別のテストで見ている）。
            point.gameObject.SetActive(false);
            Assert.IsFalse(point.IsAvailable, "前提：地点が調べられない状態になった。");

            rig.Investigation.TickInvestigation(0.05f);

            Assert.IsFalse(rig.Investigation.IsInvestigating);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "追従へ戻る。");
        }
    }
}
