namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// アプリが<b>前面に居るか</b>を言う窓口（P5.5 仕様書 §7.1。工程 P55-10f）。
    ///
    /// §7.1 はこう定める——「アプリ非フォーカス中はスライドの<b>表示時間を進めず</b>、
    /// 復帰時に<b>巨大 delta で飛ばさない</b>。ロード監視は別の unscaled／実時間で継続する。」
    ///
    /// <b>止めるのは表示だけである。</b> 読込の監視まで止めると、非フォーカスのあいだに
    /// 終端したロードを誰も引き取らなくなる——復帰した瞬間に時間切れが確定する、
    /// という<b>触っていないのに失敗する</b>形になる。
    ///
    /// <b>なぜ窓口にするか。</b> 「前面に居るか」を <c>Application.isFocused</c> で直接読むと、
    /// 受入で<b>非フォーカスを作れない</b>（Editor の Game View のフォーカスは実行側から動かせない）。
    /// 差し替えられる窓口にしておけば、規則そのものを決定的に見られる。
    /// </summary>
    public interface IAppFocusSource
    {
        /// <summary>アプリが前面に居るか。</summary>
        bool IsFocused { get; }
    }

    /// <summary>
    /// 前面判定の提供点（付録 A.2 と同じ形。工程 P55-10f）。
    ///
    /// <b>差さっていなければ「前面に居る」とみなす。</b> 既定を「非フォーカス」にすると、
    /// 窓口を配線していない構成（テスト用の最小 Scene・起動直後の 1 フレーム）で
    /// <b>スライドが進まなくなる</b>。止めるのは「止めろと言われたとき」だけにする。
    /// </summary>
    public static class AppFocusProvider
    {
        /// <summary>いまの窓口（無ければ null）。</summary>
        public static IAppFocusSource Current { get; private set; }

        /// <summary>差した者（所有者一致の解除に使う）。</summary>
        public static object Owner { get; private set; }

        /// <summary>差さっているか。</summary>
        public static bool HasOwner => Current != null;

        /// <summary>アプリが前面に居るか（窓口が無ければ true）。</summary>
        public static bool IsFocused => Current == null || Current.IsFocused;

        /// <summary>差す。別の所有者が差しているときは奪わない。</summary>
        public static bool TrySetCurrent(object owner, IAppFocusSource source)
        {
            if (owner == null || source == null)
            {
                return false;
            }

            if (Owner != null && !ReferenceEquals(Owner, owner))
            {
                return false;
            }

            Current = source;
            Owner = owner;
            return true;
        }

        /// <summary>自分が差したものだけを外す（§5.2 と同じ規律）。</summary>
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
