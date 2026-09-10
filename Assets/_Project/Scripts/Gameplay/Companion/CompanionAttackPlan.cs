namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 1 回の攻撃を最後まで進めるのに必要な値を、<b>開始した瞬間に丸ごと写し取った複製</b>（P4-FIX F06）。
    ///
    /// それまでは毎 Tick で <see cref="Data.Combat.AttackData"/> と Inspector の値を読み直していた。
    /// 数値調整は Play 中に行うのが普通なので、振っている最中に判定の長さだけ伸びる・後隙に入ってから
    /// クールダウンが変わる、といった<b>再現できない挙動</b>が混ざる。攻撃は「振り始めた条件で終わる」のが
    /// 当たり前の期待なので、SO 原本を実行時に書き換えない約束と同じ理由で開始時に確定させる。
    ///
    /// 判定寸法（Box の幅・高さ）も含めるのは、間合いだけ凍結しても Box の形が変われば当たり判定が変わるため。
    /// 「攻撃中に読むのはこの構造体だけ」と言い切れる形にしておくと、後から読み直しが紛れ込んでも見つけやすい。
    /// </summary>
    public readonly struct CompanionAttackPlan
    {
        /// <summary>開始時の攻撃設定（間合い・角度・各段の秒数・クールダウン）。</summary>
        public CompanionAttackSettings Settings { get; }

        /// <summary>判定 Box の左右半幅。</summary>
        public float HitboxHalfWidth { get; }

        /// <summary>判定 Box の中心高さ（足元から）。</summary>
        public float HitboxHeight { get; }

        /// <summary>判定 Box の上下半高。</summary>
        public float HitboxHalfHeight { get; }

        public CompanionAttackPlan(
            in CompanionAttackSettings settings, float hitboxHalfWidth, float hitboxHeight, float hitboxHalfHeight)
        {
            Settings = settings;
            HitboxHalfWidth = hitboxHalfWidth;
            HitboxHeight = hitboxHeight;
            HitboxHalfHeight = hitboxHalfHeight;
        }

        /// <summary>攻撃していない状態（すべて 0）。</summary>
        public static CompanionAttackPlan None => default;

        /// <summary>攻撃として成立する内容か。</summary>
        public bool HasAttack => Settings.HasAttack;

        /// <summary>判定が届く距離（＝<see cref="CompanionAttackSettings.UseRange"/>。Box の前方長）。</summary>
        public float Reach => Settings.UseRange;

        /// <summary>攻撃終了時に入るクールダウン秒。</summary>
        public float CooldownSeconds => Settings.CooldownSeconds;
    }
}
