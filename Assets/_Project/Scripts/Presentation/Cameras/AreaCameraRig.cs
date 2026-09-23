using System.Collections.Generic;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// エリアのカメラ（P5-06。仕様書 v1.1 §11）。固定俯角・Orthographic のまま、
    /// 領域に収めた基準位置へ追従する。
    ///
    /// <b>書込み先を分ける。</b> §11 は「基準追従と CameraShake の書込み先を分ける。
    /// 例：Rig 親に追従・境界、Camera 子に揺れ」と決めている。
    /// この部品は<b>自分（Rig）の位置</b>だけを書き、Camera は子として付いてくる。
    /// 揺れは既存の <c>CameraShakePresenter</c> が Camera 子の <c>localPosition</c> を書く。
    /// 同じ Transform を 2 人で書くと、揺れが追従に食われたり、揺れの戻り位置がずれたりする。
    ///
    /// <b>Look Ahead・ズーム演出は入れない</b>（§11。P5 の範囲外）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaCameraRig : MonoBehaviour
    {
        [Tooltip("追従対象（主人公の根）。")]
        [SerializeField] private Transform _target;

        [Tooltip("このエリアのカメラ（この Rig の子。揺れはここへ書かれる）。")]
        [SerializeField] private Camera _camera;

        [Tooltip("この Area の領域。主人公が属する領域を毎フレーム選ぶ。")]
        [SerializeField] private List<AreaCameraRegion> _regions = new List<AreaCameraRegion>();

        [Tooltip("どの領域にも入らないときに使う既定領域（§11）。")]
        [SerializeField] private AreaCameraRegion _defaultRegion;

        [Tooltip("基準位置から見たカメラの俯角（度）。Transform の回転と揃えること。")]
        [SerializeField] private float _pitchDegrees = 55f;

        [Tooltip("部屋切替の補間時間（秒。§11 の初期値は 0.15）。")]
        [SerializeField] private float _blendSeconds = CameraFocusBlend.DefaultBlendSeconds;

        [Tooltip("このエリアの初期化状態。準備完了で即時配置する（§5.1 手順 7）。")]
        [SerializeField] private AreaContext _context;

        private readonly CameraFocusBlend _blend = new CameraFocusBlend();
        private readonly List<CameraRegionDefinition> _buffer = new List<CameraRegionDefinition>();

        /// <summary>補間の状態（診断・テスト用）。</summary>
        public CameraFocusBlend Blend => _blend;

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _target != null && _camera != null && _defaultRegion != null;

        /// <summary>直近に選ばれた領域（診断・テスト用）。</summary>
        public CameraRegionDefinition CurrentRegion { get; private set; }

        /// <summary>補間せずに配置した回数（診断・テスト用）。</summary>
        public int SnapCount { get; private set; }

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(Transform target, Camera camera, AreaCameraRegion defaultRegion,
            IReadOnlyList<AreaCameraRegion> regions = null, AreaContext context = null)
        {
            if (context != null)
            {
                Unsubscribe();
                _context = context;
                Subscribe();
            }

            if (target != null)
            {
                _target = target;
            }

            if (camera != null)
            {
                _camera = camera;
            }

            if (defaultRegion != null)
            {
                _defaultRegion = defaultRegion;
            }

            if (regions != null)
            {
                _regions.Clear();
                for (int i = 0; i < regions.Count; i++)
                {
                    if (regions[i] != null)
                    {
                        _regions.Add(regions[i]);
                    }
                }
            }
        }

        /// <summary>
        /// 補間せずに今すぐ合わせる（Scene 到着・死亡再開。§11／§5.1 手順 7）。
        /// 前の部屋から滑ってくる嘘を作らない。
        /// </summary>
        public void SnapToTarget()
        {
            if (!IsWired)
            {
                return;
            }

            CameraRegionDefinition region = ResolveRegion();
            CurrentRegion = region;
            _blend.SnapTo(_target.position, region, HalfFootprint());
            Apply();
            SnapCount++;
        }

        /// <summary>1 フレーム進める（テストは決定的に直接呼べる）。</summary>
        public void Tick(float deltaTime)
        {
            if (!IsWired)
            {
                return;
            }

            CameraRegionDefinition region = ResolveRegion();
            CurrentRegion = region;
            _blend.Tick(_target.position, region, HalfFootprint(), deltaTime, _blendSeconds);
            Apply();
        }

        /// <summary>床で見える範囲の半分（俯角と画面比から）。</summary>
        public Vector2 HalfFootprint() =>
            CameraBoundsMath.HalfFootprint(_camera.orthographicSize, _camera.aspect, _pitchDegrees);

        private CameraRegionDefinition ResolveRegion()
        {
            _buffer.Clear();
            for (int i = 0; i < _regions.Count; i++)
            {
                if (_regions[i] != null)
                {
                    _buffer.Add(_regions[i].Definition);
                }
            }

            if (CameraRegionSelector.TrySelect(_buffer, _target.position, out CameraRegionDefinition chosen))
            {
                return chosen;
            }

            // どの領域にも入らない＝既定領域（§11）。
            return _defaultRegion.Definition;
        }

        /// <summary>Rig の位置だけを書く。Camera の子（揺れ）は触らない。</summary>
        private void Apply()
        {
            Vector3 focus = _blend.Current;
            transform.position = new Vector3(focus.x, transform.position.y, focus.z);
        }

        private void OnEnable()
        {
            Subscribe();

            // すでに準備が終わっている構成（Rig を後から有効化した・テストが直接組んだ）でも
            // 到着位置を映す。購読だけに頼ると、通知を取りこぼした構成が初期位置のまま残る。
            if (_context != null && _context.IsPrepared)
            {
                SnapToTarget();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_context != null)
            {
                _context.Prepared -= OnAreaPrepared;
                _context.Prepared += OnAreaPrepared;
            }
        }

        private void Unsubscribe()
        {
            if (_context != null)
            {
                _context.Prepared -= OnAreaPrepared;
            }
        }

        /// <summary>
        /// Scene 到着の即時配置（§5.1 手順 7／§11）。<b>補間しない。</b>
        /// 到着した瞬間に前の部屋から滑ってくると、居なかった場所に居たように見える。
        /// </summary>
        private void OnAreaPrepared()
        {
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (!IsWired)
            {
                GameLog.WarningOnce(LogCategory.Scene, "area_camera_unwired",
                    "カメラ Rig が未配線です（対象・カメラ・既定領域のいずれか）。");
                return;
            }

            Tick(Time.deltaTime);
        }
    }
}
