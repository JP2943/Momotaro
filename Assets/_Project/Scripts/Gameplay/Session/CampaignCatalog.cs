using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Progression;
using Momotaro.Data.World;
using Momotaro.Gameplay.Progression;

namespace Momotaro.Gameplay.Session
{
    /// <summary>解決済みのお地蔵様 1 体（不変。P6A）。</summary>
    public readonly struct ShrineInfo
    {
        public ShrineInfo(StableId shrineId, AreaEntryInfo entry, string displayName)
        {
            ShrineId = shrineId;
            Entry = entry;
            DisplayName = displayName ?? string.Empty;
        }

        /// <summary>お地蔵様の安定 ID。</summary>
        public StableId ShrineId { get; }

        /// <summary>復帰点（所属エリアの入口）。</summary>
        public AreaEntryInfo Entry { get; }

        /// <summary>所属エリア。</summary>
        public StableId AreaId => Entry.AreaId;

        /// <summary>表示名。</summary>
        public string DisplayName { get; }

        /// <summary>解決できたか。</summary>
        public bool IsValid => !ShrineId.IsEmpty && Entry.IsValid;
    }

    /// <summary>
    /// 死亡再開点の解決（P6A-03。仕様 §5／§6）。<b>P6 campaign は最後に登録したお地蔵様</b>、P5／P5.5 はカタログの固定点。
    /// Scene 側の実行役（<c>CampaignRespawnRunner</c>）と常駐の受付・遷移サービスが同じ規則を使うよう 1 か所に置く。
    /// </summary>
    public static class CampaignRespawnPoint
    {
        /// <summary>再開点を解決する。P6 で Checkpoint が解決できなければ false（固定点へ黙って逃がさない）。</summary>
        public static bool TryResolve(AreaCatalog catalog, GameSessionState session, out AreaEntryInfo entry)
        {
            entry = default;
            if (catalog == null)
            {
                return false;
            }

            CampaignCatalog campaign = catalog.Campaign;
            if (campaign == null)
            {
                return catalog.TryGetRespawnEntry(out entry);
            }

            StableId checkpoint = session != null ? session.Checkpoint : default;
            if (checkpoint.IsEmpty)
            {
                // New Game 前の直開き等。初期お地蔵様へ（開始時の技術用初期値。仕様 §5 末尾）。
                checkpoint = campaign.InitialShrineId;
            }

            if (!campaign.TryGetShrine(checkpoint, out ShrineInfo shrine))
            {
                return false;
            }

            entry = shrine.Entry;
            return true;
        }
    }

    /// <summary>解決済みの成長項目 1 つ（不変。P6A）。</summary>
    public readonly struct GrowthInfo
    {
        public GrowthInfo(StableId growthId, int cost, int maxHpBonus, IReadOnlyList<StableId> prerequisites,
            IReadOnlyList<StableId> exclusives, string displayName)
        {
            GrowthId = growthId;
            Cost = cost;
            MaxHpBonus = maxHpBonus;
            Prerequisites = prerequisites ?? System.Array.Empty<StableId>();
            Exclusives = exclusives ?? System.Array.Empty<StableId>();
            DisplayName = displayName ?? string.Empty;
        }

        public StableId GrowthId { get; }
        public int Cost { get; }
        public int MaxHpBonus { get; }
        public IReadOnlyList<StableId> Prerequisites { get; }
        public IReadOnlyList<StableId> Exclusives { get; }
        public string DisplayName { get; }
    }

    /// <summary>
    /// P6 campaign の不変 Snapshot（P6A）。<see cref="AreaCatalog"/> が P6 campaign のときだけ持つ。
    ///
    /// <b>保存の ID はここで解決する</b>（P6 仕様 §10「IDを保存内のSceneパスやファイルパスへ直接解釈しない。
    /// 許可された campaign カタログで解決する」）。Area・入口は <see cref="AreaCatalog"/> 本体、
    /// お地蔵様・アイテム・成長・初到達報酬はここ。
    /// </summary>
    public sealed class CampaignCatalog
    {
        private readonly Dictionary<string, ShrineInfo> _shrines = new Dictionary<string, ShrineInfo>();
        private readonly List<ShrineInfo> _shrineOrder = new List<ShrineInfo>();
        private readonly Dictionary<string, int> _itemCapacity = new Dictionary<string, int>();
        private readonly Dictionary<string, GrowthInfo> _growth = new Dictionary<string, GrowthInfo>();
        private readonly List<GrowthInfo> _growthOrder = new List<GrowthInfo>();
        private readonly Dictionary<string, RewardSnapshot> _arrivalRewards = new Dictionary<string, RewardSnapshot>();
        private readonly Dictionary<string, AreaContentIds> _content = new Dictionary<string, AreaContentIds>();

