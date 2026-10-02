using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// お地蔵様（P6A-03。仕様 §5）。既存の単一 Interact 経路（<see cref="AreaInteractionController"/>）で調べられる。
    ///
    /// <b>ここは「調べた」と伝えるだけ。</b> 登録・保存・メニューは常駐の campaign サービス（<see cref="IShrineOperations"/>）が行う。
    /// 調べただけでは HP・残数・敵状態を変えない（休息・成長・旅立ちは明示操作）。
    /// 復帰点の位置は所属 Area の入口（<c>AreaEntryPoint</c>）が持ち、ここは見た目と受付の基準点だけ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShrinePoint : MonoBehaviour, IAreaInteractable
    {
        [Tooltip("お地蔵様の安定 ID（カタログの ShrineDefinition と同じ）。")]
        [SerializeField] private string _shrineId = string.Empty;

        [Tooltip("所属 Area の安定 ID。")]
        [SerializeField] private string _areaId = string.Empty;

        [Tooltip("固有の受付距離。0 以下なら窓口の既定値。")]
        [SerializeField] private float _interactionRadius;

        /// <summary>お地蔵様の安定 ID。</summary>
        public StableId ShrineId => string.IsNullOrEmpty(_shrineId) ? default : new StableId(_shrineId);

        /// <summary>調べられた回数（診断・テスト用）。</summary>
        public int InteractCount { get; private set; }

        /// <summary>配線する（Builder）。</summary>
        public void Bind(StableId shrineId, StableId areaId, float radius = 0f)
        {
            _shrineId = shrineId.Value ?? string.Empty;
            _areaId = areaId.Value ?? string.Empty;
            _interactionRadius = radius;
        }

        /// <inheritdoc />
        public StableId InteractableId =>
            string.IsNullOrEmpty(_shrineId) ? default : new StableId("shrine_point_" + _shrineId);

        /// <inheritdoc />
        public StableId AreaId => string.IsNullOrEmpty(_areaId) ? default : new StableId(_areaId);

        /// <inheritdoc />
        public int FloorId => 0;

        /// <inheritdoc />
        public Vector3 InteractionAnchor => transform.position;

        /// <inheritdoc />
        public float InteractionRadius => _interactionRadius;

        /// <inheritdoc />
        public bool IsAvailable => isActiveAndEnabled && !ShrineId.IsEmpty && ShrineOperationsProvider.Current != null;

        /// <inheritdoc />
        public string Prompt => "お地蔵様を調べる";

        /// <inheritdoc />
        public AreaInteractionOutcome Interact()
        {
            IShrineOperations operations = ShrineOperationsProvider.Current;
            if (!IsAvailable || operations == null)
            {
                return AreaInteractionOutcome.Refused("お地蔵様の配線がありません。");
            }

            InteractCount++;
            return operations.OnShrineInteracted(ShrineId);
        }

        private void OnEnable() => AreaInteractableRegistry.Register(this);

        private void OnDisable() => AreaInteractableRegistry.Unregister(this);
    }
}
