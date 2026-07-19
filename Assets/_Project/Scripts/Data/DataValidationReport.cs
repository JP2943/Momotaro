using System.Collections.Generic;

namespace Momotaro.Data
{
    /// <summary>
    /// データ検証の結果を収集する器。エラーと警告を分けて蓄積する。
    /// エラーが 1 件でもあれば本番ビルドを止める判断に使う（仕様書 11.11）。
    /// </summary>
    public sealed class DataValidationReport
    {
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        /// <summary>蓄積されたエラー。</summary>
        public IReadOnlyList<string> Errors => _errors;

        /// <summary>蓄積された警告。</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        /// <summary>エラーが 1 件以上あるか。</summary>
        public bool HasErrors => _errors.Count > 0;

        /// <summary>エラーを追加する。</summary>
        public void Error(string message)
        {
            _errors.Add(message);
        }

        /// <summary>警告を追加する。</summary>
        public void Warning(string message)
        {
            _warnings.Add(message);
        }
    }
}
