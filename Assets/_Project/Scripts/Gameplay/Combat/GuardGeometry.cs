using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 通常ガードの角度判定のための純粋計算（Phase2 P2-06。仕様書 §3.2 / §8）。ガード方向を中心とした前方 180°
    /// （既定 ±90°）の内側から来る攻撃だけを防御対象とする。飛び道具でも同じ入力（攻撃の進行方向）で判定できる。
    /// World XZ 平面を前提とし、Y 成分は無視。0 ベクトルなど不正入力でも NaN を出さず安全側（前方＝防御可）へ落とす。
    /// UnityEngine の数学型のみを用い、Input System / Animator / Scene には依存しない。
    /// </summary>
    public static class GuardGeometry
    {
        /// <summary>前方ガード範囲の既定の半角（度）。前方 180° = ガード方向から左右 90°。</summary>
        public const float FrontGuardHalfAngleDegrees = 90f;

        /// <summary>
        /// ガード方向と「被弾側→攻撃者」方向の角度（0〜180 度）を返す。攻撃の進行方向（攻撃者→対象）を
        /// 符号反転して用いる。いずれかが向き無しなら 0 度（正面扱い）。
        /// </summary>
        public static float IncomingBearingDegrees(Vector3 guardForward, Vector3 attackDirection)
        {
            Vector3 toAttacker = CombatGeometry.DefenderToAttackerFromAttackDirection(attackDirection);
            return CombatGeometry.BearingDegrees(guardForward, toAttacker);
        }

        /// <summary>
        /// 攻撃がガードの前方 180°（既定 ±<paramref name="halfAngleDegrees"/>）以内から来ているか。
        /// 境界（ちょうど半角）は防御可（含む）。向き無しは安全側で true（前方扱い）。
        /// </summary>
        public static bool IsWithinGuardArc(
            Vector3 guardForward,
            Vector3 attackDirection,
            float halfAngleDegrees = FrontGuardHalfAngleDegrees)
        {
            return IncomingBearingDegrees(guardForward, attackDirection) <= halfAngleDegrees;
        }
    }
}
