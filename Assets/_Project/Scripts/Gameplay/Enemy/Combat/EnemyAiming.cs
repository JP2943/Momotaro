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
    }
}
