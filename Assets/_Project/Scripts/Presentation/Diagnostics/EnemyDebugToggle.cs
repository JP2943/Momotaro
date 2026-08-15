using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// シーン内の全 <see cref="EnemyAiDebugOverlay"/> のデバッグ表示を一括で ON/OFF する検証用スイッチ（Phase3 P3-11。§「Development 限定で
    /// …切替表示」「デバッグ表示の ON/OFF 方法」）。Play 中はコンポーネント右クリックのコンテキストメニューから切替でき、Input に依存しない。
    /// 表示専用で Gameplay を分岐しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyDebugToggle : MonoBehaviour
    {
        [Tooltip("開始時にデバッグ表示を有効化するか。")]
        [SerializeField] private bool _enabledOnStart = false;

        private void Start()
        {
            SetAll(_enabledOnStart);
        }

        /// <summary>全オーバレイのデバッグ表示を有効化する。</summary>
        [ContextMenu("Debug Overlays / ON")]
        public void EnableAll() => SetAll(true);

        /// <summary>全オーバレイのデバッグ表示を無効化する。</summary>
        [ContextMenu("Debug Overlays / OFF")]
        public void DisableAll() => SetAll(false);

        /// <summary>全 <see cref="EnemyAiDebugOverlay"/> の表示を切り替える（非アクティブも含む）。</summary>
        public void SetAll(bool display)
        {
            EnemyAiDebugOverlay[] overlays =
                Object.FindObjectsByType<EnemyAiDebugOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < overlays.Length; i++)
            {
                if (overlays[i] != null)
                {
                    overlays[i].Display = display;
                }
            }
        }
    }
}
