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
            : this(growthId, cost, new GrowthEffects(maxHpBonus, 0f, 0, 0f, 0, 0), 0, prerequisites, exclusives,
                displayName, string.Empty)
        {
        }

        public GrowthInfo(StableId growthId, int cost, GrowthEffects effects, int tier, IReadOnlyList<StableId> prerequisites,
            IReadOnlyList<StableId> exclusives, string displayName, string description)
        {
            GrowthId = growthId;
            Cost = cost;
            Effects = effects;
            Tier = tier;
            Description = description ?? string.Empty;
            MaxHpBonus = effects.MaxHpBonus;
            Prerequisites = prerequisites ?? System.Array.Empty<StableId>();
            Exclusives = exclusives ?? System.Array.Empty<StableId>();
            DisplayName = displayName ?? string.Empty;
        }

        public StableId GrowthId { get; }
        public int Cost { get; }
        public int MaxHpBonus { get; }

        /// <summary>このノード 1 つの効果（P6B）。</summary>
        public GrowthEffects Effects { get; }

        /// <summary>階層（表示用。保存形式には持たない）。</summary>
        public int Tier { get; }

        /// <summary>説明（表示用）。</summary>
        public string Description { get; }

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
        private readonly HashSet<string> _knownRewards = new HashSet<string>();
        private readonly HashSet<string> _knownCompanions = new HashSet<string>();
        private readonly HashSet<string> _knownQuests = new HashSet<string>();
        private readonly HashSet<string> _knownChapters = new HashSet<string>();
        private readonly HashSet<string> _storyQuests = new HashSet<string>();

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

        /// <summary>
        /// この campaign で付与されうる一度きり報酬の ID か（保存の検証。レビュー R4）。初到達報酬と Data の一覧の和。
        /// </summary>
        public bool IsKnownGrantOnceReward(StableId rewardId) => !rewardId.IsEmpty && _knownRewards.Contains(rewardId.Value);

        /// <summary>この campaign で加入しうる仲間の ID か（保存の検証。レビュー R4）。</summary>
        public bool IsKnownCompanion(StableId companionId) => !companionId.IsEmpty && _knownCompanions.Contains(companionId.Value);

        /// <summary>この campaign の保存が持ちうるクエストの ID か（P7 の接続口。受入 P6A 08）。</summary>
        public bool IsKnownQuest(StableId questId) => !questId.IsEmpty && _knownQuests.Contains(questId.Value);

        /// <summary>
        /// P7 の依頼（段階が 0＝未受注・1＝受注済み・2＝受領済みの意味を持つ）か。P6A の接続 fixture のような
        /// 段階の意味を持たないクエストは false（保存の検証で「0 以上の整数」のまま扱う）。
        /// </summary>
        public bool IsStoryQuest(StableId questId) => !questId.IsEmpty && _storyQuests.Contains(questId.Value);

        /// <summary>会話・依頼・必須イベント・章（P7。Data に無ければ null）。</summary>
        public Momotaro.Gameplay.Story.StoryCatalog Story { get; private set; }

        /// <summary>エリアの保存対象 ID 一覧（未知エリアは null）。</summary>
        private AreaContentIds ContentOf(StableId areaId) =>
            !areaId.IsEmpty && _content.TryGetValue(areaId.Value, out AreaContentIds ids) ? ids : null;

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

        /// <summary>
        /// 取得済み成長の効果を<b>基礎値（0）から作り直す</b>（P6B 01。仕様 §3「基礎値＋取得済み効果から毎回再構築」）。
        /// 定義順に足すだけで、取得順・既存の値には依存しない。
        /// </summary>
        public GrowthEffects GrowthEffectsOf(PlayerProgressState progress)
        {
            GrowthEffects sum = GrowthEffects.None;
            if (progress == null)
            {
                return sum;
            }

            for (int i = 0; i < _growthOrder.Count; i++)
            {
                GrowthInfo g = _growthOrder[i];
                if (progress.HasGrowth(g.GrowthId))
                {
                    GrowthEffects e = g.Effects;
                    sum = sum.Plus(e.MaxHpBonus, e.AttackHpMultiplierBonus, e.MaxStaminaBonus,
                        e.NormalPoiseMultiplierBonus, e.KibidangoHealBonus, e.KibidangoCapacityBonus);
                }
            }

            return sum;
        }

        /// <summary>きびだんごの現在の補充上限（基本値＋成長。P6B）。</summary>
        public int KibidangoCapacityOf(PlayerProgressState progress) =>
            KibidangoBaseCapacity + GrowthEffectsOf(progress).KibidangoCapacityBonus;

        /// <summary>
        /// 取得 ID の集合だけから上限を求める（保存の検証用。Session を作る前に使う）。未知 ID は数えない。
        /// </summary>
        public int KibidangoCapacityOf(IEnumerable<string> growthIds)
        {
            int capacity = KibidangoBaseCapacity;
            if (growthIds == null)
            {
                return capacity;
            }

            foreach (string id in growthIds)
            {
                if (id != null && _growth.TryGetValue(id, out GrowthInfo g))
                {
                    capacity += g.Effects.KibidangoCapacityBonus;
                }
            }

            return capacity;
        }

        /// <summary>きびだんご 1 個の回復量（基礎の固定値＋成長。最大 HP には連動しない。P6B）。</summary>
        public int KibidangoHealOf(PlayerProgressState progress) =>
            KibidangoBaseHeal + GrowthEffectsOf(progress).KibidangoHealBonus;

        /// <summary>きびだんご 1 個の基礎回復量（0＝この campaign に使用動作は無い＝P6A）。</summary>
        public int KibidangoBaseHeal { get; private set; }

        /// <summary>きびだんごを使用できる campaign か（P6B）。</summary>
        public bool HasKibidangoUse => KibidangoBaseHeal > 0;

        /// <summary>きびだんご使用の全動作（Gameplay 秒）。</summary>
        public float KibidangoUseSeconds { get; private set; } = 2f;

        /// <summary>きびだんご使用の確定時刻（Gameplay 秒）。</summary>
        public float KibidangoCommitSeconds { get; private set; } = 1.5f;

        /// <summary>きびだんご使用中の移動速度倍率。</summary>
        public float KibidangoMoveSpeedMultiplier { get; private set; } = 0.2f;

        /// <summary>払い戻しの初期権利。</summary>
        public int RefundRightsInitial { get; private set; } = 3;

        /// <summary>章クリア 1 回の追加権利。</summary>
        public int RefundRightsPerChapter { get; private set; } = 3;

        /// <summary>払い戻し権利の上限。</summary>
        public int RefundRightsMax { get; private set; } = 6;

        /// <summary>保存スロット名（campaign ごと）。</summary>
        public string SaveSlotName { get; private set; } = "slot0";

        /// <summary>権利を追加しうる章の ID か（P6B。保存の検証と章接続口が使う）。</summary>
        public bool IsKnownChapter(StableId chapterId) => !chapterId.IsEmpty && _knownChapters.Contains(chapterId.Value);

        /// <summary>
        /// この成長に依存している（前提に挙げている）<b>取得済み</b>ノードがあるか（P6B 05。払い戻しは末端だけ）。
        /// </summary>
        public bool HasAcquiredDependent(PlayerProgressState progress, StableId growthId)
        {
            if (progress == null || growthId.IsEmpty)
            {
                return false;
            }

            for (int i = 0; i < _growthOrder.Count; i++)
            {
                GrowthInfo g = _growthOrder[i];
                if (!progress.HasGrowth(g.GrowthId))
                {
                    continue;
                }

                for (int p = 0; p < g.Prerequisites.Count; p++)
                {
                    if (g.Prerequisites[p].Equals(growthId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

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
                KibidangoBaseHeal = data.KibidangoBaseHeal,
                KibidangoUseSeconds = data.KibidangoUseSeconds,
                KibidangoCommitSeconds = data.KibidangoCommitSeconds,
                KibidangoMoveSpeedMultiplier = data.KibidangoMoveSpeedMultiplier,
                RefundRightsInitial = data.RefundRightsInitial,
                RefundRightsPerChapter = data.RefundRightsPerChapter,
                RefundRightsMax = data.RefundRightsMax,
                SaveSlotName = data.SaveSlotName,
            };

            if (data.KibidangoBaseHeal < 0)
            {
                errors.Add("P6 campaign KibidangoBaseHeal must be >= 0.");
            }

            if (data.KibidangoBaseHeal > 0
                && !(data.KibidangoCommitSeconds > 0f && data.KibidangoUseSeconds >= data.KibidangoCommitSeconds
                     && !float.IsInfinity(data.KibidangoUseSeconds)
                     && data.KibidangoMoveSpeedMultiplier >= 0f && data.KibidangoMoveSpeedMultiplier <= 1f))
            {
                errors.Add("P6 campaign kibidango use timing is invalid.");
            }

            if (data.RefundRightsInitial < 0 || data.RefundRightsPerChapter < 0
                || data.RefundRightsInitial > data.RefundRightsMax)
            {
                errors.Add("P6 campaign refund rights are invalid.");
            }

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
                if (node == null || !node.Id.IsValid || node.VirtueCost < 0 || node.MaxHpBonus < 0
                    || node.MaxStaminaBonus < 0 || node.KibidangoHealBonus < 0 || node.KibidangoCapacityBonus < 0
                    || !(node.AttackHpMultiplierBonus >= 0f) || float.IsInfinity(node.AttackHpMultiplierBonus)
                    || !(node.NormalPoiseMultiplierBonus >= 0f) || float.IsInfinity(node.NormalPoiseMultiplierBonus)
                    || !node.HasAnyEffect)
                {
                    errors.Add("Campaign contains an invalid growth node.");
                    continue;
                }

                if (built._growth.ContainsKey(node.Id.Value))
                {
                    errors.Add("Duplicate growth id '" + node.Id.Value + "'.");
                    continue;
                }

                var effects = new GrowthEffects(node.MaxHpBonus, node.AttackHpMultiplierBonus, node.MaxStaminaBonus,
                    node.NormalPoiseMultiplierBonus, node.KibidangoHealBonus, node.KibidangoCapacityBonus);
                var info = new GrowthInfo(node.Id, node.VirtueCost, effects, node.Tier,
                    IdsOf(node.Prerequisites), IdsOf(node.MutuallyExclusive), node.DisplayName, node.Description);
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
                    RewardSnapshot arrival = RewardSnapshot.From(area.ArrivalReward);
                    built._arrivalRewards[area.Id.Value] = arrival;
                    if (arrival.GrantOnce && !arrival.RewardId.IsEmpty)
                    {
                        built._knownRewards.Add(arrival.RewardId.Value);
                    }
                }

                built._content[area.Id.Value] = AreaContentIds.From(area.Content);
            }

            AddKnown(built._knownRewards, data.GrantOnceRewardIds, "reward", errors);
            AddKnown(built._knownCompanions, data.CompanionIds, "companion", errors);
            AddKnown(built._knownQuests, data.QuestIds, "quest", errors);
            AddKnown(built._knownChapters, data.ChapterIds, "chapter", errors);
            SkillGraphCheck.Check(data.GrowthNodes, errors);

            // P7：会話・依頼・必須イベント・章。依頼 ID・依頼報酬 ID・章 ID は既知 ID に加える（Data へ二重に書かせない）。
            if (data.Story != null)
            {
                built.Story = Momotaro.Gameplay.Story.StoryCatalog.Build(data.Story, areas, built.ContentOf, errors);
                if (built.Story != null)
                {
                    foreach (Momotaro.Gameplay.Story.QuestInfo quest in built.Story.Quests)
                    {
                        built._knownQuests.Add(quest.QuestId.Value);
                        built._storyQuests.Add(quest.QuestId.Value);
                        if (quest.Reward.GrantOnce && !quest.Reward.RewardId.IsEmpty)
                        {
                            built._knownRewards.Add(quest.Reward.RewardId.Value);
                        }
                    }

                    foreach (Momotaro.Gameplay.Story.ChapterInfo chapter in built.Story.Chapters)
                    {
                        built._knownChapters.Add(chapter.ChapterId.Value);
                    }
                }
            }

            return errors.Count == before ? built : null;
        }

        private static void AddKnown(HashSet<string> target, IReadOnlyList<StableId> ids, string label, List<string> errors)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                if (!ids[i].IsValid)
                {
                    errors.Add("Campaign contains an invalid " + label + " id.");
                    continue;
                }

                if (!seen.Add(ids[i].Value))
                {
                    errors.Add("Duplicate " + label + " id '" + ids[i].Value + "'.");
                    continue;
                }

                target.Add(ids[i].Value);
            }
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
