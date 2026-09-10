using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Momotaro.EditorBridge
{
    /// <summary>
    /// Editor 常駐ブリッジ本体（開発補助）。<c>_bridge/command.json</c> を 1 秒ごとに見に行き、届いたコマンドを
    /// 開いたままの Editor で実行して <c>_bridge/result.json</c> へ書き戻す。生存状態は <c>status.json</c> に出す。
    ///
    /// 設計上の約束：
    /// <list type="bullet">
    /// <item><description><b>既定は無効。</b>メニュー <c>Momotaro/Bridge/Enabled</c> で明示的に有効化するまで何もしない。</description></item>
    /// <item><description><b>実行できるのは列挙した操作だけ。</b>任意コードの実行・ファイル削除・シェル起動はできない。</description></item>
    /// <item><description><b>ダイアログを出す操作は載せない。</b>無人の PC で確認ダイアログが出ると Editor が固まるため、
    /// メニュー実行は本 Task では提供しない。</description></item>
    /// <item><description><b>Editor 専用アセンブリ。</b>出荷ビルドには一切含まれない。</description></item>
    /// </list>
    ///
    /// コンパイルとテストは Editor の再読み込みをまたぐ。そのため実行中のコマンド id を <see cref="EditorPrefs"/> に
    /// 持たせ、再読み込み後に結果通知を受けても正しい結果ファイルへ書けるようにしている。
    /// </summary>
    [InitializeOnLoad]
    public static class EditorBridgeService
    {
        private const string EnabledKey = "Momotaro.EditorBridge.Enabled";
        private const string LastIdKey = "Momotaro.EditorBridge.LastCommandId";
        private const string BusyIdKey = "Momotaro.EditorBridge.BusyCommandId";
        private const string BusyKindKey = "Momotaro.EditorBridge.BusyKind";
        private const string BusyStartedKey = "Momotaro.EditorBridge.BusyStartedIso";
        private const string BusyModeKey = "Momotaro.EditorBridge.BusyMode";
        private const string BusyExpectedKey = "Momotaro.EditorBridge.BusyExpected";
        private const string BusyMinPassedKey = "Momotaro.EditorBridge.BusyMinPassed";
        private const string BusyFilteredKey = "Momotaro.EditorBridge.BusyFiltered";
        private const string BusyFilterKey = "Momotaro.EditorBridge.BusyFilter";

        private const string BusyCompile = "compile";
        private const string BusyTests = "tests";

        /// <summary>コマンドを見に行く間隔（秒）。</summary>
        public const float PollSeconds = 1f;

        /// <summary>コンパイルが始まらないまま待ち続けない上限（秒）。</summary>
        private const double CompileStartTimeoutSeconds = 5d;

        /// <summary>
        /// テストが終わらないまま実行中の印を抱え続けない上限（秒）。ここを超えたら印を落として次を受け付ける。
        /// 印が残ったままだとブリッジは<b>無言で</b>コマンドを受け取らなくなるため、必ず抜け道を用意する。
        /// </summary>
        private const double TestRunTimeoutSeconds = 900d;

        /// <summary>
        /// PlayMode テストの上限（秒）。EditMode より短くする。PlayMode は再生モードに入るため、暴走すると
        /// <b>無人の PC が再生状態のまま放置される</b>。上限を超えたら再生モードから強制的に抜ける。
        /// </summary>
        private const double PlayModeTimeoutSeconds = 600d;

        /// <summary>詳細 1 件あたりの最大文字数（結果ファイルが読めなくなるほど膨らませない）。</summary>
        private const int DetailMaxLength = 800;

        /// <summary>詳細の最大件数。</summary>
        private const int DetailMaxCount = 30;

        private static double _lastPoll;
        private static readonly List<string> _compilerMessages = new List<string>();

        static EditorBridgeService()
        {
            EditorApplication.update += Poll;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            // ScriptableObject の生成を InitializeOnLoad の最中に行わないよう、1 フレーム遅らせる。
            EditorApplication.delayCall += EditorBridgeTestRun.EnsureCallbacks;
        }

        /// <summary>ブリッジが有効か（既定 false）。</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, false);
            set
            {
                EditorPrefs.SetBool(EnabledKey, value);
                if (value)
                {
                    EditorBridgePaths.EnsureFolder();
                    WriteReadme();
                }

                WriteStatus();
            }
        }

        private static string LastCommandId
        {
            get => EditorPrefs.GetString(LastIdKey, string.Empty);
            set => EditorPrefs.SetString(LastIdKey, value ?? string.Empty);
        }

        private static string BusyCommandId
        {
            get => EditorPrefs.GetString(BusyIdKey, string.Empty);
            set => EditorPrefs.SetString(BusyIdKey, value ?? string.Empty);
        }

        /// <summary>実行中のテストの種別（PlayMode かどうかで上限と後始末が変わる）。</summary>
        private static string BusyMode
        {
            get => EditorPrefs.GetString(BusyModeKey, string.Empty);
            set => EditorPrefs.SetString(BusyModeKey, value ?? string.Empty);
        }

        private static string BusyKind
        {
            get => EditorPrefs.GetString(BusyKindKey, string.Empty);
            set => EditorPrefs.SetString(BusyKindKey, value ?? string.Empty);
        }

        /// <summary>
        /// 実行中のテストの予定件数（<c>RunStarted</c> で分かる）。開始と完了のあいだに Editor の再読み込みが挟まるため、
        /// static 変数では消える。<see cref="EditorPrefs"/> に預けて完了時に突き合わせる。0 は「不明」。
        ///
        /// <b>絞り込み実行では記録しない。</b><c>RunStarted</c> が渡してくるのは絞り込み後ではなく
        /// <b>スイート全体</b>の件数で（0 件一致の実行でも全件数が来ることを実測で確認した）、
        /// これを実行件数と比べると絞り込み実行が必ず「中断」に見えてしまう。
        /// </summary>
        private static int BusyExpected
        {
            get => EditorPrefs.GetInt(BusyExpectedKey, 0);
            set => EditorPrefs.SetInt(BusyExpectedKey, value);
        }

        /// <summary>実行中のテストが絞り込み実行か（予定件数を完走判定に使えるかの分かれ目）。</summary>
        private static bool BusyFiltered
        {
            get => EditorPrefs.GetBool(BusyFilteredKey, false);
            set => EditorPrefs.SetBool(BusyFilteredKey, value);
        }

        /// <summary>実行中のテストのフィルタ（実行記録へ残す。何を対象にした結果なのかが後から分かるように）。</summary>
        private static string BusyFilter
        {
            get => EditorPrefs.GetString(BusyFilterKey, string.Empty);
            set => EditorPrefs.SetString(BusyFilterKey, value ?? string.Empty);
        }

        /// <summary>実行中のテストに指定された成功件数の下限（0 は指定なし）。同上の理由で <see cref="EditorPrefs"/> に置く。</summary>
        private static int BusyMinPassed
        {
            get => EditorPrefs.GetInt(BusyMinPassedKey, 0);
            set => EditorPrefs.SetInt(BusyMinPassedKey, value);
        }

        private static DateTime BusyStartedUtc
        {
            get
            {
                string raw = EditorPrefs.GetString(BusyStartedKey, string.Empty);
                return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime v)
                    ? v
                    : DateTime.UtcNow;
            }
            set => EditorPrefs.SetString(BusyStartedKey, value.ToString("o"));
        }

        // ---- 主ループ ----

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup - _lastPoll < PollSeconds)
            {
                return;
            }

            _lastPoll = EditorApplication.timeSinceStartup;

            bool folderExists = Directory.Exists(EditorBridgePaths.Root);
            if (!Enabled && !folderExists)
            {
                return; // 一度も有効化していないプロジェクトには何も残さない。
            }

            if (Enabled)
            {
                EditorBridgePaths.EnsureFolder();
            }

            WriteStatus();

            if (!Enabled)
            {
                return;
            }

            if (!string.IsNullOrEmpty(BusyCommandId))
            {
                CheckBusyTimeout();
                return; // 実行中は次のコマンドを取らない。
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            TryTakeCommand();
        }

        private static void TryTakeCommand()
        {
            BridgeCommand command = ReadCommand();
            if (command == null || string.IsNullOrEmpty(command.id) || command.id == LastCommandId)
            {
                return;
            }

            LastCommandId = command.id;
            Execute(command);
        }

        private static BridgeCommand ReadCommand()
        {
            try
            {
                if (!File.Exists(EditorBridgePaths.Command))
                {
                    return null;
                }

                string json = File.ReadAllText(EditorBridgePaths.Command);
                return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<BridgeCommand>(json);
            }
            catch (Exception)
            {
                return null; // 書き込みの途中を読んだ場合は次の巡回で読み直す。
            }
        }

        private static void Execute(BridgeCommand command)
        {
            if (!EditorBridgeCommands.IsKnown(command.command))
            {
                WriteResult(Error(command, "未知のコマンド: " + command.command
                    + "（使えるのは ping / refresh / compile-status / run-tests）"));
                return;
            }

            switch (command.command)
            {
                case EditorBridgeCommands.Ping:
                    BridgeResult pong = Begin(command);
                    pong.status = "ok";
                    pong.message = "Unity " + Application.unityVersion + " が応答しました。";
                    pong.finishedAt = NowIso();
                    WriteResult(pong);
                    break;

                case EditorBridgeCommands.Refresh:
                    BridgeResult refreshed = Begin(command);
                    AssetDatabase.Refresh();
                    refreshed.status = "ok";
                    refreshed.message = "AssetDatabase を更新しました。";
                    refreshed.finishedAt = NowIso();
                    WriteResult(refreshed);
                    break;

                case EditorBridgeCommands.CompileStatus:
                    StartCompile(command);
                    break;

                case EditorBridgeCommands.RunTests:
                    StartTests(command);
                    break;

                case EditorBridgeCommands.RunOp:
                    RunOperation(command);
                    break;
            }
        }

        // ---- 編集操作 ----

        /// <summary>
        /// 許可された編集操作を同期実行する。Prefab の再生成のように「実装を届けたあとに人がメニューを押す」しかなかった
        /// 手順を無くすためのもの。ダイアログを出す操作は載せていないので、無人でも Editor が固まらない。
        /// </summary>
        private static void RunOperation(BridgeCommand command)
        {
            if (string.IsNullOrEmpty(command.op) || !EditorBridgeOperations.IsKnown(command.op))
            {
                WriteResult(Error(command, "未知の操作: " + (command.op ?? "(未指定)")
                    + "（使えるのは " + string.Join(" / ", EditorBridgeOperations.All) + "）"));
                return;
            }

            BridgeResult result = Begin(command);
            EditorBridgeOperations.OperationResult operation = EditorBridgeOperations.Run(command);

            var details = new List<string>();
            for (int i = 0; i < operation.Details.Count && details.Count < DetailMaxCount; i++)
            {
                details.Add(Trim(operation.Details[i]));
            }

            result.status = operation.Success ? "ok" : "failed";
            result.message = operation.Message;
            result.details = details.ToArray();
            result.finishedAt = NowIso();
            WriteResult(result);
        }

        // ---- コンパイル ----

        private static void StartCompile(BridgeCommand command)
        {
            BridgeResult running = Begin(command);
            running.status = "running";
            running.message = "コンパイルを待っています。";
            WriteResult(running);

            SetBusy(command.id, BusyCompile);
            _compilerMessages.Clear();

            AssetDatabase.Refresh();
            if (command.force)
            {
                CompilationPipeline.RequestScriptCompilation();
            }
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null)
            {
                return;
            }

            string assembly = Path.GetFileNameWithoutExtension(assemblyPath);
            for (int i = 0; i < messages.Length; i++)
            {
                CompilerMessage m = messages[i];
                if (m.type != CompilerMessageType.Error && m.type != CompilerMessageType.Warning)
                {
                    continue;
                }

                _compilerMessages.Add(
                    (m.type == CompilerMessageType.Error ? "[error] " : "[warning] ")
                    + assembly + " " + m.file + "(" + m.line + "," + m.column + "): " + m.message);
            }
        }

        private static void OnCompilationFinished(object context)
        {
            if (BusyKind != BusyCompile || string.IsNullOrEmpty(BusyCommandId))
            {
                return;
            }

            int errors = 0;
            var details = new List<string>();
            for (int i = 0; i < _compilerMessages.Count && details.Count < DetailMaxCount; i++)
            {
                string line = _compilerMessages[i];
                if (line.StartsWith("[error] ", StringComparison.Ordinal))
                {
                    errors++;
                }

                details.Add(Trim(line));
            }

            var result = new BridgeResult
            {
                id = BusyCommandId,
                command = EditorBridgeCommands.CompileStatus,
                status = errors > 0 ? "failed" : "ok",
                message = errors > 0
                    ? "コンパイルエラー " + errors + " 件。"
                    : "コンパイルは通りました（警告 " + details.Count + " 件）。",
                startedAt = BusyStartedUtc.ToString("o"),
                finishedAt = NowIso(),
                details = details.ToArray(),
            };

            ClearBusy();
            WriteResult(result);
        }

        /// <summary>
        /// 変更が無くてコンパイルが始まらなかった場合に、待ち続けずに結果を返す
        /// （「返事が来ない」と「変更が無い」を外から区別できるようにする）。
        /// </summary>
        private static void CheckBusyTimeout()
        {
            double elapsed = (DateTime.UtcNow - BusyStartedUtc).TotalSeconds;

            if (BusyKind == BusyTests)
            {
                bool playMode = string.Equals(BusyMode, "PlayMode", StringComparison.OrdinalIgnoreCase);
                if (elapsed < (playMode ? PlayModeTimeoutSeconds : TestRunTimeoutSeconds))
                {
                    return;
                }

                // 無人でも再生状態のまま放置しない。再生モードから抜けてから印を落とす。
                string recovery = string.Empty;
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                    recovery = " 再生モードから強制的に抜けました。";
                }

                var timedOut = new BridgeResult
                {
                    id = BusyCommandId,
                    command = EditorBridgeCommands.RunTests,
                    status = "error",
                    message = "テストの完了通知が " + (int)elapsed + " 秒待っても届きませんでした。"
                        + recovery + " Test Runner ウィンドウで状態を確認してください。",
                    startedAt = BusyStartedUtc.ToString("o"),
                    finishedAt = NowIso(),
                };

                ClearBusy();
                WriteResult(timedOut);
                return;
            }

            if (BusyKind != BusyCompile || EditorApplication.isCompiling)
            {
                return;
            }

            if (elapsed < CompileStartTimeoutSeconds)
            {
                return;
            }

            var result = new BridgeResult
            {
                id = BusyCommandId,
                command = EditorBridgeCommands.CompileStatus,
                status = "ok",
                message = "再コンパイルは発生しませんでした（スクリプトに変更なし）。強制したい場合は force を true にしてください。",
                startedAt = BusyStartedUtc.ToString("o"),
                finishedAt = NowIso(),
            };

            ClearBusy();
            WriteResult(result);
        }

        /// <summary>
        /// <c>result.json</c> に載せる詳細行を作る。失敗を先に、次に成功以外（Skip・Inconclusive）を出す。
        /// Skip は<b>名前と理由</b>を出す。件数だけでは「以前と同数だから非必須だろう」という当てにならない
        /// 判断しかできず、必須テストが静かに Skip へ化けても気付けない。
        /// </summary>
        private static List<string> BuildDetails(List<BridgeLeafResult> leaves)
        {
            var details = new List<string>();
            if (leaves == null)
            {
                return details;
            }

            int omitted = 0;

            for (int pass = 0; pass < 2; pass++)
            {
                foreach (BridgeLeafResult leaf in leaves)
                {
                    bool isFailure = leaf.status == "Failed";
                    bool wanted = pass == 0 ? isFailure : leaf.status != "Passed" && !isFailure;
                    if (!wanted)
                    {
                        continue;
                    }

                    if (details.Count >= DetailMaxCount)
                    {
                        omitted++;
                        continue;
                    }

                    string line = "[" + leaf.status + "] " + leaf.fullName;
                    if (!string.IsNullOrEmpty(leaf.message))
                    {
                        line += "\n  " + leaf.message;
                    }

                    details.Add(Trim(line));
                }
            }

            if (omitted > 0)
            {
                details.Add("（ほか " + omitted + " 件は省略しました。全件は runLogPath のファイルを参照）");
            }

            return details;
        }

        /// <summary>
        /// 実行ごとの完全な記録を <c>_bridge/runs/&lt;id&gt;.json</c> へ書き、その相対パスを返す。
        /// <c>result.json</c> は次の実行で上書きされるため、それだけを証跡にすると
        /// 「その結果がどの実行のものか」を後から追えない（受入記録の要件）。
        /// </summary>
        private static string WriteRunLog(
            string commandId, string mode, string filter, string startedAt, string rootResultState,
            string status, in BridgeRunSummary summary, List<BridgeLeafResult> leaves)
        {
            try
            {
                EditorBridgePaths.EnsureRunsFolder();

                var log = new BridgeRunLog
                {
                    id = commandId,
                    mode = string.IsNullOrEmpty(mode) ? "EditMode" : mode,
                    filter = filter ?? string.Empty,
                    startedAt = startedAt,
                    finishedAt = NowIso(),
                    unityVersion = Application.unityVersion,
                    status = status,
                    termination = summary.Termination.ToString().ToLowerInvariant(),
                    rootResultState = rootResultState ?? string.Empty,
                    expected = summary.Expected,
                    passed = summary.Passed,
                    failed = summary.Failed,
                    skipped = summary.Skipped,
                    inconclusive = summary.Inconclusive,
                    leaves = leaves == null ? Array.Empty<BridgeLeafResult>() : leaves.ToArray(),
                };

                string path = EditorBridgePaths.RunLog(commandId);
                File.WriteAllText(path, JsonUtility.ToJson(log, true));
                return EditorBridgePaths.FolderName + "/runs/" + EditorBridgePaths.Sanitize(commandId) + ".json";
            }
            catch (Exception e)
            {
                // 記録に失敗しても実行結果そのものは返す（黙って落とさず、失敗したことを結果に残す）。
                Debug.LogWarning("[EditorBridge] 実行記録を書けませんでした: " + e.Message);
                return string.Empty;
            }
        }

        /// <summary>実行中の印を手で落とす（メニューからの緊急脱出用）。</summary>
        internal static void ResetBusy()
        {
            ClearBusy();
            WriteStatus();
        }

        // ---- テスト ----

        private static void StartTests(BridgeCommand command)
        {
            BridgeResult running = Begin(command);
            running.status = "running";
            running.message = "テストを実行しています（" + (string.IsNullOrEmpty(command.mode) ? "EditMode" : command.mode) + "）。";
            WriteResult(running);

            SetBusy(command.id, BusyTests);
            BusyMode = command.mode ?? string.Empty;
            BusyExpected = 0; // RunStarted で入る。届かないまま完了したら「不明」として完走判定に使わない。
            BusyFiltered = !string.IsNullOrEmpty(command.filter);
            BusyFilter = command.filter;
            BusyMinPassed = command.minPassed < 0 ? 0 : command.minPassed;

            string error = EditorBridgeTestRun.Run(command.mode, command.filter);
            if (error == null)
            {
                return;
            }

            ClearBusy();
            WriteResult(Error(command, "テストを開始できませんでした: " + error));
        }

        /// <summary>テスト実行の開始通知（<see cref="EditorBridgeTestRun"/> から呼ばれる）。予定件数を控える。</summary>
        internal static void OnTestRunStarted(int expected)
        {
            if (BusyKind != BusyTests || string.IsNullOrEmpty(BusyCommandId))
            {
                return; // ブリッジ経由でない実行には反応しない。
            }

            // 絞り込み実行では意味を持たない件数なので記録しない（上の BusyExpected の説明を参照）。
            BusyExpected = BusyFiltered || expected < 0 ? 0 : expected;
        }

        /// <summary>
        /// テスト実行の完了通知（<see cref="EditorBridgeTestRun"/> から呼ばれる）。
        /// <paramref name="root"/> が null のときは「結果が丸ごと届かなかった」として扱う。
        /// </summary>
        internal static void OnTestRunFinished(
            UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor root,
            List<BridgeLeafResult> leaves,
            string rootResultState,
            double seconds)
        {
            if (BusyKind != BusyTests || string.IsNullOrEmpty(BusyCommandId))
            {
                return; // ブリッジ経由でない実行（Test Runner ウィンドウからの手動実行）には反応しない。
            }

            // 印を落とす前に読む（ClearBusy がこれらも消すため）。
            string commandId = BusyCommandId;
            string mode = BusyMode;
            string filter = BusyFilter;
            int expected = BusyExpected;
            int minPassed = BusyMinPassed;
            string startedAt = BusyStartedUtc.ToString("o");

            BridgeRunSummary summary;
            if (root == null)
            {
                summary = BridgeRunSummary.Missing(expected, minPassed);
                leaves = new List<BridgeLeafResult>();
            }
            else
            {
                BridgeRunTermination termination = EditorBridgeTestRun.ResolveTermination(
                    root.ResultState, root.TestStatus, root.FailCount);

                summary = new BridgeRunSummary(
                    root.PassCount, root.FailCount, root.SkipCount, root.InconclusiveCount,
                    expected, minPassed,
                    leaves == null ? -1 : leaves.Count,
                    termination,
                    string.IsNullOrEmpty(rootResultState) ? "全体結果 " + root.TestStatus : rootResultState);
            }

            // 「失敗 0 件」を成功と読み替えない。一致 0 件・全スキップ・中断・結果不明・集計の食い違いは、
            // いずれも実装が検証されていないのに緑に見える。
            BridgeTestOutcome.Outcome outcome = BridgeTestOutcome.Decide(summary);

            List<string> details = BuildDetails(leaves);
            string runLogPath = WriteRunLog(
                commandId, mode, filter, startedAt, rootResultState, outcome.Status, summary, leaves);

            string counts = "成功 " + summary.Passed + " / 失敗 " + summary.Failed + " / スキップ " + summary.Skipped
                + (summary.Inconclusive > 0 ? " / 不明 " + summary.Inconclusive : string.Empty)
                + (expected > 0 ? " / 予定 " + expected : string.Empty)
                + "（" + seconds.ToString("0.0") + " 秒）";

            var result = new BridgeResult
            {
                id = commandId,
                command = EditorBridgeCommands.RunTests,
                status = outcome.Status,
                message = outcome.IsOk ? counts : outcome.Reason + " " + counts,
                startedAt = startedAt,
                finishedAt = NowIso(),
                passed = summary.Passed,
                failed = summary.Failed,
                skipped = summary.Skipped,
                inconclusive = summary.Inconclusive,
                expected = expected,
                termination = summary.Termination.ToString().ToLowerInvariant(),
                runLogPath = runLogPath,
                details = details.ToArray(),
            };

            ClearBusy();
            WriteResult(result);
        }

        // ---- 補助 ----

        private static BridgeResult Begin(BridgeCommand command)
        {
            BusyStartedUtc = DateTime.UtcNow;
            return new BridgeResult
            {
                id = command.id,
                command = command.command,
                status = "running",
                startedAt = NowIso(),
                finishedAt = string.Empty,
                message = string.Empty,
            };
        }

        private static BridgeResult Error(BridgeCommand command, string message)
        {
            return new BridgeResult
            {
                id = command.id,
                command = command.command,
                status = "error",
                message = message,
                startedAt = NowIso(),
                finishedAt = NowIso(),
            };
        }

        private static void SetBusy(string id, string kind)
        {
            BusyCommandId = id;
            BusyKind = kind;
        }

        private static void ClearBusy()
        {
            BusyCommandId = string.Empty;
            BusyKind = string.Empty;
            BusyMode = string.Empty;
            BusyExpected = 0;
            BusyMinPassed = 0;
            BusyFiltered = false;
            BusyFilter = string.Empty;
        }

        private static void WriteResult(BridgeResult result)
        {
            Write(EditorBridgePaths.Result, JsonUtility.ToJson(result, true));
        }

        private static void WriteStatus()
        {
            var status = new BridgeStatus
            {
                aliveAt = NowIso(),
                enabled = Enabled,
                unityVersion = Application.unityVersion,
                isCompiling = EditorApplication.isCompiling,
                isPlaying = EditorApplication.isPlayingOrWillChangePlaymode,
                lastCommandId = LastCommandId,
                busyCommandId = BusyCommandId,
                pollSeconds = PollSeconds,
            };

            Write(EditorBridgePaths.Status, JsonUtility.ToJson(status, true));
        }

        private static void Write(string path, string content)
        {
            try
            {
                EditorBridgePaths.EnsureFolder();
                File.WriteAllText(path, content);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EditorBridge] 書き込みに失敗しました: " + path + " / " + e.Message);
            }
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= DetailMaxLength)
            {
                return value;
            }

            return value.Substring(0, DetailMaxLength) + " …(省略)";
        }

        private static string NowIso() => DateTime.UtcNow.ToString("o");

        private static void WriteReadme()
        {
            const string readme =
                "# _bridge（Editor 常駐ブリッジの受け渡しフォルダ）\n\n"
                + "Unity Editor を開いたまま、外部から次の操作を依頼するためのポストです。\n"
                + "Assets の外に置いてあるのは、Unity に資産として Import させないためです（Import されると\n"
                + "書き込みのたびに AssetDatabase が更新され、実行中のテストを壊します）。\n\n"
                + "## 使い方\n\n"
                + "1. `command.json` を置く（下の形式）\n"
                + "2. Editor が最大 1 秒で拾い、`result.json` に結果を書く\n"
                + "3. `status.json` は生存確認（`aliveAt` が古ければ Editor が閉じている）\n\n"
                + "## command.json\n\n"
                + "```json\n"
                + "{ \"id\": \"任意の一意な文字列\", \"command\": \"run-tests\", \"mode\": \"EditMode\", \"filter\": \"Companion\", \"force\": false }\n"
                + "```\n\n"
                + "| command | 内容 |\n"
                + "| --- | --- |\n"
                + "| `ping` | 生存確認 |\n"
                + "| `refresh` | AssetDatabase の更新 |\n"
                + "| `compile-status` | 再コンパイルし、エラー・警告を返す（`force` で強制） |\n"
                + "| `run-tests` | テスト実行（`mode` は EditMode / PlayMode、`filter` は正規表現、`minPassed` は成功件数の下限） |\n\n"
                + "| `run-op` | 許可された編集操作を実行（`op` に操作名。build-inumaru / validate-project-data / verify-required-tests / build-companion-field） |\n\n"
                + "同じ `id` は二度実行されません。実行できるのは上の 5 つだけで、任意コードの実行・\n"
                + "ファイル削除・シェル起動はできません。\n\n"
                + "## result.json の status\n\n"
                + "| status | 意味 | 取るべき行動 |\n"
                + "| --- | --- | --- |\n"
                + "| `ok` | 実行が成立し、結果も合格 | 次へ進める |\n"
                + "| `failed` | 実行は成立したが不合格（失敗あり・全スキップ・`minPassed` 割れ） | `details` を読んで直す |\n"
                + "| `error` | 実行が成立していない（一致 0 件・中断・開始できず） | 合否は<b>不明</b>。原因を潰して再実行 |\n\n"
                + "**失敗 0 件は成功と同じではありません。** フィルタの綴りが実装と食い違って 1 件も一致しない実行は\n"
                + "`error` で返ります。中断・結果欠落・Inconclusive・集計と実結果の食い違いも `error` です。\n\n"
                + "`minPassed`（成功件数の下限）は **不足の一部を検出する補助** です。件数が分かっている実行で\n"
                + "直近の実績を入れておくと、テストが消えた・スキップに化けた実行に気付けます。ただし\n"
                + "**下限は完走の証明にはなりません**（下限を超えた直後に中断しても件数は満たされる）。\n"
                + "完走は全体の終端結果（`termination`）で、必須テストの実行は名前付きの必須一覧との照合で判定します。\n\n"
                + "`expected`（予定件数）は **`filter` を付けない全件実行のときだけ** 記録されます。\n"
                + "Unity が開始時に渡してくる件数は絞り込み後ではなくスイート全体のためです。\n\n"
                + "## 実行ごとの記録\n\n"
                + "`result.json` は次の実行で上書きされます。葉テスト全件の結果（名前・状態・Skip 理由）は\n"
                + "`runs/<コマンド id>.json` に残り、上書きされません。`result.json` の `runLogPath` がその場所を指します。\n\n"
                + "## 止め方\n\n"
                + "メニュー `Momotaro / Bridge / Enabled` のチェックを外してください。無効化すると\n"
                + "コマンドを受け付けなくなります（`status.json` の更新だけ続きます）。\n\n"
                + "このフォルダは機械同士の受け渡し用です。バージョン管理へ入れる必要はありません\n"
                + "（`.gitignore` に `_bridge/` を追加することを推奨します）。\n";

            try
            {
                // 常に書き直す（生成物なので、規則が変わったのに古い説明が残り続けるほうが害が大きい）。
                File.WriteAllText(EditorBridgePaths.Readme, readme);
            }
            catch (Exception)
            {
                // 説明の生成に失敗してもブリッジの動作には影響しない。
            }
        }
    }
}
