using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Progression;
using UnityEngine;

namespace Momotaro.Data.Story
{
    /// <summary>経路の種類（P7。仕様 §7）。0 は「未記録」。</summary>
    public enum StoryRoute
    {
        /// <summary>未記録。</summary>
        None = 0,

        /// <summary>標準ルート。</summary>
        Standard = 1,

        /// <summary>困難ルート。</summary>
        Hard = 2,
    }

    /// <summary>
    /// 会話の候補を選ぶ条件の種類（P7。仕様 §4「章クリア済み、依頼状態、既知の進行フラグ、経路記録の参照に限定」）。
    /// <b>自由なスクリプトへ広げない。</b> 種類を足すときは評価・Validator・テストを同時に足す。
    /// </summary>
    public enum StoryConditionKind
    {
        /// <summary>未設定（Validator が拒否する）。</summary>
        None = 0,

        /// <summary>章クリア済み（<see cref="StoryCondition.ChapterId"/>）。</summary>
        ChapterCleared = 1,

        /// <summary>依頼の状態が一致（<see cref="StoryCondition.QuestId"/>・<see cref="StoryCondition.QuestState"/>）。</summary>
        QuestState = 2,

        /// <summary>必須イベントが完了済み（<see cref="StoryCondition.EventId"/>）。</summary>
        EventCompleted = 3,

        /// <summary>経路に到達済み（<see cref="StoryCondition.ChapterId"/>・<see cref="StoryCondition.Route"/>）。</summary>
        RouteReached = 4,

        /// <summary>経路を踏破済み（同上）。</summary>
        RouteCompleted = 5,
    }

    /// <summary>条件で参照する依頼の状態（P7。保存はしない——受注記録と世界の確定状態から導く）。</summary>
    public enum QuestStateKind
    {
        /// <summary>未受注。</summary>
        NotAccepted = 0,

        /// <summary>受注済みで条件が未成立。</summary>
        InProgress = 1,

        /// <summary>受注済みで条件が成立（報告できる）。</summary>
        Reportable = 2,

        /// <summary>報酬受領済み。</summary>
        Rewarded = 3,
    }

    /// <summary>会話の候補の条件 1 つ（P7）。<see cref="Negate"/> で否定できる（「未到達」など）。</summary>
    [Serializable]
    public sealed class StoryCondition
    {
        [SerializeField] private StoryConditionKind _kind;
        [SerializeField] private bool _negate;
        [SerializeField] private StableId _chapterId;
        [SerializeField] private StableId _questId;
        [SerializeField] private QuestStateKind _questState;
        [SerializeField] private StableId _eventId;
        [SerializeField] private StoryRoute _route;

        public StoryConditionKind Kind => _kind;
        public bool Negate => _negate;
        public StableId ChapterId => _chapterId;
        public StableId QuestId => _questId;
        public QuestStateKind QuestState => _questState;
        public StableId EventId => _eventId;
        public StoryRoute Route => _route;

        public static StoryCondition ChapterCleared(StableId chapterId, bool negate = false) =>
            new StoryCondition { _kind = StoryConditionKind.ChapterCleared, _chapterId = chapterId, _negate = negate };

        public static StoryCondition Quest(StableId questId, QuestStateKind state, bool negate = false) =>
            new StoryCondition { _kind = StoryConditionKind.QuestState, _questId = questId, _questState = state, _negate = negate };

        public static StoryCondition Event(StableId eventId, bool negate = false) =>
            new StoryCondition { _kind = StoryConditionKind.EventCompleted, _eventId = eventId, _negate = negate };

        public static StoryCondition RouteReached(StableId chapterId, StoryRoute route, bool negate = false) =>
            new StoryCondition { _kind = StoryConditionKind.RouteReached, _chapterId = chapterId, _route = route, _negate = negate };

