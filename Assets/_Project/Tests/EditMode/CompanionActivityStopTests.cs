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
    /// P4-FIX-R2（レビュー R2-08）：活動 Context の供給元が<b>無い</b>とき、各駆動が契約どおり止まることを
    /// 駆動ごとに固定する。<see cref="CompanionActivityContextTests"/> が「供給元が無ければ Stopped を返す」を
    /// 押さえ、ここは「Stopped を読んだ駆動が実際に何もしない」を押さえる。両方無いと、片方だけ通っても実機で動く。
    ///
    /// 「供給元が無い」は共通 Fixture が差した Fake を本文で外して作る。同じテストで、Fake を差し戻せば動くことも
    /// 見る（止まっているのが停止ゲートのせいであって、配線ミスではないことの確認）。
    /// </summary>
    public sealed class CompanionActivityStopTests : CompanionActivityFixture
    {
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

        private sealed class FakeEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
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
            public int ReceivedHits { get; private set; }
            public void ReceiveHit(in HitInfo hit) => ReceivedHits++;
        }

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

        private sealed class FakeHost : MonoBehaviour, IGuardianHost
        {
            public void SetGuardianResolver(IGuardianResolver resolver) { }
            public void ClearGuardianResolver(IGuardianResolver expected) { }
        }

        private CompanionData MakeData()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", 2f);
            SetPrivateField(attack, "_useAngle", 90f);
            SetPrivateField(attack, "_cooldownSeconds", 1f);
            SetPrivateField(attack, "_startupSeconds", 0.2f);
            SetPrivateField(attack, "_activeSeconds", 0.2f);
            SetPrivateField(attack, "_recoverySeconds", 0.2f);
            SetPrivateField(attack, "_hpMultiplier", 1f);

            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_attackPower", 50f);
            SetPrivateField(data, "_basicAttack", attack);
            SetPrivateField(data, "_guardianRange", 3f);
            SetPrivateField(data, "_guardianCooldownSeconds", 6f);
            return data;
        }

        private GameObject MakeCompanionRoot(Vector3 position, out CompanionActor actor)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;
            actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData());
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);
            return go;
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

        [Test]
        public void Fixture_InstallsAnExplicitFakeBeforeEachTest()
        {
            Assert.IsNotNull(CompanionActivityTestSource.CurrentFake,
                "共通 Fixture（CompanionActivityFixture の継承）が各テストの前に Fake を差している。");
            Assert.IsTrue(CompanionActivityProvider.Activity.CanAct, "既定は自由探索。");
        }

        [Test]
        public void WithoutSource_CombatNeitherStartsNorContinues_AndHitboxIsSilent()
        {
            GameObject go = MakeCompanionRoot(Vector3.zero, out CompanionActor actor);
            var motor = go.AddComponent<CompanionMotor>();
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);
            InvokePrivate(combat, "OnEnable");
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1f)); // 攻撃開始距離（UseRange×0.7）の内側。

            // 供給元が無い：攻撃を始めない。
            CompanionActivityProvider.Current = null;
            tracker.TickTargeting();
            combat.TickCombat(0.1f);
            Assert.IsFalse(combat.IsAttacking, "供給元が無ければ攻撃を始めない。");
            Assert.AreEqual(0, combat.AttackCount);

            // 供給元を差せば始まる（止まっていたのは停止ゲートのせい）。
            CompanionActivityTestSource.InstallFreeRoam();
            tracker.TickTargeting();
            combat.TickCombat(0f);
            combat.TickCombat(0.2f);
            Assert.IsTrue(combat.AttackState.IsHitboxActive, "前提：判定中。");

            // 判定中に供給元が消えたら凍結する（判定も出さない・段も進まない）。
            CompanionActivityProvider.Current = null;
            CompanionAttackPhase phase = combat.AttackState.Phase;
            combat.TickCombat(0.1f);
            Assert.AreEqual(phase, combat.AttackState.Phase, "供給元が無ければ攻撃の段を進めない（凍結）。");
            Assert.IsFalse(combat.TryApplyHit(enemy, enemy, enemy.transform.position), "供給元が無ければ命中を送出しない。");
            Assert.AreEqual(0, enemy.ReceivedHits);
        }

        [Test]
        public void WithoutSource_FollowDoesNotMoveOrWarp()
        {
            var leader = new GameObject("Leader");
            _spawned.Add(leader);
            GameObject go = MakeCompanionRoot(new Vector3(0f, 0f, 30f), out CompanionActor actor);
            var motor = go.AddComponent<CompanionMotor>();
            var follow = go.AddComponent<CompanionFollowController>();
            follow.Bind(leader.transform, actor, motor);
            InvokePrivate(follow, "OnEnable");

            CompanionActivityProvider.Current = null;
            follow.TickFollow(0.1f);

            Assert.IsFalse(motor.HasMoveTarget, "供給元が無ければ移動を指示しない。");
            Assert.AreEqual(0, motor.WarpCount, "ワープもしない。");
            Assert.AreEqual(CompanionState.Follow, actor.State);

            CompanionActivityTestSource.InstallFreeRoam();
            go.GetComponent<CompanionMovementArbiter>().EndFrame(); // 停止フレームの強制停止ラッチを落とす（フレーム境界）。
            follow.TickFollow(0.1f);
            Assert.AreEqual(1, motor.WarpCount, "供給元を差せば追従が動く（距離超過でワープ）。");
        }

        [Test]
        public void WithoutSource_DefenseDoesNotStart_AndClocksFreeze()
        {
            GameObject go = MakeCompanionRoot(Vector3.zero, out CompanionActor actor);
            var defense = go.AddComponent<CompanionDefenseController>();
            defense.Bind(actor);
            var danger = new FakeDanger { HasDanger = true };
            defense.SetDangerSense(danger);
            InvokePrivate(defense, "OnEnable");

            CompanionActivityProvider.Current = null;
            defense.TickDefense(0.1f);
            Assert.IsFalse(defense.IsGuarding, "供給元が無ければ構えない。");
            Assert.IsFalse(defense.SawDanger, "危険の観測もしない（新しい行動の判断を始めない）。");

            // 時計（ガードのクールダウン）も進まない。
            CompanionActivityTestSource.InstallFreeRoam();
            defense.TickDefense(0.1f);
            Assert.IsTrue(defense.IsGuarding, "前提：供給元があれば構える。");
            danger.HasDanger = false;
            defense.TickDefense(0.1f);
            float cooldown = defense.Guard.CooldownRemaining;
            Assert.Greater(cooldown, 0f, "前提：解除でクールダウンに入った。");

            CompanionActivityProvider.Current = null;
            defense.TickDefense(1f);
            Assert.AreEqual(cooldown, defense.Guard.CooldownRemaining, 1e-4f, "供給元が無ければクールダウンを進めない。");
        }

        [Test]
        public void WithoutSource_GuardianDoesNotTransfer_AndCooldownFreezes()
        {
            var hostGo = new GameObject("Player");
            _spawned.Add(hostGo);
            hostGo.AddComponent<FakeHost>();
            GameObject go = MakeCompanionRoot(new Vector3(0f, 0f, 1f), out CompanionActor actor);
            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);
            var guardian = go.AddComponent<CompanionGuardianController>();
            guardian.Bind(actor, receiver, hostGo.transform);
            InvokePrivate(guardian, "OnEnable");
            HitInfo hit = new HitInfo(
                null, null, Vector3.back, Vector3.zero, new HitDamage(10f, 0f, 0f),
                guardable: true, justGuardable: false, hitId: HitId.Single(11));

            CompanionActivityProvider.Current = null;
            Assert.IsFalse(guardian.TryResolveGuardian(hit, out IGuardianReceiver _), "供給元が無ければ庇わない（主人公の通常 Damage へ戻す）。");

            CompanionActivityTestSource.InstallFreeRoam();
            Assert.IsTrue(guardian.TryResolveGuardian(hit, out IGuardianReceiver resolved));
            Assert.IsTrue(resolved.TryReceiveTransferredHit(GuardianHitTransfer.Rebuild(hit, resolved)));
            guardian.NotifyTransferred(hit, resolved);
            float cooldown = guardian.CooldownRemaining;
            Assert.Greater(cooldown, 0f);

            CompanionActivityProvider.Current = null;
            guardian.TickGuardian(1f);
            Assert.AreEqual(cooldown, guardian.CooldownRemaining, 1e-4f, "供給元が無ければ守護のクールダウンも進めない。");
        }

        [Test]
        public void WithoutSource_VitalsClocksFreeze()
        {
            GameObject go = MakeCompanionRoot(Vector3.zero, out CompanionActor actor);
            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);
            receiver.ReceiveHit(new HitInfo(
                null, receiver, Vector3.back, Vector3.zero, new HitDamage(10f, 0f, 0f),
                guardable: false, justGuardable: false, hitId: HitId.Single(12)));
            Assert.IsTrue(receiver.Vitals.IsPostHitInvincible, "前提：被弾後無敵に入った。");

            CompanionActivityProvider.Current = null;
            receiver.TickVitals(5f);
            Assert.IsTrue(receiver.Vitals.IsPostHitInvincible, "供給元が無ければ無敵・ひるみ・復帰待ちの時計を進めない。");

            CompanionActivityTestSource.InstallFreeRoam();
            receiver.TickVitals(5f);
            Assert.IsFalse(receiver.Vitals.IsPostHitInvincible, "供給元を差せば進む。");
        }
    }
}
