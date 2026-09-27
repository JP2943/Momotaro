using Momotaro.Presentation.Cameras;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// スライドの進み方（P5.5 仕様書 §7.1／付録 A.4。工程 P55-04a。§11 の E05 の一部）。
    ///
    /// ここで固めるのは<b>曲線・所要時間・端の精度</b>。実カメラも Scene も要らないので、
    /// 数値の契約はここで決定的に押さえる。実カメラへの書込が 1 系統であることは
    /// 実 Rig が要るので PlayMode で見る。
    /// </summary>
    public sealed class P55CameraSlideTests
    {
        /// <summary>
        /// 曲線は <c>s(t)=3t²−2t³</c>（§7.1）。
        ///
        /// <b>端が厳密であることが要点。</b> 0 と 1 がぴったりでないと、
        /// 開始で 1 フレーム飛び、終了で翌フレームに跳ね返る。
        /// 中央が 0.5、両端の傾きが 0（滑り出し・滑り込み）であることも見る——
        /// 単に「0 と 1 を返す」だけなら線形でも通ってしまう。
        /// </summary>
        [Test]
        public void TheCurve_IsSmoothStepWithExactEnds()
        {
            Assert.AreEqual(0f, AreaCameraSlide.Ease(0f), "始点は厳密に 0。");
            Assert.AreEqual(1f, AreaCameraSlide.Ease(1f), "終点は厳密に 1。");
            Assert.AreEqual(0.5f, AreaCameraSlide.Ease(0.5f), 1e-6f, "中央は 0.5。");

            // 端の傾きが 0（線形ではない）。
            float nearStart = AreaCameraSlide.Ease(0.02f);
            float nearEnd = 1f - AreaCameraSlide.Ease(0.98f);
            Assert.Less(nearStart, 0.02f, "始めはゆっくり動く（線形なら 0.02）。");
            Assert.Less(nearEnd, 0.02f, "終わりもゆっくり止まる。");

            // 範囲外を渡しても飛び出さない。
            Assert.AreEqual(0f, AreaCameraSlide.Ease(-1f));
            Assert.AreEqual(1f, AreaCameraSlide.Ease(2f));

            // 単調増加（途中で戻らない）。
            float previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float value = AreaCameraSlide.Ease(i / 20f);
                Assert.Greater(value, previous - 1e-6f, "進行度は戻らない（i=" + i + "）。");
                previous = value;
            }
        }

        /// <summary>
        /// 所要時間どおりに終わり、<b>始点と終点は厳密に一致する</b>（§7.1）。
        ///
        /// 端が 1 フレーム分ずれるだけで、開始で飛び・終了で跳ねる。
        /// </summary>
        [Test]
        public void TheSlide_LandsExactlyOnBothEndsAfterItsDuration()
        {
            var slide = new AreaCameraSlide();
            var from = new Vector3(9f, 0f, 0f);
            var to = new Vector3(20.889f, 0f, 0f);
            slide.Begin(from, to, AreaCameraSlide.DefaultSeconds);

            Assert.IsTrue(slide.IsRunning);
            Assert.AreEqual(from, slide.Position, "始めた時点では出発位置そのもの。");
            Assert.AreEqual(0f, slide.Eased);

            // 0.45 秒ぶんを 1/60 秒で刻む。
            int steps = 0;
            while (slide.Tick(1f / 60f) && steps < 1000)
            {
                steps++;
                Assert.Greater(slide.Position.x, from.x - 0.001f, "戻らない。");
                Assert.Less(slide.Position.x, to.x + 0.001f, "行き過ぎない。");
            }

            Assert.IsFalse(slide.IsRunning, "所要時間で終わる。");
            Assert.AreEqual(AreaCameraSlide.DefaultSeconds, slide.Elapsed, 1e-4f, "経過は所要秒で止まる。");
            Assert.AreEqual(1f, slide.Eased, "終点の進行度は厳密に 1。");
            Assert.AreEqual(to, slide.Position, "終点は厳密に一致する。");
            Assert.Greater(steps, 20, "途中のフレームが存在している（1 フレームで飛んでいない）。");
        }

        /// <summary>
        /// <b>東西の接続では X だけ動く</b>（§7.1）。始点と終点の Z が同じなら途中も同じ。
        ///
        /// これは配置側（境界寄せ領域）で保証する性質だが、補間が勝手に Z を
        /// 触らないことはここで固定しておく。
        /// </summary>
        [Test]
        public void AnEastWestSlide_NeverMovesInZ()
        {
            var slide = new AreaCameraSlide();
            slide.Begin(new Vector3(9f, 0f, 0f), new Vector3(21f, 0f, 0f), 0.45f);

            while (slide.Tick(1f / 120f))
            {
                Assert.AreEqual(0f, slide.Position.z, 1e-6f, "Z は動かない。");
            }

            Assert.AreEqual(0f, slide.Position.z, 1e-6f);
        }

        /// <summary>
        /// 巨大な delta を 1 回渡しても<b>終点を超えない</b>（§7.1 末尾）。
        ///
        /// アプリ非フォーカスから復帰したときに、進める側が誤って溜まった時間を
        /// まとめて渡すことがある。飛ばさない責任は呼び出し側だが、
        /// ここが壊れると終点の向こうへ突き抜ける。
        /// </summary>
        [Test]
        public void AHugeDelta_StopsAtTheEndInsteadOfOvershooting()
        {
            var slide = new AreaCameraSlide();
            var to = new Vector3(20f, 0f, 3f);
            slide.Begin(Vector3.zero, to, 0.45f);

            Assert.IsFalse(slide.Tick(30f), "1 回で終わる。");
            Assert.AreEqual(to, slide.Position, "終点で止まる（突き抜けない）。");
            Assert.AreEqual(1f, slide.Eased);
        }

        /// <summary>
        /// 打ち切りは<b>終点へ進めない</b>（§8「同じ描画経路を逆向きに戻す」）。
        /// 失敗した遷移で到着側の位置を見せてはいけない。
        /// </summary>
        [Test]
        public void Cancelling_DoesNotJumpToTheDestination()
        {
            var slide = new AreaCameraSlide();
            var from = new Vector3(1f, 0f, 0f);
            var to = new Vector3(21f, 0f, 0f);
            slide.Begin(from, to, 0.45f);
            slide.Tick(0.1f);

            Vector3 midway = slide.Position;
            slide.Cancel();

            Assert.IsFalse(slide.IsRunning);
            Assert.AreEqual(midway, slide.Position, "打ち切っても位置は進まない。");
            Assert.Less(slide.Position.x, to.x - 1f, "終点へ飛んでいない。");
            Assert.AreEqual(from, slide.From, "出発位置は覚えている（戻る先がある）。");
        }

        /// <summary>所要秒 0 でも 0 除算にならず、1 フレームで終わる。</summary>
        [Test]
        public void AZeroDuration_FinishesImmediatelyWithoutDividingByZero()
        {
            var slide = new AreaCameraSlide();
            var to = new Vector3(5f, 0f, 0f);
            slide.Begin(Vector3.zero, to, 0f);

            Assert.AreEqual(1f, slide.Progress, "所要 0 は最初から終わっている扱い。");
            Assert.AreEqual(to, slide.Position);
            Assert.IsFalse(slide.Tick(0.016f), "1 回で終わる。");
        }
    }
}
