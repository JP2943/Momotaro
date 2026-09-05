using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Progression;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-00：<see cref="CombatSessionController.EnemyDefeated"/>（撃破報酬の受け手が購読する通知）を検証する。
    /// 初回撃破の受理時にのみ発火すること、報酬要求をそのまま運ぶこと、<see cref="CombatSessionController.AllEnemiesDefeated"/>
    /// より先に発火すること（＝報酬付与が Victory 判定より前に確定すること）、未登録・重複では発火しないことを固定する。
    /// </summary>
    public sealed class CombatSessionRewardEventTests
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

        private CombatSessionController MakeController()
        {
            var go = new GameObject("Session");
            _spawned.Add(go);
            return go.AddComponent<CombatSessionController>();
        }

        private sealed class FakeEnemy : IEnemyDefeatSource
        {
            public EnemyDefeatChannel Defeats { get; } = new EnemyDefeatChannel();
            public int DamageableId { get; set; }
            public bool IsDefeated { get; set; }
            public RewardData Reward { get; set; }
            public EnemyRole Role { get; set; } = EnemyRole.Melee;

            public void Kill()
            {
                Defeats.Publish(new EnemyDefeatedEvent(DamageableId,
                    new EnemyRewardRequest(DamageableId, Role, Reward, Vector3.zero)));
            }
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        [Test]
        public void EnemyDefeated_FiresOnce_ForFirstDefeatOnly()
        {
            CombatSessionController c = MakeController();
            var enemy = new FakeEnemy { DamageableId = 11 };
            c.RegisterEnemy(enemy);

            int fired = 0;
            c.EnemyDefeated += _ => fired++;

            enemy.Kill();
            Assert.AreEqual(1, fired);

            enemy.Kill(); // 重複通知（撃破後の余計な発行）は受理しない。
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void EnemyDefeated_CarriesRewardRequest()
        {
            CombatSessionController c = MakeController();
            RewardData reward = RewardSnapshotTests.MakeReward("reward_enemy_elite", 40, grantOnce: false);
            _spawned.Add(reward);

            var enemy = new FakeEnemy { DamageableId = 21, Reward = reward, Role = EnemyRole.Elite };
            c.RegisterEnemy(enemy);

            EnemyDefeatedEvent? received = null;
            c.EnemyDefeated += e => received = e;

            enemy.Kill();

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(21, received.Value.EnemyId);
            Assert.AreEqual(21, received.Value.Reward.EnemyId);
            Assert.AreEqual(EnemyRole.Elite, received.Value.Reward.Role);
            Assert.AreSame(reward, received.Value.Reward.Reward, "報酬 Data 参照をそのまま運ぶ（付与は受け手の責務）。");
        }

        [Test]
        public void EnemyDefeated_FiresBefore_AllEnemiesDefeated()
        {
            CombatSessionController c = MakeController();
            var enemy = new FakeEnemy { DamageableId = 31 };
            c.RegisterEnemy(enemy);

            var order = new List<string>();
            c.EnemyDefeated += _ => order.Add("reward");
            c.AllEnemiesDefeated += () => order.Add("cleared");

            enemy.Kill();

            Assert.AreEqual(new[] { "reward", "cleared" }, order.ToArray(),
                "報酬付与は生存数 0 到達（Victory 判定の入力）より先に通知される。");
        }

        [Test]
        public void EnemyDefeated_NotFired_ForUnregisteredEnemy()
        {
            CombatSessionController c = MakeController();
            var enemy = new FakeEnemy { DamageableId = 41 };

            int fired = 0;
            c.EnemyDefeated += _ => fired++;

            enemy.Kill(); // 未登録の敵は購読していないため通知が届かない。
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void EnemyDefeated_NotFired_AfterDisable()
        {
            CombatSessionController c = MakeController();
            var enemy = new FakeEnemy { DamageableId = 51 };
            c.RegisterEnemy(enemy);

            int fired = 0;
            c.EnemyDefeated += _ => fired++;

            InvokePrivate(c, "OnDisable"); // Scene 離脱・無効化で購読解除（対称管理）。
            enemy.Kill();

            Assert.AreEqual(0, fired);
        }

        [Test]
        public void EnemyDefeated_SeesUpdatedAliveCount()
        {
            CombatSessionController c = MakeController();
            var a = new FakeEnemy { DamageableId = 71 };
            var b = new FakeEnemy { DamageableId = 72 };
            c.RegisterEnemy(a);
            c.RegisterEnemy(b);
            Assert.AreEqual(2, c.AliveEnemyCount, "前提：2 体登録。");

            var observed = new List<int>();
            c.EnemyDefeated += _ => observed.Add(c.AliveEnemyCount);

            a.Kill();
            b.Kill();

            Assert.AreEqual(new[] { 1, 0 }, observed.ToArray(),
                "通知時点で生存数は減算済み（最終敵の撃破では 0）。");
        }

        [Test]
        public void ThrowingSubscriber_DoesNotCorruptAliveCount()
        {
            CombatSessionController c = MakeController();
            var enemy = new FakeEnemy { DamageableId = 81 };
            c.RegisterEnemy(enemy);
            c.EnemyDefeated += _ => throw new System.InvalidOperationException("subscriber failure");

            Assert.Throws<System.InvalidOperationException>(() => enemy.Kill());
            Assert.AreEqual(0, c.AliveEnemyCount, "購読側が例外を投げても内部の生存数は確定済み。");
        }

        [Test]
        public void EnemyDefeated_NotFired_AfterClearEnemies()
        {
            CombatSessionController c = MakeController();
            var enemy = new FakeEnemy { DamageableId = 61 };
            c.RegisterEnemy(enemy);
            c.ClearEnemies(); // Wave 間 Cleanup。

            int fired = 0;
            c.EnemyDefeated += _ => fired++;

            enemy.Kill();
            Assert.AreEqual(0, fired);
        }
    }
}
