using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// Phase 3 検証編成の一元管理（Phase3 P3-12。§4）。専用検証 Scene（SCN_Phase3_EnemyTest）の唯一の編成正本。固定シナリオ（近接1／
    /// 遠距離1／強敵1／3体混成）と性能分岐（近接6／混成6／最大8）を明示操作（Context Menu）から開始する。編成変更時は自分が生成した検証敵
    /// だけを即時 非アクティブ化してから破棄し、新編成を専用の子 Transform 配下へリング状に生成する（重複稼働なし・壁非接触・ルート Y=0）。
    /// Scene に手動配置されたオブジェクトには一切触れない（所有・生成した敵のみ管理）。自動開始は既定 OFF（初期の有効な敵は 0 体）。
    /// Phase 5 の Encounter System を先取りしない（単純な生成のみ）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyTestFieldController : MonoBehaviour
    {
        [Tooltip("近接／遠距離／強敵の完成 Prototype Prefab（未割当なら該当分は生成しない）。")]
        [SerializeField] private GameObject _meleePrefab;
        [SerializeField] private GameObject _rangedPrefab;
        [SerializeField] private GameObject _elitePrefab;

        [Tooltip("生成の中心。未指定なら本コンポーネントの Transform を用いる。")]
        [SerializeField] private Transform _spawnCenter;

        [Tooltip("生成リングの半径（m。壁に接触しない広さ）。")]
        [SerializeField] private float _radius = 4f;

        private const string SpawnRootName = "SpawnedEnemies";

        private Transform _spawnRoot;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>本コントローラが生成・所有する検証敵の数（Debug/テスト用）。</summary>
        public int SpawnedCount => _spawned.Count;

        /// <summary>近接 Prefab（テスト用の割当確認）。</summary>
        public GameObject MeleePrefab => _meleePrefab;
        /// <summary>遠距離 Prefab。</summary>
        public GameObject RangedPrefab => _rangedPrefab;
        /// <summary>強敵 Prefab。</summary>
        public GameObject ElitePrefab => _elitePrefab;

        /// <summary>テスト・Editor から Prefab を割り当てる（実行時の結線用）。</summary>
        public void ConfigurePrefabs(GameObject melee, GameObject ranged, GameObject elite)
        {
            _meleePrefab = melee;
            _rangedPrefab = ranged;
            _elitePrefab = elite;
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

        /// <summary>編成を適用する（前回生成分を破棄してから生成）。生成した体数を返す。</summary>
        public int Apply(EnemyTestFormation formation)
        {
            Clear();

            EnemyTestComposition comp = EnemyTestComposition.For(formation);
            int total = Mathf.Max(1, comp.Total);
            int index = 0;
            index = SpawnMany(_meleePrefab, comp.Melee, index, total);
            index = SpawnMany(_rangedPrefab, comp.Ranged, index, total);
            SpawnMany(_elitePrefab, comp.Elite, index, total);
            return _spawned.Count;
        }

        // ---- 明示操作（Play 中にコンポーネント右クリックのコンテキストメニューから実行。Input 依存なし） ----

        /// <summary>0 体（全撤収）。</summary>
        [ContextMenu("Formation / Clear")]
        public void ApplyClear() => Apply(EnemyTestFormation.Clear);

        /// <summary>近接 1 体。</summary>
        [ContextMenu("Formation / 近接1")]
        public void ApplyMelee1() => Apply(EnemyTestFormation.Melee1);

        /// <summary>遠距離 1 体。</summary>
        [ContextMenu("Formation / 遠距離1")]
        public void ApplyRanged1() => Apply(EnemyTestFormation.Ranged1);

        /// <summary>強敵 1 体。</summary>
        [ContextMenu("Formation / 強敵1")]
        public void ApplyElite1() => Apply(EnemyTestFormation.Elite1);

        /// <summary>3 体混成（近接2＋遠距離1）。</summary>
        [ContextMenu("Formation / 3体混成")]
        public void ApplyGroup3() => Apply(EnemyTestFormation.Group3);

        /// <summary>近接 6 体。</summary>
        [ContextMenu("Formation / 近接6")]
        public void ApplyMelee6() => Apply(EnemyTestFormation.Melee6);

        /// <summary>混成 6 体（近接4＋遠距離2）。</summary>
        [ContextMenu("Formation / 混成6")]
        public void ApplyMixed6() => Apply(EnemyTestFormation.Mixed6);

        /// <summary>最大 8 体（近接8）。</summary>
        [ContextMenu("Formation / 最大8")]
        public void ApplyMax8() => Apply(EnemyTestFormation.Max8);

        /// <summary>本コントローラが生成した検証敵を全て即時 非アクティブ化してから破棄する（重複稼働なし）。手動配置物には触れない。</summary>
        public void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                GameObject go = _spawned[i];
                if (go == null)
                {
                    continue;
                }

                go.SetActive(false); // 破棄がフレーム末まで遅延しても、旧敵を即時に止める（重複稼働防止）。
                DestroySpawned(go);
            }

            _spawned.Clear();
        }

        private void OnDisable()
        {
            Clear();
        }

        private int SpawnMany(GameObject prefab, int count, int ringIndex, int total)
        {
            if (prefab == null || count <= 0)
            {
                return ringIndex + Mathf.Max(0, count); // 欠落でもリング位置を詰めない（配置の再現性）。
            }

            Transform root = EnsureSpawnRoot();
            Vector3 center = _spawnCenter != null ? _spawnCenter.position : transform.position;
            center.y = 0f;

            for (int i = 0; i < count; i++)
            {
                float a = (ringIndex / (float)total) * Mathf.PI * 2f;
                var pos = new Vector3(center.x + Mathf.Cos(a) * _radius, 0f, center.z + Mathf.Sin(a) * _radius);
                GameObject go = Instantiate(prefab, pos, Quaternion.identity, root);
                go.transform.position = new Vector3(pos.x, 0f, pos.z); // ルート Y=0 を保証。
                go.SetActive(true); // 生成した検証敵を有効化する（テンプレートの有効状態に依存しない）。
                _spawned.Add(go);
                ringIndex++;
            }

            return ringIndex;
        }

        private static void DestroySpawned(GameObject go)
        {
            // Edit/Play 双方で安全に破棄する（テスト・Editor での即時破棄。gameplay 挙動の分岐ではなくライフサイクル安全）。
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
