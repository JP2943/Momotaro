using UnityEngine;

namespace Momotaro.Core.Logging
{
    /// <summary>
    /// Unity Console へ出力する既定の <see cref="ILogSink"/> 実装。
    /// </summary>
    public sealed class UnityLogSink : ILogSink
    {
        /// <inheritdoc />
        public void Write(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                    Debug.LogError(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }
    }
}
