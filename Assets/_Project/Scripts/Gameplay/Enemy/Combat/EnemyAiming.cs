using Momotaro.Data.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat
{
    /// <summary>
    /// 敵攻撃の照準解決（Phase3 P3-04。§6.1/Table 9）。XZ 平面で狙い方向を求める純粋関数。現在位置型は開始時の対象方向、
    /// 予測位置型は対象速度 × 予測秒で先読み、追尾型は現在位置（Prepare 中に呼び直して緩く旋回し、追尾停止で固定する）。
    /// Prepare 中に追尾を止める時刻は <see cref="EnemyAttackMachine"/> が管理する。
    /// </summary>
    public static class EnemyAimingResolver
    {
        /// <summary>照準方式に応じた狙い方向（XZ 正規化）。方向が定まらない場合は現在向き相当の +Z を返す。</summary>
        public static Vector3 Resolve(EnemyAimingMode mode, Vector3 selfPos, Vector3 targetPos, Vector3 targetVelocity,
            float predictSeconds)
        {
            Vector3 aimPoint = targetPos;
            if (mode == EnemyAimingMode.PredictedPosition)
            {
                aimPoint = targetPos + targetVelocity * predictSeconds; // 不完全予測（先読み）。
            }

            Vector3 dir = aimPoint - selfPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
            {
                return Vector3.forward;
            }

            return dir.normalized;
        }

        /// <summary>
        /// 追尾型の漸進旋回（Phase3 §6.1）。現在の狙い <paramref name="currentDir"/> を目標 <paramref name="desiredDir"/> へ、
        /// 1 ステップ最大 <paramref name="maxDegrees"/> だけ回頭した XZ 正規化方向を返す（瞬時に 180° 転換しない）。
        /// </summary>
        public static Vector3 RotateToward(Vector3 currentDir, Vector3 desiredDir, float maxDegrees)
        {
            currentDir.y = 0f;
            desiredDir.y = 0f;

            if (currentDir.sqrMagnitude < 1e-6f)
            {
                return desiredDir.sqrMagnitude < 1e-6f ? Vector3.forward : desiredDir.normalized;
            }

            if (desiredDir.sqrMagnitude < 1e-6f)
            {
                return currentDir.normalized;
            }

            float maxRad = Mathf.Max(0f, maxDegrees) * Mathf.Deg2Rad;
            Vector3 rotated = Vector3.RotateTowards(currentDir.normalized, desiredDir.normalized, maxRad, 0f);
            rotated.y = 0f;
            return rotated.sqrMagnitude < 1e-6f ? currentDir.normalized : rotated.normalized;
        }
    }
}

