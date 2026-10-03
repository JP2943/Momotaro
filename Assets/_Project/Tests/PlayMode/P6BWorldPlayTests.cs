using System.Collections;
using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
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
using Momotaro.Presentation.Diagnostics;
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
    /// P6B の検証 campaign（P6A と同じ A–B–C 構成を設定で再利用）を<b>実 Scene・実入力・実ファイル</b>で通す（P6B 仕様 §10・§11）。
    /// 成長の木は実キー・実パッドで、きびだんごは実キー F／LT で、命中は実 Hitbox で、境界は実キーで渡る。
    /// 再起動は常駐と static を捨てて作り直す（別プロセスの確認は実ビルドで別に行う。P6B 19）。
    /// </summary>
    public sealed class P6BWorldPlayTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Tests/Phase6B/SO_AreaCatalog_P6B.asset";
        private const string AreaAScene = "Assets/_Project/Scenes/Tests/Phase6B/SCN_Phase6B_AreaA.unity";

        private static readonly StableId AreaA = new StableId("area_p6b_a");
        private static readonly StableId AreaB = new StableId("area_p6b_b");
        private static readonly StableId AreaC = new StableId("area_p6b_c");
        private static readonly StableId ShrineA = new StableId("shrine_p6_a");
        private static readonly StableId ShrineC = new StableId("shrine_p6_c");
        private static readonly StableId ShrineC2 = new StableId("shrine_p6_c_2");
        private static readonly StableId EntryCShrine2 = new StableId("entry_p6_c_shrine_2");
        private static readonly StableId EntryAShrine = new StableId("entry_p6_a_shrine");
        private static readonly StableId EntryCShrine = new StableId("entry_p6_c_shrine");
        private static readonly StableId ExitAEast = new StableId("exit_p6_a_east");
        private static readonly StableId ExitBWest = new StableId("exit_p6_b_west");
        private static readonly StableId ExitBEast = new StableId("exit_p6_b_east");
        private static readonly StableId ExitCWest = new StableId("exit_p6_c_west");
        private static readonly StableId EncounterBNorth = new StableId("encounter_p6b_b_north");
        private static readonly StableId EncounterBSouth = new StableId("encounter_p6b_b_south");
        private static readonly StableId FlagBCache = new StableId("flag_p6_b_cache");
        private static readonly StableId FieldA1 = new StableId("field_p6_a_01");
        private static readonly StableId FieldA2 = new StableId("field_p6_a_02");
        private static readonly StableId QuestFixture = new StableId("quest_p6a_fixture");

        private static readonly StableId Vit1 = new StableId("growth_vit_01");
        private static readonly StableId AtkStock = new StableId("growth_atk_stock_01");
        private static readonly StableId StaRec = new StableId("growth_sta_recovery_01");
        private static readonly StableId Sta1 = new StableId("growth_sta_01");
        private static readonly StableId[] All =
        {
            new StableId("growth_vit_01"), new StableId("growth_vit_02"), new StableId("growth_vit_recovery_01"),
            new StableId("growth_atk_01"), new StableId("growth_atk_02"), new StableId("growth_atk_stock_01"),
            new StableId("growth_sta_01"), new StableId("growth_sta_posture_01"), new StableId("growth_sta_recovery_01"),
        };

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
            _saveDir = Path.Combine(Application.temporaryCachePath, "p6b_play_" + System.Guid.NewGuid().ToString("N"));
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

            // 主人公の居ない空の Scene へ移る（P6A のように A を読み直すと、その主人公が成長込みの値で作られ、
            // 次のテストの New Game がその値を「運ばれてきた値」として新しい冒険の主人公へ渡そうとして値域で断られる。
            // 本番の New Game はタイトル（主人公なし）から始まるので起きない——テストの後片付けの都合）。
            Scene empty = SceneManager.CreateScene("P6BTestEmpty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene sc = SceneManager.GetSceneAt(i);
                if (sc != empty && sc.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(sc);
                }
            }

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

        // ================================================================ 1. 成長の木（実入力）→ 効果 → 休息・保存 → 払い戻し → 再起動 → Continue

        /// <summary>
        /// P6B 01／02／03／04／16／17／18：New Game（A の初到達で徳 300）→ お地蔵様を実キー E で調べる → パッドで成長の木を開き、
        /// 確認（既定はいいえ）を経て取得 → 決定の連打で複数取得・払い戻しへ流れない → キーボードで残りを全取得 →
        /// 主人公の能力（最大 HP・最大スタミナ・刀と通常体幹の倍率）と残数・上限へ反映 → 末端を払い戻し、非末端は理由表示 →
        /// 閉じた入力が攻撃・回避へ流れない → 常駐と static を捨てて Continue し、成長・権利・効果が戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator Tree_RealInput_AcquireAllRefundLeaf_EffectsOnPlayer_ContinueRestores()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return WaitSaved("New Game");
            GameSessionState s = Session();
            Assert.AreEqual(300, s.Progress.AvailableVirtue, "A の初到達（一度きり報酬）で徳 300。");
            Assert.AreEqual(3, s.Progress.RefundRights, "初期の払い戻し権利 3。");
            Assert.AreEqual(3, s.Kibidango);

            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            PlayerStateController player = Object.FindFirstObjectByType<PlayerStateController>();
            int baseHp = vitals.MaxHp;
            float baseStamina = vitals.MaxStaminaValue;
            Assert.AreEqual(100, baseHp, "P6B は主人公の実定義（テスト倍率なし）。");

            yield return InteractShrine(ShrineA);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            AddPad();
            yield return null;
            yield return TapPad(_pad, GamepadButton.DpadDown);
            yield return TapPad(_pad, GamepadButton.South);
            Assert.IsTrue(shrines.IsTreeOpen, "パッドで成長の木を開ける。");
            int rests = shrines.RestCount;

            // (0,0) 体力 一：決定 → 確認（既定いいえ）→ 右で「はい」→ 決定。
            yield return TapPad(_pad, GamepadButton.South);
            Assert.AreEqual(GrowthTreeScreen.Pending.Acquire, shrines.Tree.Confirming, "確認が出る。");
            Assert.IsFalse(shrines.Tree.ConfirmYes, "既定はいいえ。");
            Assert.IsTrue(s.Progress.AvailableVirtue == 300, "確認を開いただけでは徳を予約消費しない。");
            yield return TapPad(_pad, GamepadButton.DpadRight);
            yield return TapPad(_pad, GamepadButton.South);
            Assert.IsTrue(s.Progress.HasGrowth(Vit1), "パッドで取得できる（" + shrines.Message + "）。");
            Assert.AreEqual(rests + 1, shrines.RestCount, "取得で休息 1 回。");
            Assert.AreEqual(baseHp + 10, vitals.MaxHp, "活動 Area の主人公へ即座に反映。");
            Assert.AreEqual(vitals.MaxHp, vitals.CurrentHp, "新しい上限で全回復。");

            // 決定の連打：払い戻し確認（既定いいえ）→ いいえで閉じる → もう一度開く……で、取得・払い戻しのどちらも増えない。
            int spent = s.Progress.SpentVirtue;
            for (int i = 0; i < 4; i++)
            {
                yield return TapPad(_pad, GamepadButton.South);
            }

            Assert.AreEqual(GrowthTreeScreen.Pending.None, shrines.Tree.Confirming, "偶数回の決定で確認は閉じている。");
            Assert.IsTrue(s.Progress.HasGrowth(Vit1), "連打で払い戻されない。");
            Assert.AreEqual(spent, s.Progress.SpentVirtue, "連打で複数取得しない。");
            Assert.AreEqual(3, s.Progress.RefundRights);
            Assert.IsTrue(shrines.IsTreeOpen, "連打のあとも木が開いている。");

            // 残りはキーボード（矢印・Enter）で全取得。
            var order = new (int c, int r)[] { (0, 1), (0, 2), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2) };
            foreach ((int col, int row) in order)
            {
                yield return KeyboardAcquire(shrines, col, row);
            }

            foreach (StableId id in All)
            {
                Assert.IsTrue(s.Progress.HasGrowth(id), id.Value + " を取得している。");
            }

            Assert.AreEqual(30, s.Progress.AvailableVirtue, "300−270。");
            Assert.AreEqual(baseHp + 20, vitals.MaxHp);
            Assert.AreEqual(baseStamina + 10f, vitals.MaxStaminaValue, 1e-3f);
            Assert.AreEqual(1.20f, player.GrowthAttackHpMultiplier, 1e-4f);
            Assert.AreEqual(1.10f, player.GrowthNormalPoiseMultiplier, 1e-4f);
            Assert.AreEqual(4, s.Kibidango, "最大数 +1 の上限まで補充。");
            Assert.AreEqual(rests + 9, shrines.RestCount, "取得ごとに休息 1 回。");

            // 非末端の払い戻しは理由を出して開かない（(0,1) 体力 二は (0,2) が依存）。
            yield return KeySelect(shrines, 0, 1);
            yield return TapKey(Key.Enter);
            Assert.AreEqual(GrowthTreeScreen.Pending.None, shrines.Tree.Confirming, "非末端は確認を開かない。");
            StringAssert.Contains("前提", shrines.Tree.Notice, "理由を表示する。");

            // 末端（(1,2) 腰袋）の払い戻し：上限 4→3、残数は新しい上限まで。
            yield return KeySelect(shrines, 1, 2);
            yield return TapKey(Key.Enter);
            Assert.AreEqual(GrowthTreeScreen.Pending.Refund, shrines.Tree.Confirming);
            yield return TapKey(Key.RightArrow);
            yield return TapKey(Key.Enter);
            Assert.IsFalse(s.Progress.HasGrowth(AtkStock), "末端を払い戻せる（" + shrines.Message + "）。");
            Assert.AreEqual(2, s.Progress.RefundRights);
            Assert.AreEqual(60, s.Progress.AvailableVirtue, "実支出 30 が戻る。");
            Assert.AreEqual(3, s.Kibidango, "上限低下後に超過を残さない。");

            // 戻る（B）で木 → メニュー → 閉じる。閉じた入力（B＝回避、A＝調べる）が Gameplay へ流れない。
            yield return TapPad(_pad, GamepadButton.East);
            Assert.IsFalse(shrines.IsTreeOpen);
            Assert.IsTrue(shrines.IsMenuOpen);
            yield return TapPad(_pad, GamepadButton.East);
            Assert.IsFalse(shrines.IsMenuOpen);
            for (int i = 0; i < 20; i++)
            {
                Assert.AreNotEqual(PlayerState.Step, player.Current, "閉じた B が回避へ流れない。");
                Assert.AreNotEqual(PlayerState.Attack, player.Current, "攻撃へ流れない。");
                yield return null;
            }

            // 再起動 → Continue：成長・権利・徳・効果（基礎値から作り直し）が戻る。
            yield return Restart();
            yield return ContinueAndWait(AreaA);
            GameSessionState c = Session();
            Assert.AreEqual(2, c.Progress.RefundRights, "権利は Load で 3 へ戻らない。");
            Assert.AreEqual(60, c.Progress.AvailableVirtue);
            Assert.IsFalse(c.Progress.HasGrowth(AtkStock));
            Assert.IsTrue(c.Progress.HasGrowth(StaRec));
            PlayerVitalsHolder v2 = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            PlayerStateController p2 = Object.FindFirstObjectByType<PlayerStateController>();
            Assert.AreEqual(baseHp + 20, v2.MaxHp, "Load で効果が累積しない。");
            Assert.AreEqual(baseStamina + 10f, v2.MaxStaminaValue, 1e-3f);
            Assert.AreEqual(1.20f, p2.GrowthAttackHpMultiplier, 1e-4f);
            Assert.AreEqual(3, c.Kibidango);
        }

        // ================================================================ 2. きびだんご：実入力・確定・保存・Continue

        /// <summary>
        /// P6B 09／10／14：実キー F と LT で使う。開始だけでは保存しない。1.5 秒で回復と残数 −1 が同じ更新で入り保存される。
        /// 確定前に正常中断（保存 → 再起動）した保存は未回復・未消費のまま、確定後の保存は回復済み・1 個減ったまま戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator Kibidango_RealKeyAndTrigger_CommitSaves_ContinueRestoresWithoutReheal()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return WaitSaved("New Game");
            GameSessionState s = Session();
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            PlayerStateController player = Object.FindFirstObjectByType<PlayerStateController>();

            // 確定前の正常中断：HP 40 で使い始め、0.6 秒で保存 → 再起動。
            vitals.Vitals.Health.SetCurrent(40);
            s.Changes.RequestAutosave("test_hp");
            yield return WaitSaved("HP 40");
            yield return TapKey(Key.F);
            Assert.IsTrue(player.IsUsingItem, "実キー F で使う（拒否=" + player.LastItemUseRejection + "）。");
            long rev = s.Changes.Revision;
            yield return WaitUntilOrTimeout(() => player.ItemUseElapsed >= 0.6f, 3f);
            Assert.AreEqual(rev, s.Changes.Revision, "開始だけでは版も保存も動かない。");
            s.Changes.RequestAutosave("test_mid_use");
            yield return WaitSaved("使用中（確定前）");
            Assert.IsTrue(player.IsUsingItem, "保存の採取は使用を止めない。");
            yield return Restart();
            yield return ContinueAndWait(AreaA);
            s = Session();
            vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            player = Object.FindFirstObjectByType<PlayerStateController>();
            QuietFieldEnemies();
            Assert.AreEqual(40, vitals.CurrentHp, "確定前の保存：回復していない。");
            Assert.AreEqual(3, s.Kibidango, "確定前の保存：消費していない。");
            Assert.IsFalse(player.IsUsingItem, "使用の途中は持ち越さない（中立姿勢）。");

            // 確定：実キー F → 1.5 秒で回復 50・残数 2・保存。
            yield return TapKey(Key.F);
            Assert.IsTrue(player.IsUsingItem);
            yield return WaitUntilOrTimeout(() => player.ItemUseCommitted, 4f);
            Assert.AreEqual(90, vitals.CurrentHp);
            Assert.AreEqual(2, s.Kibidango);
            yield return WaitSaved("確定");
            yield return WaitUntilOrTimeout(() => !player.IsUsingItem, 3f);

            // LT（パッドの左トリガー）でも使える。
            vitals.Vitals.Health.SetCurrent(30);
            AddPad();
            yield return null;
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftTrigger = 1f });
            yield return null;
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            yield return null;
            Assert.IsTrue(player.IsUsingItem, "LT で使う（拒否=" + player.LastItemUseRejection + "）。");
            yield return WaitUntilOrTimeout(() => player.ItemUseCommitted, 4f);
            Assert.AreEqual(80, vitals.CurrentHp);
            Assert.AreEqual(1, s.Kibidango);
            yield return WaitSaved("確定（LT）");

            // 確定後の正常中断 → Continue：回復済みの HP と 1 個減った残数。再度は回復しない。
            yield return Restart();
            yield return ContinueAndWait(AreaA);
            Assert.AreEqual(1, Session().Kibidango);
            Assert.AreEqual(80, Object.FindFirstObjectByType<PlayerVitalsHolder>().CurrentHp, "Load で再度回復しない。");
        }

        // ================================================================ 3. 被弾・死亡（主人公の実の被弾入口）

        /// <summary>
        /// P6B 11：確定前の被弾で中断（回復・消費なし）、致死は死亡解決が優先（返金処理なし。死亡復帰の補充は通常の休息効果）。
        /// </summary>
        [UnityTest]
        public IEnumerator Kibidango_HitInterrupts_DeathTakesPrecedence()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return WaitSaved("New Game");
            GameSessionState s = Session();
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            PlayerStateController player = Object.FindFirstObjectByType<PlayerStateController>();

            // 成長を 1 つ取ってから（死亡復帰で効果が累積しないことも見る。P6B 04）。
            yield return InteractShrine(ShrineA);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            Assert.AreEqual(ShrineMenuResult.GrowthPurchased, shrines.PurchaseGrowth(Vit1), shrines.Message);
            shrines.Close();
            yield return null;
            yield return null;
            Assert.AreEqual(110, vitals.MaxHp);

            // 犬丸がかばう（守護）と主人公に被弾が成立しない——その場合は継続するのが仕様（EditMode で確認）。
            // ここでは主人公への実被弾を見るため、犬丸を Down させてからにする。
            yield return KnockDown(Object.FindFirstObjectByType<CompanionHitReceiver>());
            vitals.Vitals.Health.SetCurrent(60);
            yield return TapKey(Key.F);
            Assert.IsTrue(player.IsUsingItem);
            yield return WaitUntilOrTimeout(() => player.ItemUseElapsed >= 0.8f, 3f);
            HitPlayer(vitals, 30f, 9100);
            yield return null;
            yield return null;
            Assert.IsFalse(player.IsUsingItem, "確定前の被弾で中断。");
            Assert.AreEqual(3, s.Kibidango, "消費しない。");
            Assert.Less(vitals.CurrentHp, 60);
            yield return WaitUntilOrTimeout(() => player.Current == PlayerState.Idle, 3f);
            yield return new WaitForSeconds(0.6f);

            // 致死：使用中に倒れる → 確定しない。死亡解決が優先される。
            yield return TapKey(Key.F);
            Assert.IsTrue(player.IsUsingItem, "拒否=" + player.LastItemUseRejection);
            yield return WaitUntilOrTimeout(() => player.ItemUseElapsed >= 0.5f, 3f);
            yield return KillPlayerWithRealHits();
            Assert.IsFalse(player.IsUsingItem);
            Assert.AreEqual(0, player.ItemUseCommitCount, "倒れた使用は確定しない。");
            Assert.AreEqual(3, s.Kibidango);
            yield return WaitForRespawnPrompt();

            // 死亡復帰：通常の休息効果（全回復・補充）。使用の返金処理は無い。成長の効果は基礎値から作り直す（累積しない）。
            AreaTransitionService transitions = Transitions();
            int completed = transitions.CompletedCount;
            yield return PressKeyUntil(Key.Enter, () => transitions.CompletedCount > completed || transitions.HasTerminalFailure, 25f);
            Assert.IsFalse(transitions.HasTerminalFailure, transitions.TerminalFailureReason);
            yield return WaitAreaReady(AreaA);
            PlayerVitalsHolder after = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            Assert.AreEqual(110, after.MaxHp, "死亡復帰で効果が累積しない。");
            Assert.AreEqual(after.MaxHp, after.CurrentHp, "全回復。");
            Assert.AreEqual(3, s.Kibidango, "補充は通常の上限まで（返金の上乗せは無い）。");
        }

        // ================================================================ 4. 境界

        /// <summary>
        /// P6B 13：A の東の出入口で使用し、外向き（D）を押し続ける。使用中は遷移しない・場外へ出ない。
        /// 使用が終われば<b>離さずに</b>そのまま遷移する。ノックバックでは遷移が始まらない。
        /// </summary>
        [UnityTest]
        public IEnumerator Kibidango_AtBoundary_NoTransitionWhileUsing_ThenTransitionsWithoutRelease()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return WaitSaved("New Game");
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            PlayerStateController player = Object.FindFirstObjectByType<PlayerStateController>();
            AreaTransitionService transitions = Transitions();
            AreaExitGate gate = FindExitGate(ExitAEast);

            // ノックバック（押し出し）だけでは遷移しない。
            yield return PlaceAt(gate.transform.position + Vector3.left * 0.4f);
            int slides = transitions.SlideCommittedCount;
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            motor.PushReaction(Vector3.right, 0.8f, 0.3f);
            for (int i = 0; i < 40; i++)
            {
                yield return null;
            }

            Assert.AreEqual(slides, transitions.SlideCommittedCount, "押されただけでは遷移しない。");

            yield return PlaceAt(gate.transform.position + Vector3.left * 0.4f);
            vitals.Vitals.Health.SetCurrent(40);
            yield return TapKey(Key.F);
            Assert.IsTrue(player.IsUsingItem, "拒否=" + player.LastItemUseRejection);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
            Vector3 eastWall = gate.transform.position;
            while (player.IsUsingItem)
            {
                Assert.AreEqual(slides, transitions.SlideCommittedCount, "使用中は遷移しない。");
                Assert.IsFalse(transitions.Slide.IsTransitioning);
                yield return null;
            }

            Assert.AreEqual(1, player.ItemUseCommitCount);
            Assert.IsTrue(gate.IsArmed, "使用中に要求して断られ、離れ直し待ちになっていない。");
            Assert.Greater(gate.SuppressedWhileBusyCount, 0, "使用中は出入口が要求を控えた。");

            // 離さずに押し続ける → 遷移する。
            yield return WaitUntilOrTimeout(() => transitions.SlideCommittedCount > slides, 10f);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.Greater(transitions.SlideCommittedCount, slides, "終了後は離れ直さずに遷移する（失敗="
                + transitions.Slide.LastFailure + " 範囲内=" + gate.PlayerInside + "）。");
            yield return WaitAreaReady(AreaB);
        }

        // ================================================================ 5. 命中窓口の倍率（刀・必殺・通常体幹）

        /// <summary>
        /// P6B 02：実キー J（通常 1 段）と L（必殺）を実 Hitbox で記録用の的へ当て、成長なしと全取得相当で比べる。
        /// 刀の HP 寄与は通常・必殺とも ×1.20、体幹は通常だけ ×1.10（必殺は ×1.00）。
        /// </summary>
        [UnityTest]
        public IEnumerator GrowthMultipliers_RealHitWindow_SwordAndSpecialScaled_PoiseOnlyNormal()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return WaitSaved("New Game");
            PlayerStateController player = Object.FindFirstObjectByType<PlayerStateController>();
            PlayerRoot root = Object.FindFirstObjectByType<PlayerRoot>();
            PlayerFacing facing = root.GetComponentInChildren<PlayerFacing>();
            Vector3 spot = FindEntry(EntryAShrine).transform.position;
            yield return PlaceAt(spot);
            facing.ConfirmFromInput(Vector2.up);

            var targetGo = new GameObject("P6BRecordingTarget");
            targetGo.layer = LayerMask.NameToLayer("Enemy");
            var box = targetGo.AddComponent<BoxCollider>();
            box.size = new Vector3(1.6f, 1.6f, 1.6f);
            RecordingTarget target = targetGo.AddComponent<RecordingTarget>();
            targetGo.transform.position = root.transform.position + new Vector3(0f, 0.5f, 1.0f);
            Physics.SyncTransforms();
            yield return null;

            yield return Swing(target, Key.J, 0.1f);
            HitInfo baseNormal = target.Last;
            yield return new WaitForSeconds(1.0f);
            yield return Swing(target, Key.L, 2.9f);
            HitInfo baseSpecial = target.Last;
            Assert.Greater(baseNormal.Damage.Hp, 0f);
            Assert.Greater(baseSpecial.Damage.Hp, baseNormal.Damage.Hp, "必殺が記録されている。");

            // 全取得相当の効果を主人公へ（本番の置き直しの口。Data・Session は変えない）。
            CurrentAreaProvider.Current.TransferPort.ApplyGrowthEffects(new GrowthEffects(20, 0.20f, 10, 0.10f, 10, 1));
            yield return new WaitForSeconds(1.0f);
            yield return PlaceAt(spot);
            facing.ConfirmFromInput(Vector2.up);
            targetGo.transform.position = root.transform.position + new Vector3(0f, 0.5f, 1.0f);
            Physics.SyncTransforms();
            yield return Swing(target, Key.J, 0.1f);
            HitInfo grownNormal = target.Last;
            yield return new WaitForSeconds(1.0f);
            yield return Swing(target, Key.L, 2.9f);
            HitInfo grownSpecial = target.Last;

            Assert.AreEqual(1.20f, grownNormal.Damage.Hp / baseNormal.Damage.Hp, 1e-3f, "通常の刀 HP は ×1.20。");
            Assert.AreEqual(1.10f, grownNormal.Damage.Poise / baseNormal.Damage.Poise, 1e-3f, "通常の体幹は ×1.10。");
            Assert.AreEqual(baseNormal.Damage.Flinch, grownNormal.Damage.Flinch, 1e-4f, "ひるませ値は変えない。");
            Assert.AreEqual(1.20f, grownSpecial.Damage.Hp / baseSpecial.Damage.Hp, 1e-3f, "必殺の刀 HP も ×1.20。");
            Assert.AreEqual(baseSpecial.Damage.Poise, grownSpecial.Damage.Poise, 1e-4f, "必殺の体幹は対象外。");
            Assert.AreEqual(baseNormal.GuardStaminaDamage, grownNormal.GuardStaminaDamage, 1e-4f, "ガード消費は対象外。");
            Object.Destroy(targetGo);
        }

        // ================================================================ 6. 保存失敗

        /// <summary>
        /// P6B 15：書込が失敗している間に取得・払い戻し・きびだんごの確定をしても、成立した Runtime 進行を巻き戻さない
        /// （dirty と失敗表示のまま）。途中で保存要求が重なっても取りこぼさず、復旧後の再試行で<b>最新</b>がまとめて書かれる。
        /// </summary>
        [UnityTest]
        public IEnumerator SaveFailure_GrowthRefundAndUseStay_RetryWritesLatest()
        {
            var fs = new SwitchableFileSystem();
            yield return NewGameWithFileSystem(fs);
            QuietFieldEnemies();
            GameSessionState s = Session();
            SaveCoordinator c = Saves().Coordinator;
            PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            PlayerStateController player = Object.FindFirstObjectByType<PlayerStateController>();

            fs.FailReplace = true;
            int failures = c.FailureCount;
            yield return InteractShrine(ShrineA);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            Assert.AreEqual(ShrineMenuResult.GrowthPurchased, shrines.PurchaseGrowth(Vit1), shrines.Message);
            yield return WaitUntilOrTimeout(() => c.FailureCount > failures, 10f);
            Assert.Greater(c.FailureCount, failures, "書込が失敗している（状態=" + c.Status + "）。");
            Assert.IsTrue(c.IsDirty, "未保存として扱う。");
            Assert.IsTrue(s.Progress.HasGrowth(Vit1), "保存失敗で取得を巻き戻さない。");
            Assert.AreEqual(110, vitals.MaxHp);

            Assert.AreEqual(ShrineMenuResult.GrowthRefunded, shrines.RefundGrowth(Vit1), shrines.Message);
            Assert.AreEqual(ShrineMenuResult.GrowthPurchased, shrines.PurchaseGrowth(Sta1), shrines.Message);
            Assert.AreEqual(2, s.Progress.RefundRights, "払い戻しの権利消費も巻き戻さない。");
            shrines.Close();
            yield return null;

            vitals.Vitals.Health.SetCurrent(40);
            yield return TapKey(Key.F);
            Assert.IsTrue(player.IsUsingItem, "拒否=" + player.LastItemUseRejection);
            yield return WaitUntilOrTimeout(() => !player.IsUsingItem, 4f);
            Assert.AreEqual(1, player.ItemUseCommitCount);
            Assert.AreEqual(2, s.Kibidango, "使用の確定も巻き戻さない。");
            Assert.AreEqual(90, vitals.CurrentHp);
            Assert.IsTrue(c.IsDirty, "まだ書けていない。");

            fs.FailReplace = false;
            int submits = c.SubmitCount;
            c.RetryNow();
            yield return WaitUntilOrTimeout(() => c.SubmitCount > submits, 3f);
            yield return WaitSaved("再試行");
            SaveLoadDecision decision = c.Store.DecideLoad();
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot snap, out string err), err);
            Assert.AreEqual(2, snap.RefundRights, "最新の権利。");
            Assert.AreEqual(2, snap.Kibidango, "最新の残数。");
            Assert.AreEqual(90, snap.Party.Player.Hp, "確定後の HP（残数と同じ組）。");
            bool hasSta = false, hasVit = false;
            foreach (KeyValuePair<string, int> g in snap.Growth)
            {
                hasSta |= g.Key == Sta1.Value;
                hasVit |= g.Key == Vit1.Value;
            }

            Assert.IsTrue(hasSta, "最新の取得。");
            Assert.IsFalse(hasVit, "払い戻し済み。");
            Assert.AreEqual(s.Changes.Revision, snap.Revision, "最新の版。");
        }

        private IEnumerator NewGameWithFileSystem(ISaveFileSystem fs)
        {
            yield return StartBootstrap();
            Assert.IsNull(Saves().Coordinator, "前提：保存の調停役はまだ作られていない（差し替えが効く）。");
            Saves().FileSystemOverride = fs;
            Assert.IsTrue(Saves().Flow.TryNewGame(Catalog(), out string error), error);
            yield return WaitAreaReady(AreaA);
            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
            yield return WaitSaved("New Game");
        }

        /// <summary>置換だけを失敗させられるファイル操作（実ファイルへ委ねる）。</summary>
        private sealed class SwitchableFileSystem : ISaveFileSystem
        {
            private readonly RealSaveFileSystem _real = new RealSaveFileSystem();
            public bool FailReplace;

            public bool Exists(string path) => _real.Exists(path);
            public string ReadAllText(string path) => _real.ReadAllText(path);
            public void WriteAllTextDurable(string path, string text) => _real.WriteAllTextDurable(path, text);

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

        // ================================================================ 7. 使用中の正常終了・終了保存の失敗からの復帰（レビュー ddb2d19 D1）

        private const string TitleScene = "Assets/_Project/Scenes/Tests/Phase6B/SCN_Phase6B_Title.unity";

        /// <summary>
        /// P6B 14：使用中（確定前 0.6 秒／確定後の後隙 1.7 秒）に、<b>製品のゲーム内メニュー</b>（Esc → T：タイトルへ）で正常終了する。
        /// 終了前の保存（SaveBeforeExit）がその時点の HP と残数を書き、Continue 後は各時点の値のまま——
        /// 確定前は未回復・未消費、確定後は回復済み・1 個減。再回復も遅延消費も無く、使用は持ち越さない。
        /// </summary>
        [UnityTest]
        public IEnumerator Kibidango_NormalExitMidUse_BeforeAndAfterCommit_ContinueMatchesThatMoment()
        {
            yield return NewGame();
            QuietFieldEnemies();
            yield return WaitSaved("New Game");

            foreach ((float at, int hpAfter, int stockAfter) in new[] { (0.6f, 40, 3), (1.7f, 90, 2) })
            {
                Transitions().LauncherScenePath = TitleScene;
                GameSessionState s = Session();
                PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
                PlayerStateController player = Object.FindFirstObjectByType<PlayerStateController>();
                vitals.Vitals.Health.SetCurrent(40);
                s.RefillKibidango(3);
                yield return TapKey(Key.F);
                Assert.IsTrue(player.IsUsingItem, at + "：使用を開始（拒否=" + player.LastItemUseRejection + "）。");
                yield return WaitUntilOrTimeout(() => player.ItemUseElapsed >= at, 4f);
                Assert.IsTrue(player.IsUsingItem, at + "：まだ使用中。");
                Assert.AreEqual(at > 1.5f, player.ItemUseCommitted, at + "：確定の前後が狙いどおり。");

                CampaignSaveService saves = Saves();
                AreaTransitionService transitions = Transitions();
                int returned = transitions.ReturnedToLauncherCount;
                yield return PressKeyUntil(Key.Escape, () => saves.IsMenuOpen, 3f);
                Assert.IsTrue(saves.IsMenuOpen, at + "：Esc でメニューが開く（使用中でも）。");
                float frozen = player.ItemUseElapsed;
                yield return PressKeyUntil(Key.T, () => saves.IsExiting || transitions.ReturnedToLauncherCount > returned, 3f);
                yield return WaitUntilOrTimeout(() => transitions.ReturnedToLauncherCount > returned || saves.AwaitingExitChoice, 20f);
                Assert.IsFalse(saves.AwaitingExitChoice, at + "：保存に失敗していない: " + saves.Coordinator.LastError);
                Assert.AreEqual(SaveExitOutcome.Saved, saves.LastExitOutcome, at + "：終了前の保存が成功。");
                Assert.AreEqual(returned + 1, transitions.ReturnedToLauncherCount, at + "：タイトルへ戻った。");
                Assert.AreEqual(at > 1.5f, frozen > 1.5f, at + "：メニューを開いた時点の経過。");

                SaveLoadDecision decision = saves.Coordinator.Store.DecideLoad();
                Assert.IsTrue(SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot snap, out string err), err);
                Assert.AreEqual(hpAfter, snap.Party.Player.Hp, at + "：終了時点の HP が保存されている。");
                Assert.AreEqual(stockAfter, snap.Kibidango, at + "：終了時点の残数が保存されている。");

                if (_keyboard != null)
                {
                    InputSystem.RemoveDevice(_keyboard);
                    _keyboard = null;
                }

                yield return ContinueAndWait(AreaA);
                QuietFieldEnemies();
                PlayerVitalsHolder v2 = Object.FindFirstObjectByType<PlayerVitalsHolder>();
                PlayerStateController p2 = Object.FindFirstObjectByType<PlayerStateController>();
                Assert.AreEqual(hpAfter, v2.CurrentHp, at + "：Continue 後の HP（再回復なし）。");
                Assert.AreEqual(stockAfter, Session().Kibidango, at + "：Continue 後の残数（遅延消費なし）。");
                Assert.IsFalse(p2.IsUsingItem, at + "：使用は持ち越さない（中立姿勢）。");
                yield return new WaitForSeconds(2.2f);
                Assert.AreEqual(hpAfter, v2.CurrentHp, at + "：時間が経っても遅れて回復しない。");
                Assert.AreEqual(stockAfter, Session().Kibidango, at + "：遅れて消費しない。");
                yield return WaitSaved(at + "：Continue 後");
            }
        }

        /// <summary>
        /// P6B 15：使用中（確定前 0.6 秒／確定後 1.7 秒）にタイトルへの終了を求め、<b>終了前の保存を失敗</b>させる。
        /// 失敗の選択中は使用の経過が止まり、実キーで「ゲームへ戻る」を選ぶと同じ経過時間・確定状態から続く。
        /// 確定前の側はその後 1 回だけ確定し、確定済みの側は再消費しない。
        /// </summary>
        [UnityTest]
        public IEnumerator Kibidango_ExitSaveFailsMidUse_BackToGame_ResumesSameUseState()
        {
            var fs = new SwitchableFileSystem();
            yield return NewGameWithFileSystem(fs);
            QuietFieldEnemies();
            SaveCoordinator c = Saves().Coordinator;

            foreach (float at in new[] { 0.6f, 1.7f })
            {
                Transitions().LauncherScenePath = TitleScene;
                GameSessionState s = Session();
                PlayerVitalsHolder vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
                PlayerStateController player = Object.FindFirstObjectByType<PlayerStateController>();
                vitals.Vitals.Health.SetCurrent(40);
                s.RefillKibidango(3);
                int commits = player.ItemUseCommitCount;
                yield return TapKey(Key.F);
                Assert.IsTrue(player.IsUsingItem, at + "：使用を開始（拒否=" + player.LastItemUseRejection + "）。");
                yield return WaitUntilOrTimeout(() => player.ItemUseElapsed >= at, 4f);
                bool committedBefore = player.ItemUseCommitted;
                Assert.AreEqual(at > 1.5f, committedBefore);
                int stockBefore = s.Kibidango;
                int hpBefore = vitals.CurrentHp;

                fs.FailReplace = true;
                CampaignSaveService saves = Saves();
                AreaTransitionService transitions = Transitions();
                int returned = transitions.ReturnedToLauncherCount;
                yield return PressKeyUntil(Key.Escape, () => saves.IsMenuOpen, 3f);
                float frozen = player.ItemUseElapsed;
                yield return PressKeyUntil(Key.T, () => saves.IsExiting || saves.AwaitingExitChoice, 3f);
                yield return WaitUntilOrTimeout(() => saves.AwaitingExitChoice, 20f);
                Assert.IsTrue(saves.AwaitingExitChoice, at + "：終了前の保存が失敗して選択を待つ。");
                Assert.AreEqual(returned, transitions.ReturnedToLauncherCount, at + "：タイトルへは戻っていない。");

                yield return new WaitForSeconds(1.0f);
                Assert.IsTrue(player.IsUsingItem, at + "：失敗の選択中も使用状態のまま。");
                Assert.AreEqual(frozen, player.ItemUseElapsed, 1e-4f, at + "：選択中は経過が止まる。");
                Assert.AreEqual(committedBefore, player.ItemUseCommitted, at + "：選択中に確定は変わらない。");
                Assert.AreEqual(stockBefore, s.Kibidango);
                Assert.AreEqual(hpBefore, vitals.CurrentHp);

                // 実キーで「ゲームへ戻る」（選択肢の 2 番目）。
                yield return TapKey(Key.DownArrow);
                yield return TapKey(Key.Enter);
                Assert.IsFalse(saves.AwaitingExitChoice, at + "：選択が閉じた。");
                Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, at + "：ゲームへ戻った。");
                Assert.IsTrue(player.IsUsingItem, at + "：同じ使用へ戻る。");
                // 戻った直後の数フレームは進んでよい。最初からやり直していない（frozen 未満にならない）・飛んでいないこと。
                float resumed = player.ItemUseElapsed;
                Assert.GreaterOrEqual(resumed, frozen - 1e-4f, at + "：最初からやり直さない（再開 " + resumed + "）。");
                Assert.Less(resumed, frozen + 0.2f, at + "：止まっていた分を飛ばさない（再開 " + resumed + "）。");

                yield return WaitUntilOrTimeout(() => !player.IsUsingItem, 4f);
                Assert.AreEqual(commits + 1, player.ItemUseCommitCount, at + "：確定はこの使用につき 1 回だけ。");
                Assert.AreEqual(committedBefore ? stockBefore : stockBefore - 1, s.Kibidango,
                    at + (committedBefore ? "：確定済みは再消費しない。" : "：未確定はその後 1 回だけ消費。"));
                Assert.AreEqual(90, vitals.CurrentHp, at + "：回復は 1 回分。");

                fs.FailReplace = false;
                int submits = c.SubmitCount;
                c.RetryNow();
                yield return WaitUntilOrTimeout(() => c.SubmitCount > submits, 3f);
                yield return WaitSaved(at + "：復旧後の再試行");
                SaveLoadDecision decision = c.Store.DecideLoad();
                Assert.IsTrue(SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot snap, out string err), err);
                Assert.AreEqual(90, snap.Party.Player.Hp, at + "：最新（使用後）を書く。");
                Assert.AreEqual(2, snap.Kibidango, at);
            }
        }

        // ================================================================ 8. 配置された敵の実攻撃で使用中に被弾（人間試遊の報告）

        /// <summary>
        /// P6B 11（人間試遊の報告「使用中に攻撃を受けても回復する」の切り分け）：P6B の A に配置された普通敵（近接）の
        /// <b>通常の攻撃経路</b>（敵の攻撃機械 → 命中判定 → 主人公の被弾入口）で、実キー F の使用中に被弾させる。
        /// まず犬丸を Down させて（かばうなし）、次に犬丸ありで行う。各試行の使用開始・被弾・確定・終了の時刻と HP・残数を
        /// <c>_bridge/p6b_kibidango_hit_timeline.txt</c> へ書き出し、仕様 §7 の規則で判定する：
        /// 確定前の有効な被弾 → 中断し、元の終了時刻を過ぎても回復・消費しない／確定後の被弾 → 残り動作を中断・二重確定なし／
        /// かばいだけ → 継続。使用ボタンを押し続けても中断後に再使用しない。
        /// </summary>
        [UnityTest]
        public IEnumerator Kibidango_RealEnemyAttack_Timeline_InterruptsBeforeCommit()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            var log = new List<string>();
            int preCommitHits = 0, postCommitHits = 0, coverOnly = 0;
            // 条件：(犬丸, 開始の時機, 敵の絞り込み, 立ち位置)。A の普通敵（近接）で、予備動作中に使い始める（確定前の被弾）／
            // 攻撃の後隙に使い始める（次の攻撃が確定の後に来やすい）。最後に B の南の遭遇戦の遠距離敵（飛び道具の経路）。
            var conditions = new (bool dog, bool late, string filter, float stand, string label)[]
            {
                (false, true, "Melee", 0.95f, "A 近接・犬丸 Down・後隙で開始"),
                (false, false, "Melee", 0.95f, "A 近接・犬丸 Down・予備動作で開始"),
                (true, false, "Melee", 0.95f, "A 近接・犬丸あり・予備動作で開始"),
                (false, false, "Ranged", 3.5f, "B 遠距離・犬丸 Down・予備動作で開始"),
            };

            foreach ((bool withDog, bool late, string filter, float stand, string condition) in conditions)
            {
                log.Add("######## " + condition);
                if (filter == "Ranged")
                {
                    yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
                    yield return StartEncounter(FindRunner(EncounterBSouth));
                }

                int trials = 0;
                int seenDamage = 0;
                while (trials < 8 && seenDamage < 3)
                {
                    trials++;
                    TrialResult r = null;
                    yield return EnemyTrial(withDog, trials, log, x => r = x, late, filter, stand);
                    if (r == null)
                    {
                        continue;
                    }

                    if (r.FirstDamageElapsed >= 0f)
                    {
                        seenDamage++;
                    }

                    if (r.FirstDamageElapsed >= 0f && !r.CommittedAtFirstDamage)
                    {
                        preCommitHits++;
                        Assert.AreEqual(0, r.Commits, r.Label + "：確定前の被弾後に確定した（回復した）。");
                        Assert.AreEqual(r.StockAtStart, r.StockAtEnd, r.Label + "：確定前の被弾後に消費した。");
                        Assert.LessOrEqual(r.MaxHpAfterHit, r.HpAfterFirstDamage, r.Label + "：被弾後、元の終了時刻を過ぎても HP が増えてはいけない。");
                        Assert.IsFalse(r.UsingAfterHit, r.Label + "：被弾後も使用が続いた。");
                    }
                    else if (r.FirstDamageElapsed >= 0f)
                    {
                        postCommitHits++;
                        Assert.AreEqual(1, r.Commits, r.Label + "：確定後の被弾で二重確定。");
                        Assert.AreEqual(r.StockAtStart - 1, r.StockAtEnd, r.Label);
                        Assert.IsFalse(r.UsingAfterHit, r.Label + "：確定後の被弾で残り動作が中断していない。");
                    }
                    else if (r.Covers > 0)
                    {
                        coverOnly++;
                        Assert.AreEqual(1, r.Commits, r.Label + "：完全にかばわれた使用は継続して確定する。");
                    }

                    Assert.AreEqual(1, r.Starts, r.Label + "：押し続けても再使用しない。");
                }
            }

            File.WriteAllLines(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_bridge",
                "p6b_kibidango_hit_timeline.txt"), log);
            Assert.Greater(preCommitHits, 0, "確定前の実被弾を 1 回以上観測する（観測なしでは検証にならない）。\n" + string.Join("\n", log));
        }

        private sealed class TrialResult
        {
            public string Label;
            public int Starts, Commits, Covers, StockAtStart, StockAtEnd, HpAfterFirstDamage, MaxHpAfterHit;
            public float FirstDamageElapsed = -1f;
            public bool CommittedAtFirstDamage, UsingAfterHit;
        }

        private sealed class UseHitRecorder : IHitResultListener, IGuardianTransferListener
        {
            private readonly PlayerStateController _player;
            private readonly PlayerVitalsHolder _vitals;
            private readonly float _t0;
            public readonly List<string> Lines = new List<string>();
            public float FirstDamageElapsed = -1f;
            public bool CommittedAtFirstDamage;
            public int HpAfterFirstDamage;
            public int Covers;

            public UseHitRecorder(PlayerStateController player, PlayerVitalsHolder vitals, float t0)
            {
                _player = player;
                _vitals = vitals;
                _t0 = t0;
            }

            public void OnHitResult(in HitResult result)
            {
                Lines.Add(Stamp() + " 被弾結果=" + result.Kind + " 適用HP=" + result.AppliedDamage.Hp + " HP後=" + _vitals.CurrentHp);
                if (result.Kind == HitResultKind.Damage && FirstDamageElapsed < 0f && _player.IsUsingItem)
                {
                    FirstDamageElapsed = _player.ItemUseElapsed;
                    CommittedAtFirstDamage = _player.ItemUseCommitted;
                    HpAfterFirstDamage = _vitals.CurrentHp;
                }
            }

            public void OnGuardianTransfer(in GuardianTransferEvent transfer)
            {
                Covers++;
                Lines.Add(Stamp() + " 犬丸がかばった");
            }

            public string Stamp() =>
                "t=" + (Time.time - _t0).ToString("0.000") + " 経過=" + (_player.IsUsingItem ? _player.ItemUseElapsed.ToString("0.000") : "-")
                + " 確定=" + _player.ItemUseCommitCount + " frame=" + Time.frameCount;
        }

        private IEnumerator EnemyTrial(bool withDog, int trial, List<string> log, System.Action<TrialResult> done,
            bool late, string nameFilter, float standDistance)
        {
            var root = Object.FindFirstObjectByType<PlayerRoot>();
            var facing = root.GetComponentInChildren<PlayerFacing>();
            var vitals = root.GetComponentInChildren<PlayerVitalsHolder>();
            var player = root.GetComponentInChildren<PlayerStateController>();
            GameSessionState s = Session();
            CompanionHitReceiver dog = null;
            foreach (CompanionHitReceiver d in Object.FindObjectsByType<CompanionHitReceiver>(FindObjectsSortMode.None))
            {
                if (d.gameObject.scene == CurrentScene())
                {
                    dog = d;
                }
            }

            if (!withDog && dog != null && !dog.Vitals.IsDown)
            {
                yield return KnockDown(dog);
            }

            EnemyActor enemy = null;
            float best = float.MaxValue;
            foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                float dist = e != null && !e.IsDefeated && e.gameObject.activeInHierarchy && e.gameObject.scene == CurrentScene()
                             && e.name.Contains(nameFilter)
                    ? Vector3.Distance(e.transform.position, root.transform.position) : float.MaxValue;
                if (dist < best)
                {
                    best = dist;
                    enemy = e;
                }
            }

            if (enemy == null && nameFilter == "Melee")
            {
                // 普通敵が倒れていたら作り直す（休息と同じ口。試行を続けるため）。
                foreach (AreaFieldEnemyDirector d in Object.FindObjectsByType<AreaFieldEnemyDirector>(FindObjectsSortMode.None))
                {
                    if (d.gameObject.scene == CurrentScene())
                    {
                        d.RebuildNow();
                    }
                }

                yield return null;
                yield return null;
                foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
                {
                    if (e != null && !e.IsDefeated && e.gameObject.activeInHierarchy && e.gameObject.scene == CurrentScene()
                        && e.name.Contains(nameFilter))
                    {
                        enemy = e;
                        break;
                    }
                }
            }

            if (enemy == null)
            {
                log.Add("試行 " + trial + "：生きている敵がいない");
                yield break;
            }

            enemy.SetAttackPowerScale(0.3f); // 倒れないように弱める（攻撃経路はそのまま）。
            EnemyAttackController attack = enemy.GetComponentInChildren<EnemyAttackController>();
            System.Reflection.FieldInfo machineField = typeof(EnemyAttackController).GetField("_machine",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            // 敵の正面へ立ち、背を向けて J（音）で気付かせ、攻撃の予備動作（Prepare）に入るまで待つ。
            float deadline = Time.realtimeSinceStartup + 20f;
            float nextTap = 0f;
            bool preparing = false;
            while (Time.realtimeSinceStartup < deadline && !preparing && !enemy.IsDefeated)
            {
                if (vitals.CurrentHp < vitals.MaxHp)
                {
                    vitals.Vitals.Health.SetCurrent(vitals.MaxHp);
                }

                Vector3 toPlayer = root.transform.position - enemy.transform.position;
                toPlayer.y = 0f;
                if (toPlayer.magnitude > standDistance + 0.45f || toPlayer.magnitude < standDistance - 0.35f)
                {
                    Vector3 stand = enemy.transform.position + (toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.back) * standDistance;
                    root.Body.position = new Vector3(stand.x, root.Body.position.y, stand.z);
                    root.Body.linearVelocity = Vector3.zero;
                    root.transform.position = new Vector3(stand.x, root.transform.position.y, stand.z);
                }

                var machine = (EnemyAttackMachine)machineField.GetValue(attack);
                preparing = machine != null && machine.Current == EnemyAttackMachine.Phase.Prepare;
                if (!preparing && Time.realtimeSinceStartup >= nextTap && player.Current != PlayerState.Hurt)
                {
                    Vector3 look = enemy.transform.position - root.transform.position;
                    facing.ConfirmFromInput(Mathf.Abs(look.x) > Mathf.Abs(look.z)
                        ? new Vector2(-Mathf.Sign(look.x), 0f) : new Vector2(0f, -Mathf.Sign(look.z)));
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.J));
                    nextTap = Time.realtimeSinceStartup + 2.5f;
                }
                else
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                }

                yield return null;
            }

            if (!preparing)
            {
                log.Add("試行 " + trial + "：敵の攻撃が始まらなかった");
                yield break;
            }

            if (late)
            {
                // この攻撃が終わる（後隙に入る）まで待ってから使い始める。HP は保つ。
                float lateDeadline = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < lateDeadline)
                {
                    var m = (EnemyAttackMachine)machineField.GetValue(attack);
                    if (m == null || m.Current == EnemyAttackMachine.Phase.Recovery || m.Current == EnemyAttackMachine.Phase.None)
                    {
                        break;
                    }

                    vitals.Vitals.Health.SetCurrent(vitals.MaxHp);
                    yield return null;
                }
            }

            // 使用：HP を下げ、F を押し<b>続ける</b>（中断後の自動再使用が無いことも見る）。
            yield return WaitUntilOrTimeout(() => player.Current == PlayerState.Idle || player.Current == PlayerState.Move, 1f);
            vitals.Vitals.Health.SetCurrent(60);
            s.RefillKibidango(3);
            int stockAtStart = s.Kibidango;
            int startsBefore = player.ItemUseStartCount;
            int commitsBefore = player.ItemUseCommitCount;
            var rec = new UseHitRecorder(player, vitals, Time.time);
            vitals.Results.AddListener(rec);
            vitals.GuardianTransfers.AddListener(rec);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.F));
            yield return null;
            rec.Lines.Add(rec.Stamp() + " 使用開始=" + (player.ItemUseStartCount > startsBefore) + " 拒否=" + player.LastItemUseRejection
                + " HP=" + vitals.CurrentHp + " 残数=" + s.Kibidango + " 犬丸=" + (withDog ? "あり" : "Down") + " 敵=" + enemy.name);
            int lastCommits = player.ItemUseCommitCount;
            bool lastUsing = player.IsUsingItem;
            int maxHpAfterHit = int.MinValue;
            bool usingAfterHit = false;
            int hitFrame = -1;
            float until = Time.time + 3.2f; // 元の終了時刻（2.0 秒）を十分に過ぎるまで
            while (Time.time < until)
            {
                yield return null;
                if (player.ItemUseCommitCount != lastCommits)
                {
                    rec.Lines.Add(rec.Stamp() + " 確定 HP=" + vitals.CurrentHp + " 残数=" + s.Kibidango);
                    lastCommits = player.ItemUseCommitCount;
                }

                if (player.IsUsingItem != lastUsing)
                {
                    rec.Lines.Add(rec.Stamp() + (player.IsUsingItem ? " 使用中へ" : " 使用終了") + " 状態=" + player.Current
                        + " HP=" + vitals.CurrentHp + " 残数=" + s.Kibidango);
                    lastUsing = player.IsUsingItem;
                }

                if (rec.FirstDamageElapsed >= 0f)
                {
                    if (hitFrame < 0)
                    {
                        hitFrame = Time.frameCount;
                    }
                    else if (Time.frameCount > hitFrame + 1)
                    {
                        usingAfterHit |= player.IsUsingItem;
                        maxHpAfterHit = Mathf.Max(maxHpAfterHit, vitals.CurrentHp);
                    }
                }
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            vitals.Results.RemoveListener(rec);
            vitals.GuardianTransfers.RemoveListener(rec);
            var r = new TrialResult
            {
                Label = (withDog ? "犬丸あり" : "犬丸 Down") + " 試行 " + trial,
                Starts = player.ItemUseStartCount - startsBefore,
                Commits = player.ItemUseCommitCount - commitsBefore,
                Covers = rec.Covers,
                StockAtStart = stockAtStart,
                StockAtEnd = s.Kibidango,
                FirstDamageElapsed = rec.FirstDamageElapsed,
                CommittedAtFirstDamage = rec.CommittedAtFirstDamage,
                HpAfterFirstDamage = rec.HpAfterFirstDamage,
                MaxHpAfterHit = maxHpAfterHit == int.MinValue ? rec.HpAfterFirstDamage : maxHpAfterHit,
                UsingAfterHit = usingAfterHit,
            };
            log.Add("== " + r.Label + "：開始 " + r.Starts + "・確定 " + r.Commits + "・かばい " + r.Covers + "・最初の被弾 経過="
                + r.FirstDamageElapsed.ToString("0.000") + "（確定済み=" + r.CommittedAtFirstDamage + "）・残数 " + r.StockAtStart + "→" + r.StockAtEnd);
            log.AddRange(rec.Lines);
            done(r);
            yield return new WaitForSeconds(0.8f);
        }

        private IEnumerator Swing(RecordingTarget target, Key key, float hold)
        {
            int before = target.Count;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            float released = Time.realtimeSinceStartup + hold;
            float deadline = Time.realtimeSinceStartup + hold + 3f;
            bool down = true;
            while (target.Count == before && Time.realtimeSinceStartup < deadline)
            {
                if (down && Time.realtimeSinceStartup >= released)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                    down = false;
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.Greater(target.Count, before, key + " の実 Hitbox が記録用の的に当たる（主人公="
                + Object.FindFirstObjectByType<PlayerStateController>().Current + "）。");
        }

        private sealed class RecordingTarget : MonoBehaviour, IDamageable
        {
            public int Count;
            public HitInfo Last;
            public int DamageableId => GetInstanceID();

            public void ReceiveHit(in HitInfo hit)
            {
                Count++;
                Last = hit;
            }
        }

        private static void HitPlayer(PlayerVitalsHolder vitals, float hp, int id)
        {
            var attackerGo = new GameObject("P6BAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = vitals.transform.position + Vector3.forward;
            vitals.ReceiveHit(new HitInfo(attacker, vitals, Vector3.back, vitals.transform.position,
                new HitDamage(hp, 0f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(id)));
            Object.DestroyImmediate(attackerGo);
        }

        // ---------------------------------------------------------------- 成長の木の操作（キーボード）

        private IEnumerator KeySelect(CampaignShrineService shrines, int column, int row)
        {
            for (int i = 0; i < 6 && shrines.Tree.Column != column; i++)
            {
                yield return TapKey(Key.RightArrow);
            }

            for (int i = 0; i < 6 && shrines.Tree.Row != row; i++)
            {
                yield return TapKey(Key.DownArrow);
            }

            Assert.AreEqual(column, shrines.Tree.Column);
            Assert.AreEqual(row, shrines.Tree.Row);
        }

        private IEnumerator KeyboardAcquire(CampaignShrineService shrines, int column, int row)
        {
            yield return KeySelect(shrines, column, row);
            yield return TapKey(Key.Enter);
            Assert.AreEqual(GrowthTreeScreen.Pending.Acquire, shrines.Tree.Confirming,
                "(" + column + "," + row + ") の取得確認が出る（" + shrines.Tree.Notice + "）。");
            yield return TapKey(Key.LeftArrow);
            yield return TapKey(Key.Enter);
            Assert.AreEqual(ShrineMenuResult.GrowthPurchased, shrines.LastResult, shrines.Message);
        }

        private IEnumerator TapKey(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return null;
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
            _bootstrap = new GameObject("BootstrapRoot_P6BWorldTest");
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
            Assert.IsNotNull(data, "P6B のカタログがある（build-phase6b-world）。");
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
            KibidangoUseProvider.Current = null;
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
