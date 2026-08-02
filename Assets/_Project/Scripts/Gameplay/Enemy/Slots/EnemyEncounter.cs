using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Slots
{
    /// <summary>
    /// 集団戦の Encounter スコープ（Phase3 P3-07。§8.1）。配下の敵が共有する <see cref="AttackSlotCoordinator"/> を保持する。
    /// 敵はこの GameObject を親に持ち（<c>GetComponentInParent</c> で解決）、同一 Encounter 内でのみ Slot を共有する。別 Encounter
    /// （別の EnemyEncounter 配下）とは Slot を共有しない。Owner 不在 Slot は定期的に回収する（Disable／Down／破棄の取りこぼし対策）。
    /// Encounter 親を持たない単体敵（Prototype 検証）は Coordinator を持たず、Slot 制限を受けない（常に開始可）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyEncounter : MonoBehaviour
    {
        [Tooltip("各攻撃分類の同時開始上限（Table12。序盤は 1／1／1）。")]
        [SerializeField] private SlotCapacities _capacities = SlotCapacities.Default;

        [Tooltip("Owner 不在 Slot の回収間隔（秒）。0 以下で毎フレーム。")]
        [SerializeField] private float _pruneInterval = 0.5f;

        private AttackSlotCoordinator _coordinator;
        private readonly SurroundCoordinator _surround = new SurroundCoordinator();
        private float _pruneTimer;

        /// <summary>この Encounter の Slot 調停。</summary>
        public AttackSlotCoordinator Coordinator
        {
            get
            {
                EnsureCoordinator();
                return _coordinator;
            }
        }

        /// <summary>この Encounter の包囲調停（待機敵を対象周囲へ均等配置する。§8.1）。</summary>
        public SurroundCoordinator Surround => _surround;

        private void Awake() => EnsureCoordinator();

        private void EnsureCoordinator()
        {
            if (_coordinator == null)
            {
                _coordinator = new AttackSlotCoordinator(_capacities);
            }
            else
            {
                _coordinator.Configure(_capacities);
            }
        }

        private void Update()
        {
            _pruneTimer += Time.deltaTime;
            if (_pruneTimer < _pruneInterval)
            {
                return;
            }

            _pruneTimer = 0f;
            _coordinator?.PruneInactive();
        }

        private void OnDisable()
        {
            _coordinator?.Reset(); // Encounter 無効化で全 Slot を解放。
            _surround.Clear();
        }
    }
}
