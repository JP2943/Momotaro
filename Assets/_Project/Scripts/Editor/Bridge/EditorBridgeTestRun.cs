using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Momotaro.EditorBridge
{
    /// <summary>
    /// ブリッジからのテスト実行（開発補助）。<see cref="TestRunnerApi"/> を薄く包み、結果の件数と失敗内容を
    /// <see cref="EditorBridgeService"/> へ渡す。
    ///
    /// テスト実行は Editor の再読み込みをまたぐため、完了通知は「実行を開始した側」では受け取れない。
    /// そこで通知の受け口は読み込みのたびに登録し直す（<see cref="EnsureCallbacks"/>）。実行中かどうかは
    /// <see cref="EditorBridgeService"/> 側が <see cref="EditorPrefs"/> で覚えている。
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
                var failures = new List<string>();
                Collect(result, failures);

                double seconds = EditorApplication.timeSinceStartup - _startedAt;
                EditorBridgeService.OnTestRunFinished(
                    result.PassCount, result.FailCount, result.SkipCount, failures, seconds);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }

            /// <summary>失敗した「葉」のテストだけを集める（親ノードは子の失敗を繰り返すだけなので拾わない）。</summary>
            private static void Collect(ITestResultAdaptor node, List<string> failures)
            {
                if (node == null)
                {
                    return;
                }

                bool isLeaf = true;
                foreach (ITestResultAdaptor child in node.Children)
                {
                    isLeaf = false;
                    Collect(child, failures);
                }

                if (!isLeaf || node.TestStatus != TestStatus.Failed)
                {
                    return;
                }

                string message = string.IsNullOrEmpty(node.Message) ? "(メッセージなし)" : node.Message.Trim();
                string stack = FirstProjectFrame(node.StackTrace);
                failures.Add(node.FullName + "\n  " + message + (string.IsNullOrEmpty(stack) ? string.Empty : "\n  " + stack));
            }

            /// <summary>スタックのうちプロジェクト内の最初の行だけを残す（どのテストのどこで落ちたかが分かれば足りる）。</summary>
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
