using System;
using System.Collections.Generic;
using System.Diagnostics;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;

namespace Momotaro.Infrastructure.Save
{
    /// <summary>保存の状態（表示用）。</summary>
    public enum SaveStatus
    {
        /// <summary>保存対象の冒険が無い。</summary>
        Inactive = 0,

        /// <summary>最新まで保存済み。</summary>
        Saved = 1,

        /// <summary>未保存の変化がある（採取待ち・書込待ち）。</summary>
        Dirty = 2,

        /// <summary>書込中。</summary>
        Writing = 3,

        /// <summary>直近の書込に失敗した。<b>保存できていない</b>。</summary>
        Failed = 4,
    }

    /// <summary>
    /// オートセーブの調停（P6A-02／05。仕様 §8・§9）。Unity に依存しない（時計・採取口・可否は注入）ので EditMode で決定的に検証できる。
    ///
    /// <b>要求は捨てない。</b> 書込中に来た要求は印を付けて残し、完了後に<b>その時点の最新</b>を採り直して出す
    /// （古い Snapshot を後から書かない）。保存済みの版は「書けた Snapshot の版」だけで進み、
    /// <b>古い版の完了で新しい dirty を消さない</b>（受入 P6A 18）。
    ///
    /// <b>採るのはメインスレッドで、区切りでだけ。</b> 要求を受けた瞬間には採らない——報酬の付与中・命中の解決中に
    /// 呼ばれることがあるため。<see cref="Pump"/>（LateUpdate）で、遷移・死亡が落ち着いているときに採る。
    ///
    /// <b>失敗は巻き戻さない。</b> Runtime の獲得済み進行はそのまま、dirty を残し、前の有効ファイルを残し、
    /// 「保存できていない」を出す。毎フレームの再試行はしない——明示の再試行か、次の保存契機で 1 回ずつ（仕様 §9）。
    /// </summary>
    public sealed class SaveCoordinator : IDisposable
    {
        private readonly SaveFileStore _store;
        private readonly ISaveExecutor _executor;
        private readonly Func<DateTime> _clock;
        private readonly List<double> _captureMs = new List<double>();
        private readonly List<double> _writeMs = new List<double>();

        private GameSessionState _session;
        private CampaignCatalog _campaign;
        private long _epoch;
        private bool _pending;
        private bool _retryArmed;
        private string _pendingReason = string.Empty;
        private long _inFlightRevision = -1;

