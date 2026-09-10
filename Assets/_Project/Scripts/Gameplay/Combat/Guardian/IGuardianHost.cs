namespace Momotaro.Gameplay.Combat.Guardian
{
    /// <summary>
    /// 守護される側（主人公）が「誰に守ってもらうか」の判断先を差し替えられることを表す契約（P4-05）。
    ///
    /// 守護者側（仲間）は本契約だけを見て自分を登録する。仲間が主人公の具象型を知る必要が無くなり、
    /// 層の向き（Companion → Player の直接依存）を作らずに済む。
    ///
    /// <b>解除は「差し替え」ではなく専用の <see cref="ClearGuardianResolver"/> で行う。</b>
    /// 解除を <c>SetGuardianResolver(null)</c> で表すと、後から登録した別の守護者の登録を
    /// 先に退場した守護者が消してしまう（登録先は 1 つしか無いため）。実際、犬丸が 1 体だけの現状では
    /// 表に出ないが、猿若・雉代が加わった瞬間に「誰も庇わない」状態が無言で発生する。
    /// 解除する側が<b>自分が登録されている場合だけ</b>外せるよう、期待する登録者を渡す形にする。
    /// </summary>
    public interface IGuardianHost
    {
        /// <summary>守護判断先を差し替える（登録・交代）。</summary>
        void SetGuardianResolver(IGuardianResolver resolver);

        /// <summary>
        /// 守護判断先を解除する。<paramref name="expected"/> が現在の登録者と<b>同一のときだけ</b>外す。
        /// 別の守護者に差し替わっていれば何もしない。
        /// </summary>
        /// <param name="expected">自分が登録したと思っている判断先。</param>
        void ClearGuardianResolver(IGuardianResolver expected);
    }
}
