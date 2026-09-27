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

        private readonly AreaCameraSlide _slide = new AreaCameraSlide();
        private AreaCameraRegionSet _boundSet;
        private int _boundSceneHandle;
        private bool _arrivalPending;
        private bool _holdingAfterSlide;

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

        /// <summary>
        /// 結び直したが<b>まだ実カメラへ適用していない</b>（診断・テスト用）。
        /// この間は通常追従を止める（付録 A.10）。
        /// </summary>
        public bool ArrivalPending => _arrivalPending;

        /// <summary>いま走っているスライド（診断・テスト用）。</summary>
        public AreaCameraSlide Slide => _slide;

        /// <summary>スライド中か。</summary>
        public bool IsSliding => _slide.IsRunning;

        /// <inheritdoc />
        public float SlideEased => _slide.Eased;

        /// <inheritdoc />
        public bool TryGetRigPosition(out Vector3 position)
        {
            if (_rig == null)
            {
                position = default;
                return false;
            }

            position = _rig.transform.position;
            return true;
        }

        /// <summary>スライドを始めた回数（診断・テスト用）。</summary>
        public int SlideCount { get; private set; }

        /// <summary>
        /// スライドが終わり、<b>結び直しを待って終点に留まっている</b>（診断・テスト用）。
        ///
        /// 終点は<b>到着 Area の</b>領域で収めた位置なので、出発 Area に結び付いたまま
        /// 通常追従へ戻すと出発側へ引き戻される。結び直しが来るまでここで止める。
        /// </summary>
        public bool IsHoldingAfterSlide => _holdingAfterSlide;

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

            // <b>ここでは実カメラへ触らない</b>（付録 A.10）。
            //
            // 結び直しが起きるのは Scene が載った直後で、その時点の主人公はまだ
            // <b>保存位置</b>に居ることがある（入口への配置は初期化担当が行う）。
            // ここで即時配置すると保存位置を基準に置いてしまい、そのあと入口へ移った
            // 主人公を追って<b>部屋を跨ぐ補間が始まる</b>（実際に踏んだ：B→A の復帰で
            // カメラが 5m 手前から流れてきた）。
            //
            // 適用は「その Area の入口配置が終わった」ことを確かめてから
            // （<see cref="TryApplyPendingArrival"/>）。それまでは通常追従も止める。
            // スライド後の留まりは、結び直しが来た時点で解く（待っていたのはこれ）。
            _holdingAfterSlide = false;
            _arrivalPending = true;
            SuspendFollowWhilePending();
            return true;
        }

        /// <summary>
        /// 移動先の到着点を<b>現在の Bind を変えずに</b>計算する（付録 A.3／A.10）。
        ///
        /// 引数なしの <see cref="TryComputeArrivalPoint(out Vector3)"/> は
        /// <b>いま結び付いている Area</b>の追従位置を返すので、
        /// 「A を遊んでいる間に B の到着点を知りたい」には答えられない。
        /// スライドの開始・終了点を決めるにはこちらを使う。
        ///
        /// <b>Bind を切り替えて測ってはいけない。</b> 切り替えるとそれだけで
        /// 領域・追従対象が入れ替わり、スライドが始まる前にカメラが動く（§7.1）。
        /// 画角はカメラと俯角だけで決まるので、Bind 先とは無関係に測れる。
        /// </summary>
        /// <param name="destination">移動先の読み込み実体（<see cref="AreaRuntimeBundle.Instance"/>）。</param>
        /// <param name="arrivalPosition">その Area での到着位置（入口の世界座標）。</param>
        public bool TryComputeArrivalPoint(
            AreaInstanceHandle destination, Vector3 arrivalPosition, out Vector3 point)
        {
            point = default;
            if (_rig == null || _camera == null || !destination.IsValid)
            {
                return false;
            }

            if (!AreaBundleDirectory.TryGetByInstance(destination, out AreaRuntimeBundle bundle)
                || bundle == null)
            {
                return false;
            }

            if (!AreaCameraRegionSetRegistry.TryGetByScene(bundle.SceneHandle,
                    out AreaCameraRegionSet set))
            {
                return false;
            }

            if (!set.TryResolveRegion(arrivalPosition, out CameraRegionDefinition region))
            {
                return false;
            }

            if (!_rig.TryGetHalfFootprint(out Vector2 half))
            {
                return false;
            }

            // 領域選択も clamp も Rig と同じ純粋関数（§11 の期待値をテストが作るのと同じもの）。
            point = CameraBoundsMath.ClampFocus(arrivalPosition, region, half);
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

            // 適用したので保留を解き、通常追従を戻す（付録 A.10）。
            _arrivalPending = false;
            _rig.FollowSuspended = false;
        }

        /// <summary>
        /// 結び直しの保留を<b>準備完了を確かめてから</b>適用する（付録 A.10）。
        ///
        /// 「準備完了」はその Area の初期化担当が<b>到着位置へ置いたあと</b>に出す報告
        /// （<see cref="AreaCameraRegionSet.IsReadyForArrival"/>）。それを待つことで、
        /// 保存位置ではなく到着位置を基準に配置できる。
        ///
        /// <b>先読みで載った Area はここを通らない。</b> 結び付ける相手は活動 Area だけなので
        /// （<see cref="TryResolveActiveSet"/>）、Staged の Area が準備完了を報告しても
        /// 実カメラは動かない——§7.1「事前に境界位置へ瞬間移動させない」が守られる。
        /// </summary>
        /// <summary>
        /// スライドを始める（§7.1／付録 A.4。工程 P55-04a）。
        ///
        /// <b>始点は「いまの実 Rig 位置」</b>——事前に境界位置へ瞬間移動させない（§7.1）。
        /// 終点は呼び出し側が <see cref="TryComputeArrivalPoint(AreaInstanceHandle, Vector3, out Vector3)"/>
        /// で先に計算しておく（到着 Area の通常追従・clamp が算出する位置）。
        ///
        /// <b>期間中は通常追従の書込を止める</b>（付録 A.4 の「書込の一系統化」）。
        /// 止めないと、同じフレームに追従とスライドが両方書いて、どちらが見えているのか
        /// 分からなくなる。回転・投影・<c>orthographicSize</c> は<b>一切触らない</b>（§7.1）。
        ///
        /// <b>開始時に残留揺れをゼロへ</b>（§7.1）。揺れを残したまま始めると、
        /// 演出の最中に前の戦闘の揺れが混じる。
        /// </summary>
        public bool BeginSlide(Vector3 to, float seconds)
        {
            if (_rig == null || _camera == null)
            {
                return false;
            }

            if (_shake != null)
            {
                _shake.Stop();
            }

            _rig.FollowSuspended = true;
            _slide.Begin(_rig.transform.position, to, seconds);
            SlideCount++;
            return true;
        }

        /// <summary>
        /// スライドを 1 フレーム進める（unscaled を渡す。§7.1）。
        ///
        /// 戻り値は<b>まだ走っているか</b>。終わったフレームでも終点は適用してから false を返すので、
        /// 呼び出し側は「false になったら <see cref="EndSlide"/>」でよい。
        /// </summary>
        public bool TickSlide(float unscaledDeltaTime)
        {
            if (!_slide.IsRunning || _rig == null)
            {
                return false;
            }

            bool running = _slide.Tick(unscaledDeltaTime);
            _rig.ApplySlidePosition(_slide.Position);
            return running;
        }

        /// <summary>
        /// スライドを終える（付録 A.4）。
        ///
        /// 終点を<b>厳密に</b>適用し、追従の内部状態も終点へ同期する——
        /// 同期しないと次の <c>Tick</c> で前の値へ引き戻され、<b>翌フレームに跳ね返る</b>。
        ///
        /// <b>通常追従はここでは戻さない。</b> 終点は到着 Area の領域で収めた位置なので、
        /// まだ出発 Area に結び付いたまま追従を戻すと、出発側の領域へ引き戻される。
        /// 戻すのは<b>活動 Area が入れ替わって入口配置が終わったとき</b>——
        /// 既にある結び直しと即時配置の経路（付録 A.10）へそのまま渡す。
        /// </summary>
        public void EndSlide()
        {
            if (_rig == null)
            {
                return;
            }

            _slide.Finish();
            _rig.ApplySlidePosition(_slide.Position);
            _rig.SyncFollowStateTo(_slide.Position);

            // <b>結び直しを待って終点に留まる。</b>
            //
            // ここで「適用待ち」（<see cref="_arrivalPending"/>）にしてはいけない——
            // 適用待ちは「結び先が変わった」ことの印で、いま結び付いているのは<b>出発</b> Area
            // である。出発側は準備が済んでいるので、次のフレームに即時配置が走って
            // <b>終点から出発側へ 12m 引き戻された</b>（実際に踏んだ）。
            // 留まるのは別の状態として持ち、結び直しが来たときに解く。
            _holdingAfterSlide = true;
            _rig.FollowSuspended = true;
        }

        /// <summary>
        /// スライドを打ち切って出発位置へ戻す（§8「同じ描画経路を逆向きに戻す」の土台）。
        /// <b>終点へは進めない。</b> 失敗した遷移で到着側の位置を見せない。
        /// </summary>
        public void CancelSlide()
        {
            if (_rig == null)
            {
                return;
            }

            _slide.Cancel();
            _rig.ApplySlidePosition(_slide.From);
            _rig.SyncFollowStateTo(_slide.From);
            _holdingAfterSlide = false;
            _rig.FollowSuspended = false;
        }

        private bool TryApplyPendingArrival()
        {
            if (!_arrivalPending || _holdingAfterSlide || !IsWired || !_rig.IsWired)
            {
                return false;
            }

            if (!_boundSet.IsReadyForArrival)
            {
                return false; // まだ入口へ置かれていない。追従も止めたまま待つ。
            }

            ApplyArrival();
            return true;
        }

        /// <summary>保留中は通常追従を止める（準備前の書込みを一切させない）。</summary>
        private void SuspendFollowWhilePending()
        {
            if (_rig != null)
            {
                _rig.FollowSuspended = _arrivalPending;
            }
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

            // <b>遷移中の表示代理も常駐で持つ</b>（§7.2）。代理は出発 Scene が撤去されても
            // 運び続ける必要があるので Area Scene には置けない。Camera と同じ寿命・同じ唯一性で
            // 管理したいので、同じ物体へ足す。Prefab 側の配線に頼らないのは、配線漏れが
            // 「スライド中だけ主人公が消える」という追いにくい壊れ方になるため。
            if (GetComponent<Transition.AreaTransitionDisplayHost>() == null)
            {
                gameObject.AddComponent<Transition.AreaTransitionDisplayHost>();
            }
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
            // <b>参照を結ぶだけ。</b> 実カメラへは触らない（付録 A.10）。
            //
            // 最初のフレームから配線が揃っているようにしておく——同じフレームに走る
            // <c>AreaCameraRig.LateUpdate</c> との実行順は宣言できないので、
            // 結び付けを後回しにすると未配線の警告を 1 度出してから結び付くことがある。
            // 適用は <see cref="Update"/> が準備完了を確かめて行う。
            TryBindActiveArea();
        }

        /// <summary>
        /// <b>Update で回す（LateUpdate ではない）。</b>
        ///
        /// 追従を書くのは <c>AreaCameraRig.LateUpdate</c> なので、結び直しと即時配置は
        /// それより<b>確実に前</b>で起きる必要がある。Unity は同じフレームの
        /// すべての Update をすべての LateUpdate より前に回すので、
        /// ここに置けば<b>実行順を宣言せずに</b>順序が決まる
        /// （どちらも LateUpdate だと、どちらが先か分からない）。
        ///
        /// <c>Awake</c>／<c>Start</c> より後なので、その Area の初期化（入口配置と
        /// 準備完了の報告）は同じフレームのここまでに済んでいる。
        /// </summary>
        private void Update()
        {
            PollCount++;

            // 1. 活動 Area が変わったら参照を結び直す（実カメラへは触らない）。
            TryBindActiveArea();

            // 2. 入口配置が終わっていれば即時配置する。終わっていなければ追従ごと待つ。
            TryApplyPendingArrival();
        }
    }
}
