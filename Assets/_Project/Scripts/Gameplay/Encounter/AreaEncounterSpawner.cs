using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Scenes;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// P5 の敵生成（§8.2 手順 7・8）。<b>生成と活動許可を分ける</b>のがこの部品の全てで、
    /// 「Spawn 中の敵が先に活動・死亡しないことを保証する」（§8.2 末尾）を構造で守る。
    ///
    /// <b>非活動の親の下へ作る。</b> Unity は <c>Instantiate</c> した瞬間に <c>Awake</c>／<c>OnEnable</c> を走らせるので、
    /// 作ってから <c>SetActive(false)</c> では遅い（索敵レジストリへ登録され、1 フレーム動く）。
    /// 親を先に無効化しておき、全数そろってから親ごと起こす。
    ///
    /// <b>4 Wave の <c>WaveRunner</c> とは別物として置く</b>（§8.1 末尾）。あちらは round-robin で
    /// 出し続ける試遊用で、こちらは「予定した構成をちょうど 1 回出す」。共用すると両方が壊れる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaEncounterSpawner : MonoBehaviour, IEncounterSpawner
    {
        private const string SpawnRootName = "EncounterEnemies";

        [Tooltip("敵の登録先。既存の戦闘セッションをそのまま使う。")]
        [SerializeField] private CombatSessionController _session;

        [Tooltip("EnemyId → Prefab の明示表（§8.1 末尾）。")]
        [SerializeField] private EnemyPrefabTable _table = new EnemyPrefabTable();

        [Tooltip("出現点。EnemyIds の順に対応させる。敵数以上を要求する（§8.1）。")]
        [SerializeField] private List<Transform> _spawnPoints = new List<Transform>();

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<IEnemyDefeatSource> _registered = new List<IEnemyDefeatSource>();
        private Transform _root;

        /// <inheritdoc />
        public int SpawnedCount => _spawned.Count;

        /// <inheritdoc />
        public bool SpawnedActive { get; private set; }

        /// <inheritdoc />
        public IReadOnlyList<IEnemyDefeatSource> Spawned => _registered;

        /// <summary>出現点の数（Validator・診断用）。</summary>
        public int SpawnPointCount => _spawnPoints != null ? _spawnPoints.Count : 0;

        /// <summary>EnemyId→Prefab 表（Validator・診断用）。</summary>
        public EnemyPrefabTable Table => _table;

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _session != null && _table != null && _table.Count > 0 && SpawnPointCount > 0;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(CombatSessionController session, IEnumerable<EnemyPrefabTable.Entry> entries,
            IEnumerable<Transform> spawnPoints)
        {
            if (session != null)
            {
                _session = session;
            }

            if (entries != null)
            {
                _table.SetEntries(entries);
            }

            if (spawnPoints != null)
            {
                _spawnPoints = new List<Transform>();
                foreach (Transform t in spawnPoints)
                {
                    if (t != null)
                    {
                        _spawnPoints.Add(t);
                    }
                }
            }
        }

        /// <inheritdoc />
        public bool TrySpawnAll(in EncounterPlan plan, out string error)
        {
            ReleaseAll();

            if (_session == null)
            {
                error = "敵の登録先（CombatSessionController）が未配線です。";
                return false;
            }

            if (plan.PlannedCount > SpawnPointCount)
            {
                error = "出現点が足りません（予定 " + plan.PlannedCount + " 体、出現点 " + SpawnPointCount + " 個）。";
                return false;
            }

            Transform root = EnsureRoot();
            root.gameObject.SetActive(false); // 起こすのは全数そろってから。

            for (int i = 0; i < plan.PlannedCount; i++)
            {
                StableId enemyId = plan.EnemyIds[i];
                if (!_table.TryResolve(enemyId, out GameObject prefab))
                {
                    error = "敵 '" + enemyId.Value + "' の Prefab を表から解決できません。";
                    ReleaseAll();
                    return false;
                }

                Transform point = _spawnPoints[i];
                if (point == null)
                {
                    error = "出現点 [" + i + "] が未割当です。";
                    ReleaseAll();
                    return false;
                }

                Vector3 at = point.position;
                GameObject go = Instantiate(prefab, new Vector3(at.x, 0f, at.z), point.rotation, root);
                go.name = prefab.name + "_" + i;
                _spawned.Add(go);

                var actor = go.GetComponentInChildren<EnemyActor>(true);
                if (actor == null)
                {
                    error = "敵 '" + enemyId.Value + "' の Prefab に EnemyActor がありません。";
                    ReleaseAll();
                    return false;
                }

                _registered.Add(actor);
                _session.RegisterEnemy(actor);
            }

            error = null;
            return true;
        }

        /// <inheritdoc />
        public event System.Action SpawnedActivated;

        /// <inheritdoc />
        public void ActivateSpawned()
        {
            if (_root == null || _spawned.Count == 0)
            {
                return;
            }

            _root.gameObject.SetActive(true);
            SpawnedActive = true;

            // 起こしてから伝える。購読側は「もう居る」敵を探せる（§8.2 手順 7）。
            SpawnedActivated?.Invoke();
        }

        /// <inheritdoc />
        public void ReleaseAll()
        {
            for (int i = 0; i < _registered.Count; i++)
            {
                _session?.UnregisterEnemy(_registered[i]);
            }

            _registered.Clear();

            for (int i = 0; i < _spawned.Count; i++)
            {
                GameObject go = _spawned[i];
                if (go == null)
                {
                    continue;
                }

                // 破棄がフレーム末まで遅れても、判定と索敵登録はここで止める。
                go.SetActive(false);
                DestroySpawned(go);
            }

            _spawned.Clear();
            SpawnedActive = false;

            if (_root != null)
            {
                DestroySpawned(_root.gameObject);
                _root = null;
            }


            // 残留 Projectile を掃除する（§8.4 手順 5。次 Scene へ飛翔体を持ち越さない）。
            EnemyProjectileRegistry.DespawnAll();
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

        /// <summary>
        /// 生成した敵を置く親を用意する。
        ///
        /// <b>Scene の直下に作る（Area の階層へ入れない）。</b> 主人公の攻撃判定は
        /// 「対象の <c>transform.root</c> が自分と同じなら自分自身」として除外する
        /// （<c>PlayerStateController.PollHitbox</c>）。P5 の Area Scene は主人公も地形も
        /// <c>AreaRoot</c> の子なので、敵をその下へ吊るすと<b>主人公と敵の root が同じになり、
        /// 攻撃が全部「自分」として捨てられる</b>（P08 で実際に踏んだ：判定は 750 フレーム出ていて
        /// 重なりも取れているのに、HP が 1 も減らなかった）。
        /// </summary>
        private Transform EnsureRoot()
        {
            if (_root != null)
            {
                return _root;
            }

            var go = new GameObject(SpawnRootName);
            go.transform.SetParent(null, worldPositionStays: true);
            go.transform.position = Vector3.zero;
            _root = go.transform;
            return _root;
        }

        private void OnDisable()
        {
            ReleaseAll();
        }
    }
}
