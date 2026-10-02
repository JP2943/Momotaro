using System.Collections;
using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.Save;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Combat;
using Momotaro.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P6A の検証 campaign（A–B–C）を<b>実 Scene・実入力・実ファイル</b>で通す（P6 仕様 §11・§13。工程 P6A-06）。
    ///
    /// EditMode の部品テストが緑でも、繋ぎ目（New Game → 到着の確定 → 自動保存 → 再起動 → Continue → 候補の採用）が
    /// 切れていれば実機は動かない（CLAUDE.md）。ここは<b>本物同士を繋いだ</b>経路だけを見る：
    /// <list type="bullet">
    /// <item>タイトルの手順（<see cref="CampaignAdventureFlow"/>）で New Game／Continue する。</item>
    /// <item>保存は実ファイル（テスト用の一時ディレクトリ）へ、実スレッドの書込担当で書く。</item>
    /// <item>「再起動」は常駐（BootstrapRoot）と static を捨てて作り直す。Editor の static 残留に頼らない
    /// （別プロセスでの確認は実ビルドで別に行う。P6A 23）。</item>
    /// <item>敵は実 Hitbox で倒し、お地蔵様は実キー E で調べ、境界は実キーで渡る。</item>
    /// </list>
    /// </summary>
    public sealed class P6AWorldPlayTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Tests/Phase6A/SO_AreaCatalog_P6A.asset";
        private const string AreaAScene = "Assets/_Project/Scenes/Tests/Phase6A/SCN_Phase6A_AreaA.unity";

        private static readonly StableId AreaA = new StableId("area_p6_a");
        private static readonly StableId AreaB = new StableId("area_p6_b");
        private static readonly StableId AreaC = new StableId("area_p6_c");
        private static readonly StableId ShrineA = new StableId("shrine_p6_a");
        private static readonly StableId ShrineC = new StableId("shrine_p6_c");
        private static readonly StableId EntryAShrine = new StableId("entry_p6_a_shrine");
        private static readonly StableId EntryCShrine = new StableId("entry_p6_c_shrine");
        private static readonly StableId ExitAEast = new StableId("exit_p6_a_east");
        private static readonly StableId ExitBWest = new StableId("exit_p6_b_west");
        private static readonly StableId ExitBEast = new StableId("exit_p6_b_east");
        private static readonly StableId ExitCWest = new StableId("exit_p6_c_west");
        private static readonly StableId EncounterBNorth = new StableId("encounter_p6_b_north");
        private static readonly StableId EncounterBSouth = new StableId("encounter_p6_b_south");
        private static readonly StableId FlagBCache = new StableId("flag_p6_b_cache");
        private static readonly StableId FieldA1 = new StableId("field_p6_a_01");
        private static readonly StableId FieldA2 = new StableId("field_p6_a_02");

        // 検証用の数値（Phase6TrialValues と同じ。Data が正本だが、期待値は仕様 §11 の仮報酬例から採る）。
        private const int ArrivalB = 10;
        private const int ArrivalC = 10;
        private const int Kill = 1;
        private const int EncounterClear = 30;

        private GameObject _bootstrap;
        private Keyboard _keyboard;
        private Gamepad _pad;
        private string _saveDir;
        private int _expectedSlides;

        [SetUp]
        public void SetUp()
        {
            P55ResidentRig.Reset();
            RemoveStrayDevices();
            ClearStatics();
            AreaPendingArrival.ResetDiagnostics();
            _saveDir = Path.Combine(Application.temporaryCachePath, "p6a_play_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            CampaignSaveService.TestDirectoryOverride = _saveDir;
            _expectedSlides = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            yield return SceneManager.LoadSceneAsync(AreaAScene, LoadSceneMode.Single);
            yield return null;
            DestroyBootstrap();
            P55ResidentRig.Reset();
            ClearStatics();
            RemoveDevices();
            CampaignSaveService.TestDirectoryOverride = null;
            yield return null;

            // 一時ディレクトリを片付ける（ロックはサービスの破棄で外れている）。
            try
            {
                if (Directory.Exists(_saveDir))
                {
                    Directory.Delete(_saveDir, true);
                }
            }
            catch (IOException)
            {
                // 片付けの失敗は検証結果に影響しない。
            }
        }

        // ================================================================ 1. New Game → 進行 → 再起動 → Continue

        /// <summary>
        /// P6A 01／02／07／09／13／23（同一プロセス版）：New Game から A・B を行き来し、普通敵を倒し、お地蔵様を調べ、
        /// <b>常駐と static を捨てて作り直してから</b> Continue する。
        /// </summary>
        [UnityTest]
        public IEnumerator NewGame_Progress_Restart_Continue_RestoresEverything()
        {
            yield return NewGame();
            GameSessionState session = Session();
            AreaTransitionService transitions = Transitions();

            Assert.IsFalse(string.IsNullOrEmpty(session.AdventureId), "New Game で冒険 ID が付く。");
            Assert.AreEqual(ShrineA.Value, session.Checkpoint.Value, "初期お地蔵様が死亡再開点。");
            Assert.AreEqual(3, session.Kibidango, "きびだんごは上限まで。");
            Assert.AreEqual(0, session.Progress.Virtue, "A の初到達報酬は 0（検証値）。");
            yield return WaitSaved("New Game の到着");

            // ---- P6A 02：普通敵 2 体（同じ敵種・別の配置 ID）。1 体倒すと 1 体ぶんだけ ----
            AreaFieldEnemyDirector field = Object.FindFirstObjectByType<AreaFieldEnemyDirector>();
            Assert.IsNotNull(field, "A に普通敵の配置役がある。");
            Assert.AreEqual(2, AliveFieldEnemies(field), "普通敵が 2 体湧いている。");
            EnemyActor first = field.Spawned[0].GetComponentInChildren<EnemyActor>();
            yield return KillWithRealHitbox(first);
            yield return WaitUntilOrTimeout(() => session.Progress.Virtue >= Kill, 3f);
            Assert.AreEqual(Kill, session.Progress.Virtue, "個別撃破の徳は 1 体ぶん（重複通知でも 1 回）。");
            Assert.AreEqual(1, AliveFieldEnemies(field), "倒していない 1 体は残る。");
            Assert.IsTrue(session.TryGetArea(AreaA, out AreaRuntimeState areaA));
            Assert.AreEqual(1, areaA.FieldDefeatRecordCount, "撃破は配置 ID で 1 件。");

            // ---- P6A 01：A → B（初到達 +10）→ A → B（増えない） ----
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            Assert.AreEqual(Kill + ArrivalB, session.Progress.Virtue, "B の初到達は Commit で 1 回。");
            yield return SlideTo(ExitBWest, Key.A, Vector3.right, "B→A");
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B 再訪");
            Assert.AreEqual(Kill + ArrivalB, session.Progress.Virtue, "再訪で初到達報酬は増えない。");
            yield return SlideTo(ExitBWest, Key.A, Vector3.right, "B→A 再訪");

            // 通常移動では普通敵は復活しない（P6A 02）。
            field = Object.FindFirstObjectByType<AreaFieldEnemyDirector>();
            Assert.AreEqual(1, AliveFieldEnemies(field), "通常移動で倒した普通敵は戻らない。");

            // ---- P6A 07：調べるだけで登録と保存。回復なし ----
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            int max = vitals.Vitals.Health.Max;
            vitals.Vitals.Health.SetCurrent(max - 7);
            long revisionBefore = session.Changes.Revision;
            yield return InteractShrine(ShrineA);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            Assert.IsTrue(shrines.IsMenuOpen, "調べるとメニューが開く。");
            Assert.AreEqual(max - 7, vitals.Vitals.Health.Current, "調べただけでは回復しない（仕様 §5）。");
            Assert.AreEqual(0, shrines.RestCount, "休息していない。");
            Assert.Greater(session.Changes.Revision, revisionBefore, "登録で版が進む。");
            Assert.AreEqual(ResumeAnchorKind.Shrine, session.Resume.Kind, "中断位置がお地蔵様になる（P6A 09）。");
            shrines.Close();
            yield return null;
            yield return WaitSaved("登録");

            // ---- P6A 16：犬丸の Down と復帰待ちを保存し、Load で無料回復しない ----
            CompanionHitReceiver dog = Object.FindFirstObjectByType<CompanionHitReceiver>();
            Assert.IsNotNull(dog, "犬丸が居る。");
            yield return KnockDown(dog);
            float recoveryAtSave = dog.Vitals.RecoveryRemaining;
            Assert.Greater(recoveryAtSave, 0f, "前提：復帰待ちが残っている。");
            session.Changes.RequestAutosave("test_companion_down");
            yield return null;
            yield return WaitSaved("犬丸 Down");

            // P6A 09：報酬取得は復帰位置を上書きしない。
            ResumeAnchor resumeAfterRegister = session.Resume;
            StableId checkpointAfterRegister = session.Checkpoint;

            int virtue = session.Progress.Virtue;
            int total = session.Progress.TotalVirtue;
            long savedRevision = Saves().Coordinator.SavedRevision;
            Assert.AreEqual(session.Changes.Revision, savedRevision, "最新の版まで保存済み。");

            // ================================ 再起動（常駐と static を捨てる）================================
            yield return Restart();

            CampaignAdventureFlow flow = Saves().Flow;
            bool continued = false;
            string failure = null;
            flow.Continued += () => continued = true;
            flow.ContinueFailed += reason => failure = reason;
            Assert.IsTrue(flow.TryContinue(Catalog(), out string error), "Continue を受理する。理由=" + error);
            yield return WaitUntilOrTimeout(() => continued || failure != null, 30f);
            Assert.IsNull(failure, "Continue が失敗した: " + failure);
            Assert.IsTrue(continued, "Continue が採用まで済む。");
            yield return WaitAreaReady(AreaA);

            GameSessionState restored = Session();
            Assert.AreNotSame(session, restored, "別の Session（作り直した）。");
            Assert.AreEqual(virtue, restored.Progress.Virtue, "徳が戻る。");
            Assert.AreEqual(total, restored.Progress.TotalVirtue, "累計も戻る。");
            Assert.IsTrue(restored.HasVisited(AreaB), "訪問済みが戻る。");
            Assert.IsTrue(restored.IsShrineRegistered(ShrineA), "登録が戻る。");
            Assert.AreEqual(checkpointAfterRegister.Value, restored.Checkpoint.Value, "死亡再開点が戻る。");
            Assert.AreEqual(resumeAfterRegister, restored.Resume, "中断位置が戻る。");
            Assert.AreEqual(3, restored.Kibidango, "きびだんごの残数が戻る。");
            Assert.AreEqual(max - 7, Object.FindFirstObjectByType<PlayerVitalsHolder>().Vitals.Health.Current,
                "HP は保存値（Load で無料回復しない。P6A 16）。");
            Assert.AreEqual(savedRevision, Saves().Coordinator.SavedRevision, "読み込んだ版が保存済み扱い。");
            Assert.IsFalse(Saves().Coordinator.IsDirty, "Continue だけでは未保存にならない（Load で報酬・保存要求を出さない）。");

            CompanionHitReceiver dogAfter = Object.FindFirstObjectByType<CompanionHitReceiver>();
            Assert.IsTrue(dogAfter.Vitals.IsDown, "犬丸は Down のまま再開する（Load で無料回復しない）。");
            Assert.AreEqual(0, dogAfter.CurrentHp, "犬丸の HP は保存値。");
            Assert.Greater(dogAfter.Vitals.RecoveryRemaining, 0f, "復帰待ちが残っている。");
            Assert.LessOrEqual(dogAfter.Vitals.RecoveryRemaining, recoveryAtSave + 0.01f, "復帰待ちは保存値以下（巻き戻らない）。");
            yield return WaitUntilOrTimeout(() => !dogAfter.Vitals.IsDown, recoveryAtSave + 3f);
            Assert.IsFalse(dogAfter.Vitals.IsDown, "残り時間が過ぎれば通常どおり復帰する。");

            AreaFieldEnemyDirector fieldAfter = Object.FindFirstObjectByType<AreaFieldEnemyDirector>();
            Assert.AreEqual(1, AliveFieldEnemies(fieldAfter), "倒した普通敵は Continue でも戻らない。");
            Assert.IsTrue(restored.TryGetArea(AreaA, out AreaRuntimeState restoredA));
            Assert.IsTrue(restoredA.IsFieldEnemyDefeated(FieldA1, restored.RespawnCycle)
                          || restoredA.IsFieldEnemyDefeated(FieldA2, restored.RespawnCycle), "撃破記録が戻る。");

            Vector3 at = Object.FindFirstObjectByType<PlayerRoot>().transform.position;
            Vector3 entry = FindEntry(EntryAShrine).ArrivalPosition;
            Assert.Less(Flat(at, entry), 2.5f, "お地蔵様の入口から再開する（到着=" + at + " 入口=" + entry + "）。");
        }

        // ================================================================ 2. 戦闘中の保存・撤退・再挑戦・クリア

        /// <summary>
        /// P6A 04／05／15：遭遇戦の途中で撃破報酬が保存され、そこから Continue すると<b>安全な入口</b>から、
        /// 遭遇戦は未クリアで始まる。撤退で徳は残り、再挑戦は最初から。クリアで撃破・ボーナス・開通が同じ保存に載る。
        /// </summary>
        [UnityTest]
        public IEnumerator Encounter_SaveMidFight_Continue_Retreat_Rechallenge_ClearCommitsAtomically()
        {
            yield return NewGame();
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            GameSessionState session = Session();
            Assert.AreEqual(ArrivalB, session.Progress.Virtue);

            // ---- 戦闘開始 → 1 体倒す → 保存（戦闘中の投影） ----
            AreaEncounterRunner north = FindRunner(EncounterBNorth);
            yield return StartEncounter(north);
            EnemyActor[] enemies = EnemiesOf(north);
            Assert.AreEqual(2, enemies.Length, "北の遭遇戦は 2 体。");
            yield return KillWithRealHitbox(enemies[0]);
            yield return WaitUntilOrTimeout(() => session.Progress.Virtue >= ArrivalB + Kill, 3f);
            Assert.AreEqual(ArrivalB + Kill, session.Progress.Virtue, "撃破の徳は戦闘中でも入る。");
            Assert.AreEqual(AreaEncounterState.Playing, north.State, "まだ戦闘中。");
            yield return WaitSaved("戦闘中の撃破");

            // ================================ 再起動 → Continue（P6A 15）================================
            yield return Restart();
            yield return ContinueAndWait(AreaB);
            session = Session();
            Assert.AreEqual(ArrivalB + Kill, session.Progress.Virtue, "戦闘中に得た徳は保持。");
            Assert.IsTrue(session.TryGetArea(AreaB, out AreaRuntimeState areaB));
            Assert.IsFalse(areaB.IsEncounterCleared(EncounterBNorth, session.RespawnCycle), "遭遇戦は未クリア。");
            north = FindRunner(EncounterBNorth);
            Assert.AreEqual(AreaEncounterState.Dormant, north.State, "遭遇戦は始まっていない状態から。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "探索で再開。");
            Assert.AreEqual(0, CountLiveEnemies(), "敵は湧いていない（Wave は最初から）。");
            Vector3 at = Object.FindFirstObjectByType<PlayerRoot>().transform.position;
            foreach (AreaArenaBoundary arena in Object.FindObjectsByType<AreaArenaBoundary>(FindObjectsSortMode.None))
            {
                Assert.IsFalse(arena.SafeBounds.Contains(new Vector3(at.x, arena.SafeBounds.center.y, at.z)),
                    "再開位置は戦闘区域の外（安全な入口）。到着=" + at);
            }

            // ---- 撤退（P6A 04）：戦闘中に出入口から A へ ----
            yield return StartEncounter(north);
            enemies = EnemiesOf(north);
            yield return KillWithRealHitbox(enemies[0]);
            yield return WaitUntilOrTimeout(() => session.Progress.Virtue >= ArrivalB + Kill * 2, 3f);
            Assert.AreEqual(ArrivalB + Kill * 2, session.Progress.Virtue, "再挑戦の撃破も徳になる。");
            Assert.IsTrue(north.AllowsRetreatNow, "戦闘中でも撤退できる遭遇戦。");
            _expectedSlides = Transitions().SlideCommittedCount;
            yield return SlideTo(ExitBWest, Key.A, Vector3.right, "撤退 B→A");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "撤退後は探索。");
            Assert.AreEqual(ArrivalB + Kill * 2, session.Progress.Virtue, "撤退で徳は失わない。");

            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "撤退後に B へ");
            north = FindRunner(EncounterBNorth);
            Assert.AreEqual(AreaEncounterState.Dormant, north.State, "戻ると挑戦は捨てられ、最初から（Wave1）。");
            Assert.GreaterOrEqual(north.AbandonedCount, 1, "撤退の確定が記録される。");

            // ---- クリア（P6A 05）：最終撃破・ボーナス・開通を同じ保存へ ----
            yield return StartEncounter(north);
            foreach (EnemyActor e in EnemiesOf(north))
            {
                yield return KillWithRealHitbox(e);
            }

            yield return WaitUntilOrTimeout(() => north.State == AreaEncounterState.Cleared, 8f);
            Assert.AreEqual(AreaEncounterState.Cleared, north.State, "殲滅でクリア。");
            int expected = ArrivalB + Kill * 4 + EncounterClear;
            Assert.AreEqual(expected, session.Progress.Virtue, "撃破 4 回ぶん + 初回ボーナス 30。");
            Assert.IsTrue(session.TryGetArea(AreaB, out areaB));
            Assert.IsTrue(areaB.IsOpen(FlagBCache), "クリアで小部屋が開通。");
            yield return WaitSaved("クリア");

            // 保存ファイルの中身で同時性を見る（同じ 1 世代に全部載っている）。
            SaveLoadDecision decision = Saves().Coordinator.Store.DecideLoad();
            Assert.IsTrue(decision.CanLoad, "保存が読める。");
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot snap, out string err), err);
            Assert.AreEqual(expected, snap.TotalVirtue - snap.SpentVirtue, "保存の徳。");
            AreaSaveRecord bRecord = FindAreaRecord(snap, AreaB);
            CollectionAssert.Contains(bRecord.ClearedEncounters, EncounterBNorth.Value, "クリアの記録。");
            CollectionAssert.Contains(bRecord.OpenedFlags, FlagBCache.Value, "開通の記録。");

            // 南は独立（P6A 05）。
            Assert.IsFalse(session.TryGetArea(AreaB, out areaB) && areaB.IsEncounterCleared(EncounterBSouth, session.RespawnCycle),
                "南の遭遇戦は未クリアのまま。");
        }

        // ================================================================ 3. A–B–C・旅立ち・死亡・休息の周期

        /// <summary>
        /// P6A 03／08／11／12：A–B–C をスライドで渡り（在留は常に 2 以下）、C のお地蔵様を登録し、A から旅立ちで C へ。
        /// 到着の確定後にだけ休息（周期 +1・全回復・補充）。C で倒れると最後に登録したお地蔵様へ全回復で戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator ABC_FastTravel_Death_EachAdvancesCycleOnce()
        {
            yield return NewGame();
            GameSessionState session = Session();
            AreaTransitionService transitions = Transitions();
            int maxScenes = 0;
            int maxResident = 0;
            System.Action sample = () =>
            {
                maxScenes = Mathf.Max(maxScenes, SceneManager.sceneCount);
                maxResident = Mathf.Max(maxResident, transitions.Slide.Residency.ResidentCount);
            };

            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B", sample);
            yield return SlideTo(ExitBEast, Key.D, Vector3.left, "B→C", sample);
            Assert.AreEqual(ArrivalB + ArrivalC, session.Progress.Virtue, "B・C の初到達。");
            Assert.LessOrEqual(maxScenes, 2, "A–B–C の移動で Scene は 2 枚まで（最大 2 在留）。");
            Assert.LessOrEqual(maxResident, 2, "在留台帳も 2 まで。");

            // C のお地蔵様を登録。
            yield return InteractShrine(ShrineC);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            Assert.IsTrue(session.IsShrineRegistered(ShrineC));
            Assert.AreEqual(ShrineC.Value, session.Checkpoint.Value, "最後に調べたお地蔵様が死亡再開点。");
            shrines.Close();
            yield return null;

            // A へ戻り、普通敵を 1 体倒す（周期 0 の撃破）。
            yield return SlideTo(ExitCWest, Key.A, Vector3.right, "C→B", sample);
            yield return SlideTo(ExitBWest, Key.A, Vector3.right, "B→A", sample);
            Assert.LessOrEqual(maxScenes, 2, "戻りも 2 枚まで。");
            AreaFieldEnemyDirector field = Object.FindFirstObjectByType<AreaFieldEnemyDirector>();
            yield return KillWithRealHitbox(field.Spawned[0].GetComponentInChildren<EnemyActor>());
            Assert.AreEqual(1, AliveFieldEnemies(field));

            // ---- 旅立ち A → C（P6A 11／12）----
            int cycle = session.RespawnCycle;
            int virtue = session.Progress.Virtue;
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            vitals.Vitals.Health.SetCurrent(vitals.Vitals.Health.Max - 9);
            Assert.IsTrue(session.TryConsumeKibidango(1), "前提：きびだんごを 1 つ使う。");
            yield return InteractShrine(ShrineA);
            Assert.AreEqual(ShrineMenuResult.FastTravelStarted, shrines.FastTravel(ShrineC),
                "旅立ちを受理する。拒否=" + shrines.LastTravelRejection + " " + shrines.Message);
            Assert.AreEqual(cycle, session.RespawnCycle, "受理しただけでは周期は進まない（到着の確定後）。");
            yield return WaitUntilOrTimeout(() => shrines.FastTravelCompletedCount >= 1 || shrines.FastTravelFailedCount > 0, 30f);
            Assert.AreEqual(1, shrines.FastTravelCompletedCount, "旅立ちが着く。失敗=" + shrines.FastTravelFailedCount);
            yield return WaitAreaReady(AreaC);
            Assert.AreEqual(cycle + 1, session.RespawnCycle, "旅立ちで周期が 1 つだけ進む。");
            Assert.AreEqual(3, session.Kibidango, "きびだんごを補充。");
            vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.AreEqual(vitals.Vitals.Health.Max, vitals.Vitals.Health.Current, "到着後に全回復。");
            Assert.AreEqual(virtue, session.Progress.Virtue, "徳は変わらない。");
            Assert.AreEqual(1, SceneManager.sceneCount, "旅立ちは Single（多重 Scene 操作なし）。");
            Assert.Less(Flat(Object.FindFirstObjectByType<PlayerRoot>().transform.position,
                FindEntry(EntryCShrine).ArrivalPosition), 2.5f, "C のお地蔵様の前に着く。");
            yield return WaitSaved("旅立ち");

            // ---- 死亡（P6A 08）：最後に登録したお地蔵様へ全回復で ----
            int cycleBeforeDeath = session.RespawnCycle;
            int completedBefore = transitions.CompletedCount;
            yield return KillPlayerWithRealHits();
            yield return WaitForRespawnPrompt();
            yield return PressKeyUntil(Key.Enter,
                () => transitions.CompletedCount > completedBefore || transitions.HasTerminalFailure, 25f);
            Assert.IsFalse(transitions.HasTerminalFailure, "終端失敗しない: " + transitions.TerminalFailureReason);
            yield return WaitAreaReady(AreaC);
            Assert.AreEqual(cycleBeforeDeath + 1, session.RespawnCycle, "死亡で周期が 1 つだけ進む。");
            Assert.AreEqual(virtue, session.Progress.Virtue, "徳は死んでも減らない。");
            Assert.IsTrue(session.IsShrineRegistered(ShrineA) && session.IsShrineRegistered(ShrineC), "登録は残る。");
            vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.AreEqual(vitals.Vitals.Health.Max, vitals.Vitals.Health.Current, "全回復で戻る。");
            Assert.Less(Flat(Object.FindFirstObjectByType<PlayerRoot>().transform.position,
                FindEntry(EntryCShrine).ArrivalPosition), 2.5f, "C のお地蔵様へ戻る。");
            yield return WaitSaved("死亡再開");

            // ---- 過去エリアへの反映：A の普通敵は周期で戻る ----
            yield return InteractShrine(ShrineC);
            Assert.AreEqual(ShrineMenuResult.FastTravelStarted, shrines.FastTravel(ShrineA));
            yield return WaitUntilOrTimeout(() => shrines.FastTravelCompletedCount >= 2, 30f);
            yield return WaitAreaReady(AreaA);
            field = Object.FindFirstObjectByType<AreaFieldEnemyDirector>();
            yield return WaitUntilOrTimeout(() => AliveFieldEnemies(field) == 2, 3f);
            Assert.AreEqual(2, AliveFieldEnemies(field), "周期が進んだので未ロードだった A の普通敵も戻っている。");
        }

        // ================================================================ 4. Snapshot は攻撃を乱さない

        /// <summary>P6A 14：攻撃の最中に保存を採っても、攻撃・HP・スタミナ・入力を変えない。</summary>
        [UnityTest]
        public IEnumerator Capture_DuringAttack_DoesNotDisturbThePlayer()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            QuietFieldEnemies();
            var player = Object.FindFirstObjectByType<PlayerStateController>();
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.IsTrue(Transitions().TryGetActiveTransferPort(out AreaActorTransferPort port), "転送の窓口がある。");

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.J));
            yield return WaitUntilOrTimeout(() => player.Current == PlayerState.Attack, 2f);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.AreEqual(PlayerState.Attack, player.Current, "前提：攻撃中。");

            int hp = vitals.Vitals.Health.Current;
            int stamina = vitals.Vitals.Stamina.Current;
            PartySaveValues values = port.ExportForSave();
            Assert.AreEqual(PlayerState.Attack, player.Current, "採取しても攻撃は中断しない。");
            Assert.AreEqual(hp, vitals.Vitals.Health.Current, "HP を変えない。");
            Assert.AreEqual(stamina, vitals.Vitals.Stamina.Current, "スタミナを変えない。");
            Assert.AreEqual(hp, values.Player.Hp, "採った値は今の値。");

            // 実際の保存（LateUpdate の採取）を攻撃中に通す。
            Session().Changes.RequestAutosave("test_mid_attack");
            int submits = Saves().Coordinator.SubmitCount;
            yield return null;
            Assert.Greater(Saves().Coordinator.SubmitCount, submits, "攻撃中でも採取される（止めない）。");
            Assert.AreEqual(hp, vitals.Vitals.Health.Current, "保存で HP は変わらない。");
            yield return WaitSaved("攻撃中の保存");
        }

        // ================================================================ 7. 失敗した遷移・旅立ちは何も確定しない

        /// <summary>
        /// P6A 01／11／12：読込に失敗した移動では初到達の徳も訪問も付かない。旅立ちが読込に失敗したら、
        /// 休息（周期・回復・補充）も登録も保存も起きず、元の場所で操作に戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator FailedTravelAndFastTravel_CommitNothing()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return WaitSaved("New Game");
            GameSessionState session = Session();
            AreaTransitionService transitions = Transitions();
            int virtue = session.Progress.Virtue;

            // ---- 通常の移動（Single）が失敗：B の初到達は付かない ----
            transitions.Loader = new FailingLoader();
            AreaTransitionDecision decision = transitions.TryTravel(AreaB, new StableId("area_p5_b_from_a"));
            Assert.IsTrue(decision.Accepted, "受付条件は満たしているので受理される。拒否=" + decision.Rejection);
            yield return WaitUntilOrTimeout(() => !transitions.IsTransitionUnsettled, 10f);
            Assert.AreEqual(virtue, session.Progress.Virtue, "失敗した移動で初到達の徳は付かない。");
            Assert.IsFalse(session.HasVisited(AreaB), "訪問済みにもならない。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A に留まる。");

            // ---- 旅立ち（Fade）が失敗：休息も登録も起きない ----
            transitions.Loader = new UnitySceneLoader();
            yield return WaitUntilOrTimeout(() => GameModeProvider.Current.Current == GameMode.Exploration, 5f);
            Assert.IsTrue(Transitions().Catalog.Campaign.TryGetShrine(ShrineC, out ShrineInfo shrineC));
            session.RegisterShrine(shrineC); // 行き先の前提（登録そのものは別の検査で見る）。
            session.RegisterShrine(Transitions().Catalog.Campaign.TryGetShrine(ShrineA, out ShrineInfo shrineA) ? shrineA : default);
            yield return WaitSaved("前提の登録");

            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            int hp = vitals.Vitals.Health.Max - 6;
            vitals.Vitals.Health.SetCurrent(hp);
            Assert.IsTrue(session.TryConsumeKibidango(1));
            int kibidango = session.Kibidango;
            int cycle = session.RespawnCycle;
            StableId checkpoint = session.Checkpoint;
            long revision = session.Changes.Revision;

            yield return InteractShrine(ShrineA);
            revision = session.Changes.Revision; // 調べた登録のぶん（調べること自体は保存してよい）。
            checkpoint = session.Checkpoint;
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            transitions.Loader = new FailingLoader();
            Assert.AreEqual(ShrineMenuResult.FastTravelStarted, shrines.FastTravel(ShrineC),
                "旅立ちを受理する。拒否=" + shrines.LastTravelRejection + " " + shrines.Message);
            yield return WaitUntilOrTimeout(() => shrines.FastTravelFailedCount > 0 || shrines.FastTravelCompletedCount > 0, 15f);
            transitions.Loader = new UnitySceneLoader();

            Assert.AreEqual(1, shrines.FastTravelFailedCount, "旅立ちの失敗を数える。");
            Assert.AreEqual(0, shrines.FastTravelCompletedCount, "着いたことにしない。");
            Assert.AreEqual(cycle, session.RespawnCycle, "周期は進まない（休息していない）。");
            Assert.AreEqual(kibidango, session.Kibidango, "きびだんごは補充されない。");
            Assert.AreEqual(hp, Object.FindFirstObjectByType<PlayerVitalsHolder>().Vitals.Health.Current, "回復しない。");
            Assert.AreEqual(checkpoint.Value, session.Checkpoint.Value, "死亡再開点は変わらない（行き先を登録しない）。");
            Assert.AreEqual(revision, session.Changes.Revision, "進行は 1 つも変わらない。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "元の場所に居る。");
            yield return WaitUntilOrTimeout(() => GameModeProvider.Current.Current == GameMode.Exploration, 5f);
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "操作に戻る。");
        }

        /// <summary>読込を開始できない Loader（P5 の検査と同じ契約）。</summary>
        private sealed class FailingLoader : IAreaSceneLoader
        {
            public IAreaLoadOperation Load(string scenePath) => new FailedOperation();

            private sealed class FailedOperation : IAreaLoadOperation
            {
                public bool IsDone => true;
                public bool HasError => true;
            }
        }

        // ================================================================ 6. 攻撃 VFX（P6A 27）

        /// <summary>
        /// P6A 27：実キー J の攻撃で剣閃が<b>出て</b>、<b>画面の中</b>にあり、手前の Collider に遮られない。
        /// スライドで B へ移ったあとも、B の剣閃が B の主人公の攻撃で出る（遷移後の購読と表示）。
        /// </summary>
        [UnityTest]
        public IEnumerator AttackVfx_ShowsInA_AndAfterSlideInB()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return AttackAndExpectSlash("A");

            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            yield return AttackAndExpectSlash("B（スライド後）");
        }

        private IEnumerator AttackAndExpectSlash(string label)
        {
            PlayerSlashVfxPresenter presenter = null;
            foreach (PlayerSlashVfxPresenter p in Object.FindObjectsByType<PlayerSlashVfxPresenter>(FindObjectsSortMode.None))
            {
                if (p.gameObject.scene == CurrentScene())
                {
                    presenter = p;
                }
            }

            Assert.IsNotNull(presenter, label + "：活動中のエリアに剣閃の表示役がある。");
            Assert.IsTrue(presenter.isActiveAndEnabled, label + "：表示役が有効。");

            var player = Object.FindFirstObjectByType<PlayerStateController>();
            SlashVfxInstance shown = null;
            float deadline = Time.realtimeSinceStartup + 3f;
            bool pressed = false;
            while (shown == null && Time.realtimeSinceStartup < deadline)
            {
                pressed = !pressed;
                InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.J) : new KeyboardState());
                yield return null;
                foreach (SlashVfxInstance i in presenter.Pool.Instances)
                {
                    if (i != null && i.IsPlaying && i.CurrentSprite != null)
                    {
                        shown = i;
                    }
                }
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.IsNotNull(shown, label + "：攻撃で剣閃が再生される（主人公=" + player.Current + "）。");

            var renderer = shown.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(renderer, label + "：剣閃は SpriteRenderer で描く。");
            Assert.IsTrue(renderer.enabled && renderer.gameObject.activeInHierarchy, label + "：描画が有効。");
            Camera cam = Camera.main;
            Assert.IsNotNull(cam, label + "：Main Camera（常駐 Rig）がある。");
            Vector3 vp = cam.WorldToViewportPoint(renderer.bounds.center);
            Assert.IsTrue(vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f,
                label + "：剣閃が画面内にある（viewport=" + vp + "）。");
            Assert.IsTrue((cam.cullingMask & (1 << renderer.gameObject.layer)) != 0, label + "：Camera の描画対象の層にある。");

            // 手前の遮蔽（壁・床）が無いこと。主人公・犬丸・敵・Trigger は除く。
            Vector3 from = cam.transform.position;
            Vector3 to = renderer.bounds.center;
            foreach (RaycastHit h in Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to) - 0.05f,
                         ~0, QueryTriggerInteraction.Ignore))
            {
                bool actor = h.collider.GetComponentInParent<PlayerRoot>() != null
                             || h.collider.GetComponentInParent<CompanionActor>() != null
                             || h.collider.GetComponentInParent<EnemyActor>() != null;
                Assert.IsTrue(actor, label + "：剣閃の手前に遮蔽物がある（" + h.collider.name + "@" + h.point + "）。");
            }

            yield return new WaitForSeconds(0.8f);
        }

        // ================================================================ 5. 終了・タイトル復帰（P6A 22）

        private const string TitleScene = "Assets/_Project/Scenes/Tests/Phase6A/SCN_Phase6A_Title.unity";

        /// <summary>
        /// P6A 22：ゲーム内メニュー（実キー Esc → T）でタイトルへ戻ると、<b>保存契機の無い最新値（HP）まで</b>保存してから戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator ReturnToTitle_FromMenu_SavesLatestThenLoadsTitle()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            QuietFieldEnemies();
            Transitions().LauncherScenePath = TitleScene; // タイトル（Phase6CampaignLauncher）が起動時に設定するのと同じ値。

            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            int hp = vitals.Vitals.Health.Max - 5;
            vitals.Vitals.Health.SetCurrent(hp);
            Assert.IsFalse(Saves().Coordinator.IsDirty, "前提：HP の変化は保存契機ではない（未保存の最新値）。");

            CampaignSaveService saves = Saves();
            yield return PressKeyUntil(Key.Escape, () => saves.IsMenuOpen, 3f);
            Assert.IsTrue(saves.IsMenuOpen, "Esc でメニューが開く。");
            Assert.AreEqual(GameMode.Paused, GameModeProvider.Current.Current, "メニュー中は入力を UI へ（攻撃・Interact が流れない）。");

            AreaTransitionService transitions = Transitions();
            int returned = transitions.ReturnedToLauncherCount;
            yield return PressKeyUntil(Key.T, () => saves.IsExiting || transitions.ReturnedToLauncherCount > returned, 3f);
            yield return WaitUntilOrTimeout(() => transitions.ReturnedToLauncherCount > returned || saves.AwaitingExitChoice, 20f);
            Assert.IsFalse(saves.AwaitingExitChoice, "保存に失敗していない: " + saves.Coordinator.LastError);
            Assert.AreEqual(returned + 1, transitions.ReturnedToLauncherCount, "タイトルへ戻った。");
            Assert.AreEqual(TitleScene, SceneManager.GetActiveScene().path, "タイトル Scene が載っている。");
            Assert.AreEqual(SaveExitOutcome.Saved, saves.LastExitOutcome);

            SaveLoadDecision decision = saves.Coordinator.Store.DecideLoad();
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot snap, out string err), err);
            Assert.AreEqual(hp, snap.Party.Player.Hp, "終了前の最新 HP が保存されている。");
        }

        /// <summary>
        /// P6A 19／22：終了前の保存が失敗したら、成功表示をせず選択肢を出す。「ゲームへ戻る」で入力が戻り、
        /// 再試行は<b>最新</b>の状態を書く。前の有効な世代は残っている。
        /// </summary>
        [UnityTest]
        public IEnumerator ReturnToTitle_SaveFails_OffersChoices_RetryWritesLatest()
        {
            var fs = new SwitchableFileSystem();
            yield return StartBootstrap();
            Assert.IsNull(Saves().Coordinator, "前提：保存の調停役はまだ作られていない（差し替えが効く）。");
            Saves().FileSystemOverride = fs;
            Assert.IsTrue(Saves().Flow.TryNewGame(Catalog(), out string error), error);
            yield return WaitAreaReady(AreaA);
            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
            yield return WaitSaved("New Game");
            QuietFieldEnemies();
            Transitions().LauncherScenePath = TitleScene;
            long generationBefore = Saves().Coordinator.Store.DecideLoad().Chosen.Info.Generation;

            fs.FailReplace = true;
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            vitals.Vitals.Health.SetCurrent(vitals.Vitals.Health.Max - 3);

            CampaignSaveService saves = Saves();
            Assert.IsNotNull(saves.Coordinator.Session, "前提：冒険が保存へ結ばれている。");
            Assert.AreSame(Session(), saves.Coordinator.Session, "前提：結ばれているのは今の Session。");
            Assert.AreEqual(SaveStatus.Saved, saves.Coordinator.Status, "前提：保存済み。");
            int failuresBefore = saves.Coordinator.FailureCount;
            int returned = Transitions().ReturnedToLauncherCount;
            saves.RequestReturnToTitle();
            yield return WaitUntilOrTimeout(() => saves.AwaitingExitChoice
                || Transitions().ReturnedToLauncherCount > returned, 20f);
            Assert.IsTrue(saves.AwaitingExitChoice, "失敗したら選択を待つ（結果=" + saves.LastExitOutcome
                + " 状態=" + saves.Coordinator.Status + " 未保存=" + saves.Coordinator.IsDirty
                + " 書込中=" + saves.Coordinator.IsWriting + " 終了待ち=" + saves.IsExiting
                + " 再開=" + Session().Respawn.Phase + " 戻った=" + Transitions().ReturnedToLauncherCount
                + " 失敗数=" + (saves.Coordinator.FailureCount - failuresBefore) + " 成功数=" + saves.Coordinator.SuccessCount
                + " 送出=" + saves.Coordinator.SubmitCount + " 要求=" + saves.Coordinator.RequestCount + "）。");
            Assert.AreEqual(SaveExitOutcome.Failed, saves.LastExitOutcome, "成功扱いにしない。");
            Assert.AreEqual(returned, Transitions().ReturnedToLauncherCount, "タイトルへは戻っていない。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "ゲームの場所に居る。");
            Assert.AreEqual(generationBefore, saves.Coordinator.Store.DecideLoad().Chosen.Info.Generation,
                "前の有効な世代が残っている。");

            saves.ChooseBackToGame();
            yield return null;
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "ゲームへ戻ると入力が戻る。");
            Assert.AreEqual(SaveStatus.Failed, saves.Coordinator.Status, "未保存の表示が残る。");

            fs.FailReplace = false;
            int latest = vitals.Vitals.Health.Max - 8;
            vitals.Vitals.Health.SetCurrent(latest);
            int submits = saves.Coordinator.SubmitCount;
            saves.Coordinator.RetryNow();
            yield return WaitUntilOrTimeout(() => saves.Coordinator.SubmitCount > submits, 3f);
            Assert.Greater(saves.Coordinator.SubmitCount, submits, "再試行で採り直して出す。");
            yield return WaitSaved("再試行");
            SaveLoadDecision decision = saves.Coordinator.Store.DecideLoad();
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot snap, out string err), err);
            Assert.AreEqual(latest, snap.Party.Player.Hp, "再試行は古い Snapshot ではなく最新を書く。");
        }

        /// <summary>
        /// P6A 17：同じフレームで報酬が確定し主人公が倒れた状態から、終了（タイトル復帰）を求めても
        /// <b>HP 0 の通常保存を作らない</b>。死亡再開を先に進め（周期 +1・全回復・お地蔵様へ）、その状態を報酬込みで保存してから戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator ReturnToTitle_WhileDead_ResolvesRespawnBeforeSaving()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return WaitSaved("New Game");
            Transitions().LauncherScenePath = TitleScene;
            GameSessionState session = Session();
            int cycle = session.RespawnCycle;
            CampaignSaveService saves = Saves();
            int submitsBefore = saves.Coordinator.SubmitCount;

            // 同じフレームで：普通敵の撃破報酬（記録と徳）と、主人公への致死の一撃。
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            var attackerGo = new GameObject("P6ASameFrameAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = vitals.transform.position + Vector3.forward;
            bool recorded = false;
            int hits = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!vitals.IsDefeated && Time.realtimeSinceStartup < deadline)
            {
                hits++;
                vitals.ReceiveHit(new HitInfo(attacker, vitals, Vector3.back, vitals.transform.position,
                    new HitDamage(9999, 0f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(9600 + hits)));
                if (vitals.IsDefeated)
                {
                    // 倒れたのと<b>同じフレーム</b>で撃破の報酬が確定する。
                    recorded = session.TryRecordFieldDefeat(AreaA, FieldA1,
                        new RewardSnapshot(new StableId("reward_p6a_kill"), 1, default, false), out _);
                    break;
                }

                yield return null;
            }

            Object.DestroyImmediate(attackerGo);
            Assert.IsTrue(vitals.IsDefeated, "前提：倒れている。");
            Assert.IsTrue(recorded, "前提：同じフレームで撃破の報酬が確定した。");
            int virtue = session.Progress.Virtue;
            yield return null;
            yield return null;
            Assert.AreEqual(submitsBefore, saves.Coordinator.SubmitCount, "倒れている間は保存を採らない（HP 0 の通常保存を作らない）。");

            int returned = Transitions().ReturnedToLauncherCount;
            saves.RequestReturnToTitle();
            yield return WaitUntilOrTimeout(() => Transitions().ReturnedToLauncherCount > returned || saves.AwaitingExitChoice, 30f);
            Assert.IsFalse(saves.AwaitingExitChoice, "保存に失敗していない: " + saves.Coordinator.LastError);
            Assert.AreEqual(returned + 1, Transitions().ReturnedToLauncherCount, "再開を済ませてからタイトルへ戻った。");
            Assert.AreEqual(cycle + 1, session.RespawnCycle, "死亡再開で周期が 1 つ進んだ。");

            SaveLoadDecision decision = saves.Coordinator.Store.DecideLoad();
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot snap, out string err), err);
            Assert.Greater(snap.Party.Player.Hp, 0, "保存の HP は 0 ではない（再開後の全回復）。");
            Assert.AreEqual(virtue, snap.TotalVirtue - snap.SpentVirtue, "同じフレームの報酬は保存に載っている。");
            Assert.AreEqual(cycle + 1, snap.RespawnCycle, "周期の進みも同じ保存に載っている。");
        }

        /// <summary>置換だけを失敗させられるファイル操作（実ファイルへ委ねる）。</summary>
        private sealed class SwitchableFileSystem : ISaveFileSystem
        {
            private readonly RealSaveFileSystem _real = new RealSaveFileSystem();
            public bool FailReplace;

            public bool Exists(string path) => _real.Exists(path);
            public string ReadAllText(string path) => _real.ReadAllText(path);
            public void WriteAllTextDurable(string path, string text) => _real.WriteAllTextDurable(path, text);

            // 置換・移動（どちらの側へ書くかで使い分けられる）を両方止める＝一時ファイルは書けたが確定できない。
            public void Replace(string source, string destination)
            {
                if (FailReplace)
                {
                    throw new IOException("test: replace failed");
                }

                _real.Replace(source, destination);
            }

            public void Move(string source, string destination)
            {
                if (FailReplace)
                {
                    throw new IOException("test: move failed");
                }

                _real.Move(source, destination);
            }
            public void Delete(string path) => _real.Delete(path);
            public void CreateDirectory(string path) => _real.CreateDirectory(path);
            public System.IDisposable TryLock(string path) => _real.TryLock(path);
        }

        // ================================================================ 8. パッドでのメニュー操作・テスト専用の調整

        /// <summary>
        /// 試遊のフィードバック（2026-10-02）：タイトルの選択肢をパッドの上下と決定で選べる。保存が無いときは「はじめから」が選ばれていて、
        /// 決定で New Game が始まる。
        /// </summary>
        [UnityTest]
        public IEnumerator Title_PadDecideStartsNewGame()
        {
            yield return StartBootstrap();
            yield return SceneManager.LoadSceneAsync(TitleScene, LoadSceneMode.Single);
            var launcher = Object.FindFirstObjectByType<Phase6CampaignLauncher>();
            Assert.IsNotNull(launcher, "タイトルの起動役がある。");
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Gamepad pad = AddPad();
            yield return null;
            yield return TapPad(pad, GamepadButton.DpadDown); // 「つづきから」は保存が無いので選べず、先頭に留まる。
            yield return TapPad(pad, GamepadButton.South);
            yield return WaitAreaReady(AreaA);
            Assert.IsFalse(string.IsNullOrEmpty(Session().AdventureId), "パッドの決定で New Game が始まった。");
        }

        /// <summary>
        /// お地蔵様のメニューとゲーム内メニューをパッドで操作できる：決定で休息、下で「閉じる」へ移って決定で閉じる、
        /// Start でゲーム内メニュー、下へ移って「ゲームへ戻る」。
        /// </summary>
        [UnityTest]
        public IEnumerator ShrineAndGameMenu_PadNavigation()
        {
            yield return NewGame();
            QuietFieldEnemies();
            Gamepad pad = AddPad();
            yield return null;

            yield return InteractShrine(ShrineA);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            yield return null;
            yield return TapPad(pad, GamepadButton.South);
            Assert.AreEqual(1, shrines.RestCount, "先頭（休息する）を決定で選べる。");
            Assert.IsTrue(shrines.IsMenuOpen, "休息のあともメニューは開いたまま。");

            yield return TapPad(pad, GamepadButton.DpadUp); // 先頭から上で末尾（閉じる）へ回り込む。

            yield return TapPad(pad, GamepadButton.South);
            Assert.IsFalse(shrines.IsMenuOpen, "末尾の「閉じる」を決定で閉じる。");
            yield return null;
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "操作に戻る。");

            CampaignSaveService saves = Saves();
            yield return TapPad(pad, GamepadButton.Start);
            Assert.IsTrue(saves.IsMenuOpen, "Start でゲーム内メニューが開く。");
            yield return TapPad(pad, GamepadButton.DpadDown);
            yield return TapPad(pad, GamepadButton.DpadDown);
            yield return TapPad(pad, GamepadButton.South);
            Assert.IsFalse(saves.IsMenuOpen, "「ゲームへ戻る」で閉じる。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);

            yield return TapPad(pad, GamepadButton.Start);
            Assert.IsTrue(saves.IsMenuOpen);
            yield return TapPad(pad, GamepadButton.East);
            Assert.IsFalse(saves.IsMenuOpen, "B（戻る）でも閉じる。");
        }

        /// <summary>
        /// テスト専用の調整（P6 の検証 campaign だけ）：主人公の最大 HP は基礎値の半分、普通敵・遭遇戦の敵の攻撃力は 2 倍。
        /// 成長の加算は半分にした後に足す。
        /// </summary>
        [UnityTest]
        public IEnumerator TestTuning_HalfPlayerHp_DoubleEnemyAttack()
        {
            yield return NewGame();
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            var data = new UnityEditor.SerializedObject(vitals).FindProperty("_data")?.objectReferenceValue
                as Momotaro.Data.Characters.PlayerData;
            Assert.IsNotNull(data, "主人公の Data がある。");
            Assert.AreEqual(0.5f, vitals.MaxHpScale, 1e-4f, "基礎最大 HP の倍率は 0.5。");
            Assert.AreEqual(Mathf.RoundToInt(data.MaxHp * 0.5f), vitals.Vitals.Health.Max, "最大 HP は基礎値の半分。");
            Assert.AreEqual(vitals.Vitals.Health.Max, vitals.Vitals.Health.Current, "New Game は満タン（半分の最大値で）。");

            AreaFieldEnemyDirector field = Object.FindFirstObjectByType<AreaFieldEnemyDirector>();
            EnemyActor enemy = field.Spawned[0].GetComponentInChildren<EnemyActor>();
            Assert.AreEqual(2f, enemy.AttackPowerScale, 1e-4f, "普通敵の攻撃力は 2 倍。");
            Assert.AreEqual(enemy.Archetype.AttackPower * 2f, enemy.EffectiveAttackPower, 1e-3f, "攻撃に使う値も 2 倍。");

            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            AreaEncounterRunner north = FindRunner(EncounterBNorth);
            yield return StartEncounter(north);
            foreach (EnemyActor e in EnemiesOf(north))
            {
                Assert.AreEqual(2f, e.AttackPowerScale, 1e-4f, "遭遇戦の敵の攻撃力も 2 倍。");
            }
        }

        private Gamepad AddPad()
        {
            _pad = InputSystem.AddDevice<Gamepad>("P55Gamepad");
            return _pad;
        }

        private static IEnumerator TapPad(Gamepad pad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            yield return null;
        }

        // ================================================================ 手順

        private IEnumerator NewGame()
        {
            yield return StartBootstrap();
            CampaignAdventureFlow flow = Saves().Flow;
            Assert.IsNotNull(flow, "New Game／Continue の手順がある。");
            Assert.IsTrue(flow.TryNewGame(Catalog(), out string error), "New Game を受理する。理由=" + error);
            yield return WaitAreaReady(AreaA);
            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
        }

        private IEnumerator StartBootstrap()
        {
            DestroyBootstrap();
            _bootstrap = new GameObject("BootstrapRoot_P6AWorldTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded, "常駐が立つ。理由=" + BootstrapRoot.Instance.BootstrapFailure);
            Saves().SaveDirectory = _saveDir;
        }

        /// <summary>「再起動」：常駐・static・入力を捨てて作り直す（Editor の static 残留に頼らない）。</summary>
        private IEnumerator Restart()
        {
            yield return WaitSaved("再起動前");
            RemoveDevices();
            DestroyBootstrap();
            P55ResidentRig.Reset();
            ClearStatics();
            yield return null;
            yield return StartBootstrap();
        }

        private IEnumerator ContinueAndWait(StableId expectedArea)
        {
            CampaignAdventureFlow flow = Saves().Flow;
            bool continued = false;
            string failure = null;
            flow.Continued += () => continued = true;
            flow.ContinueFailed += reason => failure = reason;
            Assert.IsTrue(flow.TryContinue(Catalog(), out string error), "Continue を受理する。理由=" + error);
            yield return WaitUntilOrTimeout(() => continued || failure != null, 30f);
            Assert.IsNull(failure, "Continue が失敗した: " + failure);
            Assert.IsTrue(continued, "Continue が採用まで済む。");
            yield return WaitAreaReady(expectedArea);
            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
        }

        private static IEnumerator WaitAreaReady(StableId areaId)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                AreaRuntimeBundle current = CurrentAreaProvider.Current;
                if (current != null && current.AreaId.Equals(areaId) && current.Context != null
                    && current.Context.IsAreaReady && !Transitions().IsTransitionUnsettled)
                {
                    break;
                }

                yield return null;
            }

            Assert.IsNotNull(CurrentAreaProvider.Current, "活動中のエリアがある。");
            Assert.AreEqual(areaId.Value, CurrentAreaProvider.Current.AreaId.Value, "到着したエリア。");
            Assert.IsTrue(CurrentAreaProvider.Current.Context.IsAreaReady, "エリアの準備ができている。");
            yield return null;
        }

        private static IEnumerator WaitSaved(string label)
        {
            SaveCoordinator c = Saves().Coordinator;
            Assert.IsNotNull(c, label + "：保存の調停役がある。");
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((c.IsDirty || c.IsWriting) && c.Status != SaveStatus.Failed && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreNotEqual(SaveStatus.Failed, c.Status, label + "：保存に失敗した: " + c.LastError);
            Assert.IsFalse(c.IsDirty, label + "：保存が終わらない（状態=" + c.Status + " 版=" + c.Session?.Changes.Revision
                + " 保存済み=" + c.SavedRevision + " 採取の保留=" + c.DeferredCaptureCount + "）。");
        }

        /// <summary>出入口の手前へ置き、実キーを押しっぱなしにしてスライドで渡る。</summary>
        private IEnumerator SlideTo(StableId exitId, Key key, Vector3 back, string label, System.Action sample = null)
        {
            AreaTransitionService transitions = Transitions();
            _expectedSlides = transitions.SlideCommittedCount + 1;
            AreaExitGate gate = FindExitGate(exitId);
            yield return PlaceAt(gate.transform.position + back * 0.4f);

            float deadline = Time.realtimeSinceStartup + 25f;
            float nextRelease = Time.realtimeSinceStartup + 1.5f;
            while (transitions.SlideCommittedCount < _expectedSlides && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                // 断られた出入口は「一度離す」まで再要求しない（P5 §6.1）。人と同じく、ときどき離して押し直す。
                bool release = Time.realtimeSinceStartup >= nextRelease;
                InputSystem.QueueStateEvent(_keyboard, release ? new KeyboardState() : new KeyboardState(key));
                if (release)
                {
                    nextRelease = Time.realtimeSinceStartup + 1.5f;

                    // 普通敵に押されて通路から外れていたら、出入口の手前へ置き直す（人なら歩いて戻る）。
                    if (!gate.PlayerInside && transitions.SlideCommittedCount < _expectedSlides
                        && !transitions.Slide.IsTransitioning)
                    {
                        yield return PlaceAt(gate.transform.position + back * 0.4f);
                    }
                }

                sample?.Invoke();
                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.IsFalse(transitions.HasTerminalFailure, label + "：終端失敗。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(_expectedSlides, transitions.SlideCommittedCount,
                label + "：スライドで渡れていない（失敗=" + transitions.Slide.LastFailure
                + " 受付の拒否=" + transitions.Slide.Coordinator.LastRejection
                + " mode=" + (GameModeProvider.Current != null ? GameModeProvider.Current.Current.ToString() : "null")
                + " 範囲内=" + gate.PlayerInside
                + " 主人公=" + Object.FindFirstObjectByType<PlayerStateController>()?.Current
                + " 位置=" + Object.FindFirstObjectByType<PlayerRoot>()?.transform.position
                + " 出入口=" + gate.transform.position + "）。");

            float quietDeadline = Time.realtimeSinceStartup + 20f;
            int quiet = 0;
            while (quiet < 3 && Time.realtimeSinceStartup < quietDeadline)
            {
                sample?.Invoke();
                yield return null;
                bool busy = transitions.Slide.IsTransitioning || transitions.Slide.IsRetireInFlight
                    || transitions.Slide.Preloader.HasLiveSceneOperation || transitions.IsSingleLoadInFlight;
                quiet = busy ? 0 : quiet + 1;
            }

            yield return null;
        }

        private IEnumerator InteractShrine(StableId shrineId)
        {
            ShrinePoint point = null;
            foreach (ShrinePoint p in Object.FindObjectsByType<ShrinePoint>(FindObjectsSortMode.None))
            {
                if (p.ShrineId.Equals(shrineId))
                {
                    point = p;
                }
            }

            Assert.IsNotNull(point, "お地蔵様 " + shrineId.Value + " がある。");
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            yield return PlaceAt(point.InteractionAnchor + new Vector3(0f, 0f, 1.0f));
            // 像の方（南）を向く。歩いて近づけば自然にそうなる（窓口は正面の対象だけを選ぶ）。
            Object.FindFirstObjectByType<PlayerRoot>().GetComponentInChildren<PlayerFacing>().ConfirmFromInput(Vector2.down);
            yield return null;
            int before = point.InteractCount;
            yield return PressKeyUntil(Key.E, () => point.InteractCount > before, 6f);
            var interaction = Object.FindFirstObjectByType<AreaInteractionController>();
            Assert.Greater(point.InteractCount, before, "実キー E でお地蔵様を調べられる（窓口の拒否="
                + (interaction != null ? interaction.LastRejection.ToString() : "窓口なし")
                + " 候補数=" + AreaInteractableRegistry.Count + " 利用可=" + point.IsAvailable
                + " 現在Area=" + (CurrentAreaProvider.Current != null ? CurrentAreaProvider.Current.AreaId.Value : "なし")
                + " 像=" + point.InteractionAnchor + " 主人公=" + Object.FindFirstObjectByType<PlayerRoot>().transform.position
                + " 窓口の数=" + Object.FindObjectsByType<AreaInteractionController>(FindObjectsSortMode.None).Length
                + " mode=" + GameModeProvider.Current?.Current
                + " 遮蔽=" + DumpSphere(Object.FindFirstObjectByType<PlayerRoot>().transform.position, point.InteractionAnchor)
                + "）。");
            Assert.IsTrue(shrines.IsMenuOpen, "メニューが開く（" + shrines.Message + "）。");
        }

        private static IEnumerator StartEncounter(AreaEncounterRunner runner)
        {
            AreaEncounterTrigger trigger = null;
            foreach (AreaEncounterTrigger t in Object.FindObjectsByType<AreaEncounterTrigger>(FindObjectsSortMode.None))
            {
                if (TriggerRunner(t) == runner)
                {
                    trigger = t;
                }
            }

            Assert.IsNotNull(trigger, "遭遇戦 " + runner.EncounterId.Value + " の Trigger がある。");
            yield return PlaceAt(trigger.transform.position);
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 6f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State,
                "Trigger 進入で戦闘が始まる。拒否=" + runner.LastRejection + " 補足=" + runner.LastFailureDetail);
        }

        private static AreaEncounterRunner TriggerRunner(AreaEncounterTrigger trigger)
        {
            System.Reflection.FieldInfo f = typeof(AreaEncounterTrigger).GetField("_runner",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f != null ? f.GetValue(trigger) as AreaEncounterRunner : null;
        }

        private static EnemyActor[] EnemiesOf(AreaEncounterRunner runner)
        {
            AreaEncounterSpawner spawner = runner.transform.GetComponentInChildren<AreaEncounterSpawner>(true);
            var list = new List<EnemyActor>();
            foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                if (e != null && !e.IsDefeated && spawner != null && e.transform.IsChildOf(spawner.transform))
                {
                    list.Add(e);
                }
            }

            if (list.Count == 0)
            {
                // 生成先が Spawner の子でない構成なら、区域の近くの敵で代える。
                foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
                {
                    if (e != null && !e.IsDefeated && e.GetComponentInParent<AreaFieldEnemyDirector>() == null)
                    {
                        list.Add(e);
                    }
                }
            }

            return list.ToArray();
        }

        /// <summary>
        /// 普通敵を止める（この検証の対象外。放っておくと待ちの間に主人公が倒され、死亡の解決待ちが混ざる）。
        /// 配置役を無効にすると、活動ゲートが閉じたときと同じく生成物も止まる。
        /// </summary>
        private static void QuietFieldEnemies()
        {
            foreach (AreaFieldEnemyDirector d in Object.FindObjectsByType<AreaFieldEnemyDirector>(FindObjectsSortMode.None))
            {
                d.gameObject.SetActive(false);
            }
        }

        private static int CountLiveEnemies()
        {
            int n = 0;
            foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                if (e != null && !e.IsDefeated && e.GetComponentInParent<AreaFieldEnemyDirector>() == null)
                {
                    n++;
                }
            }

            return n;
        }

        private static int AliveFieldEnemies(AreaFieldEnemyDirector field)
        {
            int n = 0;
            foreach (GameObject go in field.Spawned)
            {
                if (go == null || !go.activeInHierarchy)
                {
                    continue;
                }

                EnemyActor actor = go.GetComponentInChildren<EnemyActor>();
                if (actor != null && !actor.IsDefeated)
                {
                    n++;
                }
            }

            return n;
        }

        private static AreaEncounterRunner FindRunner(StableId encounterId)
        {
            foreach (AreaEncounterRunner r in Object.FindObjectsByType<AreaEncounterRunner>(FindObjectsSortMode.None))
            {
                if (r.EncounterId.Equals(encounterId) && r.gameObject.scene == CurrentScene())
                {
                    return r;
                }
            }

            Assert.Fail("遭遇戦 " + encounterId.Value + " が活動中のエリアにない。");
            return null;
        }

        private static Scene CurrentScene()
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            return bundle != null ? bundle.gameObject.scene : default;
        }

        private static AreaSaveRecord FindAreaRecord(SaveSnapshot snapshot, StableId areaId)
        {
            foreach (AreaSaveRecord r in snapshot.Areas)
            {
                if (r.AreaId == areaId.Value)
                {
                    return r;
                }
            }

            Assert.Fail("保存にエリア " + areaId.Value + " の記録がない。");
            return null;
        }

        private IEnumerator KillWithRealHitbox(EnemyActor enemy)
        {
            if (enemy == null || enemy.IsDefeated)
            {
                yield break;
            }

            var playerRoot = Object.FindFirstObjectByType<PlayerRoot>();
            var facing = playerRoot.GetComponentInChildren<PlayerFacing>();
            var vitals = playerRoot.GetComponentInChildren<PlayerVitalsHolder>();
            float deadline = Time.realtimeSinceStartup + 25f;
            float nextPress = 0f;
            bool pressed = false;

            while (Time.realtimeSinceStartup < deadline && enemy != null && !enemy.IsDefeated)
            {
                // 倒されて検証が止まらないよう、主人公の HP は保つ（検証対象は撃破と報酬）。
                if (vitals != null && vitals.Vitals.Health.Current < vitals.Vitals.Health.Max / 2)
                {
                    vitals.Vitals.Health.SetCurrent(vitals.Vitals.Health.Max);
                }

                Vector3 stick = enemy.transform.position + new Vector3(0f, 0f, -1.0f);
                if (playerRoot.Body != null)
                {
                    playerRoot.Body.position = stick;
                    playerRoot.Body.linearVelocity = Vector3.zero;
                }

                playerRoot.transform.position = stick;
                facing.ConfirmFromInput(Vector2.up);

                if (Time.realtimeSinceStartup >= nextPress)
                {
                    pressed = !pressed;
                    InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.J) : new KeyboardState());
                    nextPress = Time.realtimeSinceStartup + 0.12f;
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.IsTrue(enemy == null || enemy.IsDefeated, "実 Hitbox で敵を倒せていない（HP=" + (enemy != null ? enemy.CurrentHp : 0)
                + " 敵=" + (enemy != null ? enemy.name + "@" + enemy.transform.position + " active=" + enemy.gameObject.activeInHierarchy : "")
                + " 主人公=" + Object.FindFirstObjectByType<PlayerStateController>()?.Current
                + " mode=" + GameModeProvider.Current?.Current + "）。");
        }

        private IEnumerator KillPlayerWithRealHits()
        {
            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            var attackerGo = new GameObject("P6ALethalAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = vitals.transform.position + Vector3.forward;
            int hits = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!vitals.IsDefeated && Time.realtimeSinceStartup < deadline)
            {
                hits++;
                vitals.ReceiveHit(new HitInfo(attacker, vitals, Vector3.back, vitals.transform.position,
                    new HitDamage(9999, 0f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(9800 + hits)));
                yield return null;
            }

            Object.DestroyImmediate(attackerGo);
            yield return null;
            Assert.IsTrue(vitals.IsDefeated, "前提：主人公が倒れている。");
        }

        private static IEnumerator KnockDown(CompanionHitReceiver dog)
        {
            var attackerGo = new GameObject("P6ACompanionAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = dog.transform.position + Vector3.forward;
            int hits = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!dog.Vitals.IsDown && Time.realtimeSinceStartup < deadline)
            {
                hits++;
                dog.ReceiveHit(new HitInfo(attacker, dog, Vector3.back, dog.transform.position,
                    new HitDamage(9999, 0f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(9700 + hits)));
                for (int i = 0; i < 40 && !dog.Vitals.IsDown; i++)
                {
                    yield return null; // 被弾後の無敵が切れるのを待つ。
                }
            }

            Object.DestroyImmediate(attackerGo);
            Assert.IsTrue(dog.Vitals.IsDown, "前提：犬丸が Down している。");
        }

        private static IEnumerator WaitForRespawnPrompt()
        {
            var view = Object.FindFirstObjectByType<CampaignRespawnView>();
            Assert.IsNotNull(view, "再開操作の表示がある。");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!view.IsShowing && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(view.IsShowing, "倒れたら再開操作が出る。");
        }

        private sealed class LethalAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public int ActorId => GetInstanceID();
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
        }

        private static IEnumerator PlaceAt(Vector3 position)
        {
            var root = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(root, "主人公の根がある。");
            root.transform.position = new Vector3(position.x, root.transform.position.y, position.z);
            if (root.Body != null)
            {
                root.Body.position = root.transform.position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private IEnumerator PressKeyUntil(Key key, System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;
                for (int i = 0; i < 10 && !condition(); i++)
                {
                    yield return null;
                }
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }

        private static IEnumerator WaitUntilOrTimeout(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static string DumpSphere(Vector3 from, Vector3 to)
        {
            Vector3 a = from + Vector3.up * 0.5f;
            Vector3 b = to + Vector3.up * 0.5f;
            Vector3 d = b - a;
            var sb = new System.Text.StringBuilder();
            foreach (RaycastHit h in Physics.SphereCastAll(a, 0.25f, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                sb.Append('[').Append(h.collider.name).Append(" layer=").Append(LayerMask.LayerToName(h.collider.gameObject.layer))
                  .Append('@').Append(h.collider.bounds.center).Append(']');
            }

            return sb.Length == 0 ? "なし" : sb.ToString();
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private static AreaEntryPoint FindEntry(StableId entryId)
        {
            foreach (AreaEntryPoint e in Object.FindObjectsByType<AreaEntryPoint>(FindObjectsSortMode.None))
            {
                if (e.EntryId.Equals(entryId) && e.gameObject.scene == CurrentScene())
                {
                    return e;
                }
            }

            Assert.Fail("入口 " + entryId.Value + " が活動中のエリアにない。");
            return null;
        }

        private static AreaExitGate FindExitGate(StableId exitId)
        {
            foreach (AreaExitGate gate in Object.FindObjectsByType<AreaExitGate>(FindObjectsSortMode.None))
            {
                if (gate != null && gate.ExitId.Equals(exitId) && gate.gameObject.scene == CurrentScene())
                {
                    return gate;
                }
            }

            Assert.Fail("出入口 '" + exitId.Value + "' が活動中のエリアにない。");
            return null;
        }

        private static AreaCatalogData Catalog()
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<AreaCatalogData>(CatalogPath);
            Assert.IsNotNull(data, "P6A のカタログがある（build-phase6-world）。");
            return data;
        }

        private static AreaTransitionService Transitions()
        {
            AreaTransitionService service = BootstrapServices.Get<AreaTransitionService>();
            Assert.IsNotNull(service, "遷移サービスが常駐していない。");
            return service;
        }

        private static CampaignSaveService Saves()
        {
            CampaignSaveService service = BootstrapServices.Get<CampaignSaveService>();
            Assert.IsNotNull(service, "保存サービスが常駐していない。");
            return service;
        }

        private static GameSessionState Session()
        {
            Assert.IsNotNull(GameSessionProvider.Current, "Session がある。");
            return GameSessionProvider.Current;
        }

        private void DestroyBootstrap()
        {
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            if (_bootstrap != null)
            {
                Object.DestroyImmediate(_bootstrap);
                _bootstrap = null;
            }
        }

        private static void ClearStatics()
        {
            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            PerceptionTargetRegistry.Clear();
            AreaInteractableRegistry.Clear();
            InvestigationPointRegistry.Clear();
            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            GameSessionProvider.Current = null;
            GameplayClockProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            ShrineOperationsProvider.Current = null;
            AreaPendingArrival.Clear();
        }

        private void RemoveDevices()
        {
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            if (_pad != null)
            {
                InputSystem.RemoveDevice(_pad);
                _pad = null;
            }

            RemoveStrayDevices();
        }

        private static void RemoveStrayDevices()
        {
            for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = InputSystem.devices[i];
                if (device != null && device.name != null
                    && (device.name.StartsWith("P55Keyboard") || device.name.StartsWith("P55Gamepad")))
                {
                    InputSystem.RemoveDevice(device);
                }
            }
        }
    }
}
