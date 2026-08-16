using System.Collections.Generic;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-03：<see cref="CombatSessionMachine"/> の全遷移・不正遷移・重複遷移を決定的に検証する（仕様書 §5 / Table4）。
    /// </summary>
    public sealed class CombatSessionMachineTests
    {
        [Test]
        public void StartsPreparing()
        {
            Assert.AreEqual(CombatSessionState.Preparing, new CombatSessionMachine().Current);
        }

        [Test]
        public void ValidVictoryPath_Applies()
        {
            var m = new CombatSessionMachine();
            Assert.IsTrue(m.StartWave());       // Preparing → Playing
            Assert.AreEqual(CombatSessionState.Playing, m.Current);
            Assert.IsTrue(m.ToIntermission());  // Playing → Intermission
            Assert.IsTrue(m.StartWave());       // Intermission → Playing
            Assert.IsTrue(m.ToVictory());       // Playing → Victory
            Assert.IsTrue(m.ToReloading());     // Victory → Reloading
            Assert.AreEqual(CombatSessionState.Reloading, m.Current);
        }

        [Test]
        public void ValidDefeatPath_Applies()
        {
            var m = new CombatSessionMachine();
            m.StartWave();
            Assert.IsTrue(m.ToDefeat());        // Playing → Defeat
            Assert.IsTrue(m.ToReloading());     // Defeat → Reloading
            Assert.AreEqual(CombatSessionState.Reloading, m.Current);
        }

        [Test]
        public void DefeatFromIntermission_Applies()
        {
            var m = new CombatSessionMachine();
            m.StartWave();
            m.ToIntermission();
            Assert.IsTrue(m.ToDefeat(), "Intermission 中の被弾（残弾等）でも Defeat 可。");
        }

        [Test]
        public void IllegalTransitionsFromPreparing_Rejected()
        {
            var m = new CombatSessionMachine();
            Assert.IsFalse(m.ToIntermission());
            Assert.IsFalse(m.ToVictory());
            Assert.IsFalse(m.ToDefeat());
            Assert.IsFalse(m.ToReloading());
            Assert.AreEqual(CombatSessionState.Preparing, m.Current, "不正遷移では状態が変わらない。");
        }

        [Test]
        public void StartWaveFromPlaying_Rejected()
        {
            var m = new CombatSessionMachine();
            m.StartWave();
            Assert.IsFalse(m.StartWave(), "Playing から StartWave は不可。");
        }

        [Test]
        public void DuplicateVictory_Rejected()
        {
            var m = new CombatSessionMachine();
            m.StartWave();
            Assert.IsTrue(m.ToVictory());
            Assert.IsFalse(m.ToVictory(), "重複 Victory は拒否。");
        }

        [Test]
        public void DuplicateDefeat_Rejected()
        {
            var m = new CombatSessionMachine();
            m.StartWave();
            Assert.IsTrue(m.ToDefeat());
            Assert.IsFalse(m.ToDefeat(), "重複 Defeat は拒否。");
        }

        [Test]
        public void DoubleReloading_Rejected()
        {
            var m = new CombatSessionMachine();
            m.StartWave();
            m.ToDefeat();
            Assert.IsTrue(m.ToReloading());
            Assert.IsFalse(m.ToReloading(), "Reloading 中の再要求は拒否（二重要求防止）。");
        }

        [Test]
        public void VictoryRequiresPlaying_NotFromIntermission()
        {
            var m = new CombatSessionMachine();
            m.StartWave();
            m.ToIntermission();
            Assert.IsFalse(m.ToVictory(), "Intermission から直接 Victory は不可（Playing 経由）。");
        }

        [Test]
        public void StateChanged_FiresOnValidOnly()
        {
            var m = new CombatSessionMachine();
            var seen = new List<CombatSessionState>();
            m.StateChanged += seen.Add;

            m.StartWave();      // fires Playing
            m.ToVictory();      // fires Victory
            m.ToVictory();      // rejected, no fire

            Assert.AreEqual(2, seen.Count);
            Assert.AreEqual(CombatSessionState.Playing, seen[0]);
            Assert.AreEqual(CombatSessionState.Victory, seen[1]);
        }

        [Test]
        public void CanEnter_MatchesTransitionValidity()
        {
            var m = new CombatSessionMachine();
            Assert.IsTrue(m.CanEnter(CombatSessionState.Playing));
            Assert.IsFalse(m.CanEnter(CombatSessionState.Victory));
            m.StartWave();
            Assert.IsTrue(m.CanEnter(CombatSessionState.Victory));
            Assert.IsTrue(m.CanEnter(CombatSessionState.Defeat));
            Assert.IsFalse(m.CanEnter(CombatSessionState.Reloading));
        }
    }
}
