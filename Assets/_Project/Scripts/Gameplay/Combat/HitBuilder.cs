using Momotaro.Data.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 攻撃データ原本（<see cref="AttackData"/>）と実行時コンテキストから <see cref="HitInfo"/> を組み立てる
    /// 純粋ヘルパ（P2-01・依頼 §2/§3）。既存の <see cref="AttackData"/> を優先利用し、原本は一切書き換えない。
    ///
    /// 責務は「防御フラグの写し取り」と「命中情報の梱包」まで。防御属性の取得元は
    /// <see cref="AttackSnapshot"/> に一本化し、原本 SO を後続計算の正本にしない（P2-01 受入修正）。
    /// HP／体幹／ひるませの最終数値（攻撃力・防御・背後補正・スタン倍率等）は本 Phase の対象外のため、
    /// 呼び出し側が <see cref="HitDamage"/> として渡す（数値算出は後続タスク）。
    /// </summary>
    public static class HitBuilder
    {
        /// <summary>
        /// 不変 <see cref="AttackSnapshot"/> の防御フラグを反映しつつ命中情報を生成する（推奨経路）。
        /// </summary>
        /// <param name="snapshot">攻撃発動時に確定した不変 Snapshot。</param>
        /// <param name="attacker">攻撃者。</param>
        /// <param name="target">被弾対象。</param>
        /// <param name="attackDirection">攻撃の進行方向（攻撃者→対象、World）。</param>
        /// <param name="hitPoint">命中位置（World）。</param>
        /// <param name="damage">呼び出し側が算出済みの HP／体幹／ひるませ値。</param>
        /// <param name="hitId">命中の同一性（多重ヒット防止キー）。</param>
        public static HitInfo FromSnapshot(
            AttackSnapshot snapshot,
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitDamage damage,
            HitId hitId)
        {
            return new HitInfo(
                attacker,
                target,
                attackDirection,
                hitPoint,
                damage,
                snapshot.GuardStaminaCost,
                snapshot.Guardable,
                snapshot.JustGuardable,
                hitId);
        }

        /// <summary>
        /// <see cref="AttackData"/> から Snapshot を確定し、命中情報を生成する。原本は読み取りのみ。
        /// 内部で <see cref="AttackSnapshot.FromData"/> を用い、防御属性の取得を Snapshot 経由に統一する。
        /// </summary>
        public static HitInfo FromAttack(
            AttackData data,
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitDamage damage,
            HitId hitId)
        {
            return FromSnapshot(AttackSnapshot.FromData(data), attacker, target, attackDirection, hitPoint, damage, hitId);
        }
    }
}
