namespace Momotaro.Data.Combat
{
    /// <summary>
    /// 集団戦の攻撃 Slot 種別（Phase3 §8.1 / Table 12）。Encounter 単位で同時攻撃数を制限するための分類。
    /// P3-01 では語彙のみ定義し、Coordinator／Lease の実処理は P3-07。
    /// </summary>
    public enum AttackSlotKind
    {
        /// <summary>Slot を要求しない（Reposition・威嚇など）。</summary>
        None = 0,

        /// <summary>近接通常。</summary>
        MeleeNormal = 1,

        /// <summary>強／ガード不能（重い攻撃）。</summary>
        Strong = 2,

        /// <summary>遠距離。</summary>
        Ranged = 3,
    }
}
