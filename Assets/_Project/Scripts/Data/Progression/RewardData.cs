using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.Progression
{
    /// <summary>報酬データ雛形（仕様書 8.15 / 13.9）。徳・アイテム等の付与を表す。</summary>
    [CreateAssetMenu(fileName = "SO_Reward_New", menuName = "Momotaro/Data/Progression/Reward Data", order = 1)]
    public sealed class RewardData : GameDataAsset
    {
        [Header("Reward")]
        [SerializeField] private int _virtueAmount;
        [SerializeField] private StableId _itemId;
        [SerializeField] private bool _grantOnce = true;

        /// <summary>付与する徳量。</summary>
        public int VirtueAmount => _virtueAmount;

        /// <summary>一度だけ付与するか。</summary>
        public bool GrantOnce => _grantOnce;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_virtueAmount < 0)
            {
                report.Error(name + ": VirtueAmount must be >= 0.");
            }

            if (!_itemId.IsEmpty && !_itemId.IsValid)
            {
                report.Error(name + ": ItemId has invalid format '" + _itemId.Value + "'.");
            }
        }
    }
}
