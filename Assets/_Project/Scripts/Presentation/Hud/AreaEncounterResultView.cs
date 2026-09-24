using Momotaro.Gameplay.Encounter;
using UnityEngine;

namespace Momotaro.Presentation.Hud
{
    /// <summary>
    /// 戦闘の結果の短文を<b>実際に画面へ出す</b>仮表示（P5-07。仕様書 v1.1 §8.4 手順 8）。
    ///
    /// §8.4 は「短文『戦闘終了』を表示する。結果パネルや Enter 待ちで止めない」と言っている。
    /// <b>値を公開しただけでは表示したことにならない</b>（遷移の Error 表示で同じ指摘を受けた）。
    /// 見た目は仮でよいので、出る・消えるところまで動く形で置く。
    ///
    /// <b>IMGUI を使う。</b> Canvas もフォント資産も要らないので、素材が揃っていなくても必ず出る。
    /// 時間は <b>unscaled</b> で数える（ヒットストップ中も消えていく）。
    ///
    /// 正式な見た目は P10b。ここでの契約は <see cref="IsShowing"/>／<see cref="Message"/> の 2 つで、
    /// 見た目が変わってもこの 2 つは残す（テストはここを見る）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaEncounterResultView : MonoBehaviour
    {
        [Tooltip("結果の通知元。")]
        [SerializeField] private AreaEncounterRunner _runner;

        [Tooltip("短文を出しておく秒数（unscaled）。0 以下なら出さない。")]
        [SerializeField] private float _showSeconds = 2.5f;

        private bool _subscribed;
        private float _remaining;

        /// <summary>いま出している短文（出していなければ空）。</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>いま出しているか。</summary>
        public bool IsShowing => _remaining > 0f && !string.IsNullOrEmpty(Message);

        /// <summary>出した回数（診断・テスト用）。</summary>
        public int ShowCount { get; private set; }

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _runner != null;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(AreaEncounterRunner runner)
        {
            Unsubscribe();

            if (runner != null)
            {
                _runner = runner;
            }

            Subscribe();
        }

        /// <summary>時間を進める（unscaled 前提。テストは決定的に直接呼べる）。</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (_remaining <= 0f)
            {
                return;
            }

            _remaining -= unscaledDeltaTime < 0f ? 0f : unscaledDeltaTime;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                Message = string.Empty;
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();

            // 次の Scene へ出しっぱなしを持ち越さない。
            _remaining = 0f;
            Message = string.Empty;
        }

        private void Subscribe()
        {
            if (_subscribed || _runner == null)
            {
                return;
            }

            _runner.ResultAnnounced += OnResultAnnounced;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _runner == null)
            {
                return;
            }

            _runner.ResultAnnounced -= OnResultAnnounced;
            _subscribed = false;
        }

        private void OnResultAnnounced(string message)
        {
            if (string.IsNullOrEmpty(message) || _showSeconds <= 0f)
            {
                return;
            }

            Message = message;
            _remaining = _showSeconds;
            ShowCount++;
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
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

            float w = 320f;
            float h = 60f;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.22f, w, h);
            GUI.Label(rect, Message, style);
        }
    }
}
