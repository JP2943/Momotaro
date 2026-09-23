using Momotaro.Core.Logging;
using UnityEngine;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// 遷移の終端失敗を<b>プレイヤーへ見せる仮の表示</b>（仕様書 v1.1 §6.3 の最終行）。
    ///
    /// §6.3 は「Error 表示に留め、既存 Launcher へ戻る操作を提示する」と言っている。
    /// <b>API を生やしただけでは提示したことにならない</b>ので、見た目は仮でも
    /// 「理由が出る」「押すと戻る」までを実際に動く形で置く（GPT レビュー R3）。
    ///
    /// <b>常駐に置く。</b> 失敗したときは Scene 側が壊れている（AreaContext が無い・
    /// 到着していない）のが普通なので、Scene に置いた表示では出せない。
    ///
    /// <b>IMGUI を使う。</b> Canvas・Prefab・フォント資産を必要としないので、
    /// どの Scene に居ても・素材が無くても必ず出る。暗転と Error UI は unscaled で動かす決まりだが、
    /// IMGUI は Gameplay 時計とは無関係なので、時計を止めたままでも描画も入力も生きている。
    ///
    /// <b>正式版は P5-06（カメラ・画面まわり）で置き換える。</b> ここでの契約は
    /// <see cref="IsShowing"/>／<see cref="Message"/>／<see cref="TryPressReturn"/> の 3 つで、
    /// 見た目が変わってもこの 3 つは残す（テストはここを見る）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTransitionFailureView : MonoBehaviour
    {
        private AreaTransitionService _service;

        /// <summary>表示の元になるサービスを差す（常駐の組み立て時に 1 度だけ）。</summary>
        public void Bind(AreaTransitionService service)
        {
            _service = service;
        }

        /// <summary>いま Error 表示を出しているか。</summary>
        public bool IsShowing => _service != null && _service.HasTerminalFailure;

        /// <summary>出している理由（出していなければ空）。</summary>
        public string Message => IsShowing ? (_service.TerminalFailureReason ?? string.Empty) : string.Empty;

        /// <summary>戻る操作をいま押せるか（生きているロードが終端するまでは押せない）。</summary>
        public bool CanPressReturn => _service != null && _service.CanReturnToLauncher;

        /// <summary>戻る操作を押した回数（診断・テスト用）。</summary>
        public int PressCount { get; private set; }

        /// <summary>
        /// 戻る操作を押す。押せない状態では何もしない。
        /// テストはボタンの座標を叩かずにここを呼ぶ（見た目が変わっても検査が壊れない）。
        /// </summary>
        public bool TryPressReturn()
        {
            if (!CanPressReturn)
            {
                return false;
            }

            PressCount++;
            bool started = _service.TryBeginReturnToLauncher();
            if (!started)
            {
                GameLog.Warning(LogCategory.Scene, "Return to launcher could not be started.");
            }

            return started;
        }

        private void OnGUI()
        {
            if (!IsShowing)
            {
                return;
            }

            const float width = 520f;
            const float height = 170f;
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUI.Box(area, "エリアの読み込みに失敗しました");
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 32f, area.width - 32f, area.height - 48f));
            GUILayout.Label(Message);
            GUILayout.FlexibleSpace();

            // 生きているロードが終端するまでは押せない（重ねて読み込ませない。§6.3）。
            GUI.enabled = CanPressReturn;
            if (GUILayout.Button(_service.IsReturningToLauncher ? "戻っています…" : "Launcher へ戻る", GUILayout.Height(28f)))
            {
                TryPressReturn();
            }

            GUI.enabled = true;
            GUILayout.EndArea();
        }
    }
}
