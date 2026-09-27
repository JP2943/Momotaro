using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// その Area が提供するカメラ領域と追従対象（P5.5 付録 A.1）。
    ///
    /// <b>領域は Area 所有のまま。</b> 常駐化するのは Rig・Camera・AudioListener・基準照明だけで、
    /// <c>AreaCameraRegion</c> 自体は各 Area に置く（§4.3）。常駐 Rig は活動 Area に応じて
    /// この集合へ参照を切り替える。
    ///
    /// <b>活動ゲートの外に置く。</b> 領域を提供することはゲーム的な活動ではないので、
    /// 閉じている間も常駐 Rig から在処が分かる必要がある（§4.2 の構造ゲートの外側）。
    /// 追従対象（主人公）はゲートの内側に居るが、Transform 参照は非 Active でも保持できる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaCameraRegionSet : MonoBehaviour
    {
        [Tooltip("この Area の根。どの Area の領域集合かを照合する。")]
        [SerializeField] private AreaRoot _areaRoot;

        [Tooltip("カメラの追従対象（主人公の根）。")]
        [SerializeField] private Transform _followTarget;

        [Tooltip("既定のカメラ領域（どこにも入っていないときに使う）。")]
        [SerializeField] private AreaCameraRegion _defaultRegion;

        [Tooltip("重ね合わせるカメラ領域。優先度が高い方が勝つ。")]
        [SerializeField] private List<AreaCameraRegion> _regions = new List<AreaCameraRegion>();

        [Tooltip("常駐 Rig の Prefab。まだ常駐 Rig が無いときにだけ生成する（付録 A.2）。")]
        [SerializeField] private GameObject _residentRigPrefab;

        /// <summary>この Area の安定 ID（根が未配線なら空）。</summary>
        public StableId AreaId => _areaRoot != null ? _areaRoot.AreaId : default;

        /// <summary>この集合が属する Scene の handle。</summary>
        public int SceneHandle => gameObject.scene.handle;

        /// <summary>カメラの追従対象。</summary>
        public Transform FollowTarget => _followTarget;

        /// <summary>既定のカメラ領域。</summary>
        public AreaCameraRegion DefaultRegion => _defaultRegion;

        /// <summary>重ね合わせる領域（読み取り専用）。</summary>
        public IReadOnlyList<AreaCameraRegion> Regions => _regions;

        /// <summary>常駐 Rig の Prefab（Validator・テスト用）。</summary>
        public GameObject ResidentRigPrefab => _residentRigPrefab;

        /// <summary>配線が揃っているか。</summary>
        public bool IsWired =>
            _areaRoot != null && _followTarget != null && _defaultRegion != null
            && _residentRigPrefab != null;

        private void OnEnable()
        {
            AreaCameraRegionSetRegistry.Register(this);

            // <b>常駐 Rig が無ければ、ここで一度だけ作る</b>（付録 A.2 の ensure-create）。
            //
            // 直開き・統合起動・先読みのすべてが同じこの経路を通る。
            // すでにあれば何もしないので、先読み Scene のロードで再生成されることはない。
            AreaCameraRigHost.EnsureExists(_residentRigPrefab);

            // <b>ここで結び直しを促してはいけない。</b>
            //
            // OnEnable は Scene の読み込み中に走るので、この時点の主人公は<b>まだ入口へ
            // 置かれていない</b>（配置は初期化担当が Awake／Start で行う）。
            // ここで結び直すと保存位置で即時配置してしまい、そのあと入口へ移った主人公を
            // 追って<b>部屋を跨ぐ補間が始まる</b>——到着の瞬間にカメラが滑ってくる
            // （実際に踏んだ：B→A の復帰でカメラが 5m 手前から流れてきた）。
            //
            // 結び直しは常駐 Rig の LateUpdate が拾う。LateUpdate は Awake／Start より後、
            // 描画より前なので、<b>最初に描かれるフレームには正しい位置</b>になっている。
        }

        private void OnDisable()
        {
            AreaCameraRegionSetRegistry.Unregister(this);
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(
            AreaRoot areaRoot,
            Transform followTarget,
            AreaCameraRegion defaultRegion,
            List<AreaCameraRegion> regions,
            GameObject residentRigPrefab)
        {
            _areaRoot = areaRoot;
            _followTarget = followTarget;
            _defaultRegion = defaultRegion;
            _regions = regions ?? new List<AreaCameraRegion>();
            _residentRigPrefab = residentRigPrefab;
        }
#endif
    }

    /// <summary>
    /// 読み込まれている Area の領域集合の索引（付録 A.1）。
    ///
    /// <b>Scene handle で引く。</b> 同じ AreaId を往復すると前のインスタンスと並ぶ瞬間があるため
    /// （§4.3）。「どれが活動中か」は <see cref="CurrentAreaProvider"/> が持つので、ここは持たない。
    /// </summary>
    public static class AreaCameraRegionSetRegistry
    {
        private static readonly List<AreaCameraRegionSet> Sets = new List<AreaCameraRegionSet>();

        /// <summary>登録件数（診断・テスト用）。</summary>
        public static int Count => Sets.Count;

        /// <summary>登録する（二重登録はしない）。</summary>
        public static void Register(AreaCameraRegionSet set)
        {
            if (set != null && !Sets.Contains(set))
            {
                Sets.Add(set);
            }
        }

        /// <summary>解除する。</summary>
        public static void Unregister(AreaCameraRegionSet set)
        {
            if (set != null)
            {
                Sets.Remove(set);
            }
        }

        /// <summary>Scene handle で引く。</summary>
        public static bool TryGetByScene(int sceneHandle, out AreaCameraRegionSet set)
        {
            set = null;
            if (sceneHandle == 0)
            {
                return false;
            }

            for (int i = 0; i < Sets.Count; i++)
            {
                if (Sets[i] != null && Sets[i].SceneHandle == sceneHandle)
                {
                    set = Sets[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ちょうど 1 つだけ載っているときにそれを返す（単一 Area 構成の互換経路）。
        /// 2 つ以上では失敗する——どちらが活動中かはここでは決められない（§4.3）。
        /// </summary>
        public static bool TryGetSingle(out AreaCameraRegionSet set)
        {
            set = null;
            if (Sets.Count != 1)
            {
                return false;
            }

            set = Sets[0];
            return set != null;
        }

        /// <summary>いま載っている領域集合を文字で並べる（診断・失敗時の手掛かり）。</summary>
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("領域集合 ").Append(Sets.Count).Append(" 件:");
            for (int i = 0; i < Sets.Count; i++)
            {
                AreaCameraRegionSet set = Sets[i];
                sb.Append(' ');
                if (set == null)
                {
                    sb.Append("[破棄済み]");
                    continue;
                }

                sb.Append('[').Append(set.AreaId.Value).Append("@").Append(set.SceneHandle).Append(']');
            }

            return sb.ToString();
        }

        /// <summary>テスト間で状態を持ち越さないための掃除（テスト専用）。</summary>
        public static void ClearForTests()
        {
            Sets.Clear();
        }
    }
}