        public static StoryCondition RouteCompleted(StableId chapterId, StoryRoute route, bool negate = false) =>
            new StoryCondition { _kind = StoryConditionKind.RouteCompleted, _chapterId = chapterId, _route = route, _negate = negate };

        /// <summary>同じ判定をする条件か（優先度の曖昧さの検出用）。</summary>
        public string Signature =>
            (_negate ? "!" : string.Empty) + (int)_kind + ":" + _chapterId.Value + ":" + _questId.Value + ":"
            + (int)_questState + ":" + _eventId.Value + ":" + (int)_route;
    }

    /// <summary>住民（P7。静止した非戦闘キャラクター）。</summary>
    [Serializable]
    public sealed class VillagerDefinition
    {
        [SerializeField] private StableId _villagerId;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private StableId _areaId;

        public StableId VillagerId => _villagerId;
        public string DisplayName => _displayName ?? string.Empty;
        public StableId AreaId => _areaId;

#if UNITY_EDITOR
        public void EditorSet(StableId villagerId, string displayName, StableId areaId)
        {
            _villagerId = villagerId;
            _displayName = displayName;
            _areaId = areaId;
        }
#endif
    }

    /// <summary>
    /// 会話の候補 1 つ（P7。仕様 §4）。住民ごとに候補を並べ、<b>条件（AND）が成り立つもののうち優先度の最も高いもの</b>を
    /// 会話の入口で 1 つ選ぶ。住民ごとに<b>無条件の候補が 1 つ以上</b>必要（Validator）。
    /// </summary>
    [Serializable]
    public sealed class DialogueDefinition
    {
        [SerializeField] private StableId _dialogueId;
        [SerializeField] private StableId _villagerId;
        [SerializeField] private int _priority;
        [SerializeField] private List<StoryCondition> _conditions = new List<StoryCondition>();
        [SerializeField] private string _speaker = string.Empty;
        [SerializeField, TextArea] private List<string> _pages = new List<string>();

        [Tooltip("この会話の最後の決定で完了させる必須イベント（無ければ空）。")]
        [SerializeField] private StableId _completesEventId;

        [Tooltip("必須イベントを完了させる決定の文言。")]
        [SerializeField] private string _confirmLabel = string.Empty;

        public StableId DialogueId => _dialogueId;
        public StableId VillagerId => _villagerId;
        public int Priority => _priority;
        public IReadOnlyList<StoryCondition> Conditions => _conditions;
        public string Speaker => _speaker ?? string.Empty;
        public IReadOnlyList<string> Pages => _pages;
        public StableId CompletesEventId => _completesEventId;
        public string ConfirmLabel => _confirmLabel ?? string.Empty;
        public bool IsUnconditional => _conditions == null || _conditions.Count == 0;

#if UNITY_EDITOR
        public void EditorSet(StableId dialogueId, StableId villagerId, int priority, string speaker, List<string> pages,
            List<StoryCondition> conditions = null, StableId completesEventId = default, string confirmLabel = null)
        {
            _dialogueId = dialogueId;
            _villagerId = villagerId;
            _priority = priority;
            _speaker = speaker;
            _pages = pages ?? new List<string>();
            _conditions = conditions ?? new List<StoryCondition>();
            _completesEventId = completesEventId;
            _confirmLabel = confirmLabel ?? string.Empty;
        }
#endif
    }

    /// <summary>依頼の達成条件の種類（P7。仕様 §5 の 3 種類だけ）。</summary>
    public enum QuestObjectiveKind
    {
        /// <summary>未設定（Validator が拒否する）。</summary>
        None = 0,

        /// <summary>指定 Area への到達（訪問済み）。</summary>
        AreaReached = 1,

        /// <summary>指定対象の発見（その Area の配置物の取得、または調査点の調査）。</summary>
        Discovery = 2,

        /// <summary>指定遭遇戦のクリア（恒久クリア）。</summary>
        EncounterCleared = 3,
    }

