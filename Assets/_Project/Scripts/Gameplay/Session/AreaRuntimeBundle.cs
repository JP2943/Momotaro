using Momotaro.Core.Identification;
using Momotaro.Gameplay.Encounter;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// その Area Scene の参照集合（P5.5 §4.3。棚卸し報告書 §5 の成果物 1）。
    ///
    /// <b>常駐から Scene 側を探さないための口。</b> P5 は Single 読込だったので
    /// <c>FindFirstObjectByType&lt;AreaContext&gt;()</c> が「いま遊んでいるエリアの Context」と
    /// 一致していた。P5.5 では Area が 2 つ同時に在るため、この等式が崩れる——
    /// 先読み中の隣エリアの Context を掴んで、そちらを止めたり採取したりしてしまう。
    ///
    /// そこで各 Area Scene が自分の部品を明示参照で束ね、常駐は
    /// <see cref="AreaBundleDirectory"/> から<b>Scene を指定して</b>引く。
    ///
    /// <b>この部品は Staged の間も有効でなければならない。</b> 置き場所は
    /// GameplayRoot／PhysicsRoot の<b>外</b>（AreaRoot と同じ物体）。
    /// 束ねること自体はゲーム的な活動ではないので、活動ゲートの内側に入れると
    /// 「読み込んだのに誰も部品の在処を知らない」状態になる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaRuntimeBundle : MonoBehaviour
    {
        [Header("このエリアの根")]
        [Tooltip("AreaId・入口・出入口・仕掛けの明示参照を持つ根。")]
        [SerializeField] private AreaRoot _areaRoot;

        [Tooltip("初期化状態（AreaReady）。常駐の遷移サービスが活動の開閉に使う。")]
        [SerializeField] private AreaContext _context;

        [Tooltip("Actor 値の採取・復元の窓口（§4.4〜§4.6）。出発側の採取に使う。")]
        [SerializeField] private AreaActorTransferPort _transferPort;

        [Tooltip("遷移の受付条件を読む窓口。")]
        [SerializeField] private AreaTransitionConditionsSource _conditions;

        [Tooltip("この区画の Encounter（戦闘の無い区画は未割当でよい）。")]
        [SerializeField] private AreaEncounterRunner _encounter;

        [Tooltip("本編型死亡再開の実行役（§9.1）。載せない構成もある。")]
        [SerializeField] private CampaignRespawnRunner _respawn;

        [Header("活動ゲート（§4.2）")]
        [Tooltip("Gameplay の根。保存時から非 Active。Commit でだけ有効化する。")]
        [SerializeField] private GameObject _gameplayRoot;

        [Tooltip("物理の根。保存時から非 Active。")]
        [SerializeField] private GameObject _physicsRoot;

        /// <summary>このエリアの根。</summary>
        public AreaRoot Root => _areaRoot;

        /// <summary>初期化状態。</summary>
        public AreaContext Context => _context;

        /// <summary>Actor 値の採取・復元の窓口。</summary>
        public AreaActorTransferPort TransferPort => _transferPort;

        /// <summary>遷移の受付条件を読む窓口。</summary>
        public AreaTransitionConditionsSource Conditions => _conditions;

        /// <summary>この区画の Encounter。</summary>
        public AreaEncounterRunner Encounter => _encounter;

        /// <summary>死亡再開の実行役。</summary>
        public CampaignRespawnRunner Respawn => _respawn;

        /// <summary>Gameplay の根（活動ゲートの対象）。</summary>
        public GameObject GameplayRoot => _gameplayRoot;

        /// <summary>物理の根（活動ゲートの対象）。</summary>
        public GameObject PhysicsRoot => _physicsRoot;

        /// <summary>このエリアの安定 ID（根が未配線なら空）。</summary>
        public StableId AreaId => _areaRoot != null ? _areaRoot.AreaId : default;

        /// <summary>この束が属する Scene の handle。<see cref="AreaBundleDirectory"/> の鍵。</summary>
        public int SceneHandle => gameObject.scene.handle;

        /// <summary>
        /// 常駐が割り当てた読み込み実体の識別子（§4.3）。未割当なら
        /// <see cref="AreaInstanceHandle.None"/>。<b>Scene 側からは決めない。</b>
        /// 世代は常駐の台帳が単調増加で配るもので、Scene が自称すると往復で衝突する。
        /// </summary>
        public AreaInstanceHandle Instance { get; private set; }

        /// <summary>常駐が実体の識別子を結び付ける。</summary>
        public void BindInstance(AreaInstanceHandle handle)
        {
            Instance = handle;
        }

        /// <summary>
        /// 必須参照が揃っているか（Validator・テスト用）。
        /// Encounter・Respawn・物理の根は構成によって無い場合があるので必須に含めない。
        /// </summary>
        public bool IsWired =>
            _areaRoot != null && _areaRoot.Definition != null && _context != null;

        /// <summary>
        /// この Area Scene の中だけを探して部品を引く（層をまたぐ部品のための口）。
        ///
        /// <b>全 Scene 検索の代わり。</b> Presentation・Infrastructure の部品は
        /// Gameplay から型参照できないので明示参照フィールドを持てない。
        /// それでも「どの Scene の物か」だけは確実に絞りたいので、
        /// この Scene の根から下だけを対象にする。非 Active も含める
        /// （Staged／Prepared の間は活動ゲートが閉じている）。
        /// </summary>
        public bool TryResolve<T>(out T component) where T : Component
        {
            component = null;
            UnityEngine.SceneManagement.Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // 読込途中・Prefab 編集中などは自分の下だけを見る。
                component = GetComponentInChildren<T>(true);
                return component != null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                {
                    continue;
                }

                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                {
                    component = found;
                    return true;
                }
            }

            return false;
        }

        private void OnEnable()
        {
            AreaBundleDirectory.Register(this);
        }

        private void OnDisable()
        {
            AreaBundleDirectory.Unregister(this);
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(
            AreaRoot areaRoot,
            AreaContext context,
            AreaActorTransferPort transferPort = null,
            AreaTransitionConditionsSource conditions = null,
            AreaEncounterRunner encounter = null,
            CampaignRespawnRunner respawn = null,
            GameObject gameplayRoot = null,
            GameObject physicsRoot = null)
        {
            _areaRoot = areaRoot;
            _context = context;
            _transferPort = transferPort;
            _conditions = conditions;
            _encounter = encounter;
            _respawn = respawn;
            _gameplayRoot = gameplayRoot;
            _physicsRoot = physicsRoot;
        }
#endif
    }
}
