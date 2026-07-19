namespace Momotaro.Core.Logging
{
    /// <summary>
    /// ログの分類カテゴリ。仕様書 11.11 のログ分類に対応する。
    /// フィルタリングと検索性のため、状態を文字列でなく列挙で表す。
    /// </summary>
    public enum LogCategory
    {
        /// <summary>基盤・共通処理。</summary>
        Core = 0,

        /// <summary>起動・初期化（Bootstrap）。</summary>
        Boot = 1,

        /// <summary>戦闘。</summary>
        Combat = 2,

        /// <summary>敵・仲間などのAI。</summary>
        AI = 3,

        /// <summary>会話。</summary>
        Dialogue = 4,

        /// <summary>イベントシーケンス。</summary>
        Event = 5,

        /// <summary>セーブ・ロード。</summary>
        Save = 6,

        /// <summary>Scene遷移・読込。</summary>
        Scene = 7,

        /// <summary>サウンド。</summary>
        Audio = 8,

        /// <summary>データ・参照の検証。</summary>
        Validation = 9,

        /// <summary>入力。</summary>
        Input = 10,
    }
}
