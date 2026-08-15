using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-06 受入修正 統合検証（req7/8/9）。Threat 選択が実 AI（認識・攻撃開始・照準）へ接続されていることを、公開シームで
    /// 決定的に確認する。最寄りでなく Threat 最大対象へ攻撃を開始すること、攻撃中は照準対象が固定されること、主人公と仲間が
    /// 近接しても攻撃者本人へヘイトが加算されること。EditMode（Update/物理なし）で seams により駆動する。
    /// </summary>
    public sealed class EnemyTargetSelectionIntegrationTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

        [TearDown]
        public void TearDown()
        {
            PerceptionTargetRegistry.Clear();
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

        private sealed class FakeThreatTarget : IThreatTarget
        {
            public int ActorId { get; set; }
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public Vector3 Position { get; set; }
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat { get; set; }
            public float AcquiredThreatMultiplier { get; set; } = 1f;
        }

        private sealed class AlwaysVisibleProbe : ILineOfSightProbe
        {
            public bool HasLineOfSight(Vector3 from, Vector3 to) => true;
        }

        private EnemyAttackData MakeAttack()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_attackClass", EnemyAttackClass.Normal);
            SetField(d, "_useRange", 2.0f);
            SetField(d, "_useAngle", 120f);
            SetField(d, "_cooldownSeconds", 1.0f);
            SetField(d, "_prepareSeconds", 0.25f);
            SetField(d, "_activeSeconds", 0.10f);
            SetField(d, "_recoverySeconds", 0.20f);
            SetField(d, "_trackingStopSeconds", 0.15f);
            SetField(d, "_slotKind", AttackSlotKind.MeleeNormal);
            SetField(d, "_aimingMode", EnemyAimingMode.CurrentPosition);
            return d;
        }

        private EnemyArchetypeData MakeArchetype()
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(a);
            SetField(a, "_maxHp", 100);
            SetField(a, "_stopDistance", 1.6f);
            SetField(a, "_viewAngleDegrees", 120f);
            SetField(a, "_viewDistance", 8f);
            SetField(a, "_alertViewDistance", 10f);
            SetField(a, "_fullRecognitionSeconds", 0.25f);
            SetField(a, "_attacks", new[] { MakeAttack() });
            return a;
        }

        // 認識→攻撃までを持つ敵（物理・Update 不要。motor は RequireComponent で自動付与）。
        private GameObject BuildEnemy(out EnemyPerception perception, out EnemyThreatTracker tracker,
            out EnemyAttackController combat, out EnemyBrain brain)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = Vector3.zero;

            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", MakeArchetype());
            perception = go.AddComponent<EnemyPerception>();
            tracker = go.AddComponent<EnemyThreatTracker>();
            combat = go.AddComponent<EnemyAttackController>();
            brain = go.AddComponent<EnemyBrain>(); // EnemyMotor/Rigidbody を自動付与。
            perception.SetLineOfSightProbe(new AlwaysVisibleProbe());
            return go;
        }

        [Test]
        public void Brain_AttacksHighestThreatTarget_NotNearest()
        {
            // 近い低ヘイトの仲間と、遠い高ヘイトの主人公。敵は最寄りでなく Threat 最大（主人公）を狙う（req7）。
            var ally = new FakeThreatTarget
            {
                ActorId = 102, Faction = CombatFaction.Ally, BaseThreat = 0f, Position = new Vector3(0, 0, 0.5f),
            };
            var player = new FakeThreatTarget
            {
                ActorId = 101, Faction = CombatFaction.Player, BaseThreat = 50f, Position = new Vector3(0, 0, 1.2f),
            };
            PerceptionTargetRegistry.Register(ally);
            PerceptionTargetRegistry.Register(player);

            BuildEnemy(out EnemyPerception perception, out EnemyThreatTracker tracker,
                out EnemyAttackController combat, out EnemyBrain brain);

            // 選択→認識を進めて Alert 化（注視は Threat 最大＝主人公）。
            for (int i = 0; i < 6 && perception.Phase != PerceptionPhase.Alert; i++)
            {
                tracker.TickSelection(0.15f);
                perception.EvaluateOnce(0.15f);
            }

            Assert.AreEqual(PerceptionPhase.Alert, perception.Phase, "Threat 対象を認識して Alert になる。");
            Assert.AreEqual(player.ActorId, tracker.CurrentTargetId, "Threat 最大＝主人公を選択。");
            Assert.AreEqual(1.2f, perception.LastKnownPosition.z, 1e-2f, "最寄りの仲間(0.5)でなく主人公(1.2)を注視。");

            tracker.TickSelection(0.15f);
            brain.TickBrain(0.15f); // 停止帯で攻撃開始（照準対象＝主人公）。

            Assert.IsTrue(combat.IsAttacking, "停止帯で攻撃を開始する。");
            Assert.AreEqual(player.ActorId, combat.AttackTargetId, "最寄りでなく Threat 最大対象を攻撃する（req7）。");
        }

        [Test]
        public void AttackTarget_StaysLocked_WhenAnotherTargetBecomesHigherThreat()
        {
            var player = new FakeThreatTarget
            {
                ActorId = 201, Faction = CombatFaction.Player, BaseThreat = 50f, Position = new Vector3(0, 0, 1.2f),
            };
            var ally = new FakeThreatTarget
            {
                ActorId = 202, Faction = CombatFaction.Ally, BaseThreat = 0f, Position = new Vector3(0, 0, 1.3f),
            };
            PerceptionTargetRegistry.Register(player);
            PerceptionTargetRegistry.Register(ally);

            BuildEnemy(out _, out EnemyThreatTracker tracker, out EnemyAttackController combat, out _);
            tracker.TickSelection(0.1f);

            // 主人公を明示的に照準して攻撃開始（req8 前提）。
            Assert.IsTrue(combat.TryStartAttack(player, player.Position, Vector3.zero));
            Assert.AreEqual(player.ActorId, combat.AttackTargetId);

            // 攻撃中に仲間のヘイトが主人公を 25% 超で上回る。
            tracker.Table.AddThreat(ally, ThreatSource.DogTaunt); // +100（仲間×1.5=150）
            tracker.TickSelection(0.1f); // 近接攻撃中は Tracker も切替えない（attackLocked）。
            combat.TickAttack(0.1f);     // Prepare 進行（まだ攻撃中）。

            Assert.IsTrue(combat.IsAttacking);
            Assert.AreEqual(player.ActorId, combat.AttackTargetId, "攻撃終了まで照準対象は変わらない（req8）。");
            Assert.AreEqual(player.ActorId, tracker.CurrentTargetId, "攻撃中は Tracker も切替えない。");

            // 攻撃を最後まで進めると固定が解け、次回再評価で切替可能になる。
            for (int i = 0; i < 40 && combat.IsAttacking; i++)
            {
                combat.TickAttack(0.05f);
            }

            Assert.IsFalse(combat.IsAttacking, "攻撃は完了する。");
            Assert.AreEqual(0, combat.AttackTargetId, "終了で照準対象の固定が解ける。");
        }

        // ---- req9：攻撃者本人へ加算 ----

        private sealed class TestCombatActor : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
        }

        private GameObject MakeEntity(string name, CombatFaction faction, Vector3 pos, bool withActor)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            go.transform.position = pos;
            var binder = go.AddComponent<PerceptionTargetBinder>();
            SetField(binder, "_faction", faction);
            SetField(binder, "_baseThreat", 0f);
            PerceptionTargetRegistry.Register(binder); // EditMode は OnEnable 非実行のため明示登録。
            if (withActor)
            {
                go.AddComponent<TestCombatActor>().Faction = faction;
            }

            return go;
        }

        [Test]
        public void Threat_IsAttributedToAttacker_NotAdjacentAlly()
        {
            var enemyGo = new GameObject("Enemy");
            _spawned.Add(enemyGo);
            var enemyActor = enemyGo.AddComponent<EnemyActor>();
            SetField(enemyActor, "_archetype", MakeArchetype());
            var tracker = enemyGo.AddComponent<EnemyThreatTracker>();

            // 主人公と仲間がほぼ同座標で近接。攻撃者は主人公本人。
            GameObject player = MakeEntity("Player", CombatFaction.Player, new Vector3(0, 0, 1f), withActor: true);
            GameObject ally = MakeEntity("Ally", CombatFaction.Ally, new Vector3(0.05f, 0, 1f), withActor: false);
            var playerBinder = player.GetComponent<PerceptionTargetBinder>();
            var allyBinder = ally.GetComponent<PerceptionTargetBinder>();
            var attacker = player.GetComponent<TestCombatActor>();

            // 主人公が敵へ HP10 を与えた命中結果を敵の被弾として通知する。
            var applied = new HitDamage(10f, 0f, 0f);
            var result = HitResult.Damage(default(HitId), attacker, enemyActor, applied);
            tracker.OnHitResult(result);

            Assert.AreEqual(10f, tracker.Table.GetAcquired(playerBinder.ActorId), 1e-4f, "攻撃者本人（主人公）へ加算。");
            Assert.AreEqual(0f, tracker.Table.GetAcquired(allyBinder.ActorId), 1e-4f, "近接する仲間へは加算されない（req9）。");
        }
    }
}
