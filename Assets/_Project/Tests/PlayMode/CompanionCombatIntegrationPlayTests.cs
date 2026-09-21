using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy;
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
    /// P4-08：仲間の戦闘を<b>実行時の配管ごと</b>検証する統合受入。EditMode のテストは判断・数値・契約を決定的に固定するが、
    /// 実機で起きた不具合はいずれもそこでは捕まらなかった。
    ///
    /// <list type="number">
    /// <item><description>敵が索敵レジストリへ未登録で、仲間の候補が常に 0 件だった（部品は全部緑）</description></item>
    /// <item><description>判定 Box の到達距離が攻撃開始距離より短く、延々と空振りしていた
    /// （テストが命中適用を直接呼び、物理判定を通していなかった）</description></item>
    /// </list>
    ///
    /// どちらも「実際に Update を回し、実際に Collider を置き、実際に OverlapBox を通す」ことでしか検出できない。
    /// 本テストはその経路だけを見る（間合いや秒数の境界は EditMode 側で固定済み）。
    /// </summary>
    public sealed class CompanionCombatIntegrationPlayTests : CompanionActivityFixture
    {
        private readonly List<Object> _spawned = new List<Object>();
        private float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            PerceptionTargetRegistry.Clear();

            // 前のテストが残した GameMode を持ち越さない。Exploration／Combat 以外だと戦闘・被弾・防御の Update が
            // まるごと止まり、「索敵は動いているのに何もしない」という紛らわしい失敗になる。
            GameModeProvider.Current = null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
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

        // ---- 生成 ----

        private AttackData MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", 2f);
            SetPrivateField(attack, "_useAngle", 90f);
            SetPrivateField(attack, "_cooldownSeconds", 1.2f);
            SetPrivateField(attack, "_startupSeconds", 0.25f);
            SetPrivateField(attack, "_activeSeconds", 0.12f);
            SetPrivateField(attack, "_recoverySeconds", 0.35f);
            SetPrivateField(attack, "_hpMultiplier", 0.8f);
            SetPrivateField(attack, "_poiseDamage", 6f);
            SetPrivateField(attack, "_flinchPower", 0f);
            return attack;
        }

        private sealed class CompanionRig
        {
            public GameObject Root;
            public CompanionActor Actor;
            public CompanionTargetTracker Tracker;
            public CompanionCombatController Combat;
            public CompanionHitReceiver Receiver;
            public CompanionThreatBinder Binder;
        }

        /// <summary>実機と同じ構成の犬丸を組む（Prefab は PlayMode から読めないため同じ部品を手で載せる）。</summary>
        private CompanionRig MakeCompanion(Vector3 position)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_attackPower", 60f);
            SetPrivateField(data, "_basicAttack", MakeAttack());

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;
            go.SetActive(false); // 上と同じ理由（Data を入れてから Awake を走らせる）。

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = 1.2f;
            capsule.center = new Vector3(0f, 0.6f, 0f);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var motor = go.AddComponent<CompanionMotor>();
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);

            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);

            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            var binder = go.AddComponent<CompanionThreatBinder>();
            binder.Bind(actor);

            go.SetActive(true);

            return new CompanionRig
            {
                Root = go,
                Actor = actor,
                Tracker = tracker,
                Combat = combat,
                Receiver = receiver,
                Binder = binder,
            };
        }

        private EnemyActor MakeEnemy(Vector3 position, bool withThreatTracker = false)
        {
            var archetype = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(archetype);

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = position;

            // PlayMode では AddComponent の時点で Awake が走る。Data を差し込む前に Runtime が組み上がってしまうと
            // 最大 HP などが既定値で確定するため、非アクティブで組み立ててから起こす。
            go.SetActive(false);

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.32f;
            capsule.height = 1.2f;
            capsule.center = new Vector3(0f, 0.6f, 0f);

            var actor = go.AddComponent<EnemyActor>();
            SetPrivateField(actor, "_archetype", archetype);

            if (withThreatTracker)
            {
                go.AddComponent<EnemyThreatTracker>();
            }

            go.SetActive(true);
            return actor;
        }

        /// <summary>攻撃しない主人公役（基礎ヘイト 50 のまま動かない）。</summary>
        private PerceptionTargetBinder MakePlayerTarget(Vector3 position)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.transform.position = position;
            return go.AddComponent<PerceptionTargetBinder>();
        }

        /// <summary>失敗時に「どこで止まっているか」を一目で分かるようにする（憶測で往復しないため）。</summary>
        private static string Diagnose(CompanionRig companion, EnemyActor enemy)
        {
            return "state=" + companion.Actor.State
                + " decision=" + companion.Combat.Decision
                + " combatTarget=" + (companion.Combat.CurrentTarget != null)
                + " combatEnabled=" + companion.Combat.isActiveAndEnabled
                + " hasTarget=" + companion.Tracker.HasTarget
                + " candidates=" + companion.Tracker.CandidateCount
                + " targetChanges=" + companion.Tracker.TargetChanges
                + " attacks=" + companion.Combat.AttackCount
                + " hits=" + companion.Combat.HitCount
                + " distance=" + companion.Combat.LastDistance.ToString("0.00")
                + " hasAttackData=" + companion.Combat.HasAttackData
                + " enemyActive=" + enemy.IsActive
                + " enemyHp=" + enemy.CurrentHp
                + " registry=" + PerceptionTargetRegistry.Count
                + " timeScale=" + Time.timeScale.ToString("0.00");
        }

        // ---- 命中経路（実 Update ＋ 実物理） ----

        [UnityTest]
        public IEnumerator Companion_DamagesEnemy_ThroughUpdateAndPhysics()
        {
            EnemyActor enemy = MakeEnemy(new Vector3(0f, 0f, 1.2f));
            CompanionRig companion = MakeCompanion(Vector3.zero);
            yield return null; // Awake/OnEnable。

            int hpBefore = enemy.CurrentHp;

            // 索敵 → 接近 → 攻撃 → 判定 → 命中 まで、実際の Update と OverlapBox を通す。
            for (int i = 0; i < 240 && enemy.CurrentHp >= hpBefore; i++)
            {
                yield return null;
            }

            Assert.Less(enemy.CurrentHp, hpBefore,
                "犬丸が敵に実際にダメージを与える。ここが赤なら、判定が届いていないか索敵が繋がっていない"
                + "（部品の単体テストが全部緑でも起こりうる）。" + Diagnose(companion, enemy));
            Assert.Greater(companion.Combat.HitCount, 0, "命中が記録される。");
        }

        [UnityTest]
        public IEnumerator Companion_AcquiresRegisteredEnemy_WithoutManualRegistration()
        {
            EnemyActor enemy = MakeEnemy(new Vector3(0f, 0f, 4f));
            CompanionRig companion = MakeCompanion(Vector3.zero);

            yield return null;
            yield return null;

            Assert.IsTrue(companion.Tracker.HasTarget,
                "敵を置いて有効化しただけで索敵に載る（レジストリへの自己登録が繋がっている）。");
            Assert.AreSame(enemy, companion.Tracker.CurrentTarget);
        }

        // ---- 被弾側として物理世界に居るか ----

        [UnityTest]
        public IEnumerator Companion_IsFoundAsDamageableByPhysicsQuery()
        {
            CompanionRig companion = MakeCompanion(Vector3.zero);
            yield return null;

            // 敵の攻撃判定と同じ手順（重なった Collider から親の IDamageable を辿る）で見つかること。
            Physics.SyncTransforms();
            Collider[] hits = Physics.OverlapSphere(
                new Vector3(0f, 0.6f, 0f), 1f, ~0, QueryTriggerInteraction.Collide);

            IDamageable found = null;
            for (int i = 0; i < hits.Length; i++)
            {
                var damageable = hits[i].GetComponentInParent<IDamageable>();
                if (damageable is CompanionHitReceiver)
                {
                    found = damageable;
                    break;
                }
            }

            Assert.IsNotNull(found,
                "犬丸が物理世界で被弾対象として見つかる。ここが赤なら敵の攻撃はすり抜ける"
                + "（Collider が無い・受け口がルートに無い）。");
            Assert.AreSame(companion.Receiver, found);
        }

        // ---- ヘイトの移動（主人公が何もしない場合） ----

        [UnityTest]
        public IEnumerator Enemy_ShiftsTargetToCompanion_WhenOnlyCompanionAttacks()
        {
            MakePlayerTarget(new Vector3(0f, 0f, -4f)); // 基礎ヘイト 50 のまま一度も攻撃しない。
            EnemyActor enemy = MakeEnemy(new Vector3(0f, 0f, 1.2f), withThreatTracker: true);
            CompanionRig companion = MakeCompanion(Vector3.zero);
            EnemyThreatTracker threat = enemy.GetComponent<EnemyThreatTracker>();

            yield return null;

            // 実時間で 10 秒以上かかるため加速する。1 フレームの経過が判定時間（0.12 秒）を超えない範囲に留める
            // （超えると判定段を跨いでしまい、実機とは違う条件になる）。
            Time.timeScale = 3f;

            for (int i = 0; i < 900 && threat.CurrentTargetId != companion.Binder.ActorId; i++)
            {
                yield return null;
            }

            Assert.AreEqual(companion.Binder.ActorId, threat.CurrentTargetId,
                "主人公が何もしなければ、殴り続けた犬丸へ狙いが移る。犬丸の獲得ヘイト="
                + threat.Table.GetAcquired(companion.Binder.ActorId).ToString("0.0")
                + " " + Diagnose(companion, enemy));
        }

        // ---- 後始末 ----

        [UnityTest]
        public IEnumerator DestroyedCompanion_LeavesNoRegistryResidue()
        {
            MakeEnemy(new Vector3(0f, 0f, 3f));
            CompanionRig companion = MakeCompanion(Vector3.zero);
            yield return null;

            int before = PerceptionTargetRegistry.Count;
            Object.DestroyImmediate(companion.Root);
            yield return null;

            Assert.AreEqual(before - 1, PerceptionTargetRegistry.Count,
                "破棄で登録を残さない（次の Scene・次のテストへ持ち越さない）。");
        }
    }
}
