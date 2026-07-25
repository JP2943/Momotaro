using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-08：ジャストガードの受付状態機械（<see cref="JustGuardState"/>）を検証する。押下窓 0.15、解除後窓 0.075、連打ペナルティ 0.20、
    /// 連続成功猶予、多段は一段ごとに新押下、通常ガード中の再押下では窓更新しない、という規則を確認する。
    /// </summary>
    public sealed class JustGuardStateTests
    {
        private static JustGuardState Make() => new JustGuardState(); // 0.15 / 0.075 / 0.20

        [Test]
        public void Press_OpensReceiveWindow()
        {
            var js = Make();
            js.Press();
            Assert.IsTrue(js.CanJustGuard, "押下で受付窓が開く。");
            Assert.AreEqual(JustGuardPhase.Normal, js.Phase);
        }

        [Test]
        public void PressWindow_ClosesAfter0_15()
        {
            var js = Make();
            js.Press();
            js.Tick(0.14f);
            Assert.IsTrue(js.CanJustGuard, "0.14 秒時点では受付中。");
            js.Tick(0.02f); // 累計 0.16 > 0.15
            Assert.IsFalse(js.CanJustGuard, "0.15 秒を過ぎると受付終了（境界）。");
        }

        [Test]
        public void HoldBeyondWindow_NoJustGuard_ButNormalGuardContinues()
        {
            var js = Make();
            js.Press();
            js.Tick(0.20f); // 押しっぱなしで窓超過
            Assert.IsFalse(js.CanJustGuard, "窓を過ぎれば JG 不可（通常ガードへ移行）。");
        }

        [Test]
        public void Release_OpensReleaseWindow_AndEntersPenalty()
        {
            var js = Make();
            js.Press();
            js.Tick(0.20f);     // 押下窓は消化
            js.Release();
            Assert.IsTrue(js.CanJustGuard, "解除直後にも受付窓を残す。");
            Assert.AreEqual(JustGuardPhase.ReleasePenalty, js.Phase, "解除で連打ペナルティへ。");

            js.Tick(0.05f);
            Assert.IsTrue(js.CanJustGuard, "0.05 秒時点は解除後窓内。");
            js.Tick(0.03f);     // 累計 0.08 > 0.075
            Assert.IsFalse(js.CanJustGuard, "解除後窓 0.075 を過ぎると終了。");
        }

        [Test]
        public void Penalty_EndsAfter0_20()
        {
            var js = Make();
            js.Press();
            js.Tick(0.20f);
            js.Release();
            Assert.AreEqual(JustGuardPhase.ReleasePenalty, js.Phase);
            js.Tick(0.20f);
            Assert.AreEqual(JustGuardPhase.Normal, js.Phase, "ペナルティ時間経過で通常へ。");
        }

        [Test]
        public void SpamPressDuringPenalty_NoWindow()
        {
            var js = Make();
            js.Press();
            js.Tick(0.20f);
            js.Release();           // ペナルティ 0.20 開始
            js.Tick(0.09f);         // 解除後窓(0.075)は閉じ、ペナルティは継続
            Assert.IsFalse(js.CanJustGuard);

            js.Press();             // ペナルティ中の押下 → 窓を開かない
            Assert.IsFalse(js.CanJustGuard, "連打（ペナルティ中の押下）は受付窓を開かない。");
        }

        [Test]
        public void NotifySuccess_GrantsGrace_ClosesWindow_ClearsPenalty()
        {
            var js = Make();
            js.Press();
            js.NotifySuccess();
            Assert.IsTrue(js.HasSuccessGrace, "成功で連続成功猶予を付与。");
            Assert.IsFalse(js.CanJustGuard, "成功で現在の受付窓は閉じる（多段は新押下が必要）。");
            Assert.AreEqual(JustGuardPhase.SuccessGrace, js.Phase);
        }

        [Test]
        public void SuccessChain_ReleaseAvoidsPenalty_NextPressReopensFullWindow()
        {
            var js = Make();
            js.Press();
            js.NotifySuccess();          // 猶予付与、窓クローズ
            js.Release();                // 猶予中の解除：ペナルティを発生させず消費
            Assert.IsFalse(js.HasSuccessGrace, "解除で猶予を消費。");
            Assert.AreEqual(0f, js.PenaltyRemaining, 1e-4f, "猶予中の解除は連打ペナルティを発生させない。");

            js.Press();                  // 連鎖の次押下：通常窓が開く
            Assert.IsTrue(js.CanJustGuard, "連鎖の次押下で受付窓が再び開く。");
        }

        [Test]
        public void MultiHit_RequiresNewPress_AfterSuccess()
        {
            var js = Make();
            js.Press();
            js.NotifySuccess();          // 1 段目 JG 成功 → 窓クローズ
            Assert.IsFalse(js.CanJustGuard, "同じ押下では 2 段目を JG できない。");

            js.Release();
            js.Press();                  // 新しい押下
            Assert.IsTrue(js.CanJustGuard, "一段ごとに新押下で受付。");
        }

        [Test]
        public void RePressWhileHeld_DoesNotRefreshWindow()
        {
            var js = Make();
            js.Press();
            js.Tick(0.10f);              // 窓残り 0.05
            js.Press();                  // 保持中の再押下 → 窓更新しない
            js.Tick(0.06f);              // 残り 0.05 を超過
            Assert.IsFalse(js.CanJustGuard, "通常ガード中の再押下で窓は更新されない。");
        }

        [Test]
        public void ReleaseWithoutPress_NoOp()
        {
            var js = Make();
            js.Release();
            Assert.IsFalse(js.CanJustGuard);
            Assert.AreEqual(JustGuardPhase.Normal, js.Phase);
        }
    }
}