        private CampaignCatalog(StableId campaignId, int contentVersion, StableId initialShrineId, int kibidangoBaseCapacity)
        {
            CampaignId = campaignId;
            ContentVersion = contentVersion;
            InitialShrineId = initialShrineId;
            KibidangoBaseCapacity = kibidangoBaseCapacity;
        }

        /// <summary>campaign の安定 ID（＝カタログ Data の ID。保存 Envelope の campaignId）。</summary>
        public StableId CampaignId { get; }

        /// <summary>保存の内容版。</summary>
        public int ContentVersion { get; }

        /// <summary>New Game の初期お地蔵様。</summary>
        public StableId InitialShrineId { get; }

        /// <summary>きびだんごの基本補充上限（仮値）。</summary>
        public int KibidangoBaseCapacity { get; }

        /// <summary>テスト専用：敵の攻撃力の倍率（1 で無効。P6 の検証 campaign だけが 1 以外を持つ）。</summary>
        public float TestEnemyAttackScale { get; private set; } = 1f;

        /// <summary>テスト専用：主人公の基礎最大 HP の倍率（1 で無効）。</summary>
        public float TestPlayerMaxHpScale { get; private set; } = 1f;

        /// <summary>お地蔵様（定義順）。</summary>
        public IReadOnlyList<ShrineInfo> Shrines => _shrineOrder;

        /// <summary>成長項目（定義順）。</summary>
        public IReadOnlyList<GrowthInfo> GrowthNodes => _growthOrder;

        /// <summary>お地蔵様を解決する。</summary>
        public bool TryGetShrine(StableId shrineId, out ShrineInfo shrine)
        {
            shrine = default;
            return !shrineId.IsEmpty && _shrines.TryGetValue(shrineId.Value, out shrine);
        }

        /// <summary>アイテムの上限を解決する（未知 ID は false）。</summary>
        public bool TryGetItemCapacity(StableId itemId, out int maxStack)
        {
            maxStack = 0;
            return !itemId.IsEmpty && _itemCapacity.TryGetValue(itemId.Value, out maxStack);
        }

        /// <summary>成長項目を解決する（未知 ID は false）。</summary>
        public bool TryGetGrowth(StableId growthId, out GrowthInfo growth)
        {
            growth = default;
            return !growthId.IsEmpty && _growth.TryGetValue(growthId.Value, out growth);
        }

        /// <summary>初到達報酬（無ければ <see cref="RewardSnapshot.None"/>）。</summary>
        public RewardSnapshot ArrivalRewardOf(StableId areaId)
        {
            return !areaId.IsEmpty && _arrivalRewards.TryGetValue(areaId.Value, out RewardSnapshot reward)
                ? reward
                : RewardSnapshot.None;
        }

        /// <summary>エリアの保存対象 ID 一覧（未知エリアは false）。</summary>
        public bool TryGetContent(StableId areaId, out AreaContentIds content)
        {
            content = null;
            return !areaId.IsEmpty && _content.TryGetValue(areaId.Value, out content);
        }

        /// <summary>
        /// 取得済み成長から最大 HP の加算を<b>計算し直す</b>（P6 仕様 §7「既存 Max へ加算を繰り返さない。基礎値と取得 ID から再計算」）。
        /// </summary>
        public int MaxHpBonusOf(PlayerProgressState progress)
        {
            if (progress == null)
            {
                return 0;
            }

            int sum = 0;
            for (int i = 0; i < _growthOrder.Count; i++)
            {
                if (progress.HasGrowth(_growthOrder[i].GrowthId))
                {
                    sum += _growthOrder[i].MaxHpBonus;
                }
            }

            return sum;
        }

        /// <summary>きびだんごの現在の補充上限（基本値。成長での拡張は P6B）。</summary>
        public int KibidangoCapacityOf(PlayerProgressState progress) => KibidangoBaseCapacity;

