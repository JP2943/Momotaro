using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Presentation.Hud
{
    /// <summary>
    /// 死亡時の再開操作を<b>実際に画面へ出す</b>仮表示（P5-08。仕様書 v1.1 §9.1／§11）。
    ///
    /// 表示ラベルは<b>「再開する」</b>（§9.1 の 1 行目）。既存試遊版の「Retry」とは別物で、
    /// あちらは Scene をまるごと初期化する。同じ言葉を使うと、試しに触った人が
    /// 「徳が消えるやり直し」だと思ってしまう。
    ///
    /// <b>IMGUI を使う。</b> Canvas もフォント資産も要らないので、素材が揃っていなくても必ず出る。
    /// 正式な見た目は P10b。ここでの契約は <see cref="IsShowing"/>／<see cref="Message"/> の 2 つ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampaignRespawnView : MonoBehaviour
    {
        /// <summary>再開操作のラベル（§9.1 の 1 行目）。正本は <see cref="CampaignRespawnLabels"/>。</summary>
        public const string RespawnLabel = CampaignRespawnLabels.Respawn;

        /// <summary>読込に失敗して再試行できるときの短文（§9.1 末尾）。正本は同上。</summary>
        public const string RetryLabel = CampaignRespawnLabels.Retry;

        [Tooltip("死亡再開の実行役。")]
        [SerializeField] private CampaignRespawnRunner _runner;

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _runner != null;

        /// <summary>いま出している短文（出していなければ空）。</summary>
        public string Message =>
            _runner == null ? string.Empty : CampaignRespawnLabels.ForPhase(_runner.Phase);

        /// <summary>いま出しているか。</summary>
        public bool IsShowing => !string.IsNullOrEmpty(Message);

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(CampaignRespawnRunner runner)
        {
            if (runner != null)
            {
                _runner = runner;
            }
        }

        private void OnGUI()
        {
            if (!IsShowing)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
            };
            style.normal.textColor = Color.white;

            float w = 520f;
            float h = 60f;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.45f, w, h);
            GUI.Label(rect, Message, style);
        }
    }
}
