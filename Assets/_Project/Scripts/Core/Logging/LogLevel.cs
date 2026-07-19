namespace Momotaro.Core.Logging
{
    /// <summary>
    /// ログの重大度。Unity の Debug.Log／LogWarning／LogError に対応する。
    /// </summary>
    public enum LogLevel
    {
        /// <summary>情報。開発ビルドでのみ出力される。</summary>
        Info = 0,

        /// <summary>警告。常に出力される。</summary>
        Warning = 1,

        /// <summary>エラー。常に出力される。</summary>
        Error = 2,
    }
}
