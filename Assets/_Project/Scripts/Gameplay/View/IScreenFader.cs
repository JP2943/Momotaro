namespace Momotaro.Gameplay.View
{
    /// <summary>
    /// 画面フェードの抽象。0 で透明（見える）、1 で暗転。SceneFlow がフェードを制御し、
    /// 具体的な描画実装は Presentation 層が提供する。Phase 0 では <see cref="NullScreenFader"/> を既定とする。
    /// </summary>
    public interface IScreenFader
    {
        /// <summary>暗転の度合い（0=透明, 1=暗転）。</summary>
        float Alpha { get; set; }
    }
}
