namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の活動許可の供給元を差す 1 本の窓（P4-FIX F05）。既存の <c>GameModeProvider</c> と同じ形にしてある。
    ///
    /// <b>未設定なら停止</b>（P4-FIX-R2。v1.0 §7.2「未配線の本番／試遊 Scene は安全側に停止し、Validator でエラーにする。
    /// テストは明示的な Fake の活動 Context を注入する」）。
    ///
    /// 以前は供給元が無いとき <c>GameModeProvider</c> だけを見て許可側へ倒すフォールバックがあり、
    /// 「既存テストを直すコストを避けて残した」と書いていた（レビュー R2-08）。それは実装の許可条件をテストの都合で
    /// 緩めたということで、Context の欠落・無効化・Scene 切替の瞬間に仲間が動き出す穴だった。
    /// 供給元が無い＝何も分からない、なので何も許さない。テスト側は <c>CompanionActivityTestSource</c> 相当の
    /// 明示 Fake を共通 Fixture から注入する。
    /// </summary>
    public static class CompanionActivityProvider
    {
        /// <summary>現在の供給元（未設定なら null）。</summary>
        public static ICompanionActivitySource Current { get; set; }

        /// <summary>いまの許可。供給元が無ければ <see cref="CompanionActivity.Stopped"/>。</summary>
        public static CompanionActivity Activity =>
            Current != null ? Current.Current : CompanionActivity.Stopped;

        /// <summary>供給元が差さっているか（診断・テスト用）。</summary>
        public static bool HasSource => Current != null;
    }
}
