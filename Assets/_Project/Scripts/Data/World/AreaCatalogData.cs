using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.World
{
    /// <summary>
    /// P5 のエリアカタログ（P5-02。仕様書 v1.1 §13.3「カタログから A／B の Scene と全入口を解決できる」）。
    ///
    /// 死亡再開点はここが正本で、P5 では変更できない固定の試作再開点（A の開始点。§3.1）。
    /// お地蔵様の保存・休息機能は先行実装しない。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_AreaCatalog_New", menuName = "Momotaro/World/Area Catalog")]
    public sealed class AreaCatalogData : GameDataAsset
    {
        [Header("エリア")]
        [SerializeField] private List<AreaDefinition> _areas = new List<AreaDefinition>();

        [Header("死亡再開点（P5 では固定）")]
        [SerializeField] private StableId _respawnAreaId;
        [SerializeField] private StableId _respawnEntryId;

        /// <summary>登録エリア（読み取り専用）。</summary>
        public IReadOnlyList<AreaDefinition> Areas => _areas;

        /// <summary>死亡再開するエリアの ID。</summary>
        public StableId RespawnAreaId => _respawnAreaId;

        /// <summary>死亡再開する入口の ID。</summary>
        public StableId RespawnEntryId => _respawnEntryId;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);

            if (_areas.Count == 0)
            {
                report.Error(name + ": Catalog has no areas.");
                return;
            }

            var seen = new HashSet<string>();
            AreaDefinition respawnArea = null;
            for (int i = 0; i < _areas.Count; i++)
            {
                AreaDefinition a = _areas[i];
                if (a == null)
                {
                    report.Error(name + ": Areas[" + i + "] is null.");
                    continue;
                }

                if (!a.Id.IsValid)
                {
                    report.Error(name + ": Areas[" + i + "] has invalid Stable ID.");
                    continue;
                }

                if (!seen.Add(a.Id.Value))
                {
                    report.Error(name + ": Duplicate area id '" + a.Id.Value + "'.");
                }

                if (a.Id.Equals(_respawnAreaId))
                {
                    respawnArea = a;
                }
            }

            if (_respawnAreaId.IsEmpty || _respawnEntryId.IsEmpty)
            {
                report.Error(name + ": Respawn area/entry id is empty (P5 requires a fixed respawn point).");
                return;
            }

            if (respawnArea == null)
            {
                report.Error(name + ": Respawn area '" + _respawnAreaId.Value + "' is not in the catalog.");
                return;
            }

            bool found = false;
            foreach (AreaEntryDefinition e in respawnArea.Entries)
            {
                if (e != null && e.EntryId.Equals(_respawnEntryId))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                report.Error(name + ": Respawn entry '" + _respawnEntryId.Value
                    + "' is not an entry of area '" + _respawnAreaId.Value + "'.");
            }
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(List<AreaDefinition> areas, StableId respawnAreaId, StableId respawnEntryId)
        {
            _areas = areas ?? new List<AreaDefinition>();
            _respawnAreaId = respawnAreaId;
            _respawnEntryId = respawnEntryId;
        }
#endif
    }
}
