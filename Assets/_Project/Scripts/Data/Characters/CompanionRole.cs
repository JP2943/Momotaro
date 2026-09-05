namespace Momotaro.Data.Characters
{
    /// <summary>
    /// 仲間の役割（P4-01）。<see cref="EnemyRole"/> と同じく Data 層の語彙として定義し、ヘイト補正・戦闘上の役割差・
    /// 表示（仮素材の色相と識別ラベル）の切り分けに用いる。P4 では犬丸のみを実装し、猿・雉は P8 で展開する。
    /// </summary>
    public enum CompanionRole
    {
        /// <summary>犬（犬丸）。前衛。敵のヘイトを引き受けやすく、守護／「かばう」の主役。</summary>
        Dog = 0,

        /// <summary>猿。機動・妨害（P8）。</summary>
        Monkey = 1,

        /// <summary>雉。遠距離・偵察（P8）。</summary>
        Pheasant = 2,
    }
}
