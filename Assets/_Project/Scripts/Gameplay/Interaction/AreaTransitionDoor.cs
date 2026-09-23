using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// Interact 1 回で別エリアへの遷移を要求する扉（P5-04。仕様書 v1.1 §6.1 の 2 行目）。
    ///
    /// 開放出入口（<c>AreaExitGate</c>）が「出口方向へ 0.15 秒連続入力」で要求するのに対し、
    /// 扉は<b>候補表示中の押下 1 回</b>で要求する。どちらも要求するだけで、
    /// 受付条件（AreaReady・探索中・主人公の状態）は遷移サービスが見る。
    ///
    /// <b>ここから遷移サービスを呼ばない。</b> Gameplay は Infrastructure を参照しないので、
    /// 要求を<b>置いておく</b>だけにして、駆動側（<c>AreaExitGateDriver</c>）が取りに来る。
    /// 出入口と同じ形にそろえてあるので、駆動の経路は 1 本のままで済む。
    ///
    /// 要求は<b>溜めない</b>。取りに来られる前にもう一度押されても、要求は 1 件のまま
    /// （同じ扉を連打しても遷移が 2 回積まれない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTransitionDoor : MonoBehaviour, IAreaInteractable
    {
        [Tooltip("この扉の安定 ID（同距離のときの順位にも使う）。")]
        [SerializeField] private string _doorId = string.Empty;

        [Tooltip("この扉があるエリア。")]
        [SerializeField] private string _areaId = string.Empty;

        [Tooltip("行き先のエリア。")]
        [SerializeField] private StableId _destinationAreaId;

        [Tooltip("行き先の入口。")]
        [SerializeField] private StableId _destinationEntryId;

        [Tooltip("距離・遮蔽の基準点（未配線なら自分の位置）。")]
        [SerializeField] private Transform _anchor;

        [Tooltip("固有の受付距離。0 以下なら窓口の既定値を使う。")]
        [SerializeField] private float _interactionRadius;

        private bool _pending;

        /// <summary>行き先のエリア。</summary>
        public StableId DestinationAreaId => _destinationAreaId;

        /// <summary>行き先の入口。</summary>
        public StableId DestinationEntryId => _destinationEntryId;

        /// <summary>要求を出した回数（診断・テスト用）。</summary>
        public int RequestCount { get; private set; }

        /// <summary>取りに来られていない要求があるか（診断・テスト用）。</summary>
        public bool HasPendingRequest => _pending;

        /// <summary>配線する（Builder・テストが呼ぶ）。</summary>
        public void Configure(
            StableId doorId, StableId areaId, StableId destinationAreaId, StableId destinationEntryId,
            Transform anchor = null)
        {
            _doorId = doorId.Value ?? string.Empty;
            _areaId = areaId.Value ?? string.Empty;
            _destinationAreaId = destinationAreaId;
            _destinationEntryId = destinationEntryId;
            _anchor = anchor;
        }

        /// <summary>置かれた要求を 1 件だけ取り出す（駆動側が呼ぶ）。</summary>
        public bool ConsumePendingRequest()
        {
            if (!_pending)
            {
                return false;
            }

            _pending = false;
            return true;
        }

        /// <inheritdoc />
        public StableId InteractableId => string.IsNullOrEmpty(_doorId) ? default : new StableId(_doorId);

        /// <inheritdoc />
        public StableId AreaId => string.IsNullOrEmpty(_areaId) ? default : new StableId(_areaId);

        /// <inheritdoc />
        public int FloorId => 0; // P5 は Floor 0 のみ（§3.1）。

        /// <inheritdoc />
        public Vector3 InteractionAnchor => _anchor != null ? _anchor.position : transform.position;

        /// <inheritdoc />
        public float InteractionRadius => _interactionRadius;

        /// <inheritdoc />
        public bool IsAvailable =>
            isActiveAndEnabled && !_destinationAreaId.IsEmpty && !_destinationEntryId.IsEmpty;

        /// <inheritdoc />
        public string Prompt => "扉をくぐる";

        /// <inheritdoc />
        public AreaInteractionOutcome Interact()
        {
            if (!IsAvailable)
            {
                return AreaInteractionOutcome.Refused("扉の行き先が未配線です。");
            }

            if (_pending)
            {
                // すでに要求済み。押下は使い切るが、要求は積まない。
                return AreaInteractionOutcome.Refused("移動を準備しています。");
            }

            _pending = true;
            RequestCount++;
            return AreaInteractionOutcome.Accepted(Prompt);
        }

        private void OnEnable() => AreaInteractableRegistry.Register(this);

        private void OnDisable()
        {
            AreaInteractableRegistry.Unregister(this);

            // 置いたままの要求を次の Scene へ持ち越さない。
            _pending = false;
        }
    }
}
