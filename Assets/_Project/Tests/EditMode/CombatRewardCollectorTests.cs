using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Progression;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-00：撃破報酬の受け手（<see cref="CombatRewardCollector"/>）が、Session の撃破通知から徳を実付与することを検証する。
    /// 一般敵報酬（GrantOnce=false）の累積、GrantOnce 報酬の重複拒否、報酬未設定の敵の無視、付与先未配線・無効化時の安全性、
    /// 購読の対称管理（Disable 後は付与しない）を対象とする。
    /// </summary>
    public sealed class CombatRewardCollectorTests
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

        private sealed class FakeEnemy : IEnemyDefeatSource
        {
            public EnemyDefeatChannel Defeats { get; } = new EnemyDefeatChannel();
            public int DamageableId { get; set; }
            public bool IsDefeated { get; set; }
            public RewardData Reward { get; set; }

            public void Kill()
            {
                Defeats.Publish(new EnemyDefeatedEvent(DamageableId,
                    new EnemyRewardRequest(DamageableId, EnemyRole.Melee, Reward, Vector3.zero)));
            }
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        private T Make<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private RewardData Reward(string id, int virtue, bool grantOnce, string itemId = null)
        {
            RewardData data = RewardSnapshotTests.MakeReward(id, virtue, grantOnce, itemId);
            _spawned.Add(data);
            return data;
        }

        /// <summary>Session／Progress／Collector を配線し、購読を開始した状態で返す。</summary>
        private (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector collector) MakeRig()
        {
            CombatSessionController session = Make<CombatSessionController>("Session");
            PlayerProgressHolder progress = Make<PlayerProgressHolder>("PlayerProgress");
            CombatRewardCollector collector = Make<CombatRewardCollector>("RewardCollector");
            collector.Bind(session, progress);
            InvokePrivate(collector, "OnEnable"); // 二重購読は内部フラグで抑止されるため、明示呼び出しは安全。
            return (session, progress, collector);
        }

        private FakeEnemy Register(CombatSessionController session, int id, RewardData reward)
        {
            var enemy = new FakeEnemy { DamageableId = id, Reward = reward };
            session.RegisterEnemy(enemy);
            return enemy;
        }

        [Test]
        public void Defeat_GrantsVirtue()
        {
            (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector collector) = MakeRig();
            FakeEnemy enemy = Register(session, 1, Reward("reward_enemy_melee", 10, grantOnce: false));

            enemy.Kill();

            Assert.AreEqual(10, progress.Virtue);
            Assert.AreEqual(1, collector.GrantedCount);
            Assert.AreEqual(10, collector.LastGrantedVirtue);
        }

        [Test]
        public void RepeatableReward_AccumulatesAcrossEnemies()
        {
            (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector collector) = MakeRig();
            RewardData melee = Reward("reward_enemy_melee", 10, grantOnce: false);
            FakeEnemy a = Register(session, 1, melee);
            FakeEnemy b = Register(session, 2, melee);
            FakeEnemy c = Register(session, 3, melee);

            a.Kill();
            b.Kill();
            c.Kill();

            Assert.AreEqual(30, progress.Virtue, "一般敵は同じ Reward を共有しても撃破ごとに累積する（GrantOnce=false）。");
            Assert.AreEqual(3, collector.GrantedCount);
            Assert.AreEqual(0, collector.AlreadyGrantedCount);
        }

        [Test]
        public void GrantOnceReward_IsGrantedOnlyOnce_AcrossEnemies()
        {
            (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector collector) = MakeRig();
            RewardData once = Reward("reward_first_elite", 40, grantOnce: true);
            FakeEnemy a = Register(session, 1, once);
            FakeEnemy b = Register(session, 2, once);

            a.Kill();
            b.Kill();

            Assert.AreEqual(40, progress.Virtue);
            Assert.AreEqual(1, collector.GrantedCount);
            Assert.AreEqual(1, collector.AlreadyGrantedCount);
            Assert.AreEqual(1, progress.GrantedRewardCount);
        }

        [Test]
        public void MissingReward_IsIgnoredAsNormal()
        {
            (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector collector) = MakeRig();
            FakeEnemy enemy = Register(session, 1, null);

            Assert.DoesNotThrow(() => enemy.Kill());

            Assert.AreEqual(0, progress.Virtue);
            Assert.AreEqual(0, collector.GrantedCount);
            Assert.AreEqual(1, collector.NoRewardCount);
        }

        [Test]
        public void VirtueChanged_FiresWithCumulativeTotal()
        {
            (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector _) = MakeRig();
            RewardData melee = Reward("reward_enemy_melee", 10, grantOnce: false);
            FakeEnemy a = Register(session, 1, melee);
            FakeEnemy b = Register(session, 2, melee);

            var totals = new List<int>();
            progress.VirtueChanged += v => totals.Add(v);

            a.Kill();
            b.Kill();

            Assert.AreEqual(new[] { 10, 20 }, totals.ToArray());
        }

        [Test]
        public void UnboundProgress_DoesNotThrow()
        {
            CombatSessionController session = Make<CombatSessionController>("Session");
            CombatRewardCollector collector = Make<CombatRewardCollector>("RewardCollector");
            collector.Bind(session, null);
            InvokePrivate(collector, "OnEnable");

            FakeEnemy enemy = Register(session, 1, Reward("reward_enemy_melee", 10, grantOnce: false));

            Assert.DoesNotThrow(() => enemy.Kill(), "付与先未配線でも例外なく継続する（警告のみ）。");
            Assert.AreEqual(0, collector.GrantedCount);
        }

        [Test]
        public void Disabled_DoesNotGrant()
        {
            (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector collector) = MakeRig();
            FakeEnemy enemy = Register(session, 1, Reward("reward_enemy_melee", 10, grantOnce: false));

            InvokePrivate(collector, "OnDisable");
            enemy.Kill();

            Assert.AreEqual(0, progress.Virtue, "無効化中は購読を外しているため付与しない（対称管理）。");
            Assert.AreEqual(0, collector.GrantedCount);
        }

        [Test]
        public void ResubscribeAfterDisable_GrantsAgain()
        {
            (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector collector) = MakeRig();
            RewardData melee = Reward("reward_enemy_melee", 10, grantOnce: false);
            FakeEnemy a = Register(session, 1, melee);
            FakeEnemy b = Register(session, 2, melee);

            InvokePrivate(collector, "OnDisable");
            a.Kill();
            InvokePrivate(collector, "OnEnable");
            b.Kill();

            Assert.AreEqual(10, progress.Virtue);
            Assert.AreEqual(1, collector.GrantedCount);
        }

        [Test]
        public void ProgressHolder_ResetProgress_ClearsVirtue()
        {
            (CombatSessionController session, PlayerProgressHolder progress, CombatRewardCollector _) = MakeRig();
            FakeEnemy enemy = Register(session, 1, Reward("reward_enemy_melee", 10, grantOnce: false));
            enemy.Kill();
            Assert.AreEqual(10, progress.Virtue);

            // Retry は Scene 再読込で Holder ごと破棄されるが、明示リセットでも同じ初期状態へ戻せる。
            progress.ResetProgress();

            Assert.AreEqual(0, progress.Virtue);
            Assert.AreEqual(0, progress.GrantedRewardCount);
        }
    }
}