        public SaveCoordinator(SaveFileStore store, ISaveExecutor executor, Func<DateTime> clock = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>採取してよいか（遷移・死亡の解決中でない等）。ホストが注入する。null なら常に可。</summary>
        public Func<bool> CanCapture { get; set; }

        /// <summary>活動中の Area の Actor 採取口。ホストが注入する。null または採れないなら採取を見送る。</summary>
        public Func<ISaveActorSource> ActorSource { get; set; }

        /// <summary>保存先。</summary>
        public SaveFileStore Store => _store;

        /// <summary>いまの状態。</summary>
        public SaveStatus Status { get; private set; } = SaveStatus.Inactive;

        /// <summary>直近の失敗理由（成功なら空）。</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>保存済みの版（ファイルに書けた Snapshot の版）。-1 は未保存。</summary>
        public long SavedRevision { get; private set; } = -1;

        /// <summary>書込中の Snapshot の版（無ければ -1。診断・テスト用）。</summary>
        public long InFlightRevision => _inFlightRevision;

        /// <summary>直近に書けた世代。</summary>
        public long LastGeneration { get; private set; }

        /// <summary>書込が成功した回数。</summary>
        public int SuccessCount { get; private set; }

        /// <summary>書込が失敗した回数。</summary>
        public int FailureCount { get; private set; }

        /// <summary>要求を受けた回数。</summary>
        public int RequestCount { get; private set; }

        /// <summary>採取して書込へ出した回数。</summary>
        public int SubmitCount { get; private set; }

        /// <summary>古い Session の完了を捨てた回数（診断・テスト用）。</summary>
        public int StaleCompletionCount { get; private set; }

        /// <summary>採取を見送った回数（遷移中・死亡中・採取口なし。診断用）。</summary>
        public int DeferredCaptureCount { get; private set; }

        /// <summary>メインスレッドでの採取時間（ミリ秒）の記録（性能計測。仕様 §11）。</summary>
        public IReadOnlyList<double> CaptureMilliseconds => _captureMs;

        /// <summary>書込担当での JSON 化と I/O の時間（ミリ秒）の記録。</summary>
        public IReadOnlyList<double> WriteMilliseconds => _writeMs;

        /// <summary>直近に書いたファイルの大きさ（バイト）。</summary>
        public int LastBytes { get; private set; }

        /// <summary>結び付いている Session。</summary>
        public GameSessionState Session => _session;

        /// <summary>未保存の変化があるか（要求の保留・書込中・版の差のどれか）。</summary>
        public bool IsDirty =>
            _session != null && (_pending || IsWriting || _session.Changes.Revision > SavedRevision);

        /// <summary>書込中か。</summary>
        /// <remarks>
        /// <b>完了を取り込むまでは書込中</b>として扱う。書込担当が手を離した瞬間（<c>IsBusy</c> が false）から
        /// 次の <see cref="Pump"/> が結果を取り込むまでのあいだ、成否はまだ誰も知らない。ここを「書込中でない」と読むと、
        /// 版の変わらない保存（終了前の保存など）が<b>失敗していても「保存済み」</b>に見える（P6A の実 Scene テストで発覚）。
        /// </remarks>
        public bool IsWriting => _executor.IsBusy || _awaitingCompletion;

        private bool _awaitingCompletion;

        /// <summary>状態が変わった（表示が購読する）。</summary>
        public event Action<SaveStatus> StatusChanged;

        /// <summary>
        /// Session に結び付く（New Game・Load の採用のたび）。<b>世代（epoch）を進める</b>——
        /// 前の Session の書込が後から終わっても、新しいゲームの「保存済み」を書き換えない（仕様 §9）。
        /// </summary>
        /// <param name="session">対象（null で解除）。</param>
        /// <param name="campaign">campaign の定義。</param>
        /// <param name="alreadySavedRevision">この版までファイルにある（Load 直後）。無ければ -1。</param>
        public void Bind(GameSessionState session, CampaignCatalog campaign, long alreadySavedRevision)
        {
            if (_session != null)
            {
                _session.Changes.AutosaveRequested -= OnAutosaveRequested;
            }

            _epoch++;
            _session = session;
            _campaign = campaign;
            _pending = false;
            _retryArmed = false;
            _inFlightRevision = -1;
            _awaitingCompletion = false;
            SavedRevision = alreadySavedRevision;
            LastError = string.Empty;

            if (_session != null)
            {
                _session.Changes.AutosaveRequested += OnAutosaveRequested;
            }

            SetStatus(_session == null ? SaveStatus.Inactive : (IsDirty ? SaveStatus.Dirty : SaveStatus.Saved));
        }

        /// <summary>保存を要求する（契機の名前つき）。採取は <see cref="Pump"/> で行う。</summary>
        public void Request(string reason)
        {
            if (_session == null)
            {
                return;
            }

            RequestCount++;
            _pending = true;
            _pendingReason = reason ?? string.Empty;
            if (Status != SaveStatus.Writing && Status != SaveStatus.Failed)
            {
                SetStatus(SaveStatus.Dirty);
            }
        }

        /// <summary>
        /// 失敗のあとに、明示的にもう一度試す（仕様 §9）。<b>最新 Runtime から採り直す</b>——古い失敗 Snapshot を再送しない。
        /// </summary>
        public void RetryNow()
        {
            if (_session == null)
            {
                return;
            }

            _retryArmed = true;
            Request("retry");
        }

        /// <summary>
        /// メインスレッドの区切り（LateUpdate）で呼ぶ。完了を取り込み、保留があり・書込担当が空き・採取してよければ、
        /// 最新を採って出す。
        /// </summary>
        public void Pump()
        {
            while (_executor.TryTakeCompletion(out SaveWriteResult result))
            {
                OnCompleted(result);
            }

            if (!_pending || _session == null || _campaign == null || _executor.IsBusy)
            {
                return;
            }

            // 失敗の直後は、次の契機（新しい要求）か明示の再試行まで出さない（毎フレーム再試行しない）。
            if (Status == SaveStatus.Failed && !_retryArmed)
            {
                return;
            }

            if (string.IsNullOrEmpty(_session.AdventureId))
            {
                return; // New Game の初期化前。
            }

            if (CanCapture != null && !CanCapture())
            {
                DeferredCaptureCount++;
                return;
            }

            ISaveActorSource actors = ActorSource?.Invoke();
            if (actors == null || !actors.CanExportForSave)
            {
                // 主人公が居ない・死亡中は採らない（HP 0 の通常保存を作らない。仕様 §8）。
                DeferredCaptureCount++;
                return;
            }

            Stopwatch watch = Stopwatch.StartNew();
            PartySaveValues party = actors.ExportForSave();
            SaveSnapshot snapshot = SaveSnapshot.Capture(_session, _campaign, party);
            watch.Stop();
            _captureMs.Add(watch.Elapsed.TotalMilliseconds);

            _pending = false;
            _retryArmed = false;
            _inFlightRevision = snapshot.Revision;
            long epoch = _epoch;
            string reason = _pendingReason;
            DateTime now = _clock();
            SaveFileStore store = _store;

            bool started = _executor.TryRun(() => Write(store, snapshot, epoch, now, reason));
            if (!started)
            {
                _pending = true; // 取りこぼさない。
                return;
            }

            SubmitCount++;
            _awaitingCompletion = true;
            SetStatus(SaveStatus.Writing);
        }

        /// <summary>書込担当スレッドで走る中身（JSON 化と I/O）。</summary>
        private static SaveWriteResult Write(SaveFileStore store, SaveSnapshot snapshot, long epoch, DateTime now, string reason)
        {
            Stopwatch watch = Stopwatch.StartNew();
            long generation = store.NextGeneration(out string target);
            string json = SaveJsonCodec.Serialize(snapshot, generation, now);
            bool ok = store.TryWrite(json, generation, target, out string error);
            watch.Stop();
            return new SaveWriteResult(ok, epoch, snapshot.Revision, ok ? generation : 0, error,
                watch.Elapsed.TotalMilliseconds, json.Length, reason);
        }

        private void OnCompleted(SaveWriteResult result)
        {
            // 書込担当は 1 本なので、取り込んだ完了は必ず「いま待っている 1 件」か、Bind 前の古い 1 件。
            _awaitingCompletion = false;
            if (result.Epoch != _epoch)
            {
                // 前の Session（New Game／Load 前）の書込。新しいゲームへ適用しない。
                StaleCompletionCount++;
                return;
            }

            _inFlightRevision = -1;
            _writeMs.Add(result.WriteMilliseconds);
            if (result.Success)
            {
                SuccessCount++;
                LastBytes = result.Bytes;
                LastGeneration = result.Generation;
                LastError = string.Empty;

                // 古い版の完了で新しい dirty を消さない：保存済みは「書けた版」まで。
                if (result.Revision > SavedRevision)
                {
                    SavedRevision = result.Revision;
                }

                SetStatus(IsDirty ? SaveStatus.Dirty : SaveStatus.Saved);
                return;
            }

            FailureCount++;
            LastError = result.Error;
            SetStatus(SaveStatus.Failed);
        }

        private void OnAutosaveRequested(AutosaveRequest request)
        {
            // 失敗中でも新しい契機は 1 回の再試行として扱う（仕様 §9「次の保存契機で一回ずつ」）。
            if (Status == SaveStatus.Failed)
            {
                _retryArmed = true;
            }

            Request(request.Reason);
        }

        private void SetStatus(SaveStatus status)
        {
            if (Status == status)
            {
                return;
            }

            Status = status;
            StatusChanged?.Invoke(status);
        }

        public void Dispose()
        {
            if (_session != null)
            {
                _session.Changes.AutosaveRequested -= OnAutosaveRequested;
                _session = null;
            }

            _executor.Dispose();
            _store.ReleaseLock();
        }
    }
}
