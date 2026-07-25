using UnityEngine;

namespace Momotaro.Presentation.Characters
{
    /// <summary>
    /// このコンポーネントが付いた Transform（キャラの VisualRoot 等）を、対象カメラの World 回転へ正対させ、
    /// さらにカメラ視線方向へわずかに手前へずらす描画専用オフセットを与えるビルボード（Phase2 表示基盤修正）。
    ///
    /// 目的1（正対）：Orthographic かつ俯角のあるカメラでは、正対しない板ポリ Sprite が縦方向に cos(角度) 倍へ
    /// 圧縮されて見えるため、Sprite 面をカメラ面と平行に保ち元の縦横比を維持する。
    /// 目的2（Depth 安定化）：45 度俯瞰では傾いた Sprite の上半分が壁 3D Mesh と描画深度で交差し、上半身だけ隠れる
    /// ことがある。基準アンカーからカメラ側（-camera.forward）へ <see cref="DepthOffset"/> だけ移動させて解消する。
    ///
    /// 回転と「基準位置からの表示位置」のみを自身の Transform に適用し、親・兄弟（Character Root / Physics /
    /// Collider / Shadow）や Scale には一切触れない。オフセットは毎回「基準アンカー」から再計算するため累積しない。
    /// Orthographic 前提（視線軸移動は画面上の位置・大きさ・縦横比・足元を変えない）。Perspective 一般化は対象外。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraFacingBillboard : MonoBehaviour
    {
        [Tooltip("正対させる対象カメラ。未指定なら Main Camera を取得してキャッシュする。")]
        [SerializeField] private Camera _camera;

        [Tooltip("カメラ視線方向へ手前（-camera.forward）へずらす描画専用オフセット（m）。壁際の部分遮蔽の解消用。")]
        [SerializeField, Min(0f)] private float _depthOffset = 0.5f;

        private Camera _cached;
        private Vector3 _baseLocalPosition;
        private bool _anchorCaptured;

        /// <summary>採用中の Depth Offset（診断・報告用）。</summary>
        public float DepthOffset => _depthOffset;

        /// <summary>対象カメラを差し替える（カメラキャッシュをリセットする）。</summary>
        public void SetCamera(Camera camera)
        {
            _camera = camera;
            _cached = null;
        }

        /// <summary>Depth Offset を設定する（テスト・調整用）。負値は 0 に丸める。</summary>
        public void SetDepthOffset(float depthOffset)
        {
            _depthOffset = depthOffset < 0f ? 0f : depthOffset;
        }

        /// <summary>
        /// 表示位置を求める純粋関数：基準アンカー（World）からカメラ視線方向の手前へ offset だけ移動させる。
        /// 毎フレームこの結果を基準から再計算するため、オフセットは累積しない。
        /// </summary>
        public static Vector3 ComputeDisplayPosition(Vector3 anchorWorld, Vector3 cameraForward, float depthOffset)
        {
            return anchorWorld - cameraForward * depthOffset;
        }

        /// <summary>
        /// 対象カメラへ正対し、基準アンカーからカメラ側へ Depth Offset を適用する。カメラが無ければ何もしない
        /// （例外を出さない）。位置は必ず基準アンカーから再計算（累積なし）、Scale は変更しない。テストからも直接呼べる。
        /// </summary>
        public void AlignToCamera()
        {
            if (!_anchorCaptured)
            {
                CaptureAnchor();
            }

            Camera cam = ResolveCamera();
            if (cam == null)
            {
                return;
            }

            Vector3 cameraForward = cam.transform.forward;
            transform.position = ComputeDisplayPosition(AnchorWorld(), cameraForward, _depthOffset);
            transform.rotation = cam.transform.rotation;
        }

        /// <summary>基準アンカーの World 位置（親 Character Root の移動を反映）。</summary>
        private Vector3 AnchorWorld()
        {
            Transform parent = transform.parent;
            return parent != null ? parent.TransformPoint(_baseLocalPosition) : _baseLocalPosition;
        }

        /// <summary>基準の Local Position を保持する（設計時位置。オフセット適用前に一度だけ取得）。</summary>
        private void CaptureAnchor()
        {
            _baseLocalPosition = transform.localPosition;
            _anchorCaptured = true;
        }

        private Camera ResolveCamera()
        {
            if (_camera != null)
            {
                return _camera;
            }

            if (_cached == null)
            {
                _cached = Camera.main;
            }

            return _cached;
        }

        private void Awake()
        {
            CaptureAnchor();
        }

        private void OnEnable()
        {
            // Disable/Enable や Scene 切替後に取り直せるよう、カメラキャッシュをクリアする。
            // 基準アンカーは Awake で確定済みのため再取得しない（オフセットの累積・破壊を防ぐ）。
            _cached = null;
        }

        private void LateUpdate()
        {
            AlignToCamera();
        }
    }
}
