namespace Momotaro.Core.Logging
{
    /// <summary>
    /// ログの出力先を抽象化するインターフェース。
    /// 既定では Unity Console へ出力するが、テストでは差し替えて出力を検証できる。
    /// </summary>
    public interface ILogSink
    {
        /// <summary>
        /// 整形済みのログ行を出力する。
        /// </summary>
        /// <param name="level">重大度。</param>
        /// <param name="message">整形済みメッセージ。</param>
        void Write(LogLevel level, string message);
    }
}
