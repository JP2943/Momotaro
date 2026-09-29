using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.World;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 前面判定の窓口（P5.5 仕様書 §7.1。§11 の P19。工程 P55-10f）。
    ///
    /// §7.1 は「アプリ非フォーカス中はスライドの<b>表示時間を進めず</b>、
    /// 復帰時に巨大 delta で飛ばさない。<b>ロード監視は別の unscaled／実時間で継続する</b>」と定める。
    ///
    /// ここで固めるのは<b>既定と所有権</b>である。規則の効き方（止まる・飛ばない）は
    /// 実スライドで見る（<c>P55SlideTransitionPlayTests</c>）。
    /// </summary>
    public sealed class P55AppFocusTests
    {
        [SetUp]
        public void SetUp() => AppFocusProvider.ClearForTests();

        [TearDown]
        public void TearDown() => AppFocusProvider.ClearForTests();

        /// <summary>
        /// <b>差さっていなければ「前面に居る」。</b>
        ///
        /// ここが逆だと、窓口を配線していない構成——テスト用の最小 Scene、常駐が立つ前の
        /// 1 フレーム——で<b>スライドが一切進まなくなる</b>。
        /// 止めるのは「止めろと言われたとき」だけにする。
        /// </summary>
        [Test]
        public void WithoutASource_TheAppCountsAsFocused()
        {
            Assert.IsFalse(AppFocusProvider.HasOwner, "前提：誰も差していない。");
            Assert.IsTrue(AppFocusProvider.IsFocused, "既定は前面。");
        }

        /// <summary>差した窓口の答えがそのまま出る。</summary>
        [Test]
        public void TheSourceDecides()
        {
            var owner = new object();
            var source = new FakeFocus { IsFocused = false };

            Assert.IsTrue(AppFocusProvider.TrySetCurrent(owner, source), "差せる。");
            Assert.IsFalse(AppFocusProvider.IsFocused, "背面と言えば背面。");

            source.IsFocused = true;
            Assert.IsTrue(AppFocusProvider.IsFocused, "前面と言えば前面。");
        }

        /// <summary>
        /// 別の所有者からは<b>奪えない</b>（付録 A.2 と同じ規律）。
        /// 先読み中の Area が差せると、載っただけの Area が現行の担当を奪う。
        /// </summary>
        [Test]
        public void AnotherOwner_CannotTakeOver()
        {
            var first = new object();
            var second = new object();
            var mine = new FakeFocus { IsFocused = false };

            Assert.IsTrue(AppFocusProvider.TrySetCurrent(first, mine));
            Assert.IsFalse(
                AppFocusProvider.TrySetCurrent(second, new FakeFocus { IsFocused = true }),
                "別の所有者は奪えない。");
            Assert.IsFalse(AppFocusProvider.IsFocused, "答えは最初の窓口のまま。");

            // 同じ所有者なら差し直せる（配線のやり直し）。
            Assert.IsTrue(AppFocusProvider.TrySetCurrent(first, new FakeFocus { IsFocused = true }),
                "同じ所有者は差し直せる。");
            Assert.IsTrue(AppFocusProvider.IsFocused);
        }

        /// <summary>
        /// 外せるのは<b>自分が差したものだけ</b>。外れたら既定（前面）へ戻る——
        /// <b>常駐が消えた拍子にスライドが止まる</b>のを防ぐ。
        /// </summary>
        [Test]
        public void ReleasingIsOwnerMatched_AndFallsBackToFocused()
        {
            var owner = new object();
            AppFocusProvider.TrySetCurrent(owner, new FakeFocus { IsFocused = false });

            AppFocusProvider.ReleaseIfOwner(new object());
            Assert.IsTrue(AppFocusProvider.HasOwner, "他人は外せない。");
            Assert.IsFalse(AppFocusProvider.IsFocused, "まだ背面のまま。");

            AppFocusProvider.ReleaseIfOwner(owner);
            Assert.IsFalse(AppFocusProvider.HasOwner, "自分のは外せる。");
            Assert.IsTrue(AppFocusProvider.IsFocused, "外れたら既定（前面）へ戻る。");
        }

        /// <summary>null は差さない（黙って「背面」になる事故を作らない）。</summary>
        [Test]
        public void NullsAreRefused()
        {
            Assert.IsFalse(AppFocusProvider.TrySetCurrent(new object(), null), "窓口が null。");
            Assert.IsFalse(AppFocusProvider.TrySetCurrent(null, new FakeFocus()), "所有者が null。");
            Assert.IsTrue(AppFocusProvider.IsFocused, "既定のまま。");
        }

        // ------------------------------------------------ 表示に使ってよい秒数

        /// <summary>
        /// <b>非フォーカス中は 0</b>——表示時間を進めない（§7.1）。
        /// 進めてしまうと、戻ってきたときには<b>もう着いている</b>（移動を一切見ていない）。
        /// </summary>
        [Test]
        public void WhileNotFocused_TheDisplayStepIsZero()
        {
            Assert.AreEqual(0f, AreaSlideTransitionRunner.ResolveDisplayStep(0.016f, focused: false));
            Assert.AreEqual(0f, AreaSlideTransitionRunner.ResolveDisplayStep(5f, focused: false));
        }

        /// <summary>
        /// <b>復帰時に巨大 delta で飛ばさない</b>（§7.1）。
        ///
        /// 背面から戻った 1 フレームの <c>unscaledDeltaTime</c> は<b>止まっていた時間そのもの</b>に
        /// なりうる。そのまま渡すと、0.45 秒の演出が 1 フレームで終わる——止めた意味が消える。
        ///
        /// <b>ここでしか見られない。</b> 実 Scene では背面に回しても Unity がフレームを回し続けるので、
        /// 巨大 delta を作れない（工程 P55-10f で実測した）。値を与えて決定的に見る。
        /// </summary>
        [Test]
        public void AHugeStep_IsCappedSoTheSlideCannotJump()
        {
            Assert.AreEqual(AreaSlideTransitionRunner.MaxDisplayStepSeconds,
                AreaSlideTransitionRunner.ResolveDisplayStep(5f, focused: true), 1e-6f,
                "止まっていた 5 秒がそのまま届いても、上限で押さえる。");

            Assert.Less(AreaSlideTransitionRunner.MaxDisplayStepSeconds,
                AreaSlideTransitionRunner.FallbackSlideSeconds,
                "上限は演出時間より短い（1 フレームで終わらない、と言えるための前提）。");
        }

        /// <summary>普通のフレームは<b>そのまま通す</b>（上限が常時効いて遅くならない）。</summary>
        [Test]
        public void AnOrdinaryStep_PassesThrough()
        {
            Assert.AreEqual(1f / 60f,
                AreaSlideTransitionRunner.ResolveDisplayStep(1f / 60f, focused: true), 1e-6f);
        }

        private sealed class FakeFocus : IAppFocusSource
        {
            public bool IsFocused { get; set; } = true;
        }
    }
}
