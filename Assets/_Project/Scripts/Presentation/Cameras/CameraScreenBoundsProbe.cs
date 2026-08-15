using Momotaro.Gameplay.Enemy.Screen;
using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// Camera による画面内判定アダプタ（Phase3 §8.2）。<see cref="IScreenBoundsProbe"/> を実装し、World 座標を Viewport へ射影して
    /// 境界余白込みで画面内かを判定する。純粋計算は <see cref="ViewportBounds"/> へ委譲し、Gameplay は Camera API へ直接依存しない。
    /// 有効時に自身を <see cref="ScreenBoundsProvider"/> へ注入する。カメラ未指定時は <see cref="Camera.main"/> を用いる。
    /// 余白（Data 化）は境界付近の ON／OFF 振動を防ぐ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraScreenBoundsProbe : MonoBehaviour, IScreenBoundsProbe
    {
        [Tooltip("判定に用いるカメラ（未指定なら同 GameObject → Camera.main の順で解決）。")]
        [SerializeField] private Camera _camera;

        [Tooltip("Viewport 境界の余白（0..1）。外側この割合まで画面内とみなし、境界付近の攻撃開始のちらつきを防ぐ。")]
        [SerializeField] private float _margin01 = 0.05f;

        private void Awake()
        {
            ResolveCamera();
        }

        private void OnEnable()
        {
            ResolveCamera();
            ScreenBoundsProvider.Current = this; // Gameplay へ注入。
        }

        private void OnDisable()
        {
            if (ReferenceEquals(ScreenBoundsProvider.Current, this))
            {
                ScreenBoundsProvider.Current = null;
            }
        }

        private void ResolveCamera()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }
        }

        /// <inheritdoc />
        public bool IsOnScreen(Vector3 worldPos)
        {
            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                return true; // カメラ不在時は進行（Gameplay を止めない）。
            }

            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            return ViewportBounds.IsInside(new Vector2(vp.x, vp.y), vp.z > 0f, _margin01);
        }
    }
}
