namespace Momotaro.Gameplay.Enemy.Slots
{
    /// <summary>
    /// 攻撃 Slot の所有者契約（Phase3 P3-07。§8.1）。<see cref="AttackSlotCoordinator"/> は所有者を ID で管理し、Owner 不在
    /// （Disable／Down／破棄）の Slot を検出・回収する（<see cref="AttackSlotCoordinator.PruneInactive"/>）。所有者は敵 1 体に対応する。
    /// </summary>
    public interface ISlotOwner
    {
        /// <summary>所有者の同定 ID（敵 Actor の DamageableId 等、Encounter 内で一意）。</summary>
        int SlotOwnerId { get; }

        /// <summary>Slot を保持し続けてよい有効状態か（無効なら Coordinator が回収する）。</summary>
        bool IsSlotOwnerActive { get; }
    }
}
