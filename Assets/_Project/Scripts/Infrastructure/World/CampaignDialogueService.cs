using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Data.Story;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Story;
using Momotaro.Infrastructure.Bootstrap;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.World
{
    /// <summary>会話を始められなかった理由（P7 01。診断・テスト用）。</summary>
    public enum DialogueStartRejection
    {
        None = 0,
        NoCampaign = 1,
        Transition = 2,
        Respawn = 3,
        WrongMode = 4,
        AreaNotReady = 5,
        Encounter = 6,
        PlayerBusy = 7,
        UnknownVillager = 8,
        WrongArea = 9,
        JustClosed = 10,
        Pending = 11,

        /// <summary>調べたのと同じフレームに被弾が確定した（その解決を優先して開かない）。</summary>
        HitThisFrame = 12,
    }

    /// <summary>
    /// 会話の常駐サービス（P7 01〜03。仕様 §3〜§6・§10）。住民（<see cref="VillagerPoint"/>）から「調べた」を受け、
    /// 会話の表示（仮 UI）・選択・停止と復帰・依頼の受注と報告・必須イベントの決定・短い通知を行う。
    ///
    /// <b>開始は調べたフレームの後（LateUpdate）で条件を見直してから。</b> 同じフレームに確定した被弾・死亡の解決を優先し、
    /// 条件が崩れていれば開かない（押下は捨て、動作終了後に自動で開かない）。
    ///
    /// <b>停止</b>：<see cref="GameMode.Dialogue"/>（敵・飛び道具・仲間・入力）と <see cref="GameplayClockProvider.Hold"/>
    /// （主人公の状態・Vitals・移動・被弾、仲間の各 Controller）。UI と保存の書込は止めない。
    /// <b>復帰</b>：自分が Dialogue にしたときだけ、閉じたときに元のモードへ戻す。閉じる時点で別の停止（終了導線の Paused 等）が
    /// 上に乗っていればモードは触らない。会話の入力はモードが Dialogue のときだけ読む。
    ///
    /// <b>保存しない</b>：会話ウィンドウ・表示中の行・選択カーソル・停止の保持・入力の解放待ち（仕様 §9）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampaignDialogueService : MonoBehaviour, IGameService, IDialogueOperations, IChapterProgress,
        Momotaro.Gameplay.Combat.IHitResultListener
    {
        private Momotaro.Gameplay.Player.PlayerVitalsHolder _pendingVitals;
        private bool _hitDuringPending;
        private readonly List<string> _deferredNotices = new List<string>();
        private readonly PadMenuNavigator _navigator = new PadMenuNavigator();
        private readonly List<QuestInfo> _questBuffer = new List<QuestInfo>();
        private readonly HashSet<string> _knownReportable = new HashSet<string>();
        private AreaTransitionService _transitions;
        private DialogueConversation _open;
        private StableId _pendingVillager;
        private StableId _pendingArea;
        private int _pendingFrame = -1;
        private bool _ownsMode;
        private GameMode _modeBeforeDialogue = GameMode.Exploration;
        private bool _awaitRelease;
        private bool _choicesShown;
        private GameSessionState _watchedSession;
        private float _noticeUntil;

        /// <inheritdoc />
        public string ServiceName => "CampaignDialogue";

        /// <summary>会話が開いているか。</summary>
        public bool IsOpen => _open != null;

        /// <summary>開いている会話（閉じていれば null。テスト用）。</summary>
        public DialogueConversation Conversation => _open;

        /// <summary>開始が保留中か（調べたフレームの LateUpdate まで）。</summary>
        public bool HasPendingStart => !_pendingVillager.IsEmpty;

        /// <summary>直近の開始拒否の理由。</summary>
        public DialogueStartRejection LastRejection { get; private set; }

        /// <summary>会話を開いた回数・閉じた回数（診断・テスト用）。</summary>
        public int OpenCount { get; private set; }

        public int CloseCount { get; private set; }

        /// <summary>会話を閉じたフレーム（同じ Esc でメニューが開かないようにする）。</summary>
        public int LastClosedFrame { get; private set; } = -1;

        /// <summary>直近の受注・報告の結果（診断・テスト用）。</summary>
        public QuestAcceptResult LastAcceptResult { get; private set; } = QuestAcceptResult.Unknown;

        public QuestReportResult LastReportResult { get; private set; } = QuestReportResult.Unknown;

        /// <summary>直近の報告で実際に増えた徳。</summary>
        public int LastGrantedVirtue { get; private set; }

        /// <summary>入力の解放待ちか（開始直後。テスト用）。</summary>
        public bool AwaitingRelease => _awaitRelease;

        /// <summary>短い通知（直近のもの）。</summary>
        public string Notice { get; private set; } = string.Empty;

        /// <summary>出した通知の数（診断・テスト用。Load の再構築で増えないことを確かめる）。</summary>
        public int NoticeCount { get; private set; }

        /// <inheritdoc />
        public ServiceInitResult Initialize()
        {
            DialogueOperationsProvider.Current = this;
            ChapterProgressProvider.Current = this;
            return ServiceInitResult.Ok("Dialogue service ready.");
        }

        /// <summary>遷移サービスを結ぶ（Bootstrap が呼ぶ）。</summary>
        public void Bind(AreaTransitionService transitions)
        {
            _transitions = transitions;
        }

        private GameSessionState Session => GameSessionProvider.Current;

        private CampaignCatalog Campaign => _transitions != null && _transitions.Catalog != null
            ? _transitions.Catalog.Campaign
            : null;

        /// <summary>この campaign の会話・依頼の定義（無ければ null）。</summary>
        public StoryCatalog Story => Campaign?.Story;

        // ---------------------------------------------------------------- 開始

        /// <summary>
        /// 会話を始めてよいか（仕様 §3）：冒険中・遷移外・死亡解決外・探索中・Area 準備済み・遭遇戦外・主人公が平常（Idle／Move。
        /// 攻撃・ガード・ステップ・必殺・きびだんご使用・被弾・死亡でない）。
        /// </summary>
        public bool CanStart(out DialogueStartRejection rejection, out string reason)
        {
            GameSessionState session = Session;
            if (session == null || Story == null || string.IsNullOrEmpty(session.AdventureId))
            {
                rejection = DialogueStartRejection.NoCampaign;
                reason = "冒険が始まっていません。";
                return false;
            }

            if (_transitions.IsTransitionUnsettled)
            {
                rejection = DialogueStartRejection.Transition;
                reason = "移動中です。";
                return false;
            }

            if (session.Respawn.Phase != CampaignRespawnPhase.Idle)
            {
                rejection = DialogueStartRejection.Respawn;
                reason = "倒れています。";
                return false;
            }

            GameMode mode = GameModeProvider.Current != null ? GameModeProvider.Current.Current : GameMode.Loading;
            if (mode != GameMode.Exploration)
            {
                rejection = DialogueStartRejection.WrongMode;
                reason = "いまは話せません。";
                return false;
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            if (bundle == null || bundle.Context == null || !bundle.Context.IsAreaReady)
            {
                rejection = DialogueStartRejection.AreaNotReady;
                reason = "エリアの準備ができていません。";
                return false;
            }

            if ((bundle.Encounter != null && bundle.Encounter.IsEncounterActive)
                || (bundle.EncounterGroup != null && bundle.EncounterGroup.IsEncounterActive))
            {
                rejection = DialogueStartRejection.Encounter;
                reason = "戦闘中です。";
                return false;
            }

            AreaActorTransferPort port = bundle.TransferPort;
            Momotaro.Gameplay.Player.PlayerStateController player = port != null ? port.PlayerState : null;
            if (port == null || !port.CanExportForSave || player == null || !player.IsFreeToTravel || player.IsUsingItem
                || (port.PlayerHitReaction != null && port.PlayerHitReaction.IsHurt))
            {
                rejection = DialogueStartRejection.PlayerBusy;
                reason = "いまは話せません。";
                return false;
            }

            rejection = DialogueStartRejection.None;
            reason = null;
            return true;
        }

        /// <inheritdoc />
        public AreaInteractionOutcome OnVillagerInteracted(StableId villagerId, StableId areaId)
        {
            if (_open != null || HasPendingStart)
            {
                LastRejection = DialogueStartRejection.Pending;
                return AreaInteractionOutcome.Refused("会話中です。");
            }

            if (LastClosedFrame == Time.frameCount)
            {
                LastRejection = DialogueStartRejection.JustClosed;
                return AreaInteractionOutcome.Refused("会話を閉じたところです。");
            }

            if (!CanStart(out DialogueStartRejection rejection, out string reason))
            {
                LastRejection = rejection;
                return AreaInteractionOutcome.Refused(reason);
            }

            if (!Story.TryGetVillager(villagerId, out VillagerInfo villager))
            {
                LastRejection = DialogueStartRejection.UnknownVillager;
                return AreaInteractionOutcome.Refused("この住民は会話の Data にありません。");
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            if (!villager.AreaId.Equals(bundle.AreaId) || !villager.AreaId.Equals(areaId))
            {
                LastRejection = DialogueStartRejection.WrongArea;
                return AreaInteractionOutcome.Refused("この住民は別のエリアの人です。");
            }

            // 開くのはこのフレームの後（LateUpdate）。同じフレームの被弾・死亡の解決を先にする。
            // 被弾は硬直を伴わない小さな命中もあるので、状態ではなく<b>このフレームの命中結果</b>を主人公の結果通知で見る。
            _pendingVillager = villagerId;
            _pendingArea = areaId;
            _pendingFrame = Time.frameCount;
            _hitDuringPending = false;
            _pendingVitals = bundle.TransferPort != null ? bundle.TransferPort.PlayerVitals : null;
            _pendingVitals?.Results.AddListener(this);
            LastRejection = DialogueStartRejection.None;
            return AreaInteractionOutcome.Accepted(villager.DisplayName + "に話しかけた");
        }

        private void LateUpdate()
        {
            WatchQuestProgress();
            FlushDeferredNotices();

            if (!HasPendingStart)
            {
                return;
            }

            StableId villagerId = _pendingVillager;
            StableId areaId = _pendingArea;
            bool hit = _hitDuringPending;
            ReleasePendingHitWatch();
            _pendingVillager = default;
            _pendingArea = default;
            _pendingFrame = -1;

            // 見直し：崩れていたら開かない（押下は捨てる。動作終了後に自動で開かない）。
            if (hit)
            {
                LastRejection = DialogueStartRejection.HitThisFrame;
                return;
            }

            if (!CanStart(out DialogueStartRejection rejection, out _))
            {
                LastRejection = rejection;
                return;
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            if (bundle == null || !bundle.AreaId.Equals(areaId))
            {
                LastRejection = DialogueStartRejection.WrongArea;
                return;
            }

            DialogueConversation conversation = DialogueConversation.Open(Session, Story, villagerId);
            if (conversation == null)
            {
                LastRejection = DialogueStartRejection.UnknownVillager;
                return;
            }

            OpenConversation(conversation);
        }

        /// <inheritdoc />
        public void OnHitResult(in Momotaro.Gameplay.Combat.HitResult result)
        {
            if (result.Kind == Momotaro.Gameplay.Combat.HitResultKind.Damage
                || result.Kind == Momotaro.Gameplay.Combat.HitResultKind.Guard
                || result.Kind == Momotaro.Gameplay.Combat.HitResultKind.JustGuard)
            {
                _hitDuringPending = true;
            }
        }

        private void ReleasePendingHitWatch()
        {
            _pendingVitals?.Results.RemoveListener(this);
            _pendingVitals = null;
            _hitDuringPending = false;
        }

        private void OpenConversation(DialogueConversation conversation)
        {
            _open = conversation;
            _choicesShown = false;
            _awaitRelease = true; // 開始に使ったボタン（調べる＝E／South）で第一文を飛ばさない。
            _navigator.Reset(0);
            OpenCount++;

            IGameModeService modes = GameModeProvider.Current;
            _ownsMode = false;
            if (modes != null && modes.Current != GameMode.Dialogue)
            {
                _modeBeforeDialogue = modes.Current;
                _ownsMode = modes.ChangeMode(GameMode.Dialogue);
            }

            GameplayClockProvider.Hold(this);
        }

        // ---------------------------------------------------------------- 操作

        /// <summary>次のページへ（最後のページで選択肢が無ければ閉じる）。開いていなければ何もしない。</summary>
        public void Advance()
        {
            if (_open == null)
            {
                return;
            }

            if (_open.ShowsChoices)
            {
                return; // 選択肢は選ぶ（決定で閉じる・進む）。
            }

            if (!_open.Advance())
            {
                Close();
            }
        }

        /// <summary>
        /// 最後のページの選択肢を選ぶ（表示順の番号）。<b>確定は実行時に再検証する</b>（<see cref="StoryProcedures"/>）。
        /// </summary>
        public bool Choose(int index)
        {
            if (_open == null || !_open.ShowsChoices || index < 0 || index >= _open.Current.Choices.Count)
            {
                return false;
            }

            DialogueChoice choice = _open.Current.Choices[index];
            GameSessionState session = Session;
            StoryCatalog story = Story;
            StableId villager = _open.Villager.VillagerId;
            switch (choice.Kind)
            {
                case DialogueChoiceKind.ListenQuest:
                    if (_open.ShowQuestOffer(choice.TargetId))
                    {
                        _choicesShown = false;
                        _navigator.Reset(0);
                    }

                    return true;

                case DialogueChoiceKind.AcceptQuest:
                    LastAcceptResult = StoryProcedures.AcceptQuest(session, story, villager, choice.TargetId);
                    if (LastAcceptResult == QuestAcceptResult.Accepted && story.TryGetQuest(choice.TargetId, out QuestInfo accepted))
                    {
                        PostNotice("依頼を受けた：" + accepted.DisplayName);
                        RememberReportable(session, story);
                    }

                    Close();
                    return true;

                case DialogueChoiceKind.ReportQuest:
                    LastReportResult = StoryProcedures.ReportQuest(session, story, villager, choice.TargetId, out int granted);
                    LastGrantedVirtue = granted;
                    if (LastReportResult == QuestReportResult.Reported && story.TryGetQuest(choice.TargetId, out QuestInfo done))
                    {
                        PostNotice(done.ReportText + "　徳 +" + granted);
                        RememberReportable(session, story);
                    }

                    Close();
                    return true;

                case DialogueChoiceKind.ConfirmEvent:
                    if (StoryProcedures.CompleteEvent(session, story, _open.Dialogue, choice.TargetId, out bool opened)
                        && story.TryGetEvent(choice.TargetId, out StoryEventInfo info))
                    {
                        if (opened)
                        {
                            ApplyOpenedDoors(info);
                        }

                        PostNotice(info.DisplayName);
                    }

                    Close();
                    return true;

                default:
                    Close(); // 閉じる・今は受けない：何も変えない。
                    return true;
            }
        }

        /// <summary>閉じる（何も変えない）。停止の保持を外し、自分が Dialogue にしていれば元のモードへ戻す。</summary>
        public void Close()
        {
            if (_open == null)
            {
                return;
            }

            _open = null;
            _choicesShown = false;
            _awaitRelease = false;
            CloseCount++;
            LastClosedFrame = Time.frameCount;
            GameplayClockProvider.Release(this);

            IGameModeService modes = GameModeProvider.Current;
            if (_ownsMode && modes != null && modes.Current == GameMode.Dialogue)
            {
                modes.ChangeMode(_modeBeforeDialogue);
            }

            _ownsMode = false;
        }

        /// <summary>
        /// 強制的に閉じる（冒険が外れた・タイトルへ戻る・常駐の破棄）。<b>モードは触らない</b>（持ち主が別に決める）。
        /// </summary>
        private void ForceClose()
        {
            ReleasePendingHitWatch();
            _pendingVillager = default;
            _pendingArea = default;
            if (_open == null)
            {
                GameplayClockProvider.Release(this);
                return;
            }

            _open = null;
            _ownsMode = false;
            _choicesShown = false;
            _awaitRelease = false;
            CloseCount++;
            LastClosedFrame = Time.frameCount;
            GameplayClockProvider.Release(this);
        }

        private void Update()
        {
            if (_open == null)
            {
                return;
            }

            GameMode mode = GameModeProvider.Current != null ? GameModeProvider.Current.Current : GameMode.Loading;
            if (Session == null || Story == null || mode == GameMode.Loading || mode == GameMode.GameOver)
            {
                ForceClose();
                return;
            }

            // 別の停止（終了導線の保存待ち・失敗時の選択など）が上に乗っている間は、会話を進めも閉じもしない。
            if (mode != GameMode.Dialogue)
            {
                return;
            }

            if (_awaitRelease)
            {
                if (AnyConfirmHeld())
                {
                    return;
                }

                _awaitRelease = false;
                _navigator.Reset(_navigator.Selected);
                return;
            }

            if (CancelPressed())
            {
                Close();
                return;
            }

            if (_open.ShowsChoices)
            {
                if (!_choicesShown)
                {
                    _choicesShown = true;
                    _navigator.Reset(0); // 最後のページを開いた押下で選択まで進めない。
                    return;
                }

                int chosen = _navigator.Poll(_open.Current.Choices.Count, null, out bool cancelled);
                if (chosen >= 0)
                {
                    Choose(chosen);
                }
                else if (cancelled)
                {
                    Close();
                }

                return;
            }

            if (AdvancePressed())
            {
                Advance();
            }
        }

        // ---------------------------------------------------------------- 入力（物理ボタン。Action Map の切り替えに依存しない）

        private static bool AnyConfirmHeld()
        {
            Keyboard k = Keyboard.current;
            if (k != null && (k.eKey.isPressed || k.enterKey.isPressed || k.numpadEnterKey.isPressed || k.spaceKey.isPressed))
            {
                return true;
            }

            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                if (Gamepad.all[i].buttonSouth.isPressed)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AdvancePressed()
        {
            Keyboard k = Keyboard.current;
            if (k != null && (k.eKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame
                || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame))
            {
                return true;
            }

            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                if (Gamepad.all[i].buttonSouth.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CancelPressed()
        {
            Keyboard k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                if (Gamepad.all[i].buttonEast.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- 門・通知

        /// <inheritdoc />
        public AreaInteractionOutcome OnGateInspected(StableId eventId)
        {
            StoryCatalog story = Story;
            GameSessionState session = Session;
            if (story == null || session == null || !story.TryGetEvent(eventId, out StoryEventInfo info))
            {
                return AreaInteractionOutcome.Refused("門の配線がありません。");
            }

            if (session.Story.IsEventCompleted(eventId))
            {
                return AreaInteractionOutcome.Accepted("門は開いている。");
            }

            PostNotice(info.LockedNotice);
            return AreaInteractionOutcome.Refused(info.LockedNotice);
        }

        /// <inheritdoc />
        public bool IsEventCompleted(StableId eventId)
        {
            GameSessionState session = Session;
            return session != null && session.Story.IsEventCompleted(eventId);
        }

        /// <summary>短い通知を出す（受注・報告可能・受領・章クリア・門の理由）。</summary>
        public void PostNotice(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Notice = text;
            NoticeCount++;
            _noticeUntil = Time.unscaledTime + 4f;
        }

        /// <summary>イベントが開けた門を、いまの Area でその場で開ける（記録は確定済み。未ロードの Area は入場時に復元される）。</summary>
        private void ApplyOpenedDoors(StoryEventInfo info)
        {
            AreaRuntimeBundle bundle = ActiveBundle();
            if (bundle == null || bundle.Root == null || !bundle.AreaId.Equals(info.OpensAreaId))
            {
                return;
            }

            foreach (AreaFlagDoor door in bundle.Root.Doors)
            {
                if (door != null && door.FlagId.Equals(info.OpensFlagId) && !door.TryApplyOpened(out string error))
                {
                    GameLog.Error(LogCategory.Scene, "Event completed but the door did not open: " + error);
                }
            }
        }

        /// <summary>
        /// 依頼が「報告可能」へ変わったら通知する（仕様 §10）。冒険が変わった（New Game・Continue）ときは<b>その時点の状態を覚えるだけで
        /// 通知しない</b>——Load の再構築で過去の達成を再生しない。
        /// </summary>
        private void WatchQuestProgress()
        {
            GameSessionState session = Session;
            StoryCatalog story = Story;
            if (session == null || story == null || string.IsNullOrEmpty(session.AdventureId))
            {
                _watchedSession = null;
                _knownReportable.Clear();
                return;
            }

            if (!ReferenceEquals(session, _watchedSession))
            {
                _watchedSession = session;
                RememberReportable(session, story);
                return;
            }

            foreach (QuestInfo quest in story.Quests)
            {
                if (StoryRules.StateOf(session, quest) == QuestStateKind.Reportable && _knownReportable.Add(quest.QuestId.Value))
                {
                    PostNotice("依頼「" + quest.DisplayName + "」：依頼主に報告できます");
                }
            }
        }

        private void RememberReportable(GameSessionState session, StoryCatalog story)
        {
            _knownReportable.Clear();
            foreach (QuestInfo quest in story.Quests)
            {
                if (StoryRules.StateOf(session, quest) == QuestStateKind.Reportable)
                {
                    _knownReportable.Add(quest.QuestId.Value);
                }
            }
        }

        private AreaRuntimeBundle ActiveBundle()
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            if (bundle != null)
            {
                return bundle;
            }

            return AreaBundleDirectory.TryGetSingle(out bundle) ? bundle : null;
        }

        private void OnDestroy()
        {
            ForceClose();
            DialogueOperationsProvider.ReleaseIfOwner(this);
            ChapterProgressProvider.ReleaseIfOwner(this);
        }

        // ---------------------------------------------------------------- 章（P7 04）

        /// <summary>章クリアの通知を出した回数（診断・テスト用）。</summary>
        public int ChapterNoticeCount { get; private set; }

        /// <summary>まだ出していない通知があるか（死亡再開と重なった章クリアなど。テスト用）。</summary>
        public bool HasDeferredNotice => _deferredNotices.Count > 0;

        /// <inheritdoc />
        public bool TryGetChapterBoss(StableId areaId, StableId encounterId, out ChapterBossInfo info)
        {
            info = default;
            CampaignCatalog campaign = Campaign;
            StoryCatalog story = campaign?.Story;
            if (story == null || !story.TryGetChapterByBoss(areaId, encounterId, out ChapterInfo chapter))
            {
                return false;
            }

            info = new ChapterBossInfo(chapter.ChapterId, chapter.DisplayName, campaign.RefundRightsPerChapter,
                campaign.RefundRightsMax);
            return true;
        }

        /// <inheritdoc />
        public void OnChapterBossVictory(in ChapterBossInfo chapter, in ChapterClearCommit commit)
        {
            if (!commit.ChapterCleared)
            {
                return;
            }

            // 実際に確定した増加量を出す（上限で 0 なら +3 と書かない。仕様 §10）。
            string rights = commit.RightsAdded > 0
                ? "払い戻し権利 +" + commit.RightsAdded
                : "払い戻し権利は上限のため増えません";
            _deferredNotices.Add(chapter.DisplayName + " クリア！　" + rights);
            ChapterNoticeCount++;
        }

        /// <summary>
        /// 保留した通知を、安全な UI のタイミング（探索中・死亡再開の外・遷移の外）で出す。死亡復帰と重なった章クリアは復帰後に出る。
        /// </summary>
        private void FlushDeferredNotices()
        {
            if (_deferredNotices.Count == 0)
            {
                return;
            }

            GameSessionState session = Session;
            GameMode mode = GameModeProvider.Current != null ? GameModeProvider.Current.Current : GameMode.Loading;
            if (session == null || string.IsNullOrEmpty(session.AdventureId))
            {
                _deferredNotices.Clear(); // 冒険が外れた（タイトルへ）。Load で再生しない。
                return;
            }

            if (session.Respawn.Phase != CampaignRespawnPhase.Idle || mode != GameMode.Exploration
                || (_transitions != null && _transitions.IsTransitionUnsettled))
            {
                return;
            }

            PostNotice(_deferredNotices[0]);
            _deferredNotices.RemoveAt(0);
        }

        // ---------------------------------------------------------------- 仮 UI（P7。正式な会話画面素材は後続）

        private void OnGUI()
        {
            if (_open == null)
            {
                if (!string.IsNullOrEmpty(Notice) && Time.unscaledTime < _noticeUntil)
                {
                    _navigator.BeginScaled();
                    GUI.Box(new Rect(16f, 16f, 620f, 30f), Notice);
                    _navigator.EndScaled();
                }

                return;
            }

            _navigator.BeginScaled();
            float width = Mathf.Min(PadMenuNavigator.VirtualWidth - 32f, 760f);
            int choiceCount = _open.ShowsChoices ? _open.Current.Choices.Count : 0;
            float height = 150f + choiceCount * 34f;
            var area = new Rect((PadMenuNavigator.VirtualWidth - width) * 0.5f, PadMenuNavigator.VirtualHeight - height - 16f,
                width, height);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 8f, area.width - 28f, area.height - 16f));
            GUILayout.Label("【" + _open.Current.Speaker + "】");
            var body = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Label(_open.CurrentPage, body, GUILayout.MinHeight(60f));
            if (_open.ShowsChoices)
            {
                for (int i = 0; i < _open.Current.Choices.Count; i++)
                {
                    if (_navigator.DrawItem(i, _open.Current.Choices[i].Label) && !_awaitRelease)
                    {
                        Choose(i);
                        break;
                    }
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(_open.ShowsChoices
                ? PadMenuNavigator.Hint
                : "次へ：E／Enter／A（×）　閉じる：Esc／B（○）　（" + (_open.PageIndex + 1) + "／" + _open.Current.Pages.Count + "）");
            GUILayout.EndArea();
            _navigator.EndScaled();
        }
    }
}
