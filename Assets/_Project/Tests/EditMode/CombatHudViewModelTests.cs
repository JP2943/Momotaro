using System;
using System.Reflection;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Vitals;
using Momotaro.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-04：<see cref="CombatHudViewModel"/> の遅延 Bind／破棄／再購読、値 0／最大、GuardBreak／Special Ready、
    /// Session 状態の型付き購読、Scene 再読込後の購読重複・残留なしを決定的に検証する（仕様書 §6）。
    /// </summary>
    public sealed class CombatHudViewModelTests
    {
        private GameObject _sessionGo;

        [TearDown]
        public void TearDown()
        {
            if (_sessionGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_sessionGo);
                _sessionGo = null;
            }
        }

        private CombatSessionController NewSession()
        {
            _sessionGo = new GameObject("Session");
            return _sessionGo.AddComponent<CombatSessionController>();
        }

        // Vital.Changed（field-like event）の購読者数を反射で数え、購読の重複・残留を検証する。
        private static int SubscriberCount(Vital v)
        {
            FieldInfo f = typeof(Vital).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance);
            var d = f.GetValue(v) as Delegate;
            return d?.GetInvocationList().Length ?? 0;
        }

        [Test]
        public void Unbound_HasSafeDefaults()
        {
            var vm = new CombatHudViewModel();
            Assert.IsFalse(vm.HasPlayer);
            Assert.IsFalse(vm.HasSession);
            Assert.AreEqual(0, vm.HpCurrent);
            Assert.AreEqual(0, vm.HpMax);
            Assert.AreEqual(CombatSessionState.Preparing, vm.Phase);
            Assert.AreEqual(1, vm.Wave);
        }

        [Test]
        public void BindPlayer_LateBind_ReflectsVitalsImmediately()
        {
            var vm = new CombatHudViewModel();
            var hp = new Vital(100);
            var st = new Vital(80, 40);

            vm.BindPlayer(hp, st, () => false, () => false, () => false);

            Assert.IsTrue(vm.HasPlayer);
            Assert.AreEqual(100, vm.HpCurrent);
            Assert.AreEqual(100, vm.HpMax);
            Assert.AreEqual(1f, vm.HpRatio, 1e-4f);
            Assert.AreEqual(40, vm.StaminaCurrent);
            Assert.AreEqual(80, vm.StaminaMax);
            Assert.AreEqual(0.5f, vm.StaminaRatio, 1e-4f);
        }

        [Test]
        public void HealthChange_ZeroAndMax_FiresChangedAndUpdatesRatio()
        {
            var vm = new CombatHudViewModel();
            var hp = new Vital(100);
            vm.BindPlayer(hp, new Vital(50), () => false, () => false, () => false);

            int changed = 0;
            vm.Changed += () => changed++;

            hp.SetCurrent(0);
            Assert.AreEqual(0, vm.HpCurrent);
            Assert.AreEqual(0f, vm.HpRatio, 1e-4f);
            Assert.AreEqual(1, changed, "HP 変化で一度発火。");

            hp.SetCurrent(100);
            Assert.AreEqual(100, vm.HpCurrent);
            Assert.AreEqual(1f, vm.HpRatio, 1e-4f);
            Assert.AreEqual(2, changed);
        }

        [Test]
        public void GuardBreakAndSpecial_ArePolledByTick()
        {
            var vm = new CombatHudViewModel();
            bool[] guard = { false };
            bool[] ready = { false };
            bool[] charging = { false };
            vm.BindPlayer(new Vital(10), new Vital(10), () => guard[0], () => ready[0], () => charging[0]);

            Assert.IsFalse(vm.GuardBroken);
            Assert.IsFalse(vm.SpecialReady);

            guard[0] = true;
            charging[0] = true;
            vm.Tick();
            Assert.IsTrue(vm.GuardBroken, "GuardBreak は Tick のポーリングで反映。");
            Assert.IsTrue(vm.SpecialCharging);
            Assert.IsFalse(vm.SpecialReady);

            charging[0] = false;
            ready[0] = true;
            vm.Tick();
            Assert.IsTrue(vm.SpecialReady, "Special Ready を反映。");
            Assert.IsFalse(vm.SpecialCharging);
        }

        [Test]
        public void SetWave_UpdatesAndFires()
        {
            var vm = new CombatHudViewModel();
            int changed = 0;
            vm.Changed += () => changed++;

            vm.SetWave(3);
            Assert.AreEqual(3, vm.Wave);
            Assert.AreEqual(1, changed);

            vm.SetWave(0); // 1 未満は 1 に丸め。
            Assert.AreEqual(1, vm.Wave);
        }

        [Test]
        public void Session_TypedSubscription_TracksPhase()
        {
            var vm = new CombatHudViewModel();
            CombatSessionController s = NewSession();
            vm.BindSession(s);
            Assert.IsTrue(vm.HasSession);
            Assert.AreEqual(CombatSessionState.Preparing, vm.Phase);

            int changed = 0;
            vm.Changed += () => changed++;

            s.StartWave();
            Assert.AreEqual(CombatSessionState.Playing, vm.Phase);
            Assert.AreEqual(1, changed);

            s.ToVictory();
            Assert.AreEqual(CombatSessionState.Victory, vm.Phase);
            Assert.AreEqual(2, changed);
        }

        [Test]
        public void UnbindPlayer_StopsUpdates_NoLeftoverSubscription()
        {
            var vm = new CombatHudViewModel();
            var hp = new Vital(100);
            vm.BindPlayer(hp, new Vital(10), () => false, () => false, () => false);
            Assert.AreEqual(1, SubscriberCount(hp), "Bind で購読は 1 件。");

            vm.UnbindPlayer();
            Assert.AreEqual(0, SubscriberCount(hp), "Unbind で購読が外れる（残留なし）。");

            int changed = 0;
            vm.Changed += () => changed++;
            hp.SetCurrent(0); // 破棄済み供給元の変化。
            Assert.AreEqual(0, changed, "破棄後は旧供給元の変化で発火しない。");
        }

        [Test]
        public void Rebind_DoesNotDuplicateSubscription()
        {
            var vm = new CombatHudViewModel();
            var hp = new Vital(100);
            var st = new Vital(10);

            vm.BindPlayer(hp, st, () => false, () => false, () => false);
            vm.UnbindPlayer();
            vm.BindPlayer(hp, st, () => false, () => false, () => false); // 再読込後の再 Bind 相当。

            Assert.AreEqual(1, SubscriberCount(hp), "再 Bind でも購読は 1 件（重複しない）。");
            Assert.AreEqual(1, SubscriberCount(st));
        }

        [Test]
        public void SameReferenceRebind_KeepsSingleSubscription()
        {
            var vm = new CombatHudViewModel();
            var hp = new Vital(100);
            var st = new Vital(10);

            vm.BindPlayer(hp, st, () => false, () => false, () => false);
            vm.BindPlayer(hp, st, () => true, () => true, () => false); // 同一供給元の再 Bind（デリゲートのみ更新）。

            Assert.AreEqual(1, SubscriberCount(hp), "同一参照の再 Bind で購読は重複しない。");
            vm.Tick();
            Assert.IsTrue(vm.GuardBroken, "デリゲートは更新される。");
        }

        [Test]
        public void UnbindSession_NoLeftoverSubscription()
        {
            var vm = new CombatHudViewModel();
            CombatSessionController s = NewSession();
            vm.BindSession(s);
            vm.UnbindSession();

            int changed = 0;
            vm.Changed += () => changed++;
            s.StartWave(); // 破棄済み Session の状態変化。
            Assert.AreEqual(0, changed, "Session 解除後は状態変化で発火しない（購読残留なし）。");
        }

        [Test]
        public void Dispose_RemovesAllSubscriptions()
        {
            var vm = new CombatHudViewModel();
            var hp = new Vital(100);
            CombatSessionController s = NewSession();
            vm.BindPlayer(hp, new Vital(10), () => false, () => false, () => false);
            vm.BindSession(s);

            vm.Dispose();

            Assert.AreEqual(0, SubscriberCount(hp), "Dispose で Player 購読解除。");
            Assert.IsFalse(vm.HasPlayer);
            Assert.IsFalse(vm.HasSession);
            Assert.DoesNotThrow(() => s.StartWave(), "Dispose 後の Session 変化は安全。");
        }
    }
}
