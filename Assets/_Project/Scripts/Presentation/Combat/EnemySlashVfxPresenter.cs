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
    /// §7.2 の識別：敵タイプ鍵（<see cref="IEnemySlashVisual.SlashVfxKey"/>。近接骸骨=Small／侍骸骨=Medium 等）と攻撃分類
    /// （通常／強／ガード不能）の両方で剣閃素材を引き当てる。割り当て済みの組み合わせのみ表示する（当面は各タイプの通常＝
    /// Slash_Enemy_Small_A／Medium_A。強・ガード不能は素材制作中＝未割当・無処理）。突進・投射は剣閃を出さない。
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

            [Tooltip("この素材セットの再生時間（秒）。素材ごとに調整する。0 以下でも無限再生しない（安全に極小時間へ丸める）。")]
            public float duration = 0.12f;
        }

        /// <summary>敵タイプ鍵ごとの剣閃素材（§7.2 通常／強／ガード不能）。鍵は <see cref="EnemyAttackController.SlashVfxKey"/> と一致させる。</summary>
        [System.Serializable]
        public sealed class EnemySlashEntry
        {
            [Tooltip("敵タイプ鍵（例：Small=近接骸骨／Medium=侍骸骨）。EnemyAttackController.SlashVfxKey と一致させる。")]
            public string key = "Small";
            public SlashFrameSet normal;
            public SlashFrameSet heavy;
            public SlashFrameSet unblockable;

            [Header("色（Tint。攻撃分類ごとに危険度が伝わる暖色系）")]
            [Tooltip("通常攻撃の剣閃色（既定 #FF8055）。")]
            public Color normalColor = new Color(1f, 0.5019608f, 0.33333334f, 1f);

            [Tooltip("強攻撃の剣閃色（既定 #FF7045）。")]
            public Color heavyColor = new Color(1f, 0.4392157f, 0.27058825f, 1f);

            [Tooltip("ガード不能攻撃の剣閃色（既定 #FF453A）。")]
            public Color unblockableColor = new Color(1f, 0.27058825f, 0.22745098f, 1f);
        }

        [Header("敵タイプ別の剣閃素材（鍵は EnemyAttackController.SlashVfxKey に一致。強/ガード不能は素材制作中）")]
        [SerializeField] private EnemySlashEntry[] _entries;

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

        /// <summary>敵タイプ別の剣閃素材テーブル（Scene 構築 P3.5-06・テストが設定）。</summary>
        public EnemySlashEntry[] Entries { get => _entries; set => _entries = value; }

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

                bool active = src.IsSwingHitboxActive && FrameSetForSource(src) != null;

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
            SlashFrameSet set = FrameSetForSource(src);
            Sprite[] frames = FramesFor(set, src.SwingForward);
            if (frames == null || frames.Length == 0)
            {
                return null; // 未割当（素材制作中）：無処理で継続。
            }

            SlashVfxInstance inst = EnsurePool().Get();
            inst.Play(frames, src.SwingCenter, set.duration, _sortingOrder, ColorForSource(src));
            return inst;
        }

        /// <summary>観測元の敵タイプ鍵（<see cref="IEnemySlashVisual"/>）と攻撃分類から剣閃素材を引き当てる。未登録・未割当は null。</summary>
        private SlashFrameSet FrameSetForSource(IAttackSwingSource src)
        {
            EnemySlashEntry entry = EntryFor(src);
            if (entry == null)
            {
                return null;
            }

            switch (src.SwingStage)
            {
                case AttackSwing.EnemyMeleeNormal: return entry.normal;
                case AttackSwing.EnemyMeleeHeavy: return entry.heavy;
                case AttackSwing.EnemyMeleeUnblockable: return entry.unblockable;
                default: return null; // 突進/投射は剣閃なし。
            }
        }

        /// <summary>攻撃分類ごとの剣閃色（通常／強／ガード不能）。未登録は白（Tint なし）で安全側。</summary>
        private Color ColorForSource(IAttackSwingSource src)
        {
            EnemySlashEntry entry = EntryFor(src);
            if (entry == null)
            {
                return Color.white;
            }

            switch (src.SwingStage)
            {
                case AttackSwing.EnemyMeleeNormal: return entry.normalColor;
                case AttackSwing.EnemyMeleeHeavy: return entry.heavyColor;
                case AttackSwing.EnemyMeleeUnblockable: return entry.unblockableColor;
                default: return Color.white;
            }
        }

        private EnemySlashEntry EntryFor(IAttackSwingSource src)
        {
            if (_entries == null)
            {
                return null;
            }

            string key = (src as IEnemySlashVisual)?.SlashVfxKey;
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i] != null && _entries[i].key == key)
                {
                    return _entries[i];
                }
            }

            return null; // 未登録の敵タイプは無処理。
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
