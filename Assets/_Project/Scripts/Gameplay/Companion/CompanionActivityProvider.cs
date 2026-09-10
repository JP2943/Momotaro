using Momotaro.Gameplay.Modes;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の活動許可の供給元を差す 1 本の窓（P4-FIX F05）。既存の <see cref="GameModeProvider"/> と同じ形にしてある。
    ///
    /// <b>未設定のときの扱い</b>：供給元が居ないうちは、これまでどおり <see cref="GameModeProvider"/> だけを見て
    /// 判断する（<see cref="Fallback"/>）。仕様としては「未配線は安全側に停止」が正しいが、
    /// いきなり停止側へ倒すと、まだ活動 Context を置いていない既存 Scene で仲間が固まる。
    /// そのため<b>ここは移行期の措置</b>とし、P4-08R の専用 Scene Builder／Validator で
    /// 「活動 Context が置かれていること」を必須にした時点でこの穴を閉じる。
    /// 供給元そのもの（<see cref="CompanionActivityContext"/>）は未配線なら停止を返す。
    /// </summary>
    public static class CompanionActivityProvider
    {
        /// <summary>現在の供給元（未設定なら null）。</summary>
        public static ICompanionActivitySource Current { get; set; }

        /// <summary>いまの許可。供給元が無ければ <see cref="Fallback"/>。</summary>
        public static CompanionActivity Activity =>
            Current != null ? Current.Current : Fallback();

        /// <summary>
        /// 供給元が居ないときの判断（移行期。従来の <c>IsGameplayActive()</c> と同じ結論になるようにしてある）。
        /// 戦闘セッションを知らないので <see cref="CompanionActivity.EncounterActive"/> は立てない。
        /// </summary>
        public static CompanionActivity Fallback()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return CompanionActivity.FreeRoam; // 未初期化（単体テスト等）は従来どおり許可する。
            }

            return CompanionActivityResolver.Resolve(modes.Current, null);
        }
    }
}
