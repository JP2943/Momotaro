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
    /// 本 Task の範囲は「通常攻撃 1〜3 段目＋必殺技」まで（敵の剣閃は素材制作中のため未割当＝無処理）。必殺技は
    /// コンボ段とは別系統の判定区間（<see cref="AttackSwing.SpecialStage"/>）として観測し、専用素材で表示する。
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

            [Tooltip("この素材セットの再生時間（秒）。素材ごとに調整する。0 以下でも無限再生しない（安全に極小時間へ丸める）。")]
            public float duration = 0.12f;
        }

        [Header("剣閃素材（通常1〜3段目＋必殺技。敵は素材制作中）")]
        [Tooltip("通常攻撃 1 段目（Slash_Small_A）。")]
        [SerializeField] private SlashFrameSet _stage1;

        [Tooltip("通常攻撃 2 段目（Slash_Small_B）。")]
        [SerializeField] private SlashFrameSet _stage2;

        [Tooltip("通常攻撃 3 段目（Slash_Small_C）。")]
        [SerializeField] private SlashFrameSet _stage3;

        [Tooltip("必殺技（Slash_Special_A）。")]
        [SerializeField] private SlashFrameSet _special;

        [Header("色（Tint）")]
        [Tooltip("主人公の剣閃に乗せる色。素材はほぼ白のため薄い寒色で「主人公らしさ」を付ける。")]
        [SerializeField] private Color _playerSlashColor = new Color(0.85f, 0.95f, 1f, 1f);

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

        /// <summary>2 段目の剣閃素材（Scene 構築 P3.5-06・テストが設定）。</summary>
        public SlashFrameSet Stage2Frames { get => _stage2; set => _stage2 = value; }

        /// <summary>3 段目の剣閃素材（Scene 構築 P3.5-06・テストが設定）。</summary>
        public SlashFrameSet Stage3Frames { get => _stage3; set => _stage3 = value; }

        /// <summary>必殺技の剣閃素材（Scene 構築 P3.5-06・テストが設定）。</summary>
        public SlashFrameSet SpecialFrames { get => _special; set => _special = value; }

        /// <summary>主人公の剣閃色（Tint。Scene 構築 P3.5-06・テストが設定）。</summary>
        public Color PlayerSlashColor { get => _playerSlashColor; set => _playerSlashColor = value; }

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

            bool active = _source != null && _source.IsSwingHitboxActive && FrameSetFor(_source.SwingStage) != null;

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
            SlashFrameSet set = FrameSetFor(_source.SwingStage);
            Sprite[] frames = FramesFor(set, _source.SwingForward);
            if (frames == null || frames.Length == 0)
            {
                return; // 未割当（素材制作中）：無処理でGameplay継続。
            }

            _current = EnsurePool().Get();
            _current.Play(frames, _source.SwingCenter, set.duration, _sortingOrder, _playerSlashColor);
        }

        /// <summary>段に対応する剣閃素材セット（1..3=通常コンボ Slash_Small_A/B/C, 必殺技=Slash_Special_A）。敵は未割当。</summary>
        private SlashFrameSet FrameSetFor(int stage)
        {
            switch (stage)
            {
                case 1: return _stage1;
                case 2: return _stage2;
                case 3: return _stage3;
                case AttackSwing.SpecialStage: return _special;
                default: return null;
            }
        }

        private Sprite[] FramesFor(SlashFrameSet set, Vector3 forward)
        {
            if (set == null)
            {
                return null;
            }

            // FacingToVector 準拠：Up=+Z, Down=-Z, Right=+X, Left=-X。優勢な軸で 4 方向へ量子化する。
            if (Mathf.Abs(forward.x) >= Mathf.Abs(forward.z))
            {
                return forward.x >= 0f ? set.right : set.left;
            }

            return forward.z >= 0f ? set.up : set.down;
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
