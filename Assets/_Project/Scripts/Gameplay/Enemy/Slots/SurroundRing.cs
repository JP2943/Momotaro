using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Slots
{
    /// <summary>
    /// 包囲リングの純粋計算（Phase3 P3-07。§8.1）。対象を中心に、<paramref name="count"/> 体を均等角度で配置した際の
    /// <paramref name="index"/> 番目の位置（XZ 平面）を返す。待機敵がこの点へ向かうことで、同一点へ殺到して単縦列になるのを避け、
    /// 取り囲む。基準角は World +Z（0°）で決定的。半径は攻撃帯の内側に取り、到達後そのまま攻撃を試みられるようにする。
    /// Unity 非依存で EditMode 再現可能。
    /// </summary>
    public static class SurroundRing
    {
        /// <summary>
        /// 対象 <paramref name="center"/> の周囲、半径 <paramref name="radius"/> の円周上で、<paramref name="count"/> 等分した
        /// <paramref name="index"/> 番目の位置を返す（+Z を 0°、時計回りに配分）。<paramref name="count"/> ≤ 0 は 1 とみなす。
        /// </summary>
        public static Vector3 RingPosition(Vector3 center, float radius, int index, int count)
        {
            int n = count > 0 ? count : 1;
            int i = index >= 0 ? index : 0;
            float deg = 360f / n * i;
            Vector3 dir = Quaternion.AngleAxis(deg, Vector3.up) * Vector3.forward;
            return center + dir * Mathf.Max(0.01f, radius);
        }
    }
}
