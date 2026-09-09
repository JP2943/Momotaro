namespace Momotaro.Gameplay.Combat.Guardian
{
    /// <summary>
    /// 守護される側（主人公）が「誰に守ってもらうか」の判断先を差し替えられることを表す契約（P4-05）。
    ///
    /// 守護者側（仲間）は本契約だけを見て自分を登録する。仲間が主人公の具象型を知る必要が無くなり、
    /// 層の向き（Companion → Player の直接依存）を作らずに済む。
    /// </summary>
    public interface IGuardianHost
    {
        /// <summary>守護判断先を差し替える（null で解除）。</summary>
        void SetGuardianResolver(IGuardianResolver resolver);
    }
}
