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
        /// <b>参照を差し替えるだけ</b>で、Camera インスタンスは交換しない（付録 A.5）。
        /// </summary>
        bool TryBindActiveArea();

        /// <summary>
        /// 到着点を<b>計算するだけ</b>（実カメラへは触らない。付録 A.3）。
        /// 計算できなければ false。
        /// </summary>
        bool TryComputeArrivalPoint(out UnityEngine.Vector3 point);

        /// <summary>
        /// 計算した位置を実カメラへ適用する。<b>Commit 後の追従復帰と演出担当だけが呼ぶ。</b>
        /// </summary>
        void ApplyArrival();
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
