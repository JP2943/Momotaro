using Momotaro.Data.Combat;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の通常攻撃の設定 Snapshot（P4-03）。原本 <see cref="AttackData"/> から必要な値だけを複製した不変値型で、
    /// 攻撃中に SO 原本が変化しても挙動が揺れない（本書 §2.2。主人公・敵と同じ方針）。
    ///
    /// 距離は 2 つある。<see cref="AttackStartDistance"/>（ここまで近づいて攻撃を始める）と
    /// <see cref="UseRange"/>（判定が届く距離）で、必ず 前者 &lt; 後者 とする。この 2 つを取り違えると、
    /// 届かない距離から攻撃を始めて延々と空振りする（P4-03 受入で実際に起きた不具合）。
    ///
    /// 仲間専用の Data 型は作らず、主人公・敵と同じ <see cref="AttackData"/> を正本にする。攻撃の数値・時間・
    /// 防御属性（ガード可否・ジャストガード可否・ステップ回避可否）を全員で同じ語彙のまま扱えるようにするため。
    ///
    /// <see cref="AttackData"/> 未割当（null）のときは <see cref="None"/> を返し、<see cref="HasAttack"/> が false になる。
    /// この場合、仲間は攻撃も接近もしない（主人公が <c>_attackCombo</c> 未割当で攻撃不可になるのと同じ扱い）。
    /// 「Data が無いので既定値で殴る」ことはしない。数値の正本は必ず Data 側に置く。
    /// </summary>
    public readonly struct CompanionAttackSettings
    {
        /// <summary>攻撃を開始できる距離（m）。</summary>
        public float UseRange { get; }

        /// <summary>攻撃を開始できる角度（度。0 以下で角度制限なし）。</summary>
        public float UseAngle { get; }

        /// <summary>攻撃終了後、次の攻撃までの待ち秒。</summary>
        public float CooldownSeconds { get; }

        /// <summary>予兆（振りかぶり）秒。</summary>
        public float StartupSeconds { get; }

        /// <summary>判定（Hitbox 有効）秒。</summary>
        public float ActiveSeconds { get; }

        /// <summary>後隙秒。</summary>
        public float RecoverySeconds { get; }

        /// <summary>
        /// 接近を止め、そこから攻撃を始める距離（m）。<see cref="UseRange"/>（判定の届く距離）より内側に置く。
        ///
        /// 攻撃開始と判定到達を同じ距離にしてはならない。予兆のあいだに対象が少しでも離れると必ず空振りするうえ、
        /// 間合いの境目で「攻撃 → 空振り → 接近」を往復するため。内側で始めることで、予兆中に対象が動いても
        /// <see cref="UseRange"/> までの余裕で吸収できる。
        /// </summary>
        public float AttackStartDistance { get; }

        /// <summary>攻撃全体の長さ（秒）。</summary>
        public float TotalSeconds => StartupSeconds + ActiveSeconds + RecoverySeconds;

        /// <summary>攻撃できる構成か（判定時間と射程がある）。false のときは攻撃も接近もしない。</summary>
        public bool HasAttack => ActiveSeconds > 0f && UseRange > 0f;

        /// <summary>
        /// 攻撃開始距離を使用距離から求める比率。残りの <c>1 - 比率</c> が、予兆中に対象が動いても判定が届くための余裕になる。
        /// </summary>
        public const float AttackStartRatio = 0.7f;

        /// <summary>各値を指定して生成する（負値は 0 に丸める）。</summary>
        public CompanionAttackSettings(
            float useRange,
            float useAngle,
            float cooldownSeconds,
            float startupSeconds,
            float activeSeconds,
            float recoverySeconds)
        {
            UseRange = useRange < 0f ? 0f : useRange;
            UseAngle = useAngle < 0f ? 0f : useAngle;
            CooldownSeconds = cooldownSeconds < 0f ? 0f : cooldownSeconds;
            StartupSeconds = startupSeconds < 0f ? 0f : startupSeconds;
            ActiveSeconds = activeSeconds < 0f ? 0f : activeSeconds;
            RecoverySeconds = recoverySeconds < 0f ? 0f : recoverySeconds;
            AttackStartDistance = UseRange * AttackStartRatio;
        }

        /// <summary>攻撃を持たない設定（Data 未割当・無効値）。</summary>
        public static CompanionAttackSettings None => new CompanionAttackSettings(0f, 0f, 0f, 0f, 0f, 0f);

        /// <summary>攻撃データ原本から Snapshot を生成する。null なら <see cref="None"/>。</summary>
        public static CompanionAttackSettings From(AttackData data)
        {
            if (data == null)
            {
                return None;
            }

            return new CompanionAttackSettings(
                data.UseRange,
                data.UseAngle,
                data.CooldownSeconds,
                data.StartupSeconds,
                data.ActiveSeconds,
                data.RecoverySeconds);
        }
    }
}
