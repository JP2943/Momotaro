using System;
using System.IO;
using System.Text;

namespace Momotaro.Infrastructure.Save
{
    /// <summary>
    /// 保存先のファイル操作（P6A-02）。<b>故障注入のための継ぎ目</b>——テストは書込・flush・検証・置換の各段で
    /// 失敗させた実装を差す（受入 P6A 19）。既定は実ファイル。
    /// </summary>
    public interface ISaveFileSystem
    {
        bool Exists(string path);
        string ReadAllText(string path);

        /// <summary>書いて OS のバッファまで flush する（<c>Flush(true)</c>）。</summary>
        void WriteAllTextDurable(string path, string text);

        /// <summary><paramref name="source"/> で <paramref name="destination"/> を置き換える（無ければ移動）。</summary>
        void Replace(string source, string destination);

        void Move(string source, string destination);
        void Delete(string path);
        void CreateDirectory(string path);

        /// <summary>排他ロックを取る（取れなければ null）。保存先を同時に 2 つのプロセスが書かないため。</summary>
        IDisposable TryLock(string path);
    }

    /// <summary>実ファイルの <see cref="ISaveFileSystem"/>。</summary>
    public sealed class RealSaveFileSystem : ISaveFileSystem
    {
        public bool Exists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

        public void WriteAllTextDurable(string path, string text)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        /// <remarks>
        /// <b>一時的な共有違反は書込担当の中で数回やり直す</b>（P6A 実ビルドの計測で発覚）。Windows ではウイルス対策や
        /// 検索インデクサが置換先を一瞬開いていることがあり、<c>File.Replace</c> が「置換されるファイルを削除できません」で失敗する。
        /// 置換に失敗しても両方のファイルは残る（ReplaceFile の仕様）ので、やり直しは安全。やり直しは書込担当スレッドで行うので
        /// Gameplay を止めない。尽きたら従来どおり失敗として返す（前の有効な世代は残る）。
        /// </remarks>
        public void Replace(string source, string destination)
        {
            TransientIo.Retry(() =>
            {
                if (File.Exists(destination))
                {
                    File.Replace(source, destination, null);
                }
                else
                {
                    File.Move(source, destination);
                }
            });
        }

        public void Move(string source, string destination) => TransientIo.Retry(() => File.Move(source, destination));

        public void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public IDisposable TryLock(string path)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>一時的な I/O 失敗（共有違反・一時的なアクセス拒否）のやり直し（書込担当スレッド専用）。</summary>
    public static class TransientIo
    {
        /// <summary>既定の試行回数（初回を含む）。</summary>
        public const int DefaultAttempts = 5;

