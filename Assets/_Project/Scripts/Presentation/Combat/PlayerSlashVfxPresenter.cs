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
    /// 表示位置は判定中心（<c>SwingCenter</c>）そのものではなく、<see cref="SlashVfxPlacement"/> で刀身高さへ持ち上げ・カメラ正対
    /// （billboard）・DepthOffset を適用する（P3.5-06。俯瞰カメラでの沈み込みと床/壁の深度欠けを防ぐ）。Active 終了・攻撃中断・Disable・
    /// Scene 離脱で残留を残さない。Gameplay ロジックには一切干渉しない（読み取りのみ）。
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

            [Tooltip("判定（Active）終了後も自前の再生時間まで表示を継続する（P3.5-06）。ジャンプ切り下ろし(3段目)のように、"
                + "判定終了後の着地モーションまで剣閃を残したい攻撃で true にする。通常の横斬りは false（判定終了で消灯）。")]
            public bool holdThroughRecovery;
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

        [Header("表示位置補正（P3.5-06。SwingCenter は戦闘判定用で見た目の刀身高さと一致しない）")]
        [Tooltip("剣閃を刀身高さへ持ち上げるワールド上方向オフセット（m）。")]
        [SerializeField] private float _slashHeightOffset = 1.1f;

        [Tooltip("カメラ側（-forward）へ逃がす深度オフセット（m）。床・壁との深度交差による欠けを防ぐ。キャラ billboard と同値が目安。")]
        [SerializeField] private float _depthOffset = 0.5f;

        [Tooltip("剣閃を攻撃方向（SwingForward）に沿ってプレイヤー側へ引き戻す量（m。P3.5-06）。判定（Hitbox）は不変で、表示だけ手元へ寄せる。"
            + " 俯瞰カメラでは Left/Right が画面上で最も補正され、Down は圧縮で最小になる（Down 以外が離れて見える症状に対応）。")]
        [SerializeField] private float _vfxForwardPull = 0.35f;

        [Tooltip("正対（billboard）対象カメラ。未指定なら Main Camera を取得してキャッシュする。")]
        [SerializeField] private Camera _camera;

        [Tooltip("未 Bind の間の自動探索間隔（秒）。毎フレーム FindObjects しないためのスロットル。")]
        [SerializeField] private float _autoLocateInterval = 0.5f;

        [SerializeField] private PlayerStateController _player;

        private IAttackSwingSource _source;
        private SlashVfxPool _pool;
        private Transform _poolRoot;
        private bool _wasActive;
        private float _locateTimer;
        private SlashVfxInstance _current;
        private bool _currentHold; // 現在の剣閃が holdThroughRecovery（判定終了後も継続）か。
        private Camera _cachedCamera;

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

        /// <summary>刀身高さへの持ち上げオフセット（m。Scene 構築・試遊調整・テストが設定）。</summary>
        public float SlashHeightOffset { get => _slashHeightOffset; set => _slashHeightOffset = value; }

        /// <summary>深度オフセット（m。Scene 構築・試遊調整・テストが設定）。</summary>
        public float DepthOffset { get => _depthOffset; set => _depthOffset = value; }

        /// <summary>剣閃を攻撃方向に沿ってプレイヤー側へ引き戻す量（m。判定は不変。試遊調整・テストが設定）。</summary>
        public float VfxForwardPull { get => _vfxForwardPull; set => _vfxForwardPull = value; }

        /// <summary>正対対象カメラを設定する（Scene 構築 P3.5-06・テスト）。キャッシュをリセットする。</summary>
        public void SetCamera(Camera camera)
        {
            _camera = camera;
            _cachedCamera = null;
        }

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
                if (_currentHold)
                {
                    // ジャンプ切り下ろし等：判定終了後も自前 duration まで表示継続（着地モーションまで残す）。追跡だけ解除。
                    _current = null;
                }
                else
                {
                    _current.Stop(); // 通常攻撃：判定終了・中断で剣閃を消す（遅れて残さない）。
                    _current = null;
                }
            }

            // 必殺技は判定中心が Active 中に前方へ進む（P3.5-09）。剣閃を SwingCenter に毎フレーム追従させ、判定と見た目を一致させる。
            // 通常コンボは中心が固定のため対象外（生成時の姿勢のまま）。
            if (_current != null && _current.IsPlaying && active && _source.SwingStage == AttackSwing.SpecialStage)
            {
                Vector3 followCenter = _source.SwingCenter - _source.SwingForward.normalized * _vfxForwardPull;
                SlashVfxPlacement.Compute(followCenter, ResolveCamera(), _slashHeightOffset, _depthOffset,
                    out Vector3 followPos, out Quaternion followRot);
                _current.SetPose(followPos, followRot);
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

            // 判定中心（SwingCenter）から攻撃方向へ引き戻し、表示だけプレイヤー側へ寄せる（判定 Hitbox は不変。P3.5-06）。
            Vector3 center = _source.SwingCenter - _source.SwingForward.normalized * _vfxForwardPull;
            SlashVfxPlacement.Compute(center, ResolveCamera(), _slashHeightOffset, _depthOffset,
                out Vector3 pos, out Quaternion rot);
            _current = EnsurePool().Get();
            _currentHold = set.holdThroughRecovery; // 判定終了後も残すか（3段目のジャンプ切り下ろし等）。
            _current.Play(frames, pos, rot, set.duration, _sortingOrder, _playerSlashColor);
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
