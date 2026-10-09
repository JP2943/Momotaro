using Momotaro.Core.Identification;
using Momotaro.Gameplay.Story;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// 必須イベントで開く門の手前の立て札（P7 03。仕様 §7「条件不足なら理由を表示」）。調べると、イベントが未完了なら
    /// 理由（Data の <c>LockedNotice</c>）を返す。門そのものは既存の <see cref="AreaFlagDoor"/> で、開通はイベントの確定が記録する。
    /// 開通後は調べる対象から外れる（扉など先の対象の選択を邪魔しない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StoryGateNotice : MonoBehaviour, IAreaInteractable
    {
        [Tooltip("この門を開ける必須イベントの安定 ID。")]
        [SerializeField] private string _eventId = string.Empty;

        [Tooltip("所属 Area の安定 ID。")]
        [SerializeField] private string _areaId = string.Empty;

        [Tooltip("固有の受付距離。0 以下なら窓口の既定値。")]
        [SerializeField] private float _interactionRadius;

        /// <summary>必須イベント ID。</summary>
        public StableId EventId => string.IsNullOrEmpty(_eventId) ? default : new StableId(_eventId);

        /// <summary>調べられた回数（診断・テスト用）。</summary>
        public int InspectCount { get; private set; }

        /// <summary>設定（Builder 用）。</summary>
        public void Bind(StableId eventId, StableId areaId, float radius = 0f)
        {
            _eventId = eventId.Value ?? string.Empty;
            _areaId = areaId.Value ?? string.Empty;
            _interactionRadius = radius;
        }

        /// <inheritdoc />
        public StableId InteractableId =>
            string.IsNullOrEmpty(_eventId) ? default : new StableId("gate_notice_" + _eventId);

        /// <inheritdoc />
        public StableId AreaId => string.IsNullOrEmpty(_areaId) ? default : new StableId(_areaId);

        /// <inheritdoc />
        public int FloorId => 0;

        /// <inheritdoc />
        public Vector3 InteractionAnchor => transform.position;

        /// <inheritdoc />
        public float InteractionRadius => _interactionRadius;

        /// <inheritdoc />
        public bool IsAvailable
        {
            get
            {
                IDialogueOperations operations = DialogueOperationsProvider.Current;
                return isActiveAndEnabled && !EventId.IsEmpty && operations != null && !operations.IsEventCompleted(EventId);
            }
        }

        /// <inheritdoc />
        public string Prompt => "門を調べる";

        /// <inheritdoc />
        public AreaInteractionOutcome Interact()
        {
            IDialogueOperations operations = DialogueOperationsProvider.Current;
            if (operations == null || EventId.IsEmpty)
            {
                return AreaInteractionOutcome.Refused("門の配線がありません。");
            }

            InspectCount++;
            return operations.OnGateInspected(EventId);
        }

        private void OnEnable() => AreaInteractableRegistry.Register(this);

        private void OnDisable() => AreaInteractableRegistry.Unregister(this);
    }
}
