using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Gameplay.Modes;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 活動停止が<b>実際に効いているか</b>を、実 Update・実物理で確認する（N13〜N15。P4-FIX F05）。
    ///
    /// EditMode では規則（どの軸が立つか）を固定した。ここで見るのは、その規則が実機の駆動へ届いているか。
    /// <b>入力を閉じるだけでは止まらない</b>のがこの機能の要点で、実際に確かめるべきは 3 つある。
    ///
    /// <list type="number">
    /// <item><description><c>timeScale</c> が 1 のままでも位置と時計が止まること（Pause を timeScale で作らない設計）。</description></item>
    /// <item><description>復帰したときに、同じ攻撃の段・既命中集合が保たれ、同じ対象へ二度目の被害を出さないこと。</description></item>
    /// <item><description>会話・イベントは<b>破棄</b>であり、復帰時に古い Active を再開しないこと。</description></item>
    /// </list>
    /// </summary>
    public sealed class CompanionActivityGatePlayTests : CompanionActivityFixture
    {
        private readonly List<Object> _spawned = new List<Object>();
        private float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            GameModeProvider.Current = null;
            CompanionActivityTestSource.InstallFreeRoam(); // 明示 Fake（P4-FIX-R2。供給元が無いと停止する）
            PerceptionTargetRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            CompanionActivityProvider.Current = null;
            GameModeProvider.Current = null;

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

        /// <summary>許可を直接差し替える供給元（Pause・会話をこのテストが作るため）。</summary>
        private sealed class FakeActivity : ICompanionActivitySource
        {
            public CompanionActivity Current { get; set; } = CompanionActivity.Fighting;
        }

        private sealed class Rig
        {
            public GameObject Root;
            public CompanionActor Actor;
            public CompanionMotor Motor;
            public CompanionTargetTracker Tracker;
            public CompanionCombatController Combat;
            public CompanionHitReceiver Receiver;
        }

        private AttackData MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", 2f);
            SetPrivateField(attack, "_useAngle", 120f);
            SetPrivateField(attack, "_cooldownSeconds", 5f);
            SetPrivateField(attack, "_startupSeconds", 0.05f);
            SetPrivateField(attack, "_activeSeconds", 1.5f); // 長めにして「判定中に Pause する」状況を作る。
            SetPrivateField(attack, "_recoverySeconds", 0.3f);
            SetPrivateField(attack, "_hpMultiplier", 0.8f);
            SetPrivateField(attack, "_poiseDamage", 6f);
            SetPrivateField(attack, "_flinchPower", 0f);
            return attack;
        }

        private Rig MakeCompanion(Vector3 position)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_attackPower", 60f);
            SetPrivateField(data, "_maxHp", 80);
            SetPrivateField(data, "_basicAttack", MakeAttack());

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;
            go.SetActive(false); // PlayMode は AddComponent で Awake が走るため、配線後に起こす。

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = 1.2f;
            capsule.center = new Vector3(0f, 0.6f, 0f);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);

            var motor = go.AddComponent<CompanionMotor>();
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);
            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            go.SetActive(true);
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            return new Rig
            {
                Root = go, Actor = actor, Motor = motor,
                Tracker = tracker, Combat = combat, Receiver = receiver,
            };
        }

        /// <summary>被害を数えるだけの敵役（実 Collider を持ち、判定に拾われる）。</summary>
        private sealed class CountingEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
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

            public int Hits { get; private set; }

            public void ReceiveHit(in HitInfo hit) => Hits++;
        }

        private CountingEnemy MakeEnemy(Vector3 position)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = position;

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = 1.2f;
            capsule.center = new Vector3(0f, 0.6f, 0f);

            var enemy = go.AddComponent<CountingEnemy>();
            PerceptionTargetRegistry.Register(enemy);
            return enemy;
        }

        private FakeActivity InstallActivity(CompanionActivity initial)
        {
            var source = new FakeActivity { Current = initial };
            CompanionActivityProvider.Current = source;
            return source;
        }

        // ---- N13：timeScale=1 のままでも止まる ----

        [UnityTest]
        public IEnumerator PauseAtTimeScaleOne_StopsBodyAndClocks()
        {
            FakeActivity activity = InstallActivity(CompanionActivity.Fighting);
            Rig companion = MakeCompanion(Vector3.zero);
            MakeEnemy(new Vector3(0f, 0f, 1.2f));
            yield return null;

            // まず普通に動かして、攻撃を始めさせる。
            for (int i = 0; i < 30 && !companion.Combat.IsAttacking; i++)
            {
                yield return null;
            }

            Assert.IsTrue(companion.Combat.IsAttacking, "前提：攻撃が始まっている。");

            // 追従の移動指示も入れておく（位置が止まることを見るため）。
            companion.Motor.Configure(4.5f, 0.05f);
            companion.Motor.SetMoveTarget(new Vector3(0f, 0f, 20f));

            // ここで Pause。timeScale は 1 のまま（入力や timeScale に頼らない設計であることを確かめる）。
            Time.timeScale = 1f;
            activity.Current = CompanionActivity.Paused(encounterActive: true);

            yield return new WaitForFixedUpdate();
            yield return null;

            Vector3 position = companion.Root.transform.position;
            float cooldown = companion.Combat.CooldownRemaining;
            CompanionAttackPhase phase = companion.Combat.AttackState.Phase;
            float elapsed = companion.Combat.AttackState.Elapsed;

            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            yield return new WaitForFixedUpdate();

            Assert.AreEqual(position.x, companion.Root.transform.position.x, 1e-3f, "Pause 中は動かない（X）。");
            Assert.AreEqual(position.z, companion.Root.transform.position.z, 1e-3f, "Pause 中は動かない（Z）。");
            Assert.AreEqual(0f, companion.Root.GetComponent<Rigidbody>().linearVelocity.magnitude, 1e-3f,
                "timeScale が 1 でも速度をゼロにする。");
            Assert.AreEqual(cooldown, companion.Combat.CooldownRemaining, 1e-4f, "時計が進まない。");
            Assert.AreEqual(phase, companion.Combat.AttackState.Phase, "攻撃の段が変わらない。");
            Assert.AreEqual(elapsed, companion.Combat.AttackState.Elapsed, 1e-4f, "攻撃の経過時間も止まる。");
        }

        // ---- N14：復帰しても同じ相手を二度殴らない ----

        [UnityTest]
        public IEnumerator Resume_KeepsSwingAndDoesNotHitAgain()
        {
            FakeActivity activity = InstallActivity(CompanionActivity.Fighting);
            Rig companion = MakeCompanion(Vector3.zero);
            CountingEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1.2f));
            yield return null;

            // 判定が当たるまで進める。
            for (int i = 0; i < 60 && enemy.Hits == 0; i++)
            {
                yield return null;
            }

            Assert.AreEqual(1, enemy.Hits, "前提：1 回当たっている。");
            Assert.IsTrue(companion.Combat.IsAttacking, "前提：まだ判定段（Active を長めに設定してある）。");

            CompanionAttackPhase phaseBefore = companion.Combat.AttackState.Phase;

            activity.Current = CompanionActivity.Paused(encounterActive: true);
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(1, enemy.Hits, "Pause 中に追加の被害を出さない。");

            activity.Current = CompanionActivity.Fighting; // 復帰。
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(phaseBefore, companion.Combat.AttackState.Phase,
                "同じ段から続く（攻撃を作り直していない）。");
            Assert.AreEqual(1, enemy.Hits,
                "復帰しても同じ Swing の既命中集合が保たれ、同じ相手へ二度目の被害を出さない。");
        }

        // ---- N15：会話・イベントは破棄 ----

        [UnityTest]
        public IEnumerator Dialogue_CancelsAttackAndDoesNotResumeOldActive()
        {
            FakeActivity activity = InstallActivity(CompanionActivity.Fighting);
            Rig companion = MakeCompanion(Vector3.zero);
            CountingEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1.2f));
            yield return null;

            for (int i = 0; i < 30 && !companion.Combat.IsAttacking; i++)
            {
                yield return null;
            }

            Assert.IsTrue(companion.Combat.IsAttacking, "前提：攻撃中。");

            // 会話へ入る。timeScale には触らない。
            activity.Current = CompanionActivity.Interrupted(encounterActive: true);
            yield return null;

            Assert.IsFalse(companion.Combat.IsAttacking, "会話・イベントでは古い攻撃を捨てる。");
            Assert.IsFalse(companion.Combat.ActivePlan.HasAttack, "確定値も残さない。");

            int hitsAtInterrupt = enemy.Hits;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(hitsAtInterrupt, enemy.Hits, "会話中に判定は出ない。");

            // 復帰。古い Active を再開しないこと（新しく振り直すのは構わない）。
            activity.Current = CompanionActivity.Fighting;
            yield return null;

            Assert.AreNotEqual(CompanionAttackPhase.Active, companion.Combat.AttackState.Phase,
                "復帰した瞬間に古い判定段から再開しない。");
        }
    }
}
