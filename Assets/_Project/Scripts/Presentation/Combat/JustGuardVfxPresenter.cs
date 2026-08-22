using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Diagnostics;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// ジャストガード成立時に「弾いた」閃光 VFX を接触点へ表示する Presenter（Phase3.5 P3.5-08B）。
    /// <see cref="CombatFeedbackDispatcher.Feedback"/> を購読し、<see cref="HitResultKind.JustGuard"/> の結果だけに反応して、
    /// 結果へ載った接触点（<see cref="HitResult.HitPoint"/>）へ無方向のフラッシュ（<see cref="SlashVfxPool"/> で再利用）を 1 回再生する。
    ///
    /// 表示位置は接触点をカメラへ正対（billboard）・DepthOffset 逃がしで補正する（<see cref="SlashVfxPlacement"/>。俯瞰カメラでの
    /// 沈み込み・床/壁の深度欠けを防ぐ）。当たり判定・ダメージは一切持たない（表示専用）。Disable・Scene 離脱・Retry で残留を残さない。
    /// 素材未割当・接触点ゼロでも例外なく継続する。Gameplay ロジックには一切干渉しない（読み取りのみ）。
    ///
    /// 手応え全体（ヒットストップ・点滅・カメラ揺れ・SE）は <see cref="CombatFeedbackPresenter"/> が担当する。本 Presenter は
    /// 接触点へのスプライト閃光のみを足す独立コンポーネントで、同じチャネルを別購読者として購読する（役割を分離）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JustGuardVfxPresenter : MonoBehaviour, ICombatFeedbackListener
    {
        [Header("ジャストガード閃光素材（無方向・単発。JustGuard_Flash_A）")]
        [SerializeField] private Sprite[] _flashFrames;

        [Tooltip("閃光の再生時間（秒）。短い単発表現。")]
        [SerializeField] private float _duration = 0.18f;

        [Tooltip("閃光に乗せる色（Tint）。JG は金色系で「弾いた」感を強調する（既定は Presenter の点滅色に合わせた暖色）。")]
        [SerializeField] private Color _tint = new Color(1f, 0.95f, 0.5f, 1f);

        [Tooltip("閃光スプライトの Sorting Order（剣閃 50・警告 60 より前面へ）。")]
        [SerializeField] private int _sortingOrder = 70;

        [Tooltip("接触点からの持ち上げ（m）。HitPoint は接触高さなので既定 0。上へずらしたい時のみ指定する。")]
        [SerializeField] private float _heightOffset = 0f;

        [Tooltip("カメラ側（-forward）へ逃がす深度オフセット（m）。床・壁との深度交差による欠けを防ぐ。キャラ billboard と同値が目安。")]
        [SerializeField] private float _depthOffset = 0.5f;

        [Tooltip("正対（billboard）対象カメラ。未指定なら Main Camera を取得してキャッシュする。")]
        [SerializeField] private Camera _camera;

        [Tooltip("配信元（Dispatcher）を再取得する間隔（秒）。未購読の間だけ探索する。")]
        [SerializeField] private float _refreshInterval = 1f;

        private CombatFeedbackChannel _channel;
        private SlashVfxPool _pool;
        private Transform _poolRoot;
        private Camera _cachedCamera;
        private float _nextRefresh;

        /// <summary>閃光素材（Scene 構築・テストが設定）。</summary>
        public Sprite[] FlashFrames { get => _flashFrames; set => _flashFrames = value; }

        /// <summary>閃光色（Tint。Scene 構築・テストが設定）。</summary>
        public Color Tint { get => _tint; set => _tint = value; }

        /// <summary>再生時間（秒。Scene 構築・試遊調整・テストが設定）。</summary>
        public float Duration { get => _duration; set => _duration = value; }

        /// <summary>プール（テスト・検証用）。</summary>
        public SlashVfxPool Pool => EnsurePool();

        /// <summary>再生中インスタンス数（テスト・検証用）。</summary>
        public int ActiveCount => EnsurePool().ActiveCount;

        /// <summary>正対対象カメラを設定する（Scene 構築・テスト）。キャッシュをリセットする。</summary>
        public void SetCamera(Camera camera)
        {
            _camera = camera;
            _cachedCamera = null;
        }

        /// <summary>購読チャネルを差し替える（テスト・Scene 構築）。重複購読しない。</summary>
        public void Bind(CombatFeedbackChannel channel)
        {
            if (ReferenceEquals(channel, _channel))
            {
                return;
            }

            if (_channel != null)
            {
                _channel.RemoveListener(this);
            }

            _channel = channel;
            if (_channel != null)
            {
                _channel.AddListener(this);
            }
        }

        /// <summary>Scene 内の <see cref="CombatFeedbackDispatcher"/> を探して購読し直す。</summary>
        public void Rescan()
        {
            CombatFeedbackDispatcher dispatcher = FindFirstObjectByType<CombatFeedbackDispatcher>();
            Bind(dispatcher != null ? dispatcher.Feedback : null);
        }

        private void Awake()
        {
            EnsurePool();
        }

        private void OnEnable()
        {
            Rescan();
        }

        private void OnDisable()
        {
            if (_channel != null)
            {
                _channel.RemoveListener(this);
                _channel = null;
            }

            StopAll();
        }

        private void Update()
        {
            // 再生中インスタンスをスケール時間で進める（HitStop 中は 0 が渡り閃光も止まる＝Slash と同方針）。
            EnsurePool().TickActive(Time.deltaTime);

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, _refreshInterval);
                if (_channel == null)
                {
                    Rescan(); // 未購読の間だけ探索（Dispatcher 生成待ち・Scene 再読込）。
                }
            }
        }

        /// <inheritdoc />
        public void OnCombatFeedback(in CombatFeedbackEvent feedback)
        {
            // JG 以外は無処理（点滅・ヒットストップ・SE は CombatFeedbackPresenter が担当）。
            if (feedback.Result.Kind != HitResultKind.JustGuard)
            {
                return;
            }

            Sprite[] frames = _flashFrames;
            if (frames == null || frames.Length == 0)
            {
                return; // 素材未割当：無処理でGameplay継続。
            }

            // 接触点をカメラ正対・深度補正した表示位置へ変換して 1 回再生する（判定は不変。表示専用）。
            SlashVfxPlacement.Compute(feedback.Result.HitPoint, ResolveCamera(), _heightOffset, _depthOffset,
                out Vector3 pos, out Quaternion rot);
            EnsurePool().Get().Play(frames, pos, rot, _duration, _sortingOrder, _tint);
        }

        /// <summary>1 フレーム進める（テストが決定的に呼ぶ）。Update と同じ駆動点。</summary>
        public void Tick(float deltaTime)
        {
            EnsurePool().TickActive(deltaTime);
        }

        /// <summary>全閃光を打ち切る（Disable・Scene 離脱・Retry）。残留を残さない。</summary>
        public void StopAll()
        {
            _pool?.StopAll();
        }

        private Camera ResolveCamera()
        {
            if (_camera != null)
            {
                return _camera;
            }

            if (_cachedCamera == null)
            {
                _cachedCamera = Camera.main;
            }

            return _cachedCamera;
        }

        private SlashVfxPool EnsurePool()
        {
            if (_pool != null)
            {
                return _pool;
            }

            var go = new GameObject("JustGuardVfxPool");
            go.transform.SetParent(transform, false);
            _poolRoot = go.transform;
            _pool = new SlashVfxPool(_poolRoot);
            return _pool;
        }
    }
}
