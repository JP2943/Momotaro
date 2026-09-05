using System.Text;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Scenes;
using UnityEngine;
using UnityEngine.UI;

namespace Momotaro.Presentation.Hud
{
    /// <summary>
    /// 共同開発者向け試遊 HUD の描画（Phase3.5 P3.5-04。仕様書 §6）。Debug 表示（OnGUI 系）とは別に、
    /// 戦闘判断と再試行に必要な情報を Screen Space Canvas へ固定表示する。<see cref="CombatHudViewModel"/> が
    /// 集約した HP／Stamina／Special／GuardBreak／Wave／Session 状態を、16:9 基準（1920x1080）の
    /// <see cref="CanvasScaler"/> と Anchor により解像度変更でも読める位置で描く。
    ///
    /// 本コンポーネントは表示のみを担い、Gameplay 値は変更しない。Player／Session は遅延生成され得るため
    /// <see cref="Bind"/> で注入するか、未 Bind の間だけ低頻度（毎フレームではない）に探索して接続する。
    /// Player 頭上 Bar は出さず、敵頭上 Bar（world space）と重ならない画面端へ配置する。
    /// 実際の入力ロック・Retry 遷移・VFX・Sorting 制御は本 Task の対象外（先回りしない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatPlayHud : MonoBehaviour
    {
        [Header("任意注入（未設定なら未 Bind の間だけ低頻度で探索）")]
        [SerializeField] private PlayerVitalsHolder _player;
        [SerializeField] private PlayerStateController _playerState;
        [SerializeField] private CombatSessionController _session;
        [SerializeField] private WaveRunner _waves;
        [SerializeField] private CombatOutcomeController _outcome;
        [SerializeField] private PlayerProgressHolder _progress;

        [Tooltip("未 Bind の間の自動探索間隔（秒）。毎フレーム FindObjects しないためのスロットル。")]
        [SerializeField] private float _autoLocateInterval = 0.5f;

        private readonly CombatHudViewModel _vm = new CombatHudViewModel();

        private bool _built;
        private float _locateTimer;
        private RectTransform _canvasRoot;
        private WaveRunner _boundWaves; // 現在購読中の WaveRunner（対称管理・重複購読防止）。

        private Bar _hpBar;
        private Bar _staminaBar;
        private Text _hpText;
        private Text _staminaText;
        private Text _specialText;
        private Text _guardText;
        private Text _waveText;
        private Text _phaseText;
        private Text _virtueText;
        private Font _font;

        /// <summary>集約 ViewModel（テスト・外部参照用）。</summary>
        public CombatHudViewModel ViewModel => _vm;

        /// <summary>進行データ（徳）の供給元（配線確認・Validator・テスト用。P4-00）。</summary>
        public PlayerProgressHolder ProgressSource => _progress;

        private void Awake()
        {
            EnsureBuilt();
            _vm.Changed += RefreshVisuals;
        }

        private void OnEnable()
        {
            TryBindFromFields();
        }

        private void OnDestroy()
        {
            if (_boundWaves != null)
            {
                _boundWaves.WaveChanged -= OnWaveChanged;
                _boundWaves = null;
            }

            _vm.Changed -= RefreshVisuals;
            _vm.Dispose();
        }

        private void Update()
        {
            // Player／Session／PlayerState のいずれかが未解決の間だけ低頻度で探索（毎フレーム FindObjects しない）。
            // PlayerVitals だけ先に見つかっても、PlayerState を後から解決できるよう探索を継続する。
            if (_player == null || _playerState == null || _session == null || _waves == null || _outcome == null
                || _progress == null)
            {
                _locateTimer += Time.unscaledDeltaTime;
                if (_locateTimer >= _autoLocateInterval)
                {
                    _locateTimer = 0f;
                    AutoLocate();
                }
            }

            _vm.Tick(); // GuardBreak／Special など非イベント値のポーリング反映。

            // 結果パネルの表示遅延（§9.1 0.75s）は時間で変化しイベントを伴わないため、結果状態の間は毎フレーム再描画する。
            if (_vm.Phase == CombatSessionState.Victory || _vm.Phase == CombatSessionState.Defeat)
            {
                RefreshVisuals();
            }
        }

        /// <summary>
        /// Player／Session を明示注入する（Scene 構築・P3.5-06 が接続、テストも利用）。null は無視して既存 Bind を保つ。
        /// </summary>
        public void Bind(PlayerVitalsHolder player, PlayerStateController playerState, CombatSessionController session)
        {
            if (player != null)
            {
                _player = player;
            }

            if (playerState != null)
            {
                _playerState = playerState;
            }

            if (session != null)
            {
                _session = session;
            }

            TryBindFromFields();
        }

        /// <summary>進行データ（徳）の供給元を注入する（Scene 構築・テストが接続。null は無視。P4-00）。</summary>
        public void SetProgressSource(PlayerProgressHolder progress)
        {
            if (progress != null)
            {
                _progress = progress;
            }

            TryBindFromFields();
        }

        /// <summary>Wave 供給元（P3.5-07）を注入する（Scene 構築が接続。null は無視）。現在 Wave／総数を「WAVE n / N」へ反映する。</summary>
        public void SetWaveSource(WaveRunner waves)
        {
            if (waves != null)
            {
                _waves = waves;
            }

            TryBindFromFields();
        }

        private void AutoLocate()
        {
            if (_player == null)
            {
                _player = FindFirstObjectByType<PlayerVitalsHolder>();
            }

            if (_playerState == null)
            {
                _playerState = FindFirstObjectByType<PlayerStateController>();
            }

            if (_session == null)
            {
                _session = FindFirstObjectByType<CombatSessionController>();
            }

            if (_waves == null)
            {
                _waves = FindFirstObjectByType<WaveRunner>();
            }

            if (_outcome == null)
            {
                _outcome = FindFirstObjectByType<CombatOutcomeController>();
            }

            if (_progress == null)
            {
                _progress = FindFirstObjectByType<PlayerProgressHolder>();
            }

            TryBindFromFields();
        }

        private void TryBindFromFields()
        {
            if (_player != null)
            {
                // GuardBreak／Special のデリゲートは _player／_playerState フィールドを都度参照する。これにより
                // PlayerVitals だけ先に Bind され、PlayerStateController が後から見つかった場合でも、再 Bind せずに
                // Special Ready／Charging が反映される。同一 Vital 参照での再 Bind は VM 側で重複購読にならない。
                _vm.BindPlayer(
                    _player.Vitals != null ? _player.Vitals.Health : null,
                    _player.Vitals != null ? _player.Vitals.Stamina : null,
                    () => _player != null && _player.IsGuardBroken,
                    () => _playerState != null && _playerState.IsSpecialCharged,
                    () => _playerState != null && _playerState.IsSpecialCharging);
            }

            if (_session != null)
            {
                _vm.BindSession(_session);
            }

            if (_progress != null)
            {
                _vm.BindProgress(_progress);
            }

            BindWaves();
            RefreshVisuals();
        }

        /// <summary>WaveRunner の <see cref="WaveRunner.WaveChanged"/> を対称購読し、現在 Wave／総数を ViewModel へ反映する（重複購読なし）。</summary>
        private void BindWaves()
        {
            if (ReferenceEquals(_boundWaves, _waves))
            {
                return;
            }

            if (_boundWaves != null)
            {
                _boundWaves.WaveChanged -= OnWaveChanged;
            }

            _boundWaves = _waves;
            if (_boundWaves != null)
            {
                _boundWaves.WaveChanged += OnWaveChanged;
                _vm.SetWave(_boundWaves.CurrentWave, _boundWaves.WaveCount);
            }
        }

        private void OnWaveChanged(int wave)
        {
            _vm.SetWave(wave, _boundWaves != null ? _boundWaves.WaveCount : 0);
        }

        // ---- 構築（冪等。重複 UI を作らない） ----

        /// <summary>Canvas と各表示要素を一度だけ構築する（二度目以降は何もしない＝重複 UI 防止）。</summary>
        public void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _font = ResolveUiFont();

            // Canvas は RectTransform を必ず持つ専用の子 GameObject に載せる。CombatPlayHud 自体が通常 Transform の
            // GameObject に追加されても安全（transform を RectTransform へキャストしないため InvalidCastException を起こさない）。
            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvasRoot = (RectTransform)canvasGo.transform;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // Debug/敵頭上 Bar より前面。

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f); // 16:9 基準。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = _canvasRoot;

