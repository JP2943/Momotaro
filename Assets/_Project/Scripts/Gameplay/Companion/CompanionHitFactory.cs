using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間攻撃の命中情報を組み立てる純粋ヘルパ（P4-03）。敵側の <c>EnemyHitFactory</c> と同じ役割で、不変
    /// <see cref="AttackSnapshot"/> と攻撃力から主人公・敵と同じ <see cref="HitInfo"/> を生成する。
    /// これを対象の <see cref="IDamageable.ReceiveHit"/> へ渡すことで、無敵＞JG＞Guard＞Damage の解決順は
    /// 被弾側（Phase 2／敵側 P3）が担保する。仲間専用の命中経路は作らない。
    ///
    /// 数値の作り方は主人公の通常攻撃に合わせる：HP は攻撃側寄与（防御適用前）＝攻撃力 × 技倍率 × 0.1 ×背後(×1.1)、
    /// 体幹は固定系統に状況補正（背後×1.5／攻撃中×1.5。高い方だけ）を攻撃側で適用、ひるませ値は状況補正なし。
    /// 防御補正・スタン倍率は対象側で適用される。
    /// </summary>
    public static class CompanionHitFactory
    {
        /// <summary>攻撃側寄与の HP／体幹／ひるませ（防御適用前）。</summary>
        /// <param name="snapshot">攻撃開始時に確定した不変 Snapshot。</param>
        /// <param name="attackPower">攻撃者の攻撃力（<c>CharacterData.AttackPower</c>）。</param>
        /// <param name="isBackHit">背後からの命中か（HP ×1.1／体幹の状況補正）。</param>
        /// <param name="targetActing">対象が攻撃の予備・判定中か（体幹の状況補正）。</param>
        public static HitDamage Damage(in AttackSnapshot snapshot, float attackPower, bool isBackHit, bool targetActing)
        {
            float hpBackMultiplier = isBackHit ? HpDamageCalculator.BackMultiplier : 1f;
            float hp = HpDamageCalculator.AttackContribution(attackPower, snapshot.HpMultiplier, 1f, hpBackMultiplier);

            float poiseSituational = PoiseDamageCalculator.SituationalMultiplier(isBackHit, targetActing);
            float poise = PoiseDamageCalculator.Compute(snapshot.PoiseDamage, poiseSituational, 1f, 1f);

            float flinch = FlinchValueCalculator.Compute(snapshot.FlinchPower, 1f, 1f);

            return new HitDamage(hp, poise, flinch);
        }

        /// <summary>
        /// 不変 Snapshot と攻撃力から命中情報を生成する（原本 Data は参照しない）。ヒットバックは Snapshot の値を載せる。
        /// 仲間の通常攻撃は近接のみのため、ガードバック 0・飛び道具フラグ false とする。
        /// </summary>
        public static HitInfo Build(
            in AttackSnapshot snapshot,
            float attackPower,
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitId hitId,
            bool isBackHit = false,
            bool targetActing = false)
        {
            HitDamage damage = Damage(snapshot, attackPower, isBackHit, targetActing);

            return HitBuilder.FromSnapshot(snapshot, attacker, target, attackDirection, hitPoint, damage, hitId)
                .WithReaction(new HitReaction(
                    snapshot.HitbackDistance, snapshot.HitbackSeconds, 0f, isProjectile: false));
        }
    }
}
