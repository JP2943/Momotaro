using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.Events
{
    /// <summary>遭遇（戦闘発生）データ雛形（仕様書 13.8）。出現する敵IDと出現点を持つ。</summary>
    [CreateAssetMenu(fileName = "SO_Encounter_New", menuName = "Momotaro/Data/Events/Encounter Data", order = 0)]
    public sealed class EncounterData : GameDataAsset
    {
        [Header("Encounter")]
        [SerializeField] private List<StableId> _enemyIds = new List<StableId>();
        [SerializeField] private StableId _spawnPointId;
        [SerializeField] private bool _isBossEncounter;

        /// <summary>出現する敵の Stable ID 群。</summary>
        public IReadOnlyList<StableId> EnemyIds => _enemyIds;

        /// <summary>
        /// 出現点集合の安定 ID（P5-07。仕様書 v1.1 §8.1）。
        /// <b>Data 側はこの ID までしか持たない。</b> 実際の Transform は Scene の
        /// EncounterBinding が持つ（共有 Data や常駐 Session へ Transform を保存しない）。
        /// </summary>
        public StableId SpawnPointId => _spawnPointId;

        /// <summary>ボス戦か。</summary>
        public bool IsBossEncounter => _isBossEncounter;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_enemyIds.Count == 0)
            {
                report.Error(name + ": Encounter has no enemy IDs.");
            }

            for (int i = 0; i < _enemyIds.Count; i++)
            {
                if (!_enemyIds[i].IsValid)
                {
                    report.Error(name + ": EnemyIds[" + i + "] has invalid format '" + _enemyIds[i].Value + "'.");
                }
            }
        }
    }
}
