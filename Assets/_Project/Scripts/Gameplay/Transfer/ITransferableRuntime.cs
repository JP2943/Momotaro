namespace Momotaro.Gameplay.Transfer
{
    /// <summary>
    /// エリア遷移で<b>値を持ち越す Runtime</b> の印（P5-03a。仕様書 v1.1 §4.5）。
    ///
    /// この印を付けた型は、持ち越し台帳 <c>P5_ActorTransferInventory.md</c> に 1 行以上の分類を持たなければならない。
    /// 受入テスト P5-E28 が Gameplay アセンブリを<b>反射で走査</b>して台帳と双方向に照合するため、
    /// 「台帳にあるのに型が無い」「型があるのに台帳に無い」のどちらも失敗する（裁定 1）。
    /// 対象型リストを手書きしないのは、リストの更新漏れでテストが嘘をつくのを防ぐため。
    ///
    /// <b>この印だけでは何も持ち越さない。</b> 実際の値の出し入れは
    /// <see cref="ITransferableRuntime{TSnapshot}"/> が定める 1 組の API で行う。
    /// </summary>
    public interface ITransferableRuntime
    {
    }

    /// <summary>
    /// 不変 Snapshot 1 種と Export／Import 1 組（§4.5 の「状態を所有する単位につき 1 組」）。
    ///
    /// <b>Export の前提。</b> 中断で CD が始まる行動は、<b>中断が完了したあと</b>に採取する（§4.4）。
    /// 採取そのものは行動を止めないので、停止は呼び出し側（遷移手順 §6.2 の 4 → 5）の責任。
    ///
    /// <b>Import の前提。</b> 対象の初期化／停止期間だけ許可する。実装は値域を検証し、
    /// NaN・Infinity・負の残り時間・HP と Down の矛盾を<b>部分適用せずに</b>拒否する。
    /// Import 自体から新しい被弾・回復・守護成立・報酬・SE／VFX を発行しない（表示の状態同期は可）。
    /// </summary>
    /// <typeparam name="TSnapshot">この単位の不変 Snapshot。</typeparam>
    public interface ITransferableRuntime<TSnapshot> : ITransferableRuntime
        where TSnapshot : struct
    {
        /// <summary>現在の値を不変 Snapshot へ採取する。採取は対象の状態を変えない。</summary>
        TSnapshot ExportTransferSnapshot();

        /// <summary>
        /// Snapshot を適用する。値域が不正なら<b>何も変えずに</b> false を返す（§4.5）。
        /// 失敗は遷移の失敗として §6.3 へ戻す。不正値を黙って全回復に置換しない。
        /// </summary>
        bool TryImportTransferSnapshot(in TSnapshot snapshot);
    }
}
