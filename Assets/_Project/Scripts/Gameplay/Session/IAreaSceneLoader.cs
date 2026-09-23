namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// エリア Scene を読み込む狭い契約（P5-03b 修正。GPT レビュー R1）。
    ///
    /// <b>本番の読込もこの口を通る。</b> 以前はテスト専用の差し替え口（<c>LoadOverride</c>）を
    /// 本番クラスへ生やしていたが、それだと「テストのときだけ通る別経路」ができてしまい、
    /// 開始失敗・例外・遅延完了を本番と同じ経路で検査できない。
    /// 実装を丸ごと差し替える形にすれば、検査対象の経路は 1 本のままになる。
    ///
    /// Gameplay は UnityEngine の Scene API を触らないので、実装は Infrastructure に置く。
    /// </summary>
    public interface IAreaSceneLoader
    {
        /// <summary>
        /// 読み込みを開始する。<b>開始に失敗しても例外を投げず</b>、
        /// <see cref="IAreaLoadOperation.HasError"/> が true の操作を返す（失敗も同じ経路で観測する）。
        /// </summary>
        IAreaLoadOperation Load(string scenePath);
    }
}
