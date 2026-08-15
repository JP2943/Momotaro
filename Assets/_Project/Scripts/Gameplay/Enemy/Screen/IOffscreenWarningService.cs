using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Screen
{
    /// <summary>
    /// 画面外射撃の画面端警告サービス契約（Phase3 P3-08。§8.2／§9.2）。画面外の遠距離敵が射撃を開始する前に、対象方向・距離を示す
    /// 仮警告を表示できるかを問い合わせる。Gameplay は Camera／Presentation に直接依存せず、この契約越しに「警告を出せたか」を得て、
    /// 出せない場合は射撃候補から除外する。具象は Presentation（Camera 実装）が提供し、<see cref="OffscreenWarningProvider"/> へ注入する。
    /// </summary>
    public interface IOffscreenWarningService
    {
        /// <summary>
        /// 発射者位置 <paramref name="sourceWorldPos"/>／対象位置 <paramref name="targetWorldPos"/> に対して画面端警告を表示し、
        /// 表示できたら true を返す（＝画面外射撃を許可できる）。
        /// </summary>
        bool TryShowWarning(Vector3 sourceWorldPos, Vector3 targetWorldPos);
    }

    /// <summary>
    /// 画面端警告サービスの提供点（Phase3 P3-08。<see cref="ScreenBoundsProvider"/> と同系統）。Presentation が起動時に注入し、
    /// Gameplay はここから警告を要求する。未注入（Presentation 欠如・テスト）時は「警告を出せない」（false）として扱い、画面外射撃を抑止する。
    /// </summary>
    public static class OffscreenWarningProvider
    {
        /// <summary>現在の警告サービス。未設定時は null。</summary>
        public static IOffscreenWarningService Current { get; set; }

        /// <summary>画面端警告を要求し、表示できたか返す。未注入時は false（画面外射撃不可）。</summary>
        public static bool TryShowWarning(Vector3 sourceWorldPos, Vector3 targetWorldPos)
        {
            IOffscreenWarningService svc = Current;
            return svc != null && svc.TryShowWarning(sourceWorldPos, targetWorldPos);
        }
    }
}
