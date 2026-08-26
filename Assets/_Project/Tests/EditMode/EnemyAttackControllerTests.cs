using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-04：<see cref="EnemyAttackController"/> の統合を決定的に検証する。選択→Prepare→Active→Recovery、予兆イベント、
    /// 同一対象 1Hit、Faction フィルタ、命中の Phase2 経路（<see cref="IDamageable.ReceiveHit"/>）伝達、Cooldown、中断 Cleanup。
    /// 公開シーム（TryStartAttack/TickAttack/TryApplyHit/CancelAttack）で駆動する（EditMode では Update/物理が走らない）。
    /// </summary>
    public sealed class EnemyAttackControllerTests
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

        private EnemyAttackData MakeAttack()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_attackClass", EnemyAttackClass.Normal);
            SetField(d, "_useRange", 2.0f);
            SetField(d, "_useAngle", 60f);
            SetField(d, "_cooldownSeconds", 1.0f);
            SetField(d, "_baseScore", 10f);
            SetField(d, "_prepareSeconds", 0.30f);
            SetField(d, "_activeSeconds", 0.10f);
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
            SetField(d, "_telegraph", AttackTelegraph.Normal);
            return d;
        }

        private EnemyAttackController MakeController()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 100);
            SetField(arch, "_attackPower", 50f);
            SetField(arch, "_attacks", new[] { MakeAttack() });

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            var controller = go.AddComponent<EnemyAttackController>();
            return controller;
        }

        private sealed class FakeDamageable : IDamageable
        {
            public int Received;
            public HitInfo Last;
            public int DamageableId => 123;
            public void ReceiveHit(in HitInfo hit) { Received++; Last = hit; }
        }

        private sealed class FakeActor : ICombatActor
        {
            public CombatFaction Faction { get; set; }
            public int FloorId => 0;
            public Vector3 WorldPosition => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
        }

        private sealed class TelegraphSpy : IEnemyTelegraphListener
        {
            public readonly List<EnemyTelegraphPhase> Phases = new List<EnemyTelegraphPhase>();
            public void OnTelegraph(in EnemyTelegraphEvent t) => Phases.Add(t.Phase);
        }

        [Test]
        public void TryStartAttack_SelectsAndEntersPrepare_EmitsBeginTelegraph()
        {
            var c = MakeController();
            var spy = new TelegraphSpy();
            c.Telegraph.AddListener(spy);

            bool started = c.TryStartAttack(new Vector3(0, 0, 1.5f), Vector3.zero);
            Assert.IsTrue(started, "帯内・角度内で攻撃開始。");
            Assert.AreEqual(EnemyAttackMachine.Phase.Prepare, c.Phase);
            Assert.AreEqual(EnemyState.AttackPrepare, c.GetComponent<EnemyActor>().State);
            Assert.Contains(EnemyTelegraphPhase.Begin, spy.Phases);
        }

        [Test]
        public void TicksThroughPhases_AndCooldownBlocksImmediateRestart()
        {
            var c = MakeController();
            var actor = c.GetComponent<EnemyActor>();
            var spy = new TelegraphSpy();
            c.Telegraph.AddListener(spy);

            c.TryStartAttack(new Vector3(0, 0, 1.5f), Vector3.zero);
            c.TickAttack(0.30f); // → Active
            Assert.AreEqual(EnemyAttackMachine.Phase.Active, c.Phase);
            Assert.AreEqual(EnemyState.AttackActive, actor.State);
            Assert.Contains(EnemyTelegraphPhase.Fire, spy.Phases);

            c.TickAttack(0.10f); // → Recovery
            Assert.AreEqual(EnemyState.AttackRecovery, actor.State);

            c.TickAttack(0.20f); // → 終了
            Assert.IsFalse(c.IsAttacking, "後隙明けで攻撃終了。");
            Assert.Contains(EnemyTelegraphPhase.End, spy.Phases);

            // 直後は Cooldown 中で再攻撃できない。
            Assert.IsFalse(c.TryStartAttack(new Vector3(0, 0, 1.5f), Vector3.zero), "Cooldown 中は再攻撃不可。");
        }

        [Test]
        public void TryApplyHit_DeliversOncePerTarget_FactionFilters_BuildsPhase2Hit()
        {
            var c = MakeController();
            c.TryStartAttack(new Vector3(0, 0, 1.5f), Vector3.zero);
            c.TickAttack(0.30f); // Active

            var player = new FakeDamageable();
            var playerActor = new FakeActor { Faction = CombatFaction.Player };
            Assert.IsTrue(c.TryApplyHit(player, playerActor, Vector3.zero), "対象へ命中を伝達。");
            Assert.AreEqual(1, player.Received);
            Assert.IsTrue(player.Last.Guardable, "Snapshot の Guardable を反映。");
            Assert.IsTrue(player.Last.JustGuardable, "Snapshot の JustGuardable を反映。");
            Assert.Greater(player.Last.Damage.Hp, 0f, "攻撃力×技倍率の HP 寄与。");

            Assert.IsFalse(c.TryApplyHit(player, playerActor, Vector3.zero), "同一 Swing で同一対象は 1 回だけ。");

            var ally = new FakeDamageable();
            var enemyActor = new FakeActor { Faction = CombatFaction.Enemy };
            Assert.IsFalse(c.TryApplyHit(ally, enemyActor, Vector3.zero), "敵 Faction には当てない。");
            Assert.AreEqual(0, ally.Received);
        }

        [Test]
        public void CancelAttack_Cleanup_EmitsCancel_AndStopsHits()
        {
            var c = MakeController();
            var spy = new TelegraphSpy();
            c.Telegraph.AddListener(spy);
            c.TryStartAttack(new Vector3(0, 0, 1.5f), Vector3.zero);
            c.TickAttack(0.30f); // Active

            c.CancelAttack();
            Assert.IsFalse(c.IsAttacking, "中断で攻撃終了。");
            Assert.Contains(EnemyTelegraphPhase.Cancel, spy.Phases);

            var player = new FakeDamageable();
            Assert.IsFalse(c.TryApplyHit(player, new FakeActor { Faction = CombatFaction.Player }, Vector3.zero),
                "中断後は判定が出ない（Cleanup）。");
        }

        [Test]
        public void JustGuardForcedFlinch_DuringActiveAttack_CancelsAttack_TelegraphAndHitbox_ViaUpdate()
        {
            // P3.5-08A 結合試験（GPT レビュー対応）：JG 強制ひるみ → 敵が Stagger → 実 Update 経路で進行中攻撃が中断され、
            // Telegraph Cancel が出て判定（Hitbox）が消える（＝以後 TryApplyHit が通らない）ことを検証する。Slot 解放は CancelAttack
            // 内部で行われ（AttackSlotCoordinator 統合テストで別途担保）、ここでは中断・予兆・判定停止を通しで確認する。
            var c = MakeController();
            var actor = c.GetComponent<EnemyActor>();
            var spy = new TelegraphSpy();
            c.Telegraph.AddListener(spy);

            c.TryStartAttack(new Vector3(0, 0, 1.5f), Vector3.zero);
            c.TickAttack(0.30f); // → Active（Hitbox 有効）
            Assert.AreEqual(EnemyAttackMachine.Phase.Active, c.Phase);
            Assert.IsTrue(c.IsAttacking);

            // ジャストガード成立時に主人公被弾解決が呼ぶのと同じ経路（IForcedFlinchReceiver）で近接攻撃者へ強制ひるみを付与。
            ((IForcedFlinchReceiver)actor).ForceFlinch(0.35f);
            Assert.AreEqual(EnemyState.Stagger, actor.State, "強制ひるみで Stagger（AttackActive を上書き）。");
            Assert.IsTrue(EnemyStatePriority.IsForcedByHit(actor.State), "Update が中断判定に用いる被弾由来状態になる。");

            // 実 Update（被弾由来→CancelAttack）を決定的に駆動（EditMode は Update 非駆動のため明示呼び出し）。
            MethodInfo update = typeof(EnemyAttackController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(update, "Update が見つからない。");
            update.Invoke(c, null);

            Assert.IsFalse(c.IsAttacking, "強制ひるみで進行中攻撃が中断される。");
            Assert.Contains(EnemyTelegraphPhase.Cancel, spy.Phases, "予兆（Telegraph）が Cancel される。");

            var player = new FakeDamageable();
            Assert.IsFalse(c.TryApplyHit(player, new FakeActor { Faction = CombatFaction.Player }, Vector3.zero),
                "中断後は Hitbox 判定が出ない（Cleanup 済み）。");
            Assert.AreEqual(0, player.Received);
        }
    }
}
