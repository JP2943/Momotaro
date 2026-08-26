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
    /// P3.5-08A：<see cref="EnemyActor"/> の被弾ヒットバックと JG 強制ひるみを検証する。Damage で AttackDirection へ押し出し要求、
    /// 撃破時は押し出さない、強制ひるみで Stagger へ入りつつ Down／Stunned は上書きしない（優先度）ことを、公開経路で確認する。
    /// </summary>
    public sealed class EnemyReactionTests
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

        private sealed class FakeReactionMotor : MonoBehaviour, IReactionMotor
        {
            public int PushCount;
            public int ClearCount;
            public Vector3 LastDir;
            public float LastDistance;
            public float LastSeconds;
            public void PushReaction(Vector3 direction, float distance, float seconds)
            {
                PushCount++; LastDir = direction; LastDistance = distance; LastSeconds = seconds;
            }
            public void ClearReaction() { ClearCount++; }
        }

        private EnemyArchetypeData MakeArchetype(int hp, float poiseMax, float flinchResist, EnemyRole role)
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(a);
            SetField(a, "_maxHp", hp);
            SetField(a, "_defense", 0f);
            SetField(a, "_poiseMax", poiseMax);
            SetField(a, "_flinchResistance", flinchResist);
            SetField(a, "_role", role);
            return a;
        }

        private (EnemyActor actor, FakeReactionMotor motor) MakeActor(
            int hp = 100, float poiseMax = 100f, float flinchResist = 60f, EnemyRole role = EnemyRole.Melee)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.SetActive(false);
            var motor = go.AddComponent<FakeReactionMotor>();
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", MakeArchetype(hp, poiseMax, flinchResist, role));
            go.SetActive(true);
            return (actor, motor);
        }

        private static HitInfo Hit(IDamageable target, float hp, float poise, float flinch, HitReaction reaction, int id = 1)
        {
            return new HitInfo(null, target, Vector3.forward, Vector3.zero,
                    new HitDamage(hp, poise, flinch), true, true, HitId.Single(id))
                .WithReaction(reaction);
        }

        [Test]
        public void Damage_RequestsHitback_AlongAttackDirection()
        {
            var (actor, motor) = MakeActor(hp: 100);
            var reaction = new HitReaction(0.2f, 0.14f, 0f, isProjectile: false);

            actor.ReceiveHit(Hit(actor, 30f, 0f, 0f, reaction));

            Assert.AreEqual(1, motor.PushCount, "被弾でヒットバックを要求。");
            Assert.AreEqual(Vector3.forward, motor.LastDir, "AttackDirection（攻撃者→自分）へ押し出す。");
            Assert.AreEqual(0.2f, motor.LastDistance, 1e-4f);
            Assert.AreEqual(0.14f, motor.LastSeconds, 1e-4f);
        }

        [Test]
        public void LethalDamage_DoesNotPush_ButClears()
        {
            var (actor, motor) = MakeActor(hp: 10);
            var reaction = new HitReaction(0.2f, 0.14f, 0f, isProjectile: false);

            actor.ReceiveHit(Hit(actor, 100f, 0f, 0f, reaction));

            Assert.IsTrue(actor.IsDown, "撃破で Down。");
            Assert.AreEqual(0, motor.PushCount, "撃破時は押し出さない（死体が滑らない）。");
            Assert.GreaterOrEqual(motor.ClearCount, 1, "撃破確定で進行中の押し出しを打ち切る。");
        }

        [Test]
        public void ForceFlinch_EntersStagger()
        {
            var (actor, _) = MakeActor();
            actor.ForceFlinch(0.35f);
            Assert.AreEqual(EnemyState.Stagger, actor.State, "JG 強制ひるみで Stagger へ。");
        }

        [Test]
        public void ForceFlinch_ZeroSeconds_NoChange()
        {
            var (actor, _) = MakeActor();
            EnemyState before = actor.State;
            actor.ForceFlinch(0f);
            Assert.AreEqual(before, actor.State, "0 秒は無処理。");
        }

        [Test]
        public void ForceFlinch_DoesNotOverrideStunned()
        {
            var (actor, _) = MakeActor(poiseMax: 20f);
            actor.ReceiveHit(Hit(actor, 0f, 25f, 0f, HitReaction.None)); // 体幹 0 → Stunned
            Assert.AreEqual(EnemyState.Stunned, actor.State);

            actor.ForceFlinch(0.35f);
            Assert.AreEqual(EnemyState.Stunned, actor.State, "Stunned（高優先）は上書きしない。");
        }

        [Test]
        public void ForceFlinch_DoesNotOverrideDown()
        {
            var (actor, _) = MakeActor(hp: 10);
            actor.ReceiveHit(Hit(actor, 100f, 0f, 0f, HitReaction.None)); // 撃破 → Down
            Assert.AreEqual(EnemyState.Down, actor.State);

            actor.ForceFlinch(0.35f);
            Assert.AreEqual(EnemyState.Down, actor.State, "Down（最高優先）は上書きしない。");
        }
    }
}
