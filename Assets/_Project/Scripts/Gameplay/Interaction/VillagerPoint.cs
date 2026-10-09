using Momotaro.Core.Identification;
using Momotaro.Gameplay.Story;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// 住民（P7 01。仕様 §3）。静止した非戦闘キャラクターで、既存の「調べる」と対象選択（<see cref="AreaInteractionSelector"/>）に乗る。
    /// 画面に示す対象と実際に会話を始める対象は、同じ選択規則で決まるので一致する。
    ///
    /// 会話の判断・停止・保存はここでは行わない。常駐の会話サービス（<see cref="DialogueOperationsProvider"/>）へ渡すだけ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VillagerPoint : MonoBehaviour, IAreaInteractable
    {
        [Tooltip("住民の安定 ID（campaign の会話 Data の VillagerDefinition と同じ）。")]
        [SerializeField] private string _villagerId = string.Empty;

        [Tooltip("所属 Area の安定 ID。")]
        [SerializeField] private string _areaId = string.Empty;

        [Tooltip("表示名（調べるの案内に使う。Data の表示名と同じにする）。")]
        [SerializeField] private string _displayName = string.Empty;

        [Tooltip("固有の受付距離。0 以下なら窓口の既定値。")]
        [SerializeField] private float _interactionRadius;

        /// <summary>住民 ID。</summary>
        public StableId VillagerId => string.IsNullOrEmpty(_villagerId) ? default : new StableId(_villagerId);

        /// <summary>調べられた回数（診断・テスト用）。</summary>
        public int InteractCount { get; private set; }

        /// <summary>設定（Builder 用）。</summary>
        public void Bind(StableId villagerId, StableId areaId, string displayName, float radius = 0f)
        {
            _villagerId = villagerId.Value ?? string.Empty;
            _areaId = areaId.Value ?? string.Empty;
            _displayName = displayName ?? string.Empty;
            _interactionRadius = radius;
        }

        /// <inheritdoc />
        public StableId InteractableId =>
            string.IsNullOrEmpty(_villagerId) ? default : new StableId("villager_point_" + _villagerId);

        /// <inheritdoc />
        public StableId AreaId => string.IsNullOrEmpty(_areaId) ? default : new StableId(_areaId);

        /// <inheritdoc />
        public int FloorId => 0;

        /// <inheritdoc />
        public Vector3 InteractionAnchor => transform.position;

        /// <inheritdoc />
        public float InteractionRadius => _interactionRadius;

        /// <inheritdoc />
        public bool IsAvailable => isActiveAndEnabled && !VillagerId.IsEmpty && DialogueOperationsProvider.Current != null;

        /// <inheritdoc />
        public string Prompt => string.IsNullOrEmpty(_displayName) ? "話しかける" : _displayName + "に話しかける";

        /// <inheritdoc />
        public AreaInteractionOutcome Interact()
        {
            IDialogueOperations operations = DialogueOperationsProvider.Current;
            if (!IsAvailable || operations == null)
            {
                return AreaInteractionOutcome.Refused("会話の配線がありません。");
            }

            InteractCount++;
            return operations.OnVillagerInteracted(VillagerId, AreaId);
        }

        private void OnEnable() => AreaInteractableRegistry.Register(this);

        private void OnDisable() => AreaInteractableRegistry.Unregister(this);
    }
}
