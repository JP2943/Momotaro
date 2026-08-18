using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Combat;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// ガード不能攻撃の予告（予兆）を敵頭上へ表示する Presenter（Phase3.5 P3.5-05）。Scene 内の
    /// <see cref="EnemyAttackController"/>（＝<see cref="IEnemyUnblockableWarningSource"/>）を低頻度で探索し、ガード不能攻撃の
    /// Prepare 区間中は各敵の頭上へ警告 VFX（無方向・ループ）を継続表示する。予兆終了・撃破・Disable・Scene 離脱で消す。
    ///
    /// ガード不能は Guard／JG 不可のため、この予告で回避（Step）を促す。表示専用（Collider・ダメージ無し）。
    /// Gameplay ロジックには一切干渉しない（読み取りのみ）。素材未割当でも例外なく継続する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyUnblockableWarningPresenter : MonoBehaviour
    {
        [Header("ガード不能予告素材（無方向・ループ。Warning_Enemy_Unguardable_A）")]
        [SerializeField] private Sprite[] _warningFrames;

        [Tooltip("敵中心からの頭上オフセット（m）。")]
        [SerializeField] private float _height = 2f;

        [Tooltip("予告コマのループ周期（秒）。")]
        [SerializeField] private float _loopSeconds = 0.4f;

        [Tooltip("予告に乗せる色（Tint）。ガード不能予告は危険を伝える赤系が前提（既定 #FF3B30）。")]
        [SerializeField] private Color _warningColor = new Color(1f, 0.23137255f, 0.1882353f, 1f);

        [Tooltip("予告スプライトの Sorting Order。")]
        [SerializeField] private int _sortingOrder = 60;

        [Tooltip("Scene 内の敵を再取得する間隔（秒）。毎フレーム FindObjects しない。")]
        [SerializeField] private float _rescanInterval = 1f;

        private Transform _root;
        private readonly List<IEnemyUnblockableWarningSource> _sources = new List<IEnemyUnblockableWarningSource>();
        private readonly Dictionary<IEnemyUnblockableWarningSource, WarningVfxInstance> _active =
            new Dictionary<IEnemyUnblockableWarningSource, WarningVfxInstance>();
        private readonly Stack<WarningVfxInstance> _free = new Stack<WarningVfxInstance>();
        private readonly List<IEnemyUnblockableWarningSource> _scratch = new List<IEnemyUnblockableWarningSource>();
        private int _total;
        private float _rescanTimer;

        /// <summary>予告素材（Scene 構築 P3.5-06・テストが設定）。</summary>
        public Sprite[] WarningFrames { get => _warningFrames; set => _warningFrames = value; }

        /// <summary>予告色（Tint。ガード不能予告は赤系前提。Scene 構築 P3.5-06・テストが設定）。</summary>
        public Color WarningColor { get => _warningColor; set => _warningColor = value; }

        /// <summary>現在表示中の予告数（テスト・検証用）。</summary>
        public int ActiveCount => _active.Count;

        /// <summary>生成済みインスタンス総数（再利用検証用）。</summary>
        public int TotalCount => _total;

        private void Awake()
        {
            EnsureRoot();
        }

        private void OnDisable()
        {
            HideAll();
        }

        private void Update()
        {
            _rescanTimer += Time.unscaledDeltaTime;
            if (_rescanTimer >= _rescanInterval)
            {
                _rescanTimer = 0f;
                Rescan();
            }

            Tick(Time.deltaTime);
        }

        /// <summary>観測元を明示注入する（テスト・Scene 構築。読み取りのみ）。</summary>
        public void Bind(IEnumerable<IEnemyUnblockableWarningSource> sources)
        {
            _sources.Clear();
            if (sources != null)
            {
                _sources.AddRange(sources);
            }
        }

        /// <summary>Scene 内の敵を取得し直す（動的生成・撃破に追従）。</summary>
        public void Rescan()
        {
            _sources.Clear();
            EnemyAttackController[] found = FindObjectsByType<EnemyAttackController>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                _sources.Add(found[i]);
            }
        }

        /// <summary>
        /// 1 フレーム進める（Update から、またはテストが決定的に呼ぶ）。ガード不能予兆中の敵へ予告を継続表示し、頭上へ追従させ、
        /// 予兆終了・破棄で消す。素材未割当時は何も表示しない。
        /// </summary>
        public void Tick(float deltaTime)
        {
            EnsureRoot();
            bool hasFrames = _warningFrames != null && _warningFrames.Length > 0;

            _scratch.Clear();
            _scratch.AddRange(_sources);

            for (int i = 0; i < _scratch.Count; i++)
            {
                IEnemyUnblockableWarningSource src = _scratch[i];

                if (src is Object o && o == null)
                {
                    Release(src);
                    _sources.Remove(src);
                    continue;
                }

                bool warn = hasFrames && src.IsUnblockableTelegraphing;
                if (warn)
                {
                    Vector3 pos = src.WarningPosition + Vector3.up * _height;
                    if (!_active.TryGetValue(src, out WarningVfxInstance w))
                    {
                        w = Acquire();
                        w.Show(_warningFrames, pos, _sortingOrder, _loopSeconds, _warningColor);
                        _active[src] = w;
                    }
                    else
                    {
                        w.SetPosition(pos);
                    }

                    w.Tick(deltaTime);
                }
                else
                {
                    Release(src);
                }
            }
        }

        /// <summary>全予告を消す（Disable・Scene 離脱・Retry）。</summary>
        public void HideAll()
        {
            foreach (KeyValuePair<IEnemyUnblockableWarningSource, WarningVfxInstance> kv in _active)
            {
                if (kv.Value != null)
                {
                    kv.Value.Hide();
                    _free.Push(kv.Value);
                }
            }

            _active.Clear();
        }

        private void Release(IEnemyUnblockableWarningSource src)
        {
            if (_active.TryGetValue(src, out WarningVfxInstance w))
            {
                if (w != null)
                {
                    w.Hide();
                    _free.Push(w);
                }

                _active.Remove(src);
            }
        }

        private WarningVfxInstance Acquire()
        {
            if (_free.Count > 0)
            {
                return _free.Pop();
            }

            var go = new GameObject("UnblockableWarning", typeof(SpriteRenderer));
            go.transform.SetParent(_root, false);
            _total++;
            return go.AddComponent<WarningVfxInstance>();
        }

        private void EnsureRoot()
        {
            if (_root != null)
            {
                return;
            }

            var go = new GameObject("UnblockableWarnings");
            go.transform.SetParent(transform, false);
            _root = go.transform;
        }
    }
}
