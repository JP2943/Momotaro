using Momotaro.Gameplay.Companion;

namespace Momotaro.Tests.Support
{
    /// <summary>
    /// テストが明示的に注入する活動 Context の Fake（P4-FIX-R2。v1.0 §7.2「テストは明示的な Fake の活動 Context を注入する」）。
    ///
    /// 実装側（<see cref="CompanionActivityProvider"/>）は供給元が無ければ停止する。以前はそこに許可側のフォールバックが
    /// あり、テストはそれに黙って乗っていた。いまはテストが「この状況を与える」と書く。
    /// 既定は自由探索（<see cref="CompanionActivity.FreeRoam"/>）で、共通 Fixture（<see cref="CompanionActivityFixtureAttribute"/>）が
    /// 各テストの前に差し込む。停止・戦闘中・Pause を作りたいテストは <see cref="Current"/> を書き換えるか、
    /// <see cref="CompanionActivityProvider.Current"/> を null にして「供給元が無い」を作る。
    /// </summary>
    public sealed class CompanionActivityTestSource : ICompanionActivitySource
    {
        /// <summary>いま返す許可。</summary>
        public CompanionActivity Current { get; set; }

        public CompanionActivityTestSource(CompanionActivity activity)
        {
            Current = activity;
        }

        /// <summary>供給元として差し込み、その Fake を返す（テスト本文から状況を切り替えるため）。</summary>
        public static CompanionActivityTestSource Install(CompanionActivity activity)
        {
            var source = new CompanionActivityTestSource(activity);
            CompanionActivityProvider.Current = source;
            return source;
        }

        /// <summary>自由探索の Fake を差し込む。</summary>
        public static CompanionActivityTestSource InstallFreeRoam() => Install(CompanionActivity.FreeRoam);

        /// <summary>戦闘中の Fake を差し込む。</summary>
        public static CompanionActivityTestSource InstallFighting() => Install(CompanionActivity.Fighting);

        /// <summary>
        /// いま差さっている供給元がこの型の Fake ならそれを返す（共通 Fixture が差した既定を、テストから書き換えるため）。
        /// 実 Context が差さっていれば null。
        /// </summary>
        public static CompanionActivityTestSource CurrentFake => CompanionActivityProvider.Current as CompanionActivityTestSource;
    }
}
