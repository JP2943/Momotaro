using System;
using System.Collections.Generic;
using System.Globalization;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Data.World;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.World;

namespace Momotaro.Infrastructure.Save
{
    /// <summary>
    /// New Game と Continue の手順（P6A-02／05。仕様 §10）。タイトル（Launcher）から呼ぶ。
    ///
    /// <b>Continue は候補 Session で行う。</b> 保存を検証して候補を作り、保存先の Area を準備し、最大値の計算・
    /// 資源の復元・安全位置・門と撃破と Trigger の同期が<b>全部済んでから</b>採用する。準備に失敗したら候補を捨て、
    /// 元の正本（タイトルでは無し）へ戻す。ファイルは変更しない。
    ///
    /// <b>New Game は前の冒険を退避してから。</b> 退避に失敗したら始めない（上書きしない）。
    /// 確認は呼び出し側（タイトルの表示）が開始時に取る。
    /// </summary>
    public sealed class CampaignAdventureFlow
    {
        private readonly AreaTransitionService _transitions;
        private readonly GameSessionBootService _sessions;
        private readonly CampaignSaveService _saves;

        private int _loadTransitionId;
        private SaveSnapshot _loading;
        private CampaignCatalog _loadingCampaign;

        public CampaignAdventureFlow(AreaTransitionService transitions, GameSessionBootService sessions,
            CampaignSaveService saves)
        {
            _transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        }

        /// <summary>Continue の準備中か。</summary>
        public bool IsContinuing => _loadTransitionId != 0;

        /// <summary>直近の Continue で片側破損から復旧したか（通知する）。</summary>
        public string RecoveryNotice { get; private set; } = string.Empty;

        /// <summary>Continue が採用まで済んだ。</summary>
        public event Action Continued;

        /// <summary>Continue が失敗した（理由つき）。候補は捨て済み、ファイルは未変更。</summary>
        public event Action<string> ContinueFailed;

        /// <summary>
        /// いまの保存の状態を見る（タイトルの「続きから」の表示用。読み取りだけ）。
        /// </summary>
        public SaveLoadDecision PeekSave()
        {
            return _saves.EnsureCoordinator().Store.DecideLoad();
        }

        /// <summary>campaign の保存スロットで覗く（P6B。campaign ごとに保存領域を分ける）。</summary>
        public SaveLoadDecision PeekSave(AreaCatalogData catalogData)
        {
            if (catalogData != null)
            {
                _saves.UseSlot(catalogData.SaveSlotName);
            }

            return PeekSave();
        }

