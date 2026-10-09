using UnityEngine;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// 実ビルド確認（<c>-p6a-smoke play</c> の切り分け）専用：お地蔵様のメニューと同じ大きさ・同じ文字を IMGUI で数フレーム描く。
    /// 最初の休息で 1 フレーム止まる原因が「メニューの初回描画（文字の準備）」かを分けるために使う。試遊の通常起動では置かれない。
    /// </summary>
    public sealed class Phase6SmokeGuiProbe : MonoBehaviour
    {
        /// <summary>お地蔵様のメニュー・操作案内・保存表示に出る文字。</summary>
        public const string Text =
            "お地蔵様：A のお地蔵様 C のお地蔵様（ボス前） 徳 0／きびだんご 3／3 ▶ 休息（全回復・きびだんご補充・普通敵が戻る） " +
            "成長 旅立ち 閉じる 0123456789 ↑↓／十字キー：選ぶ　決定：A（×）／Enter　戻る：B（○）／Esc 保存中… 保存できていません";

        private readonly PadMenuNavigator _navigator = new PadMenuNavigator();

        /// <summary>描いたフレーム数。</summary>
        public int DrawnFrames { get; private set; }

        private void OnGUI()
        {
            _navigator.BeginScaled();
            try
            {
                GUI.Box(new Rect(20f, 20f, 600f, 200f), Text);
                GUILayout.BeginArea(new Rect(30f, 50f, 580f, 160f));
                GUILayout.Label(Text);
                GUILayout.Button(Text, GUILayout.Height(30f));
                GUILayout.EndArea();
            }
            finally
            {
                _navigator.EndScaled();
            }

            if (Event.current.type == EventType.Repaint)
            {
                DrawnFrames++;
            }
        }
    }
}
