namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// <b>待ちが長いときだけ「読み込み中」を出す</b>ための時計（P5.5 仕様書 §5。工程 P55-09a）。
    ///
    /// §5 はこう定める——「受理時に準備不足なら、A を描画したまま操作を止めて待つ。
    /// <b>0.3 秒以上待つ場合だけ</b>控えめな『読み込み中』を表示。全画面を黒くしない。」
    ///
    /// <b>短い待ちに出さないことが要件である。</b> 先読みが間に合っている正常系では待ちは
    /// 1〜2 フレームで終わるので、そこで一瞬だけ字が出ると<b>ちらつきとして見える</b>。
    /// 「出す」ほうだけを実装すると、それは受入を通ってしまう（誰も「出ないこと」を見ない）。
    ///
    /// <b>判断だけを取り出す。</b> 実 Scene のロードに要する時間は環境で変わるので、
    /// 実遷移で 0.3 秒の境目を狙うと<b>割れる検査</b>になる。刻みを与えて決定的に見る。
    ///
    /// <b>数え直しが要点。</b> 待ちごとに 0 から数えないと、前の待ちの残りで<b>次の待ちが
    /// 即座に出る</b>——1 回目が長かっただけで、以後ずっと出ることになる。
    /// </summary>
    public sealed class AreaTransitionWaitNoticeTimer
    {
        /// <summary>この秒数以上待つときだけ出す（§5）。</summary>
        public const float ThresholdSeconds = 0.3f;

        /// <summary>
        /// 閾値を見るときの遊び（秒）。
        ///
        /// <b>足し算の誤差で壁が越えられなくなるのを防ぐ。</b> 秒数は毎フレームの delta を
        /// float で足していくので、0.29 ＋ 0.01 は 0.3 より<b>わずかに小さい</b>。
        /// 遊びが無いと「0.3 秒以上」が実際には 0.3 秒<b>より少し後</b>になり、
        /// しかもその差はフレームの刻み方で変わる（＝再現しない）。
        /// 1 フレーム（16ms）よりはるかに小さい値なので、見た目には影響しない。
        /// </summary>
        private const float ThresholdEpsilon = 1e-4f;

        private bool _waiting;

        /// <summary>いま出すべきか。</summary>
        public bool ShouldShow { get; private set; }

        /// <summary>この待ちで経過した秒数（診断・テスト用）。</summary>
        public float WaitedSeconds { get; private set; }

        /// <summary>出すと決めた回数（診断・テスト用）。</summary>
        public int ShownCount { get; private set; }

        /// <summary>
        /// <b>出すと決めた時点の経過秒数</b>（診断・テスト用。まだ出していなければ −1）。
        ///
        /// <b>決めた場所で記録する。</b> 実遷移の受入から「0.3 秒に届いてから出したか」を
        /// 見ようとすると、テストが値を読むのは<b>決めた次のフレーム</b>になる——
        /// Scene ロード中はフレームが重いので、0.3 秒を跨いだ 1 フレームで
        /// <b>閾値を無視した実装でも「届いてから出した」に見える</b>。
        /// 実際そうなった（注入 80 が PlayMode を素通りした）。
        /// </summary>
        public float ShownAtSeconds { get; private set; } = -1f;

        /// <summary>待ちを始めた回数（診断・テスト用）。</summary>
        public int BegunCount { get; private set; }

        /// <summary>
        /// 待ちを始める。<b>毎回 0 から数え直す。</b>
        /// </summary>
        public void Begin()
        {
            _waiting = true;
            WaitedSeconds = 0f;
            ShouldShow = false;
            ShownAtSeconds = -1f;
            BegunCount++;
        }

        /// <summary>
        /// 待ちを 1 フレーム進める（unscaled。先読みは Gameplay 時計を見ない）。
        /// </summary>
        /// <returns>このフレームに<b>出すべき</b>なら true（出している間は毎フレーム true）。</returns>
        public bool Tick(float unscaledDeltaTime)
        {
            if (!_waiting)
            {
                return false;
            }

            if (unscaledDeltaTime > 0f)
            {
                WaitedSeconds += unscaledDeltaTime;
            }

            if (!ShouldShow && WaitedSeconds >= ThresholdSeconds - ThresholdEpsilon)
            {
                ShouldShow = true;
                ShownAtSeconds = WaitedSeconds;
                ShownCount++;
            }

            return ShouldShow;
        }

        /// <summary>待ちを終える（成功でも失敗でも）。出していたものは消す。</summary>
        public void End()
        {
            _waiting = false;
            ShouldShow = false;
        }
    }

    /// <summary>
    /// 待ち表示の狭い窓口（§5。工程 P55-09a）。
    ///
    /// <b>Infrastructure は Presentation を参照しない。</b> 字を出すのは Presentation の持ち物なので、
    /// 遷移の実行役はこの口だけを通す（<see cref="IAreaTransitionDisplay"/>・
    /// <see cref="IAreaCameraOwner"/> と同じ形）。
    ///
    /// <b>「出す・消す」しか無い。</b> 0.3 秒の境目を決めるのは
    /// <see cref="AreaTransitionWaitNoticeTimer"/>——表示側が自分で時間を計ると、
    /// 規則が 2 か所に散る。
    /// </summary>
    public interface IAreaTransitionWaitNotice
    {
        /// <summary>いま出ているか。</summary>
        bool IsShowing { get; }

        /// <summary>出した回数（診断・テスト用）。</summary>
        int ShowCount { get; }

        /// <summary>消した回数（診断・テスト用）。</summary>
        int HideCount { get; }

        /// <summary>
        /// 画面に対する<b>面積の割合</b>（診断・テスト用。0〜1）。
        /// <b>全画面を黒くしない</b>（§5）ことを、見た目の言葉ではなく数で言えるようにしておく。
        /// </summary>
        float ScreenAreaFraction { get; }

        /// <summary>出す。何度呼んでも 1 つ。</summary>
        void Show();

        /// <summary>消す。何度呼んでも安全（成功経路と失敗経路の両方から呼ばれる）。</summary>
        void Hide();
    }

    /// <summary>
    /// 待ち表示の提供点（付録 A.2 と同じ形）。所有者一致で解除する（§5.2）。
    ///
    /// <b>Scene 側からは差さない。</b> 常駐だけが自分を差す——先読み中の Area が
    /// 差せると、載っただけの Area が現行の担当を奪う。
    /// </summary>
    public static class AreaTransitionWaitNoticeProvider
    {
        /// <summary>いまの担当（無ければ null）。</summary>
        public static IAreaTransitionWaitNotice Current { get; private set; }

        /// <summary>差した者（所有者一致の解除に使う）。</summary>
        public static object Owner { get; private set; }

        /// <summary>差さっているか。</summary>
        public static bool HasOwner => Current != null;

        /// <summary>差す。別の所有者が差しているときは奪わない。</summary>
        public static bool TrySetCurrent(object owner, IAreaTransitionWaitNotice notice)
        {
            if (owner == null || notice == null)
            {
                return false;
            }

            if (Owner != null && !ReferenceEquals(Owner, owner))
            {
                return false;
            }

            Current = notice;
            Owner = owner;
            return true;
        }

        /// <summary>自分が差したものだけを外す（§5.2）。</summary>
        public static void ReleaseIfOwner(object owner)
        {
            if (owner != null && ReferenceEquals(Owner, owner))
            {
                Current = null;
                Owner = null;
            }
        }

        /// <summary>テスト間で状態を持ち越さないための掃除（テスト専用）。</summary>
        public static void ClearForTests()
        {
            Current = null;
            Owner = null;
        }
    }
}
