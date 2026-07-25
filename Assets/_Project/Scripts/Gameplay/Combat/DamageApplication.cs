using Momotaro.Gameplay.Vitals;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中の HP 適用の共通処理（Phase2 P2-04 受入修正）。攻撃側寄与（防御適用前 HP）へ対象の防御を適用し、
    /// HP <see cref="Vital"/> へ減算し、Clamp を含めた「実際に減った量」を返す。被弾側（<see cref="IDamageable"/>
    /// 実装）が Player・Dummy で同じ規則を使えるよう小さく分離したもの。将来の Damage Resolver 等の大規模化はしない。
    /// </summary>
    public static class DamageApplication
    {
        /// <summary>
        /// 防御適用後の最終 HP ダメージを <paramref name="health"/> へ減算し、実際に減った HP 量（0..残 HP）を返す。
        /// HP は Vital 側で 0..Max へ Clamp される。SO 原本には触れない。
        /// </summary>
        /// <param name="health">対象の HP。</param>
        /// <param name="preDefenseHp">攻撃側寄与（防御適用前の HP、<see cref="HitInfo"/> の Damage.Hp）。</param>
        /// <param name="defense">対象の防御力。</param>
        /// <param name="stunMultiplier">対象がスタン中なら 1.25 等（Phase2 P2-05）。既定 1.0。</param>
        /// <returns>実際に減少した HP 量（Clamp 後の差分）。</returns>
        public static int ApplyHpDamage(Vital health, float preDefenseHp, float defense, float stunMultiplier = 1f)
        {
            int before = health.Current;
            int finalHp = HpDamageCalculator.ResolveFinal(preDefenseHp, defense, stunMultiplier: stunMultiplier);
            health.Change(-finalHp);
            return before - health.Current;
        }
    }
}
