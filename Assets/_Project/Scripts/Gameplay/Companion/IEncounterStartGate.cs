namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 「戦闘開始が要求されたか」の読み取り契約（P4-08R。v1.0 §7.1・§13.1）。
    /// P4 専用 Scene は起動直後を自由探索の段階とし、明示の開始操作で Encounter を起動する。
    /// 活動 Context はこれを読んで、要求前の Preparing を自由探索として供給する。
    /// </summary>
    public interface IEncounterStartGate
    {
        bool EncounterRequested { get; }
    }
}
