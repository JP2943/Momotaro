using Momotaro.Gameplay.Modes;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の活動許可の供給元を差す 1 本の窓（P4-FIX F05）。既存の <see cref="GameModeProvider"/> と同じ形にしてある。
    ///
    /// <b>未設定のときの扱い</b>：供給元が居ないうちは、これまでどおり <see cref="GameModeProvider"/> だけを見て
    /// 判断する（<see cref="Fallback"/>）。仕様としては「未配線は安全側に停止」が正しいが、
    /// いきなり停止側へ倒すと、まだ活動 Context を置いていない既存 Scene で仲間が固まる。
    /// <b>P4-08R での決着。</b>フォールバックは<b>残した</b>。停止側へ倒すと、自前で配線している
    /// 大量の EditMode テストが一斉に動かなくなり、テストを直す作業が実装の検証より大きくなる。
    /// 代わりに次の 2 つで「出荷される構成では通らない」ことを機械で示す。
    /// <list type="number">
    /// <item><description>Scene Validator（Editor 側）が活動 Context の存在と配線を必須にする。
    /// Builder が作る Scene には必ず入っている。</description></item>
    /// <item><description><see cref="FallbackCount"/> を数え、Builder 産の構成を Play したとき
    /// 0 のままであることを PlayMode テストが確かめる。</description></item>
    /// </list>
    /// 供給元そのもの（<see cref="CompanionActivityContext"/>）は未配線なら停止を返す。
    /// </summary>
    public static class CompanionActivityProvider
    {
        /// <summary>現在の供給元（未設定なら null）。</summary>
        public static ICompanionActivitySource Current { get; set; }

        /// <summary>
        /// 移行期のフォールバックが使われた回数（P4-08R）。
        ///
        /// <b>穴を塞いだと言うために数える。</b>フォールバックそのものは残してある（理由は下記）。
        /// 残す以上、「出荷される構成では通っていない」ことを言葉でなく機械で示す必要がある。
        /// Builder が作る Scene ではここが 0 のままであることを Play で確かめる。
        /// </summary>
        public static int FallbackCount { get; private set; }

        /// <summary>フォールバックの計数を初期化する（テスト・Scene 再構築）。</summary>
        public static void ResetFallbackCount() => FallbackCount = 0;

        /// <summary>いまの許可。供給元が無ければ <see cref="Fallback"/>。</summary>
        public static CompanionActivity Activity =>
            Current != null ? Current.Current : Fallback();

        /// <summary>
        /// 供給元が居ないときの判断（移行期。従来の <c>IsGameplayActive()</c> と同じ結論になるようにしてある）。
        /// 戦闘セッションを知らないので <see cref="CompanionActivity.EncounterActive"/> は立てない。
        /// </summary>
        public static CompanionActivity Fallback()
        {
            FallbackCount++;

            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return CompanionActivity.FreeRoam; // 未初期化（単体テスト等）は従来どおり許可する。
            }

            return CompanionActivityResolver.Resolve(modes.Current, null);
        }
    }
}
