using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-03：<see cref="CombatSessionController"/> の型付き購読（Player/Enemy 死亡）、敵登録・生存数・重複拒否、0 体誤 Victory 防止、
    /// Scene 再読込の一回性、Disable での購読解除を検証する（仕様書 §5 / §11）。
    /// </summary>
    public sealed class CombatSessionControllerTests
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
            public void Kill()
            {
                Defeats.Publish(new EnemyDefeatedEvent(DamageableId,
                    new EnemyRewardRequest(DamageableId, EnemyRole.Melee, null, Vector3.zero)));
            }
        }

        private sealed class FakeReloader : ICombatSceneReloader
        {
            public int Calls;
            public bool ReloadCurrent() { Calls++; return true; }
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        [Test]
        public void PlayerDefeated_TransitionsToDefeat_Once()
        {
            var c = MakeController();
            var channel = new PlayerDefeatChannel();
            c.BindPlayerDefeat(channel);
            c.StartWave();

            int stateChanges = 0;
            c.StateChanged += _ => stateChanges++;

            channel.Publish(new PlayerDefeatedEvent(1, Vector3.zero));
            Assert.AreEqual(CombatSessionState.Defeat, c.State);
            Assert.AreEqual(1, stateChanges);

            channel.Publish(new PlayerDefeatedEvent(1, Vector3.zero)); // 二重
            Assert.AreEqual(CombatSessionState.Defeat, c.State);
            Assert.AreEqual(1, stateChanges, "重複 Defeat では再遷移しない。");
        }

        [Test]
        public void RegisterEnemy_TracksAliveAndRegisteredCounts()
        {
            var c = MakeController();
            c.RegisterEnemy(new FakeEnemy { DamageableId = 1 });
            c.RegisterEnemy(new FakeEnemy { DamageableId = 2 });
            c.RegisterEnemy(new FakeEnemy { DamageableId = 2 }); // 重複 ID は無視

            Assert.AreEqual(2, c.RegisteredEnemyCount);
            Assert.AreEqual(2, c.AliveEnemyCount);
        }

        [Test]
        public void EnemyDefeated_DecrementsAlive_Deduped_FiresAllClearedOnce()
        {
            var c = MakeController();
            var e1 = new FakeEnemy { DamageableId = 1 };
            var e2 = new FakeEnemy { DamageableId = 2 };
            c.RegisterEnemy(e1);
            c.RegisterEnemy(e2);

            int allCleared = 0;
            c.AllEnemiesDefeated += () => allCleared++;

            e1.Kill();
            Assert.AreEqual(1, c.AliveEnemyCount);
            e1.Kill(); // 重複通知
            Assert.AreEqual(1, c.AliveEnemyCount, "重複撃破通知は無視。");
            Assert.AreEqual(0, allCleared);

            e2.Kill();
            Assert.AreEqual(0, c.AliveEnemyCount);
            Assert.AreEqual(1, allCleared, "生存 0 到達で一度だけ通知。");
        }

        [Test]
        public void ZeroEnemies_NoAllClearedAndNoAutoVictory()
        {
            var c = MakeController();
            int allCleared = 0;
            c.AllEnemiesDefeated += () => allCleared++;

            c.StartWave(); // 敵 0 体で開始
            Assert.AreEqual(0, allCleared, "0 体では AllEnemiesDefeated は発火しない。");
            Assert.AreEqual(CombatSessionState.Playing, c.State, "0 体でも自動 Victory しない（誤 Victory 防止）。");
        }

        [Test]
        public void UnregisteredEnemyDefeat_Ignored()
        {
            var c = MakeController();
            c.RegisterEnemy(new FakeEnemy { DamageableId = 1 });
            var stranger = new FakeEnemy { DamageableId = 99 }; // 未登録

            stranger.Defeats.AddListener(c); // 直接購読しても未登録 ID は無視される想定
            stranger.Kill();

            Assert.AreEqual(1, c.AliveEnemyCount, "未登録敵の撃破は生存数に影響しない。");
        }

        [Test]
        public void UnregisterEnemy_AdjustsAlive()
        {
            var c = MakeController();
            var e1 = new FakeEnemy { DamageableId = 1 };
            c.RegisterEnemy(e1);
            Assert.AreEqual(1, c.AliveEnemyCount);

            c.UnregisterEnemy(e1);
            Assert.AreEqual(0, c.AliveEnemyCount);
            Assert.AreEqual(0, c.RegisteredEnemyCount);
            Assert.AreEqual(0, e1.Defeats.ListenerCount, "解除で購読も外れる。");
        }

        [Test]
        public void RequestReload_IsOneShot()
        {
            var c = MakeController();
            var reloader = new FakeReloader();
            c.SetReloader(reloader);
            c.StartWave();
            c.ToVictory();

            Assert.IsTrue(c.RequestReload());
            Assert.AreEqual(CombatSessionState.Reloading, c.State);
            Assert.AreEqual(1, reloader.Calls);

            Assert.IsFalse(c.RequestReload(), "二重要求は拒否。");
            Assert.AreEqual(1, reloader.Calls, "再読込は一度だけ発行。");
        }

        [Test]
        public void RequestReload_FromPlaying_Rejected()
        {
            var c = MakeController();
            var reloader = new FakeReloader();
            c.SetReloader(reloader);
            c.StartWave();

            Assert.IsFalse(c.RequestReload(), "Victory/Defeat 以外からは再読込しない。");
            Assert.AreEqual(0, reloader.Calls);
        }

        [Test]
        public void Disable_UnsubscribesPlayerAndEnemyChannels()
        {
            var c = MakeController();
            var player = new PlayerDefeatChannel();
            c.BindPlayerDefeat(player);
            var e1 = new FakeEnemy { DamageableId = 1 };
            c.RegisterEnemy(e1);

            Assert.AreEqual(1, player.ListenerCount);
            Assert.AreEqual(1, e1.Defeats.ListenerCount);

            InvokePrivate(c, "OnDisable");

            Assert.AreEqual(0, player.ListenerCount, "Disable で Player 購読を解除。");
            Assert.AreEqual(0, e1.Defeats.ListenerCount, "Disable で Enemy 購読を解除。");

            // 解除後に発火しても状態は変わらない（例外なし）。
            player.Publish(new PlayerDefeatedEvent(1, Vector3.zero));
            Assert.AreNotEqual(CombatSessionState.Defeat, c.State);
        }

        [Test]
        public void ClearEnemies_ResetsCountsAndSubscriptions()
        {
            var c = MakeController();
            var e1 = new FakeEnemy { DamageableId = 1 };
            var e2 = new FakeEnemy { DamageableId = 2 };
            c.RegisterEnemy(e1);
            c.RegisterEnemy(e2);

            c.ClearEnemies();

            Assert.AreEqual(0, c.RegisteredEnemyCount);
            Assert.AreEqual(0, c.AliveEnemyCount);
            Assert.AreEqual(0, e1.Defeats.ListenerCount);
            Assert.AreEqual(0, e2.Defeats.ListenerCount);
            Assert.DoesNotThrow(() => c.ClearEnemies(), "二重呼び出し安全。");
        }
    }
}
