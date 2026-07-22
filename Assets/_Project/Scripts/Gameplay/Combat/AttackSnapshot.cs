using Momotaro.Data.Combat;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 攻撃データ原本（<see cref="AttackData"/>）から、実行中に必要な値だけを複製した不変 Snapshot（P2-01 受入修正）。
    /// 攻撃発動時に一度生成し、以降の命中計算はこの Snapshot を正本とする。生成後に SO 原本が変化しても
    /// Snapshot は不変であり、後続計算が原本の実行時変更に影響されないことを保証する（本書 §2.2）。
    ///
    /// P2-01 では値の複製・保持のみを担い、これを用いた実ダメージ解決は後続 Task で行う。
    /// </summary>
    public readonly struct AttackSnapshot
    {
        /// <summary>HP 技倍率。</summary>
        public float HpMultiplier { get; }

        /// <summary>体幹ダメージ（固定系統）。</summary>
        public float PoiseDamage { get; }

        /// <summary>ひるませ値。</summary>
        public float FlinchPower { get; }

        /// <summary>ガードで消費させるスタミナ量（Guard Stamina Damage）。</summary>
        public float GuardStaminaCost { get; }

        /// <summary>ジャストガード成立時に攻撃者の体幹へ反射する固定ダメージ。</summary>
        public float JustGuardPoiseDamage { get; }

        /// <summary>通常ガード可能か。</summary>
        public bool Guardable { get; }

        /// <summary>ジャストガード可能か。</summary>
        public bool JustGuardable { get; }

        /// <summary>ステップ回避可能か。</summary>
        public bool StepAvoidable { get; }

        /// <summary>攻撃予兆種別。</summary>
        public AttackTelegraph Telegraph { get; }

        /// <summary>各値を指定して生成する。</summary>
        public AttackSnapshot(
            float hpMultiplier,
            float poiseDamage,
            float flinchPower,
            float guardStaminaCost,
            float justGuardPoiseDamage,
            bool guardable,
            bool justGuardable,
            bool stepAvoidable,
            AttackTelegraph telegraph)
        {
            HpMultiplier = hpMultiplier;
            PoiseDamage = poiseDamage;
            FlinchPower = flinchPower;
            GuardStaminaCost = guardStaminaCost;
            JustGuardPoiseDamage = justGuardPoiseDamage;
            Guardable = guardable;
            JustGuardable = justGuardable;
            StepAvoidable = stepAvoidable;
            Telegraph = telegraph;
        }

        /// <summary>
        /// 攻撃データ原本から Snapshot を生成する。<paramref name="data"/> が null の場合は
        /// 既定値（全フラグ false・数値 0・Telegraph=Normal）の安全な Snapshot を返す。
        /// 生成後に <paramref name="data"/> が変化しても、この Snapshot は変化しない。
        /// </summary>
        public static AttackSnapshot FromData(AttackData data)
        {
            if (data == null)
            {
                return default;
            }

            return new AttackSnapshot(
                data.HpMultiplier,
                data.PoiseDamage,
                data.FlinchPower,
                data.GuardStaminaCost,
                data.JustGuardPoiseDamage,
                data.Guardable,
                data.JustGuardable,
                data.StepAvoidable,
                data.Telegraph);
        }
    }
}
