using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.Progression;
using Momotaro.Data.Story;
using Momotaro.Data.World;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Story;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P7 の EditMode テスト用の最小 campaign（拠点・標準・困難・合流・ボスの 5 Area、住民 2 人、依頼 3 件、必須イベント 1 件、章 1 つ）。
    /// 試遊の Builder とは独立に、Data の形だけを組む。<see cref="Dispose"/> で生成物を破棄する。
    /// </summary>
    public sealed class P7StoryFixture : IDisposable
    {
        public static readonly StableId CampaignId = new StableId("campaign_p7_test");
        public static readonly StableId Hub = new StableId("area_p7t_hub");
        public static readonly StableId Std = new StableId("area_p7t_std");
        public static readonly StableId Hard = new StableId("area_p7t_hard");
        public static readonly StableId Merge = new StableId("area_p7t_merge");
        public static readonly StableId BossArea = new StableId("area_p7t_boss");

        public static readonly StableId HubStart = new StableId("entry_p7t_hub_start");
        public static readonly StableId HubShrineEntry = new StableId("entry_p7t_hub_shrine");
        public static readonly StableId StdWest = new StableId("entry_p7t_std_west");
        public static readonly StableId HardSouth = new StableId("entry_p7t_hard_south");
        public static readonly StableId MergeFromStd = new StableId("entry_p7t_merge_from_std");
        public static readonly StableId MergeFromHard = new StableId("entry_p7t_merge_from_hard");
        public static readonly StableId MergeShrineEntry = new StableId("entry_p7t_merge_shrine");
        public static readonly StableId BossWest = new StableId("entry_p7t_boss_west");

        public static readonly StableId ShrineHub = new StableId("shrine_p7t_hub");
        public static readonly StableId ShrineMerge = new StableId("shrine_p7t_merge");

        public static readonly StableId EncStd = new StableId("encounter_p7t_std");
        public static readonly StableId FindHard = new StableId("find_p7t_hard_scroll");
        public static readonly StableId InvHard = new StableId("investigate_p7t_hard");
        public static readonly StableId BossId = new StableId("encounter_p7t_boss");
        public static readonly StableId GateFlag = new StableId("flag_p7t_hub_gate");

        public static readonly StableId Guide = new StableId("villager_p7t_guide");
        public static readonly StableId Giver = new StableId("villager_p7t_giver");
        public static readonly StableId GateEvent = new StableId("event_p7t_gate");
        public static readonly StableId Chapter = new StableId("chapter_p7t_01");

        public static readonly StableId QuestReach = new StableId("quest_p7t_reach");
        public static readonly StableId QuestFind = new StableId("quest_p7t_find");
        public static readonly StableId QuestMulti = new StableId("quest_p7t_multi");
        public static readonly StableId RewardReach = new StableId("reward_p7t_quest_reach");
        public static readonly StableId RewardFind = new StableId("reward_p7t_quest_find");
        public static readonly StableId RewardMulti = new StableId("reward_p7t_quest_multi");
        public static readonly StableId RewardArriveMerge = new StableId("reward_p7t_arrive_merge");
        public static readonly StableId RewardClearStd = new StableId("reward_p7t_clear_std");
        public static readonly StableId RewardFindPickup = new StableId("reward_p7t_find_pickup");

        public static readonly StableId DlgGuideDefault = new StableId("dialogue_p7t_guide_default");
        public static readonly StableId DlgGuideGate = new StableId("dialogue_p7t_guide_gate");
        public static readonly StableId DlgGuideStdOnly = new StableId("dialogue_p7t_guide_std");
        public static readonly StableId DlgGuideHardOnly = new StableId("dialogue_p7t_guide_hard");
        public static readonly StableId DlgGuideBoth = new StableId("dialogue_p7t_guide_both");
        public static readonly StableId DlgGuideCleared = new StableId("dialogue_p7t_guide_cleared");
        public static readonly StableId DlgGiverDefault = new StableId("dialogue_p7t_giver_default");
        public static readonly StableId DlgGiverCleared = new StableId("dialogue_p7t_giver_cleared");

        public const int QuestVirtue = 30;

        private readonly List<Object> _spawned = new List<Object>();

        public P7StoryFixture(Action<P7StoryFixture, CampaignStoryData> extra = null)
        {
            Story = ScriptableObject.CreateInstance<CampaignStoryData>();
            _spawned.Add(Story);
            SetId(Story, new StableId("story_p7_test"));
            FillStory(Story);
            extra?.Invoke(this, Story);
            Data = BuildCatalogData(Story);
            Assert.IsTrue(AreaCatalog.TryBuild(Data, out AreaCatalog built, out IReadOnlyList<string> errors),
                string.Join("\n", errors));
            Catalog = built;
            Assert.IsNotNull(Catalog.Campaign, "campaign");
            Assert.IsNotNull(Catalog.Campaign.Story, "story");
        }

        public CampaignStoryData Story { get; }
        public AreaCatalogData Data { get; }
        public AreaCatalog Catalog { get; }
        public CampaignCatalog Campaign => Catalog.Campaign;
        public StoryCatalog StoryCatalog => Catalog.Campaign.Story;

        public QuestInfo Quest(StableId id)
        {
            Assert.IsTrue(StoryCatalog.TryGetQuest(id, out QuestInfo q), id.Value);
            return q;
        }

        public ShrineInfo Shrine(StableId id)
        {
            Assert.IsTrue(Campaign.TryGetShrine(id, out ShrineInfo s), id.Value);
            return s;
        }

        /// <summary>New Game 済みの Session（恒久規則・初期お地蔵様・権利 3）。</summary>
        public GameSessionState NewSession(string adventureId = "adventure_p7_test")
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            Assert.IsTrue(session.InitializeNewAdventure(adventureId, Shrine(ShrineHub), 3, Campaign.RefundRightsInitial));
            return session;
        }

        /// <summary>通常の移動の到着（到着の確定と経路の記録を 1 つのまとめで。遷移サービスの NoteArrival と同じ順）。</summary>
        public ArrivalCommit Arrive(GameSessionState session, StableId areaId, StableId entryId, bool recordRoute = true)
        {
            session.Changes.BeginBatch("test_arrival");
            try
            {
                session.SetResumeAnchor(ResumeAnchor.AtEntry(areaId, entryId));
                ArrivalCommit commit = session.CommitArrival(areaId, Campaign.ArrivalRewardOf(areaId));
                if (recordRoute)
                {
                    session.NoteStoryArrival(StoryCatalog, areaId, entryId);
                }

                return commit;
            }
            finally
            {
                session.Changes.EndBatch();
            }
        }

        public RewardData Reward(StableId id, int virtue, bool grantOnce = true)
        {
            var r = ScriptableObject.CreateInstance<RewardData>();
            _spawned.Add(r);
            SetId(r, id);
            SetPrivate(r, "_virtueAmount", virtue);
            SetPrivate(r, "_grantOnce", grantOnce);
            return r;
        }

        public void Dispose()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        // ---------------------------------------------------------------- 組み立て

        private void FillStory(CampaignStoryData story)
        {
            var villagers = new List<VillagerDefinition> { Villager(Guide, "案内の老人"), Villager(Giver, "村の娘") };

            var gate = new StoryEventDefinition();
            gate.EditorSet(GateEvent, "門が開いた", Hub, GateFlag, "門は閉ざされている。案内の老人に話を聞こう。");

            var chapter = new ChapterDefinition();
            var std = new ChapterRouteDefinition();
            std.EditorSet(new List<StableId> { Std }, Merge, MergeFromStd);
            var hard = new ChapterRouteDefinition();
            hard.EditorSet(new List<StableId> { Hard }, Merge, MergeFromHard);
            chapter.EditorSet(Chapter, "第一章（試験）", BossArea, BossId, std, hard, Merge,
                new List<StableId> { MergeFromStd, MergeFromHard });

            var quests = new List<QuestDefinition>
            {
                QuestOf(QuestReach, "合流点を見てきて", Reward(RewardReach, QuestVirtue),
                    QuestObjective.Reach(Merge, "合流点へ行く")),
                QuestOf(QuestFind, "巻物を探して", Reward(RewardFind, QuestVirtue),
                    QuestObjective.Discover(Hard, FindHard, "崖道の巻物を見つける")),
                QuestOf(QuestMulti, "鬼を退けて", Reward(RewardMulti, QuestVirtue),
                    QuestObjective.ClearEncounter(Std, EncStd, "街道の鬼を退ける"),
                    QuestObjective.Reach(BossArea, "鬼の砦を見る")),
            };

            var dialogues = new List<DialogueDefinition>
            {
                Dlg(DlgGuideDefault, Guide, 0, "東の街道は穏やか、北の崖道は険しい。"),
                Dlg(DlgGuideGate, Guide, 5, "門を開けてやろう。", Cond(StoryCondition.Event(GateEvent, negate: true)),
                    GateEvent, "門を開けてもらう"),
                Dlg(DlgGuideStdOnly, Guide, 10, "街道を抜けたか。崖道は落石に気をつけよ。",
                    Cond(StoryCondition.RouteCompleted(Chapter, StoryRoute.Standard),
                        StoryCondition.RouteCompleted(Chapter, StoryRoute.Hard, negate: true))),
                Dlg(DlgGuideHardOnly, Guide, 11, "崖道を抜けたか。街道は鬼が出るぞ。",
                    Cond(StoryCondition.RouteCompleted(Chapter, StoryRoute.Hard),
                        StoryCondition.RouteCompleted(Chapter, StoryRoute.Standard, negate: true))),
                Dlg(DlgGuideBoth, Guide, 20, "両方の道を歩いたか。大したものだ。",
                    Cond(StoryCondition.RouteCompleted(Chapter, StoryRoute.Standard),
                        StoryCondition.RouteCompleted(Chapter, StoryRoute.Hard))),
                Dlg(DlgGuideCleared, Guide, 30, "鬼を倒したそうだな。", Cond(StoryCondition.ChapterCleared(Chapter))),
                Dlg(DlgGiverDefault, Giver, 0, "困っていることがあるの。"),
                Dlg(DlgGiverCleared, Giver, 30, "鬼がいなくなって安心したわ。", Cond(StoryCondition.ChapterCleared(Chapter))),
            };

            story.EditorSet(villagers, dialogues, quests, new List<StoryEventDefinition> { gate },
                new List<ChapterDefinition> { chapter });
        }

        private AreaCatalogData BuildCatalogData(CampaignStoryData story)
        {
            AreaDefinition hub = NewArea(Hub, null, new[] { HubStart, HubShrineEntry },
                Manifest(flags: new[] { GateFlag }));
            AreaDefinition std = NewArea(Std, null, new[] { StdWest },
                Manifest(encounters: new[] { EncStd }));
            AreaDefinition hard = NewArea(Hard, null, new[] { HardSouth },
                Manifest(pickups: new[] { FindHard }, investigations: new[] { InvHard }));
            AreaDefinition merge = NewArea(Merge, Reward(RewardArriveMerge, 5), new[] { MergeFromStd, MergeFromHard, MergeShrineEntry },
                Manifest());
            AreaDefinition boss = NewArea(BossArea, null, new[] { BossWest },
                Manifest(encounters: new[] { BossId }, bosses: new[] { BossId }));

            var shrineHub = new ShrineDefinition();
            shrineHub.EditorSet(ShrineHub, Hub, HubShrineEntry, "拠点のお地蔵様");
            var shrineMerge = new ShrineDefinition();
            shrineMerge.EditorSet(ShrineMerge, Merge, MergeShrineEntry, "合流点のお地蔵様");

            var catalog = ScriptableObject.CreateInstance<AreaCatalogData>();
            _spawned.Add(catalog);
            SetId(catalog, CampaignId);
            catalog.EditorSet(new List<AreaDefinition> { hub, std, hard, merge, boss }, Hub, HubStart);
            catalog.EditorSetCampaign(EncounterClearPolicy.Permanent, 1,
                new List<ShrineDefinition> { shrineHub, shrineMerge }, ShrineHub, 3,
                new List<ItemDefinition>(), new List<SkillNodeData>());
            catalog.EditorSetKnownIds(new List<StableId> { RewardClearStd, RewardFindPickup }, new List<StableId>());
            catalog.EditorSetP6B(0, 2f, 1.5f, 0.2f, 3, 3, 6, new List<StableId>(), "p7_test_slot");
            catalog.EditorSetStory(story);
            return catalog;
        }

        private AreaDefinition NewArea(StableId id, RewardData arrival, StableId[] entries, AreaContentManifest content)
        {
            var area = ScriptableObject.CreateInstance<AreaDefinition>();
            _spawned.Add(area);
            SetId(area, id);
            var list = new List<AreaEntryDefinition>();
            foreach (StableId e in entries)
            {
                var entry = new AreaEntryDefinition();
                entry.EditorSet(e, CardinalDirection.North);
                list.Add(entry);
            }

            area.EditorSet("Assets/_Project/Scenes/Tests/Phase7Test/" + id.Value + ".unity", 0, list, entries[0]);
            area.EditorSetContent(content);
            if (arrival != null)
            {
                SetPrivate(area, "_arrivalReward", arrival);
            }

            return area;
        }

        private static AreaContentManifest Manifest(StableId[] encounters = null, StableId[] bosses = null,
            StableId[] fields = null, StableId[] pickups = null, StableId[] flags = null, StableId[] investigations = null)
        {
            var m = new AreaContentManifest();
            m.EditorSet(L(encounters), L(bosses), L(fields), L(pickups), L(flags), L(investigations));
            return m;
        }

        private static List<StableId> L(StableId[] ids) => ids != null ? new List<StableId>(ids) : new List<StableId>();

        private static VillagerDefinition Villager(StableId id, string name)
        {
            var v = new VillagerDefinition();
            v.EditorSet(id, name, Hub);
            return v;
        }

        public static List<StoryCondition> Cond(params StoryCondition[] conditions) => new List<StoryCondition>(conditions);

        public static DialogueDefinition Dlg(StableId id, StableId villager, int priority, string text,
            List<StoryCondition> conditions = null, StableId completesEvent = default, string confirm = null)
        {
            var d = new DialogueDefinition();
            d.EditorSet(id, villager, priority, villager.Equals(Guide) ? "案内の老人" : "村の娘",
                new List<string> { text, "（二枚目）" + text }, conditions, completesEvent, confirm);
            return d;
        }

        public static QuestDefinition QuestOf(StableId id, string name, RewardData reward, params QuestObjective[] objectives)
        {
            var q = new QuestDefinition();
            q.EditorSet(id, name, Giver, name + "。", new List<string> { name + "の話。" }, "まだ途中みたいね。",
                "ありがとう。", reward, new List<QuestObjective>(objectives));
            return q;
        }

        public static void SetId(Object asset, StableId id) => SetPrivate(asset, "_id", id);

        public static void SetPrivate(object target, string field, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                var f = t.GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            throw new MissingFieldException(target.GetType().Name, field);
        }
    }
}
