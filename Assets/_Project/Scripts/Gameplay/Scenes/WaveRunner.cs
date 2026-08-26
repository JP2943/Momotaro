using System;
using System.Collections.Generic;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 連続ウェーブ進行の駆動（Phase3.5 P3.5-07。仕様書 §8.2 / §8.3）。純粋な時間・段階モデル <see cref="WaveSequencer"/> を、
    /// 敵生成（固定 Spawn Point・round-robin）・<see cref="CombatSessionController"/> の状態遷移と生存数・Wave 間の残留 Cleanup と
    /// Player 全回復／中立化・HUD への現在 Wave 通知へ結線する MonoBehaviour。Wave 定義は Serialized（<see cref="WaveDefinition"/> 配列）で持ち、
    /// Controller へ敵名・座標を直書きしない。実際の勝利パネル・入力ロック・Retry は先回りせず、最終 Wave 完了は <see cref="AllWavesCleared"/> で
    /// 通知して P3.5-08 に委ねる。
    ///
    /// ライフサイクル：<see cref="CombatSessionController.AllEnemiesDefeated"/> を購読して全滅を検出し、1.0s 後に Session を Intermission へ、
    /// 3.0s 後に次 Wave を engage する（§8.3）。時間は Game Time（Pause で停止）。Disable／Scene 離脱で生成敵・Projectile・購読を残さない（§2.3）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaveRunner : MonoBehaviour
    {
        [Header("Session / Player 配線（未設定なら有効化時に自動探索）")]
        [SerializeField] private CombatSessionController _session;
        [SerializeField] private PlayerStateController _playerState;
        [SerializeField] private PlayerVitalsHolder _playerVitals;
        [SerializeField] private PlayerHitReaction _playerHurt;

        [Header("敵 Prefab（近接=骸骨剣士 / 遠距離=骸骨弓兵 / 強敵=侍骸骨）")]
        [SerializeField] private GameObject _meleePrefab;
        [SerializeField] private GameObject _rangedPrefab;
        [SerializeField] private GameObject _elitePrefab;

        [Header("固定 Spawn Point（Player と重ならず Camera 内。round-robin で配置）")]
        [SerializeField] private Transform[] _spawnPoints;

        [Header("Wave 構成（§8.2 Table7。既定：剣士1 / 弓兵1 / 剣士2+弓兵1 / 侍骸骨1）")]
        [SerializeField] private WaveDefinition[] _waves =
        {
            new WaveDefinition(1, 0, 0),
            new WaveDefinition(0, 1, 0),
            new WaveDefinition(2, 1, 0),
            new WaveDefinition(0, 0, 1),
        };

        [Header("進行時間（§8.3。全滅→休止 1.0s、休止 3.0s）")]
        [SerializeField] private float _postClearDelay = 1.0f;
        [SerializeField] private float _intermissionDelay = 3.0f;

        [Tooltip("有効化時に Preparing から自動で Wave1 を開始する。決定入力ゲート（§5.2）は P3.5-08 が付与する。")]
        [SerializeField] private bool _autoStart = true;

        private const string SpawnRootName = "WaveEnemies";

        private WaveSequencer _seq;
        private Transform _spawnRoot;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private int _spawnCursor;
        private bool _subscribed;
        private bool _startRequested;
        private bool _missingPrefabWarned;

        /// <summary>現在 Wave 番号（1 始まり。未開始 0）。HUD／Debug 参照。</summary>
        public int CurrentWave => _seq != null ? _seq.CurrentWaveNumber : 0;

        /// <summary>Wave 総数。</summary>
        public int WaveCount => _waves != null ? _waves.Length : 0;

        /// <summary>固定 Spawn Point 数（配線確認・テスト用）。</summary>
        public int SpawnPointCount => _spawnPoints != null ? _spawnPoints.Length : 0;

        /// <summary>近接 Prefab（配線確認・テスト用）。</summary>
        public GameObject MeleePrefab => _meleePrefab;

        /// <summary>遠距離 Prefab（配線確認・テスト用）。</summary>
        public GameObject RangedPrefab => _rangedPrefab;

        /// <summary>強敵 Prefab（配線確認・テスト用）。</summary>
        public GameObject ElitePrefab => _elitePrefab;

        /// <summary>本 Runner が生成・所有する敵数（Debug／テスト用）。</summary>
        public int SpawnedCount => _spawned.Count;

        /// <summary>Wave が engage された瞬間に発火（1 始まり番号）。HUD（P3.5-04）が購読して現在 Wave を更新する。</summary>
        public event Action<int> WaveChanged;

        /// <summary>全 Wave 完了時に一度だけ発火。P3.5-08 が Victory 遷移・パネルへ接続する。</summary>
        public event Action AllWavesCleared;

        // ---- 配線（Scene Builder・テストが注入） ----

        /// <summary>敵 Prefab を割り当てる（近接／遠距離／強敵）。</summary>
        public void ConfigurePrefabs(GameObject melee, GameObject ranged, GameObject elite)
        {
            _meleePrefab = melee;
            _rangedPrefab = ranged;
            _elitePrefab = elite;
        }

        /// <summary>固定 Spawn Point を割り当てる。</summary>
        public void ConfigureSpawnPoints(Transform[] spawnPoints)
        {
            _spawnPoints = spawnPoints;
        }

        /// <summary>Wave 構成を割り当てる（§8.2）。</summary>
        public void ConfigureWaves(WaveDefinition[] waves)
        {
            _waves = waves;
        }

        /// <summary>Session／Player を注入する（null は無視して既存を保つ）。</summary>
        public void Bind(CombatSessionController session, PlayerStateController playerState,
            PlayerVitalsHolder playerVitals, PlayerHitReaction playerHurt)
        {
            if (session != null)
            {
                _session = session;
            }

            if (playerState != null)
            {
                _playerState = playerState;
            }

            if (playerVitals != null)
            {
                _playerVitals = playerVitals;
            }

            if (playerHurt != null)
            {
                _playerHurt = playerHurt;
            }
        }

        // ---- ライフサイクル ----

        private void OnEnable()
        {
            EnsureSequencer();
            Subscribe();
        }

        private void Start()
        {
            if (_autoStart)
            {
                RequestStartWave();
            }
        }

        private void Update()
        {
            if (_seq == null)
            {
                return;
            }

            // 敗北・勝利・再読込・未 Bind の停止段では時間を進めない（休止/休止入りタイマを凍結）。
            if (_session != null)
            {
                CombatSessionState s = _session.State;
                if (s == CombatSessionState.Defeat || s == CombatSessionState.Victory || s == CombatSessionState.Reloading)
                {
                    return;
                }
            }

            _seq.Tick(Time.deltaTime); // Game Time（Pause=timeScale0 で停止）。
        }

        private void OnDisable()
        {
            Unsubscribe();
            CleanupSpawned();
            _seq = null;
            _startRequested = false;
        }

        /// <summary>Wave1 を開始する（Preparing → Playing）。決定入力ゲート（P3.5-08）はこの入口を呼ぶだけで済む。二重開始は無視。</summary>
        public void RequestStartWave()
        {
            EnsureSequencer();
            if (_startRequested)
            {
                return;
            }

            _startRequested = true;
            _seq.Begin();
        }

        // ---- Sequencer 配線 ----

        private void EnsureSequencer()
        {
            if (_seq != null)
            {
                return;
            }

            int count = _waves != null ? _waves.Length : 0;
            _seq = new WaveSequencer(count, _postClearDelay, _intermissionDelay);
            _seq.WaveEngaged += OnWaveEngaged;
            _seq.IntermissionEntered += OnIntermissionEntered;
            _seq.AllWavesCleared += OnAllWavesCleared;
        }

        private void Subscribe()
        {
            if (_subscribed || _session == null)
            {
                return;
            }

            _session.AllEnemiesDefeated += OnAllEnemiesDefeated;

            // Player 死亡 → Session Defeat の結線は Runtime 専用（チャネルは非 Serialized のため Scene に焼けない）。
            // WaveRunner が Session/Player の双方参照を持つため、ここで一度だけ結線する（同一チャネルの再 Bind は Session 側で無視）。
            if (_playerVitals != null)
            {
                _session.BindPlayerDefeat(_playerVitals.Defeats);
            }

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _session == null)
            {
                return;
            }

            _session.AllEnemiesDefeated -= OnAllEnemiesDefeated;
            _subscribed = false;
        }

        private void OnAllEnemiesDefeated()
        {
            _seq?.NotifyWaveCleared();
        }

        private void OnWaveEngaged(int waveNumber)
        {
            SpawnWave(waveNumber - 1);
            _session?.StartWave(); // Preparing/Intermission → Playing。
            WaveChanged?.Invoke(waveNumber);

            // 生成 0 体（構成空・Prefab 欠落）でも進行が止まらないよう、その場で全滅扱いにする（§Spawn 失敗）。
            if (_session != null && _session.AliveEnemyCount == 0)
            {
                _seq?.NotifyWaveCleared();
            }
        }

        private void OnIntermissionEntered()
        {
            _session?.ToIntermission();      // Playing → Intermission。
            CleanupSpawned();                // 残留敵を破棄し Session 登録を解除（§5.2 / §8.3）。
            EnemyProjectileRegistry.DespawnAll(); // 残留 Projectile を掃除。
            RecoverPlayer();                 // HP/Stamina 全回復・中立化・Special0（§8.3 試遊仮仕様）。
        }

        private void OnAllWavesCleared()
        {
            AllWavesCleared?.Invoke(); // Session は Playing のまま。Victory 遷移・パネルは P3.5-08。
        }

        // ---- 敵生成・Cleanup ----

        private void SpawnWave(int index)
        {
            if (_waves == null || index < 0 || index >= _waves.Length)
            {
                return;
            }

            WaveDefinition w = _waves[index];
            _spawnCursor = 0;
            SpawnKind(_meleePrefab, w.melee);
            SpawnKind(_rangedPrefab, w.ranged);
            SpawnKind(_elitePrefab, w.elite);
        }

        private void SpawnKind(GameObject prefab, int count)
        {
            if (count <= 0)
            {
                return;
            }

            if (prefab == null)
            {
                WarnMissingPrefabOnce();
                return;
            }

            Transform root = EnsureSpawnRoot();
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = NextSpawnPosition();
                GameObject go = Instantiate(prefab, pos, Quaternion.identity, root);
                go.transform.position = new Vector3(pos.x, 0f, pos.z); // ルート Y=0 を保証。
                go.SetActive(true);
                _spawned.Add(go);

                var actor = go.GetComponentInChildren<EnemyActor>(true);
                if (actor != null)
                {
                    _session?.RegisterEnemy(actor);
                }
            }
        }

        private Vector3 NextSpawnPosition()
        {
            if (_spawnPoints != null && _spawnPoints.Length > 0)
            {
                Transform p = _spawnPoints[_spawnCursor % _spawnPoints.Length];
                _spawnCursor++;
                if (p != null)
                {
                    Vector3 sp = p.position;
                    return new Vector3(sp.x, 0f, sp.z);
                }
            }

            // Spawn Point 未設定時のフォールバック（本体前方に軽く展開。決定的）。
            int k = _spawnCursor++;
            float x = ((k % 3) - 1) * 2.5f;
            float z = 4f + (k / 3) * 2.5f;
            return new Vector3(transform.position.x + x, 0f, transform.position.z + z);
        }

        private Transform EnsureSpawnRoot()
        {
            if (_spawnRoot != null)
            {
                return _spawnRoot;
            }

            Transform existing = transform.Find(SpawnRootName);
            if (existing != null)
            {
                _spawnRoot = existing;
                return _spawnRoot;
            }

            var go = new GameObject(SpawnRootName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            _spawnRoot = go.transform;
            return _spawnRoot;
        }

        /// <summary>生成した敵を全て破棄し、Session の登録・生存数を初期化する（Wave 間・Disable。二重呼び出し安全）。</summary>
        private void CleanupSpawned()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                GameObject go = _spawned[i];
                if (go == null)
                {
                    continue;
                }

                go.SetActive(false); // 破棄がフレーム末まで遅延しても即時に止める。
                DestroySpawned(go);
            }

            _spawned.Clear();
            _session?.ClearEnemies();
        }

        private void RecoverPlayer()
        {
            // 状態・攻撃・Step・入力 Buffer の中立化＋Special Charge 0（§8.3）。
            _playerState?.ResetToNeutral();
            // HP／Stamina 全回復＋GuardBreak 解除（§8.3 試遊仮仕様）。
            _playerVitals?.RestoreForWaveRecovery();
            // Hurt／被弾後無敵の解除。
            _playerHurt?.ResetHurt();
        }

        private void WarnMissingPrefabOnce()
        {
            if (_missingPrefabWarned)
            {
                return;
            }

            _missingPrefabWarned = true;
            Debug.LogWarning("[WaveRunner] 敵 Prefab が未割当のため、その分の生成をスキップしました。", this);
        }

        private static void DestroySpawned(GameObject go)
        {
            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }
    }
}
