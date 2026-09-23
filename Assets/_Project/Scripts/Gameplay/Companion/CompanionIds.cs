using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の安定 ID（P5-03b）。Data の <c>SO_Companion_Inumaru</c> と同じ値を持つ。
    ///
    /// 文字列を各所へ散らすと綴り違いが静かに通るので、参照する側はここを使う。
    /// 猿・雉は P8 で足す（未使用の語彙を先回りして置かない）。
    /// </summary>
    public static class CompanionIds
    {
        /// <summary>犬丸。</summary>
        public static readonly StableId Inumaru = new StableId("companion_inumaru");
    }
}
