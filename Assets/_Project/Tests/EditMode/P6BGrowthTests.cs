using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data.Characters;
using Momotaro.Data.Progression;
using Momotaro.Data.World;
using Momotaro.Editor.Phase6;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Save;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P6B 01〜08・16・17：浅い 9 ノードの取得・効果・払い戻し・権利・章・保存形式 3（P6B 仕様 §3〜§5・§9）。
    ///
    /// <b>出荷される P6B の Data</b>（<c>SO_AreaCatalog_P6B</c> と 9 ノード）を実行時と同じ手順でカタログへ組んで使う。
    /// 分岐（親 1・子 2）と価格変更は製品ノードを増やさず、テスト内の小さなグラフで確かめる（仕様 §5 末尾）。
    /// </summary>
    public sealed class P6BGrowthTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Tests/Phase6B/SO_AreaCatalog_P6B.asset";

        private static readonly StableId ShrineA = new StableId("shrine_p6_a");
        private static readonly StableId Vit1 = new StableId("growth_vit_01");
        private static readonly StableId Vit2 = new StableId("growth_vit_02");
        private static readonly StableId VitRec = new StableId("growth_vit_recovery_01");
        private static readonly StableId Atk1 = new StableId("growth_atk_01");
        private static readonly StableId Atk2 = new StableId("growth_atk_02");
        private static readonly StableId AtkStock = new StableId("growth_atk_stock_01");
        private static readonly StableId Sta1 = new StableId("growth_sta_01");
        private static readonly StableId StaPosture = new StableId("growth_sta_posture_01");
        private static readonly StableId StaRec = new StableId("growth_sta_recovery_01");
        private static readonly StableId[] All = { Vit1, Vit2, VitRec, Atk1, Atk2, AtkStock, Sta1, StaPosture, StaRec };

        private readonly List<Object> _spawned = new List<Object>();
        private AreaCatalog _catalog;

        private CampaignCatalog Campaign => _catalog.Campaign;

        [SetUp]
        public void SetUp()
        {
            var data = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(CatalogPath);
            Assert.IsNotNull(data, "P6B のカタログがある（build-phase6b-world で生成）: " + CatalogPath);
            Assert.IsTrue(AreaCatalog.TryBuild(data, out _catalog, out IReadOnlyList<string> errors),
                "出荷 Data から実行時と同じ手順で組める: " + string.Join(" / ", errors));
            Assert.IsNotNull(_catalog.Campaign);
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
        }

        // ================================================================ P6B 01／17：木の形と取得の検証

        /// <summary>9 ノード・三方向・各方向の最初は前提なし・費用 20/40/30・全取得 270・排他なし（出荷 Data）。</summary>
        [Test]
        public void ShippedTree_NineNodes_ThreeDirections_CostsAndPrerequisites()
        {
            Assert.AreEqual(9, Campaign.GrowthNodes.Count);
            int total = 0;
            foreach (Phase6BTrialValues.Node n in Phase6BTrialValues.Nodes)
            {
                Assert.IsTrue(Campaign.TryGetGrowth(new StableId(n.Id), out GrowthInfo g), n.Id);
                Assert.AreEqual(n.Cost, g.Cost, n.Id + " の費用");
                Assert.AreEqual(0, g.Exclusives.Count, n.Id + " に排他は無い");
                if (string.IsNullOrEmpty(n.Prerequisite))
                {
                    Assert.AreEqual(0, g.Prerequisites.Count, n.Id + " は前提なし");
                }
                else
                {
                    Assert.AreEqual(1, g.Prerequisites.Count);
                    Assert.AreEqual(n.Prerequisite, g.Prerequisites[0].Value, n.Id + " の前提");
                }

                total += g.Cost;
            }

            Assert.AreEqual(270, total, "全取得 270。");
        }

        /// <summary>
        /// 三方向を混ぜて取得できる。未知 ID・取得済み・前提不足・場所外は<b>全体無変更</b>（徳・取得・残数・周期・版・保存要求）。
        /// </summary>
        [Test]
        public void Purchase_MixedDirections_AndRejectionsChangeNothing()
        {
            GameSessionState s = NewAdventure(300);
            var actors = new FakeActors();

            Assert.AreEqual(GrowthPurchaseResult.Purchased, Purchase(s, Sta1, actors));
            Assert.AreEqual(GrowthPurchaseResult.Purchased, Purchase(s, Vit1, actors));
            Assert.AreEqual(GrowthPurchaseResult.Purchased, Purchase(s, Atk1, actors));
            Assert.AreEqual(GrowthPurchaseResult.Purchased, Purchase(s, Vit2, actors), "方向を行き来して取得できる。");

            string before = Fingerprint(s);
            int rests = actors.RestCount;
            AssertNoChange(s, before, () => Purchase(s, new StableId("growth_unknown"), actors), GrowthPurchaseResult.UnknownGrowth);
            AssertNoChange(s, before, () => Purchase(s, Vit1, actors), GrowthPurchaseResult.AlreadyAcquired);
            AssertNoChange(s, before, () => Purchase(s, StaRec, actors), GrowthPurchaseResult.PrerequisiteNotMet);
            AssertNoChange(s, before, () => ShrineProcedures.PurchaseGrowth(s, Campaign, default, Atk2, actors, null),
                GrowthPurchaseResult.NotAllowedHere);
            Assert.AreEqual(rests, actors.RestCount, "不成立では休息しない。");
        }

        /// <summary>徳不足は別 fixture（徳 30）で：費用 40 は取れず全体無変更、20 は取れる。</summary>
        [Test]
        public void Purchase_InsufficientVirtue_ChangesNothing()
        {
            GameSessionState s = NewAdventure(30);
            var actors = new FakeActors();
            Assert.AreEqual(GrowthPurchaseResult.Purchased, Purchase(s, Atk1, actors));
            string before = Fingerprint(s);
            AssertNoChange(s, before, () => Purchase(s, Atk2, actors), GrowthPurchaseResult.InsufficientVirtue);
            Assert.AreEqual(10, s.Progress.AvailableVirtue);
        }

        // ================================================================ P6B 02：効果の値と合計

        /// <summary>各ノードの効果が設定値どおり、全取得の合計は加算（HP+20・刀 1.20・スタミナ+10・体幹 1.10・回復+10・最大数+1）。</summary>
        [Test]
        public void Effects_EachNodeAndFullTree_AddUp()
        {
            GameSessionState s = NewAdventure(300);
            var actors = new FakeActors();
            Assert.AreEqual(0, Campaign.GrowthEffectsOf(s.Progress).MaxHpBonus);

            Purchase(s, Vit1, actors);
            Assert.AreEqual(10, Campaign.GrowthEffectsOf(s.Progress).MaxHpBonus);
            Purchase(s, Atk1, actors);
            Assert.AreEqual(1.10f, Campaign.GrowthEffectsOf(s.Progress).AttackHpMultiplier, 1e-5f);

            foreach (StableId id in All)
            {
                if (!s.Progress.HasGrowth(id))
                {
                    Assert.AreEqual(GrowthPurchaseResult.Purchased, Purchase(s, id, actors), id.Value);
                }
            }

            GrowthEffects e = Campaign.GrowthEffectsOf(s.Progress);
            Assert.AreEqual(20, e.MaxHpBonus);
            Assert.AreEqual(1.20f, e.AttackHpMultiplier, 1e-5f, "倍率は加算（1.10×1.10 ではない）。");
            Assert.AreEqual(10, e.MaxStaminaBonus);
            Assert.AreEqual(1.10f, e.NormalPoiseMultiplier, 1e-5f);
            Assert.AreEqual(10, e.KibidangoHealBonus);
            Assert.AreEqual(1, e.KibidangoCapacityBonus);
            Assert.AreEqual(4, Campaign.KibidangoCapacityOf(s.Progress));
            Assert.AreEqual(60, Campaign.KibidangoHealOf(s.Progress), "基礎 50＋10。最大 HP に連動しない。");
            Assert.AreEqual(30, s.Progress.AvailableVirtue, "300−270。");
        }

        /// <summary>
        /// 主人公への反映（本物の Vitals）：最大 HP・最大スタミナは「基礎値＋加算」で置き直し、何度呼んでも累積しない。
        /// 現在値は回復しない（休息が回復する）。基礎値は Data（PlayerData）から読み、Data は書き換えない。
        /// </summary>
        [Test]
        public void Effects_AppliedToPlayerVitals_RecomputedFromBase()
        {
            PlayerData data = AssetDatabase.LoadAssetAtPath<PlayerData>("Assets/_Project/Data/Player/SO_Player_Momotaro.asset");
            Assert.IsNotNull(data);
            int baseHp = data.MaxHp;
            int baseStamina = data.MaxStamina;
            var go = new GameObject("Player");
            _spawned.Add(go);
            var vitals = go.AddComponent<Momotaro.Gameplay.Player.PlayerVitalsHolder>();
            SetPrivate(vitals, "_data", data);

            for (int i = 0; i < 3; i++)
            {
                vitals.ApplyGrowth(20, 10);
            }

            Assert.AreEqual(baseHp + 20, vitals.MaxHp, "累積しない。");
            Assert.AreEqual(baseStamina + 10, vitals.MaxStaminaValue, 1e-4f);
            Assert.AreEqual(baseHp, vitals.CurrentHp, "最大値の置き直しは回復しない。");
            vitals.ApplyGrowth(0, 0);
            Assert.AreEqual(baseHp, vitals.MaxHp);
            Assert.AreEqual(baseStamina, vitals.MaxStaminaValue, 1e-4f);
            Assert.AreEqual(100, data.MaxHp, "Data の基礎値は書き換えない。");
        }

        // ================================================================ P6B 03／04：休息・保存の一回性と再計算

        /// <summary>取得の成立で：効果の置き直し → 休息 1 回（新しい上限まで補充・周期 +1）→ 保存要求 1 件。</summary>
        [Test]
        public void Purchase_RestsOnceAtNewCaps_RefillsAndSavesOnce()
        {
            GameSessionState s = NewAdventure(300);
            var actors = new FakeActors();
            Purchase(s, Atk1, actors);
            Purchase(s, Atk2, actors);
            s.TryConsumeKibidango(2);
            int cycle = s.RespawnCycle;
            int rests = actors.RestCount;
            var requests = new List<AutosaveRequest>();
            s.Changes.AutosaveRequested += requests.Add;
            var field = new FakeField();

            Assert.AreEqual(GrowthPurchaseResult.Purchased,
                ShrineProcedures.PurchaseGrowth(s, Campaign, Shrine(ShrineA), AtkStock, actors, new[] { field }));

            Assert.AreEqual(4, s.Kibidango, "新しい上限（3＋1）まで補充。");
            Assert.AreEqual(cycle + 1, s.RespawnCycle, "普通敵の周期は 1 回だけ。");
            Assert.AreEqual(rests + 1, actors.RestCount, "休息は 1 回。");
            Assert.AreEqual(1, field.RebuildCount, "活動 Area の普通敵の作り直しも 1 回。");
            Assert.AreEqual(1, requests.Count, "保存要求は 1 件。");
            Assert.Less(actors.EffectsOrder, actors.RestOrder, "効果の置き直しは休息より前（回復は新しい最大値まで）。");
            Assert.AreEqual(1, actors.LastEffects.KibidangoCapacityBonus);
        }

        /// <summary>取得と払い戻しを繰り返しても効果は基礎値から作り直す（累積・取り残しが無い）。</summary>
        [Test]
        public void AcquireRefundRepeated_EffectsAlwaysRebuiltFromBase()
        {
            GameSessionState s = NewAdventure(300, rights: 6);
            var actors = new FakeActors();
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(GrowthPurchaseResult.Purchased, Purchase(s, Vit1, actors));
                Assert.AreEqual(10, actors.LastEffects.MaxHpBonus, "取得 " + i);
                Assert.AreEqual(GrowthRefundResult.Refunded, Refund(s, Vit1, actors));
                Assert.AreEqual(0, actors.LastEffects.MaxHpBonus, "払い戻し " + i);
            }

            Assert.AreEqual(300, s.Progress.AvailableVirtue, "往復で徳は増減しない。");
            Assert.AreEqual(3, s.Progress.RefundRights, "権利は 1 回ずつ減る（6→3）。");
        }

        // ================================================================ P6B 05：末端だけ（分岐 fixture）

        /// <summary>
        /// 小さなテスト用グラフ（親 R・子 C1／C2・孫 G は C1 と C2 の両方が前提）。取得済みの依存があるノードは払い戻せない。
        /// UI の表示（CanRefund）と処理側の再検証が同じ判定。最後に取得したノードに限らない。
        /// </summary>
        [Test]
        public void Refund_OnlyLeaves_WithBranchingFixture()
        {
            AreaCatalog branching = BuildBranchingCatalog(out StableId r, out StableId c1, out StableId c2, out StableId g);
            CampaignCatalog c = branching.Campaign;
            Assert.IsTrue(c.TryGetShrine(new StableId("shrine_fx_a"), out ShrineInfo shrine));
            var s = new GameSessionState(EncounterClearPolicy.Permanent);
            Assert.IsTrue(s.InitializeNewAdventure("adv_fx", shrine, 3, 6));
            Grant(s, 100);
            var actors = new FakeActors();
            foreach (StableId id in new[] { r, c1, c2, g })
            {
                Assert.AreEqual(GrowthPurchaseResult.Purchased,
                    ShrineProcedures.PurchaseGrowth(s, c, shrine, id, actors, null), id.Value);
            }

            string before = Fingerprint(s, c);
            foreach (StableId id in new[] { r, c1, c2 })
            {
                Assert.AreEqual(GrowthRefundResult.HasDependents, ShrineProcedures.CanRefund(s, c, id), id.Value);
                Assert.AreEqual(GrowthRefundResult.HasDependents,
                    ShrineProcedures.RefundGrowth(s, c, shrine, id, actors, null), id.Value);
                Assert.AreEqual(before, Fingerprint(s, c), id.Value + "：非末端は全体無変更。");
            }

            Assert.AreEqual(GrowthRefundResult.Refunded, ShrineProcedures.RefundGrowth(s, c, shrine, g, actors, null));
            Assert.AreEqual(GrowthRefundResult.HasDependents, ShrineProcedures.CanRefund(s, c, r), "子が 1 つでも残れば親は不可。");
            Assert.AreEqual(GrowthRefundResult.Refunded, ShrineProcedures.RefundGrowth(s, c, shrine, c1, actors, null),
                "最後に取得したノード（C2）以外も払い戻せる。");
            Assert.AreEqual(GrowthRefundResult.HasDependents, ShrineProcedures.CanRefund(s, c, r));
            Assert.AreEqual(GrowthRefundResult.Refunded, ShrineProcedures.RefundGrowth(s, c, shrine, c2, actors, null));
            Assert.AreEqual(GrowthRefundResult.Refunded, ShrineProcedures.RefundGrowth(s, c, shrine, r, actors, null));
        }

        // ================================================================ P6B 06：会計

        /// <summary>
        /// 実支出の返還・累計不変・使用済みの減少・権利 −1。価格が変わっても返還は実支出、再取得はその時の Data 価格。
        /// 返還と再取得を繰り返して徳や権利が増えない。
        /// </summary>
        [Test]
        public void Refund_ReturnsActualSpend_PriceChangeReacquireAtNewPrice()
        {
            SkillNodeData node;
            AreaCatalog cheap = BuildSingleNodeCatalog(20, out node);
            CampaignCatalog c = cheap.Campaign;
            Assert.IsTrue(c.TryGetShrine(new StableId("shrine_fx_a"), out ShrineInfo shrine));
            var s = new GameSessionState(EncounterClearPolicy.Permanent);
            Assert.IsTrue(s.InitializeNewAdventure("adv_price", shrine, 3, 3));
            Grant(s, 100);
            var actors = new FakeActors();
            Assert.AreEqual(GrowthPurchaseResult.Purchased, ShrineProcedures.PurchaseGrowth(s, c, shrine, node.Id, actors, null));
            Assert.AreEqual(20, s.Progress.GrowthSpent(node.Id));

            // Data の価格が 35 へ変わった（次の起動でカタログを組み直した）。
            node.EditorSetP6B(35, 0, 5, 0f, 0, 0f, 0, 0, null);
            Assert.IsTrue(AreaCatalog.TryBuild(Data(cheap), out AreaCatalog repriced, out IReadOnlyList<string> errors),
                string.Join("/", errors));
            CampaignCatalog c2 = repriced.Campaign;

            int total = s.Progress.TotalVirtue;
            Assert.AreEqual(GrowthRefundResult.Refunded, ShrineProcedures.RefundGrowth(s, c2, shrine, node.Id, actors, null));
            Assert.AreEqual(total, s.Progress.TotalVirtue, "累計は変えない。");
            Assert.AreEqual(0, s.Progress.SpentVirtue, "使用済みから実支出 20 を戻す。");
            Assert.AreEqual(100, s.Progress.AvailableVirtue, "返還は実支出（20）で、現在価格（35）ではない。");
            Assert.AreEqual(2, s.Progress.RefundRights);

            Assert.AreEqual(GrowthPurchaseResult.Purchased, ShrineProcedures.PurchaseGrowth(s, c2, shrine, node.Id, actors, null));
            Assert.AreEqual(35, s.Progress.GrowthSpent(node.Id), "再取得はその時点の Data 価格。");
            Assert.AreEqual(65, s.Progress.AvailableVirtue);
            Assert.AreEqual(GrowthRefundResult.Refunded, ShrineProcedures.RefundGrowth(s, c2, shrine, node.Id, actors, null));
            Assert.AreEqual(100, s.Progress.AvailableVirtue, "往復で徳は増えない。");
            Assert.AreEqual(1, s.Progress.RefundRights, "権利も増えない。");
        }

        // ================================================================ P6B 07：払い戻し後の上限・休息・不成立・連打

        /// <summary>
        /// 最大数ノードの払い戻しで上限 4→3、休息で残数は新しい上限まで（超過を残さない）・周期 +1・保存 1 件。
        /// 権利 0・未取得・未知・場所外は全体無変更。二重入力は 1 回だけ処理（2 回目は未取得で拒否）。
        /// </summary>
        [Test]
        public void Refund_LowersCaps_RestsOnce_RejectionsAndDoubleInputChangeNothing()
        {
            GameSessionState s = NewAdventure(300);
            var actors = new FakeActors();
            Purchase(s, Atk1, actors);
            Purchase(s, Atk2, actors);
            Purchase(s, AtkStock, actors);
            Assert.AreEqual(4, s.Kibidango);
            int cycle = s.RespawnCycle;
            var requests = new List<AutosaveRequest>();
            s.Changes.AutosaveRequested += requests.Add;
            var field = new FakeField();

            Assert.AreEqual(GrowthRefundResult.Refunded,
                ShrineProcedures.RefundGrowth(s, Campaign, Shrine(ShrineA), AtkStock, actors, new[] { field }));
            Assert.AreEqual(3, Campaign.KibidangoCapacityOf(s.Progress));
            Assert.AreEqual(3, s.Kibidango, "上限低下後に超過を残さない。");
            Assert.AreEqual(cycle + 1, s.RespawnCycle, "普通敵は 1 周期。");
            Assert.AreEqual(1, field.RebuildCount);
            Assert.AreEqual(1, requests.Count, "保存要求 1 件。");
            Assert.AreEqual(0, actors.LastEffects.KibidangoCapacityBonus);

            string before = Fingerprint(s);
            AssertRefundNoChange(s, before, AtkStock, actors, GrowthRefundResult.NotAcquired, "二重入力の 2 回目");
            AssertRefundNoChange(s, before, new StableId("growth_unknown"), actors, GrowthRefundResult.UnknownGrowth, "未知");
            AssertRefundNoChange(s, before, Atk1, actors, GrowthRefundResult.HasDependents, "非末端");
            Assert.AreEqual(GrowthRefundResult.NotAllowedHere,
                ShrineProcedures.RefundGrowth(s, Campaign, default, Atk2, actors, null), "場所外");
            Assert.AreEqual(before, Fingerprint(s));

            // 権利を使い切ると 0 で拒否。
            Assert.AreEqual(GrowthRefundResult.Refunded, Refund(s, Atk2, actors));
            Assert.AreEqual(GrowthRefundResult.Refunded, Refund(s, Atk1, actors));
            Assert.AreEqual(0, s.Progress.RefundRights);
            Purchase(s, Vit1, actors);
            string zero = Fingerprint(s);
            AssertRefundNoChange(s, zero, Vit1, actors, GrowthRefundResult.NoRights, "権利 0");
        }

        // ================================================================ P6B 08：権利と章

        /// <summary>
        /// 初期 3・章ごと +3・上限 6。上限で増加 0 の章も処理済み。再通知・Load 後の再通知で重複追加しない。
        /// 死亡（周期）・休息・Load で権利を初期化しない。
        /// </summary>
        [Test]
        public void ChapterRights_Initial3_PlusThreePerChapter_Cap6_NoDuplicates()
        {
            GameSessionState s = NewAdventure(300);
            Assert.AreEqual(3, s.Progress.RefundRights, "初期 3（New Game）。");
            StableId ch1 = Phase6BTrialValues.ChapterFixture;
            StableId ch2 = Phase6BTrialValues.ChapterFixture2;

            Assert.AreEqual(ChapterRightsResult.Processed,
                ShrineProcedures.GrantChapterRefundRights(s, Campaign, ch1, out int added));
            Assert.AreEqual(3, added);
            Assert.AreEqual(6, s.Progress.RefundRights);
            Assert.AreEqual(ChapterRightsResult.AlreadyProcessed,
                ShrineProcedures.GrantChapterRefundRights(s, Campaign, ch1, out added), "再通知");
            Assert.AreEqual(6, s.Progress.RefundRights);

            Assert.AreEqual(ChapterRightsResult.Processed,
                ShrineProcedures.GrantChapterRefundRights(s, Campaign, ch2, out added), "上限でも処理済みにする");
            Assert.AreEqual(0, added);
            Assert.IsTrue(s.Progress.IsChapterProcessed(ch2));
            Assert.AreEqual(ChapterRightsResult.UnknownChapter,
                ShrineProcedures.GrantChapterRefundRights(s, Campaign, new StableId("chapter_unknown"), out _));

            // 権利を使い、死亡（周期）と休息を挟む——初期化しない。
            var actors = new FakeActors();
            Purchase(s, Vit1, actors);
            Refund(s, Vit1, actors);
            s.AdvanceRespawnCycle();
            ShrineProcedures.Rest(s, Campaign, Shrine(ShrineA), actors, null, "rest");
            Assert.AreEqual(5, s.Progress.RefundRights);

            // Load（保存 → 候補）でも 5 のまま。同じ章を再通知しても受け取り直せない。
            GameSessionState loaded = RoundTrip(s);
            Assert.AreEqual(5, loaded.Progress.RefundRights, "Load で 3 へ戻さない。");
            Assert.IsTrue(loaded.Progress.IsChapterProcessed(ch1));
            Assert.AreEqual(ChapterRightsResult.AlreadyProcessed,
                ShrineProcedures.GrantChapterRefundRights(loaded, Campaign, ch1, out _));
            Assert.AreEqual(5, loaded.Progress.RefundRights);
        }

        /// <summary>章の追加は呼び出し側のまとめ（章の確定）の中で同じ保存単位に入る（保存要求は最後に 1 件）。</summary>
        [Test]
        public void ChapterRights_JoinTheCallersSaveUnit()
        {
            GameSessionState s = NewAdventure(0);
            var requests = new List<AutosaveRequest>();
            s.Changes.AutosaveRequested += requests.Add;
            s.Changes.BeginBatch("chapter_cleared");
            s.RefillKibidango(1); // 章の確定に伴う別の変化（代わり）
            ShrineProcedures.GrantChapterRefundRights(s, Campaign, Phase6BTrialValues.ChapterFixture, out _);
            Assert.AreEqual(0, requests.Count, "まとめの途中では出さない。");
            s.Changes.EndBatch();
            Assert.AreEqual(1, requests.Count, "章の確定と同じ 1 件。");
        }

        // ================================================================ P6B 16：保存形式 3

        /// <summary>新しい保存項目（権利・処理済み章）を非初期値で往復。成長・残数・徳も一緒に戻る。</summary>
        [Test]
        public void Save_V3_RoundTripsRightsChaptersGrowthAndStock()
        {
            GameSessionState s = NewAdventure(300);
            var actors = new FakeActors();
            Purchase(s, Atk1, actors);
            Purchase(s, Atk2, actors);
            Purchase(s, AtkStock, actors);
            Purchase(s, Vit1, actors);
            Refund(s, Vit1, actors);
            ShrineProcedures.GrantChapterRefundRights(s, Campaign, Phase6BTrialValues.ChapterFixture2, out _);
            s.TryConsumeKibidango(1);

            string json = Serialize(s);
            // P7 で現行の版は 4（版 3 の欄に会話・章の story を足した）。払い戻しの欄の往復はこのまま検査する。
            StringAssert.Contains("\"schemaVersion\": " + SaveSnapshot.CurrentSchemaVersion, json);
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(json, out _, out SaveSnapshot read, out string error), error);
            Assert.IsTrue(read.HasRefundData);
            Assert.IsTrue(SessionRestorer.TryBuildCandidate(read, _catalog, out GameSessionState c, out error), error);
            Assert.AreEqual(5, c.Progress.RefundRights, "3−1＋3（章）。");
            Assert.IsTrue(c.Progress.IsChapterProcessed(Phase6BTrialValues.ChapterFixture2));
            Assert.IsTrue(c.Progress.HasGrowth(AtkStock));
            Assert.IsFalse(c.Progress.HasGrowth(Vit1));
            Assert.AreEqual(3, c.Kibidango);
            Assert.AreEqual(4, Campaign.KibidangoCapacityOf(c.Progress));
            Assert.AreEqual(s.Progress.AvailableVirtue, c.Progress.AvailableVirtue);
            Assert.AreEqual(json, Serialize(c), "往復で 1 文字も変わらない。");
        }

        /// <summary>版 1・2 は明示的な移行で権利 3・章集合空。版 3 の欄を版 2 に付けると未知の欄で拒否。</summary>
        [Test]
        public void Save_V1AndV2_MigrateToInitialRightsAndEmptyChapters()
        {
            GameSessionState s = NewAdventure(300);
            Purchase(s, Sta1, new FakeActors());
            string v3 = Serialize(s);
            string payload = PayloadOf(v3);
            int at = payload.IndexOf(",\"refund\":", StringComparison.Ordinal);
            Assert.Greater(at, 0);
            string payloadV2 = payload.Substring(0, at) + "}";
            int q = payloadV2.IndexOf(",\"questStages\":", StringComparison.Ordinal);
            string payloadV1 = payloadV2.Substring(0, q) + "}";

            foreach ((int version, string body) in new[] { (2, payloadV2), (1, payloadV1) })
            {
                string json = Envelope(v3, version, body);
                Assert.IsTrue(SaveJsonCodec.TryDeserialize(json, out _, out SaveSnapshot read, out string error),
                    "版 " + version + ": " + error);
                Assert.IsFalse(read.HasRefundData);
                Assert.IsTrue(SessionRestorer.TryBuildCandidate(read, _catalog, out GameSessionState c, out error), error);
                Assert.AreEqual(3, c.Progress.RefundRights, "版 " + version + "：権利は初期 3 へ移行。");
                Assert.AreEqual(0, c.Progress.ProcessedChapterCount, "章集合は空。");
                Assert.IsTrue(c.Progress.HasGrowth(Sta1));

                // 新版で保存し直すと、その値（3）が版 3 として残る（以後の Load で 3 へ戻すのではない）。
                c.Progress.TryRefundGrowth(Sta1, out _);
                GameSessionState again = RoundTrip(c);
                Assert.AreEqual(2, again.Progress.RefundRights, "版 3 の値を Load のたびに 3 へ戻さない。");
            }

            Assert.IsFalse(SaveJsonCodec.TryDeserialize(Envelope(v3, 2, payload), out _, out _, out _),
                "版 2 に refund は未知の欄。");
        }

        /// <summary>
        /// 不正の拒否：権利 7・負・未知章・重複章・未知成長・前提矛盾・残数の上限超過（成長込みの上限で判定）。
        /// 欠損・型違いは厳格な読み取りで拒否。現在価格と実支出が違うこと自体は不正にしない。
        /// </summary>
        [Test]
        public void Save_V3_RejectsBadRefundGrowthAndStock()
        {
            GameSessionState s = NewAdventure(300);
            var actors = new FakeActors();
            Purchase(s, Atk1, actors);
            Purchase(s, Atk2, actors);
            Purchase(s, AtkStock, actors); // 上限 4、残数 4
            SaveSnapshot good = Capture(s);
            AssertValid(good, "基準");

            AssertInvalid(With(good, rights: 7), "権利 7");
            AssertInvalid(With(good, rights: -1), "権利 −1");
            AssertInvalid(With(good, chapters: new[] { "chapter_unknown" }), "未知の章");
            AssertInvalid(With(good, chapters: new[] { Phase6BTrialValues.ChapterFixture.Value, Phase6BTrialValues.ChapterFixture.Value }),
                "章の重複");
            AssertInvalid(With(good, growth: new[] { Pair("growth_unknown", 0) }, spent: 0), "未知の成長");
            AssertInvalid(With(good, growth: new[] { Pair(AtkStock.Value, 30) }, spent: 30), "前提矛盾（atk_02 なしで stock）");
            AssertInvalid(With(good, kibidango: 5), "残数 5 は上限 4 超過");
            AssertValid(With(good, kibidango: 4), "残数 4 は成長込みの上限内");
            AssertValid(With(good, growth: new[] { Pair(Atk1.Value, 1), Pair(Atk2.Value, 40), Pair(AtkStock.Value, 30) }, spent: 71),
                "価格と実支出の違いは不正ではない");

            string json = Serialize(s);
            Assert.IsFalse(SaveJsonCodec.TryDeserialize(json.Replace("\"rights\": 3", "\"rights\": \"3\""), out _, out _, out _),
                "型違い");
            Assert.IsFalse(SaveJsonCodec.TryDeserialize(json.Replace("\"rights\": 3,", ""), out _, out _, out _), "欠損");
        }

        // ================================================================ P6B 17：Data の構造検査

        /// <summary>前提の循環・campaign 外の参照・ゼロ効果・負値を Data 検証とカタログ構築の両方が拒否する。</summary>
        [Test]
        public void GraphCheck_RejectsCycleForeignReferenceZeroEffectAndNegative()
        {
            SkillNodeData a = NewNode("growth_fx_a", 10, maxHp: 5);
            SkillNodeData b = NewNode("growth_fx_b", 10, maxHp: 5);
            a.EditorSetP6B(10, 0, 5, 0f, 0, 0f, 0, 0, new List<SkillNodeData> { b });
            b.EditorSetP6B(10, 0, 5, 0f, 0, 0f, 0, 0, new List<SkillNodeData> { a });
            var errors = new List<string>();
            Assert.IsFalse(SkillGraphCheck.Check(new List<SkillNodeData> { a, b }, errors), "循環");

            SkillNodeData foreign = NewNode("growth_fx_foreign", 10, maxHp: 5);
            SkillNodeData c = NewNode("growth_fx_c", 10, maxHp: 5);
            c.EditorSetP6B(10, 0, 5, 0f, 0, 0f, 0, 0, new List<SkillNodeData> { foreign });
            errors.Clear();
            Assert.IsFalse(SkillGraphCheck.Check(new List<SkillNodeData> { c }, errors), "campaign 外の前提");

            SkillNodeData zero = NewNode("growth_fx_zero", 10);
            Assert.IsFalse(zero.HasAnyEffect);
            var report = new Momotaro.Data.DataValidationReport();
            SkillNodeData negative = NewNode("growth_fx_neg", 10);
            negative.EditorSetP6B(10, 0, 0, -0.1f, 0, 0f, 0, 0, null);
            negative.Validate(report);
            Assert.IsTrue(report.HasErrors, "負の倍率");

            AreaCatalog built = BuildSingleNodeCatalog(10, out SkillNodeData single);
            AreaCatalogData data = Data(built);
            single.EditorSetP6B(10, 0, 0, 0f, 0, 0f, 0, 0, null); // ゼロ効果へ
            Assert.IsFalse(AreaCatalog.TryBuild(data, out _, out _), "ゼロ効果ノードの campaign は組まない。");
        }

        // ================================================================ 補助

        private GameSessionState NewAdventure(int virtue, int rights = 3)
        {
            var s = new GameSessionState(EncounterClearPolicy.Permanent);
            Assert.IsTrue(s.InitializeNewAdventure("adventure_p6b_test", Shrine(ShrineA),
                Campaign.KibidangoCapacityOf(s.Progress), rights));
            Grant(s, virtue);
            return s;
        }

        private static void Grant(GameSessionState s, int virtue)
        {
            if (virtue > 0)
            {
                s.Progress.TryGrant(new RewardSnapshot(new StableId("reward_p6b_test_" + virtue), virtue, default, false), out _);
            }
        }

        private ShrineInfo Shrine(StableId id)
        {
            Assert.IsTrue(Campaign.TryGetShrine(id, out ShrineInfo shrine));
            return shrine;
        }

        private GrowthPurchaseResult Purchase(GameSessionState s, StableId id, FakeActors actors) =>
            ShrineProcedures.PurchaseGrowth(s, Campaign, Shrine(ShrineA), id, actors, null);

        private GrowthRefundResult Refund(GameSessionState s, StableId id, FakeActors actors) =>
            ShrineProcedures.RefundGrowth(s, Campaign, Shrine(ShrineA), id, actors, null);

        private static PartySaveValues Party() =>
            new PartySaveValues(new PlayerSaveValues(80, 50f, 0f, 0f), false, default);

        private SaveSnapshot Capture(GameSessionState s) => SaveSnapshot.Capture(s, Campaign, Party());

        private string Serialize(GameSessionState s) =>
            SaveJsonCodec.Serialize(Capture(s), 5, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));

        private GameSessionState RoundTrip(GameSessionState s)
        {
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(Serialize(s), out _, out SaveSnapshot read, out string error), error);
            Assert.IsTrue(SessionRestorer.TryBuildCandidate(read, _catalog, out GameSessionState c, out error), error);
            return c;
        }

        /// <summary>全体の指紋（保存の形・版・保存要求数）。「全体無変更」を 1 か所で見る。</summary>
        private string Fingerprint(GameSessionState s, CampaignCatalog campaign = null)
        {
            SaveSnapshot snap = SaveSnapshot.Capture(s, campaign ?? Campaign, Party());
            return SaveJsonCodec.Serialize(snap, 1, DateTime.UnixEpoch) + "|rev=" + s.Changes.Revision
                + "|req=" + s.Changes.AutosaveRequestCount;
        }

        private void AssertNoChange(GameSessionState s, string before, Func<GrowthPurchaseResult> act, GrowthPurchaseResult expected)
        {
            Assert.AreEqual(expected, act());
            Assert.AreEqual(before, Fingerprint(s), expected + "：全体無変更。");
        }

        private void AssertRefundNoChange(GameSessionState s, string before, StableId id, FakeActors actors,
            GrowthRefundResult expected, string label)
        {
            int rests = actors.RestCount;
            Assert.AreEqual(expected, Refund(s, id, actors), label);
            Assert.AreEqual(before, Fingerprint(s), label + "：全体無変更。");
            Assert.AreEqual(rests, actors.RestCount, label + "：休息しない。");
        }

        private void AssertValid(SaveSnapshot snap, string label)
        {
            var errors = new List<string>();
            Assert.IsTrue(SaveSnapshotValidator.Validate(snap, _catalog, errors), label + ": " + string.Join(" / ", errors));
        }

        private void AssertInvalid(SaveSnapshot snap, string label)
        {
            var errors = new List<string>();
            Assert.IsFalse(SaveSnapshotValidator.Validate(snap, _catalog, errors), label + " を拒否する。");
            Assert.IsFalse(SessionRestorer.TryBuildCandidate(snap, _catalog, out _, out _), label + "：候補を作らない。");
        }

        private static KeyValuePair<string, int> Pair(string k, int v) => new KeyValuePair<string, int>(k, v);

        private static SaveSnapshot With(SaveSnapshot s, int? rights = null, string[] chapters = null,
            KeyValuePair<string, int>[] growth = null, int? spent = null, int? kibidango = null)
        {
            var areas = new List<AreaSaveRecord>(s.Areas).ToArray();
            return new SaveSnapshot(s.CampaignId, s.ContentVersion, s.AdventureId, s.Revision, s.RespawnCycle,
                s.TotalVirtue, spent ?? s.SpentVirtue, new List<string>(s.GrantedRewards).ToArray(),
                growth ?? new List<KeyValuePair<string, int>>(s.Growth).ToArray(),
                new List<string>(s.VisitedAreas).ToArray(), new List<string>(s.Recruited).ToArray(), areas,
                new List<KeyValuePair<string, int>>(s.Inventory).ToArray(), kibidango ?? s.Kibidango,
                new List<string>(s.RegisteredShrines).ToArray(), s.Checkpoint, s.ResumeKind, s.ResumeAreaId,
                s.ResumePointId, s.Party, new List<KeyValuePair<string, int>>(s.QuestStages).ToArray(),
                true, rights ?? s.RefundRights, chapters ?? new List<string>(s.ProcessedChapters).ToArray());
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

        private SkillNodeData NewNode(string id, int cost, int maxHp = 0)
        {
            var node = ScriptableObject.CreateInstance<SkillNodeData>();
            _spawned.Add(node);
            SetPrivate(node, "_id", new StableId(id));
            node.EditorSetP6B(cost, 0, maxHp, 0f, 0, 0f, 0, 0, null);
            return node;
        }

        private readonly Dictionary<AreaCatalog, AreaCatalogData> _dataOf = new Dictionary<AreaCatalog, AreaCatalogData>();

        private AreaCatalogData Data(AreaCatalog built) => _dataOf[built];

        private AreaCatalog BuildFixtureCatalog(List<SkillNodeData> nodes)
        {
            StableId area = new StableId("area_fx_a");
            StableId entry = new StableId("entry_fx_a");
            var def = ScriptableObject.CreateInstance<AreaDefinition>();
            _spawned.Add(def);
            SetPrivate(def, "_id", area);
            var e = new AreaEntryDefinition();
            e.EditorSet(entry, Momotaro.Core.World.CardinalDirection.North);
            def.EditorSet("Assets/_Project/Scenes/Tests/Phase6B/fx.unity", 0, new List<AreaEntryDefinition> { e }, entry);
            var shrine = new ShrineDefinition();
            shrine.EditorSet(new StableId("shrine_fx_a"), area, entry, "FX");
            var catalog = ScriptableObject.CreateInstance<AreaCatalogData>();
            _spawned.Add(catalog);
            SetPrivate(catalog, "_id", new StableId("campaign_p6b_fixture"));
            catalog.EditorSet(new List<AreaDefinition> { def }, area, entry);
            catalog.EditorSetCampaign(EncounterClearPolicy.Permanent, 1, new List<ShrineDefinition> { shrine },
                new StableId("shrine_fx_a"), 3, new List<ItemDefinition>(), nodes);
            Assert.IsTrue(AreaCatalog.TryBuild(catalog, out AreaCatalog built, out IReadOnlyList<string> errors),
                string.Join(" / ", errors));
            _dataOf[built] = catalog;
            return built;
        }

        private AreaCatalog BuildBranchingCatalog(out StableId r, out StableId c1, out StableId c2, out StableId g)
        {
            SkillNodeData nr = NewNode("growth_fx_root", 10, maxHp: 1);
            SkillNodeData n1 = NewNode("growth_fx_child_1", 10, maxHp: 1);
            SkillNodeData n2 = NewNode("growth_fx_child_2", 10, maxHp: 1);
            SkillNodeData ng = NewNode("growth_fx_grand", 10, maxHp: 1);
            n1.EditorSetP6B(10, 1, 1, 0f, 0, 0f, 0, 0, new List<SkillNodeData> { nr });
            n2.EditorSetP6B(10, 1, 1, 0f, 0, 0f, 0, 0, new List<SkillNodeData> { nr });
            ng.EditorSetP6B(10, 2, 1, 0f, 0, 0f, 0, 0, new List<SkillNodeData> { n1, n2 });
            r = nr.Id;
            c1 = n1.Id;
            c2 = n2.Id;
            g = ng.Id;
            return BuildFixtureCatalog(new List<SkillNodeData> { nr, n1, n2, ng });
        }

        private AreaCatalog BuildSingleNodeCatalog(int cost, out SkillNodeData node)
        {
            node = NewNode("growth_fx_price", cost, maxHp: 5);
            return BuildFixtureCatalog(new List<SkillNodeData> { node });
        }

        private static void SetPrivate(object target, string field, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        private sealed class FakeActors : IRestTarget
        {
            private int _order;
            public int RestCount;
            public int RestOrder = -1;
            public int EffectsOrder = -1;
            public GrowthEffects LastEffects;

            public void RestoreForRest()
            {
                RestCount++;
                RestOrder = ++_order;
            }

            public void ApplyGrowthEffects(in GrowthEffects effects)
            {
                LastEffects = effects;
                EffectsOrder = ++_order;
            }
        }

        private sealed class FakeField : IFieldEnemyRebuild
        {
            public int RebuildCount;
            public void RebuildNow() => RebuildCount++;
        }
    }
}
