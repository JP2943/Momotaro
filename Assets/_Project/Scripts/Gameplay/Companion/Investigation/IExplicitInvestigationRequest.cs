using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// <b>地点を指定してだけ</b>調査を依頼できる狭い入口（P5-04。仕様書 v1.1 §7.1）。
    ///
    /// P5 の Adapter にはこれを渡す。理由は「共通選択の後に別の地点へ依頼がすり替わる構造にしない」ため。
    /// 引数なしの <see cref="InvestigationCoordinator.TryRequest"/> は内部で最近傍を選び直すので、
    /// 単一選択窓口が選んだ地点と実際に依頼される地点が食い違いうる。
    /// その入口をそもそも<b>見せない</b>のがこの型の役目で、呼べないものは呼び間違えようがない。
    ///
    /// 指定された地点は、依頼の直前に<b>もう一度検証する</b>（距離・遮蔽・調査済み・加入）。
    /// 選んだ瞬間と押した瞬間の間に状況が変わっていても、古い判断のまま実行しない。
    /// </summary>
    public interface IExplicitInvestigationRequest
    {
        /// <summary>指定した地点へ依頼する。受理・拒否のどちらも通知を 1 回出す。</summary>
        InvestigationRequestResult RequestAt(StableId pointId);

        /// <summary>依頼を出さずに、指定した地点がいま依頼できるかを見る（表示用。副作用なし）。</summary>
        InvestigationRejectReason PeekAt(StableId pointId, out IInvestigationPoint point);
    }
}
