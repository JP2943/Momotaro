using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Progression;
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

        [Header("クリア（P6A）")]
        [Tooltip("初回クリアの殲滅ボーナス（GrantOnce 推奨）。クリア記録・開通と同時に確定する。")]
        [SerializeField] private RewardData _clearReward;

        [Tooltip("クリアで開通させる仕掛けの FlagId（無ければ空）。")]
        [SerializeField] private StableId _unlockFlagId;

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

        /// <summary>初回クリアの殲滅ボーナス（P6A。未設定なら null）。</summary>
        public RewardData ClearReward => _clearReward;

        /// <summary>クリアで開通させる FlagId（P6A。空なら無し）。</summary>
        public StableId UnlockFlagId => _unlockFlagId;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_enemyIds.Count == 0)
            {
                report.Error(name + ": Encounter has no enemy IDs.");
            }

            if (!_unlockFlagId.IsEmpty && !_unlockFlagId.IsValid)
            {
                report.Error(name + ": UnlockFlagId has invalid format '" + _unlockFlagId.Value + "'.");
            }

            for (int i = 0; i < _enemyIds.Count; i++)
            {
                if (!_enemyIds[i].IsValid)
                {
                    report.Error(name + ": EnemyIds[" + i + "] has invalid format '" + _enemyIds[i].Value + "'.");
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用。P6A）。</summary>
        public void EditorSetP6(RewardData clearReward, StableId unlockFlagId, bool isBossEncounter)
        {
            _clearReward = clearReward;
            _unlockFlagId = unlockFlagId;
            _isBossEncounter = isBossEncounter;
        }
#endif
    }
}
