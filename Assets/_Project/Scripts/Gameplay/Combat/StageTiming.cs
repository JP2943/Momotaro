namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 攻撃 1 段の時間パラメータ（Phase2 P2-03B）。予備・判定・後隙と、Guard/Step キャンセル許可開始秒を持つ。
    /// SO（AttackData）由来の値を実行時 Snapshot として渡すための不変の値型。
    /// </summary>
    public readonly struct StageTiming
    {
        /// <summary>予備動作（秒）。</summary>
        public float Startup { get; }

        /// <summary>判定（秒）。Hitbox 有効時間。</summary>
        public float Active { get; }

        /// <summary>後隙（秒）。</summary>
        public float Recovery { get; }

        /// <summary>段開始からのキャンセル許可開始秒（0 以下なら判定終了後 = Startup+Active）。</summary>
        public float CancelWindowStart { get; }

        /// <summary>段の総時間（予備＋判定＋後隙）。</summary>
        public float Total => Startup + Active + Recovery;

        /// <summary>判定終了時刻（= 予備＋判定）。次段への連鎖・キャンセルの基準。</summary>
        public float ActiveEnd => Startup + Active;

        /// <summary>各値を指定して生成する。</summary>
        public StageTiming(float startup, float active, float recovery, float cancelWindowStart)
        {
            Startup = startup < 0f ? 0f : startup;
            Active = active < 0f ? 0f : active;
            Recovery = recovery < 0f ? 0f : recovery;
            CancelWindowStart = cancelWindowStart;
        }

        /// <summary>キャンセルが許可される実効開始秒（未指定 0 以下なら判定終了後）。</summary>
        public float EffectiveCancelStart => CancelWindowStart > 0f ? CancelWindowStart : ActiveEnd;
    }
}
