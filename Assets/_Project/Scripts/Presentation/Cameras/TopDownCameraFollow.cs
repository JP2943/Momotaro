using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// Orthographic の見下ろしカメラを Player へ追従させる（Phase1 P1-08）。回転は Inspector で固定角に設定し、
    /// 位置のみ固定オフセットで追従する。物理後（LateUpdate）に更新して目立つ Jitter を避ける。
    /// Target 消失時は何もしない（例外を出さない）。境界・Boss・演出カメラは扱わない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TopDownCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [Tooltip("対象からのカメラ位置オフセット（見下ろし角は Transform の回転で設定）。")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, 12f, -8f);

        /// <summary>追従対象を差し替える。</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            transform.position = CameraFollowMath.ComputePosition(_target.position, _offset);
        }
    }
}
