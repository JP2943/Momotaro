namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 遷移中の表示代理の狭い窓口（P5.5 仕様書 §7.2／付録 A.1 と同じ考え方）。
    ///
    /// <b>Infrastructure は Presentation を参照しない。</b> 代理そのもの（SpriteRenderer の複製）は
    /// Presentation の持ち物なので、遷移の実行役はこの口だけを通す。カメラで同じことをしている
    /// <see cref="IAreaCameraOwner"/> と対になる。
    ///
    /// <b>Area Scene ではなく常駐が持つ。</b> 代理は出発 Scene が撤去されても運び続ける必要があるので、
    /// Scene 側の部品に持たせられない（§7.2「旧 Scene が消えた瞬間に代理も消えて主人公が欠ける」）。
    ///
    /// <b>「立てる」と「隠す」を分けてある。</b> 出発側は代理を立てて実 Renderer を隠すが、
    /// 到着側は<b>隠すだけ</b>——到着 Actor はすでに入口に置かれており、代理を立てる必要はない。
    /// 隠さないと、代理と到着 Actor が同じ画面に二重で映る（§6.2 手順 6
    /// 「両方の実 Actor の Renderer は隠し」）。
    /// </summary>
    public interface IAreaTransitionDisplay
    {
        /// <summary>いま代理が立っているか。</summary>
        bool IsActive { get; }

        /// <summary>立っている代理の数（診断・テスト用）。</summary>
        int ProxyCount { get; }

        /// <summary>隠している実 Renderer の数（診断・テスト用。出発側と到着側の合計）。</summary>
        int HiddenRendererCount { get; }

        /// <summary>代理を立てた回数（診断・テスト用）。</summary>
        int BuildCount { get; }

        /// <summary>代理を畳んだ回数（診断・テスト用）。</summary>
        int ReleaseCount { get; }

        /// <summary>退場していたので犬丸の代理を作らなかったか（診断・テスト用。§7.2）。</summary>
        bool CompanionSkippedBecauseAway { get; }

        /// <summary>
        /// 安全な表示経路を選べず、犬丸を運ばなかったか（診断・テスト用。工程 P55-07c）。
        /// </summary>
        bool CompanionRouteDropped { get; }

        /// <summary>
        /// 主人公の代理へ渡した Move のコマ数（診断・テスト用。§7.2。工程 P55-09b）。
        /// <b>0 は「止まった絵」</b>——コマを取り出せなかったことを黙って通さないために数える。
        /// </summary>
        int PlayerMoveFrameCount { get; }

        /// <summary>コマを渡せなかった理由（診断・テスト用。渡せたなら空）。</summary>
        string MoveFrameFallbackReason { get; }

        /// <summary>
        /// 犬丸の表示代理を<b>取り下げる</b>（工程 P55-07c）。
        ///
        /// §7.2 は犬丸について「障害物を横切らない表示経路を<b>選び</b>」と定め、
        /// 準備失敗を求めているのは<b>接続</b>（主人公の経路）のほうである。
        /// 汎用経路探索は導入しないので、直線が塞がっていれば選べる経路は無い——
        /// そのときは運ばない。犬丸は到着地点で現れる（実 Renderer は
        /// <see cref="Release"/> が戻す）。
        /// </summary>
        void DropCompanionProxy();

        /// <summary>
        /// 出発側の Actor から代理を立て、<b>同じフレームで</b>実 Renderer を隠す（§7.2）。
        /// すでに立っているときは false（二重に立てない）。
        /// </summary>
        bool TryBegin(AreaRuntimeBundle departure);

        /// <summary>
        /// 到着側の実 Actor の Renderer を隠す（§6.2 手順 6）。代理は立てない。
        /// <see cref="Release"/> で元に戻す。
        /// </summary>
        void HideArrivals(AreaRuntimeBundle destination);

        /// <summary>運ぶ区間を渡す（主人公の出発位置→到着位置。犬丸は同じ差分で運ぶ）。</summary>
        /// <summary>
        /// 運ぶ区間を渡す（§7.2）。
        ///
        /// <b>主人公と犬丸の終点は別々に渡す</b>（工程 P55-07b。GPT 受入④）。
        /// 以前は犬丸を「主人公の移動差分」で運んでいたが、到着実体は
        /// <c>AreaInitializer.PlaceArrivals</c> が<b>入口から進行方向と逆へ 1.2m</b>に置く。
        /// 出発時の相対位置が偶然一致していなければ、代理を畳んだ瞬間に犬丸が跳ぶ。
        /// Down など離れているときほど差が大きい。
        /// </summary>
        void SetRoute(
            UnityEngine.Vector3 playerFrom, UnityEngine.Vector3 playerTo,
            UnityEngine.Vector3 companionFrom, UnityEngine.Vector3 companionTo);

        /// <summary>カメラと同じ補間進行度を配る（§7.2）。</summary>
        void SetProgress(float eased);

        /// <summary>表示専用時計を配る（unscaled。コマ送りだけを進める）。</summary>
        void TickDisplayClock(float unscaledDeltaTime);

        /// <summary>
        /// 代理を畳み、<b>同じフレームで</b>隠した実 Renderer を戻す（§7.2）。
        /// 成功経路と Rollback の両方から呼ばれるので、何度呼んでも安全であること。
        /// </summary>
        void Release();
    }

    /// <summary>
    /// 遷移中の表示代理の提供点（付録 A.2 と同じ形）。所有者一致で解除する（§5.2）。
    ///
    /// <b>Scene 側からは差さない。</b> 常駐の表示担当だけが自分を差す。
    /// Area Scene の部品に差させると、先読みで載った Area が現行の担当を奪う。
    /// </summary>
    public static class AreaTransitionDisplayProvider
    {
        /// <summary>いまの表示担当（無ければ null）。</summary>
        public static IAreaTransitionDisplay Current { get; private set; }

        /// <summary>差した者（所有者一致の解除に使う）。</summary>
        public static object Owner { get; private set; }

        /// <summary>差さっているか。</summary>
        public static bool HasOwner => Current != null;

        /// <summary>差す。別の所有者が差しているときは奪わない。</summary>
        public static bool TrySetCurrent(object owner, IAreaTransitionDisplay display)
        {
            if (owner == null || display == null)
            {
                return false;
            }

            if (Owner != null && !ReferenceEquals(Owner, owner))
            {
                return false;
            }

            Current = display;
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