        /// <summary>
        /// <paramref name="action"/> を最大 <paramref name="attempts"/> 回試す。<see cref="IOException"/>／
        /// <see cref="UnauthorizedAccessException"/> のときだけ待ってやり直し、最後の失敗はそのまま投げる。
        /// 待ちは <paramref name="firstDelayMs"/> から倍々（既定 20→40→80→160ms。合計 300ms）。
        /// </summary>
        public static int Retry(Action action, int attempts = DefaultAttempts, int firstDelayMs = 20,
            Action<int> sleep = null)
        {
            int delay = firstDelayMs;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return attempt;
                }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < attempts)
                {
                    if (sleep != null)
                    {
                        sleep(delay);
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(delay);
                    }

                    delay *= 2;
                }
            }
        }
    }

    /// <summary>片側のファイルの状態。</summary>
    public enum SaveSideState
    {
        Missing = 0,
        Valid = 1,
        Corrupt = 2,
    }

    /// <summary>片側のファイルを読んだ結果。</summary>
    public readonly struct SaveSideScan
    {
        public SaveSideScan(SaveSideState state, SaveEnvelopeInfo info, string json, string error)
        {
            State = state;
            Info = info;
            Json = json;
            Error = error ?? string.Empty;
        }

        public SaveSideState State { get; }
        public SaveEnvelopeInfo Info { get; }
        public string Json { get; }
        public string Error { get; }
    }

    /// <summary>Load の判定（どちらの側を読むか）。</summary>
    public enum SaveLoadVerdict
    {
        /// <summary>どちらも無い（初めて）。</summary>
        NoSave = 0,

        /// <summary>最新の有効な側がある。</summary>
        Ok = 1,

        /// <summary>片側が壊れていたので、もう片側から読んだ（通知する）。</summary>
        RecoveredFromOtherSide = 2,

        /// <summary>両側が壊れている。Load 不可。元ファイルは残す。</summary>
        BothCorrupt = 3,

        /// <summary>両側が有効だが別の冒険・別 campaign が混在している。Load 不可。</summary>
        MixedAdventures = 4,

        /// <summary>両側が同じ世代で内容が違う。Load 不可。</summary>
        GenerationConflict = 5,
    }

    /// <summary>Load の判定結果。</summary>
    public readonly struct SaveLoadDecision
    {
        public SaveLoadDecision(SaveLoadVerdict verdict, SaveSideScan chosen, string detail)
        {
            Verdict = verdict;
            Chosen = chosen;
            Detail = detail ?? string.Empty;
        }

        public SaveLoadVerdict Verdict { get; }
        public SaveSideScan Chosen { get; }
        public string Detail { get; }
        public bool CanLoad => Verdict == SaveLoadVerdict.Ok || Verdict == SaveLoadVerdict.RecoveredFromOtherSide;
    }

    /// <summary>
    /// 1 つの論理スロットを<b>二世代のファイル</b>で持つ（P6A-02。仕様 §10）。
    ///
    /// 書くときは<b>最新の有効な側を残し、反対側へ</b>書く：一時ファイル → flush → 読み直して検証 → 置換 → 再検証。
    /// どの段で失敗しても最新の有効な側は無傷で残る（受入 P6A 19）。一時ファイルは読込候補にしない。
    /// 世代は単調で、<b>時刻で順序を決めない</b>。
    ///
    /// <b>スレッド。</b> 書込担当スレッドから呼ぶ。Unity の API を使わない。
    /// </summary>
    public sealed class SaveFileStore
    {
        private readonly ISaveFileSystem _fs;
        private readonly string _slot;
        private IDisposable _lock;

        public SaveFileStore(string directory, string slotName = "slot0", ISaveFileSystem fileSystem = null)
        {
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _slot = string.IsNullOrEmpty(slotName) ? "slot0" : slotName;
            _fs = fileSystem ?? new RealSaveFileSystem();
        }

        /// <summary>保存先のディレクトリ。</summary>
        public string Directory { get; }

        /// <summary>A 側のファイル。</summary>
        public string PathA => Path.Combine(Directory, _slot + "_a.json");

        /// <summary>B 側のファイル。</summary>
        public string PathB => Path.Combine(Directory, _slot + "_b.json");

        private string LockPath => Path.Combine(Directory, _slot + ".lock");

        /// <summary>排他ロックを持っているか。</summary>
        public bool HasLock => _lock != null;

        /// <summary>
        /// 排他ロックを取る（仕様 §10「複数プロセス競合」）。取れなければ書かない。
        /// </summary>
        public bool TryAcquireLock(out string error)
        {
            if (_lock != null)
            {
                error = null;
                return true;
            }

            try
            {
                _fs.CreateDirectory(Directory);
            }
            catch (Exception e)
            {
                error = "保存先を作れません: " + e.Message;
                return false;
            }

            _lock = _fs.TryLock(LockPath);
            if (_lock == null)
            {
                error = "保存先が別のプロセスに使われています（同じゲームを 2 つ起動していませんか）。";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>排他ロックを放す。</summary>
        public void ReleaseLock()
        {
            _lock?.Dispose();
            _lock = null;
        }

        /// <summary>片側を読む。</summary>
        public SaveSideScan ScanSide(string path)
        {
            if (!_fs.Exists(path))
            {
                return new SaveSideScan(SaveSideState.Missing, default, null, null);
            }

            string json;
            try
            {
                json = _fs.ReadAllText(path);
            }
            catch (Exception e)
            {
                return new SaveSideScan(SaveSideState.Corrupt, default, null, "読めません: " + e.Message);
            }

            if (!SaveJsonCodec.TryReadEnvelope(json, out SaveEnvelopeInfo info, out _, out string error))
            {
                return new SaveSideScan(SaveSideState.Corrupt, default, json, error);
            }

            return new SaveSideScan(SaveSideState.Valid, info, json, null);
        }

        /// <summary>
        /// どちらを読むかを決める（仕様 §10）。片側破損はもう片側から復旧して通知、両側破損は Load 不可で元ファイルを残す。
        /// 別の冒険・別 campaign の混在、同一世代の内容競合は<b>黙って選ばない</b>。
        /// </summary>
        public SaveLoadDecision DecideLoad()
        {
            SaveSideScan a = ScanSide(PathA);
            SaveSideScan b = ScanSide(PathB);

            if (a.State == SaveSideState.Missing && b.State == SaveSideState.Missing)
            {
                return new SaveLoadDecision(SaveLoadVerdict.NoSave, default, null);
            }

            if (a.State != SaveSideState.Valid && b.State != SaveSideState.Valid)
            {
                return new SaveLoadDecision(SaveLoadVerdict.BothCorrupt, default,
                    "A: " + Describe(a) + " / B: " + Describe(b));
            }

            if (a.State == SaveSideState.Valid && b.State == SaveSideState.Valid)
            {
                if (!string.Equals(a.Info.AdventureId, b.Info.AdventureId, StringComparison.Ordinal)
                    || !string.Equals(a.Info.CampaignId, b.Info.CampaignId, StringComparison.Ordinal))
                {
                    return new SaveLoadDecision(SaveLoadVerdict.MixedAdventures, default,
                        "A=" + a.Info.CampaignId + "/" + a.Info.AdventureId + "、B=" + b.Info.CampaignId + "/" + b.Info.AdventureId);
                }

                if (a.Info.Generation == b.Info.Generation)
                {
                    if (!string.Equals(a.Info.Checksum, b.Info.Checksum, StringComparison.Ordinal))
                    {
                        return new SaveLoadDecision(SaveLoadVerdict.GenerationConflict, default,
                            "両側が世代 " + a.Info.Generation + " で内容が違います。");
                    }

                    return new SaveLoadDecision(SaveLoadVerdict.Ok, a, null);
                }

                return new SaveLoadDecision(SaveLoadVerdict.Ok, a.Info.Generation > b.Info.Generation ? a : b, null);
            }

            // 片側だけ有効。
            SaveSideScan valid = a.State == SaveSideState.Valid ? a : b;
            SaveSideScan other = a.State == SaveSideState.Valid ? b : a;
            if (other.State == SaveSideState.Corrupt)
            {
                return new SaveLoadDecision(SaveLoadVerdict.RecoveredFromOtherSide, valid,
                    "片側が壊れていたため、もう一方（世代 " + valid.Info.Generation + "）から読みました: " + other.Error);
            }

            return new SaveLoadDecision(SaveLoadVerdict.Ok, valid, null);
        }

        /// <summary>
        /// 次の世代の番号を決める（有効な側の最大 ＋ 1。無ければ 1）。
        /// </summary>
        public long NextGeneration(out string targetPath)
        {
            SaveSideScan a = ScanSide(PathA);
            SaveSideScan b = ScanSide(PathB);
            long ga = a.State == SaveSideState.Valid ? a.Info.Generation : 0;
            long gb = b.State == SaveSideState.Valid ? b.Info.Generation : 0;

            // 最新の有効な側を残し、反対側へ書く。両方無効なら A。
            if (ga == 0 && gb == 0)
            {
                targetPath = PathA;
            }
            else
            {
                targetPath = ga >= gb ? PathB : PathA;
            }

            return Math.Max(ga, gb) + 1;
        }

        /// <summary>
        /// 書く（一時ファイル → flush → 読み直して検証 → 置換 → 再検証）。成功したときだけ true。
        /// 失敗しても最新の有効な側は残る。一時ファイルは片付ける（片付けに失敗しても読込候補にはならない）。
        /// </summary>
        public bool TryWrite(string json, long expectedGeneration, string targetPath, out string error)
        {
            if (_lock == null && !TryAcquireLock(out error))
            {
                return false;
            }

            string temp = targetPath + ".tmp";
            try
            {
                _fs.CreateDirectory(Directory);
                _fs.WriteAllTextDurable(temp, json);

                // 一時ファイルを読み直して検証する（書けたつもりで壊れたファイルを置かない）。
                if (!VerifyFile(temp, expectedGeneration, out error))
                {
                    TryDelete(temp);
                    return false;
                }

                _fs.Replace(temp, targetPath);

                if (!VerifyFile(targetPath, expectedGeneration, out error))
                {
                    error = "置換後の再検証に失敗しました: " + error;
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception e)
            {
                TryDelete(temp);
                error = "書き込みに失敗しました: " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// New Game の前に、いまの冒険を退避する（仕様 §10）。<b>退避に失敗したら上書きしない</b>（呼び出し側が New Game を止める）。
        /// 退避先は <c>archive/&lt;日時&gt;</c>。旧冒険を新冒険の破損復旧候補にしない（スロットからは消える）。
        /// </summary>
        public bool TryArchiveCurrent(string stamp, out string archivedTo, out string error)
        {
            archivedTo = null;
            bool hasA = _fs.Exists(PathA);
            bool hasB = _fs.Exists(PathB);
            if (!hasA && !hasB)
            {
                error = null;
                return true;
            }

            try
            {
                string dir = Path.Combine(Directory, "archive", stamp);
                _fs.CreateDirectory(dir);
                if (hasA)
                {
                    _fs.Move(PathA, Path.Combine(dir, Path.GetFileName(PathA)));
                }

                if (hasB)
                {
                    _fs.Move(PathB, Path.Combine(dir, Path.GetFileName(PathB)));
                }

                archivedTo = dir;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = "前の冒険を退避できませんでした: " + e.Message;
                return false;
            }
        }

        private bool VerifyFile(string path, long expectedGeneration, out string error)
        {
            SaveSideScan scan = ScanSide(path);
            if (scan.State != SaveSideState.Valid)
            {
                error = "検証に失敗しました: " + Describe(scan);
                return false;
            }

            if (scan.Info.Generation != expectedGeneration)
            {
                error = "世代が一致しません（期待 " + expectedGeneration + "／実際 " + scan.Info.Generation + "）。";
                return false;
            }

            error = null;
            return true;
        }

        private void TryDelete(string path)
        {
            try
            {
                _fs.Delete(path);
            }
            catch
            {
                // 片付けの失敗は読込候補にならない（.tmp は読まない）ので握りつぶしてよい。
            }
        }

        private static string Describe(SaveSideScan scan)
        {
            switch (scan.State)
            {
                case SaveSideState.Missing:
                    return "なし";
                case SaveSideState.Corrupt:
                    return "破損（" + scan.Error + "）";
                default:
                    return "世代 " + scan.Info.Generation;
            }
        }
    }
}
