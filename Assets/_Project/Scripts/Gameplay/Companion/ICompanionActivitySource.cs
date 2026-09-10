namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の各駆動系が「いま何を許されているか」を読む窓口（P4-FIX F05）。
    ///
    /// 各コンポーネントが <c>GameModeProvider</c> を直接読んで独自に戦闘判定を組むと、必ず食い違う
    /// （実際、追従だけゲートが無く、Pause でも歩き続けていた）。読む先を 1 つにして、
    /// テストは Fake を注入するだけで任意の状況を作れるようにする。
    /// </summary>
    public interface ICompanionActivitySource
    {
        /// <summary>現在の許可。</summary>
        CompanionActivity Current { get; }
    }
}
