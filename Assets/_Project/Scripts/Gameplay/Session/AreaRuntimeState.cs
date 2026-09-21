using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 1 エリア分の世界状態（P5-01。仕様書 v1.1 §4.1／§4.3）。純粋 C# で UnityEngine に依存しない。
    ///
    /// 持つのは「そのエリアで起きた恒久的な変化」だけ：調査済み地点、開通済みの仕掛け、
    /// 通常 Encounter のクリア記録。Actor の HP・CD のような<b>活動中の値は持たない</b>
    /// （それは遷移 Snapshot の担当。§4.4）。Transform・GameObject も保持しない（§8.1）。
    ///
    /// 生存期間は Session と同じ。通常の A↔B 往復では全項目を保持し、本編型死亡再開では
    /// <b>通常 Encounter のクリア記録だけ</b>が初期化される（§4.1 の表）。
    /// </summary>
    public sealed class AreaRuntimeState
    {
        private readonly InvestigationRecord _investigation = new InvestigationRecord();

        // 開通済みの仕掛け。扉とレバーを別々に持つと不一致が起きるため、FlagId を正本にする（§4.3）。
        private readonly HashSet<StableId> _openedFlags = new HashSet<StableId>();

        // 通常 Encounter のクリア記録。値は「どの再出現周期でクリアしたか」。周期が進めば自動的に無効になる。
        private readonly Dictionary<StableId, int> _clearedEncounters = new Dictionary<StableId, int>();

        public AreaRuntimeState(StableId areaId)
        {
            AreaId = areaId;
        }

        /// <summary>このエリアの安定 ID。</summary>
        public StableId AreaId { get; }

        /// <summary>調査済み記録への狭い書込み口。<see cref="InvestigationRecordHolder"/> へ注入する。</summary>
        public IInvestigationRecordSink Investigation => _investigation;

        /// <summary>調査済み地点の数（診断・テスト用）。</summary>
        public int InvestigatedCount => _investigation.Count;

        /// <summary>開通済みの仕掛けの数（診断・テスト用）。</summary>
        public int OpenedFlagCount => _openedFlags.Count;

        /// <summary>現在の周期でクリア済みとして記録されている Encounter の数（診断・テスト用）。</summary>
        public int ClearedEncounterRecordCount => _clearedEncounters.Count;

        /// <summary>指定の仕掛けが開通済みか。門の Collider・表示はこの 1 つの値から復元する（§4.3）。</summary>
        public bool IsOpen(StableId flagId) => !flagId.IsEmpty && _openedFlags.Contains(flagId);

        /// <summary>
        /// 仕掛けを開通させる。<b>既に開通済みなら false</b>（開通は 1 回だけ。§7.3）。
        /// レバーはこの false を見て「開通済み」表示に留め、状態変更と開通通知を再発火しない。
        /// </summary>
        public bool TryOpen(StableId flagId)
        {
            return !flagId.IsEmpty && _openedFlags.Add(flagId);
        }

        /// <summary>
        /// 指定 Encounter が<b>現在の再出現周期で</b>クリア済みか。
        /// 周期が進むと過去の記録は一致しなくなるため、死亡再開後は未クリアとして扱われる（§9.1）。
        /// </summary>
        public bool IsEncounterCleared(StableId encounterId, int respawnCycle)
        {
            return !encounterId.IsEmpty
                && _clearedEncounters.TryGetValue(encounterId, out int cycle)
                && cycle == respawnCycle;
        }

        /// <summary>
        /// 指定 Encounter を現在の周期でクリア済みとして記録する。
        /// 同じ周期で既に記録済みなら false（勝利の二重記録を防ぐ。§8.4）。
        /// </summary>
        public bool TryMarkEncounterCleared(StableId encounterId, int respawnCycle)
        {
            if (encounterId.IsEmpty)
            {
                return false;
            }

            if (_clearedEncounters.TryGetValue(encounterId, out int cycle) && cycle == respawnCycle)
            {
                return false;
            }

            _clearedEncounters[encounterId] = respawnCycle;
            return true;
        }

        /// <summary>
        /// 通常 Encounter のクリア記録<b>だけ</b>を初期化する（本編型死亡再開。§9.1）。
        /// 調査済み・開通・その他の恒久進行には触れない。将来のボス撃破・一度限りイベント完了を
        /// この初期化へ混ぜないこと（§9.1 末尾）。呼び出し元は <see cref="GameSessionState.AdvanceRespawnCycle"/>。
        /// </summary>
        internal void ClearNormalEncounterRecords()
        {
            _clearedEncounters.Clear();
        }
    }
}
