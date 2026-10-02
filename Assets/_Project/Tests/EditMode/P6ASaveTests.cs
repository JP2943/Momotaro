using System;
using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.Progression;
using Momotaro.Data.World;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Save;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P6A-02：非破壊 Snapshot、DTO 検証、二世代ストレージ、候補 Load（P6 仕様 §9・§10、受入 P6A 13／18／19／20／21）。
    ///
    /// ファイルは<b>注入した一時ディレクトリだけ</b>を使う（本物のセーブを触らない。仕様 §10）。
    /// 書込担当は <see cref="ManualSaveExecutor"/>——「書込中に要求が来る」を遅延の度合いに頼らず作る。
    /// </summary>
    public sealed class P6ASaveTests
    {
        private static readonly StableId CampaignId = new StableId("campaign_p6a_test");
        private static readonly StableId AreaA = new StableId("area_p6_a");
        private static readonly StableId AreaB = new StableId("area_p6_b");
        private static readonly StableId EntryAStart = new StableId("entry_p6_a_start");
        private static readonly StableId EntryAShrine = new StableId("entry_p6_a_shrine");
        private static readonly StableId EntryBWest = new StableId("entry_p6_b_west");
        private static readonly StableId EntryBShrine = new StableId("entry_p6_b_shrine");
        private static readonly StableId ShrineA = new StableId("shrine_p6_a");
        private static readonly StableId ShrineB = new StableId("shrine_p6_b");
        private static readonly StableId Encounter = new StableId("encounter_p6_b_01");
        private static readonly StableId Boss = new StableId("encounter_p6_b_boss");
        private static readonly StableId Field = new StableId("field_p6_a_01");
        private static readonly StableId Pickup = new StableId("pickup_p6_b_01");
        private static readonly StableId Flag = new StableId("flag_p6_b_gate");
        private static readonly StableId Investigation = new StableId("investigate_p6_b_01");
        private static readonly StableId Tonic = new StableId("item_p6_tonic");
        private static readonly StableId GrowthHp = new StableId("growth_p6_hp");
        private static readonly StableId GrowthHp2 = new StableId("growth_p6_hp2");
        private static readonly StableId QuestFixture = new StableId("quest_p6_fixture");

        private readonly List<Object> _spawned = new List<Object>();
        private string _dir;
        private AreaCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "momotaro_p6a_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _catalog = BuildCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();

            // ロックを放し忘れた FileStream を閉じさせてから消す（Windows は開いたファイルを消せない）。
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }

        // ================================================================ RoundTrip（P6A 13）

        /// <summary>全保存項目を非初期値で往復させ、同じ Snapshot（同じ JSON）に戻ることを確かめる。</summary>
        [Test]
        public void RoundTrip_AllFieldsNonDefault_ProduceIdenticalSnapshot()
        {
            GameSessionState session = BuildRichSession();
            PartySaveValues party = RichParty();
            SaveSnapshot first = SaveSnapshot.Capture(session, _catalog.Campaign, party);
            DateTime at = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

            string json = SaveJsonCodec.Serialize(first, 7, at);
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(json, out SaveEnvelopeInfo info, out SaveSnapshot read, out string error), error);
            Assert.AreEqual(7, info.Generation);
            Assert.IsTrue(SessionRestorer.TryBuildCandidate(read, _catalog, out GameSessionState candidate, out error), error);

            SaveSnapshot again = SaveSnapshot.Capture(candidate, _catalog.Campaign, read.Party);
            Assert.AreEqual(json, SaveJsonCodec.Serialize(again, 7, at), "往復で 1 文字も変わらない。");

            // 値そのものも確かめる（JSON が両方とも空だった、を通さない）。
            Assert.AreEqual(session.Progress.TotalVirtue, candidate.Progress.TotalVirtue);
            Assert.AreEqual(20, candidate.Progress.SpentVirtue);
            Assert.IsTrue(candidate.Progress.HasGrowth(GrowthHp));
            Assert.IsTrue(candidate.Progress.HasGranted(new StableId("reward_p6_clear_b1")));
            Assert.AreEqual(2, candidate.RespawnCycle);
            Assert.AreEqual(session.Changes.Revision, candidate.Changes.Revision);
            Assert.IsTrue(candidate.HasVisited(AreaB));
            Assert.IsTrue(candidate.IsRecruited(new StableId("companion_inumaru")));
            Assert.AreEqual(2, candidate.QuestStageOf(QuestFixture), "クエスト段階（P7 の接続口）も往復する。");
            Assert.AreEqual(1, candidate.Inventory.CountOf(Tonic));
            Assert.AreEqual(1, candidate.Kibidango);
            Assert.IsTrue(candidate.IsShrineRegistered(ShrineB));
            Assert.AreEqual(ShrineB, candidate.Checkpoint);
            Assert.AreEqual(ResumeAnchor.AtEntry(AreaB, EntryBWest), candidate.Resume);
            Assert.AreEqual(session.AdventureId, candidate.AdventureId);
            Assert.IsTrue(candidate.TryGetArea(AreaB, out AreaRuntimeState b));
            Assert.IsTrue(b.IsEncounterCleared(Encounter, candidate.RespawnCycle));
            Assert.IsTrue(b.IsBossDefeated(Boss));
            Assert.IsTrue(b.IsOpen(Flag));
            Assert.IsTrue(b.IsPlacementPicked(Pickup));
            Assert.IsTrue(b.Investigation.IsInvestigated(Investigation));
            Assert.IsTrue(candidate.TryGetArea(AreaA, out AreaRuntimeState a));
            Assert.IsTrue(a.IsFieldEnemyDefeated(Field, candidate.RespawnCycle));
            Assert.AreEqual(party.Player.Hp, read.Party.Player.Hp);
            Assert.AreEqual(party.Companion.GuardianCooldown, read.Party.Companion.GuardianCooldown);
            Assert.IsTrue(read.Party.Companion.IsDown);
        }

        /// <summary>Snapshot は採取後の Runtime の変化を受けない（可変参照を共有しない。P6A 13）。</summary>
        [Test]
        public void Snapshot_DoesNotShareMutableStateWithRuntime()
        {
            GameSessionState session = BuildRichSession();
            SaveSnapshot snap = SaveSnapshot.Capture(session, _catalog.Campaign, RichParty());
            string before = SaveJsonCodec.Serialize(snap, 1, DateTime.UnixEpoch);

            session.Progress.TryGrant(new RewardSnapshot(new StableId("reward_enemy_melee"), 99, default, false), out _);
            session.GetOrCreateArea(AreaB).Investigation.TryMarkInvestigated(new StableId("investigate_p6_b_02"));
            session.Inventory.TryAdd(Tonic, 1, 3);
            session.AdvanceRespawnCycle();
            session.RegisterShrine(Shrine(ShrineA));
            session.TrySetQuestStage(QuestFixture, 5);

            Assert.AreEqual(before, SaveJsonCodec.Serialize(snap, 1, DateTime.UnixEpoch), "採取後の変化が Snapshot に漏れない。");
        }

        /// <summary>Load は何も「起こさない」：報酬・保存要求・版の進みが無い（仕様 §10）。</summary>
        [Test]
        public void Restore_DoesNotGrantOrRequestSaves()
        {
            GameSessionState session = BuildRichSession();
            SaveSnapshot snap = SaveSnapshot.Capture(session, _catalog.Campaign, RichParty());

            Assert.IsTrue(SessionRestorer.TryBuildCandidate(snap, _catalog, out GameSessionState candidate, out string error), error);
            int requests = 0;
            candidate.Changes.AutosaveRequested += _ => requests++;

            Assert.AreEqual(snap.Revision, candidate.Changes.Revision, "版は保存の値のまま。");
            Assert.AreEqual(0, candidate.Changes.AutosaveRequestCount, "復元で保存要求を出さない。");
            Assert.AreEqual(snap.TotalVirtue, candidate.Progress.TotalVirtue, "復元で徳を足さない。");
            Assert.AreEqual(snap.RespawnCycle, candidate.RespawnCycle, "復元で周期を進めない。");
            Assert.AreEqual(0, requests);
        }

        // ================================================================ 厳格な読み取り（P6A 20）

        [Test]
        public void Codec_RejectsMissingDuplicateUnknownWrongTypeAndTamper()
        {
            string json = SaveJsonCodec.Serialize(
                SaveSnapshot.Capture(BuildRichSession(), _catalog.Campaign, RichParty()), 3, DateTime.UnixEpoch);
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(json, out _, out _, out string ok), ok);

            AssertRejected(json.Replace("\"kibidango\": 1,", ""), "必須欠損");
            AssertRejected(json.Replace("\"kibidango\": 1,", "\"kibidango\": 1,\n    \"kibidango\": 1,"), "重複プロパティ");
            AssertRejected(json.Replace("\"kibidango\": 1,", "\"kibidango\": 1,\n    \"bonus\": 5,"), "未知の欄");
            AssertRejected(json.Replace("\"kibidango\": 1,", "\"kibidango\": \"1\","), "型違い");
            AssertRejected(json.Replace("\"total\": ", "\"total\": 1"), "改ざん（checksum 不一致）");
            AssertRejected(json + "{}", "後ろの余計な内容");
            AssertRejected(json.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 3"), "未対応の版");
            AssertRejected(json.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1"), "版 1 に questStages は無い（未知の欄）");
            AssertRejected(string.Empty, "空");

            // 0 は欠損と区別される（0 は正当な値）。
            GameSessionState zero = BuildRichSession();
            zero.TryConsumeKibidango(zero.Kibidango);
            string zeroJson = SaveJsonCodec.Serialize(
                SaveSnapshot.Capture(zero, _catalog.Campaign, RichParty()), 3, DateTime.UnixEpoch);
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(zeroJson, out _, out SaveSnapshot z, out string zeroError), zeroError);
            Assert.AreEqual(0, z.Kibidango);
        }

        /// <summary>
        /// 版 1（クエスト段階の接続口が無い）の保存は、<b>クエスト段階が空</b>として読む。ほかの欄は同じに読める。
        /// 版 1 に questStages があれば未知の欄として拒否する（上の検査）。
        /// </summary>
        [Test]
        public void Codec_ReadsSchemaVersion1_AsNoQuestStages()
        {
            GameSessionState session = BuildRichSession();
            SaveSnapshot snap = SaveSnapshot.Capture(session, _catalog.Campaign, RichParty());
            string v2 = SaveJsonCodec.Serialize(snap, 4, DateTime.UnixEpoch);
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(v2, out SaveEnvelopeInfo info, out _, out string error), error);

            // 版 1 の形を作る：payload から questStages を外し、checksum を採り直す。
            string compact = Minify(v2);
            int at = compact.IndexOf("\"payload\":", StringComparison.Ordinal);
            Assert.Greater(at, 0);
            string payload = compact.Substring(at + "\"payload\":".Length, compact.Length - at - "\"payload\":".Length - 1);
            int quests = payload.IndexOf(",\"questStages\":", StringComparison.Ordinal);
            Assert.Greater(quests, 0, "前提：版 2 は questStages を持つ。");
            string payloadV1 = payload.Substring(0, quests) + "}";
            string v1 = "{\"schemaVersion\":1,\"contentVersion\":" + info.ContentVersion
                + ",\"campaignId\":\"" + info.CampaignId + "\",\"adventureId\":\"" + info.AdventureId
                + "\",\"generation\":4,\"savedAtUtc\":\"" + info.SavedAtUtc + "\",\"checksum\":\""
                + SaveJsonCodec.Sha256Hex(payloadV1) + "\",\"payload\":" + payloadV1 + "}";

            Assert.IsTrue(SaveJsonCodec.TryDeserialize(v1, out SaveEnvelopeInfo v1Info, out SaveSnapshot read, out error), error);
            Assert.AreEqual(1, v1Info.SchemaVersion);
            Assert.AreEqual(0, read.QuestStages.Count, "版 1 はクエスト段階が空。");
            Assert.AreEqual(snap.TotalVirtue, read.TotalVirtue, "ほかの欄は同じに読める。");
            Assert.AreEqual(snap.Party.Player.Hp, read.Party.Player.Hp);
            Assert.IsTrue(SessionRestorer.TryBuildCandidate(read, _catalog, out GameSessionState candidate, out error), error);
            Assert.AreEqual(0, candidate.QuestStageOf(QuestFixture));
        }

        /// <summary>空白を（文字列の外だけ）取り除く。Newtonsoft の Formatting.None と同じ形になる（保存の値は空白を含まない）。</summary>
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

        private static void AssertRejected(string json, string label)
        {
            Assert.IsFalse(SaveJsonCodec.TryDeserialize(json, out _, out _, out string error), label + " を受け付けた。");
            Assert.IsFalse(string.IsNullOrEmpty(error), label + " の理由が空。");
        }

        // ================================================================ 検証（P6A 20）

        [Test]
        public void Validator_RejectsUnknownIdsRangesAndContradictions()
        {
            SaveSnapshot good = SaveSnapshot.Capture(BuildRichSession(), _catalog.Campaign, RichParty());
            var errors = new List<string>();
            Assert.IsTrue(SaveSnapshotValidator.Validate(good, _catalog, errors), string.Join("\n", errors));

            AssertInvalid(With(good, areas: new[] { Area("area_p6_unknown") }), "未知エリア");
            AssertInvalid(With(good, areas: new[] { Area(AreaB.Value, cleared: "encounter_p6_unknown") }), "未知の遭遇戦");
            AssertInvalid(With(good, areas: new[] { Area(AreaA.Value, fieldCycle: 99) }), "周期の範囲外");
            AssertInvalid(With(good, inventory: new[] { Pair("item_p6_unknown", 1) }), "未知アイテム");
            AssertInvalid(With(good, inventory: new[] { Pair(Tonic.Value, 99) }), "上限超過");
            AssertInvalid(With(good, inventory: new[] { Pair(Tonic.Value, -1) }), "負数");
            AssertInvalid(With(good, growth: new[] { Pair("growth_p6_unknown", 20) }), "未知の成長");
            AssertInvalid(With(good, growth: new[] { Pair(GrowthHp2.Value, 20) }), "前提未取得");
            AssertInvalid(With(good, spent: 5), "使用済みと実支出の不一致");
            AssertInvalid(With(good, checkpoint: "shrine_p6_unknown"), "未知の再開地点");
            AssertInvalid(With(good, registered: new[] { ShrineA.Value }), "再開地点が未登録");
            AssertInvalid(With(good, resumeKind: ResumeAnchorKind.Entry, resumeArea: AreaB.Value, resumePoint: "entry_p6_none"), "無効な復帰点");
            AssertInvalid(With(good, resumeKind: ResumeAnchorKind.Shrine, resumeArea: AreaA.Value, resumePoint: ShrineB.Value), "所有 Area 不一致");
            AssertInvalid(With(good, playerHp: 0), "HP 0 の通常保存");
            AssertInvalid(With(good, stamina: float.NaN), "NaN");
            AssertInvalid(With(good, campaignId: "campaign_other"), "別 campaign");
            AssertInvalid(With(good, contentVersion: 2), "内容版違い");
            AssertInvalid(With(good, kibidango: 9), "きびだんご上限超過");
            AssertInvalid(With(good, visited: new[] { AreaA.Value, AreaA.Value }), "重複");
            AssertInvalid(With(good, quests: new[] { Pair("quest_p6_unknown", 1) }), "未知のクエスト");
            AssertInvalid(With(good, quests: new[] { Pair(QuestFixture.Value, -1) }), "クエスト段階が負");
        }

        /// <summary>
        /// P6A 20（レビュー R4）：付与済み報酬と加入済み仲間も campaign の既知 ID で解決する。
        /// <b>書式が正しい未知 ID</b>（別 campaign・改変・旧版の名残）を拒否し、既知の ID は通す。
        /// </summary>
        [Test]
        public void Validator_RejectsWellFormedUnknownRewardAndCompanionIds()
        {
            SaveSnapshot good = SaveSnapshot.Capture(BuildRichSession(), _catalog.Campaign, RichParty());
            Assert.Greater(good.GrantedRewards.Count, 0, "前提：付与済み報酬が保存に載っている。");
            Assert.Greater(good.Recruited.Count, 0, "前提：加入済み仲間が保存に載っている。");
            var errors = new List<string>();
            Assert.IsTrue(SaveSnapshotValidator.Validate(good, _catalog, errors), string.Join("\n", errors));

            var granted = new List<string>(good.GrantedRewards) { "reward_p6_unknown" };
            AssertInvalid(With(good, granted: granted.ToArray()), "未知の付与済み報酬");
            AssertInvalid(With(good, granted: new[] { "reward_p6_arrive_b", "reward_p6_arrive_b" }), "付与済み報酬の重複");
            AssertInvalid(With(good, recruited: new[] { "companion_saru_unknown" }), "未知の仲間");
            Assert.IsTrue(SaveSnapshotValidator.Validate(With(good, recruited: new string[0]), _catalog, errors),
                "仲間が居ない保存は正しい。");

            errors.Clear();
            Assert.IsFalse(SaveSnapshotValidator.Validate(With(good, granted: granted.ToArray()), _catalog, errors));
            StringAssert.Contains("reward_p6_unknown", string.Join("\n", errors), "理由に未知 ID を示す。");
        }

        private void AssertInvalid(SaveSnapshot snapshot, string label)
        {
            var errors = new List<string>();
            Assert.IsFalse(SaveSnapshotValidator.Validate(snapshot, _catalog, errors), label + " を受け付けた。");
            Assert.IsFalse(SessionRestorer.TryBuildCandidate(snapshot, _catalog, out GameSessionState candidate, out _),
                label + " から候補を作った。");
            Assert.IsNull(candidate);
        }

        // ================================================================ 二世代ストレージ（P6A 19）

        [Test]
        public void Store_AlternatesSides_ChoosesNewest_RecoversFromOneCorruptSide()
        {
            var store = new SaveFileStore(_dir);
            string json1 = Write(store, 1);
            string json2 = Write(store, 2);
            Write(store, 3);

            Assert.IsTrue(File.Exists(store.PathA));
            Assert.IsTrue(File.Exists(store.PathB));
            SaveLoadDecision decision = store.DecideLoad();
            Assert.AreEqual(SaveLoadVerdict.Ok, decision.Verdict);
            Assert.AreEqual(3, decision.Chosen.Info.Generation);

            // 最新の側（A：1→A, 2→B, 3→A）を壊す → B（世代 2）から復旧して通知。
            File.WriteAllText(store.PathA, "{ broken");
            decision = store.DecideLoad();
            Assert.AreEqual(SaveLoadVerdict.RecoveredFromOtherSide, decision.Verdict);
            Assert.AreEqual(2, decision.Chosen.Info.Generation);
            Assert.AreEqual(json2, decision.Chosen.Json);

            // 両方壊す → Load 不可。元ファイルは残す。
            File.WriteAllText(store.PathB, "garbage");
            decision = store.DecideLoad();
            Assert.AreEqual(SaveLoadVerdict.BothCorrupt, decision.Verdict);
            Assert.IsFalse(decision.CanLoad);
            Assert.AreEqual("garbage", File.ReadAllText(store.PathB), "壊れたファイルも消さない。");
            Assert.IsNotNull(json1);
        }

        /// <summary>一時ファイルは読込候補にしない。</summary>
        [Test]
        public void Store_TempFileIsNeverACandidate()
        {
            var store = new SaveFileStore(_dir);
            Write(store, 1);
            File.WriteAllText(store.PathB + ".tmp", SerializeRich(99));

            SaveLoadDecision decision = store.DecideLoad();
            Assert.AreEqual(1, decision.Chosen.Info.Generation, "書きかけの .tmp を選ばない。");
        }

        [Test]
        public void Store_RefusesMixedAdventuresAndSameGenerationConflict()
        {
            var store = new SaveFileStore(_dir);
            File.WriteAllText(store.PathA, SerializeRich(4));
            GameSessionState other = BuildRichSession("adventure_other");
            File.WriteAllText(store.PathB, SaveJsonCodec.Serialize(
                SaveSnapshot.Capture(other, _catalog.Campaign, RichParty()), 5, DateTime.UnixEpoch));
            Assert.AreEqual(SaveLoadVerdict.MixedAdventures, store.DecideLoad().Verdict);

            GameSessionState sameAdventure = BuildRichSession();
            sameAdventure.Inventory.TryAdd(Tonic, 1, 3);
            File.WriteAllText(store.PathB, SaveJsonCodec.Serialize(
                SaveSnapshot.Capture(sameAdventure, _catalog.Campaign, RichParty()), 4, DateTime.UnixEpoch));
            Assert.AreEqual(SaveLoadVerdict.GenerationConflict, store.DecideLoad().Verdict);
        }

        /// <summary>書込・flush・検証・置換・再検証のどこで落ちても、前の有効な世代が残る（P6A 19）。</summary>
        [TestCase(FaultyFileSystem.Stage.Write)]
        [TestCase(FaultyFileSystem.Stage.CorruptOnWrite)]
        [TestCase(FaultyFileSystem.Stage.Replace)]
        [TestCase(FaultyFileSystem.Stage.CorruptAfterReplace)]
        public void Store_FaultAtAnyStage_KeepsPreviousValidGeneration(FaultyFileSystem.Stage stage)
        {
            var fs = new FaultyFileSystem();
            var store = new SaveFileStore(_dir, "slot0", fs);
            Write(store, 1);
            Write(store, 2);

            fs.FailAt = stage;
            long generation = store.NextGeneration(out string target);
            Assert.AreEqual(3, generation);
            bool ok = store.TryWrite(SerializeRich(generation), generation, target, out string error);

            Assert.IsFalse(ok, "故障を成功として返した。");
            Assert.IsFalse(string.IsNullOrEmpty(error));
            fs.FailAt = FaultyFileSystem.Stage.None;
            SaveLoadDecision decision = store.DecideLoad();
            Assert.IsTrue(decision.CanLoad, "前の有効な世代が読めること：" + decision.Verdict + " " + decision.Detail);
            Assert.AreEqual(2, decision.Chosen.Info.Generation, "最新の有効な側（世代 2）は無傷。");
        }

        /// <summary>同じ保存先を 2 つの書き手が同時に掴めない（複数プロセス競合。P6A 21）。</summary>
        [Test]
        public void Store_SecondWriterCannotTakeTheLock()
        {
            var first = new SaveFileStore(_dir);
            var second = new SaveFileStore(_dir);
            try
            {
                Assert.IsTrue(first.TryAcquireLock(out string e1), e1);
                Assert.IsFalse(second.TryAcquireLock(out string e2), "2 つ目が掴めてしまった。");
                Assert.IsFalse(string.IsNullOrEmpty(e2));
                Assert.IsFalse(second.TryWrite(SerializeRich(1), 1, second.PathA, out _), "ロック無しで書けてしまった。");
            }
            finally
            {
                first.ReleaseLock();
                second.ReleaseLock();
            }
        }

        /// <summary>New Game は前の冒険を退避してから。退避に失敗したら上書きしない（P6A 21）。</summary>
        [Test]
        public void Store_ArchiveBeforeNewGame()
        {
            var fs = new FaultyFileSystem();
            var store = new SaveFileStore(_dir, "slot0", fs);
            Write(store, 1);
            Write(store, 2);

            fs.FailAt = FaultyFileSystem.Stage.Write;
            Assert.IsFalse(store.TryArchiveCurrent("20261001_000000", out _, out string error), "退避の失敗を成功にした。");
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Assert.IsTrue(store.DecideLoad().CanLoad, "退避に失敗したら元のまま。");

            fs.FailAt = FaultyFileSystem.Stage.None;
            string a = File.ReadAllText(store.PathA);
            string b = File.ReadAllText(store.PathB);
            Assert.IsTrue(store.TryArchiveCurrent("20261001_000001", out string archived, out error), error);
            Assert.AreEqual(SaveLoadVerdict.NoSave, store.DecideLoad().Verdict, "スロットは空になる。");
            Assert.AreEqual(a, File.ReadAllText(Path.Combine(archived, "slot0_a.json")), "写しは元と同じ。");
            Assert.AreEqual(b, File.ReadAllText(Path.Combine(archived, "slot0_b.json")), "写しは元と同じ。");
        }

        /// <summary>
        /// P6A 21（レビュー R2）：退避の<b>途中</b>で失敗しても、スロットには「読める旧冒険（最新の世代）」が残り、New Game は始まらない。
        /// 再起動（新しい Store）しても同じ旧冒険を最新の世代で読める。2 つ目の写し・2 つ目の削除・削除後の戻しの失敗を注入する。
        /// </summary>
        [Test]
        public void Store_ArchivePartialFailure_KeepsLatestLoadable_AcrossRestart()
        {
            var fs = new FaultyFileSystem();
            var store = new SaveFileStore(_dir, "slot0", fs);
            Write(store, 1);
            Write(store, 2);
            SaveLoadDecision before = store.DecideLoad();
            Assert.AreEqual(2, before.Chosen.Info.Generation, "前提：最新は世代 2。");
            string latestPath = store.ScanSide(store.PathA).Info.Generation == 2 ? store.PathA : store.PathB;
            string olderPath = latestPath == store.PathA ? store.PathB : store.PathA;
            string latestText = File.ReadAllText(latestPath);
            string olderText = File.ReadAllText(olderPath);

            // (1) 2 つ目の写しで失敗：スロットは無傷、書きかけの写しは残さない。
            int archiveWrites = 0;
            fs.FailWriteIf = path => path.Contains("archive") && ++archiveWrites == 2;
            Assert.IsFalse(store.TryArchiveCurrent("s1", out string archived, out string error), "写しの失敗を成功にした。");
            StringAssert.Contains("スロットはそのまま", error);
            Assert.IsNull(archived);
            AssertSlotIntact(store, latestText, olderText, "写しの失敗");
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "archive", "s1", "slot0_a.json"))
                           || File.Exists(Path.Combine(_dir, "archive", "s1", "slot0_b.json")), "書きかけの写しを残さない。");
            fs.FailWriteIf = null;

            // (2) 2 つ目の削除（最新）で失敗：古い側は写しから戻り、二世代のまま読める。
            fs.FailDeleteIf = path => path == latestPath;
            Assert.IsFalse(store.TryArchiveCurrent("s2", out archived, out error), "削除の失敗を成功にした。");
            StringAssert.Contains("New Game は始めません", error);
            AssertSlotIntact(store, latestText, olderText, "削除の失敗");
            AssertSlotIntact(new SaveFileStore(_dir, "slot0"), latestText, olderText, "削除の失敗→再起動");
            Assert.AreEqual(latestText, File.ReadAllText(Path.Combine(_dir, "archive", "s2", Path.GetFileName(latestPath))),
                "写しは揃っている。");

            // (3) 最新を消せず、古い側も戻せない：最新の片側だけが残り、それを読む（旧冒険の古い世代へ黙って戻らない）。
            fs.FailWriteIf = path => path == olderPath;
            Assert.IsFalse(store.TryArchiveCurrent("s3", out archived, out error));
            StringAssert.Contains("最新の側は残っています", error);
            var restarted = new SaveFileStore(_dir, "slot0");
            SaveLoadDecision afterRestart = restarted.DecideLoad();
            Assert.IsTrue(afterRestart.CanLoad, "再起動後も旧冒険を読める: " + afterRestart.Verdict + " " + afterRestart.Detail);
            Assert.AreEqual(2, afterRestart.Chosen.Info.Generation, "最新の世代を読む。");
            Assert.AreEqual(latestText, File.ReadAllText(latestPath));
            fs.FailWriteIf = null;
            fs.FailDeleteIf = null;

            // (4) 1 つ目の削除（古い側）で失敗：何も変わらない。
            File.WriteAllText(olderPath, olderText);
            fs.FailDeleteIf = path => path == olderPath;
            Assert.IsFalse(store.TryArchiveCurrent("s4", out _, out _));
            AssertSlotIntact(store, latestText, olderText, "1 つ目の削除の失敗");
            fs.FailDeleteIf = null;

            // 故障が直れば退避できる。
            Assert.IsTrue(store.TryArchiveCurrent("s5", out archived, out error), error);
            Assert.AreEqual(SaveLoadVerdict.NoSave, store.DecideLoad().Verdict);
        }

        private static void AssertSlotIntact(SaveFileStore store, string latestText, string olderText, string label)
        {
            SaveLoadDecision d = store.DecideLoad();
            Assert.AreEqual(SaveLoadVerdict.Ok, d.Verdict, label + "：旧冒険を通常どおり読める（" + d.Detail + "）。");
            Assert.AreEqual(2, d.Chosen.Info.Generation, label + "：最新の世代を読む。");
            var texts = new[] { File.ReadAllText(store.PathA), File.ReadAllText(store.PathB) };
            CollectionAssert.AreEquivalent(new[] { latestText, olderText }, texts, label + "：両側とも元のまま。");
        }

        // ================================================================ 書込の調停（P6A 18／19）

        /// <summary>
        /// 書込中の新しい要求は捨てずに後続保存へ。古い版の完了で dirty を消さない。最後は最新の版まで保存される。
        /// </summary>
        [Test]
        public void Coordinator_RequestDuringWrite_IsSavedAfterwards_OldCompletionKeepsDirty()
        {
            var exec = new ManualSaveExecutor();
            var coordinator = new SaveCoordinator(new SaveFileStore(_dir), exec)
            {
                ActorSource = () => new FakeActors(),
            };
            GameSessionState session = BuildRichSession();
            coordinator.Bind(session, _catalog.Campaign, -1);

            session.Changes.RequestAutosave("first");
            coordinator.Pump();
            Assert.AreEqual(SaveStatus.Writing, coordinator.Status);
            long firstRevision = coordinator.InFlightRevision;

            // 書込中に進行が進み、保存が要求される。
            session.Progress.TryGrant(new RewardSnapshot(new StableId("reward_enemy_melee"), 1, default, false), out _);
            long latest = session.Changes.Revision;
            Assert.Greater(latest, firstRevision);
            coordinator.Pump();
            Assert.AreEqual(1, coordinator.SubmitCount, "書込中は重ねて出さない。");

            exec.RunPending();
            coordinator.Pump(); // 古い版の完了を取り込み、保留を最新で出し直す。
            Assert.AreEqual(firstRevision, coordinator.SavedRevision, "保存済みは書けた版まで。");
            Assert.IsTrue(coordinator.IsDirty, "古い版の完了で新しい dirty を消さない。");
            Assert.AreEqual(2, coordinator.SubmitCount, "保留していた要求を最新で出し直す。");

            exec.RunPending();
            coordinator.Pump();
            Assert.AreEqual(latest, coordinator.SavedRevision);
            Assert.IsFalse(coordinator.IsDirty);
            Assert.AreEqual(SaveStatus.Saved, coordinator.Status);

            // ファイルの中身も最新。
            SaveLoadDecision decision = coordinator.Store.DecideLoad();
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot onDisk, out string e), e);
            Assert.AreEqual(latest, onDisk.Revision);
            coordinator.Dispose();
        }

        /// <summary>前の Session（New Game／Load 前）の書込完了は、新しいゲームへ適用しない。</summary>
        [Test]
        public void Coordinator_CompletionFromOldSessionIsIgnored()
        {
            var exec = new ManualSaveExecutor();
            var coordinator = new SaveCoordinator(new SaveFileStore(_dir), exec) { ActorSource = () => new FakeActors() };
            GameSessionState old = BuildRichSession();
            coordinator.Bind(old, _catalog.Campaign, -1);
            old.Changes.RequestAutosave("old");
            coordinator.Pump();

            GameSessionState fresh = BuildRichSession("adventure_fresh");
            coordinator.Bind(fresh, _catalog.Campaign, -1);
            exec.RunPending();
            coordinator.Pump();

            Assert.AreEqual(1, coordinator.StaleCompletionCount);
            Assert.AreEqual(-1, coordinator.SavedRevision, "古い Session の完了で新しいゲームを保存済みにしない。");
            coordinator.Dispose();
        }

        /// <summary>
        /// 失敗は巻き戻さず、dirty を残し、前の有効ファイルを残し、毎フレーム再試行しない。
        /// 再試行は最新から採り直す（古い失敗 Snapshot で上書きしない）。
        /// </summary>
        [Test]
        public void Coordinator_FailureKeepsDirty_NoAutoRetry_RetryUsesLatest()
        {
            var fs = new FaultyFileSystem();
            var exec = new ManualSaveExecutor();
            var coordinator = new SaveCoordinator(new SaveFileStore(_dir, "slot0", fs), exec)
            {
                ActorSource = () => new FakeActors(),
            };
            GameSessionState session = BuildRichSession();
            coordinator.Bind(session, _catalog.Campaign, -1);

            session.Changes.RequestAutosave("ok");
            coordinator.Pump();
            exec.RunPending();
            coordinator.Pump();
            long savedOk = coordinator.SavedRevision;
            int virtueBefore = session.Progress.Virtue;

            fs.FailAt = FaultyFileSystem.Stage.Write;
            session.Progress.TryGrant(new RewardSnapshot(new StableId("reward_enemy_melee"), 5, default, false), out _);
            coordinator.Pump();
            exec.RunPending();
            coordinator.Pump();
            Assert.AreEqual(SaveStatus.Failed, coordinator.Status);
            Assert.IsFalse(string.IsNullOrEmpty(coordinator.LastError));
            Assert.AreEqual(savedOk, coordinator.SavedRevision);
            Assert.IsTrue(coordinator.IsDirty, "保存できていない。");
            Assert.AreEqual(virtueBefore + 5, session.Progress.Virtue, "Runtime の獲得は巻き戻さない。");

            int submits = coordinator.SubmitCount;
            for (int i = 0; i < 10; i++)
            {
                coordinator.Pump();
            }

            Assert.AreEqual(submits, coordinator.SubmitCount, "毎フレームの再試行をしない。");

            fs.FailAt = FaultyFileSystem.Stage.None;
            session.Inventory.TryAdd(Tonic, 1, 3);
            long latest = session.Changes.Revision;
            coordinator.RetryNow();
            coordinator.Pump();
            exec.RunPending();
            coordinator.Pump();
            Assert.AreEqual(SaveStatus.Saved, coordinator.Status);
            Assert.AreEqual(latest, coordinator.SavedRevision, "再試行は最新の Runtime から。");
            coordinator.Dispose();
        }

        /// <summary>
        /// P6A 19／22：版の変わらない保存（終了前など）が<b>書き終わったが完了をまだ取り込んでいない</b>間は、
        /// 書込中・未保存として見える。ここで「保存済み」に見えると、失敗した終了前の保存が成功扱いになる
        /// （実 Scene の終了テストで発覚）。
        /// </summary>
        [Test]
        public void Coordinator_UntakenCompletion_IsStillWriting()
        {
            var fs = new FaultyFileSystem();
            var exec = new ManualSaveExecutor();
            var coordinator = new SaveCoordinator(new SaveFileStore(_dir, "slot0", fs), exec)
            {
                ActorSource = () => new FakeActors(),
            };
            GameSessionState session = BuildRichSession();
            coordinator.Bind(session, _catalog.Campaign, -1);
            session.Changes.RequestAutosave("first");
            coordinator.Pump();
            exec.RunPending();
            coordinator.Pump();
            Assert.AreEqual(SaveStatus.Saved, coordinator.Status, "前提：保存済み。");

            fs.FailAt = FaultyFileSystem.Stage.Write;
            session.Changes.RequestAutosave("exit"); // 版は進まない。
            coordinator.Pump();
            exec.RunPending(); // 書込担当は失敗で手を離した。結果はまだ取り込んでいない。
            Assert.IsFalse(exec.IsBusy, "前提：書込担当は空いている。");
            Assert.IsTrue(coordinator.IsWriting, "完了を取り込むまでは書込中。");
            Assert.IsTrue(coordinator.IsDirty, "完了を取り込むまでは保存済みと言わない。");

            coordinator.Pump();
            Assert.AreEqual(SaveStatus.Failed, coordinator.Status, "取り込めば失敗が見える。");
            Assert.IsFalse(coordinator.IsWriting);
            coordinator.Dispose();
        }

        /// <summary>
        /// P6A 19／22（レビュー R1）：<b>版の変わらない保存が失敗したら、版の差が無くても未保存のまま</b>。
        /// HP など版を進めない変化は失敗した書込にしか乗っていない。次の成功でだけ消える。
        /// </summary>
        [Test]
        public void Coordinator_FailedSaveWithUnchangedRevision_StaysDirtyUntilNextSuccess()
        {
            var fs = new FaultyFileSystem();
            var exec = new ManualSaveExecutor();
            var coordinator = new SaveCoordinator(new SaveFileStore(_dir, "slot0", fs), exec)
            {
                ActorSource = () => new FakeActors(),
            };
            GameSessionState session = BuildRichSession();
            coordinator.Bind(session, _catalog.Campaign, -1);
            session.Changes.RequestAutosave("first");
            coordinator.Pump();
            exec.RunPending();
            coordinator.Pump();
            Assert.IsFalse(coordinator.IsDirty, "前提：保存済み。");

            fs.FailAt = FaultyFileSystem.Stage.Write;
            long revision = session.Changes.Revision;
            session.Changes.RequestAutosave("exit"); // 版は進まない（HP だけの変化を拾う終了前の保存）。
            coordinator.Pump();
            exec.RunPending();
            coordinator.Pump();
            Assert.AreEqual(SaveStatus.Failed, coordinator.Status, "前提：失敗した。");
            Assert.AreEqual(revision, session.Changes.Revision, "前提：版は進んでいない。");
            Assert.AreEqual(revision, coordinator.SavedRevision, "前提：版の差は無い。");
            Assert.IsTrue(coordinator.IsDirty, "版の差が無くても、失敗した保存のあとは未保存。");
            Assert.IsTrue(coordinator.HasUnsavedFailure);
            for (int i = 0; i < 5; i++)
            {
                coordinator.Pump();
            }

            Assert.IsTrue(coordinator.IsDirty, "時間が経っても勝手に保存済みへ戻らない。");

            fs.FailAt = FaultyFileSystem.Stage.None;
            coordinator.RetryNow();
            coordinator.Pump();
            exec.RunPending();
            coordinator.Pump();
            Assert.AreEqual(SaveStatus.Saved, coordinator.Status);
            Assert.IsFalse(coordinator.IsDirty, "次の成功で保存済みに戻る。");
            Assert.IsFalse(coordinator.HasUnsavedFailure);

            // 冒険を結び直したら（New Game／Load）前の失敗は持ち越さない。
            fs.FailAt = FaultyFileSystem.Stage.Write;
            session.Changes.RequestAutosave("again");
            coordinator.Pump();
            exec.RunPending();
            coordinator.Pump();
            Assert.IsTrue(coordinator.HasUnsavedFailure, "前提：失敗した。");
            coordinator.Bind(session, _catalog.Campaign, session.Changes.Revision);
            Assert.IsFalse(coordinator.HasUnsavedFailure, "結び直しで失敗の印は消える（新しい Session の基準から数える）。");
            coordinator.Dispose();
        }

        /// <summary>
        /// P6A 19：一時的な共有違反（実ビルドで観測した「置換されるファイルを削除できません」）は書込担当の中で数回やり直す。
        /// 尽きたら失敗として返し（呼び出し側で前の世代が残る）、I/O 以外の例外はやり直さない。
        /// </summary>
        [Test]
        public void TransientIo_RetriesSharingViolations_ThenGivesUp()
        {
            var waits = new List<int>();
            int calls = 0;
            int attempts = TransientIo.Retry(() =>
            {
                calls++;
                if (calls < 3)
                {
                    throw new IOException("sharing violation");
                }
            }, sleep: waits.Add);
            Assert.AreEqual(3, attempts, "3 回目で成功する。");
            CollectionAssert.AreEqual(new[] { 20, 40 }, waits, "待ちは倍々。");

            calls = 0;
            Assert.Throws<UnauthorizedAccessException>(() => TransientIo.Retry(() =>
            {
                calls++;
                throw new UnauthorizedAccessException("locked");
            }, sleep: _ => { }));
            Assert.AreEqual(TransientIo.DefaultAttempts, calls, "尽きるまで試して、最後の失敗を返す。");

            calls = 0;
            Assert.Throws<InvalidOperationException>(() => TransientIo.Retry(() =>
            {
                calls++;
                throw new InvalidOperationException("bug");
            }, sleep: _ => { }));
            Assert.AreEqual(1, calls, "I/O 以外はやり直さない。");
        }

        /// <summary>遷移中・死亡中（採取不可）・主人公不在は採らずに待つ。</summary>
        [Test]
        public void Coordinator_DefersCaptureWhenUnsettled()
        {
            var exec = new ManualSaveExecutor();
            bool canCapture = false;
            var actors = new FakeActors { CanExport = false };
            var coordinator = new SaveCoordinator(new SaveFileStore(_dir), exec)
            {
                CanCapture = () => canCapture,
                ActorSource = () => actors,
            };
            GameSessionState session = BuildRichSession();
            coordinator.Bind(session, _catalog.Campaign, -1);
            session.Changes.RequestAutosave("x");

            coordinator.Pump();
            Assert.AreEqual(0, coordinator.SubmitCount, "遷移中は採らない。");
            canCapture = true;
            coordinator.Pump();
            Assert.AreEqual(0, coordinator.SubmitCount, "主人公が死亡中・不在なら採らない。");
            actors.CanExport = true;
            coordinator.Pump();
            Assert.AreEqual(1, coordinator.SubmitCount, "落ち着いたら採る。");
            Assert.AreEqual(2, coordinator.DeferredCaptureCount);
            coordinator.Dispose();
        }

        // ================================================================ 道具

        private string Write(SaveFileStore store, long expected)
        {
            long generation = store.NextGeneration(out string target);
            Assert.AreEqual(expected, generation);
            string json = SerializeRich(generation);
            Assert.IsTrue(store.TryWrite(json, generation, target, out string error), error);
            return json;
        }

        private string SerializeRich(long generation)
        {
            return SaveJsonCodec.Serialize(
                SaveSnapshot.Capture(BuildRichSession(), _catalog.Campaign, RichParty()), generation, DateTime.UnixEpoch);
        }

        private ShrineInfo Shrine(StableId id)
        {
            Assert.IsTrue(_catalog.Campaign.TryGetShrine(id, out ShrineInfo shrine));
            return shrine;
        }

        private GameSessionState BuildRichSession(string adventureId = "adventure_p6a_test")
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            Assert.IsTrue(session.InitializeNewAdventure(adventureId, Shrine(ShrineA), 3));
            session.Progress.TryGrant(new RewardSnapshot(new StableId("reward_enemy_melee"), 50, default, false), out _);
            Assert.AreEqual(GrowthPurchaseResult.Purchased, session.Progress.TryPurchaseGrowth(GrowthHp, 20));
            session.CommitArrival(AreaA, RewardSnapshot.None);
            session.CommitArrival(AreaB, new RewardSnapshot(new StableId("reward_p6_arrive_b"), 10, default, true));
            session.Recruit(new StableId("companion_inumaru"));
            session.AdvanceRespawnCycle();
            session.AdvanceRespawnCycle();
            session.TryRecordFieldDefeat(AreaA, Field, new RewardSnapshot(new StableId("reward_enemy_melee"), 1, default, false), out _);
            session.CommitEncounterClear(AreaB, Encounter, new RewardSnapshot(new StableId("reward_p6_clear_b1"), 30, default, true), Flag);
            session.TryRecordBossDefeat(AreaB, Boss, RewardSnapshot.None, out _);
            session.TryPickPlacement(AreaB, Pickup, Tonic, 1, 3, RewardSnapshot.None);
            session.GetOrCreateArea(AreaB).Investigation.TryMarkInvestigated(Investigation);
            session.RegisterShrine(Shrine(ShrineB));
            session.SetResumeAnchor(ResumeAnchor.AtEntry(AreaB, EntryBWest));
            session.TryConsumeKibidango(2);
            session.TrySetQuestStage(QuestFixture, 2);
            return session;
        }

        private static PartySaveValues RichParty()
        {
            return new PartySaveValues(
                new PlayerSaveValues(37, 42.5f, 0.25f, 0.125f),
                true,
                new CompanionSaveValues(new StableId("companion_inumaru"), 0, true, 3.5f, 0.5f, 1.25f, 2.5f, 0.75f, 4.0f));
        }

        private AreaCatalog BuildCatalog()
        {
            AreaDefinition a = NewArea(AreaA, EntryAStart, EntryAShrine,
                content: Manifest(fields: new[] { Field }));
            AreaDefinition b = NewArea(AreaB, EntryBWest, EntryBShrine,
                content: Manifest(encounters: new[] { Encounter, Boss }, bosses: new[] { Boss },
                    pickups: new[] { Pickup }, flags: new[] { Flag }, investigations: new[] { Investigation }));

            var hp = ScriptableObject.CreateInstance<SkillNodeData>();
            _spawned.Add(hp);
            SetId(hp, GrowthHp);
            hp.EditorSet(20, 0, 10);
            var hp2 = ScriptableObject.CreateInstance<SkillNodeData>();
            _spawned.Add(hp2);
            SetId(hp2, GrowthHp2);
            hp2.EditorSet(20, 1, 10);
            SetPrivate(hp2, "_prerequisites", new List<SkillNodeData> { hp });

            var shrineA = new ShrineDefinition();
            shrineA.EditorSet(ShrineA, AreaA, EntryAShrine, "A のお地蔵様");
            var shrineB = new ShrineDefinition();
            shrineB.EditorSet(ShrineB, AreaB, EntryBShrine, "B のお地蔵様");
            var tonic = new ItemDefinition();
            tonic.EditorSet(Tonic, 3, "強壮薬");

            var catalog = ScriptableObject.CreateInstance<AreaCatalogData>();
            _spawned.Add(catalog);
            SetId(catalog, CampaignId);
            catalog.EditorSet(new List<AreaDefinition> { a, b }, AreaA, EntryAStart);
            catalog.EditorSetCampaign(EncounterClearPolicy.Permanent, 1,
                new List<ShrineDefinition> { shrineA, shrineB }, ShrineA, 3,
                new List<ItemDefinition> { tonic }, new List<SkillNodeData> { hp, hp2 });
            catalog.EditorSetKnownIds(
                new List<StableId> { new StableId("reward_p6_arrive_b"), new StableId("reward_p6_clear_b1") },
                new List<StableId> { new StableId("companion_inumaru") },
                new List<StableId> { QuestFixture });

            Assert.IsTrue(AreaCatalog.TryBuild(catalog, out AreaCatalog built, out IReadOnlyList<string> errors),
                string.Join("\n", errors));
            Assert.IsNotNull(built.Campaign);
            return built;
        }

        private AreaDefinition NewArea(StableId id, StableId entry1, StableId entry2, AreaContentManifest content)
        {
            var area = ScriptableObject.CreateInstance<AreaDefinition>();
            _spawned.Add(area);
            SetId(area, id);
            var e1 = new AreaEntryDefinition();
            e1.EditorSet(entry1, CardinalDirection.North);
            var e2 = new AreaEntryDefinition();
            e2.EditorSet(entry2, CardinalDirection.South);
            area.EditorSet("Assets/_Project/Scenes/Tests/Phase6/" + id.Value + ".unity", 0,
                new List<AreaEntryDefinition> { e1, e2 }, entry1);
            area.EditorSetContent(content);
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

        private static void SetId(Object asset, StableId id) => SetPrivate(asset, "_id", id);

        private static void SetPrivate(object target, string field, object value)
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

        private static KeyValuePair<string, int> Pair(string key, int value) => new KeyValuePair<string, int>(key, value);

        private static AreaSaveRecord Area(string areaId, string cleared = null, int fieldCycle = -1)
        {
            return new AreaSaveRecord(areaId, null, null,
                cleared != null ? new[] { cleared } : null, null, null,
                fieldCycle >= 0 ? new[] { Pair(Field.Value, fieldCycle) } : null);
        }

        /// <summary>1 箇所だけ差し替えた Snapshot を作る。</summary>
        private static SaveSnapshot With(SaveSnapshot s,
            AreaSaveRecord[] areas = null, KeyValuePair<string, int>[] inventory = null,
            KeyValuePair<string, int>[] growth = null, int? spent = null, string checkpoint = null,
            string[] registered = null, ResumeAnchorKind? resumeKind = null, string resumeArea = null,
            string resumePoint = null, int? playerHp = null, float? stamina = null, string campaignId = null,
            int? contentVersion = null, int? kibidango = null, string[] visited = null,
            string[] granted = null, string[] recruited = null, KeyValuePair<string, int>[] quests = null)
        {
            PlayerSaveValues p = s.Party.Player;
            var player = new PlayerSaveValues(playerHp ?? p.Hp, stamina ?? p.Stamina, p.StaminaRegenDelay, p.InvincibleRemaining);
            return new SaveSnapshot(
                campaignId ?? s.CampaignId, contentVersion ?? s.ContentVersion, s.AdventureId, s.Revision, s.RespawnCycle,
                s.TotalVirtue, spent ?? s.SpentVirtue, granted ?? ToArray(s.GrantedRewards),
                growth ?? ToArray(s.Growth), visited ?? ToArray(s.VisitedAreas), recruited ?? ToArray(s.Recruited),
                areas ?? ToArray(s.Areas), inventory ?? ToArray(s.Inventory), kibidango ?? s.Kibidango,
                registered ?? ToArray(s.RegisteredShrines), checkpoint ?? s.Checkpoint,
                resumeKind ?? s.ResumeKind, resumeArea ?? s.ResumeAreaId, resumePoint ?? s.ResumePointId,
                new PartySaveValues(player, s.Party.HasCompanion, s.Party.Companion), quests ?? ToArray(s.QuestStages));
        }

        private static T[] ToArray<T>(IReadOnlyList<T> list)
        {
            var array = new T[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                array[i] = list[i];
            }

            return array;
        }

        private sealed class FakeActors : ISaveActorSource
        {
            public bool CanExport = true;
            public bool CanExportForSave => CanExport;
            public PartySaveValues ExportForSave() => RichParty();
        }

        /// <summary>故障注入できるファイル操作（P6A 19）。</summary>
        public sealed class FaultyFileSystem : ISaveFileSystem
        {
            public enum Stage
            {
                None,
                Write,
                CorruptOnWrite,
                Replace,
                CorruptAfterReplace,
                Move,
            }

            private readonly RealSaveFileSystem _real = new RealSaveFileSystem();

            public Stage FailAt = Stage.None;

            /// <summary>真を返した書込を失敗させる（パスで選ぶ故障注入。退避の途中失敗用）。</summary>
            public Func<string, bool> FailWriteIf;

            /// <summary>真を返した削除を失敗させる。</summary>
            public Func<string, bool> FailDeleteIf;

            public bool Exists(string path) => _real.Exists(path);

            public string ReadAllText(string path) => _real.ReadAllText(path);

            public void WriteAllTextDurable(string path, string text)
            {
                if (FailAt == Stage.Write || (FailWriteIf != null && FailWriteIf(path)))
                {
                    throw new IOException("注入：書込に失敗");
                }

                _real.WriteAllTextDurable(path, FailAt == Stage.CorruptOnWrite ? text.Substring(0, text.Length / 2) : text);
            }

            public void Replace(string source, string destination)
            {
                if (FailAt == Stage.Replace)
                {
                    throw new IOException("注入：置換に失敗");
                }

                _real.Replace(source, destination);
                if (FailAt == Stage.CorruptAfterReplace)
                {
                    File.WriteAllText(destination, "{ \"corrupt\": true }");
                }
            }

            public void Move(string source, string destination)
            {
                if (FailAt == Stage.Move)
                {
                    throw new IOException("注入：移動に失敗");
                }

                _real.Move(source, destination);
            }

            public void Delete(string path)
            {
                if (FailDeleteIf != null && FailDeleteIf(path))
                {
                    throw new IOException("注入：削除に失敗");
                }

                _real.Delete(path);
            }

            public void CreateDirectory(string path) => _real.CreateDirectory(path);

            public IDisposable TryLock(string path) => _real.TryLock(path);
        }
    }
}
