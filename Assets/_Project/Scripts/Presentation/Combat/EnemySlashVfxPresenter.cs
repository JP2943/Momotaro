using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Combat;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 敵（近接骸骨など）の攻撃に同期して剣閃 VFX を表示する Presenter（Phase3.5 P3.5-05）。プレイヤーと異なり敵は複数体
    /// 同時に存在するため、Scene 内の <see cref="EnemyAttackController"/>（＝<see cref="IAttackSwingSource"/>）を低頻度で探索し、
    /// 個々の判定（Active）区間の立ち上がりを検出して、共有プール（<see cref="SlashVfxPool"/>）から剣閃を生成する。
    ///
    /// §7.2 の識別：通常／強／ガード不能を段値（<see cref="AttackSwing.EnemyMeleeNormal"/> 等）で受け取り、割り当て済みの
    /// 種別のみ表示する（当面は通常＝Slash_Enemy_Small_A のみ。強・ガード不能は素材制作中＝未割当・無処理）。突進・投射は剣閃を出さない。
    /// VFX は表示専用（Collider・ダメージ無し）。命中の有無に依存せず空振りでも表示し、Active 終了・撃破・Disable・Scene 離脱で残さない。
    /// Gameplay ロジックには一切干渉しない（読み取りのみ）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySlashVfxPresenter : MonoBehaviour
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

        [Header("敵剣閃素材（近接骸骨=通常。強・ガード不能は素材制作中）")]
        [Tooltip("通常（§7.2 通常。Slash_Enemy_Small_A）。")]
        [SerializeField] private SlashFrameSet _normal;

        [Tooltip("強（§7.2 強。素材制作中）。")]
        [SerializeField] private SlashFrameSet _heavy;

        [Tooltip("ガード不能（§7.2 ガード不能。素材制作中）。")]
        [SerializeField] private SlashFrameSet _unblockable;

        [Tooltip("剣閃 1 発の表示時間（秒）。")]
        [SerializeField] private float _slashDuration = 0.12f;

        [Tooltip("剣閃スプライトの Sorting Order。")]
        [SerializeField] private int _sortingOrder = 45;

        [Tooltip("Scene 内の敵攻撃元を再取得する間隔（秒）。毎フレーム FindObjects しない。")]
        [SerializeField] private float _rescanInterval = 1f;

        private sealed class Track
        {
            public bool WasActive;
            public SlashVfxInstance Current;
        }

        private SlashVfxPool _pool;
        private Transform _poolRoot;
        private readonly List<IAttackSwingSource> _sources = new List<IAttackSwingSource>();
        private readonly Dictionary<IAttackSwingSource, Track> _tracks = new Dictionary<IAttackSwingSource, Track>();
        private readonly List<IAttackSwingSource> _scratch = new List<IAttackSwingSource>();
        private float _rescanTimer;

        /// <summary>通常の敵剣閃素材（Scene 構築 P3.5-06・テストが設定）。</summary>
        public SlashFrameSet NormalFrames { get => _normal; set => _normal = value; }

        /// <summary>プール（テスト・検証用）。</summary>
        public SlashVfxPool Pool => EnsurePool();

        private void Awake()
        {
            EnsurePool();
        }

        private void OnDisable()
        {
            StopAll();
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
        public void Bind(IEnumerable<IAttackSwingSource> sources)
        {
            _sources.Clear();
            if (sources != null)
            {
                _sources.AddRange(sources);
            }

            SyncTracks();
        }

        /// <summary>Scene 内の敵攻撃元（<see cref="EnemyAttackController"/>）を取得し直す（動的生成・撃破に追従）。</summary>
        public void Rescan()
        {
            _sources.Clear();
            EnemyAttackController[] found = FindObjectsByType<EnemyAttackController>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                _sources.Add(found[i]);
            }

            SyncTracks();
        }

        private void SyncTracks()
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (!_tracks.ContainsKey(_sources[i]))
                {
                    _tracks[_sources[i]] = new Track();
                }
            }
        }

        /// <summary>
        /// 1 フレーム進める（Update から、またはテストが決定的に呼ぶ）。プール再生を進めたうえで、各観測元の判定区間立ち上がり／
        /// 立ち下がりを検出して剣閃を生成／消灯する。破棄済み（撃破）観測元は追跡を解除する。
        /// </summary>
        public void Tick(float deltaTime)
        {
            EnsurePool();
            _pool.TickActive(deltaTime);

            _scratch.Clear();
            _scratch.AddRange(_sources);

            for (int i = 0; i < _scratch.Count; i++)
            {
                IAttackSwingSource src = _scratch[i];

                // 撃破・破棄された敵（Unity fake-null）は追跡を解除して残留を残さない。
                if (src is Object o && o == null)
                {
                    if (_tracks.TryGetValue(src, out Track dead))
                    {
                        dead.Current?.Stop();
                        _tracks.Remove(src);
                    }

                    _sources.Remove(src);
                    continue;
                }

                if (!_tracks.TryGetValue(src, out Track t))
                {
                    t = new Track();
                    _tracks[src] = t;
                }

                if (t.Current != null && !t.Current.IsPlaying)
                {
                    t.Current = null;
                }

                bool active = src.IsSwingHitboxActive && FrameSetFor(src.SwingStage) != null;

                if (active && !t.WasActive)
                {
                    t.Current = SpawnSlash(src);
                }
                else if (!active && t.WasActive && t.Current != null)
                {
                    t.Current.Stop();
                    t.Current = null;
                }

                t.WasActive = active;
            }
        }

        /// <summary>全剣閃を打ち切る（Disable・Scene 離脱・Retry）。</summary>
        public void StopAll()
        {
            _pool?.StopAll();
            foreach (KeyValuePair<IAttackSwingSource, Track> kv in _tracks)
            {
                kv.Value.Current = null;
                kv.Value.WasActive = false;
            }
        }

        private SlashVfxInstance SpawnSlash(IAttackSwingSource src)
        {
            Sprite[] frames = FramesFor(FrameSetFor(src.SwingStage), src.SwingForward);
            if (frames == null || frames.Length == 0)
            {
                return null; // 未割当（素材制作中）：無処理で継続。
            }

            SlashVfxInstance inst = EnsurePool().Get();
            inst.Play(frames, src.SwingCenter, _slashDuration, _sortingOrder);
            return inst;
        }

        private SlashFrameSet FrameSetFor(int stage)
        {
            switch (stage)
            {
                case AttackSwing.EnemyMeleeNormal: return _normal;
                case AttackSwing.EnemyMeleeHeavy: return _heavy;
                case AttackSwing.EnemyMeleeUnblockable: return _unblockable;
                default: return null;
            }
        }

        private Sprite[] FramesFor(SlashFrameSet set, Vector3 forward)
        {
            if (set == null)
            {
                return null;
            }

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

            var go = new GameObject("EnemySlashVfxPool");
            go.transform.SetParent(transform, false);
            _poolRoot = go.transform;
            _pool = new SlashVfxPool(_poolRoot);
            return _pool;
        }
    }
}
