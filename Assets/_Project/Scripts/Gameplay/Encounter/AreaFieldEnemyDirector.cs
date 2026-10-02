using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// 普通敵（遭遇戦の外に配置された敵）の生成と撃破記録（P6A-01。コアループ「遭遇戦と敵の復活」）。
    ///
    /// <b>P5／P5.5 には普通敵が無かった</b>（敵はすべて遭遇戦が生成する）。P6 で、
    /// 「エリア移動では復活せず、休息・成長・死亡・旅立ちで全世界の普通敵が復活する」敵を足す。
    ///
    /// <b>配置 ID で持つ。</b> 同じ敵種を 2 体倒せば 2 体分、同じ配置個体の撃破通知が重複しても 1 回分（仕様 §4）。
    /// 撃破の記録と徳の付与は <see cref="GameSessionState.TryRecordFieldDefeat"/> が同時に確定する。
    ///
    /// <b>全 Scene を読み込んで復活させない。</b> 周期は Session が持ち、この部品は入場のたびに
    /// 「現在周期で撃破済みでない配置」だけを生成する。未ロードの Area は次の生成時に反映される。
    /// 活動中の Area で周期が進んだ（休息）ときは <see cref="RebuildNow"/> がその場で作り直す。
    ///
    /// <b>生成と活動許可を分ける</b>（遭遇戦の <see cref="AreaEncounterSpawner"/> と同じ作法）。
    /// 入場の準備（Prepared）で非活動の根の下へ作り、所有者が活動を許可したとき（<see cref="AreaContext.Activated"/>）に起こす。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaFieldEnemyDirector : MonoBehaviour, IEnemyDefeatListener, IFieldEnemyRebuild
    {
        private const string SpawnRootName = "FieldEnemies";

        /// <summary>配置 1 件（Scene に保存される）。</summary>
        [Serializable]
        public struct Placement
        {
            [Tooltip("配置 ID（Area 内で一意。敵種 ID とは別）。")]
            public StableId PlacementId;

            [Tooltip("敵種 ID（Prefab 表の鍵）。")]
            public StableId EnemyId;

            [Tooltip("出現点。")]
            public Transform Point;
        }

        [Tooltip("所属 Area の初期化状態（活動許可の通知元）。")]
        [SerializeField] private AreaContext _area;

        [Tooltip("所属 Area の安定 ID（AreaRoot の定義と揃える）。")]
        [SerializeField] private string _areaId = string.Empty;

        [Tooltip("EnemyId → Prefab の明示表。")]
        [SerializeField] private EnemyPrefabTable _table = new EnemyPrefabTable();

        [Tooltip("配置。")]
        [SerializeField] private List<Placement> _placements = new List<Placement>();

        [Tooltip("撃破報酬の差し替え（P6A の検証用。未設定なら敵の RewardData）。")]
        [SerializeField] private Momotaro.Data.Progression.RewardData _rewardOverride;

        /// <summary>撃破報酬の差し替えを設定する（P6A の Builder）。</summary>
        public void SetRewardOverride(Momotaro.Data.Progression.RewardData reward)
        {
            _rewardOverride = reward;
        }

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Dictionary<int, StableId> _placementOfEnemy = new Dictionary<int, StableId>();
        private readonly List<EnemyDefeatChannel> _channels = new List<EnemyDefeatChannel>();
        private Func<GameSessionState> _session;
        private Transform _root;
        private int _builtCycle = -1;
        private bool _built;
        private bool _subscribedArea;

        /// <summary>所属 Area。</summary>
        public StableId AreaId => string.IsNullOrEmpty(_areaId) ? default : new StableId(_areaId);

        /// <summary>配置数（Validator・診断用）。</summary>
        public int PlacementCount => _placements.Count;

        /// <summary>配置（読み取り専用。Validator 用）。</summary>
        public IReadOnlyList<Placement> Placements => _placements;

        /// <summary>Prefab 表（Validator 用）。</summary>
        public EnemyPrefabTable Table => _table;

        /// <summary>いま生成されている敵の数（撃破済みを含む。診断・テスト用）。</summary>
        public int SpawnedCount => _spawned.Count;

        /// <summary>生成した敵が起きているか（診断・テスト用）。</summary>
        public bool SpawnedActive => _root != null && _root.gameObject.activeSelf;

        /// <summary>直近に生成した周期（未生成なら -1。診断・テスト用）。</summary>
        public int BuiltCycle => _builtCycle;

        /// <summary>撃破を記録した回数（診断・テスト用）。</summary>
        public int RecordedDefeatCount { get; private set; }

        /// <summary>重複・不明の撃破通知を捨てた回数（診断・テスト用）。</summary>
        public int IgnoredDefeatCount { get; private set; }

        /// <summary>直近の撃破で加算した徳（診断・テスト用）。</summary>
        public int LastGrantedVirtue { get; private set; }

        /// <summary>生成した敵（診断・テスト用。撃破済みを含む）。</summary>
        public IReadOnlyList<GameObject> Spawned => _spawned;

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => !AreaId.IsEmpty && _table != null && _table.Count > 0;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(StableId areaId, AreaContext area, IEnumerable<EnemyPrefabTable.Entry> entries,
            IEnumerable<Placement> placements)
        {
            _areaId = areaId.Value ?? string.Empty;
            if (area != null)
            {
                UnsubscribeArea();
                _area = area;
                if (isActiveAndEnabled)
                {
                    SubscribeArea();
                }
            }

            if (entries != null)
            {
                _table.SetEntries(entries);
            }

            if (placements != null)
            {
                _placements = new List<Placement>(placements);
            }
        }

        private float _attackPowerScale = 1f;

        /// <summary>生成する普通敵の攻撃力の倍率（実行時だけ。campaign のテスト専用の調整。P6A）。</summary>
        public float AttackPowerScale => _attackPowerScale;

        /// <summary>攻撃力の倍率を設定する（0 以下は 1）。すでに生成済みの普通敵にも当てる。</summary>
        public void SetEnemyAttackPowerScale(float scale)
        {
            _attackPowerScale = scale > 0f && !float.IsInfinity(scale) ? scale : 1f;
            for (int i = 0; i < _spawned.Count; i++)
            {
                EnemyActor actor = _spawned[i] != null ? _spawned[i].GetComponentInChildren<EnemyActor>(true) : null;
                actor?.SetAttackPowerScale(_attackPowerScale);
            }
        }

        /// <summary>Session の取り出し口を注入する（Area の初期化担当が呼ぶ。常駐 Session は serialize できない）。</summary>
        public void BindSession(Func<GameSessionState> session)
        {
            _session = session;
        }

        /// <summary>
        /// 入場の準備（Area の初期化担当が<b>入場のたび</b>に呼ぶ）。
        ///
        /// 前回の生成から周期が進んでいれば作り直す（留守のあいだの休息を反映する）。進んでいなければ
        /// 生き残りをそのまま残す——エリア移動では撃破済みも未撃破も変わらない（コアループの表）。
        /// どちらの場合も<b>活動許可までは起こさない</b>。
        /// </summary>
        public void PrepareForEntry()
        {
            GameSessionState session = ResolveSession();
            int cycle = session != null ? session.RespawnCycle : 0;
            if (!_built || cycle != _builtCycle)
            {
                Rebuild(session, cycle);
            }

            SetSpawnedActive(false);
        }

        /// <summary>
        /// 活動中の Area で周期が進んだ（休息の成立）ときに、<b>その場で</b>配置を作り直す（仕様 §5）。
        /// 生き残りも初期状態へ戻して置き直し、すぐ起こす。
        /// </summary>
        public void RebuildNow()
        {
            GameSessionState session = ResolveSession();
            Rebuild(session, session != null ? session.RespawnCycle : 0);
            SetSpawnedActive(_area == null || _area.IsAreaReady);
        }

        /// <inheritdoc />
        public void OnEnemyDefeated(in EnemyDefeatedEvent defeated)
        {
            if (!_placementOfEnemy.TryGetValue(defeated.EnemyId, out StableId placementId))
            {
                IgnoredDefeatCount++;
                return;
            }

            GameSessionState session = ResolveSession();
            if (session == null)
            {
                IgnoredDefeatCount++;
                GameLog.WarningOnce(LogCategory.Combat, "field_enemy_no_session",
                    "普通敵の撃破を記録できません（Session がありません）。");
                return;
            }

            // 記録と徳は 1 か所で同時に確定する。同じ配置の重複通知はここで 1 回分になる。
            if (session.TryRecordFieldDefeat(AreaId, placementId,
                    RewardSnapshot.From(_rewardOverride != null ? _rewardOverride : defeated.Reward.Reward),
                    out int granted))
            {
                RecordedDefeatCount++;
                LastGrantedVirtue = granted;
            }
            else
            {
                IgnoredDefeatCount++;
            }
        }

        private void Rebuild(GameSessionState session, int cycle)
        {
            ReleaseAll();
            _built = true;
            _builtCycle = cycle;

            AreaRuntimeState record = null;
            session?.TryGetArea(AreaId, out record);

            Transform root = EnsureRoot();
            root.gameObject.SetActive(false);

            for (int i = 0; i < _placements.Count; i++)
            {
                Placement p = _placements[i];
                if (p.PlacementId.IsEmpty || p.Point == null)
                {
                    GameLog.WarningOnce(LogCategory.Scene, "field_enemy_bad_placement:" + name + ":" + i,
                        "普通敵の配置 [" + i + "] の ID か出現点が未設定です。");
                    continue;
                }

                if (record != null && record.IsFieldEnemyDefeated(p.PlacementId, cycle))
                {
                    continue; // この周期では撃破済み。復活しない。
                }

                if (!_table.TryResolve(p.EnemyId, out GameObject prefab))
                {
                    GameLog.WarningOnce(LogCategory.Scene, "field_enemy_no_prefab:" + p.EnemyId.Value,
                        "普通敵 '" + p.EnemyId.Value + "' の Prefab を表から解決できません。");
                    continue;
                }

                Vector3 at = p.Point.position;
                GameObject go = Instantiate(prefab, new Vector3(at.x, 0f, at.z), p.Point.rotation, root);
                go.name = prefab.name + "_" + p.PlacementId.Value;
                _spawned.Add(go);

                var actor = go.GetComponentInChildren<EnemyActor>(true);
                if (actor == null)
                {
                    continue;
                }

                actor.SetAttackPowerScale(_attackPowerScale);
                _placementOfEnemy[actor.DamageableId] = p.PlacementId;
                actor.Defeats.AddListener(this);
                _channels.Add(actor.Defeats);
            }
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < _channels.Count; i++)
            {
                _channels[i]?.RemoveListener(this);
            }

            _channels.Clear();
            _placementOfEnemy.Clear();

            for (int i = 0; i < _spawned.Count; i++)
            {
                GameObject go = _spawned[i];
                if (go == null)
                {
                    continue;
                }

                go.SetActive(false);
                DestroySpawned(go);
            }

            _spawned.Clear();

            // 残留 Projectile を掃除する（遭遇戦の後始末と同じ。作り直した敵に古い弾が当たらない）。
            EnemyProjectileRegistry.DespawnAll();
        }

        private void SetSpawnedActive(bool active)
        {
            if (_root != null && _root.gameObject.activeSelf != active)
            {
                _root.gameObject.SetActive(active);
            }
        }

        private Transform EnsureRoot()
        {
            if (_root != null)
            {
                return _root;
            }

            // <b>Scene の直下に作る</b>（遭遇戦の生成役と同じ）。主人公の攻撃は「同じ根の下の相手」を
            // 自分自身として除外する（PlayerStateController の命中判定）ので、AreaRoot の下へ置くと
            // 主人公と同じ根になり、<b>一切当たらない</b>（P6A の実 Scene テストで発覚）。
            // いったん自分の子として作ってから親を外す：親を外しても所属 Scene は変わらないので、
            // Scene API を使わずにこの Area の Scene に置ける（Scene の破棄で一緒に消える）。
            // 活動ゲートの開閉には付いてこないので、<see cref="OnDisable"/>／<see cref="OnEnable"/> で合わせる。
            var go = new GameObject(SpawnRootName + "_" + _areaId);
            go.SetActive(false);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.SetParent(null, worldPositionStays: true);
            go.transform.position = Vector3.zero;
            _root = go.transform;
            return _root;
        }

        private static void DestroySpawned(GameObject go)
        {
            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        private GameSessionState ResolveSession() => _session?.Invoke() ?? GameSessionProvider.Current;

        private void OnAreaActivated()
        {
            SetSpawnedActive(true);
        }

        private void SubscribeArea()
        {
            if (_subscribedArea || _area == null)
            {
                return;
            }

            _area.Activated += OnAreaActivated;
            _subscribedArea = true;
        }

        private void UnsubscribeArea()
        {
            if (!_subscribedArea || _area == null)
            {
                return;
            }

            _area.Activated -= OnAreaActivated;
            _subscribedArea = false;
        }

        private void OnEnable()
        {
            SubscribeArea();

            // 活動ゲートが開き直した（保持していた Area へ戻った）。活動中なら起こす。
            if (_built && _area != null && _area.IsAreaReady)
            {
                SetSpawnedActive(true);
            }
        }

        private void OnDisable()
        {
            UnsubscribeArea();

            // 活動ゲートが閉じた（非活動 Area の敵は動かない）。根は Scene 直下なので自分で止める。
            SetSpawnedActive(false);
        }

        private void OnDestroy()
        {
            ReleaseAll();
            if (_root != null)
            {
                DestroySpawned(_root.gameObject);
                _root = null;
            }
        }
    }
}