            // --- 左下：Player Vitals（敵頭上 Bar と重ならない画面端。Player 頭上 Bar は作らない） ---
            RectTransform vitals = NewRect("Vitals", root,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(48f, 48f), new Vector2(360f, 132f));

            _hpBar = NewBar("HP", vitals, new Vector2(0f, 104f), 360f, 26f, new Color(0.85f, 0.20f, 0.20f, 1f));
            _hpText = NewText("HpText", vitals, new Vector2(0f, 0f), new Vector2(6f, 104f), new Vector2(348f, 26f),
                18, TextAnchor.MiddleLeft);

            _staminaBar = NewBar("Stamina", vitals, new Vector2(0f, 66f), 360f, 20f, new Color(0.20f, 0.70f, 0.85f, 1f));
            _staminaText = NewText("StaminaText", vitals, new Vector2(0f, 0f), new Vector2(6f, 66f), new Vector2(348f, 20f),
                15, TextAnchor.MiddleLeft);

            _specialText = NewText("SpecialText", vitals, new Vector2(0f, 0f), new Vector2(0f, 36f), new Vector2(360f, 26f),
                18, TextAnchor.MiddleLeft);
            _guardText = NewText("GuardText", vitals, new Vector2(0f, 0f), new Vector2(0f, 8f), new Vector2(360f, 26f),
                18, TextAnchor.MiddleLeft);
            _guardText.color = new Color(1f, 0.55f, 0.1f, 1f);

