namespace Momotaro.Gameplay.Transfer
{
    /// <summary>
    /// Snapshot の値域検証（P5-03a。仕様書 v1.1 §4.5「全 Snapshot を先に検証してから適用し、
    /// NaN・Infinity・負の残り時間・HP と Down の矛盾等で部分適用したまま活動させない」）。
    ///
    /// 各 Import の先頭でここを通す。<b>黙って丸めない</b>のが要点で、
    /// 不正値を 0 や全回復へ置換すると「壊れた Snapshot でも動いてしまう」ため検出できなくなる。
    /// </summary>
    public static class TransferValue
    {
        /// <summary>残り時間として妥当か（有限かつ 0 以上）。</summary>
        public static bool IsValidRemaining(float seconds)
        {
            return !float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds >= 0f;
        }

        /// <summary>蓄積値として妥当か（有限かつ 0 以上）。</summary>
        public static bool IsValidAccumulation(float value)
        {
            return IsValidRemaining(value);
        }

        /// <summary>HP として妥当か（0 以上、上限以下）。上限は Data から構築済みの現在値を渡す。</summary>
        public static bool IsValidHp(int hp, int max)
        {
            return hp >= 0 && hp <= max;
        }
    }
}
