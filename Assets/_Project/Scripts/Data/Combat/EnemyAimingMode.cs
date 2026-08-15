namespace Momotaro.Data.Combat
{
    /// <summary>
    /// 敵攻撃の照準方式（Phase3 §6.1 / Table 9）。Prepare 中の追尾と Active 直前の固定方針を決める。実処理は P3-04。
    /// </summary>
    public enum EnemyAimingMode
    {
        /// <summary>現在位置型：攻撃開始時の対象方向へ固定（強・ガード不能向き）。</summary>
        CurrentPosition = 0,

        /// <summary>予測位置型：0.2〜0.5 秒先を不完全予測（直線弾向き）。</summary>
        PredictedPosition = 1,

        /// <summary>追尾型：Prepare 中だけ緩く旋回し Active 直前に固定（通常近接向き）。</summary>
        Tracking = 2,
    }
}
