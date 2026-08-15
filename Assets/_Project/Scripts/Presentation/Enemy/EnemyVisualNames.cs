using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using UnityEngine;

namespace Momotaro.Presentation.Enemy
{
    /// <summary>敵スプライトの表示方向（4 方向）。カメラ相対の見た目方向で、Down は正面（手前）。</summary>
    public enum EnemyVisualFacing
    {
        Down = 0,
        Up = 1,
        Left = 2,
        Right = 3,
    }

    /// <summary>
    /// 敵の World 前方（XZ）を 4 方向の表示 Facing へ変換する純粋ヘルパ（Phase3 敵スプライト受入）。プロトタイプの
    /// 既定規約は「-Z=Down（手前/カメラ側）／+Z=Up／+X=Right／-X=Left」で、最終的なカメラ相対の対応調整は P3-05 で行う。
    /// </summary>
    public static class EnemyFacingResolver
    {
        /// <summary>前方ベクトル（XZ）から支配軸で 4 方向を選ぶ。ほぼ静止なら Down（正面）。</summary>
        public static EnemyVisualFacing FromForward(Vector3 forward)
        {
            Vector3 f = forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f)
            {
                return EnemyVisualFacing.Down;
            }

            if (Mathf.Abs(f.x) >= Mathf.Abs(f.z))
            {
                return f.x >= 0f ? EnemyVisualFacing.Right : EnemyVisualFacing.Left;
            }

            return f.z >= 0f ? EnemyVisualFacing.Up : EnemyVisualFacing.Down;
        }
    }

    /// <summary>
    /// 敵 Gameplay 状態＋表示 Facing から Animator State 名（＝クリップ名）を解決する純粋ヘルパ（Phase3 敵スプライト受入）。
    /// Animator State を Gameplay 状態の正本にしない（表示解決のみ）。対応（GPT 指定）：Idle/Patrol/Suspicious/Alert→Idle、
    /// Chase/Reposition/Return→Walk、AttackPrepare/Active/Recovery→Attack、Stagger→Hurt、Stunned→Stun、Down→Down（正面固定）。
    /// Guard/Evade/Event は本受入範囲のクリップが無いため Idle へフォールバック（P3-10 以降で拡張）。
    /// </summary>
    public static class EnemyVisualNames
    {
        /// <summary>方向サフィックス。</summary>
        public static string DirectionSuffix(EnemyVisualFacing facing)
        {
            switch (facing)
            {
                case EnemyVisualFacing.Up: return "Up";
                case EnemyVisualFacing.Left: return "Left";
                case EnemyVisualFacing.Right: return "Right";
                default: return "Down";
            }
        }

        /// <summary>状態＋Facing → Animator State 名（Basic 命名＝剣士/弓兵：Walk/Attack）。Down は Facing 非依存で正面 "Down"。</summary>
        public static string StateName(EnemyState state, EnemyVisualFacing facing)
        {
            return StateName(state, facing, EnemyVisualNamingStyle.Basic, EnemyAttackClass.Normal);
        }

        /// <summary>
        /// 状態＋Facing＋命名スタイル＋攻撃分類 → Animator State 名（Phase3 P3-09）。Basic は移動＝Walk・攻撃＝Attack（剣士/弓兵）。
        /// Elite は移動＝Move・攻撃を分類別に NormalAttack／HeavyOverhead／UnguardableThrust（侍骸骨）へ解決する。Idle/Hurt/Stun/Down は共通。
        /// </summary>
        public static string StateName(EnemyState state, EnemyVisualFacing facing, EnemyVisualNamingStyle style, EnemyAttackClass attackClass)
        {
            switch (state)
            {
                case EnemyState.Down:
                    return "Down"; // Facing 非依存の共通正面 Down。
                case EnemyState.Stunned:
                    return "Stun_" + DirectionSuffix(facing);
                case EnemyState.Stagger:
                    return "Hurt_" + DirectionSuffix(facing);
                case EnemyState.AttackPrepare:
                case EnemyState.AttackActive:
                case EnemyState.AttackRecovery:
                    return AttackStateName(style, attackClass) + "_" + DirectionSuffix(facing);
                case EnemyState.Chase:
                case EnemyState.Reposition:
                case EnemyState.Return:
                    return (style == EnemyVisualNamingStyle.Elite ? "Move_" : "Walk_") + DirectionSuffix(facing);
                default:
                    // Idle/Patrol/Suspicious/Alert/Guard/Evade/Event
                    return "Idle_" + DirectionSuffix(facing);
            }
        }

        // 攻撃 State のベース名。Basic は単一 Attack、Elite は分類別（Charge は突進で通常攻撃モーションを流用）。
        private static string AttackStateName(EnemyVisualNamingStyle style, EnemyAttackClass attackClass)
        {
            if (style != EnemyVisualNamingStyle.Elite)
            {
                return "Attack";
            }

            switch (attackClass)
            {
                case EnemyAttackClass.Heavy: return "HeavyOverhead";
                case EnemyAttackClass.Unblockable: return "UnguardableThrust";
                default: return "NormalAttack"; // Normal / Charge / Projectile
            }
        }
    }

    /// <summary>敵の Animator State 命名スタイル（Phase3 P3-09）。Basic＝剣士/弓兵（Walk/Attack）、Elite＝侍骸骨（Move/分類別攻撃）。</summary>
    public enum EnemyVisualNamingStyle
    {
        /// <summary>剣士・弓兵：移動＝Walk、攻撃＝Attack。</summary>
        Basic = 0,

        /// <summary>侍骸骨（強敵）：移動＝Move、攻撃＝NormalAttack/HeavyOverhead/UnguardableThrust。</summary>
        Elite = 1,
    }
}
