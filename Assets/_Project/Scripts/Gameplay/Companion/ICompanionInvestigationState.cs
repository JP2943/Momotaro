namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 探索の<b>利用中</b>状態（R3-03）。戦闘本体の状態（Down・Follow など）とは別に持つ。
    ///
    /// 本体で調べているあいだは <see cref="CompanionState.Investigate"/> が立つので状態だけで判定できるが、
    /// <b>表示代理（Proxy）で調べているあいだは戦闘本体の状態が探索を表さない</b>。代理は Down／Away／復帰待ちの
    /// 本体に触れずに動くため、Down の自然復帰時刻が来ると本体は Follow へ戻る。そのとき状態だけを見ている
    /// 追従・戦闘・防御は「もう通常どおり動いてよい」と判断し、<b>表示が抑制されたままの本体が歩き出す</b>
    /// （実際にその経路があった）。
    ///
    /// そこで「いま探索が走っているか」を状態とは独立に公開し、自動 Follow／Chase／Attack／Guard／Evade の
    /// 入口はこれも見る。HP・クールダウン・自然復帰の時計は<b>止めない</b>（探索は戦闘値に触れないという契約のまま）。
    /// </summary>
    public interface ICompanionInvestigationState
    {
        /// <summary>依頼を実行中か（本体・表示代理のどちらでも true）。</summary>
        bool IsInvestigationActive { get; }
    }
}
