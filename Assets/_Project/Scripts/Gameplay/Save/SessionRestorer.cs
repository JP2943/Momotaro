using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Session;

namespace Momotaro.Gameplay.Save
{
    /// <summary>
    /// 検証済みの保存から<b>候補 Session</b> を作る（P6A-02。仕様 §10）。
    ///
    /// 作るのは新しい <see cref="GameSessionState"/> で、提供点へはまだ差さない。採用は Load の準備（Area の配置・
    /// Actor の復元・門と撃破の同期）が全部成功したあと、所有者（<c>GameSessionBootService</c>）が行う。
    ///
    /// <b>Load は何も「起こさない」。</b> 回復・周期更新・到着報酬・獲得演出・保存要求を出さない——
    /// 内部の復元口（通知しない・版を進めない）だけを使う。
    /// </summary>
    public static class SessionRestorer
    {
        /// <summary>候補を作る。検証（<see cref="SaveSnapshotValidator"/>）が先に通っていること。</summary>
        public static bool TryBuildCandidate(SaveSnapshot snapshot, AreaCatalog catalog,
            out GameSessionState candidate, out string error)
        {
            candidate = null;
            var errors = new List<string>();
            if (!SaveSnapshotValidator.Validate(snapshot, catalog, errors))
            {
                error = string.Join("\n", errors);
                return false;
            }

            var session = new GameSessionState(EncounterClearPolicy.Permanent);

            var growth = new List<KeyValuePair<string, int>>(snapshot.Growth);
            var granted = new List<string>(snapshot.GrantedRewards);
            // P6B 02：版 1・2 は払い戻しの欄を持たない。明示的な移行として権利＝初期値・章集合＝空で補う
            // （P6A には本番の章報酬が無いので遡及追加しない）。版 3 以降は保存の値をそのまま使う（Load で初期化しない）。
            int rights = snapshot.HasRefundData ? snapshot.RefundRights : catalog.Campaign.RefundRightsInitial;
            var chapters = new List<string>(snapshot.ProcessedChapters);
            if (!session.Progress.TryRestore(snapshot.TotalVirtue, snapshot.SpentVirtue, granted, growth, rights,
                    chapters, out error))
            {
                return false;
            }

            var copy = new AreaRecordCopy();
            foreach (AreaSaveRecord record in snapshot.Areas)
            {
                copy.Clear();
                copy.AreaId = record.AreaId;
                copy.Investigated.AddRange(record.Investigated);
                copy.OpenedFlags.AddRange(record.OpenedFlags);
                copy.ClearedEncounters.AddRange(record.ClearedEncounters);
                copy.DefeatedBosses.AddRange(record.DefeatedBosses);
                copy.PickedPlacements.AddRange(record.PickedPlacements);
                copy.FieldDefeats.AddRange(record.FieldDefeats);
                session.GetOrCreateArea(new StableId(record.AreaId)).RestoreFrom(copy);
            }

            session.Inventory.RestoreFrom(new List<KeyValuePair<string, int>>(snapshot.Inventory));
            session.RestoreVisitsAndRecruits(Ids(snapshot.VisitedAreas), Ids(snapshot.Recruited));
            session.RestoreQuestStages(snapshot.QuestStages);
            session.RestoreAdventure(
                snapshot.AdventureId, Ids(snapshot.RegisteredShrines), new StableId(snapshot.Checkpoint),
                ResumeAnchor.From(snapshot.ResumeKind, new StableId(snapshot.ResumeAreaId), new StableId(snapshot.ResumePointId)),
                snapshot.Kibidango, snapshot.RespawnCycle, snapshot.Revision);

            candidate = session;
            error = null;
            return true;
        }

        private static IEnumerable<StableId> Ids(IReadOnlyList<string> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                yield return new StableId(values[i]);
            }
        }
    }
}
