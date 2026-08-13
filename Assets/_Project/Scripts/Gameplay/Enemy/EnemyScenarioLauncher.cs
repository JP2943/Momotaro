using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>Phase 3 検証シナリオ（Phase3 P3-12。§12「EnemyTest_* 相当」）。固定編成の検証を明示手順から開始できるようにする。</summary>
    public enum EnemyScenario
    {
        /// <summary>近接 1 体。</summary>
        Melee1 = 0,

        /// <summary>遠距離 1 体。</summary>
        Ranged1 = 1,

        /// <summary>強敵 1 体。</summary>
        Elite1 = 2,

        /// <summary>3 体混成（近接 2＋遠距離 1。§12 EnemyTest_Group）。</summary>
        Group3 = 3,
    }

    /// <summary>シナリオごとの内訳（近接／遠距離／強敵の体数）を与える純粋ヘルパ（Phase3 P3-12）。EditMode で決定的に検証できる。</summary>
    public readonly struct EnemyScenarioComposition
    {
        /// <summary>近接体数。</summary>
        public int Melee { get; }
        /// <summary>遠距離体数。</summary>
        public int Ranged { get; }
        /// <summary>強敵体数。</summary>
        public int Elite { get; }

        /// <summary>総体数。</summary>
        public int Total => Melee + Ranged + Elite;

        public EnemyScenarioComposition(int melee, int ranged, int elite)
        {
            Melee = melee;
            Ranged = ranged;
            Elite = elite;
        }

        /// <summary>シナリオから内訳を得る。</summary>
        public static EnemyScenarioComposition For(EnemyScenario scenario)
        {
            switch (scenario)
            {
                case EnemyScenario.Ranged1:
                    return new EnemyScenarioComposition(0, 1, 0);
                case EnemyScenario.Elite1:
                    return new EnemyScenarioComposition(0, 0, 1);
                case EnemyScenario.Group3:
                    return new EnemyScenarioComposition(2, 1, 0);
                default:
                    return new EnemyScenarioComposition(1, 0, 0);
            }
        }
    }

    /// <summary>
    /// Phase 3 の固定検証シナリオ（近接1／遠距離1／強敵1／3体混成）を明示手順から開始する Launcher（Phase3 P3-12。§12）。性能 3 分岐は
    /// <see cref="EnemyPerformanceHarness"/> が担い、本 Launcher は固定編成を担当する。選んだシナリオの敵 Prefab を中心の周囲へ生成し、
    /// 切替時に前回分を破棄する。Phase 5 の Encounter System を先回りしない（単純な生成のみ）。Prefab 未割当なら該当分は生成しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyScenarioLauncher : MonoBehaviour
    {
        [Tooltip("開始するシナリオ。")]
        [SerializeField] private EnemyScenario _scenario = EnemyScenario.Melee1;

        [Tooltip("近接／遠距離／強敵の Prefab（未割当なら該当分は生成しない）。")]
        [SerializeField] private GameObject _meleePrefab;
        [SerializeField] private GameObject _rangedPrefab;
        [SerializeField] private GameObject _elitePrefab;

        [Tooltip("生成リングの半径（m）。")]
        [SerializeField] private float _radius = 4f;

        [Tooltip("開始時に自動生成するか。")]
        [SerializeField] private bool _launchOnStart = false;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>現在のシナリオ。</summary>
        public EnemyScenario Scenario => _scenario;

        /// <summary>生成中の体数（Debug/テスト用）。</summary>
        public int SpawnedCount => _spawned.Count;

        private void Start()
        {
            if (_launchOnStart)
            {
                Launch(_scenario);
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        /// <summary>シナリオを切り替えて生成し直す（前回分は破棄）。生成した体数を返す。</summary>
        public int Launch(EnemyScenario scenario)
        {
            Clear();
            _scenario = scenario;

            EnemyScenarioComposition comp = EnemyScenarioComposition.For(scenario);
            int total = Mathf.Max(1, comp.Total);
            int index = 0;
            index = SpawnMany(_meleePrefab, comp.Melee, index, total);
            index = SpawnMany(_rangedPrefab, comp.Ranged, index, total);
            SpawnMany(_elitePrefab, comp.Elite, index, total);

            return _spawned.Count;
        }

        // ---- 明示手順（Play 中にコンポーネント右クリックのコンテキストメニューから使える。Input 依存なし） ----

        /// <summary>近接 1 体を開始する。</summary>
        [ContextMenu("Launch / 近接1")]
        public void LaunchMelee1() => Launch(EnemyScenario.Melee1);

        /// <summary>遠距離 1 体を開始する。</summary>
        [ContextMenu("Launch / 遠距離1")]
        public void LaunchRanged1() => Launch(EnemyScenario.Ranged1);

        /// <summary>強敵 1 体を開始する。</summary>
        [ContextMenu("Launch / 強敵1")]
        public void LaunchElite1() => Launch(EnemyScenario.Elite1);

        /// <summary>3 体混成（近接2＋遠距離1）を開始する。</summary>
        [ContextMenu("Launch / 3体混成")]
        public void LaunchGroup3() => Launch(EnemyScenario.Group3);

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

        private int SpawnMany(GameObject prefab, int count, int ringIndex, int total)
        {
            if (prefab == null || count <= 0)
            {
                return ringIndex + Mathf.Max(0, count); // 欠落でもリング位置を詰めない（配置の再現性）。
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
