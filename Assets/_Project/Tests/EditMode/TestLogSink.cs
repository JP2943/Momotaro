using System.Collections.Generic;
using Momotaro.Core.Logging;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// テスト用のログ出力先。出力された行を捕捉して検証に使う。
    /// </summary>
    internal sealed class TestLogSink : ILogSink
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new List<(LogLevel, string)>();

        public void Write(LogLevel level, string message)
        {
            Entries.Add((level, message));
        }

        public int CountOf(LogLevel level)
        {
            int n = 0;
            foreach (var e in Entries)
            {
                if (e.Level == level)
                {
                    n++;
                }
            }

            return n;
        }
    }
}