    /// <summary>依頼の達成条件 1 つ（P7）。</summary>
    [Serializable]
    public sealed class QuestObjective
    {
        [SerializeField] private QuestObjectiveKind _kind;
        [SerializeField] private StableId _areaId;

        [Tooltip("発見：配置物 ID か調査点 ID。遭遇戦：遭遇戦 ID。到達では使わない。")]
        [SerializeField] private StableId _targetId;

        [SerializeField] private string _label = string.Empty;

        public QuestObjectiveKind Kind => _kind;
        public StableId AreaId => _areaId;
        public StableId TargetId => _targetId;
        public string Label => _label ?? string.Empty;

        public static QuestObjective Reach(StableId areaId, string label) =>
            new QuestObjective { _kind = QuestObjectiveKind.AreaReached, _areaId = areaId, _label = label };

        public static QuestObjective Discover(StableId areaId, StableId targetId, string label) =>
            new QuestObjective { _kind = QuestObjectiveKind.Discovery, _areaId = areaId, _targetId = targetId, _label = label };

        public static QuestObjective ClearEncounter(StableId areaId, StableId encounterId, string label) =>
            new QuestObjective { _kind = QuestObjectiveKind.EncounterCleared, _areaId = areaId, _targetId = encounterId, _label = label };
    }

    /// <summary>
    /// 住民の依頼（P7。仕様 §5・§6）。状態は既存のクエスト段階（0＝未受注・1＝受注済み・2＝受領済み）で保存し、
    /// 「進行中」「報告可能」は受注と達成条件から導く。報酬は既存の報酬台帳（GrantOnce・報酬 ID）で付与する。
    /// </summary>
    [Serializable]
    public sealed class QuestDefinition
    {
        [SerializeField] private StableId _questId;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private StableId _giverVillagerId;
        [SerializeField, TextArea] private string _objectiveText = string.Empty;
        [SerializeField, TextArea] private List<string> _offerPages = new List<string>();
        [SerializeField, TextArea] private string _progressText = string.Empty;
        [SerializeField, TextArea] private string _reportText = string.Empty;
        [SerializeField] private RewardData _reward;
        [SerializeField] private List<QuestObjective> _objectives = new List<QuestObjective>();

        public StableId QuestId => _questId;
        public string DisplayName => _displayName ?? string.Empty;
        public StableId GiverVillagerId => _giverVillagerId;
        public string ObjectiveText => _objectiveText ?? string.Empty;
        public IReadOnlyList<string> OfferPages => _offerPages;
        public string ProgressText => _progressText ?? string.Empty;
        public string ReportText => _reportText ?? string.Empty;
        public RewardData Reward => _reward;
        public IReadOnlyList<QuestObjective> Objectives => _objectives;

#if UNITY_EDITOR
        public void EditorSet(StableId questId, string displayName, StableId giverVillagerId, string objectiveText,
            List<string> offerPages, string progressText, string reportText, RewardData reward, List<QuestObjective> objectives)
        {
            _questId = questId;
            _displayName = displayName;
            _giverVillagerId = giverVillagerId;
            _objectiveText = objectiveText;
            _offerPages = offerPages ?? new List<string>();
            _progressText = progressText;
            _reportText = reportText;
            _reward = reward;
            _objectives = objectives ?? new List<QuestObjective>();
        }
#endif
    }

    /// <summary>
    /// 必須イベント（P7。仕様 §7「安定した EventId の完了フラグ」）。完了は会話の最後の決定で 1 回だけ確定し、
    /// 同じ更新で <see cref="OpensAreaId"/>／<see cref="OpensFlagId"/> の仕掛けを開通させる（既存の門 <c>AreaFlagDoor</c> がそれを見る）。
    /// </summary>
    [Serializable]
    public sealed class StoryEventDefinition
    {
        [SerializeField] private StableId _eventId;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private StableId _opensAreaId;
        [SerializeField] private StableId _opensFlagId;

