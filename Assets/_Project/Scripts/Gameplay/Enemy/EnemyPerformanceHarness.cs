using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵集団戦の性能検証ハーネス（Phase3 P3-11。§「性能分岐：近接6、近接4＋遠距離2、最大8体」）。選んだ <see cref="EnemyPerformanceBranch"/> の
    /// 内訳（<see cref="EnemyPerformanceComposition"/>）ぶんの敵 Prefab を中心の周囲リングへ生成し、切替時に前回分を破棄する検証専用の道具。
    /// Phase 5 の Encounter System を先取りしない（単純な生成のみ）。Prefab 未割当なら該当分は生成しない（欠落でも例外を出さない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyPerformanceHarness : MonoBehaviour
    {
        [Tooltip("生成する分岐。")]
        [SerializeField] private EnemyPerformanceBranch _branch = EnemyPerformanceBranch.Melee6;

        [Tooltip("近接／遠距離／強敵の Prefab（未割当なら該当分は生成しない）。")]
        [SerializeField] private GameObject _meleePrefab;
        [SerializeField] private GameObject _rangedPrefab;
        [SerializeField] private GameObject _elitePrefab;

        [Tooltip("生成リングの半径（m）。")]
        [SerializeField] private float _radius = 6f;

        [Tooltip("開始時に自動生成するか。")]
        [SerializeField] private bool _spawnOnStart = true;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>現在の分岐。</summary>
        public EnemyPerformanceBranch Branch => _branch;

        /// <summary>生成中の体数（Debug/テスト用）。</summary>
        public int SpawnedCount => _spawned.Count;

        private void Start()
        {
            if (_spawnOnStart)
            {
                Spawn(_branch);
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        /// <summary>分岐を切り替えて生成し直す（前回分は破棄）。生成した体数を返す。</summary>
        public int Spawn(EnemyPerformanceBranch branch)
        {
            Clear();
            _branch = branch;

            EnemyPerformanceComposition comp = EnemyPerformanceComposition.For(branch);
            int total = Mathf.Max(1, comp.Total);
            int index = 0;

            index = SpawnMany(_meleePrefab, comp.Melee, ref index, total);
            index = SpawnMany(_rangedPrefab, comp.Ranged, ref index, total);
            SpawnMany(_elitePrefab, comp.Elite, ref index, total);

            return _spawned.Count;
        }

        /// <summary>分岐を設定して生成し直す（インスペクタ・他スクリプトからの切替方法）。</summary>
        public void SetBranch(EnemyPerformanceBranch branch) => Spawn(branch);

        // ---- 編成切替方法（Play 中にコンポーネント右クリックのコンテキストメニューから使える。Input 依存なし） ----

        /// <summary>近接 6 体を生成する。</summary>
        [ContextMenu("Spawn / 近接6")]
        public void SpawnMelee6() => Spawn(EnemyPerformanceBranch.Melee6);

        /// <summary>近接 4＋遠距離 2 体を生成する。</summary>
        [ContextMenu("Spawn / 近接4+遠2")]
        public void SpawnMelee4Ranged2() => Spawn(EnemyPerformanceBranch.Melee4Ranged2);

        /// <summary>最大 8 体を生成する。</summary>
        [ContextMenu("Spawn / 最大8")]
        public void SpawnMax8() => Spawn(EnemyPerformanceBranch.Max8);

        /// <summary>生成済みの敵を全破棄する。</summary>
        [ContextMenu("Clear / 全破棄")]
        public void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Destroy(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        private int SpawnMany(GameObject prefab, int count, ref int ringIndex, int total)
        {
            if (prefab == null || count <= 0)
            {
                ringIndex += Mathf.Max(0, count); // Prefab 欠落でもリング位置は詰めない（配置の再現性）。
                return ringIndex;
            }

            for (int i = 0; i < count; i++)
            {
                float a = (ringIndex / (float)total) * Mathf.PI * 2f;
                Vector3 pos = transform.position + new Vector3(Mathf.Cos(a) * _radius, 0f, Mathf.Sin(a) * _radius);
                GameObject go = Instantiate(prefab, pos, Quaternion.identity);
                _spawned.Add(go);
                ringIndex++;
            }

            return ringIndex;
        }
    }
}
