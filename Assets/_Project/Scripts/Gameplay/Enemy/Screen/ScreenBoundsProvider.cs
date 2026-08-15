using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Screen
{
    /// <summary>
    /// 画面内判定アダプタの提供点（Phase3 §8.2）。Gameplay は Presentation（Camera 実装）を参照できないため、Presentation 側が
    /// 起動時にここへ <see cref="IScreenBoundsProbe"/> を注入し、Gameplay はここから読む（<see cref="GameModeProvider"/> と同系統）。
    /// 未注入（Presentation 欠如・単体テスト）時は画面内とみなして Gameplay を進行させる（§2.3）。
    /// </summary>
    public static class ScreenBoundsProvider
    {
        /// <summary>現在の画面内判定アダプタ。未設定時は null。</summary>
        public static IScreenBoundsProbe Current { get; set; }

        /// <summary>
        /// 指定座標が画面内か。アダプタ未設定時は true（画面内扱いで進行）。Gameplay 側の共通入口。
        /// </summary>
        public static bool IsOnScreen(Vector3 worldPos)
        {
            IScreenBoundsProbe probe = Current;
            return probe == null || probe.IsOnScreen(worldPos);
        }
    }
}
