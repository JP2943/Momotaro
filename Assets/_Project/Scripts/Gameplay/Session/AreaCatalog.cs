using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.World;

namespace Momotaro.Gameplay.Session
{
    /// <summary>解決済みの入口 1 つ（不変）。</summary>
    public readonly struct AreaEntryInfo
    {
        public StableId AreaId { get; }
        public StableId EntryId { get; }
        public CardinalDirection Facing { get; }
        public string ScenePath { get; }

        public AreaEntryInfo(StableId areaId, StableId entryId, CardinalDirection facing, string scenePath)
        {
            AreaId = areaId;
            EntryId = entryId;
            Facing = facing;
            ScenePath = scenePath;
        }

        /// <summary>解決できた入口か（既定値と区別する）。</summary>
        public bool IsValid => !AreaId.IsEmpty && !EntryId.IsEmpty;
    }

    /// <summary>
    /// エリアカタログの不変 Snapshot（P5-02。仕様書 v1.1 §13.3）。
    ///
    /// SO 原本を実行時に読み回さず、<see cref="TryBuild"/> で<b>一度だけ</b>複製して以降はこれを正本にする
    /// （`CLAUDE.md`「SO 原本は実行時に書き換えない。開始時に不変 Snapshot へ複製する」）。
    ///
    /// <b>不正な定義は黙って直さず拒否する。</b> 特に非 0 の FloorId は自動補正して同じ階とみなさない（§3.3）。
    /// 失敗の理由は <paramref name="errors"/> に全件入れるので、Validator と Bridge がそのまま表示できる。
    /// </summary>
    public sealed class AreaCatalog
    {
        private readonly Dictionary<string, string> _scenePaths = new Dictionary<string, string>();
        private readonly Dictionary<string, StableId> _defaultEntries = new Dictionary<string, StableId>();
        private readonly Dictionary<string, AreaEntryInfo> _entries = new Dictionary<string, AreaEntryInfo>();

        private AreaCatalog(StableId respawnAreaId, StableId respawnEntryId)
        {
            RespawnAreaId = respawnAreaId;
            RespawnEntryId = respawnEntryId;
        }

        /// <summary>死亡再開するエリア（§3.1。P5 では固定）。</summary>
        public StableId RespawnAreaId { get; }

        /// <summary>死亡再開する入口。</summary>
        public StableId RespawnEntryId { get; }

        /// <summary>登録エリア数。</summary>
        public int AreaCount => _scenePaths.Count;

        /// <summary>登録入口の総数。</summary>
        public int EntryCount => _entries.Count;

        private static string Key(StableId areaId, StableId entryId) => areaId.Value + "/" + entryId.Value;

        /// <summary>
        /// Data からカタログを構築する。<b>1 件でも不整合があれば構築しない</b>（部分的に使えるカタログを作らない）。
        /// </summary>
        /// <param name="data">カタログ Data。null は失敗。</param>
        /// <param name="catalog">成功時のカタログ。失敗時は null。</param>
        /// <param name="errors">検出した不整合（複数件）。</param>
        /// <returns>構築できたか。</returns>
        public static bool TryBuild(AreaCatalogData data, out AreaCatalog catalog, out IReadOnlyList<string> errors)
        {
            var found = new List<string>();
            catalog = null;

            if (data == null)
            {
                found.Add("Area catalog data is null.");
                errors = found;
                return false;
            }

            var built = new AreaCatalog(data.RespawnAreaId, data.RespawnEntryId);

            foreach (AreaDefinition area in data.Areas)
            {
                if (area == null)
                {
                    found.Add("Catalog contains a null area.");
                    continue;
                }

                if (!area.Id.IsValid)
                {
                    found.Add("Area '" + area.name + "' has an invalid stable id.");
                    continue;
                }

                // Floor は推定も補正もしない。非 0 は不整合として拒否する（§3.3）。
                if (area.FloorId != AreaDefinition.SupportedFloorId)
                {
                    found.Add("Area '" + area.Id.Value + "' has unsupported FloorId " + area.FloorId
                        + " (P5 play areas must be " + AreaDefinition.SupportedFloorId + ").");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(area.ScenePath))
                {
                    found.Add("Area '" + area.Id.Value + "' has no scene path.");
                    continue;
                }

                if (built._scenePaths.ContainsKey(area.Id.Value))
                {
                    found.Add("Duplicate area id '" + area.Id.Value + "'.");
                    continue;
                }

                built._scenePaths.Add(area.Id.Value, area.ScenePath);

                bool defaultFound = false;
                foreach (AreaEntryDefinition entry in area.Entries)
                {
                    if (entry == null || !entry.EntryId.IsValid)
                    {
                        found.Add("Area '" + area.Id.Value + "' has an entry with an invalid stable id.");
                        continue;
                    }

                    string key = Key(area.Id, entry.EntryId);
                    if (built._entries.ContainsKey(key))
                    {
                        found.Add("Duplicate entry id '" + entry.EntryId.Value + "' in area '" + area.Id.Value + "'.");
                        continue;
                    }

                    built._entries.Add(key,
                        new AreaEntryInfo(area.Id, entry.EntryId, entry.Facing, area.ScenePath));

                    if (entry.EntryId.Equals(area.DefaultEntryId))
                    {
                        defaultFound = true;
                    }
                }

                if (!defaultFound)
                {
                    found.Add("Area '" + area.Id.Value + "' default entry '"
                        + area.DefaultEntryId.Value + "' is not among its entries.");
                    continue;
                }

                built._defaultEntries.Add(area.Id.Value, area.DefaultEntryId);
            }

            if (built._scenePaths.Count == 0)
            {
                found.Add("Catalog resolved no usable areas.");
            }

            // 死亡再開点は必ず解決できること（§3.1）。ここが解けないと敗北から復帰できない。
            if (!built._entries.ContainsKey(Key(data.RespawnAreaId, data.RespawnEntryId)))
            {
                found.Add("Respawn point '" + data.RespawnAreaId.Value + "/" + data.RespawnEntryId.Value
                    + "' cannot be resolved.");
            }

            errors = found;
            if (found.Count > 0)
            {
                return false;
            }

            catalog = built;
            return true;
        }

        /// <summary>エリアの Scene パスを解決する。</summary>
        public bool TryGetScenePath(StableId areaId, out string scenePath)
        {
            return _scenePaths.TryGetValue(areaId.Value, out scenePath);
        }

        /// <summary>入口を解決する。</summary>
        public bool TryGetEntry(StableId areaId, StableId entryId, out AreaEntryInfo entry)
        {
            return _entries.TryGetValue(Key(areaId, entryId), out entry);
        }

        /// <summary>Scene を直接開いたときの既定入口を解決する（§5.2）。</summary>
        public bool TryGetDefaultEntry(StableId areaId, out AreaEntryInfo entry)
        {
            entry = default;
            return _defaultEntries.TryGetValue(areaId.Value, out StableId entryId)
                && TryGetEntry(areaId, entryId, out entry);
        }

        /// <summary>死亡再開点を解決する（§9.1）。</summary>
        public bool TryGetRespawnEntry(out AreaEntryInfo entry)
        {
            return TryGetEntry(RespawnAreaId, RespawnEntryId, out entry);
        }
    }
}
