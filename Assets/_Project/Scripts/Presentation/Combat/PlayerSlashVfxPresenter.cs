using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 主人公の通常攻撃に同期して剣閃 VFX を表示する Presenter（Phase3.5 P3.5-05・第1弾）。
    /// <see cref="IAttackSwingSource"/> の判定（Active）区間立ち上がりを検出し、その段・Facing・Hitbox 中心に合わせて
    /// 汎用 Slash 素材（Slash_Small_A）をプール（<see cref="SlashVfxPool"/>）から生成する。命中の有無に依存せず「空振りでも」表示し、
    /// VFX には当たり判定・ダメージを持たせない（表示専用）。
    ///
    /// 本 Task の範囲は「通常攻撃 1 段目」まで（他段・必殺技・敵の剣閃は素材制作中のため未割当＝無処理）。
    /// Active 終了・攻撃中断・Disable・Scene 離脱で残留を残さない。Gameplay ロジックには一切干渉しない（読み取りのみ）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSlashVfxPresenter : MonoBehaviour
    {
        /// <summary>方向別（4 方向）×コマの剣閃素材セット。未割当（null／空）の方向は表示しない。</summary>
        [System.Serializable]
        public sealed class SlashFrameSet
        {
            public Sprite[] down;
            public Sprite[] up;
            public Sprite[] left;
            public Sprite[] right;
        }

        [Header("剣閃素材（当面は 1 段目のみ。2 段目以降・必殺技は素材制作中）")]
        [SerializeField] private SlashFrameSet _stage1;

        [Tooltip("剣閃 1 発の表示時間（秒）。判定区間に概ね合わせる短め既定。")]
        [SerializeField] private float _slashDuration = 0.12f;

        [Tooltip("剣閃スプライトの Sorting Order（敵頭上 Bar 等より前面）。")]
        [SerializeField] private int _sortingOrder = 50;

        [Tooltip("未 Bind の間の自動探索間隔（秒）。毎フレーム FindObjects しないためのスロットル。")]
        [SerializeField] private float _autoLocateInterval = 0.5f;

        [SerializeField] private PlayerStateController _player;

        private IAttackSwingSource _source;
        private SlashVfxPool _pool;
        private Transform _poolRoot;
        private bool _wasActive;
        private float _locateTimer;
        private SlashVfxInstance _current;

        /// <summary>1 段目の剣閃素材（Scene 構築 P3.5-06・テストが設定）。</summary>
        public SlashFrameSet Stage1Frames { get => _stage1; set => _stage1 = value; }

        /// <summary>剣閃 1 発の表示時間（秒）。</summary>
        public float SlashDuration { get => _slashDuration; set => _slashDuration = value; }

        /// <summary>プール（テスト・検証用）。</summary>
        public SlashVfxPool Pool => EnsurePool();

        private void Awake()
        {
            EnsurePool();
            if (_player != null)
            {
                _source = _player;
            }
        }

        private void OnDisable()
        {
            StopAll();
        }

        private void Update()
        {
            if (_source == null)
            {
                _locateTimer += Time.unscaledDeltaTime;
                if (_locateTimer >= _autoLocateInterval)
                {
                    _locateTimer = 0f;
                    _player = FindFirstObjectByType<PlayerStateController>();
                    if (_player != null)
                    {
                        _source = _player;
                    }
                }
            }

            Tick(Time.deltaTime);
        }

        /// <summary>観測元を注入する（Scene 構築・テスト。読み取りのみ）。</summary>
        public void Bind(IAttackSwingSource source)
        {
            _source = source;
        }

        /// <summary>
        /// 1 フレーム進める（Update から、またはテストが決定的に呼ぶ）。プール再生を進めたうえで、判定区間の立ち上がり／
        /// 立ち下がりを検出して剣閃を生成／消灯する。Pause 時はスケール時間 0 が渡り、剣閃も進まない。
        /// </summary>
        public void Tick(float deltaTime)
        {
            EnsurePool();
            _pool.TickActive(deltaTime);

            if (_current != null && !_current.IsPlaying)
            {
                _current = null;
            }

            bool active = _source != null && _source.IsSwingHitboxActive && _source.SwingStage == 1;

            if (active && !_wasActive)
            {
                SpawnSlash();
            }
            else if (!active && _wasActive && _current != null)
            {
                _current.Stop(); // 判定終了・中断で剣閃を消す（遅れて残さない）。
                _current = null;
            }

            _wasActive = active;
        }

        /// <summary>全剣閃を打ち切る（Disable・Scene 離脱・Retry）。</summary>
        public void StopAll()
        {
            _pool?.StopAll();
            _current = null;
            _wasActive = false;
        }

        private void SpawnSlash()
        {
            Sprite[] frames = FramesFor(_source.SwingForward);
            if (frames == null || frames.Length == 0)
            {
                return; // 未割当（素材制作中）：無処理でGameplay継続。
            }

            _current = EnsurePool().Get();
            _current.Play(frames, _source.SwingCenter, _slashDuration, _sortingOrder);
        }

        private Sprite[] FramesFor(Vector3 forward)
        {
            if (_stage1 == null)
            {
                return null;
            }

            // FacingToVector 準拠：Up=+Z, Down=-Z, Right=+X, Left=-X。優勢な軸で 4 方向へ量子化する。
            if (Mathf.Abs(forward.x) >= Mathf.Abs(forward.z))
            {
                return forward.x >= 0f ? _stage1.right : _stage1.left;
            }

            return forward.z >= 0f ? _stage1.up : _stage1.down;
        }

        private SlashVfxPool EnsurePool()
        {
            if (_pool != null)
            {
                return _pool;
            }

            var go = new GameObject("SlashVfxPool");
            go.transform.SetParent(transform, false);
            _poolRoot = go.transform;
            _pool = new SlashVfxPool(_poolRoot);
            return _pool;
        }
    }
}
