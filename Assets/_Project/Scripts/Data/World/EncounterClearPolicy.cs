namespace Momotaro.Data.World
{
    /// <summary>
    /// 遭遇戦のクリア記録の扱い（P6A-00 の判断 1）。campaign（エリアカタログ）ごとに選ぶ。
    ///
    /// <b>既定は P5 の規則</b>（<see cref="PerRespawnCycle"/>）。P5 は「本編型死亡再開で通常 Encounter が復活する」
    /// ことを受入要求にしているので、既存の P5／P5.5 Data はこのまま。P6 のコアループは
    /// 「クリア済み遭遇戦は恒久」なので、P6 専用 campaign だけ <see cref="Permanent"/> にする
    /// （P6 仕様 §2「既存試遊 Data 非汚染」）。
    /// </summary>
    public enum EncounterClearPolicy
    {
        /// <summary>P5：クリアは再出現周期に紐付き、周期が進む（死亡再開）と未クリアへ戻る。</summary>
        PerRespawnCycle = 0,

        /// <summary>P6：クリアは恒久。死亡・休息・成長・旅立ちで復活しない（コアループ）。</summary>
        Permanent = 1,
    }
}
