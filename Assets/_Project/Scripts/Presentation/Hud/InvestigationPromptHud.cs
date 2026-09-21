using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Companion;
using UnityEngine;
using UnityEngine.UI;

namespace Momotaro.Presentation.Hud
{
    /// <summary>
    /// 探索の短文 UI（P4-07B／P4-08R。v1.0 §4.1・§11）。Screen Space Canvas に 3 行だけ出す。
    ///
    /// - 案内行（画面下中央）：いま Interact したらどうなるか（「E / 南ボタン：調べる」、未加入ヒント、調査済み）。
    ///   調停役の <see cref="InvestigationCoordinator.Peek"/> を 1 フレーム 1 回だけ呼び、結果を地点マーカーへも配る。
    /// - 通知行（案内行の上）：受理・発見・拒否・中断の短文を数秒だけ出す（型付き通知 <see cref="IInvestigationListener"/> を購読）。
    /// - 開始行（画面上中央）：試遊段階が戦闘を始めていない間だけ「Enter / Start：戦闘を始める」。
    ///
    /// Gameplay へは一切書かない。Canvas は自分の子に一度だけ組む（<see cref="CombatPlayHud"/> と同じ作り）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvestigationPromptHud : MonoBehaviour, IInvestigationListener
    {
        [Tooltip("探索の調停役（Peek と通知の購読先）。")]
        [SerializeField] private InvestigationCoordinator _coordinator;

        [Tooltip("試遊段階（無い Scene では開始行を出さない）。")]
        [SerializeField] private TrialStageController _stage;

        [Tooltip("候補の結果を配る地点マーカー。")]
        [SerializeField] private InvestigationPointMarker[] _markers = System.Array.Empty<InvestigationPointMarker>();

        [Tooltip("通知行を表示し続ける時間（秒）。")]
        [SerializeField, Min(0.1f)] private float _messageSeconds = 2.0f;

        private bool _built;
        private Font _font;
        private Text _promptText;
        private Text _messageText;
        private Text _startText;
        private float _messageRemaining;
        private InvestigationCoordinator _subscribed;

        /// <summary>案内行の現在文（テスト用。空なら非表示）。</summary>
        public string PromptLine { get; private set; } = string.Empty;

        /// <summary>通知行の現在文（テスト用。空なら非表示）。</summary>
        public string MessageLine { get; private set; } = string.Empty;

        /// <summary>開始行の現在文（テスト用。空なら非表示）。</summary>
        public string StartLine { get; private set; } = string.Empty;

        /// <summary>直近の Peek 結果（テスト・診断用）。</summary>
        public InvestigationRejectReason LastPeekReason { get; private set; }

        /// <summary>配線された調停役（Scene 検査用）。</summary>
        public InvestigationCoordinator Coordinator => _coordinator;

        /// <summary>配線された試遊段階（Scene 検査用）。</summary>
        public TrialStageController Stage => _stage;

        /// <summary>配線されたマーカー（Scene 検査用）。</summary>
        public IReadOnlyList<InvestigationPointMarker> Markers => _markers;

        /// <summary>参照を注入する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(InvestigationCoordinator coordinator, TrialStageController stage, InvestigationPointMarker[] markers)
        {
            if (coordinator != null)
            {
                _coordinator = coordinator;
                if (isActiveAndEnabled)
                {
                    Subscribe();
                }
            }

            if (stage != null)
            {
                _stage = stage;
            }

            if (markers != null)
            {
                _markers = markers;
            }
        }

        private void OnEnable()
        {
            EnsureBuilt();
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>1 フレームぶんの更新（LateUpdate から呼ばれるが、テストは決定的に直接呼べる）。</summary>
        public void Tick(float deltaTime)
        {
            if (_messageRemaining > 0f)
            {
                _messageRemaining -= deltaTime;
                if (_messageRemaining <= 0f)
                {
                    _messageRemaining = 0f;
                    MessageLine = string.Empty;
                }
            }

            Refresh();
        }

        /// <summary>案内・開始行を調停役と段階から作り直し、候補をマーカーへ配る。</summary>
        public void Refresh()
        {
            EnsureBuilt();

            // 案内行：1 フレーム 1 回の Peek。
            IInvestigationPoint candidate = null;
            InvestigationRejectReason reason = InvestigationRejectReason.NotWired;
            if (_coordinator != null)
            {
                reason = _coordinator.Peek(out candidate);
            }

            LastPeekReason = reason;
            PromptLine = BuildPrompt(candidate, reason);

            for (int i = 0; i < _markers.Length; i++)
            {
                InvestigationPointMarker marker = _markers[i];
                if (marker == null)
                {
                    continue;
                }

                bool isCandidate = candidate != null && ReferenceEquals(marker.Point, candidate);
                marker.SetCandidate(isCandidate, reason, isCandidate ? PointText(candidate, reason) : string.Empty);
            }

            // 開始行：明示開始の前だけ。
            StartLine = _stage != null && !_stage.EncounterRequested ? InvestigationTexts.CombatStartPrompt : string.Empty;

            Apply(_promptText, PromptLine);
            Apply(_messageText, MessageLine);
            Apply(_startText, StartLine);
        }

        private static string BuildPrompt(IInvestigationPoint candidate, InvestigationRejectReason reason)
        {
            if (candidate == null)
            {
                return string.Empty; // 対象なし・入力閉鎖・未配線・戦闘中は案内を出さない（既存操作のまま）。
            }

            if (reason == InvestigationRejectReason.None)
            {
                return InvestigationTexts.Prompt(candidate.Prompt);
            }

            return InvestigationTexts.Reject(reason, PointText(candidate, reason));
        }

        private static string PointText(IInvestigationPoint point, InvestigationRejectReason reason)
        {
            switch (reason)
            {
                case InvestigationRejectReason.CompanionNotRecruited: return point.MissingCompanionHint;
                case InvestigationRejectReason.AlreadyInvestigated: return point.CompletedText;
                default: return string.Empty;
            }
        }

        private void ShowMessage(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            MessageLine = text;
            _messageRemaining = _messageSeconds;
            Apply(_messageText, MessageLine);
        }

        /// <inheritdoc />
        public void OnInvestigationAccepted(in InvestigationAccepted accepted)
        {
            ShowMessage(InvestigationTexts.Accepted);
        }

        /// <inheritdoc />
        public void OnInvestigationRejected(in InvestigationRejected rejected)
        {
            ShowMessage(InvestigationTexts.Reject(rejected.Reason, rejected.Text));
        }

        /// <inheritdoc />
        public void OnInvestigationCompleted(in InvestigationCompleted completed)
        {
            ShowMessage(InvestigationTexts.Completed);
        }

        /// <inheritdoc />
        public void OnInvestigationInterrupted(in InvestigationInterrupted interrupted)
        {
            ShowMessage(InvestigationTexts.Interrupt(interrupted.Reason));
        }

        private static void Apply(Text target, string text)
        {
            if (target == null)
            {
                return;
            }

            if (target.text != text)
            {
                target.text = text;
            }

            bool show = !string.IsNullOrEmpty(text);
            if (target.enabled != show)
            {
                target.enabled = show;
            }
        }

        /// <summary>Canvas と 3 行を一度だけ組む（二度目以降は何もしない）。</summary>
        public void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            _font = ResolveUiFont();

            var canvasGo = new GameObject("InvestigationHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 101; // 戦闘 HUD（100）と同じ層の少し前。重ならない位置に置く。

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var root = (RectTransform)canvasGo.transform;
            _promptText = NewText("PromptText", root, new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(900f, 40f), 26);
            _messageText = NewText("MessageText", root, new Vector2(0.5f, 0f), new Vector2(0f, 144f), new Vector2(900f, 40f), 24);
            _messageText.color = new Color(1f, 0.95f, 0.6f, 1f);
            _startText = NewText("StartText", root, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(900f, 36f), 22);
            _startText.color = new Color(0.8f, 0.9f, 1f, 1f);
        }

        private Text NewText(string name, RectTransform parent, Vector2 anchor, Vector2 anchoredPos, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.enabled = false;
            return t;
        }

        private static Font ResolveUiFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null)
            {
                f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            if (f == null)
            {
                f = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Verdana", "Segoe UI", "sans-serif" }, 16);
            }

            return f;
        }

        private void Subscribe()
        {
            if (ReferenceEquals(_subscribed, _coordinator))
            {
                return;
            }

            Unsubscribe();
            _subscribed = _coordinator;
            _subscribed?.Events.AddListener(this);
        }

        private void Unsubscribe()
        {
            _subscribed?.Events.RemoveListener(this);
            _subscribed = null;
        }
    }
}
