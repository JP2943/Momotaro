namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// サービス初期化の結果。成功可否と、失敗時に起動を停止すべきか（Critical か）を明示する。
    /// 仕様書 15.7「各Serviceは初期化完了を返し、失敗時の続行・停止を明示する」に対応。
    /// </summary>
    public readonly struct ServiceInitResult
    {
        /// <summary>初期化に成功したか。</summary>
        public bool Success { get; }

        /// <summary>失敗時に起動を停止すべき重要サービスか。成功時は無意味。</summary>
        public bool IsCritical { get; }

        /// <summary>ログ用の補足メッセージ。</summary>
        public string Message { get; }

        private ServiceInitResult(bool success, bool isCritical, string message)
        {
            Success = success;
            IsCritical = isCritical;
            Message = message;
        }

        /// <summary>成功結果を生成する。</summary>
        public static ServiceInitResult Ok(string message = null)
        {
            return new ServiceInitResult(true, false, message);
        }

        /// <summary>
        /// 失敗結果を生成する。
        /// </summary>
        /// <param name="message">失敗理由。</param>
        /// <param name="isCritical">true なら以降の初期化を止め、Launcher へ遷移しない。</param>
        public static ServiceInitResult Fail(string message, bool isCritical)
        {
            return new ServiceInitResult(false, isCritical, message);
        }
    }
}
