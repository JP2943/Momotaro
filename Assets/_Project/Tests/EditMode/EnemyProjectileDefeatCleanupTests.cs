using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-02 受入修正：プレイヤー死亡前に発射され Scene に残る敵 Projectile を、死亡通知（<see cref="PlayerDefeatChannel"/>）で
    /// 一括 Cleanup する（<see cref="EnemyProjectileRegistry"/> ＋ <see cref="EnemyProjectileDefeatCleanup"/>）。複数弾の掃除、
    /// 二重 Cleanup の安全性、破棄取りこぼしのレジストリ解除、実 PlayerVitalsHolder の死亡チャネル連携を検証する。
    /// </summary>
    public sealed class EnemyProjectileDefeatCleanupTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => EnemyProjectileRegistry.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
            EnemyProjectileRegistry.Clear();
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

        private EnemyProjectile MakeProjectile(int id = 1)
        {
            var go = new GameObject("Projectile");
            _spawned.Add(go);
            var p = go.AddComponent<EnemyProjectile>();
            p.Initialize(default(EnemyAttackSnapshot), Vector3.zero, Vector3.forward, null, 10f, HitId.Single(id));
            return p;
        }

        private EnemyProjectileDefeatCleanup MakeBridge(PlayerDefeatChannel channel)
        {
            var go = new GameObject("ProjectileCleanup");
            _spawned.Add(go);
            var bridge = go.AddComponent<EnemyProjectileDefeatCleanup>();
            bridge.Bind(channel); // EditMode では OnEnable が呼ばれないため明示注入。
            return bridge;
        }

        [Test]
        public void Initialize_RegistersLiveProjectile()
        {
            var p = MakeProjectile();
            Assert.IsTrue(p.IsLive);
            Assert.AreEqual(1, EnemyProjectileRegistry.LiveCount, "発射で生存レジストリへ登録される。");
        }

        [Test]
        public void PlayerDefeated_DespawnsSingleProjectile()
        {
            var p = MakeProjectile();
            var channel = new PlayerDefeatChannel();
            MakeBridge(channel);

            channel.Publish(new PlayerDefeatedEvent(1, Vector3.zero));

            Assert.IsFalse(p.IsLive, "死亡通知で Projectile が消滅。");
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount, "レジストリから外れる。");
        }

        [Test]
        public void PlayerDefeated_DespawnsAllProjectiles()
        {
            var a = MakeProjectile(1);
            var b = MakeProjectile(2);
            var d = MakeProjectile(3);
            Assert.AreEqual(3, EnemyProjectileRegistry.LiveCount);

            var channel = new PlayerDefeatChannel();
            MakeBridge(channel);
            channel.Publish(new PlayerDefeatedEvent(1, Vector3.zero));

            Assert.IsFalse(a.IsLive);
            Assert.IsFalse(b.IsLive);
            Assert.IsFalse(d.IsLive);
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount, "複数弾すべて Cleanup。");
        }

        [Test]
        public void DoubleCleanup_NoExceptionNoDoubleFree()
        {
            var p = MakeProjectile();

            Assert.DoesNotThrow(() => EnemyProjectileRegistry.DespawnAll());
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount);
            Assert.DoesNotThrow(() => EnemyProjectileRegistry.DespawnAll(), "空集合の再 Cleanup は安全。");
            Assert.DoesNotThrow(() => p.Cleanup(), "個別の二重 Cleanup も安全（冪等）。");
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount);
        }

        [Test]
        public void DoubleDefeatNotification_IsSafe()
        {
            MakeProjectile();
            var channel = new PlayerDefeatChannel();
            MakeBridge(channel);

            Assert.DoesNotThrow(() =>
            {
                channel.Publish(new PlayerDefeatedEvent(1, Vector3.zero));
                channel.Publish(new PlayerDefeatedEvent(1, Vector3.zero)); // 二重通知でも安全。
            });
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount);
        }

        [Test]
        public void DestroyedProjectile_UnregistersFromRegistry()
        {
            var p = MakeProjectile();
            Assert.AreEqual(1, EnemyProjectileRegistry.LiveCount);

            Object.DestroyImmediate(p.gameObject); // 破棄取りこぼしの Backstop（OnDestroy で解除）。

            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount, "破棄でレジストリから外れ、参照を残さない。");
        }

        [Test]
        public void RealPlayerDefeatChannel_TriggersCleanup()
        {
            // 実 PlayerVitalsHolder の致死 → PlayerDefeatChannel 発火 → Projectile 一括 Cleanup（IPlayerDefeatSource 経由の連携）。
            var playerGo = new GameObject("Player");
            _spawned.Add(playerGo);
            var holder = playerGo.AddComponent<PlayerVitalsHolder>();
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetField(data, "_maxHp", 10);
            SetField(data, "_defense", 0f);
            SetField(data, "_maxStamina", 100);
            SetField(holder, "_data", data);

            var source = (IPlayerDefeatSource)holder;
            MakeBridge(source.Defeats);
            var p = MakeProjectile();

            holder.ReceiveHit(new HitInfo(null, holder, -Vector3.forward, Vector3.zero,
                new HitDamage(1000f, 0f, 0f), true, true, HitId.Single(9))); // 致死

            Assert.IsTrue(holder.IsDefeated);
            Assert.IsFalse(p.IsLive, "実プレイヤー死亡で残存 Projectile が消滅。");
            Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount);
        }
    }
}
