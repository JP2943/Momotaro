namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 追加読込・撤去の観測窓（P5.5 §2／§5）。
    ///
    /// <b>P5 の <see cref="IAreaSceneLoader"/> はそのまま残す。</b> あちらは
    /// <c>LoadSceneMode.Single</c> 前提で「読み終わったか・失敗したか」しか返さない。
    /// P5.5 は Area を 2 つ同時に載せるので、<b>どの Scene を読んだのか</b>と
    /// <b>撤去</b>が必要になる。既存の口を広げず別の口にしたのは、
    /// P5 の遷移経路（Fade・Single）を 1 行も変えずに残すため（§1.2 と同じ考え方）。
    /// </summary>
    public interface IAreaSceneOperation
    {
        /// <summary>終わったか（成功・失敗を問わない）。</summary>
        bool IsDone { get; }

        /// <summary>失敗したか。<b>開始に失敗した場合もここで観測する</b>（例外は投げない）。</summary>
        bool HasError { get; }

        /// <summary>
        /// 読み込んだ Scene の handle（読み終わるまで 0。撤去では常に 0）。
        /// <b>path ではなく handle で持つ</b>——同じ path を 2 度読む往復があるため（§4.3）。
        /// </summary>
        int SceneHandle { get; }
    }

    /// <summary>
    /// Area Scene の追加読込・撤去を行う狭い契約（P5.5 §2）。
    ///
    /// Gameplay は UnityEngine の Scene API を触らないので、実装は Infrastructure に置く。
    /// <b>直列化はここでは持たない。</b> 「同時に 1 つだけ」は <see cref="AreaResidencyLedger"/> の仕事で、
    /// 両方に持たせると「どちらが断ったのか」が実行時まで分からなくなる。
    /// </summary>
    public interface IAreaSceneHost
    {
        /// <summary>
        /// 追加で読み込む。<b>開始に失敗しても例外を投げず</b>、
        /// <see cref="IAreaSceneOperation.HasError"/> が true の操作を返す。
        /// </summary>
        IAreaSceneOperation LoadAdditive(string scenePath);

        /// <summary>
        /// 読み込んだ Scene を撤去する。開始に失敗しても例外を投げない。
        /// <b>handle で指定する</b>——同じ path の別インスタンスを取り違えないため。
        /// </summary>
        IAreaSceneOperation Unload(int sceneHandle);
    }
}
