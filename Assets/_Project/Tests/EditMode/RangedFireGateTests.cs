using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Enemy.Screen;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-08：遠距離射撃の開始ゲートと発射を検証（§9.2）。射線に味方の敵がいれば発射しない、画面外は画面端警告を出せた時だけ発射、
    /// Active 突入で Projectile を 1 発生成（近接 Hitbox は出さない）。物理・Camera・Presentation は Fake 注入で決定的に確認する。
    /// </summary>
    public sealed class RangedFireGateTests
    {
        private readonly List<Object> _spawned = new List<Object>();
        private IScreenBoundsProbe _prevScreen;
        private IOffscreenWarningService _prevWarn;

        [SetUp]
        public void SetUp()
        {
            _prevScreen = ScreenBoundsProvider.Current;
            _prevWarn = OffscreenWarningProvider.Current;
        }

        [TearDown]
        public void TearDown()
        {
            ScreenBoundsProvider.Current = _prevScreen;
            OffscreenWarningProvider.Current = _prevWarn;
            foreach (Object o in _spawned) { if (o != null) Object.DestroyImmediate(o); }
            _spawned.Clear();
        }

        private static void SetField(object t, string n, object v)
        {
            System.Type ty = t.GetType(); FieldInfo f = null;
            while (ty != null && f == null) { f = ty.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); ty = ty.BaseType; }
            Assert.IsNotNull(f, "field not found: " + n); f.SetValue(t, v);
        }

        private sealed class FakeScreen : IScreenBoundsProbe { public bool OnScreen = true; public bool IsOnScreen(Vector3 p) => OnScreen; }
        private sealed class FakeWarn : IOffscreenWarningService { public bool Avail; public int Calls; public bool TryShowWarning(Vector3 a, Vector3 b) { Calls++; return Avail; } }
        private sealed class FakeLine : IEnemyFireLineProbe { public bool Blocked; public bool AllyBlocksLine(Vector3 a, Vector3 b, int id) => Blocked; }
        private sealed class FakeLauncher : IEnemyProjectileLauncher
        {
            public int Count; public HitId LastHitId;
            public bool TryLaunch(in EnemyAttackSnapshot s, Vector3 o, Vector3 d, ICombatActor owner, float ap, HitId hid)
            { Count++; LastHitId = hid; return true; }
        }

        private EnemyAttackController MakeRanged(out FakeLine line, out FakeLauncher launcher)
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_attackClass", EnemyAttackClass.Projectile);
            SetField(d, "_useRange", 9f);
            SetField(d, "_useAngle", 30f);
            SetField(d, "_cooldownSeconds", 0f);
            SetField(d, "_prepareSeconds", 0.3f);
            SetField(d, "_activeSeconds", 0.05f);
            SetField(d, "_recoverySeconds", 0.6f);
            SetField(d, "_trackingStopSeconds", 0.2f);
            SetField(d, "_slotKind", AttackSlotKind.Ranged);
            SetField(d, "_aimingMode", EnemyAimingMode.CurrentPosition);
            SetField(d, "_projectileSpeed", 10f);
            SetField(d, "_projectileMaxDistance", 15f);
            SetField(d, "_projectileLifetimeSeconds", 3f);
            SetField(d, "_requiresOffscreenWarning", true);

            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 40);
            SetField(arch, "_attackPower", 30f);
            SetField(arch, "_attacks", new[] { d });

            var go = new GameObject("Ranged");
            _spawned.Add(go);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            var c = go.AddComponent<EnemyAttackController>();
            line = new FakeLine();
            launcher = new FakeLauncher();
            c.SetFireLineProbe(line);
            c.SetProjectileLauncher(launcher);
            return c;
        }

        private static readonly Vector3 Target = new Vector3(0, 0, 5f);

        [Test]
        public void OnScreen_LineClear_FiresAndLaunchesOneProjectileAtActive()
        {
            ScreenBoundsProvider.Current = new FakeScreen { OnScreen = true };
            var c = MakeRanged(out _, out FakeLauncher launcher);

            Assert.IsTrue(c.TryStartAttack(Target, Vector3.zero), "画面内・射線クリアなら開始。");
            Assert.AreEqual(0, launcher.Count, "Prepare 中は未発射。");
            c.TickAttack(0.31f); // Active 突入
            Assert.AreEqual(1, launcher.Count, "Active 突入で 1 発生成。");

            for (int i = 0; i < 40 && c.IsAttacking; i++) c.TickAttack(0.05f);
            Assert.AreEqual(1, launcher.Count, "1 攻撃で 1 発のみ（1 発 1 生成）。");
        }

        [Test]
        public void LineBlockedByAlly_DoesNotFire()
        {
            ScreenBoundsProvider.Current = new FakeScreen { OnScreen = true };
            var c = MakeRanged(out FakeLine line, out _);
            line.Blocked = true;
            Assert.IsFalse(c.TryStartAttack(Target, Vector3.zero), "射線に味方の敵がいると発射しない（Reposition へ）。");
            Assert.IsFalse(c.IsAttacking);
        }

        [Test]
        public void Offscreen_NoWarningService_DoesNotFire()
        {
            ScreenBoundsProvider.Current = new FakeScreen { OnScreen = false };
            OffscreenWarningProvider.Current = null; // 警告不能
            var c = MakeRanged(out _, out _);
            Assert.IsFalse(c.TryStartAttack(Target, Vector3.zero), "画面外で警告を出せなければ射撃候補から除外。");
        }

        [Test]
        public void Offscreen_WarningAvailable_FiresAfterWarning()
        {
            ScreenBoundsProvider.Current = new FakeScreen { OnScreen = false };
            var warn = new FakeWarn { Avail = true };
            OffscreenWarningProvider.Current = warn;
            var c = MakeRanged(out _, out _);
            Assert.IsTrue(c.TryStartAttack(Target, Vector3.zero), "画面端警告を出せれば画面外でも開始。");
            Assert.GreaterOrEqual(warn.Calls, 1, "射撃前に警告を要求する（警告先行）。");
        }

        [Test]
        public void Offscreen_WarningUnavailable_DoesNotFire()
        {
            ScreenBoundsProvider.Current = new FakeScreen { OnScreen = false };
            OffscreenWarningProvider.Current = new FakeWarn { Avail = false };
            var c = MakeRanged(out _, out _);
            Assert.IsFalse(c.TryStartAttack(Target, Vector3.zero), "警告を出せない（Avail=false）なら開始しない。");
        }
    }
}
