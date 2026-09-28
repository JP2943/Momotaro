using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 常駐 Camera の狭い窓口（P5.5 付録 A.1／A.2）。
    ///
    /// <b>Infrastructure は Presentation を参照しない。</b> 遷移サービスが常駐 Rig へ用があるときは
    /// この口だけを通す。Rig の具体型（<c>AreaCameraRig</c>）は Presentation に閉じる。
    ///
    /// <b>「到着点を計算する」と「実カメラへ適用する」を分けてある</b>（付録 A.3）。
    /// Prepared の時点で適用してしまうと、スライドが始まる前にカメラが跳ぶ——
    /// §7.1 の「事前に境界位置へ瞬間移動させない」が破れる。
    /// </summary>
    public interface IAreaCameraOwner
    {
        /// <summary>活動 Area の領域集合と結び付いているか。</summary>
        bool IsWired { get; }

        /// <summary>いま結び付いている Area（無ければ空）。</summary>
        StableId BoundArea { get; }

        /// <summary>結び直した回数（診断・テスト用）。</summary>
        int BindCount { get; }

        /// <summary>実カメラへ適用した回数（診断・テスト用）。Prepared では増えない。</summary>
        int ApplyCount { get; }

        /// <summary>
        /// 活動 Area（<see cref="CurrentAreaProvider"/>）の領域集合へ結び直す。
        /// <b>参照を差し替えるだけ</b>で、Camera インスタンスは交換しないし
        /// 実カメラの位置も動かさない（付録 A.5／A.10）。
        /// </summary>
        bool TryBindActiveArea();

        /// <summary>
        /// <b>いま結び付いている Area</b>での追従位置を計算するだけ（実カメラへは触らない。付録 A.3）。
        /// 計算できなければ false。
        /// </summary>
        bool TryComputeArrivalPoint(out UnityEngine.Vector3 point);

        /// <summary>
        /// <b>移動先</b>の到着点を、現在の結び先を変えずに計算するだけ（付録 A.3／A.10）。
        ///
        /// 引数なしの版は結び先＝活動 Area の位置しか返せないので、
        /// 「A を遊んでいる間に B の到着点を知りたい」には答えられない。
        /// スライドの終点を決めるにはこちらを使う。
        /// <b>結び先を切り替えて測ってはいけない</b>——切り替えるだけで領域と追従対象が
        /// 入れ替わり、スライドが始まる前にカメラが動く（§7.1）。
        /// </summary>
        /// <param name="destination">移動先の読み込み実体（<see cref="AreaRuntimeBundle.Instance"/>）。</param>
        /// <param name="arrivalPosition">その Area での到着位置（入口の世界座標）。</param>
        bool TryComputeArrivalPoint(
            AreaInstanceHandle destination, UnityEngine.Vector3 arrivalPosition,
            out UnityEngine.Vector3 point);

        /// <summary>
        /// 計算した位置を実カメラへ適用する（付録 A.10）。
        ///
        /// 適用するのは次の 3 か所だけで、それ以外からは呼ばない。
        /// <list type="number">
        /// <item><description><b>初回配置</b>——常駐 Rig が立った Area の入口配置が終わったとき。</description></item>
        /// <item><description><b>Single 遷移の到着</b>——活動 Area が入れ替わり、入口配置が終わったとき。</description></item>
        /// <item><description><b>スライドの Commit 後</b>——演出担当が書込みを手放して追従へ戻すとき。</description></item>
        /// </list>
        /// <b>Prepared だけでは呼ばない。</b> 先読みで載った Area の準備完了は、
        /// まだ活動 Area ではないので適用の合図ではない（§7.1）。
        /// </summary>
        void ApplyArrival();

        /// <summary>
        /// スライドを始める（§7.1／付録 A.4）。
        ///
        /// <b>始点は渡さない。</b> 始点は「受理時の実 Rig 位置」で、それを知っているのは Rig 自身である
        /// （§7.1「事前に境界位置へ瞬間移動させない」）。呼び出し側が始点を渡せる形にすると、
        /// 出発位置を計算で作れてしまい、その計算がずれた分だけ演出の初めに飛びが出る。
        /// 終点は <see cref="TryComputeArrivalPoint(AreaInstanceHandle, UnityEngine.Vector3, out UnityEngine.Vector3)"/>
        /// で先に測っておく。
        /// </summary>
        bool BeginSlide(UnityEngine.Vector3 to, float seconds);

        /// <summary>
        /// スライドを 1 フレーム進める（unscaled 秒を渡す）。戻り値は<b>まだ走っているか</b>。
        /// 終わったフレームでも終点を適用してから false を返すので、呼び出し側は
        /// 「false になったら <see cref="EndSlide"/>」でよい。
        /// </summary>
        bool TickSlide(float unscaledDeltaTime);

        /// <summary>スライドを終える。終点を厳密に適用し、結び直しが来るまで終点に留まる。</summary>
        void EndSlide();

        /// <summary>
        /// スライドを終え、<b>そのまま通常追従へ戻す</b>（§8 の 3 行目「同じ描画経路を逆向きに戻す」の終わり）。
        ///
        /// <see cref="EndSlide"/> は結び直しを待って終点に留まる——活動 Area が入れ替わる
        /// 成功経路のためのものである。<b>戻しには使えない。</b> 戻したあとに結び直しは起きない
        /// （活動 Area は出発側のまま）ので、留まったまま誰も解かず、カメラが二度と追従しない。
        ///
        /// こちらは追従の内部状態も終点へ同期してから追従を戻す。同期しないと、
        /// 次のフレームに前の値から補間が始まって<b>カメラが跳ね返る</b>。
        /// </summary>
        void EndSlideAndResumeFollow();

        /// <summary>スライドを打ち切って出発位置へ戻す（§8）。<b>終点へは進めない。</b></summary>
        void CancelSlide();

        /// <summary>いまスライド中か。</summary>
        bool IsSliding { get; }

        /// <summary>
        /// いまの補間進行度（0〜1。<c>s(t)=3t²−2t³</c> を通した値）。
        ///
        /// <b>表示代理へ配るのはこの値</b>（§7.2「カメラと同じ補間進行度で移動する」）。
        /// 呼び出し側が自前の時計から進行度を作ると、カメラと代理が別々の曲線で動き、
        /// 主人公が画面の中で滑る。
        /// </summary>
        float SlideEased { get; }

        /// <summary>
        /// いまの実 Rig 位置（未配線なら false）。Rollback の戻り先と診断に使う。
        /// </summary>
        bool TryGetRigPosition(out UnityEngine.Vector3 position);
    }

    /// <summary>
    /// 常駐 Camera の提供点（付録 A.2）。所有者一致で解除する（§5.2）。
    ///
    /// <b>Scene 側からは差さない。</b> 常駐 Rig 自身だけが自分を差す。
    /// Area Scene の部品に差させると、先読みで載った Area が現行の Rig を奪う。
    /// </summary>
    public static class AreaCameraOwnerProvider
    {
        /// <summary>いまの常駐 Camera（無ければ null）。</summary>
        public static IAreaCameraOwner Current { get; private set; }

        /// <summary>差した者（所有者一致の解除に使う）。</summary>
        public static object Owner { get; private set; }

        /// <summary>差さっているか。</summary>
        public static bool HasOwner => Current != null;

        /// <summary>差す。別の所有者が差しているときは奪わない。</summary>
        public static bool TrySetCurrent(object owner, IAreaCameraOwner camera)
        {
            if (owner == null || camera == null)
            {
                return false;
            }

            if (Owner != null && !ReferenceEquals(Owner, owner))
            {
                return false;
            }

            Current = camera;
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
