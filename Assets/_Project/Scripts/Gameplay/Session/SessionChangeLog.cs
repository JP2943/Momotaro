using System;
using Momotaro.Data.World;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 保存要求 1 件（P6A-01。仕様 §8／§9）。<b>何を書け</b>ではなく「この版まで進んだので保存してほしい」という合図。
    /// 中身は書込担当がメインスレッドで Session から採り直す（要求が古い値を運ばない）。
    /// </summary>
    public readonly struct AutosaveRequest
    {
        public AutosaveRequest(string reason, long revision)
        {
            Reason = reason ?? string.Empty;
            Revision = revision;
        }

        /// <summary>契機（診断・記録用）。</summary>
        public string Reason { get; }

        /// <summary>要求した時点の世界の版。</summary>
        public long Revision { get; }
    }

    /// <summary>
    /// Session の<b>世界の版</b>と保存要求の窓口（P6A-01。P6A-00 の判断 3・4）。純粋 C#。
    ///
    /// <b>版は論理進行が変わるたびに進む。</b> 保存は「どの版を書いたか」を持ち、
    /// 書込中に新しい変化が来たとき、古い版の完了で dirty を消さない（仕様 §9）。
    ///
    /// <b>まとめ（<see cref="BeginBatch"/>）。</b> 遭遇戦の最終撃破は「クリア記録・初回ボーナス・開通」を
    /// 1 つの整合した更新として確定する（仕様 §4）。途中で保存要求を出すと、書込担当が
    /// 「クリアだけ記録してボーナスが無い」瞬間の Snapshot を採りうる。まとめの間は要求を溜め、
    /// 最後に 1 件だけ出す。
    /// </summary>
    public sealed class SessionChangeLog
    {
        private int _batchDepth;
        private bool _batchAutosave;
        private string _batchReason;

        public SessionChangeLog(EncounterClearPolicy encounterPolicy = EncounterClearPolicy.PerRespawnCycle)
        {
            EncounterPolicy = encounterPolicy;
        }

        /// <summary>遭遇戦のクリア規則（campaign の Data から。Session の生存中は変えない）。</summary>
        public EncounterClearPolicy EncounterPolicy { get; }

        /// <summary>世界の版。論理進行が変わるたびに 1 進む。</summary>
        public long Revision { get; private set; }

        /// <summary>保存要求を出した回数（診断・テスト用）。</summary>
        public int AutosaveRequestCount { get; private set; }

        /// <summary>まとめの最中か。</summary>
        public bool InBatch => _batchDepth > 0;

        /// <summary>
        /// 保存要求。<b>購読者はこの中で Snapshot を採ってよい</b>——呼ばれるのは更新を終えたあと（まとめの外）だけ。
        /// </summary>
        public event Action<AutosaveRequest> AutosaveRequested;

        /// <summary>
        /// 変化を記録する。<paramref name="autosave"/> なら保存要求も出す（まとめの間は溜める）。
        /// </summary>
        public void Touch(string reason, bool autosave)
        {
            Revision++;
            if (_batchDepth > 0)
            {
                if (autosave)
                {
                    _batchAutosave = true;
                    _batchReason ??= reason;
                }

                return;
            }

            if (autosave)
            {
                Raise(reason);
            }
        }

        /// <summary>
        /// 保存要求だけを出す（版は進めない）。正常終了の前など「変化は無いが最新を書きたい」とき。
        /// </summary>
        public void RequestAutosave(string reason)
        {
            if (_batchDepth > 0)
            {
                _batchAutosave = true;
                _batchReason ??= reason;
                return;
            }

            Raise(reason);
        }

        /// <summary>まとめを始める。<see cref="EndBatch"/> と必ず対にする（入れ子可）。</summary>
        public void BeginBatch(string reason)
        {
            if (_batchDepth == 0)
            {
                _batchAutosave = false;
                _batchReason = reason;
            }

            _batchDepth++;
        }

        /// <summary>まとめを終える。最外のときだけ、溜めた保存要求を 1 件出す。</summary>
        public void EndBatch()
        {
            if (_batchDepth == 0)
            {
                return;
            }

            _batchDepth--;
            if (_batchDepth > 0)
            {
                return;
            }

            if (_batchAutosave)
            {
                _batchAutosave = false;
                Raise(_batchReason);
            }

            _batchReason = null;
        }

        /// <summary>Load の復元で版を合わせる（候補 Session の構築だけが使う）。</summary>
        internal void RestoreRevision(long revision)
        {
            Revision = revision < 0 ? 0 : revision;
        }

        private void Raise(string reason)
        {
            AutosaveRequestCount++;
            AutosaveRequested?.Invoke(new AutosaveRequest(reason, Revision));
        }
    }
}
