using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// Scene に置くカメラ領域（P5-06。仕様書 v1.1 §11）。
    ///
    /// 中心は自分の位置、大きさは Inspector の値。<b>回転は見ない</b>：領域は XZ の軸平行矩形で、
    /// 斜めの部屋は矩形の組み合わせで表す（§11）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaCameraRegion : MonoBehaviour
    {
        [Tooltip("この領域の安定 ID（同優先度のときの順位にも使う）。")]
        [SerializeField] private string _regionId = string.Empty;

        [Tooltip("優先度。重なったときは大きい方が勝つ。")]
        [SerializeField] private int _priority;

        [Tooltip("XZ の大きさ（幅, 奥行）。")]
        [SerializeField] private Vector2 _size = new Vector2(20f, 20f);

        /// <summary>Builder・テストからの設定。</summary>
        public void Configure(StableId regionId, int priority, Vector2 size)
        {
            _regionId = regionId.Value ?? string.Empty;
            _priority = priority;
            _size = size;
        }

        /// <summary>純粋な判定へ渡す形。</summary>
        public CameraRegionDefinition Definition =>
            new CameraRegionDefinition(
                string.IsNullOrEmpty(_regionId) ? default : new StableId(_regionId),
                _priority,
                new Vector2(transform.position.x, transform.position.z),
                _size);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireCube(
                new Vector3(transform.position.x, transform.position.y, transform.position.z),
                new Vector3(_size.x, 0.1f, _size.y));
        }
    }
}
