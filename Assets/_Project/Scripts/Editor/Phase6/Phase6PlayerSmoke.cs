using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Momotaro.Editor.Phase6
{
    /// <summary>
    /// P6A の<b>実ビルド</b>確認（P6A 23／25。工程 P6A-06）。Windows の試遊ビルドを作り、<b>別プロセス</b>で
    /// New Game → 正常終了の保存 → 終了 → 別プロセスで Continue、と性能計測（通常 I/O・遅い I/O）を自動で走らせる。
    ///
    /// 操作はビルド側の <c>Phase6SmokeDriver</c> がコマンドライン引数で行う。ここは作って起動して結果を集めるだけ。
    /// 保存先は <c>_bridge/p6a_smoke/saves</c>（試遊者の保存へ触れない）。ビルドは <c>Builds/P6A</c>（追跡しない）。
    /// </summary>
    public static class Phase6PlayerSmoke
    {
        public const string BuildFolder = "Builds/P6A";
        public const string ExeName = "Momotaro_P6A.exe";
        public const string WorkFolder = "_bridge/p6a_smoke";

        /// <summary>結果（ブリッジの RunBuilder が読む形）。</summary>
        public readonly struct BuildResult
        {
            public BuildResult(bool success, string message, IReadOnlyList<string> outputs)
            {
                Success = success;
                Message = message;
                Outputs = outputs ?? new List<string>();
            }

            public bool Success { get; }
            public string Message { get; }
            public IReadOnlyList<string> Outputs { get; }
        }

        [MenuItem("Momotaro/Phase 6A/Player Build Smoke (Windows)")]
        private static void RunInteractive()
        {
            BuildResult r = BuildAll();
            EditorUtility.DisplayDialog("P6A 実ビルド確認", r.Message + "\n" + string.Join("\n", r.Outputs), "OK");
        }

        /// <summary>ビルドして、別プロセスの確認と性能計測を走らせる。</summary>
        public static BuildResult BuildAll()
        {
            var outputs = new List<string>();
            string root = Directory.GetParent(Application.dataPath).FullName;
            string work = Path.Combine(root, WorkFolder);
            string saves = Path.Combine(work, "saves_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(saves);
            string exe = Path.Combine(root, BuildFolder, ExeName);

            // ---- 1. ビルド ----
            var options = new BuildPlayerOptions
            {
                scenes = new[]
                {
                    Phase6WorldIds.TitleScenePath, Phase6WorldIds.AreaAScenePath,
                    Phase6WorldIds.AreaBScenePath, Phase6WorldIds.AreaCScenePath,
                },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var watch = Stopwatch.StartNew();
            BuildReport report = BuildPipeline.BuildPlayer(options);
            watch.Stop();
            outputs.Add("ビルド: " + report.summary.result + "（" + watch.Elapsed.TotalSeconds.ToString("0") + " 秒、"
                + (report.summary.totalSize / (1024 * 1024)) + " MB、エラー " + report.summary.totalErrors + "）→ " + exe);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                return new BuildResult(false, "ビルドに失敗しました。", outputs);
            }

            // ---- 2. 別プロセス：New Game → 正常終了の保存 ----
            bool ok = true;
            ok &= RunPlayer(exe, "new", saves, work, 0, batch: true, outputs, out Dictionary<string, string> created);
            ok &= RunPlayer(exe, "continue", saves, work, 0, batch: true, outputs, out Dictionary<string, string> continued);
            if (ok)
            {
                ok &= Expect(outputs, "別プロセスの Continue が同じ冒険", Get(created, "adventureId"), Get(continued, "adventureId"));
                ok &= Expect(outputs, "正常終了で保存した残数が戻る", Get(created, "kibidango"), Get(continued, "kibidango"));
                ok &= Expect(outputs, "正常終了の保存が成功", "Saved", Get(created, "exitOutcome"));
                ok &= Expect(outputs, "再開エリア", Get(created, "area"), Get(continued, "area"));
                ok &= Expect(outputs, "Continue 直後は未保存なし", "false", Get(continued, "dirtyAfterContinue"));
                ok &= Expect(outputs, "プロセスが別", "different",
                    Get(created, "processId") != Get(continued, "processId") ? "different" : "same");
            }

            // ---- 2b. 別プロセス：版を進めない変化だけで通常の終了要求 → Continue（レビュー R1）----
            string closeSaves = Path.Combine(work, "close_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(closeSaves);
            bool closeOk = RunPlayer(exe, "close", closeSaves, work, 0, batch: true, outputs, out Dictionary<string, string> closed);
            closeOk &= RunPlayer(exe, "continue", closeSaves, work, 0, batch: true, outputs,
                out Dictionary<string, string> reopened, tag: "continue_after_close");
            if (closeOk)
            {
                closeOk &= Expect(outputs, "終了要求の前は版が進んでいない（HP・Down だけ）", "true", Get(closed, "revisionUnchanged"));
                closeOk &= Expect(outputs, "同じ冒険", Get(closed, "adventureId"), Get(reopened, "adventureId"));
                closeOk &= Expect(outputs, "終了要求の直前の HP が戻る", Get(closed, "beforePlayerHp"), Get(reopened, "afterPlayerHp"));
                closeOk &= Expect(outputs, "犬丸の Down が戻る", "true", Get(reopened, "afterCompanionDown"));
                closeOk &= ExpectAtMost(outputs, "犬丸の復帰待ちは終了時以下（無料回復しない）",
                    Get(closed, "beforeCompanionRecovery"), Get(reopened, "afterCompanionRecovery"));
                closeOk &= Expect(outputs, "Continue 直後は未保存なし", "false", Get(reopened, "dirtyAfterContinue"));
            }

            ok &= closeOk;

            // ---- 2c. 実プレイ中（歩行・実攻撃の連続撃破・休息）の保存性能。画面あり ----
            string playSaves = Path.Combine(work, "play_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(playSaves);
            ok &= RunPlayer(exe, "play", playSaves, work, 0, batch: false, outputs, out _);

            // 切り分け：同じ実プレイを保存の採取なしで（休息の停止が保存によるものかを分ける）。
            string playNoSave = Path.Combine(work, "play_nosave_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(playNoSave);
            ok &= RunPlayer(exe, "play", playNoSave, work, 0, batch: false, outputs, out _, tag: "play_nosave", extraArgs: " -p6a-no-save 1");

            // 切り分け：GC・一時停止・メニューの文字の描画を、計測の前に段階を分けて先に起こす。
            string playProbe = Path.Combine(work, "play_probe_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(playProbe);
            ok &= RunPlayer(exe, "play", playProbe, work, 0, batch: false, outputs, out _, tag: "play_probe", extraArgs: " -p6a-play-probe 1");

            // ---- 3. 性能（通常 I/O・遅い I/O）。画面ありで 60fps 目標 ----
            string perfSaves = Path.Combine(work, "perf_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(perfSaves);
            ok &= RunPlayer(exe, "perf", perfSaves, work, 0, batch: false, outputs, out _);
            string slowSaves = perfSaves + "_slow";
            Directory.CreateDirectory(slowSaves);
            ok &= RunPlayer(exe, "perf", slowSaves, work, 200, batch: false, outputs, out _);

            return new BuildResult(ok, ok ? "実ビルドの別プロセス確認と計測が終わりました。" : "実ビルドの確認に失敗があります。", outputs);
        }

        private static string Get(Dictionary<string, string> d, string key) =>
            d != null && d.TryGetValue(key, out string v) ? v : "(なし)";

        private static bool Expect(List<string> outputs, string label, string expected, string actual)
        {
            bool ok = expected == actual;
            outputs.Add((ok ? "[OK] " : "[NG] ") + label + "：期待=" + expected + " 実際=" + actual);
            return ok;
        }

        private static bool ExpectAtMost(List<string> outputs, string label, string limit, string actual)
        {
            bool ok = float.TryParse(limit, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float l)
                      && float.TryParse(actual, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float a)
                      && a <= l + 0.001f && a > 0f;
            outputs.Add((ok ? "[OK] " : "[NG] ") + label + "：上限=" + limit + " 実際=" + actual);
            return ok;
        }

        private static bool RunPlayer(string exe, string mode, string saveDir, string work, int slowIo, bool batch,
            List<string> outputs, out Dictionary<string, string> result, string tag = null, string extraArgs = "")
        {
            result = null;
            tag ??= mode + (slowIo > 0 ? "_slow" + slowIo : string.Empty);
            string outPath = Path.Combine(work, "result_" + tag + ".json");
            string logPath = Path.Combine(work, "player_" + tag + ".log");
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }

            string args = "-p6a-smoke " + mode + " -p6a-save-dir \"" + saveDir + "\" -p6a-out \"" + outPath + "\""
                + (slowIo > 0 ? " -p6a-slow-io " + slowIo : string.Empty)
                + extraArgs
                + " -logFile \"" + logPath + "\""
                + (batch ? " -batchmode -nographics" : " -screen-fullscreen 0 -screen-width 1280 -screen-height 720");

            var watch = Stopwatch.StartNew();
            using (Process p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false }))
            {
                if (p == null)
                {
                    outputs.Add("[NG] " + tag + "：起動できませんでした。");
                    return false;
                }

                if (!p.WaitForExit(180000))
                {
                    p.Kill();
                    outputs.Add("[NG] " + tag + "：180 秒で終わりませんでした（強制終了）。ログ: " + logPath);
                    return false;
                }
            }

            watch.Stop();
            if (!File.Exists(outPath))
            {
                outputs.Add("[NG] " + tag + "：結果が書かれていません。ログ: " + logPath);
                return false;
            }

            string json = File.ReadAllText(outPath);
            result = ParseFlat(json);
            outputs.Add(tag + "（" + watch.Elapsed.TotalSeconds.ToString("0.0") + " 秒）: " + json.Replace("\n", " "));
            if (result.ContainsKey("error"))
            {
                outputs.Add("[NG] " + tag + "：" + result["error"]);
                return false;
            }

            return true;
        }

        /// <summary>1 段の {"k": "v"} だけを読む（ビルド側が書く形）。</summary>
        private static Dictionary<string, string> ParseFlat(string json)
        {
            var d = new Dictionary<string, string>();
            foreach (string line in json.Split('\n'))
            {
                string t = line.Trim().TrimEnd(',');
                int colon = t.IndexOf("\": \"", StringComparison.Ordinal);
                if (!t.StartsWith("\"", StringComparison.Ordinal) || colon < 0 || !t.EndsWith("\"", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = t.Substring(1, colon - 1);
                string value = t.Substring(colon + 4, t.Length - colon - 5);
                d[key] = value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            return d;
        }
    }
}
