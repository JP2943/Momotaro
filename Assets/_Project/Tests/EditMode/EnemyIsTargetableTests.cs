using System.Collections.Generic;
using System.Reflection;
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
    /// P4-03 受入修正：<b>敵が「狙える対象」として登録されている</b>ことを検証する。
    ///
    /// Phase 3 までの <see cref="PerceptionTargetRegistry"/> は「敵が主人公を見つける」ための片方向で、登録していたのは
    /// 主人公だけだった。仲間は逆向きに敵を探すため、敵が登録されていないと候補が常に 0 件になり、索敵・接近・攻撃の
    /// 実装がすべて正しくても犬丸は何もしない。
    ///
    /// 本テストは<b>本物の <see cref="EnemyActor"/></b> を相手に検証する。仲間側の単体テストは検証用の敵役を手で登録して
    /// いたため、この登録漏れを検出できなかった（アルゴリズムは通るが、実際の敵とは繋がっていなかった）。同じ穴を
    /// 再び開けないよう、ここでは「敵を作って有効化しただけで、仲間が捕捉できる」ことを固定する。
    /// </summary>
    /// <remarks>
    /// 仲間の索敵は活動 Context（<see cref="CompanionActivityProvider"/>）が無いと停止する（P4-FIX-R2：未注入は許可側へ戻さない）。
    /// 仲間側の検証は <see cref="CompanionActivityFixture"/> で自由探索の供給元を差してから行う。
    /// </remarks>
    public sealed class EnemyIsTargetableTests : CompanionActivityFixture
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

        private EnemyActor MakeEnemy(Vector3 position = default)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = position;
            var actor = go.AddComponent<EnemyActor>();
            InvokePrivate(actor, "OnEnable"); // EditMode では自動で呼ばれないことがあるため明示する（登録は冪等）。
            return actor;
        }

        private (CompanionActor actor, CompanionTargetTracker tracker) MakeCompanion(Vector3 position = default)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;
            var actor = go.AddComponent<CompanionActor>();
            actor.ResetState(CompanionState.Follow);
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            return (actor, tracker);
        }

        // ---- 登録 ----

        [Test]
        public void Enemy_RegistersItselfOnEnable()
        {
            EnemyActor enemy = MakeEnemy();

            Assert.AreEqual(1, PerceptionTargetRegistry.Count, "有効化しただけで登録される（Prefab への付け忘れが起きない）。");
            Assert.AreEqual(CombatFaction.Enemy, enemy.Faction);
        }

        [Test]
        public void Enemy_IsHostileTargetForAlly()
        {
            MakeEnemy(new Vector3(0f, 0f, 3f));

            bool found = PerceptionTargetRegistry.TryGetNearestHostile(
                Vector3.zero, CombatFaction.Ally, out IPerceptionTarget nearest);

            Assert.IsTrue(found, "仲間から見て敵は敵対対象になる。");
            Assert.AreEqual(CombatFaction.Enemy, nearest.Faction);
        }

        [Test]
        public void Enemy_UnregistersOnDisable()
        {
            EnemyActor enemy = MakeEnemy();

            InvokePrivate(enemy, "OnDisable");

            Assert.AreEqual(0, PerceptionTargetRegistry.Count, "撃破後の破棄・Scene 離脱で登録を残さない。");
        }

        [Test]
        public void Enemies_AreNotCandidatesForEachOther()
        {
            MakeEnemy(new Vector3(0f, 0f, 1f));
            MakeEnemy(new Vector3(0f, 0f, 2f));
            var buffer = new List<IThreatTarget>();

            PerceptionTargetRegistry.CollectHostileThreatTargets(Vector3.zero, CombatFaction.Enemy, 0f, buffer);

            Assert.AreEqual(0, buffer.Count, "敵同士は敵対しない（敵 AI の候補は増えない）。");
        }

        // ---- 仲間の索敵（本来ここで繋がっているべきだった接続） ----

        [Test]
        public void Companion_AcquiresRealEnemy()
        {
            (CompanionActor _, CompanionTargetTracker tracker) = MakeCompanion();
            EnemyActor enemy = MakeEnemy(new Vector3(0f, 0f, 3f));

            tracker.TickTargeting();

            Assert.IsTrue(tracker.HasTarget, "本物の敵を捕捉できる（検証用の敵役を手で登録しなくても繋がる）。");
            Assert.AreSame(enemy, tracker.CurrentTarget);
        }

        [Test]
        public void Companion_PicksNearestRealEnemy()
        {
            (CompanionActor _, CompanionTargetTracker tracker) = MakeCompanion();
            MakeEnemy(new Vector3(0f, 0f, 6f));
            EnemyActor near = MakeEnemy(new Vector3(0f, 0f, 2f));

            tracker.TickTargeting();

            Assert.AreSame(near, tracker.CurrentTarget, "最寄りの敵を狙う。");
        }

        [Test]
        public void Companion_DropsDefeatedEnemy()
        {
            (CompanionActor _, CompanionTargetTracker tracker) = MakeCompanion();
            EnemyActor enemy = MakeEnemy(new Vector3(0f, 0f, 3f));
            tracker.TickTargeting();
            Assert.IsTrue(tracker.HasTarget, "前提：捕捉している。");

            Assert.IsTrue(enemy.RequestState(EnemyState.Down, EnemyStateChangeReason.Defeated), "前提：ダウンへ遷移できる。");
            tracker.TickTargeting();

            Assert.IsFalse(enemy.IsActive, "ダウンした敵は候補から外れる。");
            Assert.IsFalse(tracker.HasTarget, "倒した敵を狙い続けない（次の敵へ移れる）。");
        }
    }
}
