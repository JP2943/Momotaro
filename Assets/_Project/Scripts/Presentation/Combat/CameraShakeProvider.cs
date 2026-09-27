namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 常駐する画面揺れの提供点（P5.5 付録 A.1／A.2）。
    ///
    /// <b>揺れは常駐 Rig の Camera 子に付く。</b> Area Scene は Camera を持たないので（付録 A.2）、
    /// Scene 構築時に <see cref="CombatFeedbackPresenter"/> へ直接配線できない。
    /// 常駐側だけがここへ自分を差し、演出の調停役は<b>自分の配線が空のときだけ</b>ここを見る。
    ///
    /// <b>所有者一致で解除する</b>（§5.2）。別の所有者が差しているものは奪えない。
    /// 先読みで載った Area が現行の揺れを差し替えることを防ぐ——もっとも Scene 側からは
    /// 差さない設計なので、これは二重の歯止めである。
    ///
    /// P3.5 の試遊 Scene は従来どおり Scene 内の直接配線を使うので、この提供点を通らない
    /// （付録 A.2「P3.5／P4／従来 P5 互換モードの Scene は一括変更しない」）。
    /// </summary>
    public static class CameraShakeProvider
    {
        /// <summary>いまの常駐揺れ（無ければ null）。</summary>
        public static CameraShakePresenter Current { get; private set; }

        /// <summary>差した者（所有者一致の解除に使う）。</summary>
        public static object Owner { get; private set; }

        /// <summary>差さっているか。</summary>
        public static bool HasOwner => Current != null;

        /// <summary>差す。別の所有者が差しているときは奪わない。</summary>
        public static bool TrySetCurrent(object owner, CameraShakePresenter shake)
        {
            if (owner == null || shake == null)
            {
                return false;
            }

            if (Owner != null && !ReferenceEquals(Owner, owner))
            {
                return false;
            }

            Current = shake;
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
