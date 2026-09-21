using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-03 受入：<b>犬丸の与ダメージが敵のヘイトへ載り、主人公が何もしなければ最終的に敵が犬丸を狙う</b>ことを、
    /// 実物同士を繋いで検証する。経路は 犬丸の攻撃 → <see cref="EnemyActor.ReceiveHit"/> → <c>Results</c> 通知 →
    /// <see cref="EnemyThreatTracker.OnHitResult"/> → <c>TryResolveThreatTarget</c>（同一ルートの
    /// <see cref="CompanionThreatBinder"/> へ帰属） → <see cref="EnemyThreatTable"/> の加算 → 対象選択。
    ///
    /// 途中の 1 箇所でも切れると「犬丸が殴っているのに敵が振り向かない」になるが、各部品の単体テストは緑のままになる。
    /// そのため部品ではなく<b>経路全体</b>を 1 本で固定する。
    ///
    /// 数値の前提（§7.1／§7.2）：主人公の基礎ヘイト 50（減衰しない）、仲間 0・獲得倍率 1.5、切替は現対象の 1.25 倍以上、
    /// 減衰は最後の獲得から 3 秒後に 20%/秒。主人公が一度も攻撃しなければ主人公の脅威は 50 のまま動かない。
    /// </summary>
    public sealed class CompanionThreatAccrualTests : CompanionActivityFixture
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

        // ---- 生成 ----

        /// <summary>主人公役（基礎ヘイト 50・攻撃しない）。既存の <see cref="PerceptionTargetBinder"/> をそのまま使う。</summary>
        private PerceptionTargetBinder MakePlayerTarget(Vector3 position)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.transform.position = position;
            var binder = go.AddComponent<PerceptionTargetBinder>();
            InvokePrivate(binder, "OnEnable");
            return binder;
        }

        private (EnemyActor actor, EnemyThreatTracker tracker) MakeEnemy(Vector3 position)
        {
            var archetype = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(archetype);

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = position;

            var actor = go.AddComponent<EnemyActor>();
            SetPrivateField(actor, "_archetype", archetype);
            InvokePrivate(actor, "OnEnable"); // 自己登録（P4-03 受入修正）。

            var tracker = go.AddComponent<EnemyThreatTracker>();
            InvokePrivate(tracker, "OnEnable"); // Results／States を購読する。
            return (actor, tracker);
        }

        private AttackData MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", 2f);
            SetPrivateField(attack, "_useAngle", 180f);
            SetPrivateField(attack, "_cooldownSeconds", 1.2f);
            SetPrivateField(attack, "_startupSeconds", 0.25f);
            SetPrivateField(attack, "_activeSeconds", 0.12f);
            SetPrivateField(attack, "_recoverySeconds", 0.35f);
            SetPrivateField(attack, "_hpMultiplier", 0.8f);
            SetPrivateField(attack, "_poiseDamage", 6f);
            SetPrivateField(attack, "_flinchPower", 0f); // ひるみは別経路の加算なので、本テストでは混ぜない。
            return attack;
        }

        private sealed class Companion
        {
            public CompanionActor Actor;
            public CompanionThreatBinder Binder;
            public CompanionTargetTracker Tracker;
            public CompanionCombatController Combat;
        }

        private Companion MakeCompanion(Vector3 position)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_attackPower", 60f);
            SetPrivateField(data, "_basicAttack", MakeAttack());

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var binder = go.AddComponent<CompanionThreatBinder>();
            binder.Bind(actor);
            InvokePrivate(binder, "OnEnable");

            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);

            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, null, tracker);
            InvokePrivate(combat, "OnEnable");

            return new Companion { Actor = actor, Binder = binder, Tracker = tracker, Combat = combat };
        }

        /// <summary>犬丸に 1 発当てさせ、その間の時間を敵のヘイト評価にも進める（実プレイ 1 サイクル相当）。</summary>
        private static void LandOneHit(Companion companion, EnemyActor enemy, EnemyThreatTracker tracker)
        {
            companion.Tracker.TickTargeting(); // 索敵（実機では Update が回す）。
            companion.Combat.TickCombat(0f); // 判断 → 攻撃開始（予兆）。
            Assert.IsTrue(companion.Combat.IsAttacking, "前提：間合いに居るので攻撃が始まる。");

            companion.Combat.TickCombat(0.25f); // 予兆を越えて判定段へ。
            Assert.IsTrue(companion.Combat.AttackState.IsHitboxActive, "前提：判定中。");

            Assert.IsTrue(companion.Combat.TryApplyHit(enemy, enemy, enemy.WorldPosition), "前提：命中する。");

            companion.Combat.TickCombat(0.47f); // 判定・後隙を終える。
            companion.Combat.TickCombat(1.2f);  // クールダウン。

            tracker.TickSelection(1.92f); // 1 サイクルぶんの時間を敵側にも進める（再評価・減衰）。
        }

        // ---- 経路 ----

        [Test]
        public void CompanionDamage_ReachesEnemy()
        {
            (EnemyActor enemy, EnemyThreatTracker tracker) = MakeEnemy(new Vector3(0f, 0f, 1f));
            Companion companion = MakeCompanion(Vector3.zero);
            int hpBefore = enemy.CurrentHp;

            LandOneHit(companion, enemy, tracker);

            Assert.Less(enemy.CurrentHp, hpBefore, "犬丸の攻撃で敵の HP が減る（減らなければヘイトも増えない）。");
        }

        [Test]
        public void CompanionDamage_IsAttributedToCompanionThreat()
        {
            (EnemyActor enemy, EnemyThreatTracker tracker) = MakeEnemy(new Vector3(0f, 0f, 1f));
            Companion companion = MakeCompanion(Vector3.zero);

            LandOneHit(companion, enemy, tracker);

            float acquired = tracker.Table.GetAcquired(companion.Binder.ActorId);
            Assert.Greater(acquired, 0f,
                "与ダメージが犬丸へ帰属する（同一ルートの CompanionThreatBinder へ解決される）。0 なら攻撃者の同定が切れている。");
        }

        [Test]
        public void CompanionThreat_AccumulatesAcrossAttacks()
        {
            (EnemyActor enemy, EnemyThreatTracker tracker) = MakeEnemy(new Vector3(0f, 0f, 1f));
            Companion companion = MakeCompanion(Vector3.zero);

            LandOneHit(companion, enemy, tracker);
            float afterFirst = tracker.Table.GetAcquired(companion.Binder.ActorId);

            LandOneHit(companion, enemy, tracker);
            float afterSecond = tracker.Table.GetAcquired(companion.Binder.ActorId);

            Assert.Greater(afterSecond, afterFirst,
                "攻撃を続けている間は蓄積が伸びる（1 発ごとに 0 へ戻らない・減衰で相殺されない）。");
        }

        // ---- 対象選択 ----

        [Test]
        public void EnemyTargetsCompanion_WhenPlayerNeverAttacks()
        {
            MakePlayerTarget(new Vector3(0f, 0f, -3f)); // 基礎 50 のまま一度も攻撃しない。
            (EnemyActor enemy, EnemyThreatTracker tracker) = MakeEnemy(new Vector3(0f, 0f, 1f));
            Companion companion = MakeCompanion(Vector3.zero);

            tracker.TickSelection(1f);
            Assert.AreNotEqual(companion.Binder.ActorId, tracker.CurrentTargetId, "前提：最初は主人公（基礎 50）を狙う。");

            // 1 発 10.5（HP 4×1 ＋ 体幹 6×0.5 の 1.5 倍）。切替には 50×1.25＝62.5 超が要るので 6 発で足りる。
            for (int i = 0; i < 8; i++)
            {
                LandOneHit(companion, enemy, tracker);
            }

            Assert.AreEqual(companion.Binder.ActorId, tracker.CurrentTargetId,
                "主人公が何もしなければ、殴り続けた犬丸へ狙いが移る。現在の脅威="
                + tracker.CurrentThreat.ToString("0.0")
                + " 犬丸の獲得=" + tracker.Table.GetAcquired(companion.Binder.ActorId).ToString("0.0"));
        }

        [Test]
        public void EnemyKeepsTargetingCompanion_WhilePlayerStaysIdle()
        {
            MakePlayerTarget(new Vector3(0f, 0f, -3f));
            (EnemyActor enemy, EnemyThreatTracker tracker) = MakeEnemy(new Vector3(0f, 0f, 1f));
            Companion companion = MakeCompanion(Vector3.zero);

            for (int i = 0; i < 8; i++)
            {
                LandOneHit(companion, enemy, tracker);
            }

            Assert.AreEqual(companion.Binder.ActorId, tracker.CurrentTargetId, "前提：犬丸を狙っている。");

            // さらに殴り続ける間は、主人公（50 固定）へ戻らない。
            for (int i = 0; i < 4; i++)
            {
                LandOneHit(companion, enemy, tracker);
            }

            Assert.AreEqual(companion.Binder.ActorId, tracker.CurrentTargetId,
                "殴り続けている限り狙いは戻らない（戻るなら蓄積が失われている）。");
        }
    }
}
