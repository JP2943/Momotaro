using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中解決の型付き結果（P2-01 受入修正）。どの命中（<see cref="HitId"/>）が、誰から誰へ、
    /// どの種別（<see cref="HitResultKind"/>）で解決され、実際に適用された HP／体幹／ひるませ値
    /// （<see cref="AppliedDamage"/>）がいくつだったかを保持する不変の値型。
    ///
    /// P2-01 では結果を運ぶ契約のみを定義し、実際のダメージ解決は行わない。生成には種別ごとの
    /// ファクトリを用い、回避・棄却は適用値 0（<see cref="HitDamage.None"/>）とする。
    ///
    /// P3.5-08B：命中の接触点（<see cref="HitPoint"/>）と攻撃進行方向（<see cref="AttackDirection"/>）を任意で運ぶ。
    /// フィードバック演出（例：ジャストガード VFX を接触点へ表示）が参照する表示専用情報で、Gameplay の解決には用いない。
    /// 既存の生成箇所に影響しないよう省略可（既定は <see cref="Vector3.zero"/>）とし、必要な命中のみ値を渡す。
    /// </summary>
    public readonly struct HitResult
    {
        /// <summary>結果種別。</summary>
        public HitResultKind Kind { get; }

        /// <summary>対象となった命中の同一性。</summary>
        public HitId HitId { get; }

        /// <summary>攻撃者。</summary>
        public ICombatActor Attacker { get; }

        /// <summary>被弾対象。</summary>
        public IDamageable Target { get; }

        /// <summary>実際に適用された HP／体幹／ひるませ値（回避・棄却は 0）。</summary>
        public HitDamage AppliedDamage { get; }

        /// <summary>命中の接触点（World 空間。P3.5-08B。未指定は <see cref="Vector3.zero"/>）。フィードバック VFX の表示位置に用いる。</summary>
        public Vector3 HitPoint { get; }

        /// <summary>攻撃の進行方向（攻撃者→対象、World XZ 平面・正規化想定。P3.5-08B。未指定は <see cref="Vector3.zero"/>）。</summary>
        public Vector3 AttackDirection { get; }

        /// <summary>すべての要素を指定して生成する（接触点・攻撃方向は任意。既定は <see cref="Vector3.zero"/>）。</summary>
        public HitResult(HitResultKind kind, HitId hitId, ICombatActor attacker, IDamageable target, HitDamage appliedDamage,
            Vector3 hitPoint = default, Vector3 attackDirection = default)
        {
            Kind = kind;
            HitId = hitId;
            Attacker = attacker;
            Target = target;
            AppliedDamage = appliedDamage;
            HitPoint = hitPoint;
            AttackDirection = attackDirection;
        }

        /// <summary>ダメージ結果を生成する（接触点・攻撃方向は任意）。</summary>
        public static HitResult Damage(HitId hitId, ICombatActor attacker, IDamageable target, HitDamage appliedDamage,
            Vector3 hitPoint = default, Vector3 attackDirection = default)
        {
            return new HitResult(HitResultKind.Damage, hitId, attacker, target, appliedDamage, hitPoint, attackDirection);
        }

        /// <summary>通常ガード結果を生成する（HP は 0、体幹・ひるませは適用値に従う。接触点・攻撃方向は任意）。</summary>
        public static HitResult Guard(HitId hitId, ICombatActor attacker, IDamageable target, HitDamage appliedDamage,
            Vector3 hitPoint = default, Vector3 attackDirection = default)
        {
            return new HitResult(HitResultKind.Guard, hitId, attacker, target, appliedDamage, hitPoint, attackDirection);
        }

        /// <summary>ジャストガード結果を生成する（接触点・攻撃方向は任意。JG VFX が接触点を参照する。P3.5-08B）。</summary>
        public static HitResult JustGuard(HitId hitId, ICombatActor attacker, IDamageable target, HitDamage appliedDamage,
            Vector3 hitPoint = default, Vector3 attackDirection = default)
        {
            return new HitResult(HitResultKind.JustGuard, hitId, attacker, target, appliedDamage, hitPoint, attackDirection);
        }

        /// <summary>回避結果を生成する（適用値 0。接触点・攻撃方向は任意）。</summary>
        public static HitResult Evade(HitId hitId, ICombatActor attacker, IDamageable target,
            Vector3 hitPoint = default, Vector3 attackDirection = default)
        {
            return new HitResult(HitResultKind.Evade, hitId, attacker, target, HitDamage.None, hitPoint, attackDirection);
        }

        /// <summary>棄却結果を生成する（適用値 0。接触点・攻撃方向は任意）。</summary>
        public static HitResult Rejected(HitId hitId, ICombatActor attacker, IDamageable target,
            Vector3 hitPoint = default, Vector3 attackDirection = default)
        {
            return new HitResult(HitResultKind.Rejected, hitId, attacker, target, HitDamage.None, hitPoint, attackDirection);
        }
    }
}
