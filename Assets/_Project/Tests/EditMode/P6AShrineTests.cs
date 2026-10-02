using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.Characters;
using Momotaro.Data.Events;
using Momotaro.Data.Progression;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P6A-03／04：お地蔵様（登録と休息の分離）、休息の一回性、成長の不成立無変更と効果の非累積、旅立ちの受付、
    /// 死亡再開点、遭遇戦の撤退（受入 P6A 03／04／07／09／10／11）。
    /// </summary>
    public sealed class P6AShrineTests
    {
        private static readonly StableId AreaA = new StableId("area_p6_a");
        private static readonly StableId AreaB = new StableId("area_p6_b");
        private static readonly StableId EntryAStart = new StableId("entry_p6_a_start");
        private static readonly StableId EntryAShrine = new StableId("entry_p6_a_shrine");
        private static readonly StableId EntryBWest = new StableId("entry_p6_b_west");
        private static readonly StableId EntryBShrine = new StableId("entry_p6_b_shrine");
        private static readonly StableId ShrineA = new StableId("shrine_p6_a");
        private static readonly StableId ShrineB = new StableId("shrine_p6_b");
        private static readonly StableId GrowthHp = new StableId("growth_p6_hp");
        private static readonly StableId Field = new StableId("field_p6_a_01");

        private readonly List<Object> _spawned = new List<Object>();
        private AreaCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
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
            GameModeProvider.Current = null;
            GameSessionProvider.Current = null;
        }

        // ---------------------------------------------------------------- 調べる／休息（P6A 07）

        /// <summary>調べるだけ：登録・死亡地点・中断位置・保存。回復・補充・周期は変えない。</summary>
        [Test]
        public void Register_OnlyRecordsAndSaves_NoRecovery()
        {
            GameSessionState s = NewAdventure();
            s.TryConsumeKibidango(2);
            int cycle = s.RespawnCycle;
            var requests = new List<AutosaveRequest>();
            s.Changes.AutosaveRequested += requests.Add;

            s.RegisterShrine(Shrine(ShrineB));

            Assert.IsTrue(s.IsShrineRegistered(ShrineB));
            Assert.AreEqual(ShrineB, s.Checkpoint);
            Assert.AreEqual(ResumeAnchor.AtShrine(AreaB, ShrineB), s.Resume);
            Assert.AreEqual(1, s.Kibidango, "調べただけでは補充しない。");
            Assert.AreEqual(cycle, s.RespawnCycle, "調べただけでは敵を戻さない。");
            Assert.AreEqual(1, requests.Count, "登録は保存する。");
        }

        /// <summary>
        /// 休息：周期 1 回・全回復 1 回・補充・活動 Area の普通敵の作り直し・登録、保存要求は 1 件（P6A 03／07）。
        /// 一般消耗品は戻さない。
        /// </summary>
        [Test]
        public void Rest_AdvancesOnce_RestoresOnce_RefillsKibidango_NotItems()
        {
            GameSessionState s = NewAdventure();
            s.TryConsumeKibidango(3);
            s.Inventory.TryAdd(new StableId("item_p6_tonic"), 2, 3);
            s.Inventory.TryConsume(new StableId("item_p6_tonic"), 1);
            s.TryRecordFieldDefeat(AreaA, Field, RewardSnapshot.None, out _);
            var actors = new FakeActors();
            var field = new FakeField();
            var requests = new List<AutosaveRequest>();
            s.Changes.AutosaveRequested += requests.Add;
            int cycle = s.RespawnCycle;

            RestOutcome outcome = ShrineProcedures.Rest(s, _catalog.Campaign, Shrine(ShrineA), actors,
                new IFieldEnemyRebuild[] { field }, "rested");

            Assert.IsTrue(outcome.Rested);
            Assert.AreEqual(cycle + 1, s.RespawnCycle, "周期は 1 回だけ。");
            Assert.AreEqual(1, actors.RestCount, "全回復は 1 回だけ。");
            Assert.AreEqual(1, field.RebuildCount, "活動 Area の普通敵をその場で作り直す。");
            Assert.AreEqual(3, s.Kibidango, "きびだんごは上限まで。");
            Assert.AreEqual(1, s.Inventory.CountOf(new StableId("item_p6_tonic")), "消耗品は戻さない。");
            Assert.IsFalse(s.GetOrCreateArea(AreaA).IsFieldEnemyDefeated(Field, s.RespawnCycle), "普通敵は復活。");
            Assert.AreEqual(ShrineA, s.Checkpoint);
            Assert.AreEqual(1, requests.Count, "保存要求は最後に 1 件。");
        }

        // ---------------------------------------------------------------- 成長（P6A 10）

        /// <summary>不成立（不足・取得済み・未知・前提）は全体無変更で休息もしない。</summary>
        [Test]
        public void Growth_FailuresChangeNothingAndDoNotRest()
        {
            GameSessionState s = NewAdventure();
            var actors = new FakeActors();
            int cycle = s.RespawnCycle;
            long revision = s.Changes.Revision;

            Assert.AreEqual(GrowthPurchaseResult.InsufficientVirtue,
                ShrineProcedures.PurchaseGrowth(s, _catalog.Campaign, Shrine(ShrineA), GrowthHp, actors, null));
            Assert.AreEqual(GrowthPurchaseResult.UnknownGrowth,
                ShrineProcedures.PurchaseGrowth(s, _catalog.Campaign, Shrine(ShrineA), new StableId("growth_p6_none"), actors, null));
            Assert.AreEqual(0, actors.RestCount);
            Assert.AreEqual(0, actors.BonusCalls.Count);
            Assert.AreEqual(cycle, s.RespawnCycle);
            Assert.AreEqual(revision, s.Changes.Revision, "不成立は版も進めない。");
            Assert.AreEqual(0, s.Progress.GrowthCount);
        }

        /// <summary>成立：実支出・取得記録・効果の置き直し（休息より前）・休息 1 回・保存要求 1 件。</summary>
        [Test]
        public void Growth_Success_SpendsAppliesThenRestsOnce()
        {
            GameSessionState s = NewAdventure();
            s.Progress.TryGrant(new RewardSnapshot(new StableId("reward_enemy_melee"), 25, default, false), out _);
            var actors = new FakeActors();
            var requests = new List<AutosaveRequest>();
            s.Changes.AutosaveRequested += requests.Add;
            int cycle = s.RespawnCycle;

            GrowthPurchaseResult result =
                ShrineProcedures.PurchaseGrowth(s, _catalog.Campaign, Shrine(ShrineA), GrowthHp, actors, null);

            Assert.AreEqual(GrowthPurchaseResult.Purchased, result);
            Assert.AreEqual(20, s.Progress.SpentVirtue);
            Assert.AreEqual(5, s.Progress.AvailableVirtue);
            Assert.AreEqual(new[] { 10 }, actors.BonusCalls.ToArray(), "加算は取得 ID から計算した 10。");
            Assert.AreEqual(1, actors.RestCount);
            Assert.Less(actors.BonusCallOrder, actors.RestCallOrder, "効果の置き直しは休息より前（回復は新しい最大値まで）。");
            Assert.AreEqual(cycle + 1, s.RespawnCycle, "成長の休息で周期 1 回。");
            Assert.AreEqual(1, requests.Count);

            Assert.AreEqual(GrowthPurchaseResult.AlreadyAcquired,
                ShrineProcedures.PurchaseGrowth(s, _catalog.Campaign, Shrine(ShrineA), GrowthHp, actors, null));
            Assert.AreEqual(1, actors.RestCount, "取得済みでは休息しない。");
        }

        /// <summary>
        /// 最大 HP の効果は「基礎値 ＋ 加算」で置き直す——何度適用しても累積しない（P6A 10）。本物の <see cref="PlayerVitalsHolder"/>。
        /// </summary>
        [Test]
        public void MaxHpBonus_ReappliedIsNotCumulative()
        {
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetPrivate(data, "_maxHp", 100);
            var go = new GameObject("Player");
            _spawned.Add(go);
            PlayerVitalsHolder vitals = go.AddComponent<PlayerVitalsHolder>();
            SetPrivate(vitals, "_data", data);

            vitals.ApplyMaxHpBonus(10);
            vitals.ApplyMaxHpBonus(10);
            vitals.ApplyMaxHpBonus(10);
            Assert.AreEqual(110, vitals.Vitals.Health.Max, "保存・遷移・Load で累積しない。");
            Assert.AreEqual(100, vitals.Vitals.Health.Current, "最大値の置き直しは回復しない（回復は休息）。");

            vitals.ApplyMaxHpBonus(0);
            Assert.AreEqual(100, vitals.Vitals.Health.Max);
        }

        // ---------------------------------------------------------------- 旅立ち（P6A 11）

        [Test]
        public void FastTravel_RejectsSameUnregisteredAndOutsideShrine()
        {
            GameSessionState s = NewAdventure();
            Assert.AreEqual(FastTravelRejection.SameShrine,
                ShrineProcedures.CanFastTravel(s, _catalog.Campaign, ShrineA, ShrineA, out _));
            Assert.AreEqual(FastTravelRejection.NotRegistered,
                ShrineProcedures.CanFastTravel(s, _catalog.Campaign, ShrineA, ShrineB, out _));
            Assert.AreEqual(FastTravelRejection.NotAtRegisteredShrine,
                ShrineProcedures.CanFastTravel(s, _catalog.Campaign, ShrineB, ShrineA, out _));

            s.RegisterShrine(Shrine(ShrineB));
            Assert.AreEqual(FastTravelRejection.None,
                ShrineProcedures.CanFastTravel(s, _catalog.Campaign, ShrineB, ShrineA, out ShrineInfo dest));
            Assert.AreEqual(AreaA, dest.AreaId);
            Assert.AreEqual(EntryAShrine, dest.Entry.EntryId, "安全復帰点（入口）で解決する。");
        }

        // ---------------------------------------------------------------- 死亡再開点（P6A 08／09）

        /// <summary>P6 は最後に登録したお地蔵様、P5 はカタログの固定点。報酬取得は復帰位置を上書きしない。</summary>
        [Test]
        public void RespawnPoint_IsLastRegisteredShrine_RewardsDoNotMoveAnchors()
        {
            GameSessionState s = NewAdventure();
            Assert.IsTrue(CampaignRespawnPoint.TryResolve(_catalog, s, out AreaEntryInfo first));
            Assert.AreEqual(EntryAShrine, first.EntryId);

            s.RegisterShrine(Shrine(ShrineB));
            s.SetResumeAnchor(ResumeAnchor.AtEntry(AreaA, EntryAStart));
            s.TryRecordFieldDefeat(AreaA, Field, new RewardSnapshot(new StableId("reward_enemy_melee"), 1, default, false), out _);
            s.Progress.TryGrant(new RewardSnapshot(new StableId("reward_p6_find"), 5, default, true), out _);

            Assert.IsTrue(CampaignRespawnPoint.TryResolve(_catalog, s, out AreaEntryInfo second));
            Assert.AreEqual(AreaB, second.AreaId);
            Assert.AreEqual(EntryBShrine, second.EntryId);
            Assert.AreEqual(ShrineB, s.Checkpoint, "報酬で死亡地点は動かない。");
            Assert.AreEqual(ResumeAnchor.AtEntry(AreaA, EntryAStart), s.Resume, "報酬で中断位置は動かない。");
        }

        // ---------------------------------------------------------------- 撤退（P6A 04）

        /// <summary>
        /// 撤退：戦闘中でも遷移の受付は通る（P6 だけ）。退出成功後の再入場で挑戦を捨て、次は最初の Wave。
        /// 撤退までの徳は保持し、再挑戦の撃破でまた得られる。クリアは 1 回だけ。
        /// </summary>
        [Test]
        public void Retreat_AllowedInP6_ChallengeAbandonedOnReentry_KillVirtueKept()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            Rig rig = MakeRig(session, allowRetreat: true);
            var conditions = new GameObject("Conditions").AddComponent<AreaTransitionConditionsSource>();
            _spawned.Add(conditions.gameObject);
            conditions.Bind(null, null, null, rig.Runner);

            Assert.IsTrue(rig.Runner.TryStart().Started);
            Assert.AreEqual(GameMode.Combat, GameModeProvider.Current.Current);
            Assert.IsTrue(conditions.IsRetreatAllowedNow);
            Assert.IsFalse(conditions.IsEncounterActive, "撤退できる戦闘中は、遷移の受付にとって戦闘ではない。");
            Assert.AreEqual(GameMode.Exploration, conditions.Mode, "撤退できる戦闘中は探索として受け付ける。");

            rig.Spawner.Enemies[0].Defeat();
            Assert.AreEqual(1, session.Progress.Virtue);

            // 退出に成功し、戻ってきた（再入場の準備）。
            Assert.IsTrue(rig.Runner.AbandonChallenge());
            Assert.AreEqual(AreaEncounterState.Dormant, rig.Runner.State);
            Assert.AreEqual(CombatSessionState.Preparing, rig.Combat.State, "戦闘セッションも最初から。");
            Assert.AreEqual(0, rig.Spawner.SpawnedCount, "撤去した敵を撃破として数えない。");
            Assert.AreEqual(1, session.Progress.Virtue, "撤退までの徳は失わない。");

            GameModeProvider.Current.ChangeMode(GameMode.Exploration);
            Assert.IsTrue(rig.Runner.TryStart().Started, "最初の Wave から再挑戦できる。");
            foreach (FakeEnemy e in rig.Spawner.Enemies)
            {
                e.Defeat();
            }

            rig.Runner.ResolvePending();
            Assert.AreEqual(AreaEncounterState.Cleared, rig.Runner.State);
            Assert.AreEqual(1 + 2, session.Progress.Virtue, "再挑戦の撃破でも徳。");
            Assert.IsFalse(rig.Runner.AbandonChallenge(), "クリア済みは戻さない。");
        }

        /// <summary>
        /// テスト専用の調整（2026-10-02 オーナー指示：敵の攻撃力 2 倍・初期体力半分）は<b>P6A の検証 campaign だけ</b>に入っている。
        /// 出荷アセットを読んで確かめる（P5／P5.5 の試遊カタログは 1 のまま）。数値は campaign の Data が正本。
        /// </summary>
        [Test]
        public void TestTuning_OnlyInTheP6ACampaign()
        {
            var p6 = UnityEditor.AssetDatabase.LoadAssetAtPath<AreaCatalogData>(
                "Assets/_Project/Data/Tests/Phase6A/SO_AreaCatalog_P6A.asset");
            Assert.IsNotNull(p6, "P6A のカタログがある（build-phase6-world）。");
            Assert.AreEqual(2f, p6.TestEnemyAttackScale, 1e-4f, "P6A：敵の攻撃力 2 倍。");
            Assert.AreEqual(0.5f, p6.TestPlayerMaxHpScale, 1e-4f, "P6A：基礎最大 HP 半分。");

            foreach (string path in new[]
                     {
                         "Assets/_Project/Data/Tests/Phase5/SO_AreaCatalog_P5.asset",
                         "Assets/_Project/Data/Tests/Phase55/SO_AreaCatalog_P55.asset",
                         "Assets/_Project/Data/Tests/Phase55NS/SO_AreaCatalog_P55NS.asset",
                     })
            {
                var other = UnityEditor.AssetDatabase.LoadAssetAtPath<AreaCatalogData>(path);
                Assert.IsNotNull(other, path);
                Assert.AreEqual(1f, other.TestEnemyAttackScale, 1e-4f, path + "：テスト用の調整は入っていない。");
                Assert.AreEqual(1f, other.TestPlayerMaxHpScale, 1e-4f, path + "：テスト用の調整は入っていない。");
            }
        }

        /// <summary>基礎最大 HP の倍率は成長の加算より前に掛かり、何度設定しても累積しない。</summary>
        [Test]
        public void MaxHpScale_AppliesBeforeGrowthBonus_NotCumulative()
        {
            var data = ScriptableObject.CreateInstance<PlayerData>();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_maxHp").intValue = 100;
            so.ApplyModifiedPropertiesWithoutUndo();
            var go = new GameObject("P6ATuningVitals");
            go.SetActive(false);
            var vitals = go.AddComponent<PlayerVitalsHolder>();
            var vso = new UnityEditor.SerializedObject(vitals);
            vso.FindProperty("_data").objectReferenceValue = data;
            vso.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                vitals.SetMaxHpScale(0.5f);
                Assert.AreEqual(50, vitals.Vitals.Health.Max);
                Assert.AreEqual(50, vitals.Vitals.Health.Current, "満タンは半分の最大値で。");
                vitals.ApplyMaxHpBonus(10);
                Assert.AreEqual(60, vitals.Vitals.Health.Max, "加算は半分にした後に足す。");
                vitals.SetMaxHpScale(0.5f);
                vitals.ApplyMaxHpBonus(10);
                Assert.AreEqual(60, vitals.Vitals.Health.Max, "何度設定しても累積しない。");
                vitals.SetMaxHpScale(0f);
                Assert.AreEqual(1f, vitals.MaxHpScale, "0 以下は未設定＝1。");
                Assert.AreEqual(110, vitals.Vitals.Health.Max);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(data);
            }
        }

        /// <summary>P5 の遭遇戦（撤退を許さない）は、従来どおり戦闘中の遷移を断る。</summary>
        [Test]
        public void Retreat_NotAllowedInP5()
        {
            var session = new GameSessionState();
            Rig rig = MakeRig(session, allowRetreat: false);
            var conditions = new GameObject("Conditions").AddComponent<AreaTransitionConditionsSource>();
            _spawned.Add(conditions.gameObject);
            conditions.Bind(null, null, null, rig.Runner);

            Assert.IsTrue(rig.Runner.TryStart().Started);
            Assert.IsFalse(conditions.IsRetreatAllowedNow);
            Assert.IsTrue(conditions.IsEncounterActive);
            Assert.AreEqual(GameMode.Combat, conditions.Mode);
        }

        /// <summary>同じエリアの 2 つの遭遇戦は独立：片方が戦闘中ならまとめは戦闘中、クリアは別々。</summary>
        [Test]
        public void Group_TwoEncountersIndependent()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            Rig one = MakeRig(session, allowRetreat: true, encounterId: "encounter_p6_b_01");
            Rig two = MakeRig(session, allowRetreat: true, encounterId: "encounter_p6_b_02");
            var go = new GameObject("Group");
            _spawned.Add(go);
            AreaEncounterGroup group = go.AddComponent<AreaEncounterGroup>();
            group.Bind(new[] { one.Runner, two.Runner });

            Assert.IsFalse(group.IsEncounterActive);
            Assert.IsNull(group.ActivitySession);
            Assert.IsTrue(one.Runner.TryStart().Started);
            Assert.IsTrue(group.IsEncounterActive);
            Assert.AreSame(one.Runner, group.Engaged);
            Assert.AreEqual(EncounterStartRejection.WrongMode, two.Runner.TryStart().Rejection, "同時に 2 つは始まらない。");

            foreach (FakeEnemy e in one.Spawner.Enemies)
            {
                e.Defeat();
            }

            one.Runner.ResolvePending();
            Assert.IsFalse(group.IsEncounterActive);
            Assert.IsTrue(two.Runner.TryStart().Started, "もう一方は独立に始まる。");
            Assert.AreEqual(1, group.AbandonUnfinished(), "置いていった挑戦だけ捨てる（クリア済みは残す）。");
            Assert.AreEqual(AreaEncounterState.Cleared, one.Runner.State);
            Assert.AreEqual(AreaEncounterState.Dormant, two.Runner.State);
        }

        // ================================================================ 道具

        private GameSessionState NewAdventure()
        {
            var s = new GameSessionState(EncounterClearPolicy.Permanent);
            Assert.IsTrue(s.InitializeNewAdventure("adventure_test", Shrine(ShrineA), 3));
            Assert.IsFalse(s.InitializeNewAdventure("adventure_again", Shrine(ShrineB), 9), "初期化は一度だけ。");
            return s;
        }

        private ShrineInfo Shrine(StableId id)
        {
            Assert.IsTrue(_catalog.Campaign.TryGetShrine(id, out ShrineInfo shrine));
            return shrine;
        }

        private AreaCatalog BuildCatalog()
        {
            AreaDefinition a = NewArea(AreaA, EntryAStart, EntryAShrine);
            AreaDefinition b = NewArea(AreaB, EntryBWest, EntryBShrine);
            var hp = ScriptableObject.CreateInstance<SkillNodeData>();
            _spawned.Add(hp);
            SetPrivate(hp, "_id", GrowthHp);
            hp.EditorSet(20, 0, 10);
            var shrineA = new ShrineDefinition();
            shrineA.EditorSet(ShrineA, AreaA, EntryAShrine, "A");
            var shrineB = new ShrineDefinition();
            shrineB.EditorSet(ShrineB, AreaB, EntryBShrine, "B");
            var catalog = ScriptableObject.CreateInstance<AreaCatalogData>();
            _spawned.Add(catalog);
            SetPrivate(catalog, "_id", new StableId("campaign_p6a_test"));
            catalog.EditorSet(new List<AreaDefinition> { a, b }, AreaA, EntryAStart);
            catalog.EditorSetCampaign(EncounterClearPolicy.Permanent, 1,
                new List<ShrineDefinition> { shrineA, shrineB }, ShrineA, 3,
                new List<ItemDefinition>(), new List<SkillNodeData> { hp });
            catalog.EditorSetKnownIds(new List<StableId> { new StableId("reward_p6_find") },
                new List<StableId> { new StableId("companion_inumaru") });
            Assert.IsTrue(AreaCatalog.TryBuild(catalog, out AreaCatalog built, out IReadOnlyList<string> errors),
                string.Join("\n", errors));
            return built;
        }

        private AreaDefinition NewArea(StableId id, StableId entry1, StableId entry2)
        {
            var area = ScriptableObject.CreateInstance<AreaDefinition>();
            _spawned.Add(area);
            SetPrivate(area, "_id", id);
            var e1 = new AreaEntryDefinition();
            e1.EditorSet(entry1, CardinalDirection.North);
            var e2 = new AreaEntryDefinition();
            e2.EditorSet(entry2, CardinalDirection.South);
            area.EditorSet("Assets/_Project/Scenes/Tests/Phase6/" + id.Value + ".unity", 0,
                new List<AreaEntryDefinition> { e1, e2 }, entry1);
            return area;
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

            throw new MissingFieldException(target.GetType().Name, field);
        }

        private static void InvokePrivate(object target, string method)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
        }

        private sealed class FakeActors : IRestTarget
        {
            private int _order;
            public int RestCount;
            public int RestCallOrder = -1;
            public int BonusCallOrder = -1;
            public readonly List<int> BonusCalls = new List<int>();

            public void RestoreForRest()
            {
                RestCount++;
                RestCallOrder = ++_order;
            }

            public void ApplyMaxHpBonus(int bonus)
            {
                BonusCalls.Add(bonus);
                BonusCallOrder = ++_order;
            }
        }

        private sealed class FakeField : IFieldEnemyRebuild
        {
            public int RebuildCount;
            public void RebuildNow() => RebuildCount++;
        }

        private sealed class Rig
        {
            public AreaEncounterRunner Runner;
            public CombatSessionController Combat;
            public FakeSpawner Spawner;
        }

        private Rig MakeRig(GameSessionState session5, bool allowRetreat, string encounterId = "encounter_p6_b_01")
        {
            var encounter = ScriptableObject.CreateInstance<EncounterData>();
            _spawned.Add(encounter);
            SetPrivate(encounter, "_id", new StableId(encounterId));
            SetPrivate(encounter, "_enemyIds", new List<StableId>
            {
                new StableId("enemy_melee_prototype"), new StableId("enemy_melee_prototype"),
            });

            var go = new GameObject("Encounter_" + encounterId);
            _spawned.Add(go);
            CombatSessionController combat = go.AddComponent<CombatSessionController>();
            var holderGo = new GameObject("Progress");
            _spawned.Add(holderGo);
            PlayerProgressHolder holder = holderGo.AddComponent<PlayerProgressHolder>();
            holder.Bind(session5.Progress);
            CombatRewardCollector rewards = go.AddComponent<CombatRewardCollector>();
            rewards.Bind(combat, holder);
            InvokePrivate(rewards, "OnEnable");

            var kill = ScriptableObject.CreateInstance<RewardData>();
            _spawned.Add(kill);
            SetPrivate(kill, "_id", new StableId("reward_enemy_melee"));
            SetPrivate(kill, "_virtueAmount", 1);
            SetPrivate(kill, "_grantOnce", false);

            var spawner = new FakeSpawner { Session = combat, Reward = kill };
            AreaRuntimeState area = session5.GetOrCreateArea(AreaB);
            if (GameModeProvider.Current == null)
            {
                GameModeProvider.Current = new GameModeService(GameMode.Exploration);
            }

            AreaEncounterRunner runner = go.AddComponent<AreaEncounterRunner>();
            runner.Bind(encounter, combat, new Conditions(), spawner, new Arena(), null,
                () => area, () => session5.RespawnCycle);
            runner.BindSession(() => area, () => session5.RespawnCycle, () => session5);
            runner.AllowRetreat = allowRetreat;
            InvokePrivate(runner, "OnEnable");
            return new Rig { Runner = runner, Combat = combat, Spawner = spawner };
        }

        private sealed class Conditions : IAreaEncounterConditions
        {
            public bool IsAreaReady => true;
            public bool IsExploration => GameModeProvider.Current == null || GameModeProvider.Current.Current == GameMode.Exploration;
            public bool IsPlayerAlive => true;
            public bool IsTransitioning => false;
        }

        private sealed class Arena : IArenaBoundary
        {
            public bool IsEnabled { get; private set; }

            public bool TryEnable(out string error)
            {
                IsEnabled = true;
                error = null;
                return true;
            }

            public void Disable() => IsEnabled = false;
        }

        private sealed class FakeEnemy : IEnemyDefeatSource
        {
            private static int _next = 8000;

            public FakeEnemy(RewardData reward)
            {
                Reward = reward;
                DamageableId = ++_next;
            }

            public RewardData Reward { get; }
            public EnemyDefeatChannel Defeats { get; } = new EnemyDefeatChannel();
            public int DamageableId { get; }
            public bool IsDefeated { get; private set; }

            public void Defeat()
            {
                IsDefeated = true;
                Defeats.Publish(new EnemyDefeatedEvent(DamageableId,
                    new EnemyRewardRequest(DamageableId, EnemyRole.Melee, Reward, Vector3.zero)));
            }
        }

        private sealed class FakeSpawner : IEncounterSpawner
        {
            private readonly List<FakeEnemy> _enemies = new List<FakeEnemy>();
            public CombatSessionController Session;
            public RewardData Reward;

            public int SpawnedCount => _enemies.Count;
            public bool SpawnedActive { get; private set; }
            public IReadOnlyList<IEnemyDefeatSource> Spawned => _enemies;
            public IReadOnlyList<FakeEnemy> Enemies => _enemies;
            public event Action SpawnedActivated;

            public bool TrySpawnAll(in EncounterPlan plan, out string error)
            {
                for (int i = 0; i < plan.PlannedCount; i++)
                {
                    var e = new FakeEnemy(Reward);
                    _enemies.Add(e);
                    Session.RegisterEnemy(e);
                }

                error = null;
                return true;
            }

            public void ActivateSpawned()
            {
                SpawnedActive = true;
                SpawnedActivated?.Invoke();
            }

            public void ReleaseAll()
            {
                foreach (FakeEnemy e in _enemies)
                {
                    Session.UnregisterEnemy(e);
                }

                _enemies.Clear();
                SpawnedActive = false;
            }
        }
    }
}
