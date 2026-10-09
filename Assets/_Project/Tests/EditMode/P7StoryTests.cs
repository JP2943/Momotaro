using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Story;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Story;
using NUnit.Framework;
using F = Momotaro.Tests.EditMode.P7StoryFixture;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P7：会話の選択・依頼の状態と確定・必須イベント・経路の記録（純粋な State と手順。仕様 §4〜§7、受入 P7 03〜P7 10 の状態遷移部分）。
    /// 入力・停止・表示・実 Scene は PlayMode（P7 01／P7 02 の実入力）で別に確かめる。
    /// </summary>
    public sealed class P7StoryTests
    {
        private F _f;

        [SetUp]
        public void SetUp() => _f = new F();

        [TearDown]
        public void TearDown() => _f.Dispose();

        // ================================================================ 会話の選択（P7 03）

        [Test]
        public void Dialogue_SelectsHighestPriorityMatching_AndFallsBackToUnconditional()
        {
            GameSessionState s = _f.NewSession();
            StoryCatalog story = _f.StoryCatalog;

            // 未訪問・門の会話（イベント未完了）が最優先。
            Assert.AreEqual(F.DlgGuideGate, StoryRules.SelectDialogue(s, story, F.Guide).DialogueId);
            Assert.AreEqual(F.DlgGiverDefault, StoryRules.SelectDialogue(s, story, F.Giver).DialogueId, "無条件の通常会話。");

            // 門を開けた後は通常会話へ落ちる（フォールバック）。
            Assert.IsTrue(s.CommitEventCompleted(Event(F.GateEvent), out _));
            Assert.AreEqual(F.DlgGuideDefault, StoryRules.SelectDialogue(s, story, F.Guide).DialogueId);

            // 片道（標準）→ 片道の会話。
            _f.Arrive(s, F.Std, F.StdWest);
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            Assert.AreEqual(F.DlgGuideStdOnly, StoryRules.SelectDialogue(s, story, F.Guide).DialogueId);

            // 両道 → 両道の会話。
            _f.Arrive(s, F.Hard, F.HardSouth);
            _f.Arrive(s, F.Merge, F.MergeFromHard);
            Assert.AreEqual(F.DlgGuideBoth, StoryRules.SelectDialogue(s, story, F.Guide).DialogueId);
        }

        [Test]
        public void Dialogue_HardOnly_SelectsHardOnlyLine()
        {
            GameSessionState s = _f.NewSession();
            s.CommitEventCompleted(Event(F.GateEvent), out _);
            _f.Arrive(s, F.Hard, F.HardSouth);
            Assert.AreEqual(F.DlgGuideDefault, StoryRules.SelectDialogue(s, _f.StoryCatalog, F.Guide).DialogueId,
                "経路に入っただけでは踏破にしない。");
            _f.Arrive(s, F.Merge, F.MergeFromHard);
            Assert.AreEqual(F.DlgGuideHardOnly, StoryRules.SelectDialogue(s, _f.StoryCatalog, F.Guide).DialogueId);
        }

        [Test]
        public void Dialogue_ContentIsFixedAtOpen_AndReportChoiceIsNeverHidden()
        {
            GameSessionState s = _f.NewSession();
            Assert.AreEqual(QuestAcceptResult.Accepted,
                StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach));
            _f.Arrive(s, F.Merge, F.MergeFromStd);

            // 章クリア後の雑談（優先度 30）を選んでいても、報告の選択肢は依頼主の会話に付く。
            MarkChapterClearedForTest(s);
            DialogueConversation c = DialogueConversation.Open(s, _f.StoryCatalog, F.Giver);
            Assert.AreEqual(F.DlgGiverCleared, c.Dialogue.DialogueId);
            Assert.IsTrue(HasChoice(c.Main, DialogueChoiceKind.ReportQuest, F.QuestReach), "報告できる依頼が隠れない。");

            // 開いた後に状態が変わっても、本文・選択肢は差し替えない。
            int pages = c.Main.Pages.Count;
            s.CommitQuestAccepted(_f.Quest(F.QuestFind));
            Assert.AreEqual(pages, c.Current.Pages.Count);
            Assert.IsTrue(HasChoice(c.Main, DialogueChoiceKind.ListenQuest, F.QuestFind), "開いた時点の選択肢のまま。");
        }

        [Test]
        public void Dialogue_PagesAdvance_AndLastPageShowsChoices()
        {
            GameSessionState s = _f.NewSession();
            DialogueConversation c = DialogueConversation.Open(s, _f.StoryCatalog, F.Giver);
            Assert.IsFalse(c.ShowsChoices, "最初のページでは選択肢を出さない。");
            int guard = 0;
            while (c.Advance() && guard++ < 20)
            {
            }

            Assert.IsTrue(c.IsOnLastPage);
            Assert.IsTrue(c.ShowsChoices);
            Assert.AreEqual(DialogueChoiceKind.Close, c.Current.Choices[c.Current.Choices.Count - 1].Kind, "閉じるは必ず最後。");

            Assert.IsTrue(c.ShowQuestOffer(F.QuestReach));
            Assert.AreEqual(0, c.PageIndex);
            while (c.Advance())
            {
            }

            Assert.AreEqual(DialogueChoiceKind.AcceptQuest, c.Current.Choices[0].Kind);
            Assert.AreEqual(DialogueChoiceKind.DeclineQuest, c.Current.Choices[1].Kind);
            StringAssert.Contains("報酬：徳 " + F.QuestVirtue, c.CurrentPage);
        }

        // ================================================================ 受注・拒否・再訪（P7 04）

        [Test]
        public void Quest_AcceptDeclineRevisitAndDuplicate()
        {
            GameSessionState s = _f.NewSession();
            QuestInfo q = _f.Quest(F.QuestReach);
            Assert.AreEqual(QuestStateKind.NotAccepted, StoryRules.StateOf(s, q));

            // 断る＝何も変えない（恒久フラグにしない）。もう一度話せば受けられる。
            long revision = s.Changes.Revision;
            DialogueConversation c = DialogueConversation.Open(s, _f.StoryCatalog, F.Giver);
            Assert.IsTrue(HasChoice(c.Main, DialogueChoiceKind.ListenQuest, F.QuestReach));
            Assert.AreEqual(revision, s.Changes.Revision, "会話を開くだけでは何も変えない。");

            int saves = s.Changes.AutosaveRequestCount;
            Assert.AreEqual(QuestAcceptResult.Accepted, StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach));
            Assert.AreEqual(saves + 1, s.Changes.AutosaveRequestCount, "受注の保存要求は 1 件。");
            Assert.AreEqual(QuestStateKind.InProgress, StoryRules.StateOf(s, q));

            // 重複入力：二度目は無変更。
            long afterAccept = s.Changes.Revision;
            Assert.AreEqual(QuestAcceptResult.AlreadyAccepted, StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach));
            Assert.AreEqual(afterAccept, s.Changes.Revision);

            // 依頼主以外からは受けられない（無変更）。
            Assert.AreEqual(QuestAcceptResult.NotOfferedHere, StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Guide, F.QuestFind));
            Assert.AreEqual(QuestStateKind.NotAccepted, StoryRules.StateOf(s, _f.Quest(F.QuestFind)));

            // 再訪：進行中は「話を聞く」を出さず、進行の文を足す。
            DialogueConversation again = DialogueConversation.Open(s, _f.StoryCatalog, F.Giver);
            Assert.IsFalse(HasChoice(again.Main, DialogueChoiceKind.ListenQuest, F.QuestReach));
            CollectionAssert.Contains(again.Main.Pages, "（依頼「合流点を見てきて」）まだ途中みたいね。");
        }

        [Test]
        public void Quest_ConditionsMetBeforeAccept_DoNotAutoAccept_ButCountAfterAccepting()
        {
            GameSessionState s = _f.NewSession();
            QuestInfo reach = _f.Quest(F.QuestReach);
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            Assert.AreEqual(QuestStateKind.NotAccepted, StoryRules.StateOf(s, reach), "未受注の達成だけで自動受注しない。");
            Assert.AreEqual(0, s.QuestStageOf(F.QuestReach));
            Assert.IsFalse(s.Progress.HasGranted(F.RewardReach), "依頼報酬は付かない。");

            Assert.AreEqual(QuestAcceptResult.Accepted, StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach));
            Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(s, reach), "受注した時点で報告可能。");
        }

        // ================================================================ 三条件と AND（P7 05）・未完遭遇戦（P7 06）

        [Test]
        public void Quest_ThreeObjectiveKinds_AndConjunction()
        {
            GameSessionState s = _f.NewSession();
            foreach (StableId id in new[] { F.QuestReach, F.QuestFind, F.QuestMulti })
            {
                StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, id);
            }

            // 到達
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(s, _f.Quest(F.QuestReach)));

            // 発見（配置物の取得）
            Assert.AreEqual(QuestStateKind.InProgress, StoryRules.StateOf(s, _f.Quest(F.QuestFind)));
            Assert.AreEqual(PlacementPickResult.Picked,
                s.TryPickPlacement(F.Hard, F.FindHard, default, 0, 0, RewardSnapshot.None));
            Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(s, _f.Quest(F.QuestFind)));

            // 複数条件：遭遇戦だけでは未成立、到達も揃って成立（AND）。
            QuestInfo multi = _f.Quest(F.QuestMulti);
            s.CommitEncounterClear(F.Std, F.EncStd, RewardSnapshot.None, default);
            Assert.IsTrue(StoryRules.IsObjectiveMet(s, multi.Objectives[0]));
            Assert.IsFalse(StoryRules.IsObjectiveMet(s, multi.Objectives[1]));
            Assert.AreEqual(QuestStateKind.InProgress, StoryRules.StateOf(s, multi));
            _f.Arrive(s, F.BossArea, F.BossWest);
            Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(s, multi));
        }

        [Test]
        public void Quest_DiscoveryByInvestigationPoint_IsRecognized()
        {
            // 発見の対象は配置物か調査点（campaign の構築時に決まる）。調査点の記録でも成立する。
            GameSessionState s = _f.NewSession();
            var objective = new QuestObjectiveInfoProbe(F.Hard, F.InvHard, investigation: true);
            Assert.IsFalse(StoryRules.IsObjectiveMet(s, objective.Info));
            s.GetOrCreateArea(F.Hard).Investigation.TryMarkInvestigated(F.InvHard);
            Assert.IsTrue(StoryRules.IsObjectiveMet(s, objective.Info));
        }

        [Test]
        public void Quest_UnfinishedEncounter_IsNotMet_AndRetreatKeepsItUnmet()
        {
            GameSessionState s = _f.NewSession();
            StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestMulti);
            _f.Arrive(s, F.BossArea, F.BossWest);

            // 途中 Wave・撤退はクリアを記録しない（遭遇戦の既存規則）。記録が無ければ未達。
            s.AdvanceRespawnCycle(); // 撤退後の休息・死亡でも同じ。
            Assert.AreEqual(QuestStateKind.InProgress, StoryRules.StateOf(s, _f.Quest(F.QuestMulti)));

            // クリア済みの遭遇戦は再戦せずに報告できる（記録は恒久）。
            s.CommitEncounterClear(F.Std, F.EncStd, RewardSnapshot.None, default);
            s.AdvanceRespawnCycle();
            Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(s, _f.Quest(F.QuestMulti)));
        }

        // ================================================================ 報告と一回報酬（P7 07）

        [Test]
        public void Quest_ReportGrants30Once_KeepsSpent_AndDoesNotRerunExistingRewards()
        {
            GameSessionState s = _f.NewSession();
            StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestMulti);
            s.CommitEncounterClear(F.Std, F.EncStd,
                new RewardSnapshot(F.RewardClearStd, 7, default, true), default);
            ArrivalCommit arrive = _f.Arrive(s, F.BossArea, F.BossWest);
            Assert.IsTrue(arrive.FirstVisit);

            int total = s.Progress.TotalVirtue;
            int spent = s.Progress.SpentVirtue;
            int grantedCount = s.Progress.GrantedRewardCount;
            int saves = s.Changes.AutosaveRequestCount;

            Assert.AreEqual(QuestReportResult.Reported,
                StoryProcedures.ReportQuest(s, _f.StoryCatalog, F.Giver, F.QuestMulti, out int granted));
            Assert.AreEqual(F.QuestVirtue, granted);
            Assert.AreEqual(total + F.QuestVirtue, s.Progress.TotalVirtue, "累計徳が 30 増える。");
            Assert.AreEqual(spent, s.Progress.SpentVirtue, "使用済み徳は変えない。");
            Assert.AreEqual(grantedCount + 1, s.Progress.GrantedRewardCount, "依頼報酬の ID だけが増える（到達・遭遇戦は再実行しない）。");
            Assert.IsTrue(s.Progress.HasGranted(F.RewardMulti));
            Assert.AreEqual(saves + 1, s.Changes.AutosaveRequestCount, "報告の保存要求は 1 件（段階・報酬を 1 つの更新で）。");
            Assert.AreEqual(QuestStateKind.Rewarded, StoryRules.StateOf(s, _f.Quest(F.QuestMulti)));

            // 再入・重複：二度目は無変更。
            long revision = s.Changes.Revision;
            Assert.AreEqual(QuestReportResult.AlreadyRewarded,
                StoryProcedures.ReportQuest(s, _f.StoryCatalog, F.Giver, F.QuestMulti, out int again));
            Assert.AreEqual(0, again);
            Assert.AreEqual(revision, s.Changes.Revision);
            Assert.AreEqual(total + F.QuestVirtue, s.Progress.TotalVirtue);

            // 受領済みは再受注できない。
            Assert.AreEqual(QuestAcceptResult.AlreadyRewarded, StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestMulti));
        }

        [Test]
        public void Quest_ReportBeforeConditions_OrBeforeAccept_ChangesNothing()
        {
            GameSessionState s = _f.NewSession();
            long revision = s.Changes.Revision;
            Assert.AreEqual(QuestReportResult.NotAccepted, StoryProcedures.ReportQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach, out _));
            StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach);
            revision = s.Changes.Revision;
            Assert.AreEqual(QuestReportResult.NotReportable, StoryProcedures.ReportQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach, out _));
            Assert.AreEqual(revision, s.Changes.Revision);
            Assert.AreEqual(0, s.Progress.TotalVirtue);

            // 依頼主以外へは報告できない。
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            Assert.AreEqual(QuestReportResult.NotOfferedHere, StoryProcedures.ReportQuest(s, _f.StoryCatalog, F.Guide, F.QuestReach, out _));
        }

        [Test]
        public void Quest_TwoQuestsSharingATarget_ReportIndependently()
        {
            // 同じ達成対象（合流点への到達）を 2 件の依頼が参照しても、それぞれ独立に報告できる。
            var reach2 = new StableId("quest_p7t_reach_again");
            var reward2 = new StableId("reward_p7t_quest_reach_again");
            using (var f = new F((fx, story) =>
                   {
                       var quests = (List<QuestDefinition>)typeof(CampaignStoryData)
                           .GetField("_quests", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                           .GetValue(story);
                       quests.Add(F.QuestOf(reach2, "もう一度合流点へ", fx.Reward(reward2, F.QuestVirtue),
                           QuestObjective.Reach(F.Merge, "合流点へ行く")));
                   }))
            {
                GameSessionState s = f.NewSession();
                StoryProcedures.AcceptQuest(s, f.StoryCatalog, F.Giver, F.QuestReach);
                StoryProcedures.AcceptQuest(s, f.StoryCatalog, F.Giver, reach2);
                f.Arrive(s, F.Merge, F.MergeFromStd);
                Assert.AreEqual(QuestReportResult.Reported, StoryProcedures.ReportQuest(s, f.StoryCatalog, F.Giver, F.QuestReach, out _));
                Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(s, f.Quest(reach2)), "一方の報告で他方は消えない。");
                Assert.AreEqual(QuestReportResult.Reported, StoryProcedures.ReportQuest(s, f.StoryCatalog, F.Giver, reach2, out _));
                Assert.AreEqual(2 * F.QuestVirtue + 5, s.Progress.TotalVirtue, "依頼 2 件＋合流点の初到達 5。");
            }
        }

        // ================================================================ 死亡・休息・FT で保持（P7 08 の状態部分）

        [Test]
        public void Quest_AndEvent_SurviveRespawnCycleAndRest()
        {
            GameSessionState s = _f.NewSession();
            StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach);
            s.CommitEventCompleted(Event(F.GateEvent), out _);
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            StoryProcedures.ReportQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach, out _);
            StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestFind);

            for (int i = 0; i < 3; i++)
            {
                s.AdvanceRespawnCycle(); // 死亡・休息・旅立ちの成功で進む周期。
            }

            Assert.AreEqual(QuestStateKind.Rewarded, StoryRules.StateOf(s, _f.Quest(F.QuestReach)));
            Assert.AreEqual(QuestStateKind.InProgress, StoryRules.StateOf(s, _f.Quest(F.QuestFind)));
            Assert.IsTrue(s.Story.IsEventCompleted(F.GateEvent));
            Assert.IsTrue(s.GetOrCreateArea(F.Hub).IsOpen(F.GateFlag));
        }

        // ================================================================ 経路（P7 09）

        [Test]
        public void Route_ReachedVsCompleted_BossRoute_Reroute_FastTravelKeeps_AndFixedAtClear()
        {
            GameSessionState s = _f.NewSession();
            StableId ch = F.Chapter;

            _f.Arrive(s, F.Std, F.StdWest);
            ChapterRouteRecord r = s.Story.RouteOf(ch);
            Assert.IsTrue(r.StandardReached);
            Assert.IsFalse(r.StandardCompleted, "経路に入っただけでは踏破でない。");

            // 共通区間へ（FT＝お地蔵様の入口）入っただけではどちらも踏破にしない。
            _f.Arrive(s, F.Merge, F.MergeShrineEntry);
            Assert.IsFalse(s.Story.RouteOf(ch).StandardCompleted);
            Assert.AreEqual(StoryRoute.None, s.Story.RouteOf(ch).BossRoute, "未記録は未記録のまま。");

            // 標準の終端から合流 → 踏破・直前の経路・ボス到達経路（合流がボス前）。
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            r = s.Story.RouteOf(ch);
            Assert.IsTrue(r.StandardCompleted);
            Assert.AreEqual(StoryRoute.Standard, r.LastRoute);
            Assert.AreEqual(StoryRoute.Standard, r.BossRoute);

            // 後から別の道で着いたら更新する。
            _f.Arrive(s, F.Hard, F.HardSouth);
            _f.Arrive(s, F.Merge, F.MergeFromHard);
            r = s.Story.RouteOf(ch);
            Assert.IsTrue(r.HardReached && r.HardCompleted && r.StandardCompleted, "両道の記録。");
            Assert.AreEqual(StoryRoute.Hard, s.Story.RouteOf(ch).BossRoute);

            // FT でボス前へ来ても推測しない（既存値を保つ）。
            _f.Arrive(s, F.Merge, F.MergeShrineEntry);
            Assert.AreEqual(StoryRoute.Hard, s.Story.RouteOf(ch).BossRoute);

            // 移動失敗の復旧（経路を記録しない到着）でも変えない。
            _f.Arrive(s, F.Merge, F.MergeFromStd, recordRoute: false);
            Assert.AreEqual(StoryRoute.Hard, s.Story.RouteOf(ch).BossRoute);

            // 章クリアで固定。以後の到着でクリア時の値は変わらない（ボス到達経路も変えない）。
            MarkChapterClearedForTest(s);
            Assert.AreEqual(StoryRoute.Hard, s.Story.ClearedRouteOf(ch));
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            Assert.AreEqual(StoryRoute.Hard, s.Story.ClearedRouteOf(ch));
            Assert.AreEqual(StoryRoute.Hard, s.Story.RouteOf(ch).BossRoute);
        }

        [Test]
        public void Route_ArrivalUpdate_IsMergedIntoArrivalSave()
        {
            GameSessionState s = _f.NewSession();
            int saves = s.Changes.AutosaveRequestCount;
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            Assert.AreEqual(saves + 1, s.Changes.AutosaveRequestCount, "到着と経路の記録は 1 つの保存要求（依頼ごとに Snapshot を量産しない）。");
        }

        // ================================================================ 必須イベントと門（P7 10 の状態部分）

        [Test]
        public void Event_CompletesOnce_OpensFlagInSameUpdate_AndOnlyFromItsDialogue()
        {
            GameSessionState s = _f.NewSession();
            StoryCatalog story = _f.StoryCatalog;
            DialogueConversation c = DialogueConversation.Open(s, story, F.Guide);
            Assert.AreEqual(F.DlgGuideGate, c.Dialogue.DialogueId);
            Assert.IsTrue(HasChoice(c.Main, DialogueChoiceKind.ConfirmEvent, F.GateEvent));

            // 途中で閉じる・ページを進めるだけでは無変更。
            long revision = s.Changes.Revision;
            c.Advance();
            Assert.AreEqual(revision, s.Changes.Revision);
            Assert.IsFalse(s.Story.IsEventCompleted(F.GateEvent));

            // 別の会話からは完了させない。
            DialogueConversation other = DialogueConversation.Open(s, story, F.Giver);
            Assert.IsFalse(StoryProcedures.CompleteEvent(s, story, other.Dialogue, F.GateEvent, out _));

            int saves = s.Changes.AutosaveRequestCount;
            Assert.IsTrue(StoryProcedures.CompleteEvent(s, story, c.Dialogue, F.GateEvent, out bool opened));
            Assert.IsTrue(opened);
            Assert.IsTrue(s.GetOrCreateArea(F.Hub).IsOpen(F.GateFlag), "門の開通も同じ更新で記録。");
            Assert.AreEqual(saves + 1, s.Changes.AutosaveRequestCount);

            Assert.IsFalse(StoryProcedures.CompleteEvent(s, story, c.Dialogue, F.GateEvent, out _), "一回だけ。");
            Assert.AreEqual(saves + 1, s.Changes.AutosaveRequestCount);
        }

        // ================================================================ Data の検査（P7 17 の Validator 部分）

        [Test]
        public void DataCheck_DetectsDuplicatesUnknownsEmptyFallbackAmbiguityNegativeEmptyObjectivesAndSelfDependency()
        {
            var errors = new List<string>();
            Assert.IsTrue(StoryDataCheck.Check(_f.Story, errors), string.Join("\n", errors));

            AssertDetected(story => Add(story, "_villagers", Villager(F.Guide, "重複")), "duplicate villager");
            AssertDetected(story => Add(story, "_dialogues", F.Dlg(new StableId("dialogue_x"), new StableId("villager_nobody"), 99, "x")),
                "unknown");
            AssertDetected(story => Add(story, "_dialogues", F.Dlg(F.DlgGuideDefault, F.Guide, 99, "x")), "duplicate dialogue");
            AssertDetected(story => Add(story, "_dialogues", F.Dlg(new StableId("dialogue_y"), F.Guide, 10, "x",
                F.Cond(StoryCondition.Event(F.GateEvent)))), "ambiguous");
            AssertDetected(story =>
            {
                var d = new DialogueDefinition();
                d.EditorSet(new StableId("dialogue_empty"), F.Guide, 98, "案内の老人", new List<string> { " " });
                Add(story, "_dialogues", d);
            }, "empty");
            AssertDetected(story =>
            {
                var d = new DialogueDefinition();
                d.EditorSet(new StableId("dialogue_long"), F.Guide, 97, "案内の老人", new List<string> { new string('あ', 500) });
                Add(story, "_dialogues", d);
            }, "longer than");
            AssertDetected(story => RemoveDialogue(story, F.DlgGiverDefault), "no unconditional");
            AssertDetected(story => Add(story, "_dialogues", F.Dlg(new StableId("dialogue_below"), F.Guide, -5, "x",
                F.Cond(StoryCondition.Event(F.GateEvent)))), "never chosen");
            AssertDetected(story => Add(story, "_dialogues", F.Dlg(new StableId("dialogue_unknown_quest"), F.Guide, 50, "x",
                F.Cond(StoryCondition.Quest(new StableId("quest_none"), QuestStateKind.Rewarded)))), "unknown quest");
            AssertDetected(story =>
            {
                QuestDefinition q = Quests(story)[0];
                F.SetPrivate(q.Reward, "_virtueAmount", -1);
            }, "negative");
            AssertDetected(story => F.SetPrivate(Quests(story)[1], "_objectives", new List<QuestObjective>()), "no objectives");
            AssertDetected(story =>
            {
                // 自己依存：門を開ける会話が「門が開いている」を条件にしている。
                foreach (DialogueDefinition d in Dialogues(story))
                {
                    if (d.DialogueId.Equals(F.DlgGuideGate))
                    {
                        F.SetPrivate(d, "_conditions", F.Cond(StoryCondition.Event(F.GateEvent)));
                    }
                }
            }, "depends on itself");
            AssertDetected(story => F.SetPrivate(Chapters(story)[0], "_bossId", default(StableId)), "no valid boss");
        }

        [Test]
        public void CatalogBuild_DetectsUnknownAreasTargetsAndBossMapping()
        {
            AssertBuildFails(story => F.SetPrivate(Quests(story)[1].Objectives[0], "_targetId", new StableId("find_nowhere")),
                "neither a pickup nor an investigation point");
            AssertBuildFails(story => F.SetPrivate(Quests(story)[2].Objectives[0], "_targetId", new StableId("encounter_nowhere")),
                "is not in");
            AssertBuildFails(story => F.SetPrivate(Chapters(story)[0], "_bossId", F.EncStd), "is not a boss encounter");
            AssertBuildFails(story => F.SetPrivate(Chapters(story)[0].Standard, "_terminalEntryId", new StableId("entry_nowhere")),
                "cannot be resolved");
            AssertBuildFails(story => F.SetPrivate(Villagers(story)[0], "_areaId", new StableId("area_nowhere")), "unknown area");
            AssertBuildFails(story => F.SetPrivate(Events(story)[0], "_opensFlagId", new StableId("flag_nowhere")), "is not in");
        }

        // ================================================================ 補助

        private StoryEventInfo Event(StableId id)
        {
            Assert.IsTrue(_f.StoryCatalog.TryGetEvent(id, out StoryEventInfo e));
            return e;
        }

        /// <summary>章クリアの記録だけを置く（P7 04 の本確定を通さない状態部分の検査用）。</summary>
        private void MarkChapterClearedForTest(GameSessionState s)
        {
            var routes = new List<KeyValuePair<string, ChapterRouteRecord>>();
            s.Story.CopyRoutesTo(routes);
            var cleared = new List<KeyValuePair<string, StoryRoute>>();
            s.Story.CopyClearedChaptersTo(cleared);
            cleared.Add(new KeyValuePair<string, StoryRoute>(F.Chapter.Value, s.Story.RouteOf(F.Chapter).BossRoute));
            var events = new List<string>();
            s.Story.CopyEventsTo(events);
            typeof(GameSessionState).GetMethod("RestoreStory",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(s, new object[] { events, routes, cleared });
        }

        private static bool HasChoice(DialogueScreen screen, DialogueChoiceKind kind, StableId target)
        {
            foreach (DialogueChoice c in screen.Choices)
            {
                if (c.Kind == kind && c.TargetId.Equals(target))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertDetected(System.Action<CampaignStoryData> mutate, string expected)
        {
            using (var f = new F())
            {
                mutate(f.Story);
                var errors = new List<string>();
                Assert.IsFalse(StoryDataCheck.Check(f.Story, errors), "検出されない：" + expected);
                Assert.IsTrue(errors.Exists(e => e.Contains(expected)), "期待した指摘（" + expected + "）が無い：\n" + string.Join("\n", errors));
            }
        }

        private static void AssertBuildFails(System.Action<CampaignStoryData> mutate, string expected)
        {
            using (var f = new F())
            {
                mutate(f.Story);
                Assert.IsFalse(Momotaro.Gameplay.Session.AreaCatalog.TryBuild(f.Data, out _, out IReadOnlyList<string> errors),
                    "構築できてしまった：" + expected);
                Assert.IsTrue(new List<string>(errors).Exists(e => e.Contains(expected)),
                    "期待した指摘（" + expected + "）が無い：\n" + string.Join("\n", errors));
            }
        }

        private static VillagerDefinition Villager(StableId id, string name)
        {
            var v = new VillagerDefinition();
            v.EditorSet(id, name, F.Hub);
            return v;
        }

        private static void Add<T>(CampaignStoryData story, string field, T item)
        {
            var list = (List<T>)typeof(CampaignStoryData)
                .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(story);
            list.Add(item);
        }

        private static List<T> ListOf<T>(CampaignStoryData story, string field) =>
            (List<T>)typeof(CampaignStoryData)
                .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(story);

        private static List<QuestDefinition> Quests(CampaignStoryData s) => ListOf<QuestDefinition>(s, "_quests");
        private static List<DialogueDefinition> Dialogues(CampaignStoryData s) => ListOf<DialogueDefinition>(s, "_dialogues");
        private static List<ChapterDefinition> Chapters(CampaignStoryData s) => ListOf<ChapterDefinition>(s, "_chapters");
        private static List<VillagerDefinition> Villagers(CampaignStoryData s) => ListOf<VillagerDefinition>(s, "_villagers");
        private static List<StoryEventDefinition> Events(CampaignStoryData s) => ListOf<StoryEventDefinition>(s, "_events");

        private static void RemoveDialogue(CampaignStoryData story, StableId id) =>
            Dialogues(story).RemoveAll(d => d.DialogueId.Equals(id));

        /// <summary>調査点を対象にした達成条件を作る（Data を経由しない検査用）。</summary>
        private sealed class QuestObjectiveInfoProbe
        {
            public QuestObjectiveInfoProbe(StableId area, StableId target, bool investigation)
            {
                Info = (QuestObjectiveInfo)typeof(QuestObjectiveInfo)
                    .GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                        new[] { typeof(QuestObjectiveKind), typeof(StableId), typeof(StableId), typeof(bool), typeof(string) }, null)
                    .Invoke(new object[] { QuestObjectiveKind.Discovery, area, target, investigation, "probe" });
            }

            public QuestObjectiveInfo Info { get; }
        }
    }
}
