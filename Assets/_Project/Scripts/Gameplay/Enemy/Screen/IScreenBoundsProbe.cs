using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Screen
{
    /// <summary>
    /// 画面内判定アダプタの契約（Phase3 §8.2）。Gameplay は Camera API へ直接依存せず、この契約越しに「ある World 座標が
    /// 画面内（境界余白込み）か」を問い合わせる。具象は Presentation（Camera 実装）が提供し、<see cref="ScreenBoundsProvider"/>
    /// へ注入する。テストでは Fake を注入して境界・余白・振動防止を決定的に検証する。余白（Data 化）は実装側が保持する。
    /// </summary>
    public interface IScreenBoundsProbe
    {
        /// <summary>指定 World 座標が画面内（境界余白込み・カメラ前方）か。</summary>
        bool IsOnScreen(Vector3 worldPos);
    }
}
