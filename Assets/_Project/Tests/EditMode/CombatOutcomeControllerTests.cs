using Momotaro.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-08：勝敗・リトライ統合（<see cref="CombatOutcomeController"/>）の結合検証。Session を直接駆動し、結果状態突入の検出、
    /// Retry 受付遅延（0.50s）・パネル表示遅延（0.75s）、受付前 Retry の無効、同フレーム二重 Retry の一度性を確認する。
    /// GameMode サービスは注入しない（<see cref="Momotaro.Gameplay.Modes.GameModeProvider.Current"/> 未設定でも安全に no-op）。
    /// </summary>
    public sealed class CombatOutcomeControllerTests
    {
        private sealed class FakeReloader : ICombatSceneReloader
        {
            public int Count;
            public bool ReloadCurrent()
            {
                Count++;
                return true;
            }
        }

        private GameObject _sessionGo;
        private GameObject _outcomeGo;

        private (CombatSessionController session, CombatOutcomeController outcome, FakeReloader reloader) Make()
        {
            _sessionGo = new GameObject("Session");
            var session = _sessionGo.AddComponent<CombatSessionController>();
            var reloader = new FakeReloader();
            session.SetReloader(reloader);

            _outcomeGo = new GameObject("Outcome");
            var outcome = _outcomeGo.AddComponent<CombatOutcomeController>();
            outcome.Bind(session, null);
            return (session, outcome, reloader);
        }

        [TearDown]
        public void TearDown()
        {
            if (_sessionGo != null) Object.DestroyImmediate(_sessionGo);
            if (_outcomeGo != null) Object.DestroyImmediate(_outcomeGo);
        }

        [Test]
        public void Victory_ArmsRetryThenPanel_AtBoundaries()
        {
            (CombatSessionController session, CombatOutcomeController outcome, _) = Make();
            session.StartWave();  // Preparing → Playing
            session.ToVictory();  // Playing → Victory

            outcome.Tick(0f);     // 結果突入を検出。
            Assert.IsTrue(outcome.IsResult);
            Assert.IsFalse(outcome.RetryArmed);
            Assert.IsFalse(outcome.ResultVisible);

            outcome.Tick(0.50f);
            Assert.IsTrue(outcome.RetryArmed, "0.50s で Retry 受付。");
            Assert.IsFalse(outcome.ResultVisible, "パネルはまだ非表示。");

            outcome.Tick(0.25f);
            Assert.IsTrue(outcome.ResultVisible, "0.75s でパネル表示。");
        }

        [Test]
        public void RequestRetry_BeforeArmed_DoesNothing()
        {
            (CombatSessionController session, CombatOutcomeController outcome, FakeReloader reloader) = Make();
            session.StartWave();
            session.ToDefeat(); // Playing → Defeat
            outcome.Tick(0f);

            outcome.RequestRetry(); // まだ 0.50s 未満。
            Assert.AreEqual(0, reloader.Count, "受付前は再読込しない。");
            Assert.AreEqual(CombatSessionState.Defeat, session.State);
        }

        [Test]
        public void RequestRetry_WhenArmed_ReloadsOnce_NoDoubleLoad()
        {
            (CombatSessionController session, CombatOutcomeController outcome, FakeReloader reloader) = Make();
            session.StartWave();
            session.ToDefeat();
            outcome.Tick(0f);
            outcome.Tick(0.60f); // 受付有効。

            outcome.RequestRetry();
            outcome.RequestRetry(); // 同フレーム二重入力。
            Assert.AreEqual(1, reloader.Count, "再読込は一度だけ（Session が Reloading で二重要求を拒否）。");
            Assert.AreEqual(CombatSessionState.Reloading, session.State);
            Assert.IsFalse(outcome.RetryArmed, "Reloading では結果状態でないため受付無効。");
        }

        [Test]
        public void LeavingResultState_ResetsArming()
        {
            (CombatSessionController session, CombatOutcomeController outcome, _) = Make();
            session.StartWave();
            session.ToVictory();
            outcome.Tick(0f);
            outcome.Tick(1.0f);
            Assert.IsTrue(outcome.ResultVisible);

            session.RequestReload(); // Victory → Reloading
            outcome.Tick(0f);
            Assert.IsFalse(outcome.IsResult);
            Assert.IsFalse(outcome.ResultVisible);
            Assert.IsFalse(outcome.RetryArmed);
        }
    }
}
