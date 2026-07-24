namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// HUD 表示のための純粋ヘルパ（Phase2 P2-11）。表示値を範囲へ Clamp して、内部状態が一時的に範囲外でも UI が
    /// 負値や上限超過を出さないようにする（表示のみ。Gameplay 値は変更しない）。
    /// </summary>
    public static class HudDisplay
    {
        /// <summary>整数値を 0..max へ Clamp する（max &lt; 0 は 0 とみなす）。</summary>
        public static int Clamp(int value, int max)
        {
            int upper = max < 0 ? 0 : max;
            if (value < 0)
            {
                return 0;
            }

            return value > upper ? upper : value;
        }
    }
}
