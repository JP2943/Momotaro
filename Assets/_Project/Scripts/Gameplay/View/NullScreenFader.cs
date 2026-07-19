using UnityEngine;

namespace Momotaro.Gameplay.View
{
    /// <summary>
    /// 描画を伴わない既定のフェード実装。値の保持のみ行う（Phase 0 用）。
    /// Presentation 層が Canvas ベースの実装を用意するまでのプレースホルダ。
    /// </summary>
    public sealed class NullScreenFader : IScreenFader
    {
        private float _alpha;

        /// <inheritdoc />
        public float Alpha
        {
            get => _alpha;
            set => _alpha = Mathf.Clamp01(value);
        }
    }
}
