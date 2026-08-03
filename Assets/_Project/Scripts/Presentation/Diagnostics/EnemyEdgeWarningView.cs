using Momotaro.Gameplay.Enemy.Screen;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 画面外遠距離射撃の画面端警告（仮）。Phase3 P3-08。§9.2。<see cref="IOffscreenWarningService"/> を実装し、有効時に
    /// <see cref="OffscreenWarningProvider"/> へ自身を注入する。射撃要求時、発射者方向を画面端の座標へ射影して短時間マーカーを表示し、
    /// 表示できた（カメラが存在する）場合に true を返す＝画面外射撃を許可する。完成 UI・演出は範囲外の仮実装で、Gameplay の正本ではない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyEdgeWarningView : MonoBehaviour, IOffscreenWarningService
    {
        [Tooltip("警告を使用するか（既定 有効）。無効時は警告不可＝画面外射撃を抑止する。")]
        [SerializeField] private bool _enabled = true;

        [Tooltip("警告マーカーの表示継続秒。")]
        [SerializeField] private float _displaySeconds = 0.6f;

        [Tooltip("画面端からの内側マージン（px）。")]
        [SerializeField] private float _edgeMargin = 24f;

        [Tooltip("判定に用いるカメラ（未指定なら Camera.main）。")]
        [SerializeField] private Camera _camera;

        private Vector3 _sourceWorld;
        private float _hideAtTime;

        private void OnEnable() => OffscreenWarningProvider.Current = this;

        private void OnDisable()
        {
            if (ReferenceEquals(OffscreenWarningProvider.Current, this))
            {
                OffscreenWarningProvider.Current = null;
            }
        }

        private Camera Cam => _camera != null ? _camera : Camera.main;

        /// <inheritdoc />
        public bool TryShowWarning(Vector3 sourceWorldPos, Vector3 targetWorldPos)
        {
            if (!_enabled || Cam == null)
            {
                return false; // 警告を出せない＝画面外射撃不可。
            }

            _sourceWorld = sourceWorldPos;
            _hideAtTime = Time.time + _displaySeconds;
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (Time.time > _hideAtTime)
            {
                return;
            }

            Camera cam = Cam;
            if (cam == null)
            {
                return;
            }

            Vector3 sp = cam.WorldToScreenPoint(_sourceWorld);
            float x = Mathf.Clamp(sp.x, _edgeMargin, Screen.width - _edgeMargin);
            float y = Mathf.Clamp(Screen.height - sp.y, _edgeMargin, Screen.height - _edgeMargin);
            GUI.Label(new Rect(x - 40f, y - 10f, 90f, 20f), "▲ 射撃警告");
        }
#endif
    }
}
