using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Defense;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-10：撃破処理（§9「Down 時に攻撃・衝突・Slot を解除し、型付き Defeated／Reward Request を 1 回発行」）。撃破で後始末
    /// （<see cref="IEnemyDefeatCleanup"/>）が 1 回呼ばれ、Collider が無効化され、報酬要求が 1 回だけ発行され、Down 後の追加被弾が無効なことを検証する。
    /// </summary>
    public sealed class EnemyDefeatTests
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

        private sealed class FakeCleanup : MonoBehaviour, IEnemyDefeatCleanup
        {
            public int Calls;
            public void OnOwnerDefeated() => Calls++;
        }

        private sealed class DefeatSpy : IEnemyDefeatListener
        {
            public readonly List<EnemyDefeatedEvent> Events = new List<EnemyDefeatedEvent>();
            public void OnEnemyDefeated(in EnemyDefeatedEvent defeated) => Events.Add(defeated);
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

        private (EnemyActor actor, FakeCleanup cleanup, BoxCollider col) MakeActor()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 50);
            SetField(arch, "_defense", 0f);

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var col = go.AddComponent<BoxCollider>();
            var cleanup = go.AddComponent<FakeCleanup>();
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            return (actor, cleanup, col);
        }

        private static HitInfo Lethal(EnemyActor target, int id)
        {
            return new HitInfo(null, target, new Vector3(0, 0, -1), Vector3.zero, new HitDamage(200f, 0f, 0f),
                true, true, HitId.Single(id));
        }

        [Test]
        public void Defeat_EmitsRewardOnce_CleansUp_DisablesCollider()
        {
            var (actor, cleanup, col) = MakeActor();
            var spy = new DefeatSpy();
            actor.Defeats.AddListener(spy);

            actor.ReceiveHit(Lethal(actor, 1));

            Assert.IsTrue(actor.IsDown, "撃破で Down。");
            Assert.AreEqual(1, spy.Events.Count, "型付き撃破は 1 回。");
            Assert.AreEqual(actor.DamageableId, spy.Events[0].Reward.EnemyId, "報酬要求に撃破敵 ID。");
            Assert.AreEqual(EnemyRole.Melee, spy.Events[0].Reward.Role, "役割を伴う。");
            Assert.AreEqual(1, cleanup.Calls, "後始末（攻撃・Slot 解除）が 1 回呼ばれる。");
            Assert.IsFalse(col.enabled, "衝突（Collider）を解除。");
        }

        [Test]
        public void Defeat_NoFurtherHits_NoDoubleReward()
        {
            var (actor, cleanup, _) = MakeActor();
            var spy = new DefeatSpy();
            actor.Defeats.AddListener(spy);

            actor.ReceiveHit(Lethal(actor, 1));
            int hpAfter = actor.CurrentHp;

            actor.ReceiveHit(Lethal(actor, 2)); // Down 後の追加被弾。
            actor.ReceiveHit(Lethal(actor, 3));

            Assert.AreEqual(hpAfter, actor.CurrentHp, "Down 後は追加被弾なし（HP 不変）。");
            Assert.AreEqual(1, spy.Events.Count, "報酬要求は 1 回のみ（二重発行なし）。");
            Assert.AreEqual(1, cleanup.Calls, "後始末も 1 回のみ。");
        }
    }
}
