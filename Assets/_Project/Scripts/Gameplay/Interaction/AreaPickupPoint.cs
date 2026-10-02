using Momotaro.Core.Identification;
using Momotaro.Data.Progression;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// 配置物・宝箱・発見（P6A-01。仕様 §4 末尾・§7）。既存の単一 Interact 経路で取る。
    ///
    /// <b>取得記録と所持数（と徳）を同じ更新で確定する</b>（<see cref="GameSessionState.TryPickPlacement"/>）。
    /// 所持上限を超えるなら<b>取得全体を拒否</b>し、未取得のまま残す（後で空きができれば取れる）。
    /// 取得済みは恒久で、死亡・休息で戻らない。見た目は記録から合わせる（入場・有効化のたび）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaPickupPoint : MonoBehaviour, IAreaInteractable
    {
        [Tooltip("配置 ID（Area 内で一意）。")]
        [SerializeField] private string _placementId = string.Empty;

        [Tooltip("所属 Area の安定 ID。")]
        [SerializeField] private string _areaId = string.Empty;

        [Tooltip("得られるアイテム（無ければ空＝発見の報酬だけ）。")]
        [SerializeField] private string _itemId = string.Empty;

        [Tooltip("個数。")]
        [SerializeField] private int _count = 1;

        [Tooltip("アイテムの所持上限（カタログの ItemDefinition と同じ値を Builder が写す）。")]
        [SerializeField] private int _maxStack = 1;

        [Tooltip("徳の報酬（GrantOnce 推奨。無ければ未設定）。")]
        [SerializeField] private RewardData _reward;

        [Tooltip("取得済みなら隠す見た目。")]
        [SerializeField] private GameObject _visual;

        [Tooltip("表示する短文。")]
        [SerializeField] private string _prompt = "調べる";

        /// <summary>配置 ID。</summary>
        public StableId PlacementId => string.IsNullOrEmpty(_placementId) ? default : new StableId(_placementId);

        /// <summary>直近の結果（診断・テスト用）。</summary>
        public PlacementPickResult LastResult { get; private set; }

        /// <summary>配線する（Builder）。</summary>
        public void Bind(StableId placementId, StableId areaId, StableId itemId, int count, int maxStack,
            RewardData reward, GameObject visual, string prompt)
        {
            _placementId = placementId.Value ?? string.Empty;
            _areaId = areaId.Value ?? string.Empty;
            _itemId = itemId.Value ?? string.Empty;
            _count = count;
            _maxStack = maxStack;
            _reward = reward;
            _visual = visual;
            _prompt = string.IsNullOrEmpty(prompt) ? "調べる" : prompt;
        }

        /// <inheritdoc />
        public StableId InteractableId =>
            string.IsNullOrEmpty(_placementId) ? default : new StableId("pickup_point_" + _placementId);

        /// <inheritdoc />
        public StableId AreaId => string.IsNullOrEmpty(_areaId) ? default : new StableId(_areaId);

        /// <inheritdoc />
        public int FloorId => 0;

        /// <inheritdoc />
        public Vector3 InteractionAnchor => transform.position;

        /// <inheritdoc />
        public float InteractionRadius => 0f;

        /// <summary>取得済みか（記録から読む）。</summary>
        public bool IsPicked
        {
            get
            {
                GameSessionState session = GameSessionProvider.Current;
                return session != null && session.TryGetArea(AreaId, out AreaRuntimeState area)
                    && area.IsPlacementPicked(PlacementId);
            }
        }

        /// <inheritdoc />
        public bool IsAvailable => isActiveAndEnabled && !PlacementId.IsEmpty && !IsPicked;

        /// <inheritdoc />
        public string Prompt => _prompt;

        /// <inheritdoc />
        public AreaInteractionOutcome Interact()
        {
            GameSessionState session = GameSessionProvider.Current;
            if (session == null || PlacementId.IsEmpty)
            {
                return AreaInteractionOutcome.Refused("配線がありません。");
            }

            StableId item = string.IsNullOrEmpty(_itemId) ? default : new StableId(_itemId);
            PlacementPickResult result = session.TryPickPlacement(AreaId, PlacementId, item, _count, _maxStack,
                RewardSnapshot.From(_reward));
            LastResult = result;
            switch (result)
            {
                case PlacementPickResult.Picked:
                    SyncVisual();
                    return AreaInteractionOutcome.Accepted(item.IsEmpty ? "見つけた" : "手に入れた");
                case PlacementPickResult.OverCapacity:
                    return AreaInteractionOutcome.Refused("これ以上持てない");
                case PlacementPickResult.AlreadyPicked:
                    SyncVisual();
                    return AreaInteractionOutcome.Refused("もう何もない");
                default:
                    return AreaInteractionOutcome.Refused("取れない");
            }
        }

        /// <summary>見た目を記録に合わせる（入場・有効化のたび）。</summary>
        public void SyncVisual()
        {
            if (_visual != null)
            {
                _visual.SetActive(!IsPicked);
            }
        }

        private void OnEnable()
        {
            AreaInteractableRegistry.Register(this);
            SyncVisual();
        }

        private void OnDisable() => AreaInteractableRegistry.Unregister(this);
    }
}
