using System.Text.RegularExpressions;

namespace Momotaro.Core.Identification
{
    /// <summary>
    /// Stable ID の書式規約（命名・データ規約 3 章）。
    /// 小文字 snake_case のみ許可し、表示名・日本語・空白・大文字を含まない。
    /// 先頭は英小文字、以降は英小文字・数字・アンダースコア。
    /// </summary>
    public static class StableIdFormat
    {
        /// <summary>ID の最大長。</summary>
        public const int MaxLength = 64;

        // 先頭は英小文字、以降 [a-z0-9_]。連続・末尾アンダースコアは許可するが先頭数字は不可。
        private static readonly Regex Pattern =
            new Regex("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// 書式が規約に適合するか判定する。
        /// </summary>
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            {
                return false;
            }

            return Pattern.IsMatch(value);
        }
    }
}