        /// <summary>
        /// Data から構築する。<b>1 件でも不整合があれば構築しない</b>（<see cref="AreaCatalog.TryBuild"/> と同じ方針）。
        /// </summary>
        internal static CampaignCatalog Build(AreaCatalogData data, AreaCatalog areas, List<string> errors)
        {
            int before = errors.Count;
            var built = new CampaignCatalog(data.Id, data.ContentVersion, data.InitialShrineId, data.KibidangoBaseCapacity)
            {
                TestEnemyAttackScale = data.TestEnemyAttackScale,
                TestPlayerMaxHpScale = data.TestPlayerMaxHpScale,
            };

            if (!data.Id.IsValid)
            {
                errors.Add("P6 campaign catalog has no valid stable id.");
            }

            if (data.ContentVersion < 1)
            {
                errors.Add("P6 campaign ContentVersion must be >= 1.");
            }

            if (data.KibidangoBaseCapacity < 0)
            {
                errors.Add("P6 campaign KibidangoBaseCapacity must be >= 0.");
            }

            foreach (ShrineDefinition def in data.Shrines)
            {
                if (def == null || !def.ShrineId.IsValid)
                {
                    errors.Add("Campaign contains a shrine with an invalid stable id.");
                    continue;
                }

                if (built._shrines.ContainsKey(def.ShrineId.Value))
                {
                    errors.Add("Duplicate shrine id '" + def.ShrineId.Value + "'.");
                    continue;
                }

                if (!areas.TryGetEntry(def.AreaId, def.EntryId, out AreaEntryInfo entry))
                {
                    errors.Add("Shrine '" + def.ShrineId.Value + "' entry '" + def.AreaId.Value + "/"
                        + def.EntryId.Value + "' cannot be resolved.");
                    continue;
                }

                var info = new ShrineInfo(def.ShrineId, entry, def.DisplayName);
                built._shrines.Add(def.ShrineId.Value, info);
                built._shrineOrder.Add(info);
            }

            if (!built._shrines.ContainsKey(data.InitialShrineId.Value ?? string.Empty))
            {
                errors.Add("Initial shrine '" + data.InitialShrineId.Value + "' cannot be resolved.");
            }

            foreach (ItemDefinition item in data.Items)
            {
                if (item == null || !item.ItemId.IsValid || item.MaxStack < 1)
                {
                    errors.Add("Campaign contains an invalid item definition.");
                    continue;
                }

                if (built._itemCapacity.ContainsKey(item.ItemId.Value))
                {
                    errors.Add("Duplicate item id '" + item.ItemId.Value + "'.");
                    continue;
                }

                built._itemCapacity.Add(item.ItemId.Value, item.MaxStack);
            }

            foreach (SkillNodeData node in data.GrowthNodes)
            {
                if (node == null || !node.Id.IsValid || node.VirtueCost < 0 || node.MaxHpBonus < 0)
                {
                    errors.Add("Campaign contains an invalid growth node.");
                    continue;
                }

                if (built._growth.ContainsKey(node.Id.Value))
                {
                    errors.Add("Duplicate growth id '" + node.Id.Value + "'.");
                    continue;
                }

                var info = new GrowthInfo(node.Id, node.VirtueCost, node.MaxHpBonus,
                    IdsOf(node.Prerequisites), IdsOf(node.MutuallyExclusive), node.DisplayName);
                built._growth.Add(node.Id.Value, info);
                built._growthOrder.Add(info);
            }

            foreach (AreaDefinition area in data.Areas)
            {
                if (area == null || !area.Id.IsValid)
                {
                    continue;
                }

                if (area.ArrivalReward != null)
                {
                    built._arrivalRewards[area.Id.Value] = RewardSnapshot.From(area.ArrivalReward);
                }

                built._content[area.Id.Value] = AreaContentIds.From(area.Content);
            }

            return errors.Count == before ? built : null;
        }

        private static IReadOnlyList<StableId> IdsOf(IReadOnlyList<SkillNodeData> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return System.Array.Empty<StableId>();
            }

            var ids = new List<StableId>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null)
                {
                    ids.Add(nodes[i].Id);
                }
            }

            return ids;
        }
    }

    /// <summary>エリアの保存対象 ID 一覧（不変。P6A-02）。</summary>
    public sealed class AreaContentIds
    {
        private AreaContentIds()
        {
        }

        public HashSet<string> Encounters { get; } = new HashSet<string>();
        public HashSet<string> Bosses { get; } = new HashSet<string>();
        public HashSet<string> FieldPlacements { get; } = new HashSet<string>();
        public HashSet<string> Pickups { get; } = new HashSet<string>();
        public HashSet<string> Flags { get; } = new HashSet<string>();
        public HashSet<string> InvestigationPoints { get; } = new HashSet<string>();

        internal static AreaContentIds From(AreaContentManifest manifest)
        {
            var ids = new AreaContentIds();
            if (manifest == null)
            {
                return ids;
            }

            Fill(ids.Encounters, manifest.Encounters);
            Fill(ids.Bosses, manifest.Bosses);
            Fill(ids.FieldPlacements, manifest.FieldPlacements);
            Fill(ids.Pickups, manifest.Pickups);
            Fill(ids.Flags, manifest.Flags);
            Fill(ids.InvestigationPoints, manifest.InvestigationPoints);
            return ids;
        }

        private static void Fill(HashSet<string> target, IReadOnlyList<StableId> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (!source[i].IsEmpty)
                {
                    target.Add(source[i].Value);
                }
            }
        }
    }
}
