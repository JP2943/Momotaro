using Momotaro.Gameplay.Enemy.Threat;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 敵のヘイト・ターゲット選択を実プレイで可視化する診断ビュー（Phase3 P3-06 受入。既定は無効＝オプトイン）。
    /// <see cref="EnemyThreatTracker"/> の読み取り専用プロパティ（現在対象・現在脅威・次回再評価・追跡数）を Development ビルド／
    /// エディタでのみラベル表示する。表示専用で Gameplay に干渉せず、本番挙動を分岐しない（AI 状態の正本化はしない。§2.2）。
    /// 完成 HUD は P3-11 で扱うため、ここでは最小の確認用オーバレイに留める。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyThreatDebugView : MonoBehaviour
    {
        [Tooltip("ヘイト情報を画面へ表示するか（診断用。既定 無効）。")]
        [SerializeField] private bool _display;

        [Tooltip("対象のトラッカー（未指定なら親から取得）。")]
        [SerializeField] private EnemyThreatTracker _tracker;

        private void Awake()
        {
            if (_tracker == null)
            {
                _tracker = GetComponentInParent<EnemyThreatTracker>();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!_display || _tracker == null)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.2f);
            if (sp.z <= 0f)
            {
                return; // カメラ背面。
            }

            string text = "Target=" + _tracker.CurrentTargetId
                + " Threat=" + _tracker.CurrentThreat.ToString("0.0")
                + " Reeval=" + _tracker.TimeToReevaluate.ToString("0.00")
                + "s Tracked=" + _tracker.TrackedCount;
            var rect = new Rect(sp.x - 90f, Screen.height - sp.y - 8f, 200f, 20f);
            GUI.Label(rect, text);
        }
#endif
    }
}
