using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Save;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// P6A の<b>実ビルド</b>確認の自動操作（P6A 23／25。工程 P6A-06）。コマンドライン引数があるときだけタイトルが起動する。
    ///
    /// <list type="bullet">
    /// <item><c>-p6a-smoke new</c>：New Game → 到着・保存 → きびだんごを 1 つ使う（保存契機ではない変化）→ 正常終了の保存 → 終了。</item>
    /// <item><c>-p6a-smoke continue</c>：別プロセスで Continue → 採用 → 状態を書き出して終了（Editor の static に依らない）。</item>
    /// <item><c>-p6a-smoke perf</c>：New Game のあと保存契機を 150 回以上出し（連続・書込中の追加要求を含む）、
    /// 採取のメインスレッド時間・要求から完了までの時間・フレーム時間を測って書き出す。<c>-p6a-slow-io &lt;ms&gt;</c> で遅い I/O。</item>
    /// </list>
    /// 結果は <c>-p6a-out &lt;path&gt;</c> へ JSON で書く。保存先は <c>-p6a-save-dir &lt;dir&gt;</c>（試遊者の保存へ触れない）。
    /// <b>試遊の通常起動では何もしない。</b>
    /// </summary>
    public static class Phase6SmokeArgs
    {
        /// <summary>モード（空なら無効）。</summary>
        public static string Mode { get; private set; } = string.Empty;

        /// <summary>結果の書き出し先。</summary>
        public static string OutPath { get; private set; } = string.Empty;

        /// <summary>遅い I/O の遅延（ミリ秒。0 で無し）。</summary>
        public static int SlowIoMilliseconds { get; private set; }

        /// <summary>引数を読み、保存先の差し替えを<b>保存サービスが調停役を作る前に</b>当てる。有効なら true。</summary>
        public static bool TryApply()
        {
            string[] args = Environment.GetCommandLineArgs();
            string saveDir = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "-p6a-smoke":
                        Mode = args[i + 1];
                        break;
                    case "-p6a-out":
                        OutPath = args[i + 1];
                        break;
                    case "-p6a-save-dir":
                        saveDir = args[i + 1];
                        break;
                    case "-p6a-slow-io":
                        int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms);
                        SlowIoMilliseconds = ms;
                        break;
                }
            }

            if (string.IsNullOrEmpty(Mode) || string.IsNullOrEmpty(saveDir))
            {
                Mode = string.Empty;
                return false;
            }

            // 窓が前面に無くても止めない（自動確認の窓は Editor の後ろで開くことがある。止まると計測が終わらない）。
            Application.runInBackground = true;
            CampaignSaveService.TestDirectoryOverride = saveDir;
            CampaignSaveService saves = BootstrapServices.Get<CampaignSaveService>();
            if (saves != null && SlowIoMilliseconds > 0)
            {
                saves.FileSystemOverride = new SlowSaveFileSystem(SlowIoMilliseconds);
            }

            return true;
        }
    }

    /// <summary>書込・置換を遅らせるファイル操作（遅い I/O の計測用）。</summary>
    public sealed class SlowSaveFileSystem : ISaveFileSystem
    {
        private readonly RealSaveFileSystem _real = new RealSaveFileSystem();
        private readonly int _delay;

        public SlowSaveFileSystem(int delayMilliseconds) => _delay = delayMilliseconds;

        public bool Exists(string path) => _real.Exists(path);
        public string ReadAllText(string path) => _real.ReadAllText(path);

        public void WriteAllTextDurable(string path, string text)
        {
            Thread.Sleep(_delay);
            _real.WriteAllTextDurable(path, text);
        }

        public void Replace(string source, string destination) => _real.Replace(source, destination);
        public void Move(string source, string destination) => _real.Move(source, destination);
        public void Delete(string path) => _real.Delete(path);
        public void CreateDirectory(string path) => _real.CreateDirectory(path);
        public IDisposable TryLock(string path) => _real.TryLock(path);
    }

    /// <summary>自動操作の本体（タイトルの <see cref="Phase6CampaignLauncher"/> が起動する）。</summary>
    [DisallowMultipleComponent]
    public sealed class Phase6SmokeDriver : MonoBehaviour
    {
        private Phase6CampaignLauncher _launcher;
        private readonly Dictionary<string, string> _result = new Dictionary<string, string>();

        /// <summary>起動する（常駐へ置く。タイトル Scene が入れ替わっても続く）。</summary>
        public static void Begin(Phase6CampaignLauncher launcher)
        {
            var go = new GameObject("Phase6SmokeDriver");
            DontDestroyOnLoad(go);
            var driver = go.AddComponent<Phase6SmokeDriver>();
            driver._launcher = launcher;
            driver.StartCoroutine(driver.Run());
        }

        private IEnumerator Run()
        {
            _result["mode"] = Phase6SmokeArgs.Mode;
            _result["platform"] = Application.platform.ToString();
            _result["unity"] = Application.unityVersion;
            _result["isEditor"] = Application.isEditor ? "true" : "false";
            _result["processId"] = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            _result["device"] = SystemInfo.deviceModel + " / " + SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName;
            _result["slowIoMs"] = Phase6SmokeArgs.SlowIoMilliseconds.ToString(CultureInfo.InvariantCulture);

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            switch (Phase6SmokeArgs.Mode)
            {
                case "new":
                    yield return NewGameThenExit();
                    break;
                case "continue":
                    yield return ContinueThenExit();
                    break;
                case "perf":
                    yield return Perf();
                    break;
                default:
                    _result["error"] = "unknown mode";
                    Finish();
                    break;
            }
        }

        private static CampaignSaveService Saves => BootstrapServices.Get<CampaignSaveService>();

        private IEnumerator StartNewGame()
        {
            if (!_launcher.PressNewGame(confirmed: true))
            {
                _result["error"] = "new game refused: " + _launcher.Status;
                yield break;
            }

            yield return WaitArrived();
            yield return WaitSaved();
        }

        private static IEnumerator WaitArrived()
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
                AreaTransitionService t = BootstrapServices.Get<AreaTransitionService>();
                if (bundle != null && bundle.Context != null && bundle.Context.IsAreaReady && t != null && !t.IsTransitionUnsettled)
                {
                    yield break;
                }

                yield return null;
            }
        }

        private static IEnumerator WaitSaved()
        {
            SaveCoordinator c = Saves.Coordinator;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (c != null && (c.IsDirty || c.IsWriting) && c.Status != SaveStatus.Failed && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private IEnumerator NewGameThenExit()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            GameSessionState session = GameSessionProvider.Current;
            session.TryConsumeKibidango(1); // 保存契機ではない変化（正常終了の保存が拾うこと）。
            _result["adventureId"] = session.AdventureId;
            _result["kibidango"] = session.Kibidango.ToString(CultureInfo.InvariantCulture);
            _result["area"] = CurrentAreaProvider.Current.AreaId.Value;
            _result["dirtyBeforeExit"] = Saves.Coordinator.IsDirty ? "true" : "false";

            SaveExitOutcome outcome = SaveExitOutcome.TimedOut;
            yield return Saves.SaveBeforeExit(o => outcome = o);
            _result["exitOutcome"] = outcome.ToString();
            _result["savedRevision"] = Saves.Coordinator.SavedRevision.ToString(CultureInfo.InvariantCulture);
            _result["revision"] = session.Changes.Revision.ToString(CultureInfo.InvariantCulture);
            Finish();
        }

        private IEnumerator ContinueThenExit()
        {
            CampaignAdventureFlow flow = Saves.Flow;
            bool done = false;
            string failure = null;
            flow.Continued += () => done = true;
            flow.ContinueFailed += reason => failure = reason;
            if (!_launcher.PressContinue())
            {
                _result["error"] = "continue refused: " + _launcher.Status;
                Finish();
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 60f;
            while (!done && failure == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!done)
            {
                _result["error"] = "continue failed: " + (failure ?? "timeout");
                Finish();
                yield break;
            }

            yield return WaitArrived();
            GameSessionState session = GameSessionProvider.Current;
            _result["adventureId"] = session.AdventureId;
            _result["kibidango"] = session.Kibidango.ToString(CultureInfo.InvariantCulture);
            _result["area"] = CurrentAreaProvider.Current != null ? CurrentAreaProvider.Current.AreaId.Value : string.Empty;
            _result["checkpoint"] = session.Checkpoint.Value;
            _result["resume"] = session.Resume.ToString();
            _result["savedRevision"] = Saves.Coordinator.SavedRevision.ToString(CultureInfo.InvariantCulture);
            _result["dirtyAfterContinue"] = Saves.Coordinator.IsDirty ? "true" : "false";
            Finish();
        }

        // ---------------------------------------------------------------- 性能（P6A 25）

        private IEnumerator Perf()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            GameSessionState session = GameSessionProvider.Current;
            SaveCoordinator c = Saves.Coordinator;
            int captureStart = c.CaptureMilliseconds.Count;
            var latencies = new List<double>();
            var frames = new List<double>();
            int requests = 0;
            int burstExtra = 0;
            var random = new System.Random(6);

            // 計測中のフレーム時間（ms）。
            bool measuring = true;
            IEnumerator FrameSampler()
            {
                while (measuring)
                {
                    yield return null;
                    frames.Add(Time.unscaledDeltaTime * 1000.0);
                }
            }

            Coroutine sampler = StartCoroutine(FrameSampler());
            for (int i = 0; i < 150; i++)
            {
                // 状態を変える（連続撃破の代わりに版を進める変化）→ 保存要求。
                session.TryConsumeKibidango(1);
                session.RefillKibidango(3);
                double t0 = Time.realtimeSinceStartupAsDouble;
                session.Changes.RequestAutosave("perf");
                requests++;

                // 10 回に 1 回は、書込中に追加要求を重ねる（最新要求の取りこぼしが無いこと）。
                if (i % 10 == 9)
                {
                    Debug.Log("P6A smoke perf " + i + ": burst");
                    float wait = Time.realtimeSinceStartup + 1f;
                    while (!c.IsWriting && Time.realtimeSinceStartup < wait)
                    {
                        yield return null;
                    }

                    session.TryConsumeKibidango(1);
                    session.Changes.RequestAutosave("perf_burst");
                    requests++;
                    burstExtra++;
                }

                float deadline = Time.realtimeSinceStartup + 10f;
                while ((c.IsDirty || c.IsWriting) && c.Status != SaveStatus.Failed && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                latencies.Add((Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
                Debug.Log("P6A smoke perf " + i + ": " + latencies[latencies.Count - 1].ToString("0.0", CultureInfo.InvariantCulture)
                    + " ms, status=" + c.Status + " rev=" + c.Session.Changes.Revision + " saved=" + c.SavedRevision
                    + " mode=" + (Momotaro.Gameplay.Modes.GameModeProvider.Current != null
                        ? Momotaro.Gameplay.Modes.GameModeProvider.Current.Current.ToString() : "null")
                    + " respawn=" + session.Respawn.Phase);
                if (c.Status == SaveStatus.Failed)
                {
                    _result["error"] = "save failed: " + c.LastError;
                    break;
                }

                if (c.Session.Changes.Revision != c.SavedRevision)
                {
                    _result["error"] = "latest revision not saved (" + c.Session.Changes.Revision + " vs " + c.SavedRevision + ")";
                    break;
                }

                int gap = random.Next(2, 10);
                for (int f = 0; f < gap; f++)
                {
                    yield return null;
                }
            }

            measuring = false;
            StopCoroutine(sampler);

            var captures = new List<double>();
            for (int i = captureStart; i < c.CaptureMilliseconds.Count; i++)
            {
                captures.Add(c.CaptureMilliseconds[i]);
            }

            _result["requests"] = requests.ToString(CultureInfo.InvariantCulture);
            _result["burstExtraRequests"] = burstExtra.ToString(CultureInfo.InvariantCulture);
            _result["captures"] = captures.Count.ToString(CultureInfo.InvariantCulture);
            _result["successes"] = c.SuccessCount.ToString(CultureInfo.InvariantCulture);
            _result["failures"] = c.FailureCount.ToString(CultureInfo.InvariantCulture);
            _result["bytes"] = c.LastBytes.ToString(CultureInfo.InvariantCulture);
            _result["captureP95Ms"] = Percentile(captures, 0.95);
            _result["captureMaxMs"] = Percentile(captures, 1.0);
            _result["captureMedianMs"] = Percentile(captures, 0.5);
            _result["latencyP95Ms"] = Percentile(latencies, 0.95);
            _result["latencyMaxMs"] = Percentile(latencies, 1.0);
            _result["latencyMedianMs"] = Percentile(latencies, 0.5);
            _result["frameP95Ms"] = Percentile(frames, 0.95);
            _result["frameMaxMs"] = Percentile(frames, 1.0);
            _result["frameMedianMs"] = Percentile(frames, 0.5);
            _result["frames"] = frames.Count.ToString(CultureInfo.InvariantCulture);
            Finish();
        }

        private static string Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
            {
                return "NaN";
            }

            var sorted = new List<double>(values);
            sorted.Sort();
            int index = (int)Math.Ceiling(p * sorted.Count) - 1;
            index = Mathf.Clamp(index, 0, sorted.Count - 1);
            return sorted[index].ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void Finish()
        {
            try
            {
                var sb = new StringBuilder("{");
                bool first = true;
                foreach (KeyValuePair<string, string> kv in _result)
                {
                    sb.Append(first ? "\n  " : ",\n  ");
                    first = false;
                    sb.Append('"').Append(Escape(kv.Key)).Append("\": \"").Append(Escape(kv.Value)).Append('"');
                }

                sb.Append("\n}\n");
                if (!string.IsNullOrEmpty(Phase6SmokeArgs.OutPath))
                {
                    File.WriteAllText(Phase6SmokeArgs.OutPath, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("P6A smoke: could not write the result: " + e.Message);
            }

            // 結果は書いた。終了前の保存（と失敗時の選択待ち）は自動確認では行わない——New Game の確認は
            // 自分で SaveBeforeExit を通し終えている。選択待ちで止まるとプロセスが終わらない（実際に踏んだ）。
            CampaignSaveService saves = Saves;
            if (saves != null)
            {
                saves.ChooseQuitWithoutSaving();
            }
            else
            {
                Application.Quit();
            }
        }

        private static string Escape(string s) =>
            (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
    }
}