            // --- 上中央：Wave と Session フェーズ表示 ---
            _waveText = NewText("WaveText", root, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(400f, 40f),
                26, TextAnchor.MiddleCenter);
            _phaseText = NewText("PhaseText", root, new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(700f, 56f),
                34, TextAnchor.MiddleCenter);

            // --- 左上：徳の累計（P4-00）。獲得ポップ・加算アニメーションは行わず現在値のみを出す。 ---
            _virtueText = NewText("VirtueText", root, new Vector2(0f, 1f), new Vector2(24f, -40f), new Vector2(320f, 34f),
                22, TextAnchor.MiddleLeft);
            _virtueText.color = new Color(1f, 0.85f, 0.35f, 1f);

            // --- 右下：操作ガイド（入力設定の既定バインドに整合） ---
            Text guide = NewText("ControlGuide", root, new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(360f, 190f),
                18, TextAnchor.LowerRight);
            guide.text = BuildControlGuide();

            _built = true;
            RefreshVisuals();
        }

        /// <summary>
        /// レガシー uGUI Text 用フォントを堅牢に解決する（Phase3.5 P3.5-08 修正）。組み込み LegacyRuntime.ttf を第一候補に、取得失敗時は
        /// Arial.ttf、さらに OS フォントへフォールバックする。null のままだと Text が一切描画されず HUD の文字（HP/Wave/勝敗等）が消えるため、
        /// 少なくとも有効なフォントを返す。
        /// </summary>
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

        private string BuildControlGuide()
        {
            var sb = new StringBuilder();
            sb.AppendLine("移動: W A S D");
            sb.AppendLine("攻撃: J");
            sb.AppendLine("ガード: K");
            sb.AppendLine("ステップ: Space");
            sb.AppendLine("必殺: L");
            sb.Append("ポーズ: Esc");
            return sb.ToString();
        }

        // ---- 再描画（VM の Changed で呼ばれる。表示のみ） ----

        private void RefreshVisuals()
        {
            if (!_built)
            {
                return;
            }

            _hpBar.SetRatio(_vm.HpRatio);
            _staminaBar.SetRatio(_vm.StaminaRatio);

            if (_hpText != null)
            {
                _hpText.text = "HP  " + _vm.HpCurrent + " / " + _vm.HpMax;
            }

            if (_staminaText != null)
            {
                _staminaText.text = "STAMINA  " + _vm.StaminaCurrent + " / " + _vm.StaminaMax;
            }

            if (_specialText != null)
            {
                _specialText.text = _vm.SpecialReady ? "SPECIAL  ● READY"
                    : _vm.SpecialCharging ? "SPECIAL  … CHARGE"
                    : "SPECIAL  ○";
                _specialText.color = _vm.SpecialReady ? new Color(1f, 0.9f, 0.2f, 1f) : new Color(0.8f, 0.8f, 0.8f, 1f);
            }

            if (_guardText != null)
            {
                _guardText.text = _vm.GuardBroken ? "GUARD BREAK!" : string.Empty;
            }

            if (_waveText != null)
            {
                // 総数が既知なら「WAVE n / N」（§6.1）、未設定（0）なら「WAVE n」。
                _waveText.text = _vm.WaveTotal > 0
                    ? "WAVE  " + _vm.Wave + " / " + _vm.WaveTotal
                    : "WAVE  " + _vm.Wave;
            }

            if (_phaseText != null)
            {
                _phaseText.text = ResolvePhaseLabel();
            }

            if (_virtueText != null)
            {
                _virtueText.text = "VIRTUE  " + _vm.Virtue;
            }
        }

        /// <summary>
        /// フェーズ表示文字列を返す。Victory／Defeat の結果パネルは <see cref="CombatOutcomeController.ResultVisible"/> が立つまで
        /// （突入から 0.75s。§9.1）表示を保留する。Outcome 未解決時は従来どおり即時表示にフォールバックする。
        /// </summary>
        private string ResolvePhaseLabel()
        {
            CombatSessionState phase = _vm.Phase;
            if ((phase == CombatSessionState.Victory || phase == CombatSessionState.Defeat)
                && _outcome != null && !_outcome.ResultVisible)
            {
                return string.Empty; // 0.75s の表示待機中はパネルを出さない。
            }

            return PhaseLabel(phase);
        }

        private static string PhaseLabel(CombatSessionState state)
        {
            switch (state)
            {
                case CombatSessionState.Preparing: return "- READY -";
                case CombatSessionState.Playing: return string.Empty; // 戦闘中は非表示。
                case CombatSessionState.Intermission: return "INTERMISSION";
                case CombatSessionState.Victory: return "VICTORY!\nPRESS RETRY";
                case CombatSessionState.Defeat: return "DEFEAT\nPRESS RETRY";
                case CombatSessionState.Reloading: return "RELOADING…";
                default: return string.Empty;
            }
        }

        // ---- UI 構築ヘルパ ----

        private RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return rt;
        }

        private Image NewImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private Text NewText(string name, Transform parent, Vector2 anchor, Vector2 anchoredPos, Vector2 size,
            int fontSize, TextAnchor alignment)
        {
            RectTransform rt = NewRect(name, parent, anchor, anchor, anchor, anchoredPos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.alignment = alignment;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        private Bar NewBar(string name, Transform parent, Vector2 anchoredPos, float width, float height, Color fill)
        {
            RectTransform container = NewRect(name, parent, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                anchoredPos, new Vector2(width, height));

            RectTransform bg = NewRect("BG", container, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f),
                Vector2.zero, Vector2.zero);
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            NewImage(bg, new Color(0f, 0f, 0f, 0.55f));

            RectTransform fillRt = NewRect("Fill", container, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(width, height));
            NewImage(fillRt, fill);

            return new Bar(fillRt, width, height);
        }

        /// <summary>幅スケールで割合を表す Bar（Sprite 非依存で決定的）。</summary>
        private sealed class Bar
        {
            private readonly RectTransform _fill;
            private readonly float _fullWidth;
            private readonly float _height;

            public Bar(RectTransform fill, float fullWidth, float height)
            {
                _fill = fill;
                _fullWidth = fullWidth;
                _height = height;
            }

            public void SetRatio(float ratio)
            {
                if (_fill == null)
                {
                    return;
                }

                float r = ratio < 0f ? 0f : (ratio > 1f ? 1f : ratio);
                _fill.sizeDelta = new Vector2(_fullWidth * r, _height);
            }
        }
    }
}
