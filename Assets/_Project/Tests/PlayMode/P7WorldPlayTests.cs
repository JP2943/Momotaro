using System.Collections;
using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Data.Story;
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
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Story;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.Save;
using Momotaro.Infrastructure.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P7 の検証 campaign（P6C の構成＋住民・依頼・門・崖道 H・章ボス）を<b>実 Scene・実入力</b>で通す（P7 仕様 §3〜§9・§12）。
    ///
    /// 会話は実キー（E で話しかけ、E／Enter で次へ、↑↓＋Enter で選ぶ、Esc で閉じる）。移動は既存の試験と同じく主人公を
    /// 出入口・扉・遭遇戦の Trigger の前へ置いてから実キーで渡る。遭遇戦の敵は実 Hitbox（J）で倒す。
    /// </summary>
    public sealed class P7WorldPlayTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Tests/Phase7/SO_AreaCatalog_P7.asset";

        private static readonly StableId AreaA = new StableId("area_p7_a");
        private static readonly StableId AreaB = new StableId("area_p7_b");
        private static readonly StableId AreaC = new StableId("area_p7_c");
        private static readonly StableId AreaH = new StableId("area_p7_h");
        private static readonly StableId ExitAEast = new StableId("exit_p6_a_east");
        private static readonly StableId ExitBEast = new StableId("exit_p6_b_east");
        private static readonly StableId EncounterBNorth = new StableId("encounter_p7_b_north");
        private static readonly StableId EncounterCBoss = new StableId("encounter_p7_c_boss");
        private static readonly StableId ShrineA = new StableId("shrine_p6_a");
        private static readonly StableId ShrineC = new StableId("shrine_p6_c");
        private static readonly StableId Guide = new StableId("villager_p7_guide");
        private static readonly StableId Giver = new StableId("villager_p7_giver");
        private static readonly StableId CliffGateEvent = new StableId("event_p7_cliff_gate");
        private static readonly StableId CliffGateFlag = new StableId("flag_p7_a_cliff_gate");
        private static readonly StableId Chapter = new StableId("chapter_p7_01");
        private static readonly StableId QuestReach = new StableId("quest_p7_reach_junction");
        private static readonly StableId QuestFind = new StableId("quest_p7_find_scroll");
        private static readonly StableId QuestMulti = new StableId("quest_p7_road_and_cliff");
        private static readonly StableId RewardReach = new StableId("reward_p7_quest_reach");
        private static readonly StableId DoorAToH = new StableId("door_p7_a_to_h");
        private static readonly StableId DoorHToC = new StableId("door_p7_h_to_c");
        private static readonly StableId DoorCToH = new StableId("door_p7_c_to_h");
        private static readonly StableId FindCliffScroll = new StableId("find_p7_h_scroll");
        private static readonly StableId GrowthVit1 = new StableId("growth_p7_vit_01");

        private GameObject _bootstrap;
        private Keyboard _keyboard;
        private string _saveDir;

        [SetUp]
        public void SetUp()
        {
            P55ResidentRig.Reset();
            RemoveStrayDevices();
            ClearStatics();
            AreaPendingArrival.ResetDiagnostics();
            _saveDir = Path.Combine(Application.temporaryCachePath, "p7_play_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            CampaignSaveService.TestDirectoryOverride = _saveDir;
            Time.timeScale = 1f;
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            Time.timeScale = 1f;
            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            Scene empty = SceneManager.CreateScene("P7TestEmpty_" + System.Guid.NewGuid().ToString("N"));
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
            try
            {
                if (Directory.Exists(_saveDir))
                {
                    Directory.Delete(_saveDir, true);
                }
            }
            catch (IOException)
            {
            }
        }

        // ================================================================ P7 01：実入力の会話

        /// <summary>
        /// P7 01：実キー E で話しかけ、全ページを読み、選択肢を選び、閉じる。開始に使った E を押したままでは第一文を飛ばさない。
        /// 長押し・連打で 1 回に 2 ページ進まない。会話中は J（攻撃）も E（調べる）も Gameplay に届かない。閉じた後に押しっぱなしの E で
        /// 再び話しかけない。
        /// </summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator Dialogue_RealKeys_ReadAllChooseAndClose_NoSkipNoLeak()
        {
            yield return NewGame();
            CampaignDialogueService dialogue = Dialogue();
            PlayerStateController state = Active().state;

            // 押したまま開く：開いた後も E を離すまでは第一文のまま。
            yield return PlaceAtVillager(Guide);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.E));
            yield return WaitUntilOrTimeout(() => dialogue.IsOpen, 3f);
            Assert.IsTrue(dialogue.IsOpen, "E で話しかけられる（拒否=" + dialogue.LastRejection + "）。");
            Assert.AreEqual(GameMode.Dialogue, GameModeProvider.Current.Current, "会話中は Dialogue。");
            Assert.IsTrue(GameplayClockProvider.IsFrozen, "会話中は Gameplay 時計を保持で止める。");
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0, dialogue.Conversation.PageIndex, "開始の E を押したままでは第一文を飛ばさない。");
            Assert.IsTrue(dialogue.AwaitingRelease, "開始に使ったボタンを離すまで決定を受けない（解放待ち）。");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return WaitUntilOrTimeout(() => !dialogue.AwaitingRelease, 2f);

            // 会話中の J・E は Gameplay へ届かない。
            int attacks = 0;
            for (int i = 0; i < 10; i++)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.J));
                yield return null;
                attacks += state.Current == PlayerState.Attack ? 1 : 0;
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;
            }

            Assert.AreEqual(0, attacks, "会話中に攻撃しない。");

            // 長押し：1 回の押下で 1 ページだけ進む。
            int page = dialogue.Conversation.PageIndex;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.AreEqual(page + 1, dialogue.Conversation.PageIndex, "長押しで 2 ページ以上進まない。");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            // 連打：押すたびに 1 ページ。最後のページで選択肢が出る（門の会話：門を開けてもらう／閉じる）。
            yield return AdvanceToChoices(dialogue);
            Assert.IsTrue(HasChoice(dialogue, DialogueChoiceKind.ConfirmEvent), "門の決定が出る。");
            Assert.IsFalse(Session().Story.IsEventCompleted(CliffGateEvent), "最後のページを開いた押下で選択まで進めない。");
            int last = dialogue.Conversation.PageIndex;

            // 「閉じる」を選ぶ（↓＋Enter）。何も変えない。
            yield return ChooseKind(dialogue, DialogueChoiceKind.Close);

            Assert.IsFalse(dialogue.IsOpen, "閉じる。");
            Assert.IsFalse(Session().Story.IsEventCompleted(CliffGateEvent), "閉じただけではイベントを確定しない。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "元のモードへ戻る。");
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "保持を外す。");
            Assert.GreaterOrEqual(last, 1);

            // Esc で閉じる。同じ Esc でゲーム内メニューを開かない。
            yield return TalkTo(Guide);
            yield return Tap(Key.Escape);
            Assert.IsFalse(dialogue.IsOpen, "Esc で閉じる。");
            Assert.IsFalse(Saves().IsMenuOpen, "閉じた同じ Esc でゲーム内メニューを開かない。");

            // 門を開けてもらう（最後の決定）。以後の案内は選択肢の無い通常会話になる。
            yield return TalkTo(Guide);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseKind(dialogue, DialogueChoiceKind.ConfirmEvent);
            Assert.IsTrue(Session().Story.IsEventCompleted(CliffGateEvent));

            // 選択肢の無い会話：最後のページで E を押したまま閉じ、押しっぱなしの E で再び話しかけない。
            yield return TalkTo(Guide);
            Assert.AreEqual(0, dialogue.Conversation.Current.Choices.Count, "前提：選択肢の無い会話。");
            while (!dialogue.Conversation.IsOnLastPage)
            {
                int before = dialogue.Conversation.PageIndex;
                yield return Tap(Key.E);
                Assert.AreEqual(before + 1, dialogue.Conversation.PageIndex, "E でも 1 ページずつ進む。");
            }

            int opens = dialogue.OpenCount;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.E));
            yield return WaitUntilOrTimeout(() => !dialogue.IsOpen, 2f);
            Assert.IsFalse(dialogue.IsOpen, "最後のページの E で閉じる。");
            for (int i = 0; i < 40; i++)
            {
                yield return null;
            }

            Assert.AreEqual(opens, dialogue.OpenCount, "閉じるのに使った E を押したままでも、再び話しかけない。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.AreNotEqual(PlayerState.Attack, state.Current);
        }

        // ================================================================ P7 02：停止と他の Pause

        /// <summary>
        /// P7 02：会話中は主人公（スタミナ回復）・仲間・P6C の反撃強化の残時間が止まる（敵・飛び道具は Dialogue モードで既存の停止経路）。
        /// 会話中に終了の保存（Paused）が上に乗っても会話は進まず閉じず、選択でゲームへ戻ると会話へ戻る。閉じれば探索へ。
        /// </summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator Dialogue_FreezesPlayerCompanionAndCounter_AndRespectsExitPause()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            CampaignDialogueService dialogue = Dialogue();
            var (root, state, vitals, _) = Active();

            // スタミナを減らして回復が始まるのを待ち、反撃強化を持たせ、犬丸を離して置く。
            yield return PlaceAtVillager(Giver);
            Assert.IsTrue(vitals.TryConsumeStamina(40f), "前提：スタミナを減らす。");
            float drained = Stamina(vitals);
            yield return WaitUntilOrTimeout(() => Stamina(vitals) > drained + 0.5f, 5f);
            Assert.Greater(Stamina(vitals), drained, "前提：スタミナの回復が始まっている。");
            state.NotifyJustEvadeSuccess();
            Assert.IsTrue(state.HasJustEvadeCounter, "前提：反撃強化を持つ。");
            CompanionHitReceiver dog = Dog();
            dog.transform.position = root.transform.position + new Vector3(6f, 0f, 0f);
            yield return PressKeyUntil(Key.E, () => dialogue.IsOpen, 3f);
            Assert.IsTrue(dialogue.IsOpen, "話しかけた（拒否=" + dialogue.LastRejection + "）。");

            float stamina = Stamina(vitals);
            float counter = state.JustEvadeCounterRemaining;
            Vector3 dogAt = dog.transform.position;
            float until = Time.realtimeSinceStartup + 2.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.AreEqual(stamina, Stamina(vitals), 1e-4f, "会話中はスタミナが回復しない（主人公の時計が止まる）。");
            Assert.AreEqual(counter, state.JustEvadeCounterRemaining, 1e-4f, "会話中は P6C の強化の残時間が減らない。");
            Assert.Less(Vector3.Distance(dogAt, dog.transform.position), 0.05f, "会話中は犬丸が追従しない。");

            // 会話中の終了の保存（Paused が上に乗る）。会話は進まず閉じない。
            CampaignSaveService saves = Saves();
            SaveExitOutcome outcome = SaveExitOutcome.TimedOut;
            bool done = false;
            saves.StartCoroutine(saves.SaveBeforeExit(o =>
            {
                outcome = o;
                done = true;
            }));
            yield return WaitUntilOrTimeout(() => done, 10f);
            Assert.AreEqual(SaveExitOutcome.Saved, outcome, "会話中でも終了前の保存ができる。");
            Assert.AreEqual(GameMode.Paused, GameModeProvider.Current.Current, "終了導線が Paused を上に乗せている。");
            int page = dialogue.Conversation.PageIndex;
            yield return Tap(Key.Escape);
            yield return Tap(Key.Enter);
            Assert.IsTrue(dialogue.IsOpen, "別の停止の最中は会話を閉じない。");
            Assert.AreEqual(page, dialogue.Conversation.PageIndex, "別の停止の最中は会話を進めない。");

            // 「ゲームへ戻る」：会話へ戻る（会話を閉じたことにはならない）。
            saves.ChooseBackToGame();
            yield return null;
            Assert.AreEqual(GameMode.Dialogue, GameModeProvider.Current.Current, "会話へ戻る。");
            Assert.IsTrue(GameplayClockProvider.IsFrozen);
            yield return WaitUntilOrTimeout(() => !dialogue.AwaitingRelease, 2f);
            yield return Tap(Key.Escape);
            Assert.IsFalse(dialogue.IsOpen);
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "閉じたら探索へ。");
            Assert.IsFalse(GameplayClockProvider.IsFrozen);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.Less(state.JustEvadeCounterRemaining, counter, "閉じた後は強化の残時間が再び減る。");
        }

        /// <summary>
        /// P7 02：攻撃中・ガード中・被弾と同じフレームでは会話を始めない。断った押下を動作の後へ持ち越して自動で話しかけない。
        /// </summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator Dialogue_RefusedWhileBusyOrHitSameFrame_AndNotCarriedOver()
        {
            yield return NewGame();
            CampaignDialogueService dialogue = Dialogue();
            var (_, state, vitals, _) = Active();
            yield return PlaceAtVillager(Guide);

            // 攻撃中（J の直後に E）。
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.J));
            yield return WaitUntilOrTimeout(() => state.Current == PlayerState.Attack, 1f);
            Assert.AreEqual(PlayerState.Attack, state.Current, "前提：攻撃中。");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.J, Key.E));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.IsFalse(dialogue.IsOpen, "攻撃中は話しかけない。");
            yield return WaitUntilOrTimeout(() => state.Current == PlayerState.Idle || state.Current == PlayerState.Move, 3f);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.IsFalse(dialogue.IsOpen, "断った押下を動作の後へ持ち越さない。");

            // ガード中（K を押したまま E）。
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.K));
            yield return WaitUntilOrTimeout(() => state.IsGuarding, 1f);
            Assert.IsTrue(state.IsGuarding, "前提：ガード中。");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.K, Key.E));
            yield return null;
            yield return null;
            Assert.IsFalse(dialogue.IsOpen, "ガード中は話しかけない。");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.IsFalse(dialogue.IsOpen);

            // 被弾と同じフレーム：調べる受付の後、LateUpdate の前に被弾が確定したら開かない。
            // 犬丸の「かばう」が命中を肩代わりすると主人公の被弾にならないので、先に Down させておく。
            yield return KnockDownDog();
            yield return PlaceAtVillager(Guide);
            int opens = dialogue.OpenCount;
            var attackerGo = new GameObject("P7Attacker");
            var attacker = attackerGo.AddComponent<EnemySideActor>();
            attackerGo.transform.position = vitals.transform.position + Vector3.forward;
            InteractNowAndHitInSameFrame(vitals, attacker);
            yield return null;
            Object.Destroy(attackerGo);
            Assert.AreEqual(opens, dialogue.OpenCount, "被弾と同じフレームの話しかけは開かない（拒否=" + dialogue.LastRejection + "）。");
            Assert.AreEqual(DialogueStartRejection.HitThisFrame, dialogue.LastRejection, "被弾の解決を優先して断った。");
            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }

            Assert.AreEqual(opens, dialogue.OpenCount, "被弾の後に自動で話しかけない。");
        }

        // ================================================================ P7 03〜P7 08・P7 10：依頼・門・経路を実プレイで

        /// <summary>
        /// P7 03・04・05・07・08：村の娘から到達の依頼を実会話で受け、断った依頼は後で受け直せる。A→B→C（合流点）へ渡ると報告できる通知が出る。
        /// 旅立ちで A へ戻り、報告で 30 徳が一度だけ増える（使用済み徳は不変、到達報酬は再実行しない）。その徳でお地蔵様の成長を取得する。
        /// 別プロセス相当の再起動・Continue で受領済みのまま。
        /// </summary>
        [UnityTest, Timeout(480000)]
        public IEnumerator Quest_AcceptReachReportOnce_GrowWithReward_SurvivesContinue()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            CampaignDialogueService dialogue = Dialogue();
            GameSessionState session = Session();

            // 断る：何も変えない。
            yield return TalkTo(Giver);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseQuest(dialogue, DialogueChoiceKind.ListenQuest, QuestReach);
            yield return AdvanceToChoices(dialogue);
            StringAssert.Contains("報酬：徳 30", dialogue.Conversation.CurrentPage, "依頼の内容に報酬を出す。");
            yield return ChooseKind(dialogue, DialogueChoiceKind.DeclineQuest);
            Assert.IsFalse(dialogue.IsOpen);
            Assert.AreEqual(0, session.QuestStageOf(QuestReach), "今は受けない＝無変更（恒久フラグにしない）。");

            // 再訪して受ける。
            int savesBefore = session.Changes.AutosaveRequestCount;
            yield return TalkTo(Giver);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseQuest(dialogue, DialogueChoiceKind.ListenQuest, QuestReach);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseKind(dialogue, DialogueChoiceKind.AcceptQuest);
            Assert.AreEqual(QuestAcceptResult.Accepted, dialogue.LastAcceptResult);
            Assert.AreEqual(1, session.QuestStageOf(QuestReach));
            Assert.AreEqual(savesBefore + 1, session.Changes.AutosaveRequestCount, "受注で保存を 1 件要求。");
            StringAssert.Contains("依頼を受けた", dialogue.Notice);
            yield return WaitSaved("受注");

            // 依頼一覧（ゲーム内メニュー）。
            yield return Tap(Key.Escape);
            Assert.IsTrue(Saves().IsMenuOpen);
            Assert.IsTrue(Saves().OpenJournal());
            Assert.IsTrue(Saves().IsJournalOpen);
            List<JournalQuestEntry> journal = StoryJournal.Quests(session, Story());
            Assert.AreEqual(1, journal.Count, "受けた依頼だけを載せる。");
            Assert.AreEqual(QuestStateKind.InProgress, journal[0].State);
            Assert.IsFalse(journal[0].ObjectivesMet[0]);
            yield return Tap(Key.Escape);
            yield return Tap(Key.Escape);
            Assert.IsFalse(Saves().IsMenuOpen);

            // A → B → C（合流点）。
            QuietFieldEnemies();
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            yield return SlideTo(ExitBEast, Key.D, Vector3.left, "B→C");
            int notices = dialogue.NoticeCount;
            yield return WaitUntilOrTimeout(() => dialogue.NoticeCount > notices, 3f);
            StringAssert.Contains("報告できます", dialogue.Notice, "報告可能への変化を通知する。");
            Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(session, Quest(QuestReach)));
            Assert.AreEqual(1, session.QuestStageOf(QuestReach), "条件の成立だけでは自動で報酬を付けない。");
            Assert.IsFalse(session.Progress.HasGranted(RewardReach));

            // C のお地蔵様に登録して、A へ旅立つ。
            yield return InteractShrine(ShrineC);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            Assert.AreEqual(ShrineMenuResult.FastTravelStarted, shrines.FastTravel(ShrineA), shrines.Message);
            yield return WaitUntilOrTimeout(() => shrines.FastTravelCompletedCount > 0, 30f);
            yield return WaitAreaReady(AreaA);

            // 報告：30 徳が一度だけ。到達の報酬は再実行しない。使用済み徳は変えない。
            int total = session.Progress.TotalVirtue;
            int spent = session.Progress.SpentVirtue;
            int grants = session.Progress.GrantedRewardCount;
            yield return TalkTo(Giver);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseQuest(dialogue, DialogueChoiceKind.ReportQuest, QuestReach);
            Assert.AreEqual(QuestReportResult.Reported, dialogue.LastReportResult);
            Assert.AreEqual(30, dialogue.LastGrantedVirtue);
            Assert.AreEqual(total + 30, session.Progress.TotalVirtue);
            Assert.AreEqual(spent, session.Progress.SpentVirtue);
            Assert.AreEqual(grants + 1, session.Progress.GrantedRewardCount, "依頼報酬の ID だけが増える。");
            StringAssert.Contains("徳 +30", dialogue.Notice);
            yield return WaitSaved("報告");

            // 再訪：報告の選択肢は出ない（受領済み）。
            yield return TalkTo(Giver);
            yield return AdvanceToChoices(dialogue);
            Assert.IsFalse(HasChoice(dialogue, DialogueChoiceKind.ReportQuest), "受領済みは再報告できない。");
            yield return ChooseKind(dialogue, DialogueChoiceKind.Close);
            Assert.AreEqual(total + 30, session.Progress.TotalVirtue);

            // 得た徳で成長する（既存の成長機能）。
            int available = session.Progress.AvailableVirtue;
            yield return InteractShrine(ShrineA);
            Assert.AreEqual(ShrineMenuResult.GrowthPurchased, shrines.PurchaseGrowth(GrowthVit1), shrines.Message);
            Assert.Less(session.Progress.AvailableVirtue, available, "徳を使った。");
            shrines.Close();
            yield return WaitSaved("成長");

            // 再起動・Continue：受領済み・徳・成長のまま。
            int totalAfter = session.Progress.TotalVirtue;
            yield return Restart();
            yield return ContinueAndWait(AreaA);
            GameSessionState loaded = Session();
            Assert.AreEqual(QuestStateKind.Rewarded, StoryRules.StateOf(loaded, Quest(QuestReach)));
            Assert.AreEqual(totalAfter, loaded.Progress.TotalVirtue);
            Assert.IsTrue(loaded.Progress.HasGrowth(GrowthVit1));
            Assert.AreEqual(0, Dialogue().NoticeCount, "Load の再構築で過去の達成を通知しない。");
        }

        /// <summary>
        /// P7 03・05・06・09・10：受注前に遭遇戦（街道の北）を倒し、門の会話を途中で閉じても門は開かず、最後の決定で一度だけ開く（立て札は理由を示す）。
        /// 崖道 H へ扉で入り（困難ルートの到達）、巻物を拾い（発見）、H→C で困難ルートを踏破する。その後に受けた依頼は受注前の達成を認める。
        /// 経路の会話差分（困難のみ）を選び、再起動・Continue で門と経路と依頼が保持される。
        /// </summary>
        [UnityTest, Timeout(540000)]
        public IEnumerator Quest_PreAcceptCompletion_GateEvent_HardRoute_Discovery_SurvivesContinue()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            CampaignDialogueService dialogue = Dialogue();
            GameSessionState session = Session();

            // 門の立て札：未完了の理由。
            StoryGateNotice notice = InActiveScene<StoryGateNotice>();
            Assert.IsNotNull(notice);
            Place(Active().root, notice.InteractionAnchor + new Vector3(0f, 0f, -0.9f));
            yield return new WaitForFixedUpdate();
            Active().facing.ConfirmFromInput(Vector2.up);
            int inspected = notice.InspectCount;
            yield return PressKeyUntil(Key.E, () => notice.InspectCount > inspected, 4f);
            Assert.Greater(notice.InspectCount, inspected, "立て札を調べられる。");
            StringAssert.Contains("案内の老人", dialogue.Notice, "条件不足の理由を示す。");
            AreaFlagDoor gate = FlagDoor(CliffGateFlag);
            Assert.IsFalse(gate.IsOpened);

            // 門の会話を途中で閉じる：無変更。
            yield return TalkTo(Guide);
            yield return Tap(Key.Enter);
            yield return Tap(Key.Escape);
            Assert.IsFalse(dialogue.IsOpen);
            Assert.IsFalse(session.Story.IsEventCompleted(CliffGateEvent), "途中で閉じたら無変更。");
            Assert.IsFalse(gate.IsOpened);

            // 最後の決定で一度だけ確定し、門がその場で開く。
            yield return TalkTo(Guide);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseKind(dialogue, DialogueChoiceKind.ConfirmEvent);
            Assert.IsTrue(session.Story.IsEventCompleted(CliffGateEvent));
            Assert.IsTrue(session.GetOrCreateArea(AreaA).IsOpen(CliffGateFlag));
            Assert.IsTrue(gate.IsOpened, "門がその場で開く。");
            Assert.IsFalse(notice.IsAvailable, "開通後の立て札は調べる対象から外れる。");
            yield return WaitSaved("門");

            // 受注前に街道の北の遭遇戦を倒す（依頼 3 の一つ目の条件）。
            QuietFieldEnemies();
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            Assert.IsTrue(session.Story.RouteOf(Chapter).StandardReached, "標準ルート（B）に到達。");
            Assert.IsFalse(session.Story.RouteOf(Chapter).StandardCompleted, "入っただけでは踏破でない。");
            AreaEncounterRunner north = FindRunner(EncounterBNorth);
            yield return StartEncounter(north);
            foreach (EnemyActor e in EnemiesOf(north))
            {
                yield return KillWithRealHitbox(e);
            }

            yield return WaitUntilOrTimeout(() => north.State == AreaEncounterState.Cleared, 10f);
            Assert.AreEqual(AreaEncounterState.Cleared, north.State, "遭遇戦をクリア。");
            Assert.IsFalse(session.Story.IsChapterCleared(Chapter), "普通の遭遇戦では章クリアにしない。");

            // 撤退した未完了の遭遇戦（南）は達成にならない（条件に無いが、記録が無いことを確認）。
            AreaRuntimeState b = session.GetOrCreateArea(AreaB);
            Assert.IsFalse(b.IsEncounterCleared(new StableId("encounter_p7_b_south"), session.RespawnCycle));

            // A へ戻り、扉で崖道 H へ（困難ルートの到達）。
            yield return SlideTo(new StableId("exit_p6_b_west"), Key.A, Vector3.right, "B→A");
            yield return UseDoor(DoorAToH, AreaH);
            Assert.IsTrue(session.Story.RouteOf(Chapter).HardReached, "困難ルート（H）に到達。");
            QuietFieldEnemies();

            // 巻物を拾う（発見）。
            AreaPickupPoint scroll = Pickup(FindCliffScroll);
            Place(Active().root, scroll.transform.position + new Vector3(0f, 0f, -1.0f));
            yield return new WaitForFixedUpdate();
            Active().facing.ConfirmFromInput(Vector2.up);
            yield return PressKeyUntil(Key.E, () => session.GetOrCreateArea(AreaH).IsPlacementPicked(FindCliffScroll), 4f);
            Assert.IsTrue(session.GetOrCreateArea(AreaH).IsPlacementPicked(FindCliffScroll), "巻物を見つけた。");

            // H → C（困難ルートの終端）。
            yield return UseDoor(DoorHToC, AreaC);
            ChapterRouteRecord route = session.Story.RouteOf(Chapter);
            Assert.IsTrue(route.HardCompleted, "困難ルートを踏破。");
            Assert.AreEqual(StoryRoute.Hard, route.LastRoute);
            Assert.AreEqual(StoryRoute.Hard, route.BossRoute, "ボス前（合流点）へ困難ルートから着いた。");
            Assert.IsFalse(route.StandardCompleted, "標準ルートは踏破していない（B の東端から C へ入っていない）。");

            // C のお地蔵様から A へ旅立つ（FT はボス到達経路を変えない）。
            yield return InteractShrine(ShrineC);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            Assert.AreEqual(ShrineMenuResult.FastTravelStarted, shrines.FastTravel(ShrineA), shrines.Message);
            yield return WaitUntilOrTimeout(() => shrines.FastTravelCompletedCount > 0, 30f);
            yield return WaitAreaReady(AreaA);
            Assert.AreEqual(StoryRoute.Hard, session.Story.RouteOf(Chapter).BossRoute, "FT で経路を推測しない。");

            // 案内の老人：困難ルートだけの会話。
            yield return TalkTo(Guide);
            Assert.AreEqual(new StableId("dialogue_p7_guide_hard"), dialogue.Conversation.Dialogue.DialogueId, "困難のみの会話。");
            yield return Tap(Key.Escape);

            // 受注前の達成を、受けた後に認める（発見・複数条件）。自動で受注・報酬はしていない。
            Assert.AreEqual(0, session.QuestStageOf(QuestFind));
            Assert.AreEqual(0, session.QuestStageOf(QuestMulti));
            foreach (StableId q in new[] { QuestFind, QuestMulti })
            {
                yield return TalkTo(Giver);
                yield return AdvanceToChoices(dialogue);
                yield return ChooseQuest(dialogue, DialogueChoiceKind.ListenQuest, q);
                yield return AdvanceToChoices(dialogue);
                yield return ChooseKind(dialogue, DialogueChoiceKind.AcceptQuest);
                Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(session, Quest(q)), q.Value + "：受けた時点で報告可能。");
            }

            yield return WaitSaved("受注 2 件");

            // 再起動・Continue：門・経路・依頼が保持され、門は開いたまま。
            yield return Restart();
            yield return ContinueAndWait(AreaA);
            GameSessionState loaded = Session();
            Assert.IsTrue(loaded.Story.IsEventCompleted(CliffGateEvent));
            Assert.IsTrue(FlagDoor(CliffGateFlag).IsOpened, "Load 後も門は開いている。");
            Assert.AreEqual(StoryRoute.Hard, loaded.Story.RouteOf(Chapter).BossRoute);
            Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(loaded, Quest(QuestFind)));
            Assert.AreEqual(QuestStateKind.Reportable, StoryRules.StateOf(loaded, Quest(QuestMulti)));

            // 報告（2 件。独立に一度ずつ）。
            int total = loaded.Progress.TotalVirtue;
            dialogue = Dialogue();
            foreach (StableId q in new[] { QuestFind, QuestMulti })
            {
                yield return TalkTo(Giver);
                yield return AdvanceToChoices(dialogue);
                yield return ChooseQuest(dialogue, DialogueChoiceKind.ReportQuest, q);
                Assert.AreEqual(QuestReportResult.Reported, dialogue.LastReportResult);
            }

            Assert.AreEqual(total + 60, loaded.Progress.TotalVirtue, "2 件で 60。");
        }

        // ================================================================ P7 11〜P7 13：章ボス

        /// <summary>
        /// P7 11・12：C の仮章ボス（既存の精鋭敵の遭遇戦）を実 Hitbox で倒すと、章クリア・権利 +3（上限 6）・処理済みが 1 つの保存で確定し、
        /// 通知が実際の増加量を示す。会話は章クリア後のものになり、未完了の依頼の報告も残る。再起動・Continue で再付与せず、ボスは復活しない。
        /// </summary>
        [UnityTest, Timeout(540000)]
        public IEnumerator Chapter_RealBossKill_ClearsOnceWithRights_NoReviveAfterContinue()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            CampaignDialogueService dialogue = Dialogue();
            GameSessionState session = Session();

            // 到達の依頼を受けておく（章クリア後も報告できることを見る）。
            yield return TalkTo(Giver);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseQuest(dialogue, DialogueChoiceKind.ListenQuest, QuestReach);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseKind(dialogue, DialogueChoiceKind.AcceptQuest);

            QuietFieldEnemies();
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            yield return SlideTo(ExitBEast, Key.D, Vector3.left, "B→C");
            Assert.AreEqual(StoryRoute.Standard, session.Story.RouteOf(Chapter).BossRoute, "標準ルートでボス前へ。");
            yield return InteractShrine(ShrineC);
            BootstrapServices.Get<CampaignShrineService>().Close();

            int rights = session.Progress.RefundRights;
            int virtue = session.Progress.TotalVirtue;
            AreaEncounterRunner boss = FindRunner(EncounterCBoss);
            yield return StartEncounter(boss);
            int saves = session.Changes.AutosaveRequestCount;
            foreach (EnemyActor e in EnemiesOf(boss))
            {
                yield return KillWithRealHitbox(e);
            }

            yield return WaitUntilOrTimeout(() => boss.State == AreaEncounterState.Cleared, 10f);
            Assert.AreEqual(AreaEncounterState.Cleared, boss.State);
            Assert.IsTrue(session.Story.IsChapterCleared(Chapter), "実撃破で章クリア。");
            Assert.AreEqual(StoryRoute.Standard, session.Story.ClearedRouteOf(Chapter), "クリア時の経路を固定。");
            Assert.IsTrue(boss.LastChapterCommit.ChapterCleared);
            Assert.AreEqual(System.Math.Min(6, rights + 3) - rights, boss.LastChapterCommit.RightsAdded);
            Assert.AreEqual(System.Math.Min(6, rights + 3), session.Progress.RefundRights, "権利 +3（上限 6）。");
            Assert.IsTrue(session.Progress.IsChapterProcessed(Chapter));
            Assert.IsTrue(session.GetOrCreateArea(AreaC).IsBossDefeated(EncounterCBoss));
            Assert.Greater(session.Progress.TotalVirtue, virtue - 1, "既存の撃破報酬はそのまま（章クリアの徳ボーナスは無い）。");
            Assert.Greater(session.Changes.AutosaveRequestCount, saves, "保存を要求した。");
            yield return WaitUntilOrTimeout(() => dialogue.Notice.Contains("クリア"), 3f);
            StringAssert.Contains("払い戻し権利 +" + boss.LastChapterCommit.RightsAdded, dialogue.Notice, "通知は実際の増加量。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current, "現地で探索を続けられる。");
            Assert.IsFalse(Transitions().IsTransitionUnsettled, "次章へ強制転送しない。");
            yield return WaitSaved("章クリア");

            // 保存単位：クリア・ボス撃破・章クリア・権利・処理済みが同じ保存に載る（片方だけの保存は作らない）。
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(Saves().Coordinator.Store.DecideLoad().Chosen.Json, out _,
                out SaveSnapshot saved, out string savedErr), savedErr);
            Assert.AreEqual(1, saved.ClearedChapters.Count, "章クリアが保存に載る。");
            CollectionAssert.Contains(saved.ProcessedChapters, Chapter.Value, "処理済みの章が同じ保存に載る。");
            Assert.AreEqual(session.Progress.RefundRights, saved.RefundRights);
            bool bossSaved = false;
            foreach (AreaSaveRecord a in saved.Areas)
            {
                bossSaved |= a.AreaId == AreaC.Value && new List<string>(a.DefeatedBosses).Contains(EncounterCBoss.Value)
                    && new List<string>(a.ClearedEncounters).Contains(EncounterCBoss.Value);
            }

            Assert.IsTrue(bossSaved, "ボス撃破と遭遇戦クリアが同じ保存に載る。");

            // 再起動・Continue：再付与しない、ボスは復活しない、章クリア後の会話・報告が残る。
            int rightsAfter = session.Progress.RefundRights;
            yield return Restart();
            yield return ContinueAndWait(AreaC);
            GameSessionState loaded = Session();
            Assert.IsTrue(loaded.Story.IsChapterCleared(Chapter));
            Assert.AreEqual(rightsAfter, loaded.Progress.RefundRights, "Load で権利を再付与しない。");
            AreaEncounterRunner bossAgain = FindRunner(EncounterCBoss);
            Assert.AreNotEqual(AreaEncounterState.Playing, bossAgain.State, "撃破後に戦闘を始め直さない。");
            Assert.AreEqual(0, AliveNonFieldEnemies(), "撃破後に復活しない。");
            Assert.AreEqual(0, Dialogue().ChapterNoticeCount, "Load で章クリアの通知を再生しない。");

            yield return InteractShrine(ShrineC);
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            Assert.AreEqual(ShrineMenuResult.FastTravelStarted, shrines.FastTravel(ShrineA), shrines.Message);
            yield return WaitUntilOrTimeout(() => shrines.FastTravelCompletedCount > 0, 30f);
            yield return WaitAreaReady(AreaA);
            dialogue = Dialogue();
            yield return TalkTo(Giver);
            Assert.AreEqual(new StableId("dialogue_p7_giver_cleared"), dialogue.Conversation.Dialogue.DialogueId, "章クリア後の会話。");
            yield return AdvanceToChoices(dialogue);
            yield return ChooseQuest(dialogue, DialogueChoiceKind.ReportQuest, QuestReach);
            Assert.AreEqual(QuestReportResult.Reported, dialogue.LastReportResult, "章クリア後も報告できる。");
            Assert.AreEqual(rightsAfter, loaded.Progress.RefundRights);
        }

        /// <summary>
        /// P7 13：章ボスと主人公が同じフレームに倒れたら、章クリアを保持して主人公は死亡再開する。通知は復帰の後に出る。
        /// 再開後の保存に章クリアと全回復の両方が載る。
        /// </summary>
        [UnityTest, Timeout(480000)]
        public IEnumerator Chapter_SimultaneousDeath_KeepsClear_NoticeAfterRespawn()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            GameSessionState session = Session();
            CampaignDialogueService dialogue = Dialogue();
            QuietFieldEnemies();
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            yield return SlideTo(ExitBEast, Key.D, Vector3.left, "B→C");
            yield return InteractShrine(ShrineC);
            BootstrapServices.Get<CampaignShrineService>().Close();

            // 犬丸の「かばう」が主人公への命中を肩代わりしないよう、先に Down させる。
            yield return KnockDownDog();
            AreaEncounterRunner boss = FindRunner(EncounterCBoss);
            yield return StartEncounter(boss);
            EnemyActor[] bosses = EnemiesOf(boss);
            Assert.AreEqual(1, bosses.Length, "前提：仮章ボスは 1 体。");
            EnemyActor enemy = bosses[0];
            var (_, _, vitals, _) = Active();

            // 同じフレームで：ボスへ致命の命中（主人公側）→ 主人公へ致命の命中（敵側）。LateUpdate の前に両方が確定する。
            var playerSide = new GameObject("P7PlayerSide").AddComponent<PlayerSideActor>();
            var enemySide = new GameObject("P7EnemySide").AddComponent<EnemySideActor>();
            yield return new WaitForEndOfFrame();
            enemy.ReceiveHit(new HitInfo(playerSide, enemy, Vector3.forward, enemy.transform.position,
                new HitDamage(99999, 999f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(7701)));
            vitals.ReceiveHit(new HitInfo(enemySide, vitals, Vector3.back, vitals.transform.position,
                new HitDamage(99999, 0f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(7702)));
            Assert.IsTrue(enemy.IsDefeated, "前提：同じフレームでボスが倒れた（HP=" + enemy.CurrentHp + "）。");
            Assert.IsTrue(vitals.IsDefeated, "前提：同じフレームで主人公が倒れた。");
            Assert.IsTrue(boss.State == AreaEncounterState.Playing, "前提：勝敗はまだ確定していない（次の LateUpdate で確定）。");
            yield return null; // 次のフレームの Update の後（LateUpdate の前）
            yield return null; // 次のフレームの LateUpdate で勝敗を確定した後
            Object.Destroy(playerSide.gameObject);
            Object.Destroy(enemySide.gameObject);

            Assert.IsTrue(session.Story.IsChapterCleared(Chapter), "同時死亡でも章クリアを保持する。");
            Assert.IsTrue(session.Progress.IsChapterProcessed(Chapter));
            Assert.AreNotEqual(GameMode.Exploration, GameModeProvider.Current.Current, "死亡側がモードを持つ（探索へ戻さない）。");
            Assert.IsTrue(dialogue.HasDeferredNotice, "章クリアの通知は復帰まで保留。");

            yield return WaitForRespawnPrompt();
            AreaTransitionService transitions = Transitions();
            int completed = transitions.CompletedCount;
            yield return PressKeyUntil(Key.Enter, () => transitions.CompletedCount > completed || transitions.HasTerminalFailure, 25f);
            yield return WaitAreaReady(AreaC);
            yield return WaitUntilOrTimeout(() => !dialogue.HasDeferredNotice, 3f);
            Assert.IsFalse(dialogue.HasDeferredNotice, "復帰後に通知を出す。");
            StringAssert.Contains("クリア", dialogue.Notice);
            yield return WaitSaved("再開");
            Assert.IsTrue(SaveJsonCodec.TryDeserialize(Saves().Coordinator.Store.DecideLoad().Chosen.Json, out _,
                out SaveSnapshot after, out string err), err);
            Assert.Greater(after.Party.Player.Hp, 0, "再開後の保存は全回復。");
            Assert.AreEqual(1, after.ClearedChapters.Count, "章クリアが載っている（片方の Snapshot で上書きされない）。");
            Assert.AreEqual(0, AliveNonFieldEnemies(), "ボスは復活しない。");
        }

        // ================================================================ P7 15：会話中の終了と Continue

        /// <summary>
        /// P7 15：依頼の内容の画面（未決定）のまま正常終了して Continue すると、会話は閉じて中立の状態から始まり、受注は実行されない。
        /// 停止の保持も残らない。回復・普通敵の復活も起こさない。
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Dialogue_QuitMidDecision_ContinueIsNeutral_NoPendingAction()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            CampaignDialogueService dialogue = Dialogue();
            var (_, _, vitals, _) = Active();
            vitals.Vitals.Health.SetCurrent(vitals.Vitals.Health.Max - 17);
            int hp = vitals.Vitals.Health.Current;
            int cycle = Session().RespawnCycle;

            yield return TalkTo(Giver);
            yield return AdvanceToChoices(dialogue);
            yield return ChooseQuest(dialogue, DialogueChoiceKind.ListenQuest, QuestReach);
            yield return AdvanceToChoices(dialogue);
            Assert.IsTrue(HasChoice(dialogue, DialogueChoiceKind.AcceptQuest), "前提：受注の決定の直前。");

            // 正常終了（保存を終えてから）。
            CampaignSaveService saves = Saves();
            bool done = false;
            SaveExitOutcome outcome = SaveExitOutcome.TimedOut;
            saves.StartCoroutine(saves.SaveBeforeExit(o =>
            {
                outcome = o;
                done = true;
            }));
            yield return WaitUntilOrTimeout(() => done, 10f);
            Assert.AreEqual(SaveExitOutcome.Saved, outcome);
            Assert.IsTrue(dialogue.IsOpen, "保存の採取は会話を壊さない。");

            yield return Restart();
            Assert.AreEqual(0, GameplayClockProvider.HoldCount, "停止の保持を残さない。");
            yield return ContinueAndWait(AreaA);
            Assert.IsFalse(Dialogue().IsOpen, "会話は閉じた状態で再開。");
            Assert.AreEqual(GameMode.Exploration, GameModeProvider.Current.Current);
            Assert.IsFalse(GameplayClockProvider.IsFrozen);
            Assert.AreEqual(0, Session().QuestStageOf(QuestReach), "未決定の受注は実行しない。");
            Assert.AreEqual(hp, Active().vitals.Vitals.Health.Current, "回復しない。");
            Assert.AreEqual(cycle, Session().RespawnCycle, "普通敵を復活させない（周期を進めない）。");
        }

        // ================================================================ 補助：会話

        private IEnumerator PlaceAtVillager(StableId villagerId)
        {
            VillagerPoint point = Villager(villagerId);
            Place(Active().root, point.InteractionAnchor + new Vector3(0f, 0f, -1.0f));
            yield return new WaitForFixedUpdate();
            Active().facing.ConfirmFromInput(Vector2.up);
            yield return null;
        }

        private IEnumerator TalkTo(StableId villagerId)
        {
            CampaignDialogueService dialogue = Dialogue();
            yield return PlaceAtVillager(villagerId);
            int opens = dialogue.OpenCount;
            yield return PressKeyUntil(Key.E, () => dialogue.OpenCount > opens, 4f);
            Assert.Greater(dialogue.OpenCount, opens, villagerId.Value + " に話しかけられる（拒否=" + dialogue.LastRejection + "）。");
            yield return WaitUntilOrTimeout(() => !dialogue.AwaitingRelease, 2f);
            yield return null;
        }

        private IEnumerator AdvanceToChoices(CampaignDialogueService dialogue)
        {
            for (int guard = 0; guard < 20 && dialogue.IsOpen && !dialogue.Conversation.ShowsChoices; guard++)
            {
                int before = dialogue.Conversation.PageIndex;
                yield return Tap(Key.Enter);
                Assert.AreEqual(before + 1, dialogue.Conversation.PageIndex, "1 回の押下で 1 ページ。");
            }

            Assert.IsTrue(dialogue.IsOpen && dialogue.Conversation.ShowsChoices, "選択肢まで進む。");
            yield return null;
            yield return null;
        }

        private IEnumerator ChooseKind(CampaignDialogueService dialogue, DialogueChoiceKind kind) =>
            ChooseQuest(dialogue, kind, default);

        private IEnumerator ChooseQuest(CampaignDialogueService dialogue, DialogueChoiceKind kind, StableId target)
        {
            IReadOnlyList<DialogueChoice> choices = dialogue.Conversation.Current.Choices;
            int index = -1;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].Kind == kind && (target.IsEmpty || choices[i].TargetId.Equals(target)))
                {
                    index = i;
                    break;
                }
            }

            Assert.GreaterOrEqual(index, 0, kind + " " + target.Value + " の選択肢がある。");
            DialogueScreen screen = dialogue.Conversation.Current;
            for (int i = 0; i < index; i++)
            {
                yield return Tap(Key.DownArrow);
            }

            yield return Tap(Key.Enter);
            yield return null;
            if (kind == DialogueChoiceKind.ListenQuest)
            {
                Assert.AreNotSame(screen, dialogue.Conversation.Current, "依頼の内容の画面へ。");
            }
        }

        private IEnumerator KnockDownDog()
        {
            CompanionHitReceiver dog = Dog();
            if (dog == null || dog.Vitals.IsDown)
            {
                yield break;
            }

            var attackerGo = new GameObject("P7CompanionAttacker");
            var attacker = attackerGo.AddComponent<EnemySideActor>();
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
                    yield return null;
                }
            }

            Object.DestroyImmediate(attackerGo);
            Assert.IsTrue(dog.Vitals.IsDown, "前提：犬丸が Down。");
        }

        private static float Stamina(PlayerVitalsHolder vitals) => vitals.ExportTransferSnapshot().Stamina.Current;

        private static bool HasChoice(CampaignDialogueService dialogue, DialogueChoiceKind kind)
        {
            if (!dialogue.IsOpen || !dialogue.Conversation.ShowsChoices)
            {
                return false;
            }

            foreach (DialogueChoice c in dialogue.Conversation.Current.Choices)
            {
                if (c.Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>調べるの受付（Update の段）と同じフレームの被弾（LateUpdate の前）を作る。</summary>
        private void InteractNowAndHitInSameFrame(PlayerVitalsHolder vitals, EnemySideActor attacker)
        {
            AreaInteractInput input = InActiveScene<AreaInteractInput>();
            Assert.IsNotNull(input);
            Assert.IsTrue(input.Controller.TryInteract(out AreaInteractionOutcome outcome), "前提：調べるの受付。");
            Assert.IsTrue(outcome.Handled, "受付は通る（開くのは LateUpdate）。理由=" + outcome.Message);
            Assert.IsTrue(Dialogue().HasPendingStart);
            vitals.ReceiveHit(new HitInfo(attacker, vitals, Vector3.back, vitals.transform.position,
                new HitDamage(5, 0f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(7710)));
        }

        // ================================================================ 補助：移動・戦闘

        private IEnumerator UseDoor(StableId doorId, StableId expectedArea)
        {
            AreaTransitionDoor door = null;
            foreach (AreaTransitionDoor d in Object.FindObjectsByType<AreaTransitionDoor>(FindObjectsSortMode.None))
            {
                if (d.InteractableId.Equals(doorId) && d.gameObject.scene == CurrentScene())
                {
                    door = d;
                }
            }

            Assert.IsNotNull(door, "扉 " + doorId.Value + " がある。");
            AreaTransitionService transitions = Transitions();
            int completed = transitions.CompletedCount;
            Vector3 toward = Flat(door.InteractionAnchor - Active().root.transform.position);
            Place(Active().root, door.InteractionAnchor - (toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.forward) * 0.9f);
            yield return new WaitForFixedUpdate();
            Vector3 dir = Flat(door.InteractionAnchor - Active().root.transform.position).normalized;
            Active().facing.ConfirmFromInput(new Vector2(dir.x, dir.z));
            yield return null;
            yield return PressKeyUntil(Key.E, () => door.RequestCount > 0 || transitions.CompletedCount > completed, 4f);
            yield return WaitUntilOrTimeout(() => transitions.CompletedCount > completed || transitions.HasTerminalFailure, 30f);
            Assert.IsFalse(transitions.HasTerminalFailure, "扉の移動が終端失敗しない: " + transitions.TerminalFailureReason);
            yield return WaitAreaReady(expectedArea);
        }

        private IEnumerator SlideTo(StableId exitId, Key key, Vector3 back, string label)
        {
            AreaTransitionService transitions = Transitions();
            int expected = transitions.SlideCommittedCount + 1;
            AreaExitGate gate = FindExitGate(exitId);
            Place(Active().root, gate.transform.position + back * 0.4f);
            yield return new WaitForFixedUpdate();
            yield return null;

            float deadline = Time.realtimeSinceStartup + 25f;
            float nextRelease = Time.realtimeSinceStartup + 1.5f;
            while (transitions.SlideCommittedCount < expected && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                bool release = Time.realtimeSinceStartup >= nextRelease;
                InputSystem.QueueStateEvent(_keyboard, release ? new KeyboardState() : new KeyboardState(key));
                if (release)
                {
                    nextRelease = Time.realtimeSinceStartup + 1.5f;
                    if (!gate.PlayerInside && transitions.SlideCommittedCount < expected && !transitions.Slide.IsTransitioning)
                    {
                        Place(Active().root, gate.transform.position + back * 0.4f);
                    }
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.IsFalse(transitions.HasTerminalFailure, label + "：終端失敗。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(expected, transitions.SlideCommittedCount, label + "：スライドで渡れていない。");

            float quietDeadline = Time.realtimeSinceStartup + 20f;
            int quiet = 0;
            while (quiet < 3 && Time.realtimeSinceStartup < quietDeadline)
            {
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
                if (p.ShrineId.Equals(shrineId) && p.gameObject.scene == CurrentScene())
                {
                    point = p;
                }
            }

            Assert.IsNotNull(point, "お地蔵様 " + shrineId.Value + " がある。");
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            var (root, _, _, facing) = Active();
            Place(root, point.InteractionAnchor + new Vector3(0f, 0f, 1.0f));
            yield return new WaitForFixedUpdate();
            facing.ConfirmFromInput(Vector2.down);
            yield return null;
            int before = point.InteractCount;
            yield return PressKeyUntil(Key.E, () => point.InteractCount > before, 6f);
            Assert.Greater(point.InteractCount, before, "実キー E でお地蔵様を調べられる。");
            Assert.IsTrue(shrines.IsMenuOpen, "メニューが開く（" + shrines.Message + "）。");
        }

        private static IEnumerator StartEncounter(AreaEncounterRunner runner)
        {
            AreaEncounterTrigger trigger = null;
            foreach (AreaEncounterTrigger t in Object.FindObjectsByType<AreaEncounterTrigger>(FindObjectsSortMode.None))
            {
                System.Reflection.FieldInfo f = typeof(AreaEncounterTrigger).GetField("_runner",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null && ReferenceEquals(f.GetValue(t), runner))
                {
                    trigger = t;
                }
            }

            Assert.IsNotNull(trigger, "遭遇戦 " + runner.EncounterId.Value + " の Trigger がある。");
            Place(Active().root, trigger.transform.position);
            yield return new WaitForFixedUpdate();
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 6f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State, "Trigger 進入で戦闘が始まる。拒否=" + runner.LastRejection);
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

            // P6 の複数遭遇戦の構成では出現先が実行役の子でないことがある（P6C の試験と同じ扱い）。活動中の Area の、普通敵でない敵。
            if (list.Count == 0 && runner.State == AreaEncounterState.Playing)
            {
                foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
                {
                    if (e != null && !e.IsDefeated && e.gameObject.scene == CurrentScene()
                        && e.GetComponentInParent<AreaFieldEnemyDirector>() == null)
                    {
                        list.Add(e);
                    }
                }
            }

            return list.ToArray();
        }

        private static int AliveNonFieldEnemies()
        {
            int n = 0;
            foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                if (e != null && !e.IsDefeated && e.gameObject.activeInHierarchy && e.gameObject.scene == CurrentScene()
                    && e.GetComponentInParent<AreaFieldEnemyDirector>() == null)
                {
                    n++;
                }
            }

            return n;
        }

        private IEnumerator KillWithRealHitbox(EnemyActor enemy)
        {
            if (enemy == null || enemy.IsDefeated)
            {
                yield break;
            }

            var playerRoot = Active().root;
            var facing = Active().facing;
            var vitals = Active().vitals;
            float deadline = Time.realtimeSinceStartup + 60f;
            float nextPress = 0f;
            bool pressed = false;
            while (Time.realtimeSinceStartup < deadline && enemy != null && !enemy.IsDefeated)
            {
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
            Assert.IsTrue(enemy == null || enemy.IsDefeated, "実 Hitbox で敵を倒せていない（HP=" + (enemy != null ? enemy.CurrentHp : 0) + "）。");
        }

        private static IEnumerator WaitForRespawnPrompt()
        {
            var view = Object.FindFirstObjectByType<Momotaro.Presentation.Hud.CampaignRespawnView>();
            Assert.IsNotNull(view, "再開操作の表示がある。");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!view.IsShowing && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(view.IsShowing, "倒れたら再開操作が出る。");
        }

        private sealed class EnemySideActor : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public int ActorId => GetInstanceID();
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
        }

        private sealed class PlayerSideActor : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Player;
            public int FloorId => 0;
            public int ActorId => GetInstanceID();
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
        }

        // ================================================================ 補助：起動・保存

        private IEnumerator NewGame()
        {
            yield return StartBootstrap();
            CampaignAdventureFlow flow = Saves().Flow;
            Assert.IsNotNull(flow);
            Assert.IsTrue(flow.TryNewGame(Catalog(), out string error), "New Game を受理する。理由=" + error);
            yield return WaitAreaReady(AreaA);
            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
        }

        private IEnumerator StartBootstrap()
        {
            DestroyBootstrap();
            _bootstrap = new GameObject("BootstrapRoot_P7WorldTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded, "常駐が立つ。理由=" + BootstrapRoot.Instance.BootstrapFailure);
            Saves().SaveDirectory = _saveDir;
        }

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
            Assert.IsFalse(c.IsDirty, label + "：保存が終わらない（状態=" + c.Status + "）。");
        }

        private IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
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

        // ================================================================ 補助：探索

        private static (PlayerRoot root, PlayerStateController state, PlayerVitalsHolder vitals, PlayerFacing facing) Active()
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            Assert.IsNotNull(bundle, "活動中のエリアがある。");
            Assert.IsTrue(bundle.TryResolve(out PlayerRoot root), "活動中のエリアに主人公がいる。");
            return (root, root.GetComponentInChildren<PlayerStateController>(), root.GetComponentInChildren<PlayerVitalsHolder>(),
                root.GetComponentInChildren<PlayerFacing>());
        }

        private static T InActiveScene<T>() where T : Component
        {
            Scene scene = CurrentScene();
            foreach (T c in Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c.gameObject.scene == scene)
                {
                    return c;
                }
            }

            return null;
        }

        private static VillagerPoint Villager(StableId id)
        {
            foreach (VillagerPoint v in Object.FindObjectsByType<VillagerPoint>(FindObjectsSortMode.None))
            {
                if (v.VillagerId.Equals(id) && v.gameObject.scene == CurrentScene())
                {
                    return v;
                }
            }

            Assert.Fail("住民 " + id.Value + " が活動中のエリアにいない。");
            return null;
        }

        private static AreaFlagDoor FlagDoor(StableId flagId)
        {
            foreach (AreaFlagDoor d in Object.FindObjectsByType<AreaFlagDoor>(FindObjectsSortMode.None))
            {
                if (d.FlagId.Equals(flagId) && d.gameObject.scene == CurrentScene())
                {
                    return d;
                }
            }

            Assert.Fail("門 " + flagId.Value + " が活動中のエリアにない。");
            return null;
        }

        private static AreaPickupPoint Pickup(StableId placementId)
        {
            foreach (AreaPickupPoint p in Object.FindObjectsByType<AreaPickupPoint>(FindObjectsSortMode.None))
            {
                if (p.PlacementId.Equals(placementId) && p.gameObject.scene == CurrentScene())
                {
                    return p;
                }
            }

            Assert.Fail("配置物 " + placementId.Value + " が活動中のエリアにない。");
            return null;
        }

        private static CompanionHitReceiver Dog()
        {
            foreach (CompanionHitReceiver d in Object.FindObjectsByType<CompanionHitReceiver>(FindObjectsSortMode.None))
            {
                if (d != null && d.gameObject.scene == CurrentScene())
                {
                    return d;
                }
            }

            return null;
        }

        private static void QuietFieldEnemies()
        {
            foreach (AreaFieldEnemyDirector d in Object.FindObjectsByType<AreaFieldEnemyDirector>(FindObjectsSortMode.None))
            {
                d.gameObject.SetActive(false);
            }
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

        private static Scene CurrentScene()
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            return bundle != null ? bundle.gameObject.scene : default;
        }

        private static void Place(PlayerRoot root, Vector3 position)
        {
            root.transform.position = new Vector3(position.x, root.transform.position.y, position.z);
            if (root.Body != null)
            {
                root.Body.position = root.transform.position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static QuestInfo Quest(StableId id)
        {
            Assert.IsTrue(Story().TryGetQuest(id, out QuestInfo q));
            return q;
        }

        private static StoryCatalog Story()
        {
            StoryCatalog story = Dialogue().Story;
            Assert.IsNotNull(story, "P7 の会話 Data がある。");
            return story;
        }

        private static CampaignDialogueService Dialogue()
        {
            CampaignDialogueService service = BootstrapServices.Get<CampaignDialogueService>();
            Assert.IsNotNull(service, "会話サービスが常駐していない。");
            return service;
        }

        private static AreaCatalogData Catalog()
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<AreaCatalogData>(CatalogPath);
            Assert.IsNotNull(data, "P7 のカタログがある（build-phase7-world）。");
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
            GameplayClockProvider.ClearHolds();
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            ShrineOperationsProvider.Current = null;
            KibidangoUseProvider.Current = null;
            DialogueOperationsProvider.Current = null;
            ChapterProgressProvider.Current = null;
            AreaPendingArrival.Clear();
        }

        private void RemoveDevices()
        {
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
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
