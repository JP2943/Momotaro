using System.Collections.Generic;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-07：連続ウェーブ進行の純粋モデル <see cref="WaveSequencer"/> を決定的に検証する（仕様書 §8.2/§8.3）。
    /// 全滅検出 → 休止入り(1.0s) → Intermission(3.0s) → 次 Wave の時間境界（直前／一致／直後）、最終 Wave 完了の一度性、
    /// 別段階・遅延の全滅通知が後続 Wave を進めないことを確認する。時間依存は yield ではなく明示 Tick で駆動する。
    /// </summary>
    public sealed class WaveSequencerTests
    {
        private static WaveSequencer New(int waves, out List<int> engaged, out List<int> intermissions, out List<int> cleared)
        {
            var seq = new WaveSequencer(waves, 1.0f, 3.0f);
            var e = new List<int>();
            var it = new List<int>();
            var c = new List<int>();
            seq.WaveEngaged += n => e.Add(n);
            seq.IntermissionEntered += () => it.Add(1);
            seq.AllWavesCleared += () => c.Add(1);
            engaged = e;
            intermissions = it;
            cleared = c;
            return seq;
        }

        [Test]
        public void Initial_State_IsNotStarted()
        {
            var seq = New(4, out _, out _, out _);
            Assert.AreEqual(WaveSequencer.Phase.NotStarted, seq.Current);
            Assert.AreEqual(0, seq.CurrentWaveNumber);
            Assert.AreEqual(4, seq.WaveCount);
            Assert.IsFalse(seq.IsComplete);
        }

        [Test]
        public void Begin_EngagesFirstWave()
        {
            var seq = New(4, out List<int> engaged, out _, out _);
            seq.Begin();
            Assert.AreEqual(WaveSequencer.Phase.Fighting, seq.Current);
            Assert.AreEqual(1, seq.CurrentWaveNumber);
            CollectionAssert.AreEqual(new[] { 1 }, engaged);
        }

        [Test]
        public void Begin_Twice_IsIgnored()
        {
            var seq = New(4, out List<int> engaged, out _, out _);
            seq.Begin();
            seq.Begin();
            CollectionAssert.AreEqual(new[] { 1 }, engaged, "2 回目の Begin は無視される。");
        }

        [Test]
        public void ClearThenDelay_EntersIntermission_AtExactBoundary()
        {
            var seq = New(4, out _, out List<int> intermissions, out _);
            seq.Begin();
            seq.NotifyWaveCleared();
            Assert.AreEqual(WaveSequencer.Phase.PostClear, seq.Current);

            seq.Tick(0.9f);
            Assert.AreEqual(WaveSequencer.Phase.PostClear, seq.Current, "1.0s 直前は休止入りしない。");
            Assert.AreEqual(0, intermissions.Count);

            seq.Tick(0.1f); // 累計 1.0s ちょうど。
            Assert.AreEqual(WaveSequencer.Phase.Intermission, seq.Current, "1.0s 一致で Intermission。");
            Assert.AreEqual(1, intermissions.Count);
        }

        [Test]
        public void Intermission_AdvancesToNextWave_AtExactBoundary()
        {
            var seq = New(4, out List<int> engaged, out _, out _);
            seq.Begin();
            seq.NotifyWaveCleared();
            seq.Tick(1.0f); // → Intermission

            seq.Tick(2.9f);
            Assert.AreEqual(WaveSequencer.Phase.Intermission, seq.Current, "3.0s 直前は次 Wave へ進まない。");
            CollectionAssert.AreEqual(new[] { 1 }, engaged);

            seq.Tick(0.1f); // 累計 3.0s ちょうど。
            Assert.AreEqual(WaveSequencer.Phase.Fighting, seq.Current);
            Assert.AreEqual(2, seq.CurrentWaveNumber);
            CollectionAssert.AreEqual(new[] { 1, 2 }, engaged);
        }

        [Test]
        public void FinalWave_Clear_CompletesWithoutIntermission()
        {
            var seq = New(2, out List<int> engaged, out List<int> intermissions, out List<int> cleared);
            seq.Begin();               // Wave1
            seq.NotifyWaveCleared();
            seq.Tick(1.0f);            // → Intermission
            seq.Tick(3.0f);            // → Wave2 (final)
            Assert.AreEqual(2, seq.CurrentWaveNumber);

            seq.NotifyWaveCleared();   // final cleared
            seq.Tick(1.0f);            // → Complete
            Assert.AreEqual(WaveSequencer.Phase.Complete, seq.Current);
            Assert.IsTrue(seq.IsComplete);
            Assert.AreEqual(1, cleared.Count, "最終完了は一度だけ発火。");
            Assert.AreEqual(1, intermissions.Count, "最終 Wave 後に Intermission へは入らない。");
            CollectionAssert.AreEqual(new[] { 1, 2 }, engaged);
        }

        [Test]
        public void NotifyWaveCleared_OutsideFighting_IsIgnored()
        {
            var seq = New(4, out _, out List<int> intermissions, out _);
            seq.Begin();
            seq.NotifyWaveCleared();       // Fighting → PostClear
            seq.NotifyWaveCleared();       // PostClear 中：無視
            seq.Tick(0.5f);
            seq.NotifyWaveCleared();       // まだ PostClear：無視（後続を早送りしない）
            Assert.AreEqual(WaveSequencer.Phase.PostClear, seq.Current);
            seq.Tick(0.5f);                // 1.0s 到達 → Intermission
            Assert.AreEqual(WaveSequencer.Phase.Intermission, seq.Current);

            seq.NotifyWaveCleared();       // Intermission 中：無視
            seq.Tick(2.9f);
            Assert.AreEqual(WaveSequencer.Phase.Intermission, seq.Current, "別段階の全滅通知は次 Wave を進めない。");
            Assert.AreEqual(1, intermissions.Count);
        }

        [Test]
        public void ZeroWaves_Begin_CompletesImmediately()
        {
            var seq = New(0, out List<int> engaged, out _, out List<int> cleared);
            seq.Begin();
            Assert.AreEqual(WaveSequencer.Phase.Complete, seq.Current);
            Assert.AreEqual(1, cleared.Count);
            CollectionAssert.IsEmpty(engaged);
        }

        [Test]
        public void NonPositiveDelta_IsIgnored()
        {
            var seq = New(4, out _, out _, out _);
            seq.Begin();
            seq.NotifyWaveCleared();
            seq.Tick(0f);
            seq.Tick(-5f);
            Assert.AreEqual(WaveSequencer.Phase.PostClear, seq.Current, "0/負の dt では時間が進まない。");
        }
    }
}
