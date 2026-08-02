using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-01：<see cref="EnemyActor"/> が Phase 2 と同一経路（<see cref="IDamageable"/>/<see cref="HitResultChannel"/>）で
    /// 被弾し、被弾由来の Stagger／Stunned／Down 状態へ遷移し、同一 Hit 拒否・ノックバック無効（ボス）を満たすことを検証する。
    /// </summary>
    public sealed class EnemyActorTests
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

        private EnemyArchetypeData MakeArchetype(int hp, float defense, float poiseMax, float flinchResist, EnemyRole role)
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(a);
            SetField(a, "_maxHp", hp);
            SetField(a, "_defense", defense);
            SetField(a, "_poiseMax", poiseMax);
            SetField(a, "_flinchResistance", flinchResist);
            SetField(a, "_role", role);
            return a;
        }

        private EnemyActor MakeActor(int hp = 100, float defense = 0f, float poiseMax = 100f,
            float flinchResist = 60f, EnemyRole role = EnemyRole.Melee)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.SetActive(false);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", MakeArchetype(hp, defense, poiseMax, flinchResist, role));
            go.SetActive(true);
            return actor;
        }

        private static HitInfo Hit(IDamageable target, float hp, float poise, float flinch, int id = 1)
        {
            return new HitInfo(null, target, Vector3.forward, Vector3.zero,
                new HitDamage(hp, poise, flinch), true, true, HitId.Single(id));
        }

        private sealed class FakeListener : IHitResultListener
        {
            public int Count;
            public HitResult Last;
            public void OnHitResult(in HitResult result) { Count++; Last = result; }
        }

        [Test]
        public void Faction_IsEnemy()
        {
            var actor = MakeActor();
            Assert.AreEqual(CombatFaction.Enemy, actor.Faction);
        }

        [Test]
        public void ReceiveHit_ReducesHp_AndPublishesResult()
        {
            var actor = MakeActor(hp: 100, defense: 0f);
            var listener = new FakeListener();
            actor.Results.AddListener(listener);

            actor.ReceiveHit(Hit(actor, 30f, 0f, 0f));

            Assert.AreEqual(70, actor.CurrentHp, "HP が実減少する（防御0）。");
            Assert.AreEqual(1, listener.Count, "結果が 1 回通知される。");
            Assert.AreEqual(HitResultKind.Damage, listener.Last.Kind);
        }

        [Test]
        public void ReceiveHit_PoiseDepletion_EntersStunnedState()
        {
            var actor = MakeActor(poiseMax: 20f);
            actor.ReceiveHit(Hit(actor, 0f, 25f, 0f));
            Assert.IsTrue(actor.IsStunned, "体幹 0 でスタン。");
            Assert.AreEqual(EnemyState.Stunned, actor.State, "被弾由来で Stunned 状態へ。");
        }

        [Test]
        public void ReceiveHit_Flinch_EntersStaggerState()
        {
            var actor = MakeActor(flinchResist: 10f);
            actor.ReceiveHit(Hit(actor, 0f, 0f, 15f));
            Assert.IsTrue(actor.IsFlinching);
            Assert.AreEqual(EnemyState.Stagger, actor.State, "被弾由来で Stagger 状態へ。");
        }

        [Test]
        public void ReceiveHit_Lethal_EntersDownState()
        {
            var actor = MakeActor(hp: 10);
            actor.ReceiveHit(Hit(actor, 100f, 0f, 0f));
            Assert.IsTrue(actor.IsDefeated);
            Assert.AreEqual(EnemyState.Down, actor.State, "撃破で Down 状態へ。");
            Assert.IsTrue(actor.IsDown);
        }

        [Test]
        public void MultiHitTracker_RejectsSameHitOnEnemy()
        {
            var actor = MakeActor();
            var tracker = new MultiHitTracker();
            HitId id = HitId.Single(1);
            Assert.IsTrue(tracker.TryRegisterHit(id, actor), "初回の命中は登録される。");
            Assert.IsFalse(tracker.TryRegisterHit(id, actor), "同一攻撃 Token の同一対象は 1 回だけ（拒否）。");
        }

        [Test]
        public void Knockback_BossIsImmune_NonBossReceives()
        {
            var normal = MakeActor(role: EnemyRole.Melee);
            normal.ReceiveKnockback(Vector3.forward, 5f);
            Assert.AreEqual(5f, normal.LastKnockback, 1e-4f, "非ボスはノックバックを受ける。");

            var boss = MakeActor(role: EnemyRole.Boss);
            boss.ReceiveKnockback(Vector3.forward, 5f);
            Assert.AreEqual(0f, boss.LastKnockback, 1e-4f, "ボスはノックバック無効。");
        }

        // ---- 論理 Facing（P3-05 受入修正：向きはルート Transform を回さず論理値で保持する）----

        [Test]
        public void Forward_DefaultsToPlusZ()
        {
            var actor = MakeActor();
            Assert.AreEqual(Vector3.forward, actor.Forward, "既定の前方は +Z。");
        }

        [Test]
        public void SetFacing_UpdatesForward_Normalized_OnXZPlane()
        {
            var actor = MakeActor();
            actor.SetFacing(new Vector3(5f, 3f, 0f)); // Y 成分は無視され XZ 平面へ射影される。
            Assert.AreEqual(1f, actor.Forward.magnitude, 1e-4f, "前方は正規化される。");
            Assert.AreEqual(0f, actor.Forward.y, 1e-6f, "前方は XZ 平面（Y=0）。");
            Assert.AreEqual(1f, actor.Forward.x, 1e-4f, "X 方向へ向く。");
        }

        [Test]
        public void SetFacing_DoesNotRotateRootTransform()
        {
            var actor = MakeActor();
            Quaternion before = actor.transform.rotation;
            actor.SetFacing(new Vector3(-1f, 0f, 1f));
            Assert.AreEqual(before, actor.transform.rotation,
                "ルート Transform は回さない（Collider を持つ接地基準の姿勢を保つ）。");
        }

        [Test]
        public void SetFacing_IgnoresZeroAndVerticalOnlyDirection()
        {
            var actor = MakeActor();
            actor.SetFacing(new Vector3(1f, 0f, 0f));
            Vector3 kept = actor.Forward;
            actor.SetFacing(Vector3.zero);          // 無効入力は無視。
            actor.SetFacing(new Vector3(0f, 9f, 0f)); // XZ 成分ゼロも無視。
            Assert.AreEqual(kept, actor.Forward, "無効な向き入力では前方を変えない。");
        }
    }
}
