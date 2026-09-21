using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-03 受入：<b>攻撃を始めた距離に判定が実際に届く</b>ことを、物理判定（OverlapBox）を通して検証する。
    ///
    /// これまでの仲間側テストは <c>TryApplyHit</c> を直接呼んでおり、<c>PollHitbox</c>（唯一の物理問い合わせ）を
    /// 一度も通していなかった。そのため「判定 Box の到達距離 1.2m ＜ 攻撃開始距離 1.6m」という不整合を検出できず、
    /// 犬丸は間合いの外から振って延々と空振りしていた。ここでは実際に Collider を置き、
    /// <see cref="CompanionCombatController.TickCombat"/> 経由で判定を出させて命中を確認する。
    ///
    /// 併せて「予兆のあいだに対象が離れても、判定が届く距離までなら当たる」ことも固定する。攻撃開始距離と判定到達距離の
    /// 差はそのための余裕であり、両者を同じにしてはならない。
    /// </summary>
    public sealed class CompanionHitboxReachTests : CompanionActivityFixture
    {
        private const float UseRange = 2f;      // 判定が届く距離。
        private const float Startup = 0.25f;
        private const float Active = 0.12f;
        private const float Recovery = 0.35f;

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

        /// <summary>敵役。実際に Collider を持ち、命中を数える。</summary>
        private sealed class ColliderEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.back; // 仲間の方を向く（背後補正を混ぜない）。
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

        private ColliderEnemy MakeEnemy(float distance)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = new Vector3(0f, 0f, distance);

            // 実機の敵 Prefab と同程度の当たり判定（半径 0.32／高さ 1.2／中心 y=0.6）。
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = 1.2f;
            capsule.center = new Vector3(0f, 0.6f, 0f);

            var enemy = go.AddComponent<ColliderEnemy>();
            PerceptionTargetRegistry.Register(enemy);
            return enemy;
        }

        private sealed class Companion
        {
            public CompanionTargetTracker Tracker;
            public CompanionCombatController Combat;
        }

        private Companion MakeCompanion()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", UseRange);
            SetPrivateField(attack, "_useAngle", 180f);
            SetPrivateField(attack, "_cooldownSeconds", 1.2f);
            SetPrivateField(attack, "_startupSeconds", Startup);
            SetPrivateField(attack, "_activeSeconds", Active);
            SetPrivateField(attack, "_recoverySeconds", Recovery);

            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_attackPower", 60f);
            SetPrivateField(data, "_basicAttack", attack);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = Vector3.zero;

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);

            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, null, tracker);
            InvokePrivate(combat, "OnEnable");

            return new Companion { Tracker = tracker, Combat = combat };
        }

        /// <summary>
        /// EditMode の物理問い合わせが使えるか確認する。使えない環境では検証の意味が無いため明示的にスキップする
        /// （黙って緑にしない）。
        /// </summary>
        private static void RequirePhysicsQueries(ColliderEnemy enemy)
        {
            Physics.SyncTransforms();
            Collider[] hits = Physics.OverlapBox(
                enemy.transform.position + Vector3.up * 0.6f, new Vector3(0.5f, 0.5f, 0.5f),
                Quaternion.identity, ~0, QueryTriggerInteraction.Collide);

            if (hits == null || hits.Length == 0)
            {
                Assert.Ignore("この EditMode 環境では物理問い合わせ（OverlapBox）が使えないため、判定到達の検証をスキップします。");
            }
        }

        /// <summary>攻撃を開始し、判定段まで進める（判定は TickCombat の中で自動的に出る）。</summary>
        private static void BeginAttack(Companion companion)
        {
            companion.Tracker.TickTargeting();
            companion.Combat.TickCombat(0f);
            Assert.IsTrue(companion.Combat.IsAttacking, "前提：攻撃開始距離の内側なので攻撃が始まる。");
        }

        private static void AdvanceToActive(Companion companion)
        {
            companion.Combat.TickCombat(Startup);
            Assert.AreEqual(CompanionAttackPhase.Active, companion.Combat.AttackState.Phase, "前提：判定段。");
        }

        // ---- 到達距離 ----

        [Test]
        public void Attack_ConnectsAtAttackStartDistance()
        {
            float start = UseRange * CompanionAttackSettings.AttackStartRatio; // 1.4
            ColliderEnemy enemy = MakeEnemy(start);
            RequirePhysicsQueries(enemy);
            Companion companion = MakeCompanion();

            BeginAttack(companion);
            AdvanceToActive(companion);

            Assert.AreEqual(1, enemy.ReceivedHits,
                "攻撃を始めた距離に判定が届く。届かないなら、間合いの外から振って空振りし続ける。");
            Assert.AreEqual(1, companion.Combat.HitCount);
        }

        [Test]
        public void Attack_ConnectsCloseUp()
        {
            ColliderEnemy enemy = MakeEnemy(0.6f);
            RequirePhysicsQueries(enemy);
            Companion companion = MakeCompanion();

            BeginAttack(companion);
            AdvanceToActive(companion);

            Assert.AreEqual(1, enemy.ReceivedHits, "密着でも当たる（判定が前方へ寄りすぎて足元が抜けない）。");
        }

        [Test]
        public void Attack_StillConnects_WhenTargetDriftsToUseRange()
        {
            float start = UseRange * CompanionAttackSettings.AttackStartRatio;
            ColliderEnemy enemy = MakeEnemy(start);
            RequirePhysicsQueries(enemy);
            Companion companion = MakeCompanion();

            BeginAttack(companion);

            // 予兆のあいだに対象が離れる（敵は主人公を追って動き続ける）。判定が届く距離までなら当たること。
            enemy.transform.position = new Vector3(0f, 0f, UseRange);
            AdvanceToActive(companion);

            Assert.AreEqual(1, enemy.ReceivedHits,
                "攻撃開始距離と判定到達距離の差が、予兆中の移動を吸収する余裕になっている。");
        }

        [Test]
        public void Attack_MissesBeyondUseRange()
        {
            float start = UseRange * CompanionAttackSettings.AttackStartRatio;
            ColliderEnemy enemy = MakeEnemy(start);
            RequirePhysicsQueries(enemy);
            Companion companion = MakeCompanion();

            BeginAttack(companion);

            // 判定が届く距離より明確に外へ逃げた場合は当たらない（無限に伸びる判定になっていないこと）。
            enemy.transform.position = new Vector3(0f, 0f, UseRange + 1.5f);
            AdvanceToActive(companion);

            Assert.AreEqual(0, enemy.ReceivedHits, "届く距離を超えた対象には当たらない。");
        }

        [Test]
        public void Attack_DoesNotStart_BeyondAttackStartDistance()
        {
            float start = UseRange * CompanionAttackSettings.AttackStartRatio;
            MakeEnemy(start + 0.2f);
            Companion companion = MakeCompanion();

            companion.Tracker.TickTargeting();
            companion.Combat.TickCombat(0f);

            Assert.IsFalse(companion.Combat.IsAttacking, "攻撃開始距離の外では振らない。");
            Assert.AreEqual(CompanionEngageDecision.Chase, companion.Combat.Decision, "詰めてから振る。");
        }
    }
}
