using Momotaro.Gameplay.Session;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// <b>0.3 秒以上待つ場合だけ待ち表示を出す</b>（P5.5 仕様書 §5。工程 P55-09a）。
    ///
    /// §5 は「受理時に準備不足なら、A を描画したまま操作を止めて待つ。
    /// <b>0.3 秒以上待つ場合だけ</b>控えめな『読み込み中』を表示。全画面を黒くしない。」と定める。
    ///
    /// <b>「出ないこと」がいちばん抜けやすい。</b> 先読みが間に合っている正常系では待ちは
    /// 1〜2 フレームで終わるので、そこで一瞬だけ字が出ると<b>ちらつき</b>になる。
    /// 「出す」側だけを実装しても受入は通ってしまう——誰も「出ない」を見ないからである。
    ///
    /// <b>実遷移では境目を狙えない。</b> ロードに要する時間は環境で変わるので、
    /// 0.3 秒の前後を実 Scene で作ると割れる検査になる。刻みを与えて決定的に見る。
    /// 実際の配線（常駐の窓口・全画面を覆わないこと）は PlayMode で見る。
    /// </summary>
    public sealed class P55WaitNoticeTests
    {
        private const float Frame = 1f / 60f;

        /// <summary>閾値は 0.3 秒（仕様の数字を実装から読み直さない）。</summary>
        [Test]
        public void TheThreshold_IsThreeTenthsOfASecond()
        {
            Assert.AreEqual(0.3f, AreaTransitionWaitNoticeTimer.ThresholdSeconds, 0.0001f,
                "§5 の「0.3 秒以上」。");
        }

        /// <summary>
        /// <b>短い待ちでは出さない</b>。先読みが間に合っている正常系（1〜2 フレーム）で
        /// 字が出ると、ちらつきとして見える。
        /// </summary>
        [Test]
        public void AShortWait_ShowsNothing()
        {
            var timer = new AreaTransitionWaitNoticeTimer();
            timer.Begin();

            for (int i = 0; i < 2; i++)
            {
                Assert.IsFalse(timer.Tick(Frame),
                    "2 フレームの待ちでは出さない（" + ((i + 1) * Frame) + " 秒）。");
            }

            timer.End();
            Assert.AreEqual(0, timer.ShownCount, "一度も出していない。");
            Assert.AreEqual(-1f, timer.ShownAtSeconds, "決めた時点も残らない。");
        }

        /// <summary>閾値<b>ちょうど</b>で出す（「0.3 秒以上」なので境界は含む）。</summary>
        [Test]
        public void AtTheThreshold_ItShows()
        {
            var timer = new AreaTransitionWaitNoticeTimer();
            timer.Begin();

            Assert.IsFalse(timer.Tick(0.29f), "0.29 秒では出さない。");
            Assert.IsTrue(timer.Tick(0.01f), "0.30 秒ちょうどで出す。");
            Assert.AreEqual(1, timer.ShownCount, "出したのは 1 回。");
            Assert.GreaterOrEqual(timer.ShownAtSeconds, 0.29f,
                "決めた時点の秒数を残す（実遷移の受入がここを見る）。");
        }

        /// <summary>出したあとは、待っている間ずっと出したまま（毎フレーム出し直さない）。</summary>
        [Test]
        public void OnceShown_ItStaysShownForTheRestOfTheWait()
        {
            var timer = new AreaTransitionWaitNoticeTimer();
            timer.Begin();
            timer.Tick(0.3f);

            for (int i = 0; i < 30; i++)
            {
                Assert.IsTrue(timer.Tick(Frame), "出したままである（" + i + " フレーム目）。");
            }

            Assert.AreEqual(1, timer.ShownCount, "出したと数えるのは 1 回だけ。");
        }

        /// <summary>
        /// <b>待ちごとに 0 から数え直す</b>。
        ///
        /// ここを外すと、<b>1 回目が長かっただけで以後ずっと出る</b>——
        /// 2 回目以降の短い待ちでも、前の待ちの残りが閾値を超えているからである。
        /// </summary>
        [Test]
        public void EachWait_CountsFromZero()
        {
            var timer = new AreaTransitionWaitNoticeTimer();

            timer.Begin();
            timer.Tick(1f);
            Assert.IsTrue(timer.ShouldShow, "前提：1 回目は出た。");
            timer.End();

            timer.Begin();
            Assert.IsFalse(timer.ShouldShow, "始めた時点では出ていない。");
            Assert.AreEqual(0f, timer.WaitedSeconds, "秒数も 0 から。");
            for (int i = 0; i < 2; i++)
            {
                Assert.IsFalse(timer.Tick(Frame), "2 回目の短い待ちでは出さない。");
            }

            Assert.AreEqual(1, timer.ShownCount, "出したのは 1 回目だけ。");
            Assert.AreEqual(2, timer.BegunCount, "待ちは 2 回始めた。");
        }

        /// <summary>待ちを終えれば消える（成功でも失敗でも同じ）。</summary>
        [Test]
        public void EndingTheWait_ClearsIt()
        {
            var timer = new AreaTransitionWaitNoticeTimer();
            timer.Begin();
            timer.Tick(1f);
            Assert.IsTrue(timer.ShouldShow, "前提：出ている。");

            timer.End();

            Assert.IsFalse(timer.ShouldShow, "消えている。");
            Assert.IsFalse(timer.Tick(1f), "終えたあとは進めても出ない。");
        }

        /// <summary>始めていないうちは、進めても出ない（取りこぼしで出さない）。</summary>
        [Test]
        public void WithoutBeginning_TickingShowsNothing()
        {
            var timer = new AreaTransitionWaitNoticeTimer();

            Assert.IsFalse(timer.Tick(10f), "始めていない待ちは無い。");
            Assert.AreEqual(0, timer.ShownCount, "出していない。");
            Assert.AreEqual(0f, timer.WaitedSeconds, "数えてもいない。");
        }

        /// <summary>
        /// 負の刻みで<b>時間が巻き戻らない</b>。
        /// 非フォーカス復帰などで妙な delta が来ても、閾値の判断を狂わせない。
        /// </summary>
        [Test]
        public void ANegativeStep_DoesNotRewindTheWait()
        {
            var timer = new AreaTransitionWaitNoticeTimer();
            timer.Begin();
            timer.Tick(0.2f);

            timer.Tick(-5f);

            Assert.AreEqual(0.2f, timer.WaitedSeconds, 0.0001f, "巻き戻らない。");
            Assert.IsFalse(timer.ShouldShow, "まだ閾値に届かない。");
        }
    }
}
