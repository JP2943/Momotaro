using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Progression;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 本編型 Session の純粋 State（P5-01。仕様書 v1.1 §4.1／§4.2。P6A-01 で恒久進行・普通敵周期・版を追加）。
    /// UnityEngine・Scene API に依存しない。
    ///
    /// <b>徳の正本はここが持つ 1 個の <see cref="PlayerProgressState"/> だけ。</b>
    /// World 状態用のクラスへ徳を複製しない（§4.2）。Scene 上の <see cref="PlayerProgressHolder"/> は
    /// この参照を Bind されて窓口になるだけで、自分の State を並行して持たない。
    ///
    /// Scene 由来の Actor・Presenter を <c>DontDestroyOnLoad</c> で運ばない（§4.1 末尾）。ここに入るのは
    /// 「Scene が入れ替わっても意味を保つ純粋な値」に限る。生成・破棄の権限は Session の所有者
    /// （Bootstrap／Game Session 層）だけが持ち、明示的な New Game が新しい実体を作る（§9.2）。
    /// 本クラスに全体 Reset を置かないのは、Scene 側から共有 State を初期化できないようにするため（§4.2）。
    ///
    /// <b>確定は 1 か所で（P6 仕様 §4）。</b> 「記録と報酬を同時に」が要る操作（初到達・遭遇戦の最終撃破・
    /// 普通敵の撃破・配置物の取得）は、ここの <c>Commit*</c>／<c>TryRecord*</c>／<c>TryPick*</c> を通す。
    /// 呼び出し側で記録と付与を別々に呼ぶと、その間に保存の Snapshot が挟まりうる。
    /// </summary>
    public sealed class GameSessionState
    {
        private readonly SessionChangeLog _log;
        private readonly PlayerProgressState _progress = new PlayerProgressState();
        private readonly InventoryState _inventory = new InventoryState();
        private readonly Dictionary<StableId, AreaRuntimeState> _areas = new Dictionary<StableId, AreaRuntimeState>();
        private readonly HashSet<StableId> _visitedAreas = new HashSet<StableId>();
        private readonly HashSet<StableId> _recruited = new HashSet<StableId>();

        /// <summary>P5 の規則で作る（既存の P5／P5.5 経路・テスト）。</summary>
        public GameSessionState()
            : this(EncounterClearPolicy.PerRespawnCycle)
        {
        }

        /// <summary>campaign の規則を指定して作る（P6）。</summary>
        public GameSessionState(EncounterClearPolicy encounterPolicy)
        {
            _log = new SessionChangeLog(encounterPolicy);
            _progress.Changed += _ => _log.Touch("progress", autosave: true);
            _inventory.Changed = () => _log.Touch("inventory", autosave: true);
        }

        /// <summary>徳・GrantOnce 記録・成長の唯一の正本（§4.2）。</summary>
        public PlayerProgressState Progress => _progress;

        /// <summary>一般消耗品の所持数（P6A）。</summary>
        public InventoryState Inventory => _inventory;

        /// <summary>世界の版と保存要求の窓口（P6A）。</summary>
        public SessionChangeLog Changes => _log;

        /// <summary>遭遇戦のクリア規則。</summary>
        public EncounterClearPolicy EncounterPolicy => _log.EncounterPolicy;

        /// <summary>
        /// 普通敵（と P5 規則の通常 Encounter）の再出現周期。
        /// P5：本編型死亡再開のたびに 1 進む。P6：休息・成長・死亡・旅立ちの成功ごとに 1 進む（各 1 回）。
        /// 恒久進行（徳・調査・開通・加入・恒久クリア・ボス・配置物）はこの値に影響されない。
        /// </summary>
        public int RespawnCycle { get; private set; }

        /// <summary>State を作成済みのエリア数（診断・テスト用）。</summary>
        public int AreaCount => _areas.Count;

        /// <summary>訪問済みエリア数（診断・テスト用）。</summary>
        public int VisitedAreaCount => _visitedAreas.Count;

        /// <summary>加入済み仲間の数（診断・テスト用）。</summary>
        public int RecruitedCount => _recruited.Count;

        /// <summary>指定エリアの State を取得する。無ければ作る（初回入場）。</summary>
        public AreaRuntimeState GetOrCreateArea(StableId areaId)
        {
            if (_areas.TryGetValue(areaId, out AreaRuntimeState existing))
            {
                return existing;
            }

            var created = new AreaRuntimeState(areaId, _log);
            _areas.Add(areaId, created);
            return created;
        }

        /// <summary>指定エリアの State を取得する（未作成なら false）。作成の副作用を起こさない読み取り。</summary>
        public bool TryGetArea(StableId areaId, out AreaRuntimeState area)
        {
            return _areas.TryGetValue(areaId, out area);
        }

        /// <summary>State を作成済みのエリアを列挙する（保存用）。</summary>
        public IEnumerable<AreaRuntimeState> Areas => _areas.Values;

        /// <summary>訪問済みか。</summary>
        public bool HasVisited(StableId areaId) => !areaId.IsEmpty && _visitedAreas.Contains(areaId);

        /// <summary>訪問済みにする。<b>初回だけ true</b>（再訪で初回入場の演出を再発火させないため）。</summary>
        public bool MarkVisited(StableId areaId)
        {
            if (areaId.IsEmpty || !_visitedAreas.Add(areaId))
            {
                return false;
            }

            _log.Touch("visited", autosave: false);
            return true;
        }

        /// <summary>訪問済み Area を列挙する（保存用）。</summary>
        public IEnumerable<StableId> VisitedAreas => _visitedAreas;

        /// <summary>加入済みか。</summary>
        public bool IsRecruited(StableId companionId) => !companionId.IsEmpty && _recruited.Contains(companionId);

        /// <summary>加入させる。<b>初回だけ true</b>（加入演出・通知の二重発火を防ぐ）。</summary>
        public bool Recruit(StableId companionId)
        {
            if (companionId.IsEmpty || !_recruited.Add(companionId))
            {
                return false;
            }

            _log.Touch("recruited", autosave: true);
            return true;
        }

        /// <summary>加入済み仲間を列挙する（保存用）。</summary>
        public IEnumerable<StableId> Recruited => _recruited;

        // ---- 将来のクエスト・章進行の接続口（P6 仕様 §3「P7 が所有。安定 ID 付き状態を保存へ接続できる境界」）----

        private readonly Dictionary<string, int> _questStages = new Dictionary<string, int>();

        /// <summary>
        /// クエストの段階（安定 ID → 0 以上の整数）。<b>P6A はクエストランナーを作らない</b>——P7 が持つ状態を
        /// 保存・死亡・休息へ通す<b>境界だけ</b>を置く（受入 P6A 08 の接続 fixture）。未設定は 0。
        /// </summary>
        public int QuestStageOf(StableId questId) =>
            !questId.IsEmpty && _questStages.TryGetValue(questId.Value, out int stage) ? stage : 0;

        /// <summary>
        /// クエストの段階を確定する（P7 の進行確定の入口。仕様 §8「将来の P7 進行確定：クエスト・章状態と関連報酬を一緒に」）。
        /// 負数・空 ID は拒否。変化したら版を進めて保存を要求する。<b>死亡・休息・周期では戻らない</b>（恒久進行）。
        /// </summary>
        public bool TrySetQuestStage(StableId questId, int stage)
        {
            if (questId.IsEmpty || stage < 0)
            {
                return false;
            }

            if (_questStages.TryGetValue(questId.Value, out int current) && current == stage)
            {
                return false;
            }

            _questStages[questId.Value] = stage;
            _log.Touch("quest_stage", autosave: true);
            return true;
        }

        /// <summary>段階を持つクエストを列挙する（保存用。順序は呼び出し側で決める）。</summary>
        public void CopyQuestStagesTo(List<KeyValuePair<string, int>> buffer)
        {
            buffer.Clear();
            foreach (KeyValuePair<string, int> pair in _questStages)
            {
                buffer.Add(pair);
            }
        }

        // ---- 会話・依頼・必須イベント・経路・章（P7）----

        private readonly Momotaro.Gameplay.Story.StoryProgressState _story = new Momotaro.Gameplay.Story.StoryProgressState();

        /// <summary>必須イベント・経路・章クリアの記録（P7）。変更はこのクラスの確定入口からだけ。</summary>
        public Momotaro.Gameplay.Story.StoryProgressState Story => _story;

        /// <summary>
        /// 依頼の受注を確定する（P7 02。仕様 §6）。<b>未受注のときだけ</b>段階を 1 にして保存を要求する。
        /// 既知 ID・受注可能かの検証は呼び出し側（<c>StoryProcedures</c>）が Data と照らして行う。不成立は無変更。
        /// </summary>
        public Momotaro.Gameplay.Story.QuestAcceptResult CommitQuestAccepted(Momotaro.Gameplay.Story.QuestInfo quest)
        {
            if (quest == null || quest.QuestId.IsEmpty)
            {
                return Momotaro.Gameplay.Story.QuestAcceptResult.Unknown;
            }

            int stage = QuestStageOf(quest.QuestId);
            if (stage >= Momotaro.Gameplay.Story.QuestStage.Rewarded)
            {
                return Momotaro.Gameplay.Story.QuestAcceptResult.AlreadyRewarded;
            }

            if (stage == Momotaro.Gameplay.Story.QuestStage.Accepted)
            {
                return Momotaro.Gameplay.Story.QuestAcceptResult.AlreadyAccepted;
            }

            _log.BeginBatch("quest_accepted");
            try
            {
                _questStages[quest.QuestId.Value] = Momotaro.Gameplay.Story.QuestStage.Accepted;
                _log.Touch("quest_accepted", autosave: true);
            }
            finally
            {
                _log.EndBatch();
            }

            return Momotaro.Gameplay.Story.QuestAcceptResult.Accepted;
        }

        /// <summary>
        /// 依頼の報告を確定する（P7 02。仕様 §6）。<b>状態と条件をここで再検証し</b>、受領済みの記録・徳の加算・
        /// 既存の報酬台帳への記録（GrantOnce の報酬 ID）を<b>1 つの更新</b>で行い、保存要求は最後に 1 件だけ出す。
        /// 報告可能でなければ何も変えない。重複・再入は段階 2 と報酬 ID の両方で弾かれる（二回分の徳を付けない）。
        /// 到達・遭遇戦の確定そのもの（とその報酬）は再実行しない。
        /// </summary>
        public Momotaro.Gameplay.Story.QuestReportResult CommitQuestReported(Momotaro.Gameplay.Story.QuestInfo quest,
            out int grantedVirtue)
        {
            grantedVirtue = 0;
            if (quest == null || quest.QuestId.IsEmpty)
            {
                return Momotaro.Gameplay.Story.QuestReportResult.Unknown;
            }

            Momotaro.Data.Story.QuestStateKind state = Momotaro.Gameplay.Story.StoryRules.StateOf(this, quest);
            switch (state)
            {
                case Momotaro.Data.Story.QuestStateKind.Rewarded:
                    return Momotaro.Gameplay.Story.QuestReportResult.AlreadyRewarded;
                case Momotaro.Data.Story.QuestStateKind.NotAccepted:
                    return Momotaro.Gameplay.Story.QuestReportResult.NotAccepted;
                case Momotaro.Data.Story.QuestStateKind.InProgress:
                    return Momotaro.Gameplay.Story.QuestReportResult.NotReportable;
            }

            if (quest.Reward.HasReward && !quest.Reward.RewardId.IsEmpty && _progress.HasGranted(quest.Reward.RewardId))
            {
                // 報酬だけ付与済みで段階が 2 でない保存は Load が拒否する。ここに来たら付けずに止める。
                return Momotaro.Gameplay.Story.QuestReportResult.AlreadyRewarded;
            }

            _log.BeginBatch("quest_reported");
            try
            {
                _questStages[quest.QuestId.Value] = Momotaro.Gameplay.Story.QuestStage.Rewarded;
                _log.Touch("quest_reported", autosave: true);
                if (quest.Reward.HasReward)
                {
                    _progress.TryGrant(quest.Reward, out grantedVirtue);
                }
            }
            finally
            {
                _log.EndBatch();
            }

            return Momotaro.Gameplay.Story.QuestReportResult.Reported;
        }

        /// <summary>
        /// 必須イベントの完了を確定する（P7 03。仕様 §7）。<b>1 回だけ</b>。同じ更新で、イベントが開ける仕掛けの開通を記録する
        /// （門 <c>AreaFlagDoor</c> の開通・保存・入場時の復元は既存の仕組み）。既に完了していれば何もしない。
        /// </summary>
        public bool CommitEventCompleted(Momotaro.Gameplay.Story.StoryEventInfo storyEvent, out bool flagOpened)
        {
            flagOpened = false;
            if (storyEvent == null || storyEvent.EventId.IsEmpty || _story.IsEventCompleted(storyEvent.EventId))
            {
                return false;
            }

            _log.BeginBatch("event_completed");
            try
            {
                _story.TryCompleteEvent(storyEvent.EventId);
                _log.Touch("event_completed", autosave: true);
                if (storyEvent.OpensFlag)
                {
                    flagOpened = GetOrCreateArea(storyEvent.OpensAreaId).TryOpen(storyEvent.OpensFlagId);
                }
            }
            finally
            {
                _log.EndBatch();
            }

            return true;
        }

        /// <summary>
        /// 通常の移動の到着を章の経路へ反映する（P7 03。仕様 §7）。<b>到着の確定と同じまとめの中で</b>呼ぶ（保存を重ねない）。
        /// FT・死亡再開・移動失敗の復旧・Load の到着では呼ばない（推測で経路を書かない）。変化したら true。
        /// </summary>
        public bool NoteStoryArrival(Momotaro.Gameplay.Story.StoryCatalog story, StableId areaId, StableId entryId)
        {
            if (story == null || areaId.IsEmpty)
            {
                return false;
            }

            bool changed = false;
            foreach (Momotaro.Gameplay.Story.ChapterInfo chapter in story.Chapters)
            {
                changed |= _story.ApplyArrival(chapter, areaId, entryId);
            }

            if (changed)
            {
                _log.Touch("route_recorded", autosave: true);
            }

            return changed;
        }

        /// <summary>
        /// 章ボスの遭遇戦の勝利を確定する（P7 04。仕様 §8）。<b>遭遇戦のクリア・既存の撃破報酬（初回ボーナス）・開通・ボス撃破・
        /// 章クリア記録（その時点のボス到達経路）・払い戻し権利の追加と処理済み章</b>を 1 つの更新で行い、保存要求は最後に 1 件だけ出す。
        /// 既にクリア済みの遭遇戦なら何もしない（通知の再送・Load 後の再通知で二重に付けない）。新たな章クリアの徳ボーナスは無い。
        /// </summary>
        public ChapterClearCommit CommitChapterBossVictory(StableId areaId, StableId bossEncounterId,
            in RewardSnapshot clearBonus, StableId unlockFlagId, StableId chapterId, int rightsPerChapter, int rightsMax)
        {
            if (areaId.IsEmpty || bossEncounterId.IsEmpty || chapterId.IsEmpty)
            {
                return default;
            }

            _log.BeginBatch("chapter_cleared");
            try
            {
                EncounterClearCommit encounter = CommitEncounterClear(areaId, bossEncounterId, clearBonus, unlockFlagId);
                if (!encounter.Recorded)
                {
                    return default;
                }

                bool boss = TryRecordBossDefeat(areaId, bossEncounterId, RewardSnapshot.None, out _);
                bool cleared = _story.TryMarkChapterCleared(chapterId);
                if (cleared)
                {
                    _log.Touch("chapter_cleared", autosave: true);
                }

                ChapterRightsResult rights = _progress.TryGrantChapterRefundRights(chapterId, rightsPerChapter, rightsMax,
                    out int added);
                return new ChapterClearCommit(encounter, boss, cleared, rights, added);
            }
            finally
            {
                _log.EndBatch();
            }
        }

        /// <summary>保存から会話・章の記録を置く（候補 Session の構築だけ。検証済みの値）。</summary>
        internal void RestoreStory(IEnumerable<string> events,
            IEnumerable<KeyValuePair<string, Momotaro.Gameplay.Story.ChapterRouteRecord>> routes,
            IEnumerable<KeyValuePair<string, Momotaro.Data.Story.StoryRoute>> clearedChapters)
        {
            _story.RestoreFrom(events, routes, clearedChapters);
        }

        /// <summary>保存からクエストの段階を置く（候補 Session の構築だけ。検証済みの値）。</summary>
        internal void RestoreQuestStages(IEnumerable<KeyValuePair<string, int>> stages)
        {
            _questStages.Clear();
            foreach (KeyValuePair<string, int> pair in stages)
            {
                _questStages[pair.Key] = pair.Value;
            }
        }

        // ---- 冒険・お地蔵様・復帰位置・きびだんご（P6A）----

        private readonly HashSet<StableId> _registeredShrines = new HashSet<StableId>();

        /// <summary>
        /// 冒険 ID（New Game で発行。保存 Envelope の adventureId と一致を検証する）。P5 の Session では空。
        /// </summary>
        public string AdventureId { get; private set; } = string.Empty;

        /// <summary>死亡用の再開地点（最後に登録したお地蔵様）。未設定なら空。</summary>
        public StableId Checkpoint { get; private set; }

        /// <summary>中断用の復帰位置（死亡用とは別の値。仕様 §6）。</summary>
        public ResumeAnchor Resume { get; private set; }

        /// <summary>きびだんごの残数（補充上限は定義と成長から算出し、ここには持たない）。</summary>
        public int Kibidango { get; private set; }

        /// <summary>登録済みお地蔵様の数（診断・テスト用）。</summary>
        public int RegisteredShrineCount => _registeredShrines.Count;

        /// <summary>登録済みか。</summary>
        public bool IsShrineRegistered(StableId shrineId) =>
            !shrineId.IsEmpty && _registeredShrines.Contains(shrineId);

        /// <summary>登録済みお地蔵様を列挙する（保存・旅立ち先一覧）。</summary>
        public IEnumerable<StableId> RegisteredShrines => _registeredShrines;

        /// <summary>
        /// New Game の初期化（P6 仕様 §5 末尾）。<b>一度だけ</b>：冒険 ID の発行、初期お地蔵様の登録と
        /// 死亡・中断の両地点、きびだんごの充填。2 回目以降は何もしない（古い保存の欠損をこの初期値で隠さない）。
        /// </summary>
        public bool InitializeNewAdventure(string adventureId, ShrineInfo initialShrine, int kibidangoCapacity,
            int refundRights = 0)
        {
            if (!string.IsNullOrEmpty(AdventureId) || string.IsNullOrEmpty(adventureId) || !initialShrine.IsValid)
            {
                return false;
            }

            _log.BeginBatch("new_adventure");
            try
            {
                AdventureId = adventureId;
                _registeredShrines.Add(initialShrine.ShrineId);
                Checkpoint = initialShrine.ShrineId;
                Resume = ResumeAnchor.AtShrine(initialShrine.AreaId, initialShrine.ShrineId);
                Kibidango = kibidangoCapacity < 0 ? 0 : kibidangoCapacity;
                _progress.SetInitialRefundRights(refundRights); // P6B 02：初期の払い戻し権利。
                _log.Touch("new_adventure", autosave: true);
            }
            finally
            {
                _log.EndBatch();
            }

            return true;
        }

        /// <summary>
        /// お地蔵様を調べた（仕様 §5）。登録・死亡地点・中断位置を更新して保存を要求する。
        /// <b>回復・補充・敵状態は変えない</b>（調べるだけでは休息しない）。
        /// </summary>
        public void RegisterShrine(ShrineInfo shrine)
        {
            if (!shrine.IsValid)
            {
                return;
            }

            _log.BeginBatch("shrine_registered");
            try
            {
                _registeredShrines.Add(shrine.ShrineId);
                Checkpoint = shrine.ShrineId;
                Resume = ResumeAnchor.AtShrine(shrine.AreaId, shrine.ShrineId);
                _log.Touch("shrine_registered", autosave: true);
            }
            finally
            {
                _log.EndBatch();
            }
        }

        /// <summary>
        /// 中断用の復帰位置だけを更新する（通常エリア到着・死亡復帰。仕様 §6 の表）。Checkpoint は変えない。
        /// </summary>
        public void SetResumeAnchor(ResumeAnchor anchor)
        {
            if (!anchor.IsValid || anchor.Equals(Resume))
            {
                return;
            }

            Resume = anchor;
            _log.Touch("resume_anchor", autosave: false);
        }

        /// <summary>きびだんごを上限まで補充する（休息等）。変化したら true。</summary>
        public bool RefillKibidango(int capacity)
        {
            int target = capacity < 0 ? 0 : capacity;
            if (Kibidango == target)
            {
                return false;
            }

            Kibidango = target;
            _log.Touch("kibidango_refilled", autosave: false);
            return true;
        }

        /// <summary>
        /// きびだんごを使う（P6A は検証用の口だけ。本番の使用入力・回復量は P6B）。足りなければ何もしない。
        /// </summary>
        public bool TryConsumeKibidango(int count)
        {
            if (count <= 0 || Kibidango < count)
            {
                return false;
            }

            Kibidango -= count;
            _log.Touch("kibidango_used", autosave: false);
            return true;
        }

        /// <summary>
        /// きびだんご使用の確定（P6B 03。仕様 §6〜§8）。<b>残数 -1 と回復を同じ 1 更新</b>で行い、版を進めて保存要求を
        /// 1 件出す。回復の適用は <paramref name="applyHeal"/>（主人公の Vitals）。残数が足りなければ何も変えず false。
        /// 保存の購読者はまとめの外（両方が終わった後）でしか呼ばれないので、HP と残数の食い違った組は採取されない。
        /// </summary>
        public bool TryCommitKibidangoUse(Action applyHeal)
        {
            if (Kibidango < 1)
            {
                return false;
            }

            _log.BeginBatch("kibidango_used");
            try
            {
                Kibidango -= 1;
                applyHeal?.Invoke();
                _log.Touch("kibidango_used", autosave: true);
            }
            finally
            {
                _log.EndBatch();
            }

            return true;
        }

        /// <summary>
        /// 保存からお地蔵様・復帰位置・きびだんご・冒険 ID・周期を置く（候補 Session の構築だけ。検証済みの値）。
        /// </summary>
        internal void RestoreAdventure(string adventureId, IEnumerable<StableId> registered, StableId checkpoint,
            ResumeAnchor resume, int kibidango, int respawnCycle, long revision)
        {
            AdventureId = adventureId ?? string.Empty;
            _registeredShrines.Clear();
            foreach (StableId id in registered)
            {
                _registeredShrines.Add(id);
            }

            Checkpoint = checkpoint;
            Resume = resume;
            Kibidango = kibidango;
            RespawnCycle = respawnCycle;
            _log.RestoreRevision(revision);
        }

        /// <summary>保存から訪問・加入を置く（候補 Session の構築だけ）。</summary>
        internal void RestoreVisitsAndRecruits(IEnumerable<StableId> visited, IEnumerable<StableId> recruited)
        {
            _visitedAreas.Clear();
            foreach (StableId id in visited)
            {
                _visitedAreas.Add(id);
            }

            _recruited.Clear();
            foreach (StableId id in recruited)
            {
                _recruited.Add(id);
            }
        }

        /// <summary>
        /// 本編型死亡再開の受付と一度限りの保証（§9.1）。
        ///
        /// <b>Area ではなく Session が持つ。</b> 再開は Scene をまたぐ操作で、受理から到着までの間に
        /// 元の Area は破棄される。Area 側に置くと「周期を進めたか」の記録ごと消え、
        /// 読込失敗からの再試行で周期が二度進む（§9.1 末尾が禁じている挙動）。
        /// </summary>
        public CampaignRespawnCoordinator Respawn { get; } = new CampaignRespawnCoordinator();

        /// <summary>
        /// 再出現周期を 1 進める（P5：本編型死亡再開。P6：休息の成立）。
        ///
        /// 普通敵の撃破記録は周期で一致しなくなる（＝全世界で復活。未ロードの Area は次の生成時に反映）。
        /// P5 規則の campaign では<b>通常 Encounter のクリア記録</b>も初期化する（§9.1）。
        /// 徳・GrantOnce 記録・調査済み・門の開通・訪問済み・加入・恒久クリア・ボス・配置物は<b>変更しない</b>。
        /// 要求 1 件につき 1 回だけ呼ぶこと（P5-E21／P6A-03）。保存は呼び出し側が確定後に要求する。
        /// </summary>
        public void AdvanceRespawnCycle()
        {
            RespawnCycle++;
            foreach (KeyValuePair<StableId, AreaRuntimeState> pair in _areas)
            {
                if (_log.EncounterPolicy == EncounterClearPolicy.PerRespawnCycle)
                {
                    pair.Value.ClearNormalEncounterRecords();
                }

                pair.Value.PruneFieldDefeats(RespawnCycle);
            }

            _log.Touch("respawn_cycle", autosave: false);
        }

        // ---- 確定（P6A-01。仕様 §4）----

        /// <summary>
        /// 通常エリア遷移の Commit で到着を記録する（訪問と初到達報酬を<b>同時に</b>確定する。仕様 §4）。
        ///
        /// Prepared やロード開始では呼ばない。<b>Load 復元では呼ばない</b>（Load は報酬を出さない。§10）。
        /// 初到達報酬は GrantOnce（報酬 ID で一度だけ）。再訪では報酬なしで保存だけ要求する（§8 の表）。
        /// </summary>
        public ArrivalCommit CommitArrival(StableId areaId, in RewardSnapshot arrivalReward)
        {
            if (areaId.IsEmpty)
            {
                return default;
            }

            _log.BeginBatch("area_arrival");
            try
            {
                bool first = MarkVisited(areaId);
                int granted = 0;
                RewardGrantResult result = RewardGrantResult.NoReward;
                if (first && arrivalReward.HasReward)
                {
                    result = _progress.TryGrant(arrivalReward, out granted);
                }

                _log.RequestAutosave("area_arrival");
                return new ArrivalCommit(first, result, granted);
            }
            finally
            {
                _log.EndBatch();
            }
        }

        /// <summary>
        /// 遭遇戦の最終撃破を確定する（仕様 §4）。<b>クリア記録・初回殲滅ボーナス・開通</b>を 1 つの更新として行い、
        /// 保存要求は最後に 1 件だけ出す。既にクリア済みなら何もしない（勝利の二重記録・ボーナスの二重取得を防ぐ）。
        /// </summary>
        /// <param name="areaId">遭遇戦の所属 Area。</param>
        /// <param name="encounterId">遭遇戦 ID（Area 内で独立）。</param>
        /// <param name="clearBonus">初回殲滅ボーナス（GrantOnce を想定。無ければ None）。</param>
        /// <param name="unlockFlagId">クリアで開ける仕掛け（無ければ空）。</param>
        public EncounterClearCommit CommitEncounterClear(
            StableId areaId, StableId encounterId, in RewardSnapshot clearBonus, StableId unlockFlagId)
        {
            if (areaId.IsEmpty || encounterId.IsEmpty)
            {
                return default;
            }

            AreaRuntimeState area = GetOrCreateArea(areaId);
            _log.BeginBatch("encounter_cleared");
            try
            {
                if (!area.TryMarkEncounterCleared(encounterId, RespawnCycle))
                {
                    return default;
                }

                int granted = 0;
                RewardGrantResult result = RewardGrantResult.NoReward;
                if (clearBonus.HasReward)
                {
                    result = _progress.TryGrant(clearBonus, out granted);
                }

                bool opened = !unlockFlagId.IsEmpty && area.TryOpen(unlockFlagId);
                return new EncounterClearCommit(true, result, granted, opened);
            }
            finally
            {
                _log.EndBatch();
            }
        }

        /// <summary>
        /// 普通敵の撃破を確定する（仕様 §4）。<b>現在周期の撃破記録と徳付与を同時に</b>行う。
        /// 同じ配置個体の重複通知は 1 回分（記録済みなら何もしない）。同じ敵種でも配置 ID が違えば別個体。
        /// </summary>
        public bool TryRecordFieldDefeat(StableId areaId, StableId placementId, in RewardSnapshot reward, out int grantedVirtue)
        {
            grantedVirtue = 0;
            if (areaId.IsEmpty || placementId.IsEmpty)
            {
                return false;
            }

            AreaRuntimeState area = GetOrCreateArea(areaId);
            _log.BeginBatch("field_enemy_defeated");
            try
            {
                if (!area.TryMarkFieldEnemyDefeated(placementId, RespawnCycle))
                {
                    return false;
                }

                if (reward.HasReward)
                {
                    _progress.TryGrant(reward, out grantedVirtue);
                }

                return true;
            }
            finally
            {
                _log.EndBatch();
            }
        }

        /// <summary>
        /// 中ボス・章ボス等の撃破を確定する（恒久。仕様 §4）。撃破記録と報酬を同時に行う。
        /// 撃破確定後は死亡・休息で戻さない。記録済みなら何もしない。
        /// </summary>
        public bool TryRecordBossDefeat(StableId areaId, StableId bossId, in RewardSnapshot reward, out int grantedVirtue)
        {
            grantedVirtue = 0;
            if (areaId.IsEmpty || bossId.IsEmpty)
            {
                return false;
            }

            AreaRuntimeState area = GetOrCreateArea(areaId);
            _log.BeginBatch("boss_defeated");
            try
            {
                if (!area.TryRecordBossDefeat(bossId))
                {
                    return false;
                }

                if (reward.HasReward)
                {
                    _progress.TryGrant(reward, out grantedVirtue);
                }

                return true;
            }
            finally
            {
                _log.EndBatch();
            }
        }

        /// <summary>
        /// 配置物・宝箱を取得する（仕様 §7）。<b>取得記録と個数（と徳）を同じ更新で</b>確定する。
        /// 所持上限を超えるなら<b>取得全体を拒否</b>し、未取得のまま残す（再取得可能）。複合報酬でも徳だけ部分付与しない。
        /// </summary>
        public PlacementPickResult TryPickPlacement(
            StableId areaId, StableId placementId, StableId itemId, int count, int maxStack, in RewardSnapshot reward)
        {
            if (areaId.IsEmpty || placementId.IsEmpty)
            {
                return PlacementPickResult.Invalid;
            }

            AreaRuntimeState area = GetOrCreateArea(areaId);
            if (area.IsPlacementPicked(placementId))
            {
                return PlacementPickResult.AlreadyPicked;
            }

            bool hasItem = !itemId.IsEmpty;
            if (hasItem)
            {
                InventoryChangeResult check = _inventory.CanAdd(itemId, count, maxStack);
                if (check == InventoryChangeResult.OverCapacity)
                {
                    return PlacementPickResult.OverCapacity;
                }

                if (check != InventoryChangeResult.Applied)
                {
                    return PlacementPickResult.Invalid;
                }
            }

            _log.BeginBatch("placement_picked");
            try
            {
                area.TryMarkPlacementPicked(placementId);
                if (hasItem)
                {
                    _inventory.TryAdd(itemId, count, maxStack);
                }

                if (reward.HasReward)
                {
                    _progress.TryGrant(reward, out _);
                }

                return PlacementPickResult.Picked;
            }
            finally
            {
                _log.EndBatch();
            }
        }

        /// <summary>
        /// 発見・仮クエストの報酬を付与する（GrantOnce。仕様 §4 末尾）。将来 P7 から同じ口へ接続する。
        /// </summary>
        public RewardGrantResult GrantDiscovery(in RewardSnapshot reward, out int grantedVirtue)
        {
            _log.BeginBatch("discovery");
            try
            {
                return _progress.TryGrant(reward, out grantedVirtue);
            }
            finally
            {
                _log.EndBatch();
            }
        }
    }

    /// <summary>中断用復帰位置の種別（P6A）。</summary>
    public enum ResumeAnchorKind
    {
        /// <summary>未設定。</summary>
        None = 0,

        /// <summary>エリアの入口（通常の到着）。</summary>
        Entry = 1,

        /// <summary>お地蔵様の安全復帰点。</summary>
        Shrine = 2,
    }

    /// <summary>
    /// 中断用の復帰位置（P6A。仕様 §6）。<b>ワールド座標は持たない</b>——入口 ID かお地蔵様 ID から置き直す。
    /// </summary>
    public readonly struct ResumeAnchor : System.IEquatable<ResumeAnchor>
    {
        private ResumeAnchor(ResumeAnchorKind kind, StableId areaId, StableId pointId)
        {
            Kind = kind;
            AreaId = areaId;
            PointId = pointId;
        }

        public ResumeAnchorKind Kind { get; }

        /// <summary>所属エリア。</summary>
        public StableId AreaId { get; }

        /// <summary>入口 ID（Entry）またはお地蔵様 ID（Shrine）。</summary>
        public StableId PointId { get; }

        public bool IsValid => Kind != ResumeAnchorKind.None && !AreaId.IsEmpty && !PointId.IsEmpty;

        public static ResumeAnchor AtEntry(StableId areaId, StableId entryId) =>
            new ResumeAnchor(ResumeAnchorKind.Entry, areaId, entryId);

        public static ResumeAnchor AtShrine(StableId areaId, StableId shrineId) =>
            new ResumeAnchor(ResumeAnchorKind.Shrine, areaId, shrineId);

        /// <summary>保存からの復元用。</summary>
        public static ResumeAnchor From(ResumeAnchorKind kind, StableId areaId, StableId pointId) =>
            new ResumeAnchor(kind, areaId, pointId);

        public bool Equals(ResumeAnchor other) =>
            Kind == other.Kind && AreaId.Equals(other.AreaId) && PointId.Equals(other.PointId);

        public override bool Equals(object obj) => obj is ResumeAnchor other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ AreaId.GetHashCode() ^ (PointId.GetHashCode() * 31);

        public override string ToString() => Kind + ":" + AreaId.Value + "/" + PointId.Value;
    }

    /// <summary>到着の確定結果（P6A-01）。</summary>
    public readonly struct ArrivalCommit
    {
        public ArrivalCommit(bool firstVisit, RewardGrantResult reward, int grantedVirtue)
        {
            FirstVisit = firstVisit;
            Reward = reward;
            GrantedVirtue = grantedVirtue;
        }

        /// <summary>初到達だったか。</summary>
        public bool FirstVisit { get; }

        /// <summary>到達報酬の付与結果。</summary>
        public RewardGrantResult Reward { get; }

        /// <summary>実際に加算した徳。</summary>
        public int GrantedVirtue { get; }
    }

    /// <summary>遭遇戦クリアの確定結果（P6A-01）。</summary>
    public readonly struct EncounterClearCommit
    {
        public EncounterClearCommit(bool recorded, RewardGrantResult bonus, int grantedVirtue, bool flagOpened)
        {
            Recorded = recorded;
            Bonus = bonus;
            GrantedVirtue = grantedVirtue;
            FlagOpened = flagOpened;
        }

        /// <summary>この呼び出しでクリアを記録したか（false なら他も何もしていない）。</summary>
        public bool Recorded { get; }

        /// <summary>初回ボーナスの付与結果。</summary>
        public RewardGrantResult Bonus { get; }

        /// <summary>ボーナスで加算した徳。</summary>
        public int GrantedVirtue { get; }

        /// <summary>この呼び出しで仕掛けを開けたか。</summary>
        public bool FlagOpened { get; }
    }

    /// <summary>章ボスの勝利の確定結果（P7 04）。</summary>
    public readonly struct ChapterClearCommit
    {
        public ChapterClearCommit(EncounterClearCommit encounter, bool bossRecorded, bool chapterCleared,
            ChapterRightsResult rights, int rightsAdded)
        {
            Encounter = encounter;
            BossRecorded = bossRecorded;
            ChapterCleared = chapterCleared;
            Rights = rights;
            RightsAdded = rightsAdded;
        }

        /// <summary>遭遇戦のクリア（初回ボーナス・開通を含む）。<c>Recorded</c> が false なら他も何もしていない。</summary>
        public EncounterClearCommit Encounter { get; }

        /// <summary>この呼び出しでボス撃破を記録したか。</summary>
        public bool BossRecorded { get; }

        /// <summary>この呼び出しで章クリアを記録したか。</summary>
        public bool ChapterCleared { get; }

        /// <summary>払い戻し権利の追加の結果（上限で 0 でも Processed）。</summary>
        public ChapterRightsResult Rights { get; }

        /// <summary>実際に増えた権利（上限なら 0。通知はこの値を出す）。</summary>
        public int RightsAdded { get; }
    }

    /// <summary>配置物の取得結果（P6A-01）。</summary>
    public enum PlacementPickResult
    {
        /// <summary>取得した。</summary>
        Picked = 0,

        /// <summary>取得済み（再取得不可）。</summary>
        AlreadyPicked = 1,

        /// <summary>所持上限を超えるため全体拒否（未取得のまま）。</summary>
        OverCapacity = 2,

        /// <summary>ID・個数が不正。</summary>
        Invalid = 3,
    }
}
