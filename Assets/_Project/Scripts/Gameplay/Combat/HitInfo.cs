using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 実行時の命中情報（P2-01）。攻撃データ原本（<see cref="Momotaro.Data.Combat.AttackData"/> 等）とは
    /// 分離した、1 回の命中を表す不変の値型（依頼 §3）。通常攻撃・ガード・ジャストガード・ステップ・
    /// 必殺技のいずれもこの共通型を通して命中を扱う。
    ///
    /// 見た目（Sprite / Animation Event）を命中ロジックの正本にしない（依頼 §7）。命中の正本は本型であり、
    /// 演出はこの情報を参照して発火する側とする。
    /// </summary>
    public readonly struct HitInfo
    {
        /// <summary>攻撃者。</summary>
        public ICombatActor Attacker { get; }

        /// <summary>被弾対象。</summary>
        public IDamageable Target { get; }

        /// <summary>攻撃の進行方向（攻撃者→対象、World XZ 平面・正規化想定）。</summary>
        public Vector3 AttackDirection { get; }

        /// <summary>命中位置（World 空間）。</summary>
        public Vector3 HitPoint { get; }

        /// <summary>HP・体幹・ひるませの 3 系統値（分離済み）。</summary>
        public HitDamage Damage { get; }

        /// <summary>通常ガード可能か（不能攻撃は false）。</summary>
        public bool Guardable { get; }

        /// <summary>ジャストガード可能か（不能攻撃は false）。</summary>
        public bool JustGuardable { get; }

        /// <summary>通常ガード成功時に対象へ与える固定スタミナダメージ（Guard Stamina Damage。Phase2 P2-06）。</summary>
        public float GuardStaminaDamage { get; }

        /// <summary>ジャストガード成立時に攻撃者の体幹へ反射する固定ダメージ（Phase2 P2-08）。</summary>
        public float JustGuardPoiseDamage { get; }

        /// <summary>
        /// この命中が「ジャストガードによる反射（攻撃者の体幹への反撃）」か（Phase2 P2-08）。true の場合、受け手側は
        /// 体幹回復待機を JG 用（通常 3 秒→4 秒）に延長する。通常の命中は false。
        /// </summary>
        public bool IsJustGuardCounter { get; }

        /// <summary>防御を一部無視する割合（0..1。必殺技用。Phase2 P2-10）。実効防御 = 防御×(1-率)。通常は 0。</summary>
        public float DefenseIgnoreRatio { get; }

        /// <summary>
        /// スタン中の対象への HP 倍率の上書き（Phase2 P2-10）。0 以下なら対象既定（通常 1.25）を用いる。必殺技は 1.5 を指定し、
        /// 1.25 と乗算せず置き換える。
        /// </summary>
        public float StunHpMultiplierOverride { get; }

        /// <summary>命中の同一性（多重ヒット防止のキー）。</summary>
        public HitId HitId { get; }

        /// <summary>ガードスタミナ／JG 反射 0 で生成する（HP/体幹/ひるみのみの命中）。</summary>
        public HitInfo(
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitDamage damage,
            bool guardable,
            bool justGuardable,
            HitId hitId)
            : this(attacker, target, attackDirection, hitPoint, damage, 0f, 0f, guardable, justGuardable, hitId)
        {
        }

        /// <summary>ガードスタミナダメージを指定し、JG 反射 0 で生成する。</summary>
        public HitInfo(
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitDamage damage,
            float guardStaminaDamage,
            bool guardable,
            bool justGuardable,
            HitId hitId)
            : this(attacker, target, attackDirection, hitPoint, damage, guardStaminaDamage, 0f, guardable, justGuardable, hitId)
        {
        }

        /// <summary>ガードスタミナ・JG 反射値を指定し、JG 反射フラグ false で生成する。</summary>
        public HitInfo(
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitDamage damage,
            float guardStaminaDamage,
            float justGuardPoiseDamage,
            bool guardable,
            bool justGuardable,
            HitId hitId)
            : this(attacker, target, attackDirection, hitPoint, damage, guardStaminaDamage, justGuardPoiseDamage,
                   guardable, justGuardable, false, hitId)
        {
        }

        /// <summary>ガードスタミナ・JG 反射・JG 反射フラグを指定し、防御無視 0・スタン上書きなしで生成する。</summary>
        public HitInfo(
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitDamage damage,
            float guardStaminaDamage,
            float justGuardPoiseDamage,
            bool guardable,
            bool justGuardable,
            bool isJustGuardCounter,
            HitId hitId)
            : this(attacker, target, attackDirection, hitPoint, damage, guardStaminaDamage, justGuardPoiseDamage,
                   guardable, justGuardable, isJustGuardCounter, 0f, 0f, hitId)
        {
        }

        /// <summary>すべての要素（必殺技用の防御一部無視・スタン倍率上書き含む）を指定して生成する。</summary>
        public HitInfo(
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitDamage damage,
            float guardStaminaDamage,
            float justGuardPoiseDamage,
            bool guardable,
            bool justGuardable,
            bool isJustGuardCounter,
            float defenseIgnoreRatio,
            float stunHpMultiplierOverride,
            HitId hitId)
        {
            Attacker = attacker;
            Target = target;
            AttackDirection = attackDirection;
            HitPoint = hitPoint;
            Damage = damage;
            GuardStaminaDamage = guardStaminaDamage;
            JustGuardPoiseDamage = justGuardPoiseDamage;
            Guardable = guardable;
            JustGuardable = justGuardable;
            IsJustGuardCounter = isJustGuardCounter;
            DefenseIgnoreRatio = defenseIgnoreRatio;
            StunHpMultiplierOverride = stunHpMultiplierOverride;
            HitId = hitId;
        }
    }
}
