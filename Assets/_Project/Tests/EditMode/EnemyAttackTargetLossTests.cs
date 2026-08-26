using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Slots;
using Momotaro.Gameplay.Enemy.Threat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-02 受入修正：開始時に固定した攻撃対象がプレイヤー死亡等で非活動／Down になったら、進行中の敵攻撃を安全に Cleanup する
    /// （<see cref="EnemyAttackController"/>）。Prepare／Active の中断、Hitbox 無効、Telegraph Cancel 一度、Slot 解放、突進停止、
    /// 別対象へ切替えないこと、対象未指定攻撃を誤中断しないこと、死亡対象への新規攻撃を開始しないことを、公開シームで決定的に検証する。
    /// </summary>
    public sealed class EnemyAttackTargetLossTests
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

        private static void SetField(object target, string name, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        private EnemyAttackData MakeAttack(EnemyAttackClass cls = EnemyAttackClass.Normal,
            AttackSlotKind slot = AttackSlotKind.MeleeNormal)
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_attackClass", cls);
            SetField(d, "_slotKind", slot);
            SetField(d, "_useRange", 2.0f);
            SetField(d, "_useAngle", 60f);
            SetField(d, "_cooldownSeconds", 1.0f);
            SetField(d, "_baseScore", 10f);
            SetField(d, "_prepareSeconds", 0.30f);
            SetField(d, "_activeSeconds", 0.20f);
            SetField(d, "_recoverySeconds", 0.20f);
            SetField(d, "_trackingStopSeconds", 0.15f);
            SetField(d, "_hpMultiplier", 1.0f);
            SetField(d, "_poiseDamage", 10f);
            SetField(d, "_flinchPower", 30f);
            SetField(d, "_guardStaminaCost", 12f);
            SetField(d, "_justGuardPoiseReturn", 18f);
            SetField(d, "_guardable", true);
            SetField(d, "_justGuardable", true);
            SetField(d, "_aimingMode", EnemyAimingMode.CurrentPosition);
            SetField(d, "_hitboxHalfExtents", new Vector3(0.6f, 0.5f, 0.6f));
            SetField(d, "_hitboxForwardOffset", 0.9f);
            SetField(d, "_hitboxHeight", 0.5f);
            SetField(d, "_chargeSpeed", 5.0f);
            SetField(d, "_telegraph", AttackTelegraph.Normal);
            return d;
        }

        private EnemyAttackController MakeController(EnemyAttackData attack, Transform parent = null, bool withMotor = false)
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 100);
            SetField(arch, "_attackPower", 50f);
            SetField(arch, "_attacks", new[] { attack });

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            if (parent != null) go.transform.SetParent(parent);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            var controller = go.AddComponent<EnemyAttackController>();
            if (withMotor)
            {
                var motor = go.AddComponent<EnemyMotor>();
                SetField(controller, "_motor", motor);
            }

            return controller;
        }

        private sealed class FakeThreat : IThreatTarget
        {
            public bool Active = true;
            public bool Down;
            public Vector3 Pos = new Vector3(0f, 0f, 1.5f);
            public int ActorId => 777;
            public CombatFaction Faction => CombatFaction.Player;
            public Vector3 Position => Pos;
            public bool IsActive => Active;
            public bool IsDown => Down;
            public float BaseThreat => 50f;
            public float AcquiredThreatMultiplier => 1f;
        }

        private sealed class TelegraphSpy : IEnemyTelegraphListener
        {
            public readonly List<EnemyTelegraphPhase> Phases = new List<EnemyTelegraphPhase>();
            public int Count(EnemyTelegraphPhase p) => Phases.FindAll(x => x == p).Count;
            public void OnTelegraph(in EnemyTelegraphEvent t) => Phases.Add(t.Phase);
        }

        private sealed class FakeDamageable : IDamageable
        {
            public int Received;
            public int DamageableId => 123;
            public void ReceiveHit(in HitInfo hit) => Received++;
        }

        [Test]
        public void PrepareTargetDown_CancelsAttack()
        {
            var c = MakeController(MakeAttack());
            var target = new FakeThreat();
            Assert.IsTrue(c.TryStartAttack(target, target.Pos, Vector3.zero));
            Assert.AreEqual(EnemyAttackMachine.Phase.Prepare, c.Phase);

            target.Down = true; // プレイヤー死亡＝Down
            c.TickAttack(0.05f);

            Assert.IsFalse(c.IsAttacking, "Prepare 中に対象 Down で中断。");
        }

        [Test]
        public void ActiveTargetInactive_DisablesHitbox()
        {
            var c = MakeController(MakeAttack());
            var target = new FakeThreat();
            c.TryStartAttack(target, target.Pos, Vector3.zero);
            c.TickAttack(0.30f); // → Active
            Assert.AreEqual(EnemyAttackMachine.Phase.Active, c.Phase);

            target.Active = false; // 非活動化
            c.TickAttack(0.02f);

            Assert.IsFalse(c.IsAttacking, "Active 中の対象喪失で中断。");
            var victim = new FakeDamageable();
            Assert.IsFalse(c.TryApplyHit(victim, null, Vector3.zero), "Cleanup 後は Hitbox 判定が出ない。");
            Assert.AreEqual(0, victim.Received);
        }

        [Test]
        public void Cleanup_EmitsCancelTelegraph_Once()
        {
            var c = MakeController(MakeAttack());
            var spy = new TelegraphSpy();
            c.Telegraph.AddListener(spy);
            var target = new FakeThreat();
            c.TryStartAttack(target, target.Pos, Vector3.zero);

            target.Down = true;
            c.TickAttack(0.05f);
            c.TickAttack(0.05f); // 追加 Tick でも二重 Cancel しない（既に非攻撃）。

            Assert.IsFalse(c.IsAttacking);
            Assert.AreEqual(1, spy.Count(EnemyTelegraphPhase.Cancel), "Telegraph Cancel は一度だけ。");
        }

        [Test]
        public void Cleanup_DoesNotSwitchToAnotherTarget()
        {
            var c = MakeController(MakeAttack());
            var target = new FakeThreat();
            c.TryStartAttack(target, target.Pos, Vector3.zero);
            Assert.AreEqual(target.ActorId, c.AttackTargetId, "開始時対象を固定。");

            target.Down = true;
            c.TickAttack(0.05f);

            Assert.AreEqual(0, c.AttackTargetId, "対象喪失後は照準対象を解除し、別対象へ切替えない。");
        }

        [Test]
        public void Cleanup_ReleasesAttackSlot()
        {
            var encGo = new GameObject("Encounter");
            _spawned.Add(encGo);
            var encounter = encGo.AddComponent<EnemyEncounter>();

            var c = MakeController(MakeAttack(), parent: encGo.transform);
            var target = new FakeThreat();
            c.TryStartAttack(target, target.Pos, Vector3.zero);
            int ownerId = ((ISlotOwner)c).SlotOwnerId;
            Assert.IsTrue(encounter.Coordinator.Holds(ownerId), "開始で Slot を取得。");

            target.Down = true;
            c.TickAttack(0.05f);

            Assert.IsFalse(encounter.Coordinator.Holds(ownerId), "Cleanup で Slot を解放。");
        }

        [Test]
        public void Cleanup_StopsChargeMotion()
        {
            var c = MakeController(MakeAttack(EnemyAttackClass.Charge), withMotor: true);
            var motor = c.GetComponent<EnemyMotor>();
            var target = new FakeThreat();
            c.TryStartAttack(target, target.Pos, Vector3.zero);
            c.TickAttack(0.30f); // → Active（Charge は Active で SetCharge）
            Assert.IsTrue(motor.IsCharging, "突進開始。");

            target.Down = true;
            c.TickAttack(0.02f);

            Assert.IsFalse(c.IsAttacking, "突進中の対象喪失で中断。");
            Assert.IsFalse(motor.IsCharging, "Cleanup で突進が停止する。");
        }

        [Test]
        public void TargetlessAttack_NotCancelledByTargetLoss()
        {
            // 対象を指定せず開始した攻撃（_attackTarget==null）は、対象喪失判定で誤って中断されず通常どおり完走する。
            var c = MakeController(MakeAttack());
            var spy = new TelegraphSpy();
            c.Telegraph.AddListener(spy);
            Assert.IsTrue(c.TryStartAttack(new Vector3(0f, 0f, 1.5f), Vector3.zero));

            c.TickAttack(0.30f); // Active
            Assert.AreEqual(EnemyAttackMachine.Phase.Active, c.Phase);
            c.TickAttack(0.20f); // Recovery
            c.TickAttack(0.20f); // End
            Assert.IsFalse(c.IsAttacking, "通常完走（対象なし攻撃は中断されない）。");
            Assert.AreEqual(0, spy.Count(EnemyTelegraphPhase.Cancel), "Cancel は出ない（正常終了）。");
            Assert.AreEqual(1, spy.Count(EnemyTelegraphPhase.End), "End で正常終了。");
        }

        [Test]
        public void DeadTarget_NewAttackNotStarted()
        {
            var c = MakeController(MakeAttack());
            var target = new FakeThreat { Down = true }; // 既に死亡
            Assert.IsFalse(c.TryStartAttack(target, target.Pos, Vector3.zero), "Down 対象へは新規攻撃を開始しない。");
            Assert.IsFalse(c.IsAttacking);

            var inactive = new FakeThreat { Active = false };
            Assert.IsFalse(c.TryStartAttack(inactive, inactive.Pos, Vector3.zero), "非活動対象へも開始しない。");
        }
    }
}
