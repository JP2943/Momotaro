using System;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 本編型死亡再開の実行役（P5-08。仕様書 v1.1 §9.1）。Area ごとに 1 つ置く。
    ///
    /// <b>戦闘の中だけを見張らない。</b> §9.1 は「CombatSession が Preparing／未開始でも死亡を処理できる」
    /// ことを求めている。遭遇戦の外（たとえば飛び道具の残りや地形の罠）で死んだら再開できない、という
    /// 穴を塞ぐため、購読するのは <see cref="PlayerDefeatChannel"/> そのもので、
    /// <see cref="Momotaro.Gameplay.Encounter.AreaEncounterRunner"/> の勝敗処理には依存しない。
    ///
    /// <b>判断は Session が持つ調停役（<see cref="CampaignRespawnCoordinator"/>）に任せる。</b>
    /// 受理から到着までの間にこの Area は破棄されるので、一度限りの記録をここに置いてはいけない。
    ///
    /// 受理の確定は <c>LateUpdate</c> で行う。死亡通知は攻撃の解決中に飛んでくるので、その場で
    /// GameMode を GameOver へ動かすと、同じフレームの勝敗処理や入力が半端な世界を見る（§8.3 と同じ理由）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampaignRespawnRunner : MonoBehaviour, IPlayerDefeatListener, ICampaignRespawnRequest
    {
        [Tooltip("主人公の生存。死亡通知の発信元。")]
        [SerializeField] private PlayerVitalsHolder _playerVitals;

        [Tooltip("エリアの初期化状態。死亡時に活動を閉じる。")]
        [SerializeField] private AreaContext _area;

        [Tooltip("調査の調停役。死亡で未完了の調査を終了する（§9.1 手順 2）。")]
        [SerializeField] private InvestigationCoordinator _investigations;

        [Tooltip("再開地点を解決するカタログ Data。")]
        [SerializeField] private AreaCatalogData _catalogData;

        [Tooltip("再開の遷移を実行する常駐サービス（IAreaRespawnTravel）。")]
        [SerializeField] private MonoBehaviour _travelSource;

        private IAreaRespawnTravel _travel;
        private bool _travelResolved;
        private AreaCatalog _catalog;
        private Func<GameSessionState> _session;
        private PlayerDefeatChannel _defeats;
        private bool _defeatPending;
        private int _travelTransitionId;

        /// <summary>供給元がそろっているか（Scene 検査・診断用）。</summary>
        public bool IsWired => _playerVitals != null && _catalogData != null;

        /// <summary>死亡を受理した回数（診断・テスト用）。</summary>
        public int DefeatCount { get; private set; }

        /// <summary>再開を受理した回数（診断・テスト用）。</summary>
        public int AcceptedCount { get; private set; }

        /// <summary>再出現周期を進めた回数（診断・テスト用）。<b>再開要求 1 件につき 1 回まで</b>。</summary>
        public int CycleAdvanceCount { get; private set; }

        /// <summary>直近の遷移の拒否理由（診断・テスト用）。</summary>
        public AreaTransitionRejection LastTravelRejection { get; private set; } = AreaTransitionRejection.None;

        /// <summary>直近に受理された再開遷移の世代（診断・テスト用。0 は遷移していない）。</summary>
        public int TravelTransitionId => _travelTransitionId;

        /// <summary>直近の拒否理由（診断・テスト用）。</summary>
        public RespawnRejection LastRejection { get; private set; } = RespawnRejection.None;

        /// <summary>現在の段階（Session の調停役を覗く。未配線なら Idle）。</summary>
        public CampaignRespawnPhase Phase
        {
            get
            {
                CampaignRespawnCoordinator respawn = ResolveRespawn();
                return respawn != null ? respawn.Phase : CampaignRespawnPhase.Idle;
            }
        }

        /// <inheritdoc />
        public bool IsAwaitingRespawn
        {
            get
            {
                CampaignRespawnCoordinator respawn = ResolveRespawn();
                return respawn != null && respawn.IsAwaitingRespawn;
            }
        }

        /// <summary>配線する（Builder・Area 初期化担当・テストが呼ぶ）。</summary>
        public void Bind(PlayerVitalsHolder playerVitals, AreaContext area,
            InvestigationCoordinator investigations, AreaCatalogData catalogData)
        {
            if (playerVitals != null)
            {
                _playerVitals = playerVitals;
            }

            if (area != null)
            {
                _area = area;
            }

            if (investigations != null)
            {
                _investigations = investigations;
            }

            if (catalogData != null)
            {
                _catalogData = catalogData;
                _catalog = null;
            }
        }

        /// <summary>再開の遷移役を注入する（Infrastructure の常駐サービス、またはテストの Fake）。</summary>
        public void BindTravel(IAreaRespawnTravel travel)
        {
            _travel = travel;
            _travelResolved = travel != null;
            _travelSource = travel as MonoBehaviour;
        }

        /// <summary>Session を注入する（Area 初期化担当が呼ぶ）。</summary>
        public void BindSession(Func<GameSessionState> session)
        {
            _session = session;
        }

        /// <summary>死亡通知の発信元へ購読する。</summary>
        public void BindPlayerDefeat(PlayerDefeatChannel channel)
        {
            if (ReferenceEquals(_defeats, channel))
            {
                return;
            }

            _defeats?.RemoveListener(this);
            _defeats = channel;
            _defeats?.AddListener(this);
        }

        private void OnEnable()
        {
            if (_defeats == null && _playerVitals != null)
            {
                BindPlayerDefeat(_playerVitals.Defeats);
            }
        }

        private void OnDisable()
        {
            _defeats?.RemoveListener(this);
            _defeats = null;
        }

        /// <inheritdoc />
        public void OnPlayerDefeated(in PlayerDefeatedEvent defeated)
        {
            // ここでは印を付けるだけ。確定は LateUpdate（§8.3 と同じ「同じ刻みの中で動かさない」）。
            _defeatPending = true;
        }

        private void LateUpdate()
        {
            ResolvePendingDefeat();
        }

        /// <summary>保留した死亡を確定する（テストは Update を待たずに呼べる）。</summary>
        public void ResolvePendingDefeat()
        {
            if (!_defeatPending)
            {
                return;
            }

            _defeatPending = false;

            CampaignRespawnCoordinator respawn = ResolveRespawn();
            if (respawn == null || !respawn.NotifyPlayerDefeated())
            {
                return;
            }

            DefeatCount++;

            // ---- 手順 1：探索・戦闘・移動を止め、GameOver にする ----
            _area?.CloseForTransition();
            GameModeProvider.Current?.ChangeMode(GameMode.GameOver);

            // ---- 手順 2：未完了の調査を終了する（進行 State は保持する） ----
            _investigations?.InterruptAllForCombat();
        }

        /// <inheritdoc />
        public RespawnDecision RequestRespawn()
        {
            // 手順そのものは共有の手順役が持つ（GPT レビュー R8 の指摘 1）。
            // Scene が使えないときは常駐側の受付が同じ手順役を呼ぶので、
            // ここへ手順を写しておくと 2 か所が食い違う。ここは診断値を写すだけにする。
            CampaignRespawnRequestProcedure.Outcome outcome = CampaignRespawnRequestProcedure.Execute(
                ResolveSession(), ResolveRespawn(), ResolveCatalog, ResolveTravel());

            LastRejection = outcome.Rejection;
            LastTravelRejection = outcome.TravelRejection;

            if (outcome.CycleAdvanced)
            {
                CycleAdvanceCount++;
            }

            if (outcome.Decision.Accepted)
            {
                _travelTransitionId = outcome.TravelTransitionId;
                AcceptedCount++;
            }

            return outcome.Decision;
        }

        /// <summary>遷移側が失敗したことを受け取る（再開画面へ戻して再試行できるようにする）。</summary>
        public void NotifyTravelFailed(int transitionId)
        {
            CampaignRespawnCoordinator respawn = ResolveRespawn();
            if (respawn == null || (_travelTransitionId != 0 && transitionId != _travelTransitionId))
            {
                return;
            }

            if (respawn.NotifyFailed(respawn.CurrentRequestId))
            {
                // 失敗しても主人公は死んだままなので、探索へは戻さない。
                GameModeProvider.Current?.ChangeMode(GameMode.GameOver);
            }
        }

        private bool ResolveCatalog(out AreaEntryInfo entry)
        {
            entry = default;
            if (_catalog == null)
            {
                if (_catalogData == null
                    || !AreaCatalog.TryBuild(_catalogData, out AreaCatalog built, out _))
                {
                    return false;
                }

                _catalog = built;
            }

            return _catalog.TryGetRespawnEntry(out entry);
        }

        /// <summary>
        /// 再開の遷移役を解決する。
        ///
        /// <b>注入されていなければ常駐の窓口を見る</b>（GPT レビュー R7 の指摘 1）。
        /// 遷移役の注入は到着側 <c>AreaInitializer</c> が行うが、到着初期化が途中で落ちると
        /// そこへ到達しない。段階は「再試行待ち」へ戻っているのに実行役が居ない、という
        /// <b>再開画面は出るが押しても NotWired で断られる</b>状態になっていた。
        /// 常駐サービスは Scene の成否と無関係に生きているので、そこから取り直す。
        /// </summary>
        private IAreaRespawnTravel ResolveTravel()
        {
            if (!_travelResolved)
            {
                _travelResolved = true;
                _travel = _travelSource as IAreaRespawnTravel;
            }

            return _travel ?? CampaignRespawnTravelProvider.Current;
        }

        /// <summary>
        /// Session を解決する。注入されていなければ常駐の窓口を見る（同上）。
        /// Session は常駐が持っているので、Scene の初期化が落ちても取り直せる。
        /// </summary>
        private GameSessionState ResolveSession()
        {
            return _session?.Invoke() ?? GameSessionProvider.Current;
        }

        private CampaignRespawnCoordinator ResolveRespawn()
        {
            return ResolveSession()?.Respawn;
        }
    }
}
