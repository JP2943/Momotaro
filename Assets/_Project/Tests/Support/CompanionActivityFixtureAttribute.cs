using Momotaro.Gameplay.Companion;
using NUnit.Framework;

namespace Momotaro.Tests.Support
{
    /// <summary>
    /// 仲間を組み立てるテストの共通 Fixture（P4-FIX-R2）。継承すると、各テストの<b>前</b>に自由探索の Fake
    /// （<see cref="CompanionActivityTestSource"/>）を活動 Context の供給元として差し込み、<b>後</b>に外す。
    ///
    /// なぜ共通にするか。実装は「供給元が無ければ停止」（v1.0 §7.2）なので、仲間の駆動を Tick するテストは
    /// 例外なく供給元を要る。各 Fixture に同じ SetUp を写すと、書き忘れたテストが「仲間が動かない」形で落ちるか、
    /// 逆に実装へ許可側のフォールバックを足したくなる（それが R2-08 の穴だった）。
    /// 後片付けを共通にするのは、Fake や実 Context がテストをまたいで残ると後続テストの結果が変わるため。
    ///
    /// assembly レベルの <c>ITestAction</c> は Unity の Test Runner では各テストに適用されなかった（実測）ので、
    /// 継承による NUnit の SetUp／TearDown で行う。基底の SetUp は派生の SetUp より先に、基底の TearDown は
    /// 派生の TearDown より後に走る。
    ///
    /// 「供給元が無い」を検査するテストは、本文で <see cref="CompanionActivityProvider.Current"/> を null にする。
    /// 実 Context（<see cref="CompanionActivityContext"/>）を有効化するテストは、その OnEnable が Fake を上書きする。
    /// </summary>
    public abstract class CompanionActivityFixture
    {
        /// <summary>この Fixture が直近に差し込んだ Fake（テスト本文から状況を切り替えるため）。</summary>
        protected CompanionActivityTestSource ActivityFake { get; private set; }

        [SetUp]
        public void InstallCompanionActivityFake()
        {
            ActivityFake = CompanionActivityTestSource.InstallFreeRoam();
        }

        [TearDown]
        public void RemoveCompanionActivityFake()
        {
            // 自分が差した Fake でも、テストが差し替えた別物でも、テストをまたいで残さない。
            CompanionActivityProvider.Current = null;
            ActivityFake = null;
        }
    }
}
