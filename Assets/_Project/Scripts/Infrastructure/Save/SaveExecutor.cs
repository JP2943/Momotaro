using System;
using System.Collections.Generic;
using System.Threading;

namespace Momotaro.Infrastructure.Save
{
    /// <summary>書込 1 件の結果（書込担当 → メインスレッド）。</summary>
    public readonly struct SaveWriteResult
    {
        public SaveWriteResult(bool success, long epoch, long revision, long generation, string error,
            double writeMilliseconds, int bytes, string reason)
        {
            Success = success;
            Epoch = epoch;
            Revision = revision;
            Generation = generation;
            Error = error ?? string.Empty;
            WriteMilliseconds = writeMilliseconds;
            Bytes = bytes;
            Reason = reason ?? string.Empty;
        }

        public bool Success { get; }

        /// <summary>どの Session（New Game／Load ごとに変わる）の書込か。古い Session の完了を新しいゲームへ適用しない。</summary>
        public long Epoch { get; }

        /// <summary>書いた Snapshot の世界の版。</summary>
        public long Revision { get; }

        public long Generation { get; }
        public string Error { get; }

        /// <summary>JSON 化と I/O にかかった時間（書込担当スレッド上）。</summary>
        public double WriteMilliseconds { get; }

        public int Bytes { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 書込担当（P6A-02。仕様 §9「JSON 化と I/O は単一の書込担当で直列実行」）。
    /// 1 度に 1 件だけ走らせる。<b>集約は呼び出し側（<see cref="CampaignSaveService"/>）が行う</b>——
    /// 走っている間に来た要求は捨てずに印を付け、完了後に<b>その時点の最新</b>を採り直して出す。
    /// </summary>
    public interface ISaveExecutor : IDisposable
    {
        /// <summary>走っている書込があるか。</summary>
        bool IsBusy { get; }

        /// <summary>書込を始める（走っている間は false で何もしない）。</summary>
        bool TryRun(Func<SaveWriteResult> work);

        /// <summary>終わった書込の結果を取り出す（メインスレッドから呼ぶ）。</summary>
        bool TryTakeCompletion(out SaveWriteResult result);
    }

    /// <summary>専用スレッドで走らせる書込担当（出荷・PlayMode）。</summary>
    public sealed class ThreadSaveExecutor : ISaveExecutor
    {
        private readonly object _gate = new object();
        private readonly Queue<SaveWriteResult> _completions = new Queue<SaveWriteResult>();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly Thread _thread;
        private Func<SaveWriteResult> _work;
        private bool _busy;
        private volatile bool _disposed;

        public ThreadSaveExecutor()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Momotaro.SaveWriter" };
            _thread.Start();
        }

        public bool IsBusy
        {
            get
            {
                lock (_gate)
                {
                    return _busy;
                }
            }
        }

        public bool TryRun(Func<SaveWriteResult> work)
        {
            lock (_gate)
            {
                if (_busy || _disposed || work == null)
                {
                    return false;
                }

                _busy = true;
                _work = work;
            }

            _signal.Set();
            return true;
        }

        public bool TryTakeCompletion(out SaveWriteResult result)
        {
            lock (_gate)
            {
                if (_completions.Count == 0)
                {
                    result = default;
                    return false;
                }

                result = _completions.Dequeue();
                return true;
            }
        }

        private void Loop()
        {
            while (!_disposed)
            {
                _signal.WaitOne();
                if (_disposed)
                {
                    return;
                }

                Func<SaveWriteResult> work;
                lock (_gate)
                {
                    work = _work;
                    _work = null;
                }

                if (work == null)
                {
                    continue;
                }

                SaveWriteResult result;
                try
                {
                    result = work();
                }
                catch (Exception e)
                {
                    result = new SaveWriteResult(false, 0, 0, 0, "書込担当で例外: " + e.Message, 0, 0, null);
                }

                lock (_gate)
                {
                    _completions.Enqueue(result);
                    _busy = false;
                }
            }
        }

        /// <summary>走っている書込が終わるまで待つ（終了時。<paramref name="timeoutMs"/> で打ち切り）。</summary>
        public bool WaitIdle(int timeoutMs)
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (IsBusy && DateTime.UtcNow < limit)
            {
                Thread.Sleep(5);
            }

            return !IsBusy;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            WaitIdle(5000);
            _disposed = true;
            _signal.Set();
        }
    }

    /// <summary>
    /// 手で回す書込担当（テスト用）。<see cref="RunPending"/> を呼ぶまで書込は終わらない——
    /// 「書込中に新しい要求が来る」「古い版の完了」を<b>遅延の度合いに頼らず</b>作れる（P5.5 の教訓「終わらせない」）。
    /// </summary>
    public sealed class ManualSaveExecutor : ISaveExecutor
    {
        private readonly Queue<SaveWriteResult> _completions = new Queue<SaveWriteResult>();
        private Func<SaveWriteResult> _work;

        public bool IsBusy => _work != null;

        /// <summary>走らせた回数。</summary>
        public int RunCount { get; private set; }

        public bool TryRun(Func<SaveWriteResult> work)
        {
            if (_work != null || work == null)
            {
                return false;
            }

            _work = work;
            return true;
        }

        /// <summary>保留中の書込を今のスレッドで走らせ、完了を積む。</summary>
        public bool RunPending()
        {
            if (_work == null)
            {
                return false;
            }

            Func<SaveWriteResult> work = _work;
            SaveWriteResult result = work();
            _work = null;
            RunCount++;
            _completions.Enqueue(result);
            return true;
        }

        public bool TryTakeCompletion(out SaveWriteResult result)
        {
            if (_completions.Count == 0)
            {
                result = default;
                return false;
            }

            result = _completions.Dequeue();
            return true;
        }

        public void Dispose()
        {
        }
    }
}
