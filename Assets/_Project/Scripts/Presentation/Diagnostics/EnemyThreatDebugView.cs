using System.Collections.Generic;
using System.Text;
using Momotaro.Gameplay.Enemy.Threat;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 敵のヘイト・ターゲット選択を実プレイで可視化する診断ビュー（Phase3 P3-06 受入。既定は無効＝オプトイン）。
    /// <see cref="EnemyThreatTracker"/> の読み取り専用プロパティだけを Development ビルド／エディタでラベル表示する。
    /// 表示専用で Gameplay に干渉せず、本番挙動を分岐しない（AI 状態の正本化はしない。§2.2）。
    ///
    /// P4-03 受入：現在対象の脅威だけでなく<b>候補全員の脅威を並べて</b>表示する。「犬丸が殴っているのに狙われない」の
    /// ような症状は、現在対象の値だけでは切り分けられない（蓄積していないのか、蓄積が消えているのか、
    /// 蓄積はあるが切替閾値に届いていないのかが見えない）。基礎ヘイトと獲得ヘイトも分けて出す。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyThreatDebugView : MonoBehaviour
    {
        [Tooltip("ヘイト情報を画面へ表示するか（診断用。既定 無効）。")]
        [SerializeField] private bool _display;

        [Tooltip("対象のトラッカー（未指定なら親から取得）。")]
        [SerializeField] private EnemyThreatTracker _tracker;

        /// <summary>表示するか（<see cref="EnemyDebugToggle"/> から一括で切り替える）。</summary>
        public bool Display
        {
            get => _display;
            set => _display = value;
        }

        private void Awake()
        {
            if (_tracker == null)
            {
                _tracker = GetComponentInParent<EnemyThreatTracker>();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly StringBuilder _text = new StringBuilder(160);

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

            int lines = BuildText();
            var rect = new Rect(sp.x - 110f, Screen.height - sp.y - 8f, 260f, 16f * lines + 4f);
            GUI.Label(rect, _text.ToString());
        }

        /// <summary>表示文字列を組み立て、行数を返す（毎フレームの文字列確保を抑えるため StringBuilder を使い回す）。</summary>
        private int BuildText()
        {
            _text.Clear();
            _text.Append("Reeval=").Append(_tracker.TimeToReevaluate.ToString("0.00"))
                 .Append("s Tracked=").Append(_tracker.TrackedCount);

            int lines = 1;
            int currentId = _tracker.CurrentTargetId;
            IReadOnlyList<IThreatTarget> candidates = _tracker.Candidates;
            if (candidates == null || candidates.Count == 0)
            {
                _text.Append("\n(候補なし)");
                return lines + 1;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                IThreatTarget t = candidates[i];
                if (t == null)
                {
                    continue;
                }

                // 現在狙っている対象に印を付ける。基礎ヘイトは減衰せず、獲得ヘイトだけが減衰する（§7.2）ため分けて出す。
                _text.Append('\n')
                     .Append(t.ActorId == currentId ? "> " : "  ")
                     .Append(NameOf(t))
                     .Append(' ')
                     .Append(_tracker.ThreatOf(t).ToString("0.0"))
                     .Append(" (base ").Append(t.BaseThreat.ToString("0"))
                     .Append(" + acq ").Append(_tracker.AcquiredOf(t).ToString("0.0")).Append(')');
                lines++;
            }

            return lines;
        }

        private static string NameOf(IThreatTarget target)
        {
            return target is Component component ? component.transform.root.name : target.ActorId.ToString();
        }
#endif
    }
}
