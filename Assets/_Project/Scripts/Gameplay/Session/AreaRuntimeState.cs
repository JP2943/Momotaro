using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Companion.Investigation;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 1 エリア分の世界状態（P5-01。仕様書 v1.1 §4.1／§4.3。P6A-01 で恒久記録と普通敵の周期記録を追加）。
    /// 純粋 C# で UnityEngine に依存しない。
    ///
    /// 持つのは「そのエリアで起きた変化」だけ：調査済み地点、開通済みの仕掛け、遭遇戦のクリア、
    /// ボス撃破、取得済み配置物、普通敵の撃破。Actor の HP・CD のような<b>活動中の値は持たない</b>
    /// （それは遷移 Snapshot と保存時の採取の担当。§4.4）。Transform・GameObject も保持しない（§8.1）。
    ///
    /// <b>周期で消えるのは 2 つだけ</b>（P6A-01）。
    /// <list type="bullet">
    /// <item><description>普通敵の撃破（配置 ID → 撃破した周期）。周期が進めば記録は一致しなくなる。</description></item>
    /// <item><description>P5 規則（<see cref="EncounterClearPolicy.PerRespawnCycle"/>）の campaign での遭遇戦クリア。</description></item>
    /// </list>
    /// 恒久クリア・ボス撃破・配置物・開通・調査は周期と独立で、New Game 以外で消えない（P6 仕様 §3）。
    /// </summary>
    public sealed class AreaRuntimeState
    {
        private readonly InvestigationRecord _investigation = new InvestigationRecord();
        private readonly RevisionedInvestigationSink _investigationSink;
        private readonly SessionChangeLog _log;

        // 開通済みの仕掛け。扉とレバーを別々に持つと不一致が起きるため、FlagId を正本にする（§4.3）。
        private readonly HashSet<StableId> _openedFlags = new HashSet<StableId>();

        // P5 規則のクリア記録。値は「どの再出現周期でクリアしたか」。周期が進めば自動的に無効になる。
        private readonly Dictionary<StableId, int> _clearedEncounters = new Dictionary<StableId, int>();

        // P6 規則（恒久）のクリア記録。
        private readonly HashSet<StableId> _permanentClears = new HashSet<StableId>();

        // 撃破済みの中ボス・章ボス（恒久）。
        private readonly HashSet<StableId> _bossDefeats = new HashSet<StableId>();

        // 取得済みの配置物・宝箱（恒久）。
        private readonly HashSet<StableId> _pickedPlacements = new HashSet<StableId>();

        // 普通敵の撃破（配置 ID → 撃破した周期）。敵種 ID ではなく<b>配置 ID</b>で持つ（仕様 §4）。
        private readonly Dictionary<StableId, int> _fieldDefeats = new Dictionary<StableId, int>();

        /// <summary>単体で使う（テスト・P5 の互換）。版は自分専用の記録が持つ。</summary>
        public AreaRuntimeState(StableId areaId)
            : this(areaId, new SessionChangeLog())
        {
        }

        internal AreaRuntimeState(StableId areaId, SessionChangeLog log)
        {
            AreaId = areaId;
            _log = log ?? new SessionChangeLog();
            _investigationSink = new RevisionedInvestigationSink(_investigation, _log);
        }

        /// <summary>このエリアの安定 ID。</summary>
        public StableId AreaId { get; }

        /// <summary>遭遇戦のクリア規則（campaign の Data から）。</summary>
        public EncounterClearPolicy EncounterPolicy => _log.EncounterPolicy;

        /// <summary>調査済み記録への狭い書込み口。<see cref="InvestigationRecordHolder"/> へ注入する。</summary>
        public IInvestigationRecordSink Investigation => _investigationSink;

        /// <summary>調査済み地点の数（診断・テスト用）。</summary>
        public int InvestigatedCount => _investigation.Count;

        /// <summary>開通済みの仕掛けの数（診断・テスト用）。</summary>
        public int OpenedFlagCount => _openedFlags.Count;

        /// <summary>
        /// 現在の周期でクリア済みとして記録されている Encounter の数（P5 規則。診断・テスト用）。
        /// P6 規則では <see cref="PermanentClearCount"/> を見る。
        /// </summary>
        public int ClearedEncounterRecordCount => _clearedEncounters.Count;

        /// <summary>恒久クリアの数（診断・テスト用）。</summary>
        public int PermanentClearCount => _permanentClears.Count;

        /// <summary>撃破済みボスの数（診断・テスト用）。</summary>
        public int BossDefeatCount => _bossDefeats.Count;

        /// <summary>取得済み配置物の数（診断・テスト用）。</summary>
        public int PickedPlacementCount => _pickedPlacements.Count;

        /// <summary>普通敵の撃破記録の数（周期を問わない。診断・テスト用）。</summary>
        public int FieldDefeatRecordCount => _fieldDefeats.Count;

        /// <summary>指定の仕掛けが開通済みか。門の Collider・表示はこの 1 つの値から復元する（§4.3）。</summary>
        public bool IsOpen(StableId flagId) => !flagId.IsEmpty && _openedFlags.Contains(flagId);

        /// <summary>
        /// 仕掛けを開通させる。<b>既に開通済みなら false</b>（開通は 1 回だけ。§7.3）。
        /// レバーはこの false を見て「開通済み」表示に留め、状態変更と開通通知を再発火しない。
        /// 開通はショートカットの恒久進行なので保存を要求する（P6 仕様 §8）。
        /// </summary>
        public bool TryOpen(StableId flagId)
        {
            if (flagId.IsEmpty || !_openedFlags.Add(flagId))
            {
                return false;
            }

            _log.Touch("flag_opened", autosave: true);
            return true;
        }

        /// <summary>
        /// 指定 Encounter がクリア済みか。
        /// P5 規則では<b>現在の再出現周期で</b>クリアしたものだけ（周期が進むと未クリア。§9.1）。
        /// P6 規則では周期と無関係に恒久。
        /// </summary>
        public bool IsEncounterCleared(StableId encounterId, int respawnCycle)
        {
            if (encounterId.IsEmpty)
            {
                return false;
            }

            if (_log.EncounterPolicy == EncounterClearPolicy.Permanent)
            {
                return _permanentClears.Contains(encounterId);
            }

            return _clearedEncounters.TryGetValue(encounterId, out int cycle) && cycle == respawnCycle;
        }

        /// <summary>
        /// 指定 Encounter をクリア済みとして記録する。既に記録済みなら false（勝利の二重記録を防ぐ。§8.4）。
        /// <b>報酬・開通と同時に確定したいときは <see cref="GameSessionState.CommitEncounterClear"/> を使う</b>（仕様 §4）。
        /// </summary>
        public bool TryMarkEncounterCleared(StableId encounterId, int respawnCycle)
        {
            if (encounterId.IsEmpty)
            {
                return false;
            }

            if (_log.EncounterPolicy == EncounterClearPolicy.Permanent)
            {
                if (!_permanentClears.Add(encounterId))
                {
                    return false;
                }

                _log.Touch("encounter_cleared", autosave: true);
                return true;
            }

            if (_clearedEncounters.TryGetValue(encounterId, out int cycle) && cycle == respawnCycle)
            {
                return false;
            }

            _clearedEncounters[encounterId] = respawnCycle;
            _log.Touch("encounter_cleared", autosave: true);
            return true;
        }

        /// <summary>撃破済みのボス対象か（恒久）。</summary>
        public bool IsBossDefeated(StableId bossId) => !bossId.IsEmpty && _bossDefeats.Contains(bossId);

        /// <summary>ボス対象の撃破を記録する（恒久）。既に記録済みなら false。</summary>
        public bool TryRecordBossDefeat(StableId bossId)
        {
            if (bossId.IsEmpty || !_bossDefeats.Add(bossId))
            {
                return false;
            }

            _log.Touch("boss_defeated", autosave: true);
            return true;
        }

        /// <summary>取得済みの配置物か（恒久）。</summary>
        public bool IsPlacementPicked(StableId placementId) =>
            !placementId.IsEmpty && _pickedPlacements.Contains(placementId);

        /// <summary>
        /// 配置物の取得を記録する（恒久）。<b>所持数と同時に確定する</b>のは
        /// <see cref="GameSessionState.TryPickPlacement"/> の責任で、ここは記録だけ。
        /// </summary>
        internal bool TryMarkPlacementPicked(StableId placementId)
        {
            if (placementId.IsEmpty || !_pickedPlacements.Add(placementId))
            {
                return false;
            }

            _log.Touch("placement_picked", autosave: true);
            return true;
        }

        /// <summary>
        /// 普通敵（配置 ID）が<b>現在の周期で</b>撃破済みか。周期が進めば未撃破（＝復活）として扱う。
        /// </summary>
        public bool IsFieldEnemyDefeated(StableId placementId, int respawnCycle)
        {
            return !placementId.IsEmpty
                && _fieldDefeats.TryGetValue(placementId, out int cycle)
                && cycle == respawnCycle;
        }

        /// <summary>
        /// 普通敵の撃破を現在の周期で記録する。同じ周期で記録済みなら false（重複通知を 1 回分にする）。
        /// 報酬と同時に確定するのは <see cref="GameSessionState.TryRecordFieldDefeat"/> の責任。
        /// </summary>
        internal bool TryMarkFieldEnemyDefeated(StableId placementId, int respawnCycle)
        {
            if (placementId.IsEmpty)
            {
                return false;
            }

            if (_fieldDefeats.TryGetValue(placementId, out int cycle) && cycle == respawnCycle)
            {
                return false;
            }

            _fieldDefeats[placementId] = respawnCycle;
            _log.Touch("field_enemy_defeated", autosave: true);
            return true;
        }

        /// <summary>
        /// P5 規則の通常 Encounter クリア記録<b>だけ</b>を初期化する（本編型死亡再開。§9.1）。
        /// 調査済み・開通・恒久クリア・ボス・配置物には触れない。普通敵の撃破は周期で無効になるので消さない。
        /// 呼び出し元は <see cref="GameSessionState.AdvanceRespawnCycle"/>。
        /// </summary>
        internal void ClearNormalEncounterRecords()
        {
            _clearedEncounters.Clear();
        }

        /// <summary>周期が進んだので、もう一致しない普通敵の撃破記録を捨てる（保存を小さく保つため）。</summary>
        internal void PruneFieldDefeats(int currentCycle)
        {
            if (_fieldDefeats.Count == 0)
            {
                return;
            }

            List<StableId> stale = null;
            foreach (KeyValuePair<StableId, int> pair in _fieldDefeats)
            {
                if (pair.Value != currentCycle)
                {
                    (stale ??= new List<StableId>()).Add(pair.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            for (int i = 0; i < stale.Count; i++)
            {
                _fieldDefeats.Remove(stale[i]);
            }
        }

        // ---- 保存（P6A-02）----

        /// <summary>保存用に、この Area の記録を値で写す（呼び出し側が並べ替える）。</summary>
        internal void CopyTo(AreaRecordCopy copy)
        {
            copy.Clear();
            copy.AreaId = AreaId.Value;
            CopyIds(_investigation.CopyIds(), copy.Investigated);
            CopyIds(_openedFlags, copy.OpenedFlags);
            CopyIds(_permanentClears, copy.ClearedEncounters);
            CopyIds(_bossDefeats, copy.DefeatedBosses);
            CopyIds(_pickedPlacements, copy.PickedPlacements);
            foreach (KeyValuePair<StableId, int> pair in _fieldDefeats)
            {
                copy.FieldDefeats.Add(new KeyValuePair<string, int>(pair.Key.Value, pair.Value));
            }
        }

        /// <summary>
        /// 保存から記録を置く（候補 Session の構築だけが使う）。検証は呼び出し側（Save の検証器）が済ませている。
        /// 版は進めない・保存も要求しない。
        /// </summary>
        internal void RestoreFrom(AreaRecordCopy copy)
        {
            _investigation.Clear();
            for (int i = 0; i < copy.Investigated.Count; i++)
            {
                _investigation.TryMarkInvestigated(new StableId(copy.Investigated[i]));
            }

            Fill(_openedFlags, copy.OpenedFlags);
            Fill(_permanentClears, copy.ClearedEncounters);
            Fill(_bossDefeats, copy.DefeatedBosses);
            Fill(_pickedPlacements, copy.PickedPlacements);
            _clearedEncounters.Clear();
            _fieldDefeats.Clear();
            for (int i = 0; i < copy.FieldDefeats.Count; i++)
            {
                _fieldDefeats[new StableId(copy.FieldDefeats[i].Key)] = copy.FieldDefeats[i].Value;
            }
        }

        private static void CopyIds(IEnumerable<StableId> source, List<string> target)
        {
            foreach (StableId id in source)
            {
                target.Add(id.Value);
            }
        }

        private static void Fill(HashSet<StableId> target, List<string> source)
        {
            target.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                target.Add(new StableId(source[i]));
            }
        }

        /// <summary>
        /// 調査記録への書込みで版を進める窓口。調査の完了は恒久進行なので保存も要求する（仕様 §8）。
        /// </summary>
        private sealed class RevisionedInvestigationSink : IInvestigationRecordSink
        {
            private readonly InvestigationRecord _inner;
            private readonly SessionChangeLog _log;

            public RevisionedInvestigationSink(InvestigationRecord inner, SessionChangeLog log)
            {
                _inner = inner;
                _log = log;
            }

            public int Count => _inner.Count;

            public bool IsInvestigated(StableId pointId) => _inner.IsInvestigated(pointId);

            public bool TryMarkInvestigated(StableId pointId)
            {
                if (!_inner.TryMarkInvestigated(pointId))
                {
                    return false;
                }

                _log.Touch("investigated", autosave: true);
                return true;
            }
        }
    }

    /// <summary>
    /// 1 Area 分の記録の値コピー（保存用。P6A-02）。Unity の型を含まない。
    /// </summary>
    public sealed class AreaRecordCopy
    {
        public string AreaId = string.Empty;
        public readonly List<string> Investigated = new List<string>();
        public readonly List<string> OpenedFlags = new List<string>();
        public readonly List<string> ClearedEncounters = new List<string>();
        public readonly List<string> DefeatedBosses = new List<string>();
        public readonly List<string> PickedPlacements = new List<string>();
        public readonly List<KeyValuePair<string, int>> FieldDefeats = new List<KeyValuePair<string, int>>();

        public void Clear()
        {
            AreaId = string.Empty;
            Investigated.Clear();
            OpenedFlags.Clear();
            ClearedEncounters.Clear();
            DefeatedBosses.Clear();
            PickedPlacements.Clear();
            FieldDefeats.Clear();
        }
    }
}