        /// <summary>
        /// New Game（仕様 §5 末尾・§10）。前の冒険を退避し、新しい Session を作って初期お地蔵様の入口へ向かう。
        /// </summary>
        public bool TryNewGame(AreaCatalogData catalogData, out string error)
        {
            if (!TryBuildCampaign(catalogData, out AreaCatalog catalog, out error))
            {
                return false;
            }

            CampaignCatalog campaign = catalog.Campaign;
            if (!campaign.TryGetShrine(campaign.InitialShrineId, out ShrineInfo initial))
            {
                error = "初期お地蔵様を解決できません。";
                return false;
            }

            if (!_saves.UseSlot(campaign.SaveSlotName))
            {
                error = "別の冒険が結ばれたままです（保存スロットを切り替えられません）。";
                return false;
            }

            SaveCoordinator saves = _saves.EnsureCoordinator();
            if (!saves.Store.TryAcquireLock(out error))
            {
                return false;
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            if (!saves.Store.TryArchiveCurrent(stamp, out string archived, out error))
            {
                // 退避に失敗したら上書きしない。前の冒険はスロットに残る。
                return false;
            }

            if (archived != null)
            {
                GameLog.Info(LogCategory.Boot, "Archived the previous adventure to " + archived);
            }

            GameSessionState session = _sessions.StartNewSession(EncounterClearPolicy.Permanent);
            session.InitializeNewAdventure(Guid.NewGuid().ToString("N"), initial,
                campaign.KibidangoCapacityOf(session.Progress), campaign.RefundRightsInitial);
            _saves.BindAdventure(session, campaign, -1);

            _transitions.Bind(catalogData, new LauncherTravelConditions());
            AreaTransitionDecision decision = _transitions.TryTravel(initial.AreaId, initial.Entry.EntryId);
            if (!decision.Accepted)
            {
                error = "初期お地蔵様へ移動できません（" + decision.Rejection + "）。";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Continue（仕様 §10）。検証 → 候補 Session → 保存先の Area を Single で準備 → 採用。
        /// 結果は <see cref="Continued"/>／<see cref="ContinueFailed"/> で返る。受理できなかった場合は false。
        /// </summary>
        public bool TryContinue(AreaCatalogData catalogData, out string error)
        {
            RecoveryNotice = string.Empty;
            if (IsContinuing)
            {
                error = "再開の準備中です。";
                return false;
            }

            if (!TryBuildCampaign(catalogData, out AreaCatalog catalog, out error))
            {
                return false;
            }

            if (!_saves.UseSlot(catalog.Campaign.SaveSlotName))
            {
                error = "別の冒険が結ばれたままです（保存スロットを切り替えられません）。";
                return false;
            }

            SaveLoadDecision decision = _saves.EnsureCoordinator().Store.DecideLoad();
            if (!decision.CanLoad)
            {
                error = DescribeUnloadable(decision);
                return false;
            }

            if (!SaveJsonCodec.TryDeserialize(decision.Chosen.Json, out _, out SaveSnapshot snapshot, out error))
            {
                return false;
            }

            if (!SessionRestorer.TryBuildCandidate(snapshot, catalog, out GameSessionState candidate, out error))
            {
                // 黙って New Game にしない。
                return false;
            }

            if (!TryResolveResume(snapshot, catalog, out AreaEntryInfo entry, out error))
            {
                return false;
            }

            if (!_sessions.BeginCandidate(candidate))
            {
                error = "別の再開の準備が残っています。";
                return false;
            }

            _transitions.Bind(catalogData, new LauncherTravelConditions());
            _transitions.LoadTravelCompleted += OnLoadCompleted;
            _transitions.LoadTravelFailed += OnLoadFailed;
            AreaTransitionDecision travel = _transitions.TryLoadTravel(entry.AreaId, entry.EntryId, snapshot.Party);
            if (!travel.Accepted)
            {
                Unsubscribe();
                _sessions.RevertCandidate();
                error = "保存先へ移動できません（" + travel.Rejection + "）。";
                return false;
            }

            _loadTransitionId = travel.TransitionId;
            _loading = snapshot;
            _loadingCampaign = catalog.Campaign;
            if (decision.Verdict == SaveLoadVerdict.RecoveredFromOtherSide)
            {
                RecoveryNotice = decision.Detail;
            }

            error = null;
            return true;
        }

        private void OnLoadCompleted(int transitionId)
        {
            if (transitionId != _loadTransitionId)
            {
                return;
            }

            Unsubscribe();
            _sessions.CommitCandidate();
            _saves.BindAdventure(_sessions.Session, _loadingCampaign, _loading.Revision);
            _loadTransitionId = 0;
            _loading = null;
            _loadingCampaign = null;
            Continued?.Invoke();
        }

        private void OnLoadFailed(int transitionId)
        {
            if (transitionId != _loadTransitionId)
            {
                return;
            }

            Unsubscribe();
            _sessions.RevertCandidate();
            _loadTransitionId = 0;
            _loading = null;
            _loadingCampaign = null;
            GameModeProvider.Current?.ChangeMode(GameMode.Loading);
            ContinueFailed?.Invoke(_transitions.TerminalFailureReason ?? "保存からの再開に失敗しました。");
        }

        private void Unsubscribe()
        {
            _transitions.LoadTravelCompleted -= OnLoadCompleted;
            _transitions.LoadTravelFailed -= OnLoadFailed;
        }

        private static bool TryResolveResume(SaveSnapshot snapshot, AreaCatalog catalog, out AreaEntryInfo entry,
            out string error)
        {
            entry = default;
            var areaId = new StableId(snapshot.ResumeAreaId);
            var pointId = new StableId(snapshot.ResumePointId);
            if (snapshot.ResumeKind == ResumeAnchorKind.Entry && catalog.TryGetEntry(areaId, pointId, out entry))
            {
                error = null;
                return true;
            }

            if (snapshot.ResumeKind == ResumeAnchorKind.Shrine
                && catalog.Campaign.TryGetShrine(pointId, out ShrineInfo shrine))
            {
                entry = shrine.Entry;
                error = null;
                return true;
            }

            error = "中断用の復帰位置を解決できません。";
            return false;
        }

        private static bool TryBuildCampaign(AreaCatalogData data, out AreaCatalog catalog, out string error)
        {
            if (!AreaCatalog.TryBuild(data, out catalog, out IReadOnlyList<string> errors))
            {
                error = "カタログを構築できません: " + string.Join(" / ", errors);
                return false;
            }

            if (catalog.Campaign == null)
            {
                error = "P6 campaign のカタログではありません。";
                return false;
            }

            error = null;
            return true;
        }

        private static string DescribeUnloadable(SaveLoadDecision decision)
        {
            switch (decision.Verdict)
            {
                case SaveLoadVerdict.NoSave:
                    return "保存がありません。";
                case SaveLoadVerdict.BothCorrupt:
                    return "保存が壊れていて読めません（ファイルはそのまま残しています）: " + decision.Detail;
                case SaveLoadVerdict.MixedAdventures:
                    return "別の冒険の保存が混在しています: " + decision.Detail;
                case SaveLoadVerdict.GenerationConflict:
                    return "保存が食い違っています: " + decision.Detail;
                default:
                    return "保存を読めません。";
            }
        }
    }

    /// <summary>
    /// タイトルからの最初の移動だけの受付条件（P5 の <c>Phase5TrialLauncher</c> と同じ考え方）。
    /// <b>素通しにしてよいのは「まだどのエリアにも居ない」ときだけ。</b> 到着後は各エリアの条件へ差し替わる。
    /// </summary>
    public sealed class LauncherTravelConditions : IAreaTransitionConditions
    {
        public bool IsAreaReady => true;
        public GameMode Mode => GameMode.Exploration;
        public bool IsPlayerAlive => true;
        public bool IsPlayerBusy => false;
        public bool IsEncounterActive => false;
    }
}
