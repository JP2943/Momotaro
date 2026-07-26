using Momotaro.Data.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 実行中の敵攻撃が保持する不変 Snapshot（Phase3 §2.3 / §6.3）。攻撃開始時に <see cref="EnemyAttackData"/> から
    /// 必要値を写し取り、以降は原本 Asset が変更されても実行中の攻撃は影響を受けない。P3-01 では型と生成のみを提供し、
    /// Prepare／Active／Recovery の駆動・Hitbox 生成は P3-04 で本 Snapshot を用いて実装する。
    /// </summary>
    public readonly struct EnemyAttackSnapshot
    {
        /// <summary>攻撃分類。</summary>
        public EnemyAttackClass AttackClass { get; }
        /// <summary>使用距離（m）。</summary>
        public float UseRange { get; }
        /// <summary>使用角度（度、半角）。</summary>
        public float UseAngle { get; }
        /// <summary>必要 Slot 種別。</summary>
        public AttackSlotKind SlotKind { get; }

        /// <summary>予兆（Prepare）秒。</summary>
        public float PrepareSeconds { get; }
        /// <summary>判定（Active）秒。</summary>
        public float ActiveSeconds { get; }
        /// <summary>後隙（Recovery）秒。</summary>
        public float RecoverySeconds { get; }
        /// <summary>Prepare 内の追尾停止秒。</summary>
        public float TrackingStopSeconds { get; }

        /// <summary>HP ダメージ倍率。</summary>
        public float HpMultiplier { get; }
        /// <summary>体幹ダメージ。</summary>
        public float PoiseDamage { get; }
        /// <summary>ひるませ値。</summary>
        public float FlinchPower { get; }
        /// <summary>ガード時スタミナ削り。</summary>
        public float GuardStaminaCost { get; }
        /// <summary>ノックバック力。</summary>
        public float Knockback { get; }
        /// <summary>JG 成立時に攻撃者へ返す体幹ダメージ。</summary>
        public float JustGuardPoiseReturn { get; }

        /// <summary>ガード可能か。</summary>
        public bool Guardable { get; }
        /// <summary>ジャストガード可能か。</summary>
        public bool JustGuardable { get; }
        /// <summary>ステップ回避可能か。</summary>
        public bool Steppable { get; }
        /// <summary>攻撃中ひるみ無効か。</summary>
        public bool AttackPoiseImmune { get; }

        /// <summary>照準方式。</summary>
        public EnemyAimingMode AimingMode { get; }
        /// <summary>予測秒。</summary>
        public float PredictSeconds { get; }
        /// <summary>追尾角速度（度/秒）。</summary>
        public float TrackingAngularSpeed { get; }

        /// <summary>Hitbox の各軸 half extent。</summary>
        public Vector3 HitboxHalfExtents { get; }
        /// <summary>Hitbox の前方オフセット。</summary>
        public float HitboxForwardOffset { get; }
        /// <summary>Hitbox の高さ。</summary>
        public float HitboxHeight { get; }

        /// <summary>弾速（m/s）。</summary>
        public float ProjectileSpeed { get; }
        /// <summary>弾の最大飛距離（m）。</summary>
        public float ProjectileMaxDistance { get; }
        /// <summary>弾の寿命（秒）。</summary>
        public float ProjectileLifetimeSeconds { get; }

        /// <summary>予兆種別。</summary>
        public AttackTelegraph Telegraph { get; }
        /// <summary>ヒットストップ要求（秒）。</summary>
        public float HitStopSeconds { get; }
        /// <summary>画面外開始時に画面端警告を必要とするか。</summary>
        public bool RequiresOffscreenWarning { get; }

        private EnemyAttackSnapshot(EnemyAttackData d)
        {
            AttackClass = d.AttackClass;
            UseRange = d.UseRange;
            UseAngle = d.UseAngle;
            SlotKind = d.SlotKind;

            PrepareSeconds = d.PrepareSeconds;
            ActiveSeconds = d.ActiveSeconds;
            RecoverySeconds = d.RecoverySeconds;
            TrackingStopSeconds = d.TrackingStopSeconds;

            HpMultiplier = d.HpMultiplier;
            PoiseDamage = d.PoiseDamage;
            FlinchPower = d.FlinchPower;
            GuardStaminaCost = d.GuardStaminaCost;
            Knockback = d.Knockback;
            JustGuardPoiseReturn = d.JustGuardPoiseReturn;

            Guardable = d.Guardable;
            JustGuardable = d.JustGuardable;
            Steppable = d.Steppable;
            AttackPoiseImmune = d.AttackPoiseImmune;

            AimingMode = d.AimingMode;
            PredictSeconds = d.PredictSeconds;
            TrackingAngularSpeed = d.TrackingAngularSpeed;

            HitboxHalfExtents = d.HitboxHalfExtents;
            HitboxForwardOffset = d.HitboxForwardOffset;
            HitboxHeight = d.HitboxHeight;

            ProjectileSpeed = d.ProjectileSpeed;
            ProjectileMaxDistance = d.ProjectileMaxDistance;
            ProjectileLifetimeSeconds = d.ProjectileLifetimeSeconds;

            Telegraph = d.Telegraph;
            HitStopSeconds = d.HitStopSeconds;
            RequiresOffscreenWarning = d.RequiresOffscreenWarning;
        }

        /// <summary>攻撃 Data から不変 Snapshot を生成する（開始時に 1 回）。null は不可。</summary>
        public static EnemyAttackSnapshot From(EnemyAttackData data)
        {
            return new EnemyAttackSnapshot(data);
        }
    }
}