        [Tooltip("未完了のとき門の手前で示す理由。")]
        [SerializeField] private string _lockedNotice = string.Empty;

        public StableId EventId => _eventId;
        public string DisplayName => _displayName ?? string.Empty;
        public StableId OpensAreaId => _opensAreaId;
        public StableId OpensFlagId => _opensFlagId;
        public string LockedNotice => _lockedNotice ?? string.Empty;
        public bool OpensFlag => !_opensFlagId.IsEmpty;

#if UNITY_EDITOR
        public void EditorSet(StableId eventId, string displayName, StableId opensAreaId, StableId opensFlagId, string lockedNotice)
        {
            _eventId = eventId;
            _displayName = displayName;
            _opensAreaId = opensAreaId;
            _opensFlagId = opensFlagId;
            _lockedNotice = lockedNotice;
        }
#endif
    }

    /// <summary>
    /// 章の 1 経路の定義（P7。仕様 §7）。<see cref="Areas"/> に入れば「到達」、<see cref="TerminalAreaId"/> へ
    /// <see cref="TerminalEntryId"/> の入口から通常の移動で着けば「踏破」（合流へ入る接続口）。
    /// </summary>
    [Serializable]
    public sealed class ChapterRouteDefinition
    {
        [SerializeField] private List<StableId> _areas = new List<StableId>();
        [SerializeField] private StableId _terminalAreaId;
        [SerializeField] private StableId _terminalEntryId;

        public IReadOnlyList<StableId> Areas => _areas;
        public StableId TerminalAreaId => _terminalAreaId;
        public StableId TerminalEntryId => _terminalEntryId;

#if UNITY_EDITOR
        public void EditorSet(List<StableId> areas, StableId terminalAreaId, StableId terminalEntryId)
        {
            _areas = areas ?? new List<StableId>();
            _terminalAreaId = terminalAreaId;
            _terminalEntryId = terminalEntryId;
        }
#endif
    }

    /// <summary>
    /// 章（P7。仕様 §7・§8）。章ボス（Area とボス ID＝遭遇戦 ID）の正規の撃破確定で章クリアにする。
    /// 二つの経路と、ボス前の地点（Area と、そこへ通常の移動で入る入口）を持つ。
    /// </summary>
    [Serializable]
    public sealed class ChapterDefinition
    {
        [SerializeField] private StableId _chapterId;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private StableId _bossAreaId;
        [SerializeField] private StableId _bossId;
        [SerializeField] private ChapterRouteDefinition _standard = new ChapterRouteDefinition();
        [SerializeField] private ChapterRouteDefinition _hard = new ChapterRouteDefinition();
        [SerializeField] private StableId _bossFrontAreaId;
        [SerializeField] private List<StableId> _bossFrontEntryIds = new List<StableId>();

        public StableId ChapterId => _chapterId;
        public string DisplayName => _displayName ?? string.Empty;
        public StableId BossAreaId => _bossAreaId;
        public StableId BossId => _bossId;
        public ChapterRouteDefinition Standard => _standard;
        public ChapterRouteDefinition Hard => _hard;
        public StableId BossFrontAreaId => _bossFrontAreaId;
        public IReadOnlyList<StableId> BossFrontEntryIds => _bossFrontEntryIds;

#if UNITY_EDITOR
        public void EditorSet(StableId chapterId, string displayName, StableId bossAreaId, StableId bossId,
            ChapterRouteDefinition standard, ChapterRouteDefinition hard, StableId bossFrontAreaId, List<StableId> bossFrontEntryIds)
        {
            _chapterId = chapterId;
            _displayName = displayName;
            _bossAreaId = bossAreaId;
            _bossId = bossId;
            _standard = standard ?? new ChapterRouteDefinition();
            _hard = hard ?? new ChapterRouteDefinition();
            _bossFrontAreaId = bossFrontAreaId;
            _bossFrontEntryIds = bossFrontEntryIds ?? new List<StableId>();
        }
#endif
    }
}
