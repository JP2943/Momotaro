using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;
using Momotaro.Presentation.Combat;
using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// 単一常駐 CameraRig（P5.5 §4.3／付録 A）。
    ///
    /// <b>Camera・AudioListener・基準照明を所有し、Area をまたいで生き続ける。</b>
    /// Area が提供するのは領域と追従対象だけで（<see cref="AreaCameraRegionSet"/>）、
    /// この Rig が活動 Area に応じて<b>参照を切り替える</b>。Commit で Camera を交換しない（付録 A.5）。
    ///
    /// <b>Prepared では実カメラへ触らない</b>（付録 A.3）。内側の <see cref="AreaCameraRig"/> へ
    /// <c>AreaContext</c> を渡さないので、準備完了通知による <c>SnapToTarget</c> は起きない。
    /// 到着点の計算と適用は別の入口（<see cref="TryComputeArrivalPoint"/>／<see cref="ApplyArrival"/>）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaCameraRigHost : MonoBehaviour, IAreaCameraOwner
    {
        [Tooltip("追従・clamp を実際に行う Rig。この物体の子に置く。")]
        [SerializeField] private AreaCameraRig _rig;

        [Tooltip("この Rig が所有する Camera。")]
        [SerializeField] private Camera _camera;

        [Tooltip("この Rig が所有する画面揺れ。Camera 子の localPosition へ書く（§11 の書込み先の分離）。")]
        [SerializeField] private CameraShakePresenter _shake;

        private AreaCameraRegionSet _boundSet;
        private int _boundSceneHandle;

        /// <summary>いま生きている常駐 Rig（無ければ null）。</summary>
        public static AreaCameraRigHost Instance { get; private set; }

        /// <summary>生成した回数（診断・テスト用）。<b>1 を超えてはいけない</b>（付録 A.2）。</summary>
        public static int CreatedCount { get; private set; }

        /// <inheritdoc />
        public bool IsWired => _rig != null && _camera != null && _boundSet != null;

        /// <inheritdoc />
        public StableId BoundArea { get; private set; }

        /// <inheritdoc />
        public int BindCount { get; private set; }

        /// <inheritdoc />
        public int ApplyCount { get; private set; }

        /// <summary>この Rig が所有する Camera（Validator・テスト用）。</summary>
        public Camera Camera => _camera;

        /// <summary>内側の Rig（テスト用）。</summary>
        public AreaCameraRig Rig => _rig;

        /// <summary>いま結び付いている領域集合の Scene handle（診断・テスト用）。</summary>
        public int BoundSceneHandle => _boundSceneHandle;

        /// <summary>結び直しを試した回数（診断・テスト用。毎フレーム増える）。</summary>
        public int PollCount { get; private set; }

        /// <summary>この Rig が所有する画面揺れ（Validator・テスト用）。</summary>
        public CameraShakePresenter Shake => _shake;

        /// <summary>
        /// 常駐 Rig が居なければ作る（付録 A.2 の ensure-create）。
        ///
        /// <b>すでに居れば何もしない。</b> 先読みで 2 枚目の Area が載っても再生成されない。
        /// Prefab が未配線なら何もしない——カメラが無いこと自体は Validator が落とす。
        /// </summary>
        public static void EnsureExists(GameObject residentRigPrefab)
        {
            if (Instance != null || residentRigPrefab == null)
            {
                return;
            }

            GameObject created = Instantiate(residentRigPrefab);
            created.name = residentRigPrefab.name;
            DontDestroyOnLoad(created);
            CreatedCount++;
        }

        /// <summary>テスト間で数を持ち越さないための掃除（テスト専用）。</summary>
        public static void ResetDiagnosticsForTests()
        {
            CreatedCount = 0;
        }

        /// <inheritdoc />
        public bool TryBindActiveArea()
        {
            if (_rig == null || _camera == null)
            {
                return false;
            }

            if (!TryResolveActiveSet(out AreaCameraRegionSet set))
            {
                return false;
            }

            // <b>Scene handle では足りない。</b> Single 読込で A を捨てて B を載せると
            // Unity は同じ handle を使い回すことがあり、handle だけを見ると
            // 「すでに結び付いている」と誤判定して<b>破棄済みの A の追従対象を持ち続ける</b>
            // （実際に踏んだ：B 到着後も BoundArea が A のまま、IsWired が false）。
            // 参照の同一性で見れば取り違えようがない。
            if (ReferenceEquals(_boundSet, set))
            {
                return true; // すでにこの Area へ結び付いている。参照を触らない。
            }

            // <b>Camera は渡し替えない</b>（付録 A.5）。領域と追従対象だけを差し替える。
            // AreaContext は渡さない——Prepared による Snap を起こさないため（付録 A.3）。
            _rig.Bind(set.FollowTarget, _camera, set.DefaultRegion, set.Regions);
            _boundSet = set;
            _boundSceneHandle = set.SceneHandle;
            BoundArea = set.AreaId;
            BindCount++;

            // <b>結び直した直後は補間しない</b>（§11「Scene 到着・死亡再開では補間せず即時配置」）。
            //
            // 前の Area の位置から滑ってくると、居なかった場所に居たように見える。
            // ここを通るのは<b>活動 Area が入れ替わったとき</b>だけで、Prepared では通らない
            // （付録 A.3。準備完了の時点では到着点を計算するだけ）。
            // 先読みで 2 枚目が載っても活動 Area は変わらないので、ここも通らない。
            ApplyArrival();
            return true;
        }

        /// <inheritdoc />
        public bool TryComputeArrivalPoint(out Vector3 point)
        {
            point = default;
            if (!IsWired || !_rig.IsWired)
            {
                return false;
            }

            // 実カメラへは触らない。通常追従・clamp が算出する位置だけを返す（§7.1）。
            return _rig.TryComputeFocus(out point);
        }

        /// <inheritdoc />
        public void ApplyArrival()
        {
            if (!IsWired || !_rig.IsWired)
            {
                return;
            }

            _rig.SnapToTarget();
            ApplyCount++;
        }

        private bool TryResolveActiveSet(out AreaCameraRegionSet set)
        {
            int active = CurrentAreaProvider.ActiveSceneHandle;
            if (active != 0 && AreaCameraRegionSetRegistry.TryGetByScene(active, out set))
            {
                return true;
            }

            // 現行の指定が無いときは、Area がちょうど 1 つのときだけ結び付く
            // （直開き・単一 Area 構成）。2 つ載っていれば当て推量しない（§4.3）。
            return AreaCameraRegionSetRegistry.TryGetSingle(out set);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 二つ目は作らせない（付録 A.1「Area 間で重複させない」）。
                Destroy(gameObject);
                return;
            }

            Instance = this;
            AreaCameraOwnerProvider.TrySetCurrent(this, this);

            // 揺れも常駐側が所有する（付録 A.1）。Area Scene は Camera を持たないので、
            // 演出の調停役は Scene 構築時に配線できない——実行時にここから解決する（付録 A.2）。
            CameraShakeProvider.TrySetCurrent(this, _shake);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }

            AreaCameraOwnerProvider.ReleaseIfOwner(this);
            CameraShakeProvider.ReleaseIfOwner(this);
        }

        private void OnEnable()
        {
            // 最初のフレームから結び付いていること。
            //
            // <see cref="LateUpdate"/> だけに任せると、同じフレームに走る
            // <c>AreaCameraRig.LateUpdate</c> との順序が保証されず、
            // 未配線の警告を 1 度出してから結び付くことがある（実行順は宣言できない）。
            TryBindActiveArea();
        }

        private void LateUpdate()
        {
            // 活動 Area が変わったら結び直す。変わっていなければ何もしない。
            PollCount++;
            TryBindActiveArea();
        }
    }
}
