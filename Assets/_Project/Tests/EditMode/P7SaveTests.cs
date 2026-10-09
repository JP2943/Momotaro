using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Story;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Story;
using Momotaro.Infrastructure.Save;
using NUnit.Framework;
using F = Momotaro.Tests.EditMode.P7StoryFixture;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P7：保存形式（版 4）と移行・不正データ（受入 P7 16）、章クリアの確定と権利（P7 11・P7 12 の状態部分）、保存単位（P7 14 の要求数）。
    /// </summary>
    public sealed class P7SaveTests
    {
        private F _f;

        [SetUp]
        public void SetUp() => _f = new F();

        [TearDown]
        public void TearDown() => _f.Dispose();

        // ================================================================ 章ボスの確定（P7 11・P7 12）

        [Test]
        public void Chapter_BossVictory_CommitsClearBossChapterAndRightsInOneUpdate()
        {
            GameSessionState s = _f.NewSession();
            _f.Arrive(s, F.Std, F.StdWest);
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            int saves = s.Changes.AutosaveRequestCount;
            int virtue = s.Progress.TotalVirtue;

            ChapterClearCommit commit = Clear(s);
            Assert.IsTrue(commit.Encounter.Recorded);
            Assert.IsTrue(commit.BossRecorded);
            Assert.IsTrue(commit.ChapterCleared);
            Assert.AreEqual(ChapterRightsResult.Processed, commit.Rights);
            Assert.AreEqual(3, commit.RightsAdded, "3＋3＝6（上限 6）。");
            Assert.AreEqual(6, s.Progress.RefundRights);
            Assert.IsTrue(s.Progress.IsChapterProcessed(F.Chapter));
            Assert.IsTrue(s.Story.IsChapterCleared(F.Chapter));
            Assert.AreEqual(StoryRoute.Standard, s.Story.ClearedRouteOf(F.Chapter), "クリア時のボス到達経路を固定。");
            Assert.IsTrue(s.GetOrCreateArea(F.BossArea).IsBossDefeated(F.BossId));
            Assert.AreEqual(virtue, s.Progress.TotalVirtue, "章クリアの徳ボーナスは無い（P7）。");
            Assert.AreEqual(saves + 1, s.Changes.AutosaveRequestCount, "保存要求は 1 件。");

            // 再通知：何もしない。
            ChapterClearCommit again = Clear(s);
            Assert.IsFalse(again.Encounter.Recorded);
            Assert.IsFalse(again.ChapterCleared);
            Assert.AreEqual(6, s.Progress.RefundRights);
            Assert.AreEqual(saves + 1, s.Changes.AutosaveRequestCount);
        }

        [Test]
        public void Chapter_RightsAtCap_AddZero_ButMarkProcessed_AndNeverRegrantAfterUseAndReload()
        {
            GameSessionState s = _f.NewSession();
            ShrineProcedures.GrantChapterRefundRights(s, _f.Campaign, new StableId("chapter_p7t_dummy_never"), out _); // 未知：無変更
            Assert.AreEqual(3, s.Progress.RefundRights);

            // 上限 6 に達した状態で章クリア：実増加 0 でも処理済み。
            SetRights(s, 6);
            ChapterClearCommit commit = Clear(s);
            Assert.AreEqual(ChapterRightsResult.Processed, commit.Rights);
            Assert.AreEqual(0, commit.RightsAdded, "上限で 0。");
            Assert.IsTrue(s.Progress.IsChapterProcessed(F.Chapter));

            // 権利を使ってから保存・再読込しても、同じ章の権利は再付与されない。
            SetRights(s, 1);
            GameSessionState loaded = RoundTrip(s);
            Assert.AreEqual(1, loaded.Progress.RefundRights);
            Assert.AreEqual(ChapterRightsResult.AlreadyProcessed,
                ShrineProcedures.GrantChapterRefundRights(loaded, _f.Campaign, F.Chapter, out int added));
            Assert.AreEqual(0, added);
            Assert.IsFalse(Clear(loaded).ChapterCleared, "Load 後の再通知でも章クリアを再実行しない。");
            Assert.AreEqual(1, loaded.Progress.RefundRights);
        }

        // ================================================================ 版 4 の往復（P7 16）

        [Test]
        public void Save_V4_RoundTripsNonInitialStoryValues()
        {
            GameSessionState s = RichStorySession();
            string json = Serialize(s);
            StringAssert.Contains("\"schemaVersion\": 4", json);
            StringAssert.Contains("\"story\"", json);
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(json, out _, out SaveSnapshot read, out string error), error);
            Assert.IsTrue(read.HasStoryData);
            Assert.IsTrue(SessionRestorer.TryBuildCandidate(read, _f.Catalog, out GameSessionState c, out error), error);

            Assert.AreEqual(QuestStateKind.Rewarded, StoryRules.StateOf(c, _f.Quest(F.QuestReach)));
            Assert.AreEqual(QuestStateKind.InProgress, StoryRules.StateOf(c, _f.Quest(F.QuestFind)));
            Assert.AreEqual(QuestStateKind.NotAccepted, StoryRules.StateOf(c, _f.Quest(F.QuestMulti)));
            Assert.IsTrue(c.Story.IsEventCompleted(F.GateEvent));
            Assert.IsTrue(c.GetOrCreateArea(F.Hub).IsOpen(F.GateFlag));
            ChapterRouteRecord r = c.Story.RouteOf(F.Chapter);
            Assert.IsTrue(r.StandardReached && r.StandardCompleted && r.HardReached && r.HardCompleted);
            Assert.AreEqual(StoryRoute.Hard, r.LastRoute);
            Assert.AreEqual(StoryRoute.Hard, r.BossRoute);
            Assert.IsTrue(c.Story.IsChapterCleared(F.Chapter));
            Assert.AreEqual(StoryRoute.Hard, c.Story.ClearedRouteOf(F.Chapter));
            Assert.AreEqual(s.Progress.TotalVirtue, c.Progress.TotalVirtue);
            Assert.AreEqual(s.Progress.RefundRights, c.Progress.RefundRights);
            Assert.AreEqual(json, Serialize(c), "往復で 1 文字も変わらない。");
        }

        [Test]
        public void Save_V3_MigratesToEmptyStory_AndStoryFieldInV3IsRejected()
        {
            GameSessionState s = _f.NewSession();
            StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach);
            ShrineProcedures.GrantChapterRefundRights(s, _f.Campaign, F.Chapter, out _); // 版 3 時代の処理済み章
            string v4 = Serialize(s);
            string payload = PayloadOf(v4);
            int at = payload.IndexOf(",\"story\":", StringComparison.Ordinal);
            Assert.Greater(at, 0, "前提：現行版は story を末尾に持つ。");
            string payloadV3 = payload.Substring(0, at) + "}";

            Assert.IsTrue(SaveJsonCodec.TryDeserialize(Envelope(v4, 3, payloadV3), out _, out SaveSnapshot read, out string error),
                error);
            Assert.IsFalse(read.HasStoryData);
            Assert.IsTrue(SessionRestorer.TryBuildCandidate(read, _f.Catalog, out GameSessionState c, out error), error);
            Assert.AreEqual(0, c.Story.CompletedEventCount, "版 3 は明示移行でイベント空。");
            Assert.AreEqual(0, c.Story.ClearedChapterCount, "章クリアは遡及しない。");
            Assert.AreEqual(QuestStateKind.InProgress, StoryRules.StateOf(c, _f.Quest(F.QuestReach)), "依頼は既存の questStages のまま。");
            Assert.IsTrue(c.Progress.IsChapterProcessed(F.Chapter), "処理済み章は残る（再付与しない）。");

            // 版 3 に story を付けたものは未知の欄で拒否。
            Assert.IsFalse(SaveJsonCodec.TryDeserialize(Envelope(v4, 3, payload), out _, out _, out _));

            // 移行後に保存し直すと版 4 になる。
            StringAssert.Contains("\"schemaVersion\": 4", Serialize(c));
        }

        [Test]
        public void Save_RejectsInvalidStoryData()
        {
            SaveSnapshot ok = SaveSnapshot.Capture(RichStorySession(), _f.Campaign, Party());
            AssertValid(ok, "前提");

            AssertInvalid(With(ok, events: new[] { "event_unknown" }), "未知のイベント");
            AssertInvalid(With(ok, events: new[] { F.GateEvent.Value, F.GateEvent.Value }), "重複イベント");
            AssertInvalid(With(ok, areas: WithoutFlag(ok, F.Hub.Value, F.GateFlag.Value)), "イベント完了なのに門が閉じている");
            AssertInvalid(With(ok, events: Array.Empty<string>()), "門が開いているのにイベント未完了");
            AssertInvalid(With(ok, routes: new[] { Route("chapter_unknown", true, true, false, false, StoryRoute.Standard, StoryRoute.Standard) }),
                "未知の章の経路");
            AssertInvalid(With(ok, routes: new[] { Route(F.Chapter.Value, true, false, true, true, StoryRoute.Standard, StoryRoute.Hard) }),
                "踏破していない経路が直前の経路");
            AssertInvalid(With(ok, cleared: new[] { new KeyValuePair<string, StoryRoute>("chapter_unknown", StoryRoute.None) }),
                "未知の章のクリア");
            AssertInvalid(With(ok, chapters: Array.Empty<string>()), "章クリアなのに処理済み章が無い");
            AssertInvalid(With(ok, cleared: Array.Empty<KeyValuePair<string, StoryRoute>>()), "章ボス撃破なのに章クリアが無い");
            AssertInvalid(With(ok, quests: Pairs(F.QuestReach.Value, 3, F.QuestFind.Value, 1)), "依頼の段階が範囲外");
            AssertInvalid(With(ok, quests: Pairs(F.QuestReach.Value, 1, F.QuestFind.Value, 1)), "報酬付与済みなのに受領済みでない");
            AssertInvalid(With(ok, granted: Remove(ok.GrantedRewards, F.RewardReach.Value)), "受領済みなのに報酬が無い");
            AssertInvalid(With(ok, hasStory: false), "story の無い保存に値がある");
        }

        // ================================================================ 補助

        private ChapterClearCommit Clear(GameSessionState s) =>
            s.CommitChapterBossVictory(F.BossArea, F.BossId, RewardSnapshot.None, default, F.Chapter,
                _f.Campaign.RefundRightsPerChapter, _f.Campaign.RefundRightsMax);

        private static void SetRights(GameSessionState s, int rights) =>
            typeof(PlayerProgressState).GetProperty("RefundRights").GetSetMethod(true).Invoke(s.Progress, new object[] { rights });

        /// <summary>依頼 2 段階・イベント・両道・章クリアを持つ Session。</summary>
        private GameSessionState RichStorySession()
        {
            GameSessionState s = _f.NewSession();
            StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach);
            StoryProcedures.AcceptQuest(s, _f.StoryCatalog, F.Giver, F.QuestFind);
            Assert.IsTrue(s.CommitEventCompleted(Event(F.GateEvent), out _));
            _f.Arrive(s, F.Std, F.StdWest);
            _f.Arrive(s, F.Merge, F.MergeFromStd);
            _f.Arrive(s, F.Hard, F.HardSouth);
            _f.Arrive(s, F.Merge, F.MergeFromHard);
            Assert.AreEqual(QuestReportResult.Reported,
                StoryProcedures.ReportQuest(s, _f.StoryCatalog, F.Giver, F.QuestReach, out _));
            _f.Arrive(s, F.BossArea, F.BossWest);
            Assert.IsTrue(Clear(s).ChapterCleared);
            return s;
        }

        private StoryEventInfo Event(StableId id)
        {
            Assert.IsTrue(_f.StoryCatalog.TryGetEvent(id, out StoryEventInfo e));
            return e;
        }

        private string Serialize(GameSessionState s) =>
            SaveJsonCodec.Serialize(SaveSnapshot.Capture(s, _f.Campaign, Party()), 1, DateTime.UnixEpoch);

        private static PartySaveValues Party() =>
            new PartySaveValues(new PlayerSaveValues(50, 40f, 0f, 0f), false, default);

        private GameSessionState RoundTrip(GameSessionState s)
        {
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(Serialize(s), out _, out SaveSnapshot read, out string error), error);
            Assert.IsTrue(SessionRestorer.TryBuildCandidate(read, _f.Catalog, out GameSessionState c, out error), error);
            return c;
        }

        private void AssertValid(SaveSnapshot snap, string label)
        {
            var errors = new List<string>();
            Assert.IsTrue(SaveSnapshotValidator.Validate(snap, _f.Catalog, errors), label + ": " + string.Join(" / ", errors));
        }

        private void AssertInvalid(SaveSnapshot snap, string label)
        {
            var errors = new List<string>();
            Assert.IsFalse(SaveSnapshotValidator.Validate(snap, _f.Catalog, errors), label + " を拒否する。");
            Assert.IsFalse(SessionRestorer.TryBuildCandidate(snap, _f.Catalog, out _, out _), label + "：候補を作らない。");
        }

        private static KeyValuePair<string, ChapterRouteRecord> Route(string chapter, bool sr, bool sc, bool hr, bool hc,
            StoryRoute last, StoryRoute boss) =>
            new KeyValuePair<string, ChapterRouteRecord>(chapter, new ChapterRouteRecord(sr, sc, hr, hc, last, boss));

        private static KeyValuePair<string, int>[] Pairs(string k1, int v1, string k2, int v2) =>
            new[] { new KeyValuePair<string, int>(k1, v1), new KeyValuePair<string, int>(k2, v2) };

        private static string[] Remove(IReadOnlyList<string> source, string value)
        {
            var list = new List<string>(source);
            list.Remove(value);
            return list.ToArray();
        }

        private static AreaSaveRecord[] WithoutFlag(SaveSnapshot s, string areaId, string flag)
        {
            var list = new List<AreaSaveRecord>();
            foreach (AreaSaveRecord a in s.Areas)
            {
                if (a.AreaId != areaId)
                {
                    list.Add(a);
                    continue;
                }

                var flags = new List<string>(a.OpenedFlags);
                flags.Remove(flag);
                list.Add(new AreaSaveRecord(a.AreaId, new List<string>(a.Investigated).ToArray(), flags.ToArray(),
                    new List<string>(a.ClearedEncounters).ToArray(), new List<string>(a.DefeatedBosses).ToArray(),
                    new List<string>(a.PickedPlacements).ToArray(), new List<KeyValuePair<string, int>>(a.FieldDefeats).ToArray()));
            }

            return list.ToArray();
        }

        private static SaveSnapshot With(SaveSnapshot s, string[] events = null,
            KeyValuePair<string, ChapterRouteRecord>[] routes = null, KeyValuePair<string, StoryRoute>[] cleared = null,
            string[] chapters = null, KeyValuePair<string, int>[] quests = null, string[] granted = null,
            AreaSaveRecord[] areas = null, bool hasStory = true)
        {
            return new SaveSnapshot(s.CampaignId, s.ContentVersion, s.AdventureId, s.Revision, s.RespawnCycle,
                s.TotalVirtue, s.SpentVirtue, granted ?? new List<string>(s.GrantedRewards).ToArray(),
                new List<KeyValuePair<string, int>>(s.Growth).ToArray(),
                new List<string>(s.VisitedAreas).ToArray(), new List<string>(s.Recruited).ToArray(),
                areas ?? new List<AreaSaveRecord>(s.Areas).ToArray(),
                new List<KeyValuePair<string, int>>(s.Inventory).ToArray(), s.Kibidango,
                new List<string>(s.RegisteredShrines).ToArray(), s.Checkpoint, s.ResumeKind, s.ResumeAreaId,
                s.ResumePointId, s.Party, quests ?? new List<KeyValuePair<string, int>>(s.QuestStages).ToArray(),
                true, s.RefundRights, chapters ?? new List<string>(s.ProcessedChapters).ToArray(),
                hasStory, events ?? new List<string>(s.CompletedEvents).ToArray(),
                routes ?? new List<KeyValuePair<string, ChapterRouteRecord>>(s.Routes).ToArray(),
                cleared ?? new List<KeyValuePair<string, StoryRoute>>(s.ClearedChapters).ToArray());
        }

        private static string PayloadOf(string json)
        {
            string compact = Minify(json);
            int at = compact.IndexOf("\"payload\":", StringComparison.Ordinal);
            return compact.Substring(at + "\"payload\":".Length, compact.Length - at - "\"payload\":".Length - 1);
        }

        private static string Envelope(string source, int schema, string payload)
        {
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(source, out SaveEnvelopeInfo info, out _, out string error), error);
            return "{\"schemaVersion\":" + schema + ",\"contentVersion\":" + info.ContentVersion
                + ",\"campaignId\":\"" + info.CampaignId + "\",\"adventureId\":\"" + info.AdventureId
                + "\",\"generation\":5,\"savedAtUtc\":\"" + info.SavedAtUtc + "\",\"checksum\":\""
                + SaveJsonCodec.Sha256Hex(payload) + "\",\"payload\":" + payload + "}";
        }

        private static string Minify(string json)
        {
            var sb = new System.Text.StringBuilder(json.Length);
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inString = !inString;
                }

                if (!inString && char.IsWhiteSpace(c))
                {
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
