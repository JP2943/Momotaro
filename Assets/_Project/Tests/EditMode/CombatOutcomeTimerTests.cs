using Momotaro.Gameplay.Scenes;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-08：勝敗結果の表示・Retry 受付の時間境界（<see cref="CombatOutcomeTimer"/>）を決定的に検証する（仕様書 §4.3/§9）。
    /// Retry 受付（0.50s）と結果パネル表示（0.75s）の直前／一致／直後、Enter/Reset の可否を確認する。
    /// </summary>
    public sealed class CombatOutcomeTimerTests
    {
        [Test]
        public void BeforeEnter_NothingArmed()
        {
            var t = new CombatOutcomeTimer(0.50f, 0.75f);
            Assert.IsFalse(t.Active);
            Assert.IsFalse(t.RetryArmed);
            Assert.IsFalse(t.ResultVisible);
            t.Tick(10f); // Enter 前は進まない。
            Assert.IsFalse(t.RetryArmed);
        }

        [Test]
        public void RetryArm_AtExactBoundary()
        {
            var t = new CombatOutcomeTimer(0.50f, 0.75f);
            t.Enter();
            t.Tick(0.49f);
            Assert.IsFalse(t.RetryArmed, "0.50s 直前は受付不可。");
            t.Tick(0.01f); // 累計 0.50s。
            Assert.IsTrue(t.RetryArmed, "0.50s 一致で受付可。");
            Assert.IsFalse(t.ResultVisible, "パネルはまだ非表示。");
        }

        [Test]
        public void PanelVisible_AtExactBoundary()
        {
            var t = new CombatOutcomeTimer(0.50f, 0.75f);
            t.Enter();
            t.Tick(0.74f);
            Assert.IsFalse(t.ResultVisible, "0.75s 直前は非表示。");
            Assert.IsTrue(t.RetryArmed, "0.50s は既に超過。");
            t.Tick(0.01f); // 累計 0.75s。
            Assert.IsTrue(t.ResultVisible, "0.75s 一致で表示。");
        }

        [Test]
        public void Reset_ClearsArming()
        {
            var t = new CombatOutcomeTimer(0.50f, 0.75f);
            t.Enter();
            t.Tick(1.0f);
            Assert.IsTrue(t.ResultVisible);
            t.Reset();
            Assert.IsFalse(t.Active);
            Assert.IsFalse(t.RetryArmed);
            Assert.IsFalse(t.ResultVisible);
        }

        [Test]
        public void Enter_RestartsFromZero()
        {
            var t = new CombatOutcomeTimer(0.50f, 0.75f);
            t.Enter();
            t.Tick(1.0f);
            t.Enter(); // 再突入で先頭から。
            Assert.IsFalse(t.RetryArmed);
            Assert.AreEqual(0f, t.Elapsed, 1e-6f);
        }
    }
}
