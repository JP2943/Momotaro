using UnityEditor;
using UnityEngine;

namespace Momotaro.EditorBridge
{
    /// <summary>
    /// Editor 常駐ブリッジの入り切り（開発補助）。既定は無効で、ここで明示的に有効化するまで
    /// <see cref="EditorBridgeService"/> は何もしない（受け渡しフォルダも作らない）。
    /// </summary>
    public static class EditorBridgeMenu
    {
        private const string EnabledPath = "Momotaro/Bridge/Enabled";
        private const string RevealPath = "Momotaro/Bridge/Reveal Bridge Folder";
        private const string ResetPath = "Momotaro/Bridge/Reset Busy Flag";

        [MenuItem(EnabledPath)]
        private static void ToggleEnabled()
        {
            EditorBridgeService.Enabled = !EditorBridgeService.Enabled;
            Debug.Log("[EditorBridge] " + (EditorBridgeService.Enabled ? "有効にしました。" : "無効にしました。")
                + " 受け渡しフォルダ: " + EditorBridgePaths.Root);
        }

        [MenuItem(EnabledPath, true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(EnabledPath, EditorBridgeService.Enabled);
            return true;
        }

        [MenuItem(ResetPath)]
        private static void ResetBusy()
        {
            // 実行中の印が何らかの理由で残ってしまった場合の抜け道（残るとコマンドを受け付けなくなる）。
            EditorBridgeService.ResetBusy();
            Debug.Log("[EditorBridge] 実行中の印を落としました。次のコマンドを受け付けます。");
        }

        [MenuItem(RevealPath)]
        private static void Reveal()
        {
            EditorBridgePaths.EnsureFolder();
            EditorUtility.RevealInFinder(EditorBridgePaths.Root);
        }
    }
}
