using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Save
{
    /// <summary>1 Area 分の保存記録（不変。P6A-02）。各一覧は序数順に並べてある。</summary>
    public sealed class AreaSaveRecord
    {
        public AreaSaveRecord(string areaId, string[] investigated, string[] openedFlags, string[] clearedEncounters,
            string[] defeatedBosses, string[] pickedPlacements, KeyValuePair<string, int>[] fieldDefeats)
        {
            AreaId = areaId ?? string.Empty;
            Investigated = investigated ?? Array.Empty<string>();
            OpenedFlags = openedFlags ?? Array.Empty<string>();
            ClearedEncounters = clearedEncounters ?? Array.Empty<string>();
            DefeatedBosses = defeatedBosses ?? Array.Empty<string>();
            PickedPlacements = pickedPlacements ?? Array.Empty<string>();
            FieldDefeats = fieldDefeats ?? Array.Empty<KeyValuePair<string, int>>();
        }

        public string AreaId { get; }
        public IReadOnlyList<string> Investigated { get; }
        public IReadOnlyList<string> OpenedFlags { get; }
        public IReadOnlyList<string> ClearedEncounters { get; }
        public IReadOnlyList<string> DefeatedBosses { get; }
        public IReadOnlyList<string> PickedPlacements { get; }

        /// <summary>普通敵の撃破（配置 ID → 撃破した周期）。</summary>
        public IReadOnlyList<KeyValuePair<string, int>> FieldDefeats { get; }

        /// <summary>何も記録が無いか（保存を小さく保つため、空の Area は書かない）。</summary>
        public bool IsEmpty =>
            Investigated.Count == 0 && OpenedFlags.Count == 0 && ClearedEncounters.Count == 0
            && DefeatedBosses.Count == 0 && PickedPlacements.Count == 0 && FieldDefeats.Count == 0;
    }

    /// <summary>
    /// 保存する内容の<b>不変 Snapshot</b>（P6A-02。仕様 §3・§9。<c>P6_SaveInventory.md</c> §5 の DTO）。
    ///
    /// <b>メインスレッドで採り、書込担当スレッドへ渡す。</b> 中身は値と不変の配列だけで、Unity の型・Session への参照を持たない。
    /// 採取後に Session が変わっても、この Snapshot は変わらない（可変参照を共有しない。受入 P6A 13）。
    ///
    /// <b>順序は決定的。</b> 集合はすべて序数順に並べる——同じ世界から同じ JSON が出ないと、
    /// 片側破損の判定（同一世代の内容競合）や差分の比較が成り立たない。
    /// </summary>
    public sealed class SaveSnapshot
    {
        /// <summary>
        /// 保存形式の版（Envelope の schemaVersion）。2：クエスト段階の接続口（questStages）を足した（受入 P6A 08）。
        /// 1 の保存はクエスト段階が空のものとして読む（<see cref="OldestReadableSchemaVersion"/>）。
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>読める最も古い保存形式の版。</summary>
        public const int OldestReadableSchemaVersion = 1;

        public SaveSnapshot(
            string campaignId, int contentVersion, string adventureId, long revision, int respawnCycle,
            int totalVirtue, int spentVirtue, string[] grantedRewards, KeyValuePair<string, int>[] growth,
            string[] visitedAreas, string[] recruited, AreaSaveRecord[] areas,
            KeyValuePair<string, int>[] inventory, int kibidango,
            string[] registeredShrines, string checkpoint, ResumeAnchorKind resumeKind, string resumeAreaId,
            string resumePointId, PartySaveValues party, KeyValuePair<string, int>[] questStages = null)
        {
            CampaignId = campaignId ?? string.Empty;
            ContentVersion = contentVersion;
            AdventureId = adventureId ?? string.Empty;
            Revision = revision;
            RespawnCycle = respawnCycle;
            TotalVirtue = totalVirtue;
            SpentVirtue = spentVirtue;
            GrantedRewards = grantedRewards ?? Array.Empty<string>();
            Growth = growth ?? Array.Empty<KeyValuePair<string, int>>();
            VisitedAreas = visitedAreas ?? Array.Empty<string>();
            Recruited = recruited ?? Array.Empty<string>();
            Areas = areas ?? Array.Empty<AreaSaveRecord>();
            Inventory = inventory ?? Array.Empty<KeyValuePair<string, int>>();
            Kibidango = kibidango;
            RegisteredShrines = registeredShrines ?? Array.Empty<string>();
            Checkpoint = checkpoint ?? string.Empty;
            ResumeKind = resumeKind;
            ResumeAreaId = resumeAreaId ?? string.Empty;
            ResumePointId = resumePointId ?? string.Empty;
            Party = party;
            QuestStages = questStages ?? Array.Empty<KeyValuePair<string, int>>();
        }

        public string CampaignId { get; }
        public int ContentVersion { get; }
        public string AdventureId { get; }

        /// <summary>採取した時点の世界の版。</summary>
        public long Revision { get; }

        public int RespawnCycle { get; }
        public int TotalVirtue { get; }
        public int SpentVirtue { get; }
        public IReadOnlyList<string> GrantedRewards { get; }
        public IReadOnlyList<KeyValuePair<string, int>> Growth { get; }
        public IReadOnlyList<string> VisitedAreas { get; }
        public IReadOnlyList<string> Recruited { get; }
        public IReadOnlyList<AreaSaveRecord> Areas { get; }
        public IReadOnlyList<KeyValuePair<string, int>> Inventory { get; }
        public int Kibidango { get; }
        public IReadOnlyList<string> RegisteredShrines { get; }
        public string Checkpoint { get; }
        public ResumeAnchorKind ResumeKind { get; }
        public string ResumeAreaId { get; }
        public string ResumePointId { get; }
        public PartySaveValues Party { get; }

        /// <summary>クエストの段階（安定 ID 順。P7 の進行の接続口。受入 P6A 08）。</summary>
        public IReadOnlyList<KeyValuePair<string, int>> QuestStages { get; }

        /// <summary>
        /// Session と Actor の値から Snapshot を採る（<b>メインスレッドで</b>。仕様 §9）。
        ///
        /// 呼び出し側の責任：状態更新の途中で呼ばない（命中・死亡・報酬・クリアの調停が終わった区切りで）。
        /// 遷移の途中・死亡の解決中は呼ばない（<c>SaveService</c> が見張る）。
        /// </summary>
        public static SaveSnapshot Capture(GameSessionState session, CampaignCatalog campaign, PartySaveValues party)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (campaign == null)
            {
                throw new ArgumentNullException(nameof(campaign));
            }

            var granted = new List<string>();
            session.Progress.CopyGrantedTo(granted);
            granted.Sort(StringComparer.Ordinal);

            var growth = new List<KeyValuePair<string, int>>();
            session.Progress.CopyGrowthTo(growth);
            growth.Sort(ByKey);

            var areas = new List<AreaSaveRecord>();
            var copy = new AreaRecordCopy();
            foreach (AreaRuntimeState area in session.Areas)
            {
                area.CopyTo(copy);
                var record = new AreaSaveRecord(
                    copy.AreaId,
                    Sorted(copy.Investigated), Sorted(copy.OpenedFlags), Sorted(copy.ClearedEncounters),
                    Sorted(copy.DefeatedBosses), Sorted(copy.PickedPlacements), SortedPairs(copy.FieldDefeats));
                if (!record.IsEmpty)
                {
                    areas.Add(record);
                }
            }

            areas.Sort((a, b) => string.CompareOrdinal(a.AreaId, b.AreaId));

            var inventory = new List<KeyValuePair<string, int>>();
            session.Inventory.CopyTo(inventory);
            inventory.Sort(ByKey);

            var quests = new List<KeyValuePair<string, int>>();
            session.CopyQuestStagesTo(quests);
            quests.Sort(ByKey);

            ResumeAnchor resume = session.Resume;
            return new SaveSnapshot(
                campaign.CampaignId.Value, campaign.ContentVersion, session.AdventureId, session.Changes.Revision,
                session.RespawnCycle, session.Progress.TotalVirtue, session.Progress.SpentVirtue,
                granted.ToArray(), growth.ToArray(),
                SortedIds(session.VisitedAreas), SortedIds(session.Recruited), areas.ToArray(),
                inventory.ToArray(), session.Kibidango,
                SortedIds(session.RegisteredShrines), session.Checkpoint.Value,
                resume.Kind, resume.AreaId.Value, resume.PointId.Value, party, quests.ToArray());
        }

        private static int ByKey(KeyValuePair<string, int> a, KeyValuePair<string, int> b) =>
            string.CompareOrdinal(a.Key, b.Key);

        private static string[] Sorted(List<string> source)
        {
            string[] result = source.ToArray();
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static KeyValuePair<string, int>[] SortedPairs(List<KeyValuePair<string, int>> source)
        {
            KeyValuePair<string, int>[] result = source.ToArray();
            Array.Sort(result, ByKey);
            return result;
        }

        private static string[] SortedIds(IEnumerable<StableId> ids)
        {
            var list = new List<string>();
            foreach (StableId id in ids)
            {
                list.Add(id.Value);
            }

            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }
    }
}
