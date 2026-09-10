using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Momotaro.EditorBridge
{
    /// <summary>
    /// ブリッジからのテスト実行（開発補助）。<see cref="TestRunnerApi"/> を薄く包み、全体の終わり方と
    /// <b>葉テスト全件</b>の結果を <see cref="EditorBridgeService"/> へ渡す。
    ///
    /// テスト実行は Editor の再読み込みをまたぐため、完了通知は「実行を開始した側」では受け取れない。
    /// そこで通知の受け口は読み込みのたびに登録し直す（<see cref="EnsureCallbacks"/>）。実行中かどうかは
    /// <see cref="EditorBridgeService"/> 側が <see cref="EditorPrefs"/> で覚えている。
    ///
    /// <b>失敗した葉だけを拾っていたころの問題</b>：件数（Pass/Fail/Skip）しか外へ出ていなかったため、
    /// 「どのテストが Skip したのか」も「Inconclusive が混ざっていたか」も分からず、
    /// 「以前と同数だから非必須だろう」という当てにならない判断しかできなかった。全件を名前ごと残す。
    ///
    /// Test Runner ウィンドウからの手動実行でも通知は届くが、ブリッジが実行中でなければ何もしない。
    /// </summary>
    public static class EditorBridgeTestRun
    {
        private static TestRunnerApi _api;
        private static Callbacks _callbacks;
        private static double _startedAt;

        /// <summary>通知の受け口を登録する（読み込みのたびに 1 回）。</summary>
        public static void EnsureCallbacks()
        {
            if (_api != null)
            {
                return;
            }

            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.hideFlags = HideFlags.HideAndDontSave; // Scene・Project に残さない。

            _callbacks = new Callbacks();
            _api.RegisterCallbacks(_callbacks);
        }

        /// <summary>
        /// テストを開始する。開始できたら null、できなければ理由を返す。
        /// </summary>
        /// <param name="mode">EditMode／PlayMode（空なら EditMode）。</param>
        /// <param name="filter">テスト名の絞り込み（正規表現。空で全件）。</param>
        public static string Run(string mode, string filter)
        {
            try
            {
                EnsureCallbacks();

                TestMode testMode = string.Equals(mode, "PlayMode", StringComparison.OrdinalIgnoreCase)
                    ? TestMode.PlayMode
                    : TestMode.EditMode;

                var testFilter = new Filter { testMode = testMode };
                if (!string.IsNullOrEmpty(filter))
                {
                    testFilter.groupNames = new[] { filter };
                }

                _startedAt = EditorApplication.timeSinceStartup;
                _api.Execute(new ExecutionSettings(testFilter));
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        /// <summary>
        /// 全体結果の <c>ResultState</c> から終わり方を判定する。
        ///
        /// NUnit の ResultState は "Passed" / "Failed" / "Failed:Error" / "Skipped:Ignored" / "Cancelled" /
        /// "Inconclusive" のように「状態:詳細」の形を取る。中断だけは件数から絶対に分からないため、
        /// ここで文字列を見るしかない。<b>推測でプロパティ名を増やさず</b>、
        /// <see cref="ITestResultAdaptor.ResultState"/> と <see cref="ITestResultAdaptor.TestStatus"/> だけで決める。
        /// </summary>
        internal static BridgeRunTermination ResolveTermination(string resultState, TestStatus status, int failed)
        {
            string state = resultState ?? string.Empty;

            if (state.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return BridgeRunTermination.Cancelled;
            }

            if (status == TestStatus.Inconclusive)
            {
                return BridgeRunTermination.Unknown;
            }

            // 全体が「失敗」を主張しているのに失敗した葉が 1 件も無い＝どちらかの集計が壊れている。
            // 件数だけを信じて緑にすると、この食い違いが表に出ない。
            if (status == TestStatus.Failed && failed <= 0)
            {
                return BridgeRunTermination.Unknown;
            }

            return BridgeRunTermination.Completed;
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                _startedAt = EditorApplication.timeSinceStartup;

                // 予定件数を先に控える。完了時の件数と突き合わせれば「途中で止まった実行」を
                // 「失敗 0 件だから緑」と取り違えずに済む（キャンセル・再生モードの異常終了で起きる）。
                EditorBridgeService.OnTestRunStarted(testsToRun == null ? 0 : testsToRun.TestCaseCount);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                double seconds = EditorApplication.timeSinceStartup - _startedAt;

                if (result == null)
                {
                    // 結果が丸ごと届かないケースを黙って無視しない（無視すると実行中の印が残り、
                    // タイムアウトまでブリッジが応答しなくなる）。
                    EditorBridgeService.OnTestRunFinished(null, null, string.Empty, seconds);
                    return;
                }

                var leaves = new List<BridgeLeafResult>();
                Collect(result, leaves);

                EditorBridgeService.OnTestRunFinished(result, leaves, result.ResultState, seconds);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }

            /// <summary>
            /// 「葉」のテストをすべて集める（親ノードは子の結果を繰り返すだけなので拾わない）。
            /// 失敗だけでなく成功・スキップ・Inconclusive も残す。Skip の理由は
            /// <see cref="ITestAdaptor.SkipReason"/>、無ければ結果側の <see cref="ITestResultAdaptor.Message"/> を使う。
            /// </summary>
            private static void Collect(ITestResultAdaptor node, List<BridgeLeafResult> leaves)
            {
                if (node == null)
                {
                    return;
                }

                bool isLeaf = true;
                foreach (ITestResultAdaptor child in node.Children)
                {
                    isLeaf = false;
                    Collect(child, leaves);
                }

                if (!isLeaf)
                {
                    return;
                }

                leaves.Add(new BridgeLeafResult
                {
                    fullName = node.FullName,
                    status = node.TestStatus.ToString(),
                    resultState = node.ResultState,
                    message = node.TestStatus == TestStatus.Passed ? string.Empty : ResolveMessage(node),
                });
            }

            private static string ResolveMessage(ITestResultAdaptor node)
            {
                string message = node.Message;
                if (string.IsNullOrEmpty(message) && node.Test != null)
                {
                    message = node.Test.SkipReason;
                }

                if (string.IsNullOrEmpty(message))
                {
                    return string.Empty;
                }

                message = message.Trim();

                // 失敗はプロジェクト内の最初のスタック行だけ足す（どのテストのどこで落ちたかが分かれば足りる）。
                if (node.TestStatus == TestStatus.Failed)
                {
                    string frame = FirstProjectFrame(node.StackTrace);
                    if (!string.IsNullOrEmpty(frame))
                    {
                        message += "\n  " + frame;
                    }
                }

                return message;
            }

            /// <summary>スタックのうちプロジェクト内の最初の行だけを残す。</summary>
            private static string FirstProjectFrame(string stackTrace)
            {
                if (string.IsNullOrEmpty(stackTrace))
                {
                    return string.Empty;
                }

                string[] lines = stackTrace.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("Assets/"))
                    {
                        return lines[i].Trim();
                    }
                }

                return lines[0].Trim();
            }
        }
    }
}
