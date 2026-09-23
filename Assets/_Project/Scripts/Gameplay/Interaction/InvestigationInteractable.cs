using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// 犬丸の調査地点を、共通の Interact 候補として見せる Adapter（P5-04。仕様書 v1.1 §7.1）。
    ///
    /// <b>選択はしない。</b> どの地点が選ばれるかは共通窓口が決め、この Adapter は
    /// <b>自分が担当する 1 地点だけ</b>を代表する。依頼も
    /// <see cref="IExplicitInvestigationRequest.RequestAt"/> という
    /// 地点指定の狭い入口を通すので、選んだ地点と依頼される地点が食い違わない
    /// （§7.1「共通選択の後に別の地点へ依頼がすり替わる構造にしない」）。
    ///
    /// P4 の依頼・Snapshot・完了・中断・加入資格・Proxy はそのまま使う（§7.2）。
    /// ここが足すのは「共通窓口から呼ばれる形」だけで、調査の中身には触らない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvestigationInteractable : MonoBehaviour, IAreaInteractable
    {
        [Tooltip("担当する調査地点（同じ GameObject か、明示参照で差す）。")]
        [SerializeField] private MonoBehaviour _pointBehaviour;

        [Tooltip("このエリアの AreaId（AreaRoot の定義と揃える）。")]
        [SerializeField] private string _areaId = string.Empty;

        [Tooltip("依頼先（調停役。IExplicitInvestigationRequest として使う）。")]
        [SerializeField] private MonoBehaviour _requestBehaviour;

        private IInvestigationPoint _point;
        private IExplicitInvestigationRequest _request;

        /// <summary>担当する地点（診断・テスト用）。</summary>
        public IInvestigationPoint Point => _point ?? (_point = _pointBehaviour as IInvestigationPoint);

        /// <summary>依頼先（診断・テスト用）。</summary>
        public IExplicitInvestigationRequest Request =>
            _request ?? (_request = _requestBehaviour as IExplicitInvestigationRequest);

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(IInvestigationPoint point, IExplicitInvestigationRequest request, StableId areaId)
        {
            _point = point;
            _pointBehaviour = point as MonoBehaviour;
            _request = request;
            _requestBehaviour = request as MonoBehaviour;
            _areaId = areaId.Value ?? string.Empty;
        }

        /// <inheritdoc />
        public StableId InteractableId => Point != null ? Point.PointId : default;

        /// <inheritdoc />
        public StableId AreaId => string.IsNullOrEmpty(_areaId) ? default : new StableId(_areaId);

        /// <inheritdoc />
        public int FloorId => 0; // P5 は Floor 0 のみ（§3.1）。

        /// <inheritdoc />
        public Vector3 InteractionAnchor => Point != null ? Point.Position : transform.position;

        /// <summary>
        /// 地点固有の受付距離。<b>地点の設定の方が短ければそちらを使う</b>（§7.1）。
        /// 候補表示と受付が同じ値を参照するので、表示だけ出て押せない状態にならない。
        /// </summary>
        public float InteractionRadius => Point != null ? Point.Settings.InteractRange : 0f;

        /// <inheritdoc />
        public bool IsAvailable =>
            isActiveAndEnabled && Point != null && Point.IsAvailable && Request != null;

        /// <inheritdoc />
        public string Prompt => Point != null ? Point.Prompt : string.Empty;

        /// <inheritdoc />
        public AreaInteractionOutcome Interact()
        {
            if (Point == null || Request == null)
            {
                return AreaInteractionOutcome.Refused("調査の配線がありません。");
            }

            // 地点を指定して依頼する。調停役が最近傍を選び直す入口は通らない。
            InvestigationRequestResult result = Request.RequestAt(Point.PointId);
            if (result.Accepted)
            {
                return AreaInteractionOutcome.Accepted(Point.Prompt);
            }

            // 断られても押下は使い切る（次点の対象へ流さない。§7.1）。
            return AreaInteractionOutcome.Refused(RefusalText(result.Reason));
        }

        private string RefusalText(InvestigationRejectReason reason)
        {
            switch (reason)
            {
                case InvestigationRejectReason.AlreadyInvestigated:
                    return Point.CompletedText;
                case InvestigationRejectReason.CompanionNotRecruited:
                    return Point.MissingCompanionHint;
                default:
                    return "いまは調べられません。";
            }
        }

        private void OnEnable() => AreaInteractableRegistry.Register(this);

        private void OnDisable() => AreaInteractableRegistry.Unregister(this);
    }
}
