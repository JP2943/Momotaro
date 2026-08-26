using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat
{
    /// <summary>
    /// 敵攻撃の命中情報を組み立てる純粋ヘルパ（Phase3 P3-04）。不変 <see cref="EnemyAttackSnapshot"/> と攻撃力から
    /// Phase 2 と同じ <see cref="HitInfo"/> を生成し、Guardable／JustGuardable・ガードスタミナ・JG 反射体幹を反映する。
    /// これを対象の <see cref="IDamageable.ReceiveHit"/> へ渡すことで、無敵＞JG＞Guard＞Damage の解決順は被弾側（Phase 2）が担保する。
    /// HP は攻撃側寄与（防御適用前）＝攻撃力 × 技倍率 × 0.1。防御・スタン倍率・体幹状況補正は対象側で適用される。
    /// </summary>
    public static class EnemyHitFactory
    {
        /// <summary>攻撃側寄与の HP／体幹／ひるみ（防御適用前）。</summary>
        public static HitDamage Damage(in EnemyAttackSnapshot snapshot, float attackPower)
        {
            float hp = HpDamageCalculator.AttackContribution(attackPower, snapshot.HpMultiplier);
            return new HitDamage(hp, snapshot.PoiseDamage, snapshot.FlinchPower);
        }

        /// <summary>不変 Snapshot と攻撃力から命中情報を生成する（原本 Data は参照しない）。</summary>
        public static HitInfo Build(
            in EnemyAttackSnapshot snapshot,
            float attackPower,
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitId hitId)
        {
            var hit = new HitInfo(
                attacker,
                target,
                attackDirection,
                hitPoint,
                Damage(snapshot, attackPower),
                snapshot.GuardStaminaCost,
                snapshot.JustGuardPoiseReturn,
                snapshot.Guardable,
                snapshot.JustGuardable,
                isJustGuardCounter: false,
                defenseIgnoreRatio: 0f,
                stunHpMultiplierOverride: 0f,
                steppable: snapshot.Steppable,
                hitId);

            // P3.5-08A：移動リアクション（被弾者へのヒットバック／防御者へのガードバック）と飛び道具判別を載せる。
            // 飛び道具（Projectile）の JG では射手本人をひるませないため IsProjectile を立てる（被弾側が近接のみひるませる）。
            var reaction = new HitReaction(
                snapshot.HitbackDistance,
                snapshot.HitbackSeconds,
                snapshot.GuardbackDistance,
                snapshot.AttackClass == EnemyAttackClass.Projectile);
            return hit.WithReaction(reaction);
        }
    }
}
