using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 状態要求の<b>単一所有</b>を固定する（N19〜N23。P4-FIX F02b）。
    ///
    /// 状態機（<see cref="CompanionStateMachine"/>）は不正遷移を弾くが、<b>「誰の行動か」を持っていない</b>。
    /// そのため次の 2 つが止められなかった。
    ///
    /// <list type="number">
    /// <item><description><b>古い終了通知が新しい状態を壊す。</b>中断された攻撃の「終了」が遅れて届き、
    /// ひるみや復帰後の状態を書き換えてしまう。状態機から見れば通ってよい遷移なので弾けない。</description></item>
    /// <item><description><b>弱い行動が強い行動を奪う。</b>順位表は状態の強さしか見ないので、
    /// 同じ強さの状態へ別の持ち主が入れ替わるのを止められない。</description></item>
    /// </list>
    ///
    /// 行動に引換券（持ち主 × 実行 ID）を持たせ、段の進行と正常終了はその券が一致するときだけ受理する。
    /// <b>順位で正常終了を拒否してはいけない</b>点にも注意がいる（攻撃終了→Chase、ひるみ明け→Follow は
    /// いずれも下位状態への復帰で、これを塞ぐと「終わったのに終われない」状態になる）。
    /// </summary>
    public sealed class CompanionStateArbitrationTests
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
            public CompanionStateArbiter States;
        }

        private Rig MakeRig(CompanionState initial = CompanionState.Follow)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);

            // Actor は調停役を必須にしている（RequireComponent）。手で足さなくても付く。
            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(initial);

            var states = go.GetComponent<CompanionStateArbiter>();
            Assert.IsNotNull(states, "Actor を足せば状態調停役も付く（窓口が 1 つであることを構成で担保する）。");
            states.Bind(actor);

            return new Rig { Root = go, Actor = actor, States = states };
        }

        // ---- N19：古い終了通知 ----

        /// <summary>
        /// 中断された行動の「終了」が遅れて届いても、新しい状態を書き換えない。
        /// これが通ってしまうと、倒れた直後に攻撃終了が届いて Down が Chase に化ける。
        /// </summary>
        [Test]
        public void StaleCompletion_DoesNotOverwriteNewState()
        {
            Rig rig = MakeRig();

            // 戦闘が攻撃を始める。
            Assert.IsTrue(rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.AttackPrepare,
                CompanionStateChangeReason.AttackStarted, out CompanionActionHandle attack));
            Assert.AreEqual(CompanionState.AttackPrepare, rig.Actor.State);

            // 被弾でひるむ。ここで進行中の券は無効になる。
            Assert.IsTrue(rig.States.ForceHit(CompanionState.Stagger, CompanionStateChangeReason.Staggered));
            Assert.AreEqual(CompanionState.Stagger, rig.Actor.State);

            // 遅れて「攻撃終了」が届く。
            bool accepted = rig.States.TryComplete(
                attack, CompanionState.Chase, CompanionStateChangeReason.AttackFinished);

            Assert.IsFalse(accepted, "古い券は受理しない。");
            Assert.AreEqual(CompanionState.Stagger, rig.Actor.State,
                "ひるみが攻撃終了で上書きされない。");
            Assert.Greater(rig.States.RejectedCount, 0, "拒否したことを数えておく（診断のため）。");
        }

        [Test]
        public void StaleAdvance_DoesNotOverwriteNewState()
        {
            Rig rig = MakeRig();

            rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.AttackPrepare,
                CompanionStateChangeReason.AttackStarted, out CompanionActionHandle attack);
            rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated);

            bool accepted = rig.States.TryAdvance(
                attack, CompanionState.AttackActive, CompanionStateChangeReason.AttackAdvanced);

            Assert.IsFalse(accepted, "中断済みの行動は段も進められない。");
            Assert.AreEqual(CompanionState.Down, rig.Actor.State);
        }

        /// <summary>
        /// 同じ持ち主でも<b>別の行動</b>なら別物。持ち主だけで判定すると、どちらも Combat なので通ってしまう。
        /// </summary>
        [Test]
        public void SameOwnerNewAction_InvalidatesTheOldHandle()
        {
            Rig rig = MakeRig();

            rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.AttackPrepare,
                CompanionStateChangeReason.AttackStarted, out CompanionActionHandle attack);

            // 攻撃が中断され、同じ戦闘側が接近を始めた。
            rig.States.Release(attack);
            rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.Chase,
                CompanionStateChangeReason.EngagedTarget, out CompanionActionHandle chase);

            Assert.AreNotEqual(attack.RunId, chase.RunId, "行動を始めるたびに実行 ID が変わる。");
            Assert.IsFalse(rig.States.TryComplete(
                attack, CompanionState.Follow, CompanionStateChangeReason.AttackFinished),
                "古い攻撃の終了通知は、いま進行中の接近を終わらせない。");
            Assert.AreEqual(CompanionState.Chase, rig.Actor.State);
        }

        // ---- N20：正常な下位復帰 ----

        /// <summary>
        /// 正常終了は<b>順位で拒否しない</b>。攻撃終了→Chase、ひるみ明け→Follow はいずれも下位への復帰であり、
        /// ここを順位で塞ぐと「終わったのに終われない」状態になる。
        /// </summary>
        [Test]
        public void NormalCompletion_AllowsDescentToLowerPriority()
        {
            Rig rig = MakeRig();

            rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.AttackPrepare,
                CompanionStateChangeReason.AttackStarted, out CompanionActionHandle attack);
            Assert.IsTrue(rig.States.TryAdvance(
                attack, CompanionState.AttackActive, CompanionStateChangeReason.AttackAdvanced));
            Assert.IsTrue(rig.States.TryAdvance(
                attack, CompanionState.AttackRecovery, CompanionStateChangeReason.AttackAdvanced),
                "AttackActive→Recovery は順位が下がるが正常な進行。");

            Assert.IsTrue(rig.States.TryComplete(
                attack, CompanionState.Chase, CompanionStateChangeReason.AttackFinished),
                "攻撃終了→Chase も順位が下がるが正常。");

            Assert.AreEqual(CompanionState.Chase, rig.Actor.State);
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "終わったら所有権を返す。");
        }

        [Test]
        public void RecoveryFromStagger_IsAccepted()
        {
            Rig rig = MakeRig();
            rig.States.ForceHit(CompanionState.Stagger, CompanionStateChangeReason.Staggered);

            Assert.IsTrue(rig.States.ForceRecover(CompanionState.Follow, CompanionStateChangeReason.Recovered));
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "ひるみ明けは通す。");
        }

        // ---- N21：所有権 ----

        [Test]
        public void WeakerOwner_CannotStealFromStrongerOwner()
        {
            Rig rig = MakeRig();

            Assert.IsTrue(rig.States.TryBegin(
                CompanionActionOwner.Defense, CompanionState.Guard,
                CompanionStateChangeReason.DefensiveAction, out CompanionActionHandle guard));
            Assert.AreEqual(CompanionActionOwner.Defense, rig.States.CurrentOwner);

            // 戦闘（弱い）が防御（強い）から奪おうとする。
            Assert.IsFalse(rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.AttackPrepare,
                CompanionStateChangeReason.AttackStarted, out CompanionActionHandle attack));
            Assert.IsFalse(attack.IsValid, "受理されなければ券も出さない。");
            Assert.AreEqual(CompanionState.Guard, rig.Actor.State, "状態も変わらない。");
            Assert.AreEqual(CompanionActionOwner.Defense, rig.States.CurrentOwner);

            // 守護（さらに強い）は割り込める。
            Assert.IsTrue(rig.States.TryInterrupt(
                CompanionActionOwner.Guardian, CompanionState.Protect,
                CompanionStateChangeReason.Protected, out _));
            Assert.AreEqual(CompanionState.Protect, rig.Actor.State);

            // 防御が持っていた券はもう使えない。
            Assert.IsFalse(rig.States.TryComplete(
                guard, CompanionState.Follow, CompanionStateChangeReason.Recovered));
        }

        [Test]
        public void ReleasedOwnership_LetsAWeakerOwnerIn()
        {
            Rig rig = MakeRig();

            rig.States.TryBegin(
                CompanionActionOwner.Defense, CompanionState.Guard,
                CompanionStateChangeReason.DefensiveAction, out CompanionActionHandle guard);
            Assert.IsTrue(rig.States.Release(guard));

            Assert.IsTrue(rig.States.TryBegin(
                CompanionActionOwner.Follow, CompanionState.Follow,
                CompanionStateChangeReason.FollowResumed, out _),
                "返された後なら弱い持ち主でも取れる。");
        }

        [Test]
        public void Release_OnlyWorksForTheCurrentHolder()
        {
            Rig rig = MakeRig();

            rig.States.TryBegin(
                CompanionActionOwner.Follow, CompanionState.Follow,
                CompanionStateChangeReason.FollowResumed, out CompanionActionHandle follow);
            rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.Chase,
                CompanionStateChangeReason.EngagedTarget, out _);

            Assert.IsFalse(rig.States.Release(follow), "自分のものでない所有権は返せない。");
            Assert.AreEqual(CompanionActionOwner.Combat, rig.States.CurrentOwner);
        }

        // ---- N22：被弾由来は同期・即時 ----

        [Test]
        public void ForcedHit_InvalidatesHandlesSynchronously()
        {
            Rig rig = MakeRig();

            rig.States.TryBegin(
                CompanionActionOwner.Defense, CompanionState.Guard,
                CompanionStateChangeReason.DefensiveAction, out CompanionActionHandle guard);

            int runBefore = rig.States.CurrentRunId;

            // 次の Update を待たず、その場で確定する。
            Assert.IsTrue(rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated));

            Assert.AreEqual(CompanionState.Down, rig.Actor.State, "同期的に確定する。");
            Assert.AreNotEqual(runBefore, rig.States.CurrentRunId, "配ってある券を無効にする。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "所有権も空になる。");
            Assert.IsFalse(rig.States.IsCurrent(guard));
        }

        [Test]
        public void FailedBegin_DoesNotTakeOwnership()
        {
            Rig rig = MakeRig(CompanionState.Down);

            // Down からは理由 AttackStarted では出られない（状態機が拒否する）。
            Assert.IsFalse(rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.AttackPrepare,
                CompanionStateChangeReason.AttackStarted, out _));

            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner,
                "状態が変わらなかったなら所有権も取らない（取ると誰も行動できなくなる）。");
        }

        [Test]
        public void Disable_ReleasesOwnership()
        {
            Rig rig = MakeRig();
            rig.States.TryBegin(
                CompanionActionOwner.Combat, CompanionState.Chase,
                CompanionStateChangeReason.EngagedTarget, out CompanionActionHandle chase);

            MethodInfo onDisable = typeof(CompanionStateArbiter)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(onDisable);
            onDisable.Invoke(rig.States, null);

            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner);
            Assert.IsFalse(rig.States.IsCurrent(chase), "無効化で券も無効になる。");
        }

        // ---- N23：出荷される Prefab の構成 ----

        /// <summary>
        /// 出荷される犬丸 Prefab に窓口が載っていること。載っていないと、各駆動系が移行期の
        /// 直接書き込み経路へ落ちて、単一所有が実機で成立しない。
        /// </summary>
        [Test]
        public void CompanionPrefabHasSingleStateEntryPoint()
        {
            const string path = "Assets/_Project/Prefabs/Companions/PF_Companion_Inumaru.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Assert.Ignore("犬丸 Prefab が未生成のためスキップします（" + path + "）。");
            }

            Assert.IsNotNull(prefab.GetComponent<CompanionStateArbiter>(),
                "状態要求の窓口が Prefab に載っている。");
            Assert.IsNotNull(prefab.GetComponent<CompanionMovementArbiter>(),
                "移動の書き手も Prefab に載っている（F02a）。");
        }
    }

    /// <summary>
    /// 単一所有が<b>実物の駆動系を繋いだ状態で</b>効いていることを見る（N24。P4-FIX F02b）。
    ///
    /// 規則を単体で固定しても、駆動系が窓口を通っていなければ実機では成立しない。
    /// ここでは実 <see cref="CompanionCombatController"/> と実 <see cref="CompanionHitReceiver"/> を繋ぎ、
    /// 攻撃の最中に倒れたあと、その後の Tick で状態が Chase へ戻らないことを確かめる。
    /// </summary>
    public sealed class CompanionStateOwnershipIntegrationTests
    {
        private const int MaxHp = 10;

        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => Momotaro.Gameplay.Enemy.Perception.PerceptionTargetRegistry.Clear();

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
            Momotaro.Gameplay.Enemy.Perception.PerceptionTargetRegistry.Clear();
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

        private sealed class FakeEnemy : MonoBehaviour, ICombatActor, IDamageable,
            Momotaro.Gameplay.Enemy.Threat.IThreatTarget
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.back;
            public int DamageableId => GetInstanceID();
            public int ActorId => GetInstanceID();
            public Vector3 Position => transform.position;
            public bool IsActive => true;
            public bool IsDown => false;
            public float BaseThreat => 0f;
            public float AcquiredThreatMultiplier => 1f;
            public void ReceiveHit(in HitInfo hit) { }
        }

        [Test]
        public void StaggerDuringAttack_LaterFinishDoesNotRestoreChase()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", 2f);
            SetPrivateField(attack, "_useAngle", 120f);
            SetPrivateField(attack, "_cooldownSeconds", 1f);
            SetPrivateField(attack, "_startupSeconds", 0.2f);
            SetPrivateField(attack, "_activeSeconds", 0.1f);
            SetPrivateField(attack, "_recoverySeconds", 0.3f);
            SetPrivateField(attack, "_hpMultiplier", 0.8f);

            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_attackPower", 60f);
            SetPrivateField(data, "_maxHp", MaxHp);
            SetPrivateField(data, "_defense", 0f);
            SetPrivateField(data, "_basicAttack", attack);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var motor = go.AddComponent<CompanionMotor>();
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);
            InvokePrivate(combat, "OnEnable");
            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            var enemyGo = new GameObject("Enemy");
            _spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 1f);
            var enemy = enemyGo.AddComponent<FakeEnemy>();
            Momotaro.Gameplay.Enemy.Perception.PerceptionTargetRegistry.Register(enemy);

            // 攻撃を始める。
            tracker.TickTargeting();
            combat.TickCombat(0f);
            Assert.IsTrue(combat.IsAttacking, "前提：攻撃が始まっている。");
            Assert.AreEqual(CompanionState.AttackPrepare, actor.State);

            // 致死ダメージを受ける。被弾側が Down を同期的に確定する。
            receiver.ReceiveHit(new HitInfo(
                enemy, receiver, Vector3.back, Vector3.zero,
                new HitDamage(MaxHp * 10f, 0f, 0f),
                guardable: false, justGuardable: false, hitId: HitId.Single(801)));

            Assert.AreEqual(CompanionState.Down, actor.State, "前提：倒れている。");
            Assert.IsFalse(combat.IsAttacking, "倒れた瞬間に攻撃が消える。");

            // 攻撃の全長より長く進める。古い攻撃の「終了」が状態を書き換えないこと。
            for (int i = 0; i < 10; i++)
            {
                tracker.TickTargeting();
                combat.TickCombat(0.1f);
            }

            Assert.AreEqual(CompanionState.Down, actor.State,
                "倒れたあとに攻撃終了が届いても Chase へ戻らない。");
        }
    }
}
