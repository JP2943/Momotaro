namespace Momotaro.Data.Combat
{
    /// <summary>
    /// 敵攻撃の分類（Phase3 §9.3 / Table 5）。予兆最低時間・防御可否・画面外開始可否・Slot 種別の既定を導く語彙。
    /// Score 計算や画面内制御（P3-07/09）が参照する。実処理は後続 Task。
    /// </summary>
    public enum EnemyAttackClass
    {
        /// <summary>通常（予兆 0.25 秒以上、Guard／JG／Step 可）。</summary>
        Normal = 0,

        /// <summary>強（予兆 0.50 秒以上、Guard／JG／Step 可、高 Guard 削り）。</summary>
        Heavy = 1,

        /// <summary>ガード不能（予兆 0.70 秒以上、Guard／JG 不可・Step 可）。</summary>
        Unblockable = 2,

        /// <summary>突進（進行方向を早期固定、壁貫通なし、同一対象 1Hit）。</summary>
        Charge = 3,

        /// <summary>投射（直線 Projectile。Guard／JG／Step は Data 指定）。</summary>
        Projectile = 4,
    }
}
