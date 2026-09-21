namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// 「調べる」（Interact）入力の読み取り契約（P4-07B。v1.0 §4.2「押下 1 回で 1 依頼」・§12「既存 Interact を 1 回消費」）。
    /// <see cref="IPlayerInput"/> と分けているのは、既存の主人公入力の契約と Fake を増やさないため。
    /// 押下エッジをラッチし、消費した側だけが 1 回受け取る（Step／Attack と同じ形）。
    /// </summary>
    public interface IInteractInput
    {
        /// <summary>未消費の押下エッジがあるか（消費はしない）。</summary>
        bool InteractPressed { get; }

        /// <summary>押下エッジを 1 回消費する。無ければ false。</summary>
        bool ConsumeInteractPressed();

        /// <summary>押下エッジを実行せずに捨てる（対象が無かったときに、古い押下を次の対象へ持ち越さない）。</summary>
        void DiscardInteractPressed();
    }
}
