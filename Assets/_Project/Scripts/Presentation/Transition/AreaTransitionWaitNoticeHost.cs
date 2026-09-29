using Momotaro.Gameplay.Session;
using UnityEngine;
using UnityEngine.UI;

namespace Momotaro.Presentation.Transition
{
    /// <summary>
    /// 待ち表示（「読み込み中」）を<b>常駐で</b>持つ担当（P5.5 仕様書 §5。工程 P55-09a）。
    ///
    /// <b>常駐 Rig と同じ物体に住む。</b> 待ちは出発 Scene が撤去される前後にまたがりうるし、
    /// Camera・表示代理と同じ寿命・同じ唯一性で管理したい（§11 の P12／P14）。
    /// <see cref="Cameras.AreaCameraRigHost"/> が自分の物体へこれを足すので、
    /// Prefab 側の配線も Builder の変更も要らない。
    ///
    /// <b>控えめであること・全画面を黒くしないこと</b>が §5 の要件である。だから
    /// <list type="bullet">
    /// <item><description>背景の板を<b>一枚も置かない</b>（暗幕を作らない）。字だけを出す。</description></item>
    /// <item><description>置くのは画面の<b>右下の隅</b>で、面積は画面の数パーセントに収める。</description></item>
    /// <item><description>占める面積は <see cref="ScreenAreaFraction"/> で数として言える——
    /// 「控えめ」を見た目の言葉で終わらせない。</description></item>
    /// </list>
    ///
    /// <b>0.3 秒の境目はここで計らない。</b> 規則は
    /// <see cref="AreaTransitionWaitNoticeTimer"/> が持つ。表示側が自分で時間を計ると、
    /// 同じ規則が 2 か所に散る（そして必ず片方だけが直る）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTransitionWaitNoticeHost : MonoBehaviour, IAreaTransitionWaitNotice
    {
        /// <summary>出す文（控えめな 1 行）。</summary>
        public const string NoticeText = "読み込み中…";

        /// <summary>字を置く枠の大きさ（参照解像度 1920×1080 のときの px）。</summary>
        private static readonly Vector2 NoticeSize = new Vector2(240f, 36f);

        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        private bool _built;
        private Text _text;
        private RectTransform _rect;

        /// <summary>いま生きている常駐の待ち表示（無ければ null。テスト用）。</summary>
        public static AreaTransitionWaitNoticeHost Instance { get; private set; }

        /// <inheritdoc />
        public bool IsShowing { get; private set; }

        /// <inheritdoc />
        public int ShowCount { get; private set; }

        /// <inheritdoc />
        public int HideCount { get; private set; }

        /// <summary>出している文（テスト用。出していなければ空）。</summary>
        public string ShownLine => IsShowing && _text != null ? _text.text : string.Empty;

        /// <summary>
        /// 画面に対する面積の割合（0〜1）。出していなければ 0。
        ///
        /// <b>参照解像度で計る。</b> Canvas は <c>ScaleWithScreenSize</c> なので、
        /// 実画面の解像度が変わっても「画面のどれだけを占めるか」は変わらない——
        /// 実ピクセルで計ると、解像度を変えただけで数が動いて検査が割れる。
        /// </summary>
        public float ScreenAreaFraction
        {
            get
            {
                if (!IsShowing)
                {
                    return 0f;
                }

                float screen = ReferenceResolution.x * ReferenceResolution.y;
                return screen <= 0f ? 0f : (NoticeSize.x * NoticeSize.y) / screen;
            }
        }

        /// <summary>
        /// 全画面を覆う板を持っているか（テスト用。§5「全画面を黒くしない」）。
        ///
        /// <b>「無いこと」を数えられるようにしておく。</b> あとから暗幕を足されても、
        /// ここが true になれば受入が落ちる。
        /// </summary>
        public bool HasFullScreenBackdrop
        {
            get
            {
                foreach (Graphic g in GetComponentsInChildren<Graphic>(true))
                {
                    if (g == null || g is Text)
                    {
                        continue;
                    }

                    // 字以外の描画物（Image・RawImage）は 1 つも置かない方針なので、
                    // 見つかった時点で「暗幕を足した」と言ってよい。
                    return true;
                }

                return false;
            }
        }

        /// <inheritdoc />
        public void Show()
        {
            EnsureBuilt();
            if (IsShowing)
            {
                return;
            }

            IsShowing = true;
            ShowCount++;
            if (_text != null)
            {
                _text.text = NoticeText;
                _text.enabled = true;
            }
        }

        /// <inheritdoc />
        public void Hide()
        {
            if (!IsShowing)
            {
                return;
            }

            IsShowing = false;
            HideCount++;
            if (_text != null)
            {
                _text.enabled = false;
            }
        }

        /// <summary>Canvas と 1 行を一度だけ組む（二度目以降は何もしない）。</summary>
        public void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;

            var canvasGo = new GameObject(
                "AreaTransitionWaitNoticeCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // 探索 HUD（101）・戦闘 HUD（100）より前。待ちの字が地形や HUD に埋もれないように。
            canvas.sortingOrder = 120;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var go = new GameObject("WaitNoticeText", typeof(RectTransform));
            _rect = (RectTransform)go.transform;
            _rect.SetParent(canvasGo.transform, false);

            // 右下の隅へ。主人公・地形・案内行のどれとも重ならない場所である。
            _rect.anchorMin = new Vector2(1f, 0f);
            _rect.anchorMax = new Vector2(1f, 0f);
            _rect.pivot = new Vector2(1f, 0f);
            _rect.sizeDelta = NoticeSize;
            _rect.anchoredPosition = new Vector2(-32f, 32f);

            _text = go.AddComponent<Text>();
            _text.font = ResolveUiFont();
            _text.fontSize = 22;
            _text.alignment = TextAnchor.MiddleRight;
            _text.color = new Color(1f, 1f, 1f, 0.75f); // 控えめ（真っ白で押し出さない）。
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.supportRichText = false;
            _text.text = NoticeText;
            _text.enabled = false;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 二つ目は作らせない（常駐 Rig と同じ規律）。
                Destroy(this);
                return;
            }

            Instance = this;
            AreaTransitionWaitNoticeProvider.TrySetCurrent(this, this);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }

            AreaTransitionWaitNoticeProvider.ReleaseIfOwner(this);
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
                f = Font.CreateDynamicFontFromOSFont(
                    new[] { "Arial", "Helvetica", "Verdana", "Segoe UI", "sans-serif" }, 16);
            }

            return f;
        }
    }
}
