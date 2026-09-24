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
            CampaignRespawnCoordinator respawn = ResolveRespawn();
            GameSessionState session = _session?.Invoke();
            if (respawn == null || session == null)
            {
                LastRejection = RespawnRejection.NotWired;
                return RespawnDecision.Reject(RespawnRejection.NotWired);
            }

            // ---- 手順 3：再開操作を 1 回だけ受理する ----
            RespawnDecision decision = respawn.TryRequest();
            if (!decision.Accepted)
            {
                LastRejection = decision.Rejection;
                return decision;
            }

            // ---- 手順 5：再出現周期を進め、通常戦のクリア記録を初期化する ----
            //
            // 進めるのは「この再開要求で初めてのとき」だけ。読込に失敗して再試行しても、
            // 同じ要求 ID なのでもう一度は進まない（§9.1 末尾、P5-E21）。
            if (respawn.TryConsumeCycleAdvance(decision.RequestId))
            {
                session.AdvanceRespawnCycle();
                CycleAdvanceCount++;
            }

            if (!ResolveCatalog(out AreaEntryInfo entry))
            {
                respawn.NotifyFailed(decision.RequestId);
                LastRejection = RespawnRejection.NoRespawnPoint;
                return RespawnDecision.Reject(RespawnRejection.NoRespawnPoint);
            }

            // ---- 手順 4：Loading へ入り、再開地点をロードする ----
            IAreaRespawnTravel travel = ResolveTravel();
            if (travel == null)
            {
                respawn.NotifyFailed(decision.RequestId);
                LastRejection = RespawnRejection.NotWired;
                return RespawnDecision.Reject(RespawnRejection.NotWired);
            }

            AreaTransitionDecision travelDecision = travel.TryRespawnTravel(entry.AreaId, entry.EntryId);
            LastTravelRejection = travelDecision.Rejection;
            if (!travelDecision.Accepted)
            {
                respawn.NotifyFailed(decision.RequestId);
                LastRejection = RespawnRejection.TravelRejected;
                return RespawnDecision.Reject(RespawnRejection.TravelRejected);
            }

            _travelTransitionId = travelDecision.TransitionId;
            AcceptedCount++;
            LastRejection = RespawnRejection.None;

            // 旧 Interact のラッチを捨てる（§9.1 末尾。再開の押下が到着先の操作へ化けない）。
            (PlayerInputProvider.Current as IInteractInput)?.DiscardInteractPressed();
            return decision;
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

        private IAreaRespawnTravel ResolveTravel()
        {
            if (!_travelResolved)
            {
                _travelResolved = true;
                _travel = _travelSource as IAreaRespawnTravel;
            }

            return _travel;
        }

        private CampaignRespawnCoordinator ResolveRespawn()
        {
            GameSessionState session = _session?.Invoke();
            if (session == null)
            {
                session = GameSessionProvider.Current;
            }

            return session?.Respawn;
        }
    }
}
