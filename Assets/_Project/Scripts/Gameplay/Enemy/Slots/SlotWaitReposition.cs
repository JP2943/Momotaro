using Momotaro.Gameplay.Enemy.Perception;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Slots
{
    /// <summary>
    /// Slot 待ち・攻撃不可時の間合い調整（Phase3 §8.1「Slot なしの敵は Reposition し、包囲・距離調整・威嚇を行う」）の純粋計算。
    /// 対象を中心に停止帯の半径で周回する目標点を返し、待機敵が棒立ちにならないようにする。方向符号を敵ごとに変えると包囲になる。
    /// Unity 非依存で EditMode 再現可能。
    /// </summary>
    public static class SlotWaitReposition
    {
        /// <summary>
        /// 対象を中心に半径 <paramref name="radius"/> の円周上を、現在方位から <paramref name="sign"/>×<paramref name="stepDegrees"/>
        /// だけ回した位置を返す（XZ 平面）。自他が重なる場合は既定方向へ退避する。<paramref name="sign"/> は +1/−1（周回方向）。
        /// </summary>
        public static Vector3 OrbitTarget(Vector3 selfPos, Vector3 targetPos, float radius, float sign, float stepDegrees)
        {
            Vector3 fromTarget = selfPos - targetPos;
            fromTarget.y = 0f;
            if (fromTarget.sqrMagnitude < 1e-6f)
            {
                fromTarget = Vector3.back; // 完全重なりの既定方位。
            }

            fromTarget.Normalize();
            float s = sign >= 0f ? 1f : -1f;
            Quaternion rot = Quaternion.AngleAxis(s * stepDegrees, Vector3.up);
            Vector3 dir = rot * fromTarget;
            return targetPos + dir * Mathf.Max(0.01f, radius);
        }

        /// <summary>所有者 ID から安定した周回方向（+1/−1）を得る（偶奇で左右に分けて包囲を作る）。</summary>
        public static float DirectionSign(int ownerId) => (ownerId & 1) == 0 ? 1f : -1f;

        /// <summary>対象までの XZ 距離（利便）。</summary>
        public static float PlanarDistance(Vector3 a, Vector3 b) => VisionCheck.PlanarDistance(a, b);
    }
}
