namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// 道具使用（きびだんご）の入力（P6B 03）。<see cref="IPlayerInput"/> を広げずに足した任意の口——
    /// 既存の入力実装（テストの代替入力を含む）を壊さない。主人公の状態は <c>PlayerInputProvider.Current as IItemUseInput</c> で引く。
    /// </summary>
    public interface IItemUseInput
    {
        /// <summary>使用ボタンの押下エッジを 1 回だけ取り出す（保持では連続しない。非 Gameplay 中の押下は溜めない）。</summary>
        bool ConsumeUseItemPressed();

        /// <summary>未消費の攻撃・ステップ押下があるか（取り出さずに見る。同フレームの既存行動を優先するため）。</summary>
        bool HasPendingActionPress { get; }
    }
}
