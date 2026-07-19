using System.Collections.Generic;
using System.Text;

namespace Momotaro.Core.Logging
{
    /// <summary>
    /// プロジェクト共通のログ出力口。仕様書 11.11 に基づき、Category・対象ID・Scene を
    /// 付与して出力し、同一警告の毎フレーム重複を抑制する。
    ///
    /// 出力先は <see cref="ILogSink"/> により差し替え可能で、既定は Unity Console。
    /// <see cref="Info"/> は開発ビルド（UNITY_EDITOR / DEVELOPMENT_BUILD）でのみ呼び出しが
    /// 残り、製品ビルドではコンパイル時に除去される。
    /// </summary>
    public static class GameLog
    {
        private static ILogSink _sink = new UnityLogSink();

        // 同一キーの警告を一度だけ出すための記録。毎フレーム出力を防ぐ。
        private static readonly HashSet<string> _emittedKeys = new HashSet<string>();

        /// <summary>
        /// 出力先を差し替える。null を渡すと既定の Unity Console へ戻す。
        /// 主にテストでの検証に使用する。
        /// </summary>
        public static void SetSink(ILogSink sink)
        {
            _sink = sink ?? new UnityLogSink();
        }

        /// <summary>
        /// 重複警告の抑制記録をクリアする。Scene遷移時やテスト間で呼ぶ。
        /// </summary>
        public static void ResetSuppression()
        {
            _emittedKeys.Clear();
        }

        /// <summary>
        /// 情報ログ。開発ビルドでのみ出力され、製品ビルドでは呼び出しごと除去される。
        /// </summary>
        /// <param name="category">分類。</param>
        /// <param name="message">本文。</param>
        /// <param name="id">対象の Stable ID（任意）。</param>
        /// <param name="scene">対象 Scene 名（任意）。</param>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Info(LogCategory category, string message, string id = null, string scene = null)
        {
            _sink.Write(LogLevel.Info, Format(category, message, id, scene));
        }

        /// <summary>
        /// 警告ログ。常に出力される。
        /// </summary>
        public static void Warning(LogCategory category, string message, string id = null, string scene = null)
        {
            _sink.Write(LogLevel.Warning, Format(category, message, id, scene));
        }

        /// <summary>
        /// 同一 <paramref name="key"/> について一度だけ出力する警告。
        /// 毎フレーム発生し得る警告に使い、Console の氾濫を防ぐ。
        /// <paramref name="key"/> には定数など安定した文字列を渡すこと。
        /// </summary>
        /// <returns>今回実際に出力した場合は true。抑制された場合は false。</returns>
        public static bool WarningOnce(LogCategory category, string key, string message, string id = null, string scene = null)
        {
            string dedupeKey = (int)category + ":" + key;
            if (!_emittedKeys.Add(dedupeKey))
            {
                return false;
            }

            _sink.Write(LogLevel.Warning, Format(category, message, id, scene));
            return true;
        }

        /// <summary>
        /// エラーログ。常に出力される。
        /// </summary>
        public static void Error(LogCategory category, string message, string id = null, string scene = null)
        {
            _sink.Write(LogLevel.Error, Format(category, message, id, scene));
        }

        /// <summary>
        /// 出力行を整形する。書式は "[Category][id:xxx][scene:yyy] message"。
        /// id / scene が未指定の場合はその区画を省略する。
        /// </summary>
        internal static string Format(LogCategory category, string message, string id, string scene)
        {
            var sb = new StringBuilder(64);
            sb.Append('[').Append(category).Append(']');
            if (!string.IsNullOrEmpty(id))
            {
                sb.Append("[id:").Append(id).Append(']');
            }

            if (!string.IsNullOrEmpty(scene))
            {
                sb.Append("[scene:").Append(scene).Append(']');
            }

            sb.Append(' ').Append(message);
            return sb.ToString();
        }
    }
}
