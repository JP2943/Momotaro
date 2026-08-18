using System.Collections.Generic;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05B：<see cref="HitStopController"/> が命中要求に応じて Time.timeScale を一時停止し、unscaled 時間の満了で復帰することを検証する。
    /// 多重要求で長い方採用、上限丸め、Pause(timeScale0) 中の要求無視、CancelImmediately/Disable 相当での確実な復帰を確認する。
    /// </summary>
    public sealed class HitStopControllerTests
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
            Time.timeScale = 1f; // グローバル状態を必ず戻す。
        }

        private HitStopController New()
        {
            Time.timeScale = 1f;
            var go = new GameObject("HitStop");
            _spawned.Add(go);
            return go.AddComponent<HitStopController>();
        }

        [Test]
        public void Request_FreezesTimeScale_UntilElapsed_ThenRestores()
        {
            HitStopController h = New();
            h.Request(0.05f);
            Assert.IsTrue(h.IsStopping, "要求でヒットストップ開始。");
            Assert.AreEqual(0f, Time.timeScale, "停止中は timeScale=0。");

            h.Tick(0.03f);
            Assert.IsTrue(h.IsStopping, "満了前は継続。");

            h.Tick(0.03f); // 累計0.06 > 0.05 → 満了。
            Assert.IsFalse(h.IsStopping, "満了で解除。");
            Assert.AreEqual(1f, Time.timeScale, "元の timeScale(1)へ復帰。");
        }

        [Test]
        public void MultipleRequests_TakeLonger()
        {
            HitStopController h = New();
            h.Request(0.05f);
            h.Request(0.09f); // 長い方を採用。
            Assert.GreaterOrEqual(h.Remaining, 0.089f, "多重要求は長い方を採用。");
        }

        [Test]
        public void Request_ClampedToMax()
        {
            HitStopController h = New();
            h.Request(10f); // 上限(0.25)へ丸め。
            Assert.LessOrEqual(h.Remaining, 0.25f + 1e-4f, "上限で丸める。");
            Assert.Greater(h.Remaining, 0f);
        }

        [Test]
        public void Request_WhilePaused_IsIgnored()
        {
            HitStopController h = New();
            Time.timeScale = 0f; // Pause 相当。
            h.Request(0.05f);
            Assert.IsFalse(h.IsStopping, "Pause 中は掛けない（復帰スケールを誤らない）。");
            Assert.AreEqual(0f, Time.timeScale);
        }

        [Test]
        public void CancelImmediately_RestoresTimeScale()
        {
            HitStopController h = New();
            h.Request(0.2f);
            Assert.AreEqual(0f, Time.timeScale);

            h.CancelImmediately();
            Assert.IsFalse(h.IsStopping);
            Assert.AreEqual(1f, Time.timeScale, "即時解除で復帰。");
        }

        [Test]
        public void ZeroOrNegative_Request_NoEffect()
        {
            HitStopController h = New();
            h.Request(0f);
            h.Request(-1f);
            Assert.IsFalse(h.IsStopping, "0以下の要求は無処理。");
            Assert.AreEqual(1f, Time.timeScale);
        }
    }
}
