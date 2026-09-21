namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 行動の持ち主として調停役（<see cref="CompanionStateArbiter"/>）に登録し、<b>自分の行動が奪われた瞬間</b>に
    /// 同期的に呼ばれる側の契約（P4-FIX-R2。v1.0 §8.2「割込み：旧行動を停止してから新行動を開始」、
    /// c8c0ddf §2.4「受理確定 → 旧攻撃／防御を中断 → 被害解決」）。
    ///
    /// これが無かったころは、守護の成立で状態が Protect に変わっても、攻撃側は次の Tick まで判定を出し続け、
    /// 防御側はガード能力を握ったままだった（転送された命中を、解除すべき旧ガードが防いでしまう）。
    /// 状態の書き換えと、判定・能力・移動の停止は<b>同じ呼び出しの中</b>で終わらなければならない。
    ///
    /// 呼ばれた側がやってよいのは<b>自分の判定・能力・移動指示を止めること</b>だけ。状態は変えない
    /// （状態は奪った側のもの）。冪等であること（同じ券について 2 回呼ばれても壊れない）。
    /// </summary>
    public interface ICompanionActionParticipant
    {
        /// <summary>
        /// 自分の行動が割込み・被弾・退場・初期化で無効になった。<paramref name="lost"/> は無効になった券。
        /// </summary>
        void OnActionInterrupted(in CompanionActionHandle lost);
    }
}
