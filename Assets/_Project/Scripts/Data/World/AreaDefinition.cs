using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.World
{
    /// <summary>
    /// エリア 1 つの定義（P5-02。仕様書 v1.1 §3.1／§3.3）。安定 ID・Scene パス・Floor・入口一覧を持つ。
    ///
    /// <b>FloorId は「論理的に同じ階層か」を示す int</b> で、Y 座標や Scene の buildIndex から推定しない（§3.3）。
    /// P5 のプレイ用エリアは <b>0 だけ</b>を許可し、非 0 は不整合として拒否する（自動補正しない）。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Area_New", menuName = "Momotaro/World/Area Definition")]
    public sealed class AreaDefinition : GameDataAsset
    {
        /// <summary>P5 のプレイ用エリアが取れる唯一の Floor（§3.3）。</summary>
        public const int SupportedFloorId = 0;

        [Header("Scene")]
        [Tooltip("このエリアの Scene のアセットパス（Assets/... から始まる）。")]
        [SerializeField] private string _scenePath;

        [Tooltip("論理的な階層。P5 は 0 のみ。Y 座標や buildIndex から推定しない（§3.3）。")]
        [SerializeField] private int _floorId;

        [Header("入口")]
        [Tooltip("このエリアの入口一覧。ID はエリア内で一意。")]
        [SerializeField] private List<AreaEntryDefinition> _entries = new List<AreaEntryDefinition>();

        [Tooltip("Scene を直接開いて Play したときの既定入口（§5.2）。")]
        [SerializeField] private StableId _defaultEntryId;

        /// <summary>Scene のアセットパス。</summary>
        public string ScenePath => _scenePath;

        /// <summary>論理的な階層。</summary>
        public int FloorId => _floorId;

        /// <summary>入口一覧（読み取り専用）。</summary>
        public IReadOnlyList<AreaEntryDefinition> Entries => _entries;

        /// <summary>直接開いたときの既定入口。</summary>
        public StableId DefaultEntryId => _defaultEntryId;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);

            if (string.IsNullOrWhiteSpace(_scenePath))
            {
                report.Error(name + ": Scene Path is empty.");
            }
            else if (!_scenePath.StartsWith("Assets/") || !_scenePath.EndsWith(".unity"))
            {
                report.Error(name + ": Scene Path must be an 'Assets/....unity' path but was '" + _scenePath + "'.");
            }

            if (_floorId != SupportedFloorId)
            {
                report.Error(name + ": FloorId must be " + SupportedFloorId
                    + " for P5 play areas but was " + _floorId + " (spec 3.3).");
            }

            if (_entries.Count == 0)
            {
                report.Error(name + ": Area has no entries.");
                return;
            }

            var seen = new HashSet<string>();
            bool defaultFound = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                AreaEntryDefinition e = _entries[i];
                if (e == null)
                {
                    report.Error(name + ": Entries[" + i + "] is null.");
                    continue;
                }

                if (!e.EntryId.IsValid)
                {
                    report.Error(name + ": Entries[" + i + "] has invalid Stable ID '" + e.EntryId.Value + "'.");
                    continue;
                }

                if (!seen.Add(e.EntryId.Value))
                {
                    report.Error(name + ": Duplicate entry id '" + e.EntryId.Value + "'.");
                }

                if (e.EntryId.Equals(_defaultEntryId))
                {
                    defaultFound = true;
                }
            }

            if (_defaultEntryId.IsEmpty)
            {
                report.Error(name + ": Default Entry Id is empty.");
            }
            else if (!defaultFound)
            {
                report.Error(name + ": Default Entry Id '" + _defaultEntryId.Value + "' is not among the entries.");
            }
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(string scenePath, int floorId, List<AreaEntryDefinition> entries, StableId defaultEntryId)
        {
            _scenePath = scenePath;
            _floorId = floorId;
            _entries = entries ?? new List<AreaEntryDefinition>();
            _defaultEntryId = defaultEntryId;
        }
#endif
    }
}
