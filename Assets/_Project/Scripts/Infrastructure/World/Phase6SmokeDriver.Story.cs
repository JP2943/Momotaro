using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Momotaro.Core.Identification;
using Momotaro.Data.Story;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Story;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Save;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// P7 18 の実ビルド確認（<c>-p6a-smoke story</c>）。P7 の検証ワールドで、<b>実キー（仮想キーボード）</b>により
    /// 依頼を受け → 門の会話を最後まで読んで確定 → A→B→C をスライドで渡り（標準ルート）→ C のお地蔵様に登録 →
    /// 仮章ボスを実 Hitbox で倒して章クリア → お地蔵様の旅立ちで A へ → 依頼を報告して徳を受け取り → その徳で成長 →
    /// 値を書き出して正常終了の保存 → 終了。別プロセスの <c>continue</c> が同じ値（徳・依頼・イベント・経路・章・権利・成長）を
    /// 書き出すかを Editor 側（<c>Phase6PlayerSmoke.BuildAllP7</c>）で突き合わせる。
    /// 普通敵は止める（確認の対象は会話・依頼・章の確定と保存。普通敵の挙動は P6 の確認が持つ）。
    /// </summary>
    public sealed partial class Phase6SmokeDriver
    {
        private static readonly StableId P7Guide = new StableId("villager_p7_guide");
        private static readonly StableId P7Giver = new StableId("villager_p7_giver");
        private static readonly StableId P7QuestReach = new StableId("quest_p7_reach_junction");
        private static readonly StableId P7CliffEvent = new StableId("event_p7_cliff_gate");
        private static readonly StableId P7CliffFlag = new StableId("flag_p7_a_cliff_gate");
        private static readonly StableId P7AreaA = new StableId("area_p7_a");
        private static readonly StableId P7AreaC = new StableId("area_p7_c");
        private static readonly StableId P7ExitAEast = new StableId("exit_p6_a_east");
        private static readonly StableId P7ExitBEast = new StableId("exit_p6_b_east");
        private static readonly StableId P7ShrineA = new StableId("shrine_p6_a");
        private static readonly StableId P7ShrineC = new StableId("shrine_p6_c");
        private static readonly StableId P7BossEncounter = new StableId("encounter_p7_c_boss");
        private static readonly StableId P7Growth = new StableId("growth_p7_vit_01");

        private string _storyStep = string.Empty;

        private IEnumerator StoryThenExit()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            _keyboard = InputSystem.AddDevice<Keyboard>("P7SmokeKeyboard");
            yield return null;
            CampaignDialogueService dialogue = BootstrapServices.Get<CampaignDialogueService>();
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            GameSessionState session = GameSessionProvider.Current;
            if (dialogue == null || shrines == null || session == null)
            {
                _result["error"] = "no dialogue/shrine service";
                Finish();
                yield break;
            }

            IEnumerator steps = StorySteps(dialogue, shrines, session);
            while (true)
            {
                if (_result.ContainsKey("error"))
                {
                    break;
                }

                bool more;
                try
                {
                    more = steps.MoveNext();
                }
                catch (Exception e)
                {
                    _result["error"] = "step " + _storyStep + ": " + e.GetType().Name + " " + e.Message;
                    break;
                }

                if (!more || _result.ContainsKey("error"))
                {
                    break;
                }

                yield return steps.Current;
            }

            if (_keyboard != null)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            }

            if (_result.ContainsKey("error"))
            {
                _result["failedStep"] = _storyStep;
                Finish();
                yield break;
            }

            yield return WaitSaved();
            _result["adventureId"] = session.AdventureId;
            _result["kibidango"] = session.Kibidango.ToString(CultureInfo.InvariantCulture);
            _result["area"] = CurrentAreaProvider.Current.AreaId.Value;
            WriteProgress("before");
            WriteStory("before");
            SaveExitOutcome outcome = SaveExitOutcome.TimedOut;
            yield return Saves.SaveBeforeExit(o => outcome = o);
            _result["exitOutcome"] = outcome.ToString();
            Finish();
        }

        private IEnumerator StorySteps(CampaignDialogueService dialogue, CampaignShrineService shrines, GameSessionState session)
        {
            // 1. 依頼を受ける（会話を実キーで最後まで読み、選択肢を矢印と Enter で選ぶ）。
            _storyStep = "accept";
            yield return Talk(dialogue, P7Giver);
            yield return AdvanceToChoices(dialogue);
            yield return Choose(dialogue, DialogueChoiceKind.ListenQuest, P7QuestReach);
            yield return AdvanceToChoices(dialogue);
            yield return Choose(dialogue, DialogueChoiceKind.AcceptQuest, default);
            Require(session.QuestStageOf(P7QuestReach) == 1, "quest not accepted: " + dialogue.LastAcceptResult);
            _result["acceptResult"] = dialogue.LastAcceptResult.ToString();

            // 2. 門の会話を最後まで読んで確定（門がその場で開く）。
            _storyStep = "gate";
            yield return Talk(dialogue, P7Guide);
            yield return AdvanceToChoices(dialogue);
            yield return Choose(dialogue, DialogueChoiceKind.ConfirmEvent, default);
            Require(session.Story.IsEventCompleted(P7CliffEvent), "gate event not completed");
            AreaFlagDoor gate = FindInArea<AreaFlagDoor>(d => d.FlagId.Equals(P7CliffFlag));
            Require(gate != null && gate.IsOpened, "gate did not open");
            _result["gateOpened"] = gate != null && gate.IsOpened ? "true" : "false";
            yield return WaitSaved();

            // 3. 標準ルート：A → B → C（スライド）。
            _storyStep = "route";
            QuietFieldEnemies();
            yield return SlideThrough(P7ExitAEast, Key.D, Vector3.left);
            QuietFieldEnemies();
            yield return SlideThrough(P7ExitBEast, Key.D, Vector3.left);
            Require(CurrentAreaProvider.Current.AreaId.Equals(P7AreaC), "not in C");
            QuietFieldEnemies();
            QuestInfo reach = null;
            StoryCatalog story = BootstrapServices.Get<AreaTransitionService>().Catalog.Campaign.Story;
            foreach (QuestInfo q in story.Quests)
            {
                if (q.QuestId.Equals(P7QuestReach))
                {
                    reach = q;
                }
            }

            Require(reach != null && StoryRules.StateOf(session, reach) == QuestStateKind.Reportable, "quest not reportable at C");
            _result["reportableAtC"] = "true";

            // 4. C のお地蔵様に登録。
            _storyStep = "shrineC";
            yield return InteractShrineByKey(shrines, P7ShrineC);
            shrines.Close();
            yield return null;

            // 5. 仮章ボスを実 Hitbox で倒す → 章クリア・権利・通知。
            _storyStep = "boss";
            int rightsBefore = session.Progress.RefundRights;
            AreaEncounterRunner boss = FindInArea<AreaEncounterRunner>(r => r.EncounterId.Equals(P7BossEncounter));
            Require(boss != null, "no boss encounter");
            yield return EnterEncounter(boss);
            for (int guard = 0; guard < 8 && boss.State == AreaEncounterState.Playing; guard++)
            {
                Momotaro.Gameplay.Enemy.EnemyActor target = null;
                foreach (Momotaro.Gameplay.Enemy.EnemyActor e in EncounterEnemies(boss))
                {
                    target = e;
                    break;
                }

                if (target == null)
                {
                    yield return null;
                    continue;
                }

                bool killed = false;
                yield return KillWithAttacks(target, k => killed = k, 60f);
                if (!killed)
                {
                    Fail("boss enemy not killed: " + (_result.TryGetValue("firstKillFailure", out string why) ? why : "?"));
                    yield break;
                }
            }

            float clearDeadline = Time.realtimeSinceStartup + 10f;
            while (boss.State != AreaEncounterState.Cleared && Time.realtimeSinceStartup < clearDeadline)
            {
                yield return null;
            }

            StableId chapterId = default;
            foreach (ChapterInfo c in story.Chapters)
            {
                chapterId = c.ChapterId;
            }

            Require(boss.State == AreaEncounterState.Cleared, "boss encounter not cleared: " + boss.State);
            Require(session.Story.IsChapterCleared(chapterId), "chapter not cleared");
            float noticeDeadline = Time.realtimeSinceStartup + 5f;
            while ((dialogue.ChapterNoticeCount == 0 || dialogue.HasDeferredNotice) && Time.realtimeSinceStartup < noticeDeadline)
            {
                yield return null;
            }

            _result["chapterNotice"] = dialogue.Notice;
            _result["chapterNoticeCount"] = dialogue.ChapterNoticeCount.ToString(CultureInfo.InvariantCulture);
            _result["rightsAdded"] = (session.Progress.RefundRights - rightsBefore).ToString(CultureInfo.InvariantCulture);
            _result["expectedRightsAdded"] = (Math.Min(6, rightsBefore + 3) - rightsBefore).ToString(CultureInfo.InvariantCulture);
            yield return WaitSaved();

            // 6. お地蔵様の旅立ちで A へ。
            _storyStep = "fastTravel";
            yield return InteractShrineByKey(shrines, P7ShrineC);
            int travels = shrines.FastTravelCompletedCount;
            Require(shrines.FastTravel(P7ShrineA) == ShrineMenuResult.FastTravelStarted, "fast travel refused: " + shrines.Message);
            float travelDeadline = Time.realtimeSinceStartup + 30f;
            while (shrines.FastTravelCompletedCount == travels && Time.realtimeSinceStartup < travelDeadline)
            {
                yield return null;
            }

            yield return WaitArrived();
            Require(CurrentAreaProvider.Current.AreaId.Equals(P7AreaA), "not in A after fast travel");
            QuietFieldEnemies();

            // 7. 報告（30 徳が一度だけ）。
            _storyStep = "report";
            int total = session.Progress.TotalVirtue;
            yield return Talk(dialogue, P7Giver);
            yield return AdvanceToChoices(dialogue);
            yield return Choose(dialogue, DialogueChoiceKind.ReportQuest, P7QuestReach);
            Require(dialogue.LastReportResult == QuestReportResult.Reported, "report failed: " + dialogue.LastReportResult);
            _result["reportResult"] = dialogue.LastReportResult.ToString();
            _result["reportGranted"] = dialogue.LastGrantedVirtue.ToString(CultureInfo.InvariantCulture);
            _result["reportVirtueDelta"] = (session.Progress.TotalVirtue - total).ToString(CultureInfo.InvariantCulture);
            yield return WaitSaved();

            // 8. 得た徳で成長する。
            _storyStep = "growth";
            yield return InteractShrineByKey(shrines, P7ShrineA);
            ShrineMenuResult bought = shrines.PurchaseGrowth(P7Growth);
            Require(bought == ShrineMenuResult.GrowthPurchased, "growth purchase failed: " + shrines.Message);
            shrines.Close();
            yield return null;
            _storyStep = "done";
        }

        private void Require(bool condition, string message)
        {
            if (!condition)
            {
                Fail(message);
            }
        }

        private void Fail(string message)
        {
            if (!_result.ContainsKey("error"))
            {
                _result["error"] = "step " + _storyStep + ": " + message;
            }
        }

        // ---------------------------------------------------------------- 書き出し（Continue との突き合わせ）

        /// <summary>徳の総量・依頼の段・完了イベント・経路・章・処理済み・ボス撃破を書き出す（P7 18）。</summary>
        private void WriteStory(string prefix)
        {
            GameSessionState session = GameSessionProvider.Current;
            AreaTransitionService t = BootstrapServices.Get<AreaTransitionService>();
            StoryCatalog story = t != null && t.Catalog != null && t.Catalog.Campaign != null ? t.Catalog.Campaign.Story : null;
            if (session == null || story == null)
            {
                return;
            }

            _result[prefix + "TotalVirtue"] = session.Progress.TotalVirtue.ToString(CultureInfo.InvariantCulture);
            var quests = new List<string>();
            foreach (QuestInfo q in story.Quests)
            {
                quests.Add(q.QuestId.Value + "=" + session.QuestStageOf(q.QuestId) + ":" + StoryRules.StateOf(session, q));
            }

            _result[prefix + "Quests"] = string.Join(" ", quests);
            var events = new List<string>();
            foreach (StoryEventInfo e in story.Events)
            {
                if (session.Story.IsEventCompleted(e.EventId))
                {
                    events.Add(e.EventId.Value);
                }
            }

            _result[prefix + "Events"] = string.Join("+", events);
            var routes = new List<string>();
            var chapters = new List<string>();
            var bosses = new List<string>();
            foreach (ChapterInfo c in story.Chapters)
            {
                ChapterRouteRecord r = session.Story.RouteOf(c.ChapterId);
                routes.Add(c.ChapterId.Value + ":std" + (r.StandardReached ? "R" : "-") + (r.StandardCompleted ? "C" : "-")
                    + " hard" + (r.HardReached ? "R" : "-") + (r.HardCompleted ? "C" : "-") + " last=" + r.LastRoute + " boss=" + r.BossRoute);
                chapters.Add(c.ChapterId.Value + ":" + (session.Story.IsChapterCleared(c.ChapterId)
                    ? "cleared(" + session.Story.ClearedRouteOf(c.ChapterId) + ")" : "open")
                    + (session.Progress.IsChapterProcessed(c.ChapterId) ? " processed" : string.Empty));
                bosses.Add(c.BossId.Value + ":" + (session.GetOrCreateArea(c.BossAreaId).IsBossDefeated(c.BossId) ? "defeated" : "alive"));
            }

            _result[prefix + "Routes"] = string.Join(" | ", routes);
            _result[prefix + "Chapters"] = string.Join(" ", chapters);
            _result[prefix + "Bosses"] = string.Join(" ", bosses);
            CampaignDialogueService dialogue = BootstrapServices.Get<CampaignDialogueService>();
            if (dialogue != null)
            {
                _result[prefix + "DialogueOpen"] = dialogue.IsOpen ? "true" : "false";
                _result[prefix + "NoticeCount"] = dialogue.NoticeCount.ToString(CultureInfo.InvariantCulture);
            }
        }

        // ---------------------------------------------------------------- 補助（実キー）

        private IEnumerator TapKey(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        private IEnumerator PressUntil(Key key, Func<bool> condition, float seconds)
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

        private static bool ActivePlayer(out PlayerRoot root, out PlayerFacing facing)
        {
            root = null;
            facing = null;
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            if (bundle == null || !bundle.TryResolve(out root))
            {
                return false;
            }

            facing = root.GetComponentInChildren<PlayerFacing>();
            return true;
        }

        private static void PlaceAt(PlayerRoot root, Vector3 position)
        {
            root.transform.position = new Vector3(position.x, root.transform.position.y, position.z);
            if (root.Body != null)
            {
                root.Body.position = root.transform.position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
        }

        private static T FindInArea<T>(Predicate<T> match) where T : Component
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            if (bundle == null)
            {
                return null;
            }

            foreach (T c in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (c != null && c.gameObject.scene == bundle.gameObject.scene && match(c))
                {
                    return c;
                }
            }

            return null;
        }

        private static void QuietFieldEnemies()
        {
            foreach (AreaFieldEnemyDirector d in FindObjectsByType<AreaFieldEnemyDirector>(FindObjectsSortMode.None))
            {
                d.gameObject.SetActive(false);
            }
        }

        private IEnumerator Talk(CampaignDialogueService dialogue, StableId villagerId)
        {
            VillagerPoint point = FindInArea<VillagerPoint>(v => v.VillagerId.Equals(villagerId));
            if (point == null || !ActivePlayer(out PlayerRoot root, out PlayerFacing facing))
            {
                Fail("villager " + villagerId.Value + " not in the area");
                yield break;
            }

            PlaceAt(root, point.InteractionAnchor + new Vector3(0f, 0f, -1.0f));
            yield return new WaitForFixedUpdate();
            facing?.ConfirmFromInput(Vector2.up);
            yield return null;
            int opens = dialogue.OpenCount;
            yield return PressUntil(Key.E, () => dialogue.OpenCount > opens, 4f);
            if (dialogue.OpenCount <= opens)
            {
                Fail("could not talk to " + villagerId.Value + " (rejection=" + dialogue.LastRejection + ")");
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 2f;
            while (dialogue.AwaitingRelease && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return null;
        }

        private IEnumerator AdvanceToChoices(CampaignDialogueService dialogue)
        {
            for (int guard = 0; guard < 20 && dialogue.IsOpen && !dialogue.Conversation.ShowsChoices; guard++)
            {
                int before = dialogue.Conversation.PageIndex;
                yield return TapKey(Key.Enter);
                if (dialogue.IsOpen && dialogue.Conversation.PageIndex != before + 1 && !dialogue.Conversation.ShowsChoices)
                {
                    Fail("one press did not advance one page (" + before + " -> " + dialogue.Conversation.PageIndex + ")");
                    yield break;
                }
            }

            if (!dialogue.IsOpen || !dialogue.Conversation.ShowsChoices)
            {
                Fail("did not reach the choices");
                yield break;
            }

            yield return null;
            yield return null;
        }

        private IEnumerator Choose(CampaignDialogueService dialogue, DialogueChoiceKind kind, StableId target)
        {
            if (!dialogue.IsOpen)
            {
                Fail("dialogue closed before choosing " + kind);
                yield break;
            }

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

            if (index < 0)
            {
                Fail("no choice " + kind + " " + target.Value);
                yield break;
            }

            for (int i = 0; i < index; i++)
            {
                yield return TapKey(Key.DownArrow);
            }

            yield return TapKey(Key.Enter);
            yield return null;
        }

        private IEnumerator SlideThrough(StableId exitId, Key key, Vector3 back)
        {
            AreaTransitionService transitions = BootstrapServices.Get<AreaTransitionService>();
            AreaExitGate gate = FindInArea<AreaExitGate>(g => g.ExitId.Equals(exitId));
            if (gate == null || !ActivePlayer(out PlayerRoot root, out _))
            {
                Fail("exit " + exitId.Value + " not in the area");
                yield break;
            }

            int expected = transitions.SlideCommittedCount + 1;
            PlaceAt(root, gate.transform.position + back * 0.4f);
            yield return new WaitForFixedUpdate();
            yield return null;
            float deadline = Time.realtimeSinceStartup + 25f;
            float nextRelease = Time.realtimeSinceStartup + 1.5f;
            while (transitions.SlideCommittedCount < expected && !transitions.HasTerminalFailure && Time.realtimeSinceStartup < deadline)
            {
                bool release = Time.realtimeSinceStartup >= nextRelease;
                InputSystem.QueueStateEvent(_keyboard, release ? new KeyboardState() : new KeyboardState(key));
                if (release)
                {
                    nextRelease = Time.realtimeSinceStartup + 1.5f;
                    if (!gate.PlayerInside && transitions.SlideCommittedCount < expected && !transitions.Slide.IsTransitioning
                        && ActivePlayer(out PlayerRoot again, out _))
                    {
                        PlaceAt(again, gate.transform.position + back * 0.4f);
                    }
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            if (transitions.HasTerminalFailure || transitions.SlideCommittedCount < expected)
            {
                string where = "?";
                if (ActivePlayer(out PlayerRoot now, out _))
                {
                    var state = now.GetComponentInChildren<PlayerStateController>();
                    where = "player=" + now.transform.position.ToString("F2") + " gate=" + gate.transform.position.ToString("F2")
                        + " state=" + (state != null ? state.Current.ToString() : "?");
                }

                Fail("slide through " + exitId.Value + " failed: " + transitions.TerminalFailureReason + " [" + where
                    + " inside=" + gate.PlayerInside + " sliding=" + transitions.Slide.IsTransitioning
                    + " unsettled=" + transitions.IsTransitionUnsettled
                    + " mode=" + (Momotaro.Gameplay.Modes.GameModeProvider.Current != null
                        ? Momotaro.Gameplay.Modes.GameModeProvider.Current.Current.ToString() : "null")
                    + " frozen=" + GameplayClockProvider.IsFrozen + " holds=" + GameplayClockProvider.HoldCount
                    + " dialogueOpen=" + BootstrapServices.Get<CampaignDialogueService>().IsOpen
                    + " menu=" + Saves.IsMenuOpen + " slides=" + transitions.SlideCommittedCount + "/" + expected + "]");
                yield break;
            }

            float quietDeadline = Time.realtimeSinceStartup + 20f;
            int quiet = 0;
            while (quiet < 3 && Time.realtimeSinceStartup < quietDeadline)
            {
                yield return null;
                bool busy = transitions.Slide.IsTransitioning || transitions.Slide.IsRetireInFlight
                    || transitions.Slide.Preloader.HasLiveSceneOperation || transitions.IsSingleLoadInFlight;
                quiet = busy ? 0 : quiet + 1;
            }

            yield return WaitArrived();
        }

        private IEnumerator InteractShrineByKey(CampaignShrineService shrines, StableId shrineId)
        {
            ShrinePoint point = FindInArea<ShrinePoint>(p => p.ShrineId.Equals(shrineId));
            if (point == null || !ActivePlayer(out PlayerRoot root, out PlayerFacing facing))
            {
                Fail("shrine " + shrineId.Value + " not in the area");
                yield break;
            }

            PlaceAt(root, point.InteractionAnchor + new Vector3(0f, 0f, 1.0f));
            yield return new WaitForFixedUpdate();
            facing?.ConfirmFromInput(Vector2.down);
            yield return null;
            int before = point.InteractCount;
            yield return PressUntil(Key.E, () => point.InteractCount > before, 6f);
            if (point.InteractCount <= before || !shrines.IsMenuOpen)
            {
                Fail("shrine menu did not open: " + shrines.Message);
            }
        }

        private IEnumerator EnterEncounter(AreaEncounterRunner runner)
        {
            AreaEncounterTrigger trigger = null;
            System.Reflection.FieldInfo field = typeof(AreaEncounterTrigger).GetField("_runner",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (AreaEncounterTrigger t in FindObjectsByType<AreaEncounterTrigger>(FindObjectsSortMode.None))
            {
                if (field != null && ReferenceEquals(field.GetValue(t), runner))
                {
                    trigger = t;
                }
            }

            if (trigger == null || !ActivePlayer(out PlayerRoot root, out _))
            {
                Fail("no trigger for " + runner.EncounterId.Value);
                yield break;
            }

            PlaceAt(root, trigger.transform.position);
            yield return new WaitForFixedUpdate();
            float deadline = Time.realtimeSinceStartup + 6f;
            while (runner.State != AreaEncounterState.Playing && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (runner.State != AreaEncounterState.Playing)
            {
                Fail("encounter did not start: " + runner.LastRejection);
            }
        }

        private static List<Momotaro.Gameplay.Enemy.EnemyActor> EncounterEnemies(AreaEncounterRunner runner)
        {
            var list = new List<Momotaro.Gameplay.Enemy.EnemyActor>();
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            foreach (Momotaro.Gameplay.Enemy.EnemyActor e in FindObjectsByType<Momotaro.Gameplay.Enemy.EnemyActor>(FindObjectsSortMode.None))
            {
                if (e != null && !e.IsDefeated && e.gameObject.activeInHierarchy && bundle != null
                    && e.gameObject.scene == bundle.gameObject.scene && e.GetComponentInParent<AreaFieldEnemyDirector>() == null)
                {
                    list.Add(e);
                }
            }

            return list;
        }
    }
}
