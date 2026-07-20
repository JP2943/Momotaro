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

        /// <summary>命中の同一性（多重ヒット防止のキー）。</summary>
        public HitId HitId { get; }

        /// <summary>すべての要素を指定して生成する。</summary>
        public HitInfo(
            ICombatActor attacker,
            IDamageable target,
            Vector3 attackDirection,
            Vector3 hitPoint,
            HitDamage damage,
            bool guardable,
            bool justGuardable,
            HitId hitId)
        {
            Attacker = attacker;
            Target = target;
            AttackDirection = attackDirection;
            HitPoint = hitPoint;
            Damage = damage;
            Guardable = guardable;
            JustGuardable = justGuardable;
            HitId = hitId;
        }
    }
}
