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
        public bool InitializeNewAdventure(string adventureId, ShrineInfo initialShrine, int kibidangoCapacity)
        {
            if (!string.IsNullOrEmpty(AdventureId) || string.IsNullOrEmpty(adventureId) || !initialShrine.IsValid)
            {
                return false;
            }

            AdventureId = adventureId;
            _registeredShrines.Add(initialShrine.ShrineId);
            Checkpoint = initialShrine.ShrineId;
            Resume = ResumeAnchor.AtShrine(initialShrine.AreaId, initialShrine.ShrineId);
            Kibidango = kibidangoCapacity < 0 ? 0 : kibidangoCapacity;
            _log.Touch("new_adventure", autosave: true);
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
