using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// カメラ追従位置を求める純粋ロジック（Phase1 P1-08）。固定オフセットで見下ろし位置を返す。
    /// </summary>
    public static class CameraFollowMath
    {
        /// <summary>追従対象位置と固定オフセットからカメラ位置を返す。</summary>
        public static Vector3 ComputePosition(Vector3 target, Vector3 offset)
        {
            return target + offset;
        }
    }
}
