using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
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
    /// P6A-01：恒久進行と普通敵周期の分離、初回報酬と撃破の原子性（P6 仕様 §3・§4、受入 P6A 01／02／03／05／06）。
    ///
    /// 純粋 State（<see cref="GameSessionState"/>・<see cref="AreaRuntimeState"/>・<see cref="PlayerProgressState"/>）を
    /// 直接動かす。遭遇戦は<b>本物の <see cref="AreaEncounterRunner"/></b> と <see cref="CombatSessionController"/>／
    /// <see cref="CombatRewardCollector"/> を繋ぎ、撃破は実チャネルから流す（CLAUDE.md「繋ぎ目のテスト」）。
    /// </summary>
    public sealed class P6AProgressionTests
    {
        private static readonly StableId AreaA = new StableId("area_p6_a");
        private static readonly StableId AreaB = new StableId("area_p6_b");
        private static readonly StableId PlacementOne = new StableId("field_p6_a_01");
        private static readonly StableId PlacementTwo = new StableId("field_p6_a_02");
        private static readonly StableId EncounterOne = new StableId("encounter_p6_b_01");
        private static readonly StableId EncounterTwo = new StableId("encounter_p6_b_02");
        private static readonly StableId GateFlag = new StableId("flag_p6_b_gate");

        private readonly List<Object> _spawned = new List<Object>();

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

        private static RewardSnapshot Once(string id, int virtue) =>
            new RewardSnapshot(new StableId(id), virtue, default, grantOnce: true);

        private static RewardSnapshot Each(string id, int virtue) =>
            new RewardSnapshot(new StableId(id), virtue, default, grantOnce: false);

        // ---------------------------------------------------------------- 徳の会計

        /// <summary>使用可能 ＝ 累計 − 使用済み。支出が無い間は従来の Virtue と一致する（互換）。</summary>
        [Test]
        public void Virtue_AvailableIsTotalMinusSpent()
        {
            var p = new PlayerProgressState();
            p.TryGrant(Each("reward_enemy_melee", 30), out _);

            Assert.AreEqual(30, p.TotalVirtue);
            Assert.AreEqual(0, p.SpentVirtue);
            Assert.AreEqual(30, p.Virtue, "支出が無い間は従来の Virtue と同じ。");

            Assert.AreEqual(GrowthPurchaseResult.Purchased, p.TryPurchaseGrowth(new StableId("growth_p6_hp"), 20));
            Assert.AreEqual(30, p.TotalVirtue, "累計は減らない。");
            Assert.AreEqual(20, p.SpentVirtue);
            Assert.AreEqual(10, p.AvailableVirtue);
            Assert.AreEqual(10, p.Virtue, "Virtue は使用可能の別名。");
            Assert.AreEqual(20, p.GrowthSpent(new StableId("growth_p6_hp")), "実支出額を記録する。");
        }

        /// <summary>不成立（不足・取得済み・未知 ID・負の費用）は全体無変更（P6A 10）。</summary>
        [Test]
        public void GrowthPurchase_FailuresChangeNothing()
        {
            var p = new PlayerProgressState();
            p.TryGrant(Each("reward_enemy_melee", 15), out _);
            int changes = 0;
            p.Changed += _ => changes++;

            Assert.AreEqual(GrowthPurchaseResult.InsufficientVirtue, p.TryPurchaseGrowth(new StableId("growth_p6_hp"), 20));
            Assert.AreEqual(GrowthPurchaseResult.UnknownGrowth, p.TryPurchaseGrowth(default, 1));
            Assert.AreEqual(GrowthPurchaseResult.UnknownGrowth, p.TryPurchaseGrowth(new StableId("Bad Id"), 1));
            Assert.AreEqual(GrowthPurchaseResult.InvalidCost, p.TryPurchaseGrowth(new StableId("growth_p6_hp"), -1));
            Assert.AreEqual(15, p.AvailableVirtue);
            Assert.AreEqual(0, p.SpentVirtue);
            Assert.AreEqual(0, p.GrowthCount);
            Assert.AreEqual(0, changes, "不成立では通知も出ない。");

            Assert.AreEqual(GrowthPurchaseResult.Purchased, p.TryPurchaseGrowth(new StableId("growth_p6_hp"), 10));
            Assert.AreEqual(GrowthPurchaseResult.AlreadyAcquired, p.TryPurchaseGrowth(new StableId("growth_p6_hp"), 0));
            Assert.AreEqual(10, p.SpentVirtue, "取得済みの再購入で二重に払わない。");
        }

        // ---------------------------------------------------------------- 初到達（P6A 01）

        /// <summary>初到達は一度だけ報酬。再訪は報酬なしで保存だけ要求する。</summary>
        [Test]
        public void Arrival_FirstVisitGrantsOnce_RevisitOnlySaves()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            var requests = new List<AutosaveRequest>();
            session.Changes.AutosaveRequested += requests.Add;
            RewardSnapshot reward = Once("reward_p6_arrive_b", 10);

            ArrivalCommit first = session.CommitArrival(AreaB, reward);
            Assert.IsTrue(first.FirstVisit);
            Assert.AreEqual(10, first.GrantedVirtue);
            Assert.AreEqual(10, session.Progress.Virtue);
            Assert.AreEqual(1, requests.Count, "訪問と報酬で保存要求は 1 件にまとめる。");

            ArrivalCommit again = session.CommitArrival(AreaB, reward);
            Assert.IsFalse(again.FirstVisit);
            Assert.AreEqual(0, again.GrantedVirtue);
            Assert.AreEqual(10, session.Progress.Virtue, "再訪で報酬を出さない。");
            Assert.AreEqual(2, requests.Count, "再訪でも到着の保存は要求する（§8 の表）。");
        }

        /// <summary>
        /// 訪問だけが先に記録済みでも（直開き等）、到着報酬の付与は GrantOnce の鍵で 1 回にとどまる。
        /// <b>Load 復元は CommitArrival を呼ばない</b>ので、復元で報酬が出ることはない（P6A-02 で往復を検査）。
        /// </summary>
        [Test]
        public void Arrival_RewardKeyedByRewardId_NotByVisitOrder()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            RewardSnapshot reward = Once("reward_p6_arrive_b", 10);

            session.MarkVisited(AreaB);
            ArrivalCommit commit = session.CommitArrival(AreaB, reward);

            Assert.IsFalse(commit.FirstVisit, "訪問済みなら初到達ではない。");
            Assert.AreEqual(0, session.Progress.Virtue, "初到達でない到着で報酬を出さない。");
        }

        // ---------------------------------------------------------------- 普通敵（P6A 02／03）

        /// <summary>同じ敵種でも配置が違えば 2 体分、同じ配置の重複通知は 1 回分。通常移動では復活しない。</summary>
        [Test]
        public void FieldEnemy_PerPlacementRewards_DuplicateIgnored_NoRevivalOnMove()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            RewardSnapshot kill = Each("reward_enemy_melee", 1);

            Assert.IsTrue(session.TryRecordFieldDefeat(AreaA, PlacementOne, kill, out int g1));
            Assert.IsTrue(session.TryRecordFieldDefeat(AreaA, PlacementTwo, kill, out int g2));
            Assert.IsFalse(session.TryRecordFieldDefeat(AreaA, PlacementOne, kill, out int g3), "同じ配置の重複通知。");
            Assert.AreEqual(1, g1);
            Assert.AreEqual(1, g2);
            Assert.AreEqual(0, g3);
            Assert.AreEqual(2, session.Progress.Virtue);

            // 通常のエリア移動（周期は進まない）。
            session.CommitArrival(AreaB, RewardSnapshot.None);
            session.CommitArrival(AreaA, RewardSnapshot.None);
            Assert.IsTrue(session.TryGetArea(AreaA, out AreaRuntimeState a));
            Assert.IsTrue(a.IsFieldEnemyDefeated(PlacementOne, session.RespawnCycle), "移動では復活しない。");
            Assert.IsTrue(a.IsFieldEnemyDefeated(PlacementTwo, session.RespawnCycle));
        }

        /// <summary>
        /// 周期の更新は普通敵だけを復活させ、恒久記録には触れない（P6A 03／06）。
        /// 未ロードの Area（State は作られているが Scene が無い）にも周期で反映される。
        /// </summary>
        [Test]
        public void RespawnCycle_RevivesFieldEnemiesOnly_PermanentRecordsKept()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            session.Progress.TryGrant(Each("reward_enemy_melee", 50), out _);
            session.Progress.TryPurchaseGrowth(new StableId("growth_p6_hp"), 20);
            session.CommitArrival(AreaA, Once("reward_p6_arrive_a", 0));
            AreaRuntimeState a = session.GetOrCreateArea(AreaA);
            AreaRuntimeState b = session.GetOrCreateArea(AreaB);

            session.TryRecordFieldDefeat(AreaA, PlacementOne, Each("reward_enemy_melee", 1), out _);
            session.CommitEncounterClear(AreaB, EncounterOne, Once("reward_p6_clear_b1", 30), GateFlag);
            session.TryRecordBossDefeat(AreaB, new StableId("boss_p6_c"), RewardSnapshot.None, out _);
            session.TryPickPlacement(AreaB, new StableId("pickup_p6_b_01"), default, 0, 0, Once("reward_p6_pick", 5));
            b.Investigation.TryMarkInvestigated(new StableId("investigate_p6_b_01"));
            session.Recruit(new StableId("companion_inumaru"));

            int virtueBefore = session.Progress.Virtue;
            int cycleBefore = session.RespawnCycle;
            session.AdvanceRespawnCycle();

            Assert.AreEqual(cycleBefore + 1, session.RespawnCycle);
            Assert.IsFalse(a.IsFieldEnemyDefeated(PlacementOne, session.RespawnCycle), "普通敵は復活する。");
            Assert.IsTrue(b.IsEncounterCleared(EncounterOne, session.RespawnCycle), "クリア済み遭遇戦は恒久。");
            Assert.IsTrue(b.IsOpen(GateFlag), "開通は恒久。");
            Assert.IsTrue(b.IsBossDefeated(new StableId("boss_p6_c")), "ボス撃破は恒久。");
            Assert.IsTrue(b.IsPlacementPicked(new StableId("pickup_p6_b_01")), "配置物は恒久。");
            Assert.IsTrue(b.Investigation.IsInvestigated(new StableId("investigate_p6_b_01")), "調査は恒久。");
            Assert.IsTrue(session.HasVisited(AreaA), "訪問は恒久。");
            Assert.IsTrue(session.IsRecruited(new StableId("companion_inumaru")), "加入は恒久。");
            Assert.AreEqual(virtueBefore, session.Progress.Virtue, "徳は変わらない。");
            Assert.AreEqual(20, session.Progress.SpentVirtue, "成長の支出は変わらない。");
            Assert.AreEqual(0, a.FieldDefeatRecordCount, "一致しなくなった撃破記録は捨てる（保存を小さく保つ）。");
        }

        /// <summary>P5 規則の campaign は従来どおり（死亡再開で通常 Encounter が復活する。P5-E21 の前提を壊さない）。</summary>
        [Test]
        public void LegacyPolicy_EncounterClearStillResetsByCycle()
        {
            var session = new GameSessionState();
            Assert.AreEqual(EncounterClearPolicy.PerRespawnCycle, session.EncounterPolicy, "既定は P5 の規則。");
            AreaRuntimeState b = session.GetOrCreateArea(AreaB);

            Assert.IsTrue(b.TryMarkEncounterCleared(EncounterOne, session.RespawnCycle));
            session.AdvanceRespawnCycle();
            Assert.IsFalse(b.IsEncounterCleared(EncounterOne, session.RespawnCycle), "P5 規則では周期で未クリアへ戻る。");
        }

        // ---------------------------------------------------------------- 遭遇戦クリア（P6A 05）

        /// <summary>記録・初回ボーナス・開通を 1 回の更新で確定し、保存要求は 1 件。二度目は何もしない。</summary>
        [Test]
        public void EncounterClear_RecordBonusAndFlagAtomically()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            var requests = new List<AutosaveRequest>();
            bool sawPartial = false;
            session.Changes.AutosaveRequested += r =>
            {
                requests.Add(r);
                // 要求の時点で 3 つが揃っていること（途中の Snapshot を採らせない）。
                session.TryGetArea(AreaB, out AreaRuntimeState area);
                bool cleared = area != null && area.IsEncounterCleared(EncounterOne, session.RespawnCycle);
                bool bonus = session.Progress.HasGranted(new StableId("reward_p6_clear_b1"));
                bool open = area != null && area.IsOpen(GateFlag);
                sawPartial |= !(cleared && bonus && open);
            };

            EncounterClearCommit commit =
                session.CommitEncounterClear(AreaB, EncounterOne, Once("reward_p6_clear_b1", 30), GateFlag);

            Assert.IsTrue(commit.Recorded);
            Assert.AreEqual(RewardGrantResult.Granted, commit.Bonus);
            Assert.AreEqual(30, commit.GrantedVirtue);
            Assert.IsTrue(commit.FlagOpened);
            Assert.AreEqual(1, requests.Count, "保存要求は最後に 1 件だけ。");
            Assert.IsFalse(sawPartial, "要求の時点でクリア・ボーナス・開通が揃っている。");

            EncounterClearCommit again =
                session.CommitEncounterClear(AreaB, EncounterOne, Once("reward_p6_clear_b1", 30), GateFlag);
            Assert.IsFalse(again.Recorded);
            Assert.AreEqual(30, session.Progress.Virtue, "ボーナスの二重取得なし。");
            Assert.AreEqual(1, requests.Count, "何もしなかった確定で保存を要求しない。");
        }

        /// <summary>同じエリアの複数遭遇戦は独立した達成対象（P6A 05）。</summary>
        [Test]
        public void EncounterClear_MultipleEncountersAreIndependent()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);

            session.CommitEncounterClear(AreaB, EncounterOne, Once("reward_p6_clear_b1", 30), default);
            session.TryGetArea(AreaB, out AreaRuntimeState b);

            Assert.IsTrue(b.IsEncounterCleared(EncounterOne, session.RespawnCycle));
            Assert.IsFalse(b.IsEncounterCleared(EncounterTwo, session.RespawnCycle), "もう一方は未クリアのまま。");

            EncounterClearCommit second =
                session.CommitEncounterClear(AreaB, EncounterTwo, Once("reward_p6_clear_b2", 25), default);
            Assert.IsTrue(second.Recorded);
            Assert.AreEqual(55, session.Progress.Virtue, "それぞれの初回ボーナス。");
            Assert.AreEqual(2, b.PermanentClearCount);
        }

        // ---------------------------------------------------------------- 配置物と所持品

        /// <summary>上限を超える取得は全体拒否で未取得のまま。空きができれば再取得できる。徳だけ部分付与しない。</summary>
        [Test]
        public void PlacementPick_OverCapacityRejectsWhole_ThenRetrySucceeds()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            var item = new StableId("item_p6_tonic");
            var placement = new StableId("pickup_p6_b_01");
            RewardSnapshot reward = Once("reward_p6_pick_b1", 5);

            Assert.AreEqual(InventoryChangeResult.Applied, session.Inventory.TryAdd(item, 2, 2));

            PlacementPickResult blocked = session.TryPickPlacement(AreaB, placement, item, 1, 2, reward);
            Assert.AreEqual(PlacementPickResult.OverCapacity, blocked);
            Assert.IsFalse(session.GetOrCreateArea(AreaB).IsPlacementPicked(placement), "未取得のまま残る。");
            Assert.AreEqual(0, session.Progress.Virtue, "複合報酬の徳だけ部分付与しない。");
            Assert.AreEqual(2, session.Inventory.CountOf(item));

            Assert.AreEqual(InventoryChangeResult.Applied, session.Inventory.TryConsume(item, 1));
            Assert.AreEqual(PlacementPickResult.Picked, session.TryPickPlacement(AreaB, placement, item, 1, 2, reward));
            Assert.AreEqual(2, session.Inventory.CountOf(item));
            Assert.AreEqual(5, session.Progress.Virtue);
            Assert.AreEqual(PlacementPickResult.AlreadyPicked,
                session.TryPickPlacement(AreaB, placement, item, 1, 2, reward), "再取得不可。");
        }

        /// <summary>所持品：未知・負数・上限超過・不足を拒否し、何も変えない。</summary>
        [Test]
        public void Inventory_RejectsInvalidChanges()
        {
            var inv = new InventoryState();
            var item = new StableId("item_p6_tonic");

            Assert.AreEqual(InventoryChangeResult.InvalidItem, inv.TryAdd(default, 1, 5));
            Assert.AreEqual(InventoryChangeResult.InvalidCount, inv.TryAdd(item, 0, 5));
            Assert.AreEqual(InventoryChangeResult.InvalidCount, inv.TryAdd(item, -1, 5));
            Assert.AreEqual(InventoryChangeResult.OverCapacity, inv.TryAdd(item, 6, 5));
            Assert.AreEqual(InventoryChangeResult.NotEnough, inv.TryConsume(item, 1));
            Assert.AreEqual(0, inv.CountOf(item));
            Assert.AreEqual(0, inv.KindCount);
        }

        // ---------------------------------------------------------------- 版と保存要求

        /// <summary>論理進行の変更はすべて版を進める（調査・開通・徳・所持品）。</summary>
        [Test]
        public void Revision_AdvancesOnEveryLogicalChange()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            AreaRuntimeState b = session.GetOrCreateArea(AreaB);
            long r = session.Changes.Revision;

            b.Investigation.TryMarkInvestigated(new StableId("investigate_p6_b_01"));
            Assert.Greater(session.Changes.Revision, r, "調査");
            r = session.Changes.Revision;

            b.TryOpen(GateFlag);
            Assert.Greater(session.Changes.Revision, r, "開通");
            r = session.Changes.Revision;

            session.Progress.TryGrant(Each("reward_enemy_melee", 1), out _);
            Assert.Greater(session.Changes.Revision, r, "徳");
            r = session.Changes.Revision;

            session.Inventory.TryAdd(new StableId("item_p6_tonic"), 1, 3);
            Assert.Greater(session.Changes.Revision, r, "所持品");
            r = session.Changes.Revision;

            // 変化しない操作は版を進めない。
            b.TryOpen(GateFlag);
            b.Investigation.TryMarkInvestigated(new StableId("investigate_p6_b_01"));
            Assert.AreEqual(r, session.Changes.Revision, "無変化で版を進めない。");
        }

        // ---------------------------------------------------------------- 本物の遭遇戦と繋ぐ

        /// <summary>
        /// 本物の <see cref="AreaEncounterRunner"/> が最終撃破で <see cref="GameSessionState.CommitEncounterClear"/> を通す（P6A 05）。
        /// 個別撃破の徳（GrantOnce=false）は撃破ごとに、殲滅ボーナスは初回だけ。開通も同時。
        /// </summary>
        [Test]
        public void RealRunner_FinalKillCommitsClearBonusAndFlag()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            Rig rig = MakeRig(session, EncounterOne, Once("reward_p6_clear_b1", 30), GateFlag);

            Assert.IsTrue(rig.Runner.TryStart().Started);
            foreach (FakeEnemy e in rig.Spawner.Enemies)
            {
                e.Defeat();
            }

            rig.Runner.ResolvePending();

            AreaRuntimeState b = session.GetOrCreateArea(AreaB);
            Assert.AreEqual(AreaEncounterState.Cleared, rig.Runner.State);
            Assert.IsTrue(b.IsEncounterCleared(EncounterOne, session.RespawnCycle));
            Assert.IsTrue(b.IsOpen(GateFlag), "開通も同時に確定。");
            Assert.IsTrue(rig.Runner.LastClearCommit.Recorded);
            Assert.AreEqual(2 * 1 + 30, session.Progress.Virtue, "個別撃破 1×2 ＋ 初回ボーナス 30。");
        }

        /// <summary>
        /// 未クリアの再挑戦（死亡・撤退で最初から）では、個別撃破の徳を再び得られる（永久 GrantOnce にしない。仕様 §4）。
        /// </summary>
        [Test]
        public void RealRunner_RetryAfterDefeat_GrantsKillVirtueAgain()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            Rig rig = MakeRig(session, EncounterOne, Once("reward_p6_clear_b1", 30), default);

            Assert.IsTrue(rig.Runner.TryStart().Started);
            rig.Spawner.Enemies[0].Defeat();
            rig.PlayerDefeats.Publish(new PlayerDefeatedEvent(1, Vector3.zero));
            rig.Runner.ResolvePending();
            Assert.AreEqual(AreaEncounterState.Defeated, rig.Runner.State);
            Assert.AreEqual(1, session.Progress.Virtue, "敗北までに得た徳は失わない。");

            // 死亡再開で最初から（ここでは Runner を新しい挑戦へ戻す既存の口が無いので、新しい Runner で再現する）。
            Rig retry = MakeRig(session, EncounterOne, Once("reward_p6_clear_b1", 30), default);
            Assert.IsTrue(retry.Runner.TryStart().Started, "未クリアなので再挑戦できる。");
            foreach (FakeEnemy e in retry.Spawner.Enemies)
            {
                e.Defeat();
            }

            retry.Runner.ResolvePending();
            Assert.AreEqual(1 + 2 + 30, session.Progress.Virtue, "再挑戦の撃破も徳になる。初回ボーナスは 1 回。");
        }

        // ---------------------------------------------------------------- 普通敵の生成役（本物の部品）

        /// <summary>
        /// <see cref="AreaFieldEnemyDirector"/>：撃破済み配置は生成しない。撃破通知は配置 ID で記録と徳を同時に確定する。
        /// 周期が進むと次の入場で作り直す（未ロード Area の反映）。
        /// </summary>
        [Test]
        public void FieldDirector_SpawnsUndefeatedOnly_RecordsByPlacement_RebuildsOnCycle()
        {
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            GameObject prefab = MakeEnemyPrefab();
            var root = new GameObject("FieldDirector");
            _spawned.Add(root);
            AreaFieldEnemyDirector director = root.AddComponent<AreaFieldEnemyDirector>();
            var p1 = new GameObject("P1").transform;
            var p2 = new GameObject("P2").transform;
            _spawned.Add(p1.gameObject);
            _spawned.Add(p2.gameObject);
            var enemyId = new StableId("enemy_melee_prototype");
            director.Bind(AreaA, null,
                new[] { new EnemyPrefabTable.Entry { EnemyId = enemyId, Prefab = prefab } },
                new[]
                {
                    new AreaFieldEnemyDirector.Placement { PlacementId = PlacementOne, EnemyId = enemyId, Point = p1 },
                    new AreaFieldEnemyDirector.Placement { PlacementId = PlacementTwo, EnemyId = enemyId, Point = p2 },
                });
            director.BindSession(() => session);

            director.PrepareForEntry();
            Assert.AreEqual(2, director.SpawnedCount);
            Assert.IsFalse(director.SpawnedActive, "準備の段では起こさない。");

            // 実チャネルから撃破を流す（同じ個体を 2 回）。
            var actor = director.Spawned[0].GetComponentInChildren<Momotaro.Gameplay.Enemy.EnemyActor>(true);
            var request = new EnemyRewardRequest(actor.DamageableId, EnemyRole.Melee, NewReward("reward_enemy_melee", 1), Vector3.zero);
            actor.Defeats.Publish(new EnemyDefeatedEvent(actor.DamageableId, request));
            actor.Defeats.Publish(new EnemyDefeatedEvent(actor.DamageableId, request));
            Assert.AreEqual(1, director.RecordedDefeatCount);
            Assert.AreEqual(1, director.IgnoredDefeatCount, "同じ配置の重複通知。");
            Assert.AreEqual(1, session.Progress.Virtue);

            // 移動して戻る（周期は同じ）：生き残りを残し、撃破済みは作らない。
            director.PrepareForEntry();
            Assert.AreEqual(2, director.SpawnedCount, "周期が同じなら作り直さない（生き残りも撃破済みの亡骸もそのまま）。");

            // 留守のあいだに休息した。
            session.AdvanceRespawnCycle();
            director.PrepareForEntry();
            Assert.AreEqual(2, director.SpawnedCount, "周期が進んだので撃破済みも復活する。");
            Assert.AreEqual(session.RespawnCycle, director.BuiltCycle);

            // 撃破してから別の周期を待たずに入り直すと、撃破済みは作らない。
            var actor2 = director.Spawned[1].GetComponentInChildren<Momotaro.Gameplay.Enemy.EnemyActor>(true);
            actor2.Defeats.Publish(new EnemyDefeatedEvent(actor2.DamageableId,
                new EnemyRewardRequest(actor2.DamageableId, EnemyRole.Melee, null, Vector3.zero)));
            director.RebuildNow();
            Assert.AreEqual(1, director.SpawnedCount, "現在周期で撃破済みの配置は生成しない。");
        }

        // ================================================================ 道具

        private RewardData NewReward(string id, int virtue)
        {
            var reward = ScriptableObject.CreateInstance<RewardData>();
            _spawned.Add(reward);
            SetPrivate(reward, "_id", new StableId(id));
            SetPrivate(reward, "_virtueAmount", virtue);
            SetPrivate(reward, "_grantOnce", false);
            return reward;
        }

        private GameObject MakeEnemyPrefab()
        {
            // EditMode では Instantiate で Awake が走らないので、EnemyActor の Runtime 構築に依存しない。
            var go = new GameObject("EnemyPrefab");
            go.SetActive(false);
            go.AddComponent<Momotaro.Gameplay.Enemy.EnemyActor>();
            _spawned.Add(go);
            return go;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
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
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            m?.Invoke(target, null);
        }

        private sealed class Rig
        {
            public AreaEncounterRunner Runner;
            public FakeSpawner Spawner;
            public PlayerDefeatChannel PlayerDefeats;
        }

        private Rig MakeRig(GameSessionState session5, StableId encounterId, RewardSnapshot bonus, StableId flag)
        {
            var encounter = ScriptableObject.CreateInstance<EncounterData>();
            _spawned.Add(encounter);
            SetPrivate(encounter, "_id", encounterId);
            SetPrivate(encounter, "_enemyIds", new List<StableId>
            {
                new StableId("enemy_melee_prototype"), new StableId("enemy_melee_prototype"),
            });

            if (bonus.HasReward)
            {
                var clear = ScriptableObject.CreateInstance<RewardData>();
                _spawned.Add(clear);
                SetPrivate(clear, "_id", bonus.RewardId);
                SetPrivate(clear, "_virtueAmount", bonus.VirtueAmount);
                SetPrivate(clear, "_grantOnce", true);
                SetPrivate(encounter, "_clearReward", clear);
            }

            SetPrivate(encounter, "_unlockFlagId", flag);

            var go = new GameObject("P6AEncounter");
            _spawned.Add(go);
            CombatSessionController combat = go.AddComponent<CombatSessionController>();
            var holderGo = new GameObject("Progress");
            _spawned.Add(holderGo);
            PlayerProgressHolder holder = holderGo.AddComponent<PlayerProgressHolder>();
            holder.Bind(session5.Progress);
            CombatRewardCollector rewards = go.AddComponent<CombatRewardCollector>();
            rewards.Bind(combat, holder);
            InvokePrivate(rewards, "OnEnable");

            RewardData kill = NewReward("reward_enemy_melee", 1);
            var spawner = new FakeSpawner { Session = combat, Reward = kill };
            var defeats = new PlayerDefeatChannel();
            AreaRuntimeState area = session5.GetOrCreateArea(AreaB);

            var modes = new GameModeService(GameMode.Exploration);
            GameModeProvider.Current = modes;

            AreaEncounterRunner runner = go.AddComponent<AreaEncounterRunner>();
            runner.Bind(encounter, combat, new Conditions(), spawner, new Arena(), null,
                () => area, () => session5.RespawnCycle);
            runner.BindSession(() => area, () => session5.RespawnCycle, () => session5);
            runner.BindPlayerDefeat(defeats);
            combat.BindPlayerDefeat(defeats);
            InvokePrivate(runner, "OnEnable");
            return new Rig { Runner = runner, Spawner = spawner, PlayerDefeats = defeats };
        }

        private sealed class Conditions : IAreaEncounterConditions
        {
            public bool IsAreaReady => true;
            public bool IsExploration => true;
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
            private static int _next = 7000;

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

            public event Action SpawnedActivated;

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
