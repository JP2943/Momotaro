using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.Progression;
using Momotaro.Data.Story;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using Momotaro.Editor.Phase6;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Phase7
{
    /// <summary>
    /// P7 の試遊ワールドの ID（P7 仕様 §11）。A・B・C は P6 の Builder を <see cref="Phase6Profile.P7"/> で再利用し、
    /// ここでは P7 で足すものだけを持つ：困難ルートの Area H、住民 2 人、必須イベントの門、依頼 3 件、章。
    /// </summary>
    public static class Phase7WorldIds
    {
        // ---- 困難ルートの Area（崖道） ----
        public static readonly StableId AreaH = new StableId("area_p7_h");
        public static string AreaHScenePath => Phase6Profile.P7.SceneFolder + "/SCN_Phase7_AreaH.unity";
        public static string AreaHDataPath => Phase6Profile.P7.DataFolder + "/SO_Area_P7_H.asset";
        public static string StoryDataPath => Phase6Profile.P7.DataFolder + "/SO_Story_P7.asset";

        // ---- 入口・扉（扉は暗転の通常移動。経路の終端は C の入口） ----
        public static readonly StableId EntryAFromH = new StableId("entry_p7_a_from_h");
        public static readonly StableId EntryHFromA = new StableId("entry_p7_h_from_a");
        public static readonly StableId EntryHFromC = new StableId("entry_p7_h_from_c");
        public static readonly StableId EntryCFromH = new StableId("entry_p7_c_from_h");
        public static readonly StableId DoorAToH = new StableId("door_p7_a_to_h");
        public static readonly StableId DoorHToA = new StableId("door_p7_h_to_a");
        public static readonly StableId DoorHToC = new StableId("door_p7_h_to_c");
        public static readonly StableId DoorCToH = new StableId("door_p7_c_to_h");

        // ---- 拠点の門（必須イベントで開く）・崖道の配置 ----
        public static readonly StableId FlagACliffGate = new StableId("flag_p7_a_cliff_gate");
        public static readonly StableId FindCliffScroll = new StableId("find_p7_h_scroll");
        public static readonly StableId FieldH1 = new StableId("field_p7_h_01");
        public static readonly StableId FieldH2 = new StableId("field_p7_h_02");
        public static readonly StableId RegionHDefault = new StableId("region_p7_h_default");

        // ---- 会話・依頼・章 ----
        public static readonly StableId Story = new StableId("story_p7");
        public static readonly StableId Guide = new StableId("villager_p7_guide");
        public static readonly StableId Giver = new StableId("villager_p7_giver");
        public static readonly StableId CliffGateEvent = new StableId("event_p7_cliff_gate");
        public static readonly StableId Chapter = new StableId("chapter_p7_01");
        public static readonly StableId QuestReach = new StableId("quest_p7_reach_junction");
        public static readonly StableId QuestFind = new StableId("quest_p7_find_scroll");
        public static readonly StableId QuestMulti = new StableId("quest_p7_road_and_cliff");

        public static readonly StableId DlgGuideDefault = new StableId("dialogue_p7_guide_default");
        public static readonly StableId DlgGuideGate = new StableId("dialogue_p7_guide_gate");
        public static readonly StableId DlgGuideStd = new StableId("dialogue_p7_guide_standard");
        public static readonly StableId DlgGuideHard = new StableId("dialogue_p7_guide_hard");
        public static readonly StableId DlgGuideBoth = new StableId("dialogue_p7_guide_both");
        public static readonly StableId DlgGuideCleared = new StableId("dialogue_p7_guide_cleared");
        public static readonly StableId DlgGiverDefault = new StableId("dialogue_p7_giver_default");
        public static readonly StableId DlgGiverCleared = new StableId("dialogue_p7_giver_cleared");

        public const string RewardQuestReach = "QuestReach";
        public const string RewardQuestFind = "QuestFind";
        public const string RewardQuestMulti = "QuestMulti";
        public const string RewardFindCliff = "FindCliff";
    }

    /// <summary>P7 の試遊値（仕様 §6「一件 30 徳を初期値」。本番の経済ではない）。</summary>
    public static class Phase7TrialValues
    {
        public const int QuestVirtue = 30;

        /// <summary>A の初到達（P7 は 0。徳は依頼・遭遇戦・探索で得て成長に使う流れを確かめる）。</summary>
        public const int ArrivalA = 0;
    }

    /// <summary>P7 の配置（Area ローカル座標）。A・C は P6 の配置に足すもの、H は新しい Area。</summary>
    public static class Phase7WorldLayout
    {
        // ---- A（拠点）----
        public static readonly Vector3 Guide = new Vector3(-1.5f, 0f, -4f);
        public static readonly Vector3 Giver = new Vector3(2.5f, 0f, -3.2f);
        public static readonly Vector3 AlcoveWallWest = new Vector3(-3.2f, 0f, 7.6f);
        public static readonly Vector3 AlcoveWallEast = new Vector3(1.2f, 0f, 7.6f);
        public static readonly Vector3 AlcoveWallSize = new Vector3(0.5f, Phase5Layout.WallHeight, 2.8f);
        public static readonly Vector3 CliffGate = new Vector3(-1f, 0f, 6f);
        public static readonly Vector3 CliffGateSize = new Vector3(4.6f, Phase5Layout.WallHeight, 0.6f);
        public static readonly Vector3 GateNotice = new Vector3(-1f, 0f, 4.6f);
        public static readonly Vector3 DoorAToH = new Vector3(-1f, 0f, 8.3f);
        public static readonly Vector3 EntryAFromH = new Vector3(-1f, 0f, 7.1f);
        public static readonly Vector3[] EntryAFromHAlternates = { new Vector3(-2f, 0f, 7.1f), new Vector3(0f, 0f, 7.1f) };

        // ---- C（合流・ボス前）----
        public static readonly Vector3 DoorCToH = new Vector3(-9f, 0f, 8.3f);
        public static readonly Vector3 EntryCFromH = new Vector3(-9f, 0f, 6.8f);
        public static readonly Vector3[] EntryCFromHAlternates = { new Vector3(-8f, 0f, 6.8f), new Vector3(-10f, 0f, 6.8f) };

        // ---- H（崖道）----
        public const float AreaHWidth = Phase5Layout.AreaAWidth;
        public const float AreaHDepth = Phase5Layout.AreaADepth;

        /// <summary>H は扉（暗転）でだけ繋がるので、ほかの Area と重ならない北の離れた位置へ置く。</summary>
        public static readonly Vector3 AreaHOrigin = new Vector3(0f, 0f, 60f);

        public static readonly Vector3 EntryHFromA = new Vector3(0f, 0f, -6.6f);
        public static readonly Vector3[] EntryHFromAAlternates = { new Vector3(-1f, 0f, -6.6f), new Vector3(1f, 0f, -6.6f) };
        public static readonly Vector3 DoorHToA = new Vector3(0f, 0f, -8.3f);
        public static readonly Vector3 EntryHFromC = new Vector3(9.6f, 0f, 6f);
        public static readonly Vector3[] EntryHFromCAlternates = { new Vector3(9.6f, 0f, 5f), new Vector3(8.6f, 0f, 6f) };
        public static readonly Vector3 DoorHToC = new Vector3(11.3f, 0f, 6f);
        public static readonly Vector3 CliffScroll = new Vector3(-9f, 0f, 6.5f);
        public static readonly Vector3 FieldH1 = new Vector3(2f, 0f, 1.5f);
        public static readonly Vector3 FieldH2 = new Vector3(-5f, 0f, 3f);
        public static readonly Vector3 RockA = new Vector3(-3f, 0f, -2.5f);
        public static readonly Vector3 RockASize = new Vector3(9f, Phase5Layout.WallHeight, 0.6f);
        public static readonly Vector3 RockB = new Vector3(5f, 0f, 3.5f);
        public static readonly Vector3 RockBSize = new Vector3(0.6f, Phase5Layout.WallHeight, 6f);
    }

    /// <summary>
    /// P7 の試遊の中身（Data と Scene の P7 部分）。<see cref="Phase6WorldBuilder"/> が <see cref="Phase6Profile.P7"/> のときに呼ぶ。
    /// 文面は試遊用の仮本文（本番シナリオではない。仕様 §14）。道案内には地形の目印と危険の説明を入れる（仕様 §10）。
    /// </summary>
    public static class Phase7World
    {
        // ================================================================ Data

        /// <summary>P7 の報酬（依頼 3 件・崖道の巻物）を作る。</summary>
        public static void EnsureRewards()
        {
            Phase6WorldBuilder.EnsureReward(Phase7WorldIds.RewardQuestReach, Phase6WorldIds.RewardId("quest_reach"),
                "依頼：合流点の様子", Phase7TrialValues.QuestVirtue, true);
            Phase6WorldBuilder.EnsureReward(Phase7WorldIds.RewardQuestFind, Phase6WorldIds.RewardId("quest_find"),
                "依頼：崖道の巻物", Phase7TrialValues.QuestVirtue, true);
            Phase6WorldBuilder.EnsureReward(Phase7WorldIds.RewardQuestMulti, Phase6WorldIds.RewardId("quest_multi"),
                "依頼：街道と崖道", Phase7TrialValues.QuestVirtue, true);
            Phase6WorldBuilder.EnsureReward(Phase7WorldIds.RewardFindCliff, Phase6WorldIds.RewardId("find_cliff"),
                "発見（崖道の巻物）", Phase6TrialValues.Discovery, true);
        }

        public static RewardData Reward(string file) =>
            AssetDatabase.LoadAssetAtPath<RewardData>(Phase6WorldIds.RewardPath(file));

        /// <summary>会話・依頼・必須イベント・章の Data（SO_Story_P7）を作る。</summary>
        public static CampaignStoryData EnsureStory()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CampaignStoryData>(Phase7WorldIds.StoryDataPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<CampaignStoryData>();
                AssetDatabase.CreateAsset(asset, Phase7WorldIds.StoryDataPath);
            }

            Phase5ExplorationBuilder.SetIdentity(asset, Phase7WorldIds.Story, "P7 試遊の会話・依頼・章");

            StableId areaA = Phase6WorldIds.AreaA;
            StableId areaB = Phase6WorldIds.AreaB;
            StableId areaC = Phase6WorldIds.AreaC;
            var guide = new VillagerDefinition();
            guide.EditorSet(Phase7WorldIds.Guide, "案内の老人", areaA);
            var giver = new VillagerDefinition();
            giver.EditorSet(Phase7WorldIds.Giver, "村の娘", areaA);

            var gate = new StoryEventDefinition();
            gate.EditorSet(Phase7WorldIds.CliffGateEvent, "崖道の門が開いた", areaA, Phase7WorldIds.FlagACliffGate,
                "門は閉ざされている。案内の老人に頼めば開けてもらえそうだ。");

            var standard = new ChapterRouteDefinition();
            standard.EditorSet(new List<StableId> { areaB }, areaC, Phase6WorldIds.EntryCFromB);
            var hard = new ChapterRouteDefinition();
            hard.EditorSet(new List<StableId> { Phase7WorldIds.AreaH }, areaC, Phase7WorldIds.EntryCFromH);
            var chapter = new ChapterDefinition();
            chapter.EditorSet(Phase7WorldIds.Chapter, "第一章（試遊）", areaC, Phase6WorldIds.EncounterCBoss, standard, hard,
                areaC, new List<StableId> { Phase6WorldIds.EntryCFromB, Phase7WorldIds.EntryCFromH });

            var quests = new List<QuestDefinition>
            {
                Quest(Phase7WorldIds.QuestReach, "合流点の様子", "東の果ての合流点（お地蔵様のある広場）まで行って、様子を見てきて。",
                    new List<string>
                    {
                        "東の果てに、街道と崖道が一つになる広場があるの。",
                        "鬼の大将が近くに陣取っているって噂で……様子を見てきてくれませんか。",
                    },
                    "合流点はまだ見ていないのね。東の果てよ。", "ありがとう、広場は無事だったのね。", Phase7WorldIds.RewardQuestReach,
                    QuestObjective.Reach(areaC, "合流点（エリア C）へ行く")),
                Quest(Phase7WorldIds.QuestFind, "崖道の巻物", "北の崖道の奥に落ちている巻物を見つけて。",
                    new List<string>
                    {
                        "おじいちゃんの巻物を、北の崖道で落としてしまったの。",
                        "崖の上の、行き止まりの岩陰にあるはず。鬼が出るから気をつけて。",
                    },
                    "巻物はまだ見つからない？　崖の上の岩陰よ。", "これよ、これ！　見つけてくれてありがとう。", Phase7WorldIds.RewardQuestFind,
                    QuestObjective.Discover(Phase7WorldIds.AreaH, Phase7WorldIds.FindCliffScroll, "崖道の巻物を見つける")),
                Quest(Phase7WorldIds.QuestMulti, "街道と崖道", "街道の北の鬼を退け、崖道にも足を運んで。",
                    new List<string>
                    {
                        "街道の北側に鬼の群れが居座っていて、荷車が通れないの。",
                        "それと、崖道がどうなっているかも見てきてくれると助かるわ。",
                    },
                    "まだ片付いていないみたい。街道の北の鬼と、崖道よ。", "両方ともありがとう。これで村の皆も安心ね。",
                    Phase7WorldIds.RewardQuestMulti,
                    QuestObjective.ClearEncounter(areaB, Phase6WorldIds.EncounterBNorth, "街道の北の鬼を退ける（エリア B の北の遭遇戦）"),
                    QuestObjective.Reach(Phase7WorldIds.AreaH, "崖道（エリア H）へ行く")),
            };

            var dialogues = new List<DialogueDefinition>
            {
                Dialogue(Phase7WorldIds.DlgGuideDefault, Phase7WorldIds.Guide, 0, "案内の老人", new List<string>
                {
                    "東の通路のレバーで門を開ければ、平らな街道じゃ。鬼が二組うろついておるが、道は広い。",
                    "北の門の奥の扉は崖道へ通じておる。足場が悪く、岩陰で鬼が待ち伏せしておるぞ。",
                    "どちらを行っても、東の果てのお地蔵様の広場で道は一つになる。",
                }),
                // 門の会話は未完了の間いちばん優先する（経路差分・章クリア後の会話に隠れて門が開けられなくなるのを防ぐ）。
                Dialogue(Phase7WorldIds.DlgGuideGate, Phase7WorldIds.Guide, 40, "案内の老人", new List<string>
                {
                    "東の通路のレバーで門を開ければ、平らな街道じゃ。鬼が二組うろついておる。",
                    "北の門の奥は崖道。岩が転がり、鬼が待ち伏せする険しい道じゃが、近道でもある。",
                    "崖道へ行くなら、わしが北の門を開けてやろう。",
                }, new List<StoryCondition> { StoryCondition.Event(Phase7WorldIds.CliffGateEvent, negate: true) },
                    Phase7WorldIds.CliffGateEvent, "北の門を開けてもらう"),
                Dialogue(Phase7WorldIds.DlgGuideStd, Phase7WorldIds.Guide, 10, "案内の老人", new List<string>
                {
                    "街道を抜けて合流点まで行ったか。",
                    "崖道は北の門の奥じゃ。落石と待ち伏せに気をつけるのじゃぞ。",
                }, new List<StoryCondition>
                {
                    StoryCondition.RouteCompleted(Phase7WorldIds.Chapter, StoryRoute.Standard),
                    StoryCondition.RouteCompleted(Phase7WorldIds.Chapter, StoryRoute.Hard, negate: true),
                }),
                Dialogue(Phase7WorldIds.DlgGuideHard, Phase7WorldIds.Guide, 11, "案内の老人", new List<string>
                {
                    "崖道を越えて合流点まで行ったとは見事じゃ。",
                    "街道は東の通路の先。鬼の群れは多いが、足場は良い。",
                }, new List<StoryCondition>
                {
                    StoryCondition.RouteCompleted(Phase7WorldIds.Chapter, StoryRoute.Hard),
                    StoryCondition.RouteCompleted(Phase7WorldIds.Chapter, StoryRoute.Standard, negate: true),
                }),
                Dialogue(Phase7WorldIds.DlgGuideBoth, Phase7WorldIds.Guide, 20, "案内の老人", new List<string>
                {
                    "両方の道を歩いたか。大したものじゃ。",
                    "合流点の奥に鬼の大将が居る。お地蔵様で備えてから挑むがよい。",
                }, new List<StoryCondition>
                {
                    StoryCondition.RouteCompleted(Phase7WorldIds.Chapter, StoryRoute.Standard),
                    StoryCondition.RouteCompleted(Phase7WorldIds.Chapter, StoryRoute.Hard),
                }),
                Dialogue(Phase7WorldIds.DlgGuideCleared, Phase7WorldIds.Guide, 30, "案内の老人", new List<string>
                {
                    "鬼の大将を倒したそうじゃな。村も安心じゃ。",
                    "まだ歩いておらぬ道があれば、今のうちに見ておくとよい。",
                }, new List<StoryCondition> { StoryCondition.ChapterCleared(Phase7WorldIds.Chapter) }),
                Dialogue(Phase7WorldIds.DlgGiverDefault, Phase7WorldIds.Giver, 0, "村の娘", new List<string>
                {
                    "旅のお方、少し頼みごとを聞いてくれませんか。",
                }),
                Dialogue(Phase7WorldIds.DlgGiverCleared, Phase7WorldIds.Giver, 30, "村の娘", new List<string>
                {
                    "鬼の大将がいなくなって、村に笑い声が戻りました。",
                }, new List<StoryCondition> { StoryCondition.ChapterCleared(Phase7WorldIds.Chapter) }),
            };

            asset.EditorSet(new List<VillagerDefinition> { guide, giver }, dialogues, quests,
                new List<StoryEventDefinition> { gate }, new List<ChapterDefinition> { chapter });
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static QuestDefinition Quest(StableId id, string name, string objective, List<string> offer, string progress,
            string report, string rewardFile, params QuestObjective[] objectives)
        {
            var q = new QuestDefinition();
            q.EditorSet(id, name, Phase7WorldIds.Giver, objective, offer, progress, report, Reward(rewardFile),
                new List<QuestObjective>(objectives));
            return q;
        }

        private static DialogueDefinition Dialogue(StableId id, StableId villager, int priority, string speaker, List<string> pages,
            List<StoryCondition> conditions = null, StableId completesEvent = default, string confirm = null)
        {
            var d = new DialogueDefinition();
            d.EditorSet(id, villager, priority, speaker, pages, conditions, completesEvent, confirm);
            return d;
        }

        /// <summary>H の Area Data。</summary>
        public static AreaDefinition EnsureAreaHDefinition()
        {
            return Phase5ExplorationBuilder.EnsureAreaDefinition(
                Phase7WorldIds.AreaHDataPath, Phase7WorldIds.AreaH, "エリア H（崖道・困難ルート）", Phase7WorldIds.AreaHScenePath,
                new[]
                {
                    (Phase7WorldIds.EntryHFromA, CardinalDirection.North),
                    (Phase7WorldIds.EntryHFromC, CardinalDirection.West),
                },
                Phase7WorldIds.EntryHFromA);
        }

        /// <summary>H の保存対象（配置物・普通敵）。</summary>
        public static void ConfigureAreaH(AreaDefinition h)
        {
            h.EditorSetArrivalReward(null);
            var m = new AreaContentManifest();
            m.EditorSet(new List<StableId>(), new List<StableId>(),
                new List<StableId> { Phase7WorldIds.FieldH1, Phase7WorldIds.FieldH2 },
                new List<StableId> { Phase7WorldIds.FindCliffScroll }, new List<StableId>(), new List<StableId>());
            h.EditorSetContent(m);
            EditorUtility.SetDirty(h);
        }

        // ================================================================ Scene

        /// <summary>A（拠点）の P7 部分：住民 2 人、崖道の門（必須イベント）とその立て札、崖道への扉と崖道からの入口。</summary>
        public static void ExtendA(Phase5AreaExtension ext)
        {
            StableId areaA = Phase6WorldIds.AreaA;
            AddVillager(ext.FixtureRoot, Phase7WorldIds.Guide, areaA, Phase7WorldLayout.Guide, "案内の老人",
                new Color(0.55f, 0.45f, 0.35f));
            AddVillager(ext.FixtureRoot, Phase7WorldIds.Giver, areaA, Phase7WorldLayout.Giver, "村の娘",
                new Color(0.80f, 0.45f, 0.55f));

            // 北の奥まった小部屋（左右の壁）を門で塞ぎ、奥に崖道への扉を置く。
            Material wallMat = Phase5Placeholder.EnsureMaterial("M_P5_Wall", Phase5Placeholder.WallColor);
            Phase5ExplorationBuilder.CreateWall(ext.Environment, "Wall_AlcoveWest", Phase7WorldLayout.AlcoveWallWest,
                Phase7WorldLayout.AlcoveWallSize, wallMat);
            Phase5ExplorationBuilder.CreateWall(ext.Environment, "Wall_AlcoveEast", Phase7WorldLayout.AlcoveWallEast,
                Phase7WorldLayout.AlcoveWallSize, wallMat);
            AreaFlagDoor gate = Phase5ExplorationBuilder.CreateFlagDoor(ext.FixtureRoot, Phase7WorldIds.FlagACliffGate,
                Phase7WorldLayout.CliffGate, Phase7WorldLayout.CliffGateSize);
            ext.Doors = ext.Doors ?? new List<AreaFlagDoor>();
            ext.Doors.Add(gate);

            var noticeGo = new GameObject("GateNotice_" + Phase7WorldIds.CliffGateEvent.Value);
            noticeGo.transform.SetParent(ext.FixtureRoot, false);
            noticeGo.transform.position = Phase7WorldLayout.GateNotice;
            Phase5Placeholder.CreateLabel("北の門（崖道）", noticeGo.transform, Phase7WorldLayout.GateNotice + new Vector3(0f, 0.3f, 0f),
                Phase5Placeholder.GateColor, 0.16f);
            noticeGo.AddComponent<StoryGateNotice>().Bind(Phase7WorldIds.CliffGateEvent, areaA, 1.4f);

            AreaTransitionDoor toH = Phase5ExplorationBuilder.CreateTransitionDoor(ext.FixtureRoot, "ToH", Phase7WorldIds.DoorAToH,
                areaA, Phase7WorldIds.AreaH, Phase7WorldIds.EntryHFromA, Phase7WorldLayout.DoorAToH, Phase5SeamAxis.Z,
                "崖道へ（扉）");
            ext.TransitionDoors = ext.TransitionDoors ?? new List<AreaTransitionDoor>();
            ext.TransitionDoors.Add(toH);
            ext.EntryPoints.Add(Phase5ExplorationBuilder.CreateEntryPoint(ext.Entries, Phase7WorldIds.EntryAFromH,
                Phase7WorldLayout.EntryAFromH, Phase7WorldLayout.EntryAFromHAlternates, "崖道から"));
        }

        /// <summary>C（合流・ボス前）の P7 部分：崖道からの入口（困難ルートの終端）と崖道への扉。</summary>
        public static void ExtendC(Transform fixtureRoot, Transform entries, List<AreaEntryPoint> entryPoints,
            List<AreaTransitionDoor> doors)
        {
            entryPoints.Add(Phase5ExplorationBuilder.CreateEntryPoint(entries, Phase7WorldIds.EntryCFromH,
                Phase7WorldLayout.EntryCFromH, Phase7WorldLayout.EntryCFromHAlternates, "崖道から"));
            doors.Add(Phase5ExplorationBuilder.CreateTransitionDoor(fixtureRoot, "ToH", Phase7WorldIds.DoorCToH,
                Phase6WorldIds.AreaC, Phase7WorldIds.AreaH, Phase7WorldIds.EntryHFromC, Phase7WorldLayout.DoorCToH,
                Phase5SeamAxis.Z, "崖道へ（扉）"));
        }

        /// <summary>H（崖道）の Scene の中身。P6 の C と同じ部品で組む（接続口の無い外周・扉 2 つ・巻物・普通敵 2 体・岩）。</summary>
        public static void PopulateAreaH(Transform root, AreaDefinition definition, GameObject residentRig, Phase5BuildTargets t)
        {
            Material floorMat = Phase5Placeholder.EnsureMaterial("M_P5_Floor", Phase5Placeholder.FloorColor);
            Material wallMat = Phase5Placeholder.EnsureMaterial("M_P5_Wall", Phase5Placeholder.WallColor);
            float w = Phase7WorldLayout.AreaHWidth;
            float d = Phase7WorldLayout.AreaHDepth;

            var env = new GameObject("Environment");
            env.transform.SetParent(root, false);
            Phase5ExplorationBuilder.CreateBackdrop(env.transform, Vector3.zero, w, d);
            Phase5ExplorationBuilder.CreateFloor(env.transform, Vector3.zero, w, d, floorMat);
            Phase5ExplorationBuilder.CreateOuterWalls(env.transform, Vector3.zero, w, d, wallMat, Phase5SeamOpening.None);
            Phase5ExplorationBuilder.CreateWall(env.transform, "Rock_A", Phase7WorldLayout.RockA, Phase7WorldLayout.RockASize, wallMat);
            Phase5ExplorationBuilder.CreateWall(env.transform, "Rock_B", Phase7WorldLayout.RockB, Phase7WorldLayout.RockBSize, wallMat);

            var markers = new GameObject("Markers");
            markers.transform.SetParent(root, false);
            Phase5Placeholder.CreateLabel("エリア H（崖道・困難）", markers.transform, new Vector3(0f, 0.2f, -1.5f), Color.white, 0.5f);

            var entries = new GameObject("Entries");
            entries.transform.SetParent(root, false);
            AreaEntryPoint fromA = Phase5ExplorationBuilder.CreateEntryPoint(entries.transform, Phase7WorldIds.EntryHFromA,
                Phase7WorldLayout.EntryHFromA, Phase7WorldLayout.EntryHFromAAlternates, "拠点から");
            AreaEntryPoint fromC = Phase5ExplorationBuilder.CreateEntryPoint(entries.transform, Phase7WorldIds.EntryHFromC,
                Phase7WorldLayout.EntryHFromC, Phase7WorldLayout.EntryHFromCAlternates, "合流点から");

            var fixtures = new Phase5ExplorationBuilder.Fixtures();
            var fixtureRoot = new GameObject("Fixtures");
            fixtureRoot.transform.SetParent(root, false);
            var doors = new List<AreaTransitionDoor>
            {
                Phase5ExplorationBuilder.CreateTransitionDoor(fixtureRoot.transform, "ToA", Phase7WorldIds.DoorHToA,
                    Phase7WorldIds.AreaH, Phase6WorldIds.AreaA, Phase7WorldIds.EntryAFromH, Phase7WorldLayout.DoorHToA,
                    Phase5SeamAxis.Z, "拠点へ（扉）"),
                Phase5ExplorationBuilder.CreateTransitionDoor(fixtureRoot.transform, "ToC", Phase7WorldIds.DoorHToC,
                    Phase7WorldIds.AreaH, Phase6WorldIds.AreaC, Phase7WorldIds.EntryCFromH, Phase7WorldLayout.DoorHToC,
                    Phase5SeamAxis.X, "合流点へ（扉）"),
            };

            Phase6WorldBuilder.AddPickup(fixtureRoot.transform, Phase7WorldIds.FindCliffScroll, Phase7WorldIds.AreaH, default, 0, 0,
                Reward(Phase7WorldIds.RewardFindCliff), Phase7WorldLayout.CliffScroll, "巻物を調べる", "巻物");
            fixtures.FieldPlacements.Add(Phase6WorldBuilder.FieldPlacement(fixtureRoot.transform, Phase7WorldIds.FieldH1,
                Phase7WorldLayout.FieldH1));
            fixtures.FieldPlacements.Add(Phase6WorldBuilder.FieldPlacement(fixtureRoot.transform, Phase7WorldIds.FieldH2,
                Phase7WorldLayout.FieldH2));
            fixtures.FieldEnemyTable = Phase6WorldBuilder.EnemyTable();
            fixtures.FieldKillReward = AssetDatabase.LoadAssetAtPath<RewardData>(Phase6WorldIds.RewardPath("Kill"));

            var cameraRegions = new GameObject("CameraRegions");
            cameraRegions.transform.SetParent(root, false);
            fixtures.DefaultCameraRegion = Phase5ExplorationBuilder.CreateCameraRegion(cameraRegions.transform,
                Phase7WorldIds.RegionHDefault, 0, Vector3.zero, Phase5Layout.FollowRegionSize(w, d));

            AreaRoot areaRoot = root.gameObject.AddComponent<AreaRoot>();
            areaRoot.EditorSet(definition, new List<AreaEntryPoint> { fromA, fromC }, new List<AreaExitGate>(), null, doors, null,
                new List<AreaSeamBarrier>());

            Phase5ExplorationBuilder.CreateAreaSystems(root, areaRoot, definition, fixtures, residentRig, t);
            Phase5ExplorationBuilder.ApplyAuthoringOrigin(root, Phase7WorldLayout.AreaHOrigin);
            Phase5ExplorationBuilder.BakeNavMesh(root, t.NavMeshAssetName("H"), t.DataFolder);
            Phase5ExplorationBuilder.CloseActivityGate(areaRoot);
        }

        /// <summary>住民（静止。体は当たりあり、受付の錨は体の手前＝南に置く。お地蔵様と同じ理由で、体が遮蔽にならないように）。</summary>
        private static void AddVillager(Transform parent, StableId villagerId, StableId areaId, Vector3 position, string label, Color color)
        {
            var go = new GameObject("Villager_" + villagerId.Value);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Material mat = Phase5Placeholder.EnsureMaterial("M_P7_Villager_" + villagerId.Value, color);
            Phase5Placeholder.CreateBox("Body", go.transform, position + new Vector3(0f, 0.8f, 0f), new Vector3(0.6f, 1.6f, 0.5f), mat);
            Phase5Placeholder.CreateLabel(label, go.transform, position + new Vector3(0f, 1.9f, 0f), Color.white, 0.18f);
            var anchor = new GameObject("InteractAnchor");
            anchor.transform.SetParent(go.transform, false);
            anchor.transform.position = position + new Vector3(0f, 0f, -Phase6WorldBuilder.ShrineAnchorOffset);
            anchor.AddComponent<VillagerPoint>().Bind(villagerId, areaId, label, 1.6f);
        }
    }
}
