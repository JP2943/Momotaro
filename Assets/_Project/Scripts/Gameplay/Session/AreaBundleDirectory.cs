using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 読み込まれている Area の参照集合の索引（P5.5 §4.3）。
    ///
    /// <b>ここは「どこに在るか」だけを持ち、「どれが活動中か」は持たない。</b>
    /// 2 つを同じ表に混ぜると、先読みで束が載った瞬間に活動先が変わってしまう。
    /// 活動中の指定は <see cref="CurrentAreaProvider"/>（常駐が所有）が持ち、Commit で切り替える。
    ///
    /// 登録は Scene 側の <c>AreaRuntimeBundle</c> の <c>OnEnable</c> から起きるが、
    /// <b>引き当ては必ず Scene handle か実体ハンドルを指定して行う</b>ため、
    /// 「まだ活動していない Area の物を掴む」事故は起きない。
    /// 唯一の例外は <see cref="TryGetSingle"/>（単一 Area 構成の互換経路）で、
    /// 2 つ以上載っているときは意図的に失敗する。
    /// </summary>
    public static class AreaBundleDirectory
    {
        private static readonly List<AreaRuntimeBundle> Bundles = new List<AreaRuntimeBundle>();

        /// <summary>登録件数（診断・テスト用）。</summary>
        public static int Count => Bundles.Count;

        /// <summary>登録されている束（診断用。順序は登録順）。</summary>
        public static IReadOnlyList<AreaRuntimeBundle> All => Bundles;

        /// <summary>登録する。二重登録はしない。</summary>
        public static void Register(AreaRuntimeBundle bundle)
        {
            if (bundle == null || Bundles.Contains(bundle))
            {
                return;
            }

            Bundles.Add(bundle);
        }

        /// <summary>解除する。自分の分だけを外す。</summary>
        public static void Unregister(AreaRuntimeBundle bundle)
        {
            if (bundle == null)
            {
                return;
            }

            Bundles.Remove(bundle);

            // 活動中に指定されていた束が消えたなら、指定も落とす。
            // 残しておくと「破棄済みの束が現行」という状態で常駐が動く。
            CurrentAreaProvider.ForgetIfCurrent(bundle);
        }

        /// <summary>Scene handle で引く（常駐が読み込んだ Scene を指定して引く経路）。</summary>
        public static bool TryGetByScene(int sceneHandle, out AreaRuntimeBundle bundle)
        {
            bundle = null;
            if (sceneHandle == 0)
            {
                return false;
            }

            for (int i = 0; i < Bundles.Count; i++)
            {
                AreaRuntimeBundle b = Bundles[i];
                if (b != null && b.SceneHandle == sceneHandle)
                {
                    bundle = b;
                    return true;
                }
            }

            return false;
        }

        /// <summary>実体ハンドルで引く（遅れて届いた通知の照合に使う。§4.3）。</summary>
        public static bool TryGetByInstance(AreaInstanceHandle instance, out AreaRuntimeBundle bundle)
        {
            bundle = null;
            if (!instance.IsValid)
            {
                return false;
            }

            for (int i = 0; i < Bundles.Count; i++)
            {
                AreaRuntimeBundle b = Bundles[i];
                if (b != null && b.Instance.Equals(instance))
                {
                    bundle = b;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ちょうど 1 つだけ載っているときにそれを返す（単一 Area 構成の互換経路）。
        ///
        /// P3.5／P4／P5 の Scene は Area が常に 1 つで、遷移を伴わない直開きでは
        /// 常駐が現行の指定を受け取る機会が無い。その場合だけここを使う。
        /// <b>2 つ以上載っているときは失敗する</b>——どちらか選ぶのは常駐の仕事で、
        /// ここで当て推量をすると P5.5 で静かに間違った Area を掴む。
        /// </summary>
        public static bool TryGetSingle(out AreaRuntimeBundle bundle)
        {
            bundle = null;
            if (Bundles.Count != 1)
            {
                return false;
            }

            bundle = Bundles[0];
            return bundle != null;
        }

        /// <summary>テスト間で状態を持ち越さないための掃除（テスト専用）。</summary>
        public static void ClearForTests()
        {
            Bundles.Clear();
        }
    }

    /// <summary>
    /// いま活動している Area の指定（P5.5 §4.3／棚卸し報告書 §2.2 の 3）。
    ///
    /// <b>常駐だけが書く。</b> Scene 側の部品に書かせると、先読みで載った Area が
    /// 自分を現行だと名乗ってしまう。解除は所有者一致（§5.2）。
    ///
    /// Registry の既定列挙（境界越しの索敵・Interact を防ぐ絞り込み）もここを見る。
    /// 指定が無いときは絞り込まない——単一 Area 構成とテストを従来どおり動かすため。
    /// </summary>
    public static class CurrentAreaProvider
    {
        /// <summary>活動中 Area の参照集合（未指定なら null）。</summary>
        public static AreaRuntimeBundle Current { get; private set; }

        /// <summary>いまの指定を入れた者（所有者一致の解除に使う）。</summary>
        public static object Owner { get; private set; }

        /// <summary>活動中 Area の指定があるか。</summary>
        public static bool HasScope => Current != null;

        /// <summary>活動中 Area の Scene handle（未指定なら 0）。</summary>
        public static int ActiveSceneHandle => Current != null ? Current.SceneHandle : 0;

        /// <summary>活動中 Area の実体ハンドル（未指定なら None）。</summary>
        public static AreaInstanceHandle ActiveInstance =>
            Current != null ? Current.Instance : AreaInstanceHandle.None;

        /// <summary>
        /// 活動中 Area を差し替える。<b>所有者を必ず添える。</b>
        /// 別の所有者が差しているときは奪わない（false を返す）。
        /// </summary>
        public static bool TrySetCurrent(object owner, AreaRuntimeBundle bundle)
        {
            if (owner == null)
            {
                return false;
            }

            if (Owner != null && !ReferenceEquals(Owner, owner))
            {
                return false;
            }

            Current = bundle;
            Owner = bundle != null ? owner : null;
            return true;
        }

        /// <summary>自分が差したものだけを外す（所有者一致。§5.2）。</summary>
        public static void ReleaseIfOwner(object owner)
        {
            if (owner != null && ReferenceEquals(Owner, owner))
            {
                Current = null;
                Owner = null;
            }
        }

        /// <summary>
        /// 指定されている束が消えたときに指定を落とす（<see cref="AreaBundleDirectory"/> から呼ばれる）。
        /// 所有者は残さない——破棄済みの束を現行のままにしておくより、
        /// 「未指定」にして互換経路へ落ちるほうが被害が小さい。
        /// </summary>
        internal static void ForgetIfCurrent(AreaRuntimeBundle bundle)
        {
            if (bundle != null && ReferenceEquals(Current, bundle))
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
