using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.World
{
    /// <summary>お地蔵様メニューの操作の結果（P6A-03／04。診断・テスト・表示用）。</summary>
    public enum ShrineMenuResult
    {
        None = 0,
        Rested = 1,
        GrowthPurchased = 2,
        GrowthRejected = 3,
        FastTravelStarted = 4,
        FastTravelRejected = 5,
        Closed = 6,
        NotAllowed = 7,
        GrowthRefunded = 8,
        RefundRejected = 9,
    }

    /// <summary>
    /// お地蔵様の常駐サービス（P6A-03／04。仕様 §5・§6・§7）。Scene の <see cref="ShrinePoint"/> から「調べた」を受け、
    /// 登録・保存・メニュー（休息／成長／旅立ち）を行う。
    ///
    /// <b>操作できるのは安全な探索状態だけ</b>：主人公が生存し、戦闘・遭遇戦・遷移・死亡解決の最中でないとき。
    /// メニューの間は GameMode を Paused にして入力を UI へ切り替える（既存の入力の所有権を使う）。
    /// 一般の Pause から場所制限を回避して成長させない——成長はこのメニューからしか呼べない。
    ///
    /// <b>旅立ちは到着の確定後に休息する。</b> 失敗したら出発側で回復も登録もしない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampaignShrineService : MonoBehaviour, IGameService, IShrineOperations
    {
        private readonly List<IFieldEnemyRebuild> _fieldBuffer = new List<IFieldEnemyRebuild>(2);
        private AreaTransitionService _transitions;
        private CampaignKibidangoUse _kibidango;
        private ShrineInfo _openShrine;
        private bool _menuOpen;
        private GameMode? _modeBeforeMenu;
        private ShrineInfo _travelDestination;
        private int _travelTransitionId;

        /// <inheritdoc />
        public string ServiceName => "CampaignShrine";

        /// <summary>きびだんご使用の受け口（P6B 03。診断・テスト用）。</summary>
        public CampaignKibidangoUse KibidangoUse => _kibidango;

        /// <summary>メニューが開いているか。</summary>
        public bool IsMenuOpen => _menuOpen;

        /// <summary>開いているお地蔵様（閉じていれば無効）。</summary>
        public ShrineInfo OpenShrine => _menuOpen ? _openShrine : default;

        /// <summary>直近の操作の結果（診断・テスト用）。</summary>
        public ShrineMenuResult LastResult { get; private set; }

        /// <summary>直近の成長購入の結果（診断・テスト用）。</summary>
        public GrowthPurchaseResult LastGrowthResult { get; private set; }

        /// <summary>直近の旅立ちの拒否理由（診断・テスト用）。</summary>
        public FastTravelRejection LastTravelRejection { get; private set; }

        /// <summary>休息が成立した回数（診断・テスト用）。</summary>
        public int RestCount { get; private set; }

        /// <summary>旅立ちが到着まで済んだ回数（診断・テスト用）。</summary>
        public int FastTravelCompletedCount { get; private set; }

        /// <summary>旅立ちが失敗した回数（診断・テスト用）。</summary>
        public int FastTravelFailedCount { get; private set; }

        /// <summary>旅立ちの途中か。</summary>
        public bool IsTravelling => _travelTransitionId != 0;

        /// <summary>表示の短文（直近の結果）。</summary>
        public string Message { get; private set; } = string.Empty;

        /// <inheritdoc />
        public ServiceInitResult Initialize()
        {
            ShrineOperationsProvider.Current = this;

            // P6B 03：きびだんご使用の受け口（campaign の Session とカタログを呼ばれた時点で引く）。
            _kibidango = new CampaignKibidangoUse(() => Session, () => Campaign);
            KibidangoUseProvider.Current = _kibidango;
            return ServiceInitResult.Ok("Shrine service ready.");
        }

        /// <summary>遷移サービスを結ぶ（Bootstrap が呼ぶ）。</summary>
        public void Bind(AreaTransitionService transitions)
        {
            if (_transitions != null)
            {
                _transitions.FastTravelCompleted -= OnFastTravelCompleted;
                _transitions.FastTravelFailed -= OnFastTravelFailed;
            }

            _transitions = transitions;
            if (_transitions != null)
            {
                _transitions.FastTravelCompleted += OnFastTravelCompleted;
                _transitions.FastTravelFailed += OnFastTravelFailed;
            }
        }

        private GameSessionState Session => GameSessionProvider.Current;

        private CampaignCatalog Campaign => _transitions != null && _transitions.Catalog != null
            ? _transitions.Catalog.Campaign
            : null;

        /// <summary>
        /// お地蔵様の操作を始めてよいか（仕様 §5）。生存・探索中・戦闘／遭遇戦／遷移／死亡解決の外。
        /// </summary>
        public bool CanOperate(out string reason)
        {
            GameSessionState session = Session;
            CampaignCatalog campaign = Campaign;
            if (session == null || campaign == null || string.IsNullOrEmpty(session.AdventureId))
            {
                reason = "冒険が始まっていません。";
                return false;
            }

            if (_transitions.IsTransitionUnsettled || IsTravelling)
            {
                reason = "移動中です。";
                return false;
            }

            if (session.Respawn.Phase != CampaignRespawnPhase.Idle)
            {
                reason = "倒れています。";
                return false;
            }

            GameMode mode = GameModeProvider.Current != null ? GameModeProvider.Current.Current : GameMode.Loading;
            if (mode != GameMode.Exploration && !(_menuOpen && mode == GameMode.Paused))
            {
                reason = "戦闘中・操作できない状態です。";
                return false;
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            if (bundle == null || bundle.Context == null || !bundle.Context.IsAreaReady)
            {
                reason = "エリアの準備ができていません。";
                return false;
            }

            if ((bundle.Encounter != null && bundle.Encounter.IsEncounterActive)
                || (bundle.EncounterGroup != null && bundle.EncounterGroup.IsEncounterActive))
            {
                reason = "戦闘中です。";
                return false;
            }

            if (bundle.TransferPort == null || !bundle.TransferPort.CanExportForSave)
            {
                reason = "主人公が操作できません。";
                return false;
            }

            reason = null;
            return true;
        }

        /// <inheritdoc />
        public AreaInteractionOutcome OnShrineInteracted(StableId shrineId)
        {
            if (!CanOperate(out string reason))
            {
                LastResult = ShrineMenuResult.NotAllowed;
                return AreaInteractionOutcome.Refused(reason);
            }

            if (!Campaign.TryGetShrine(shrineId, out ShrineInfo shrine))
            {
                LastResult = ShrineMenuResult.NotAllowed;
                return AreaInteractionOutcome.Refused("このお地蔵様はカタログにありません。");
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            if (!shrine.AreaId.Equals(bundle.AreaId))
            {
                LastResult = ShrineMenuResult.NotAllowed;
                return AreaInteractionOutcome.Refused("このお地蔵様は別のエリアのものです。");
            }

            // 調べる：登録・死亡地点・中断位置を更新して保存。回復はしない（仕様 §5）。
            Session.RegisterShrine(shrine);
            OpenMenu(shrine);
            Message = "お地蔵様に手を合わせた（ここが再開地点になった）";
            return AreaInteractionOutcome.Accepted(Message);
        }

        /// <summary>休息する（明示操作）。</summary>
        public ShrineMenuResult Rest()
        {
            string reason = "メニューが開いていません。";
            if (!_menuOpen || !CanOperate(out reason))
            {
                Message = reason ?? "メニューが開いていません。";
                return LastResult = ShrineMenuResult.NotAllowed;
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            RestOutcome outcome = ShrineProcedures.Rest(Session, Campaign, _openShrine, bundle.TransferPort,
                ActiveFields(bundle), "rested");
            if (!outcome.Rested)
            {
                return LastResult = ShrineMenuResult.NotAllowed;
            }

            RestCount++;
            Message = "休息した（全回復・きびだんご補充・敵が戻った）";
            return LastResult = ShrineMenuResult.Rested;
        }

        /// <summary>成長を取得する（明示操作）。不成立なら何も変えない。</summary>
        public ShrineMenuResult PurchaseGrowth(StableId growthId)
        {
            string reason = "メニューが開いていません。";
            if (!_menuOpen || !CanOperate(out reason))
            {
                Message = reason ?? "メニューが開いていません。";
                LastGrowthResult = GrowthPurchaseResult.NotAllowedHere;
                return LastResult = ShrineMenuResult.NotAllowed;
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            GrowthPurchaseResult result = ShrineProcedures.PurchaseGrowth(Session, Campaign, _openShrine, growthId,
                bundle.TransferPort, ActiveFields(bundle));
            LastGrowthResult = result;
            if (result != GrowthPurchaseResult.Purchased)
            {
                Message = "取得できません（" + result + "）";
                return LastResult = ShrineMenuResult.GrowthRejected;
            }

            RestCount++;
            Message = "成長した（そのまま休息した）";
            return LastResult = ShrineMenuResult.GrowthPurchased;
        }

        /// <summary>旅立つ（明示操作）。到着の確定後に休息・登録・保存。</summary>
        public ShrineMenuResult FastTravel(StableId destinationShrineId)
        {
            string reason = "メニューが開いていません。";
            if (!_menuOpen || !CanOperate(out reason))
            {
                Message = reason ?? "メニューが開いていません。";
                LastTravelRejection = FastTravelRejection.NotAllowedNow;
                return LastResult = ShrineMenuResult.NotAllowed;
            }

            FastTravelRejection rejection = ShrineProcedures.CanFastTravel(Session, Campaign, _openShrine.ShrineId,
                destinationShrineId, out ShrineInfo destination);
            LastTravelRejection = rejection;
            if (rejection != FastTravelRejection.None)
            {
                Message = "旅立てません（" + rejection + "）";
                return LastResult = ShrineMenuResult.FastTravelRejected;
            }

            // メニューを閉じて探索へ戻してから受付に掛ける（受付条件は探索中を求める）。
            CloseMenu();
            AreaTransitionDecision decision = _transitions.TryFastTravel(destination.AreaId, destination.Entry.EntryId);
            if (!decision.Accepted)
            {
                LastTravelRejection = FastTravelRejection.TravelRejected;
                Message = "旅立てません（" + decision.Rejection + "）";
                return LastResult = ShrineMenuResult.FastTravelRejected;
            }

            _travelDestination = destination;
            _travelTransitionId = decision.TransitionId;
            Message = destination.DisplayName + " へ旅立つ";
            return LastResult = ShrineMenuResult.FastTravelStarted;
        }

        /// <summary>直近の払い戻しの結果（診断・テスト用。P6B）。</summary>
        public GrowthRefundResult LastRefundResult { get; private set; }

        /// <summary>
        /// 成長を払い戻す（明示操作。P6B 02）。メニューの中・安全な状態でだけ。不成立なら何も変えない（休息もしない）。
        /// </summary>
        public ShrineMenuResult RefundGrowth(StableId growthId)
        {
            string reason = "メニューが開いていません。";
            if (!_menuOpen || !CanOperate(out reason))
            {
                Message = reason ?? "メニューが開いていません。";
                LastRefundResult = GrowthRefundResult.NotAllowedHere;
                return LastResult = ShrineMenuResult.NotAllowed;
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            GrowthRefundResult result = ShrineProcedures.RefundGrowth(Session, Campaign, _openShrine, growthId,
                bundle.TransferPort, ActiveFields(bundle));
            LastRefundResult = result;
            if (result != GrowthRefundResult.Refunded)
            {
                Message = GrowthTreeScreen.RefundReason(result, Campaign, Session, growthId);
                return LastResult = ShrineMenuResult.RefundRejected;
            }

            RestCount++;
            Message = "払い戻した（徳が戻り、休息した）";
            return LastResult = ShrineMenuResult.GrowthRefunded;
        }

        // ---------------------------------------------------------------- 成長の木（P6B 04）

        private readonly GrowthTreeScreen _tree = new GrowthTreeScreen();
        private GrowthTreeScreen.Grid _grid;
        private bool _treeOpen;

        /// <summary>成長の木を開いているか。</summary>
        public bool IsTreeOpen => _menuOpen && _treeOpen;

        /// <summary>成長の木の画面（テスト用）。</summary>
        public GrowthTreeScreen Tree => _tree;

        /// <summary>成長の木の並び（テスト用）。</summary>
        public GrowthTreeScreen.Grid TreeGrid => _grid ??= GrowthTreeScreen.Grid.From(Campaign);

        /// <summary>この campaign は成長の木の画面を使うか（P6B。ノードが複数、または使用動作あり）。</summary>
        private bool UsesTree
        {
            get
            {
                CampaignCatalog campaign = Campaign;
                return campaign != null && (campaign.GrowthNodes.Count > 1 || campaign.HasKibidangoUse);
            }
        }

        /// <summary>成長の木を開く（メニューの中だけ）。</summary>
        public bool OpenTree()
        {
            if (!_menuOpen)
            {
                return false;
            }

            _grid = GrowthTreeScreen.Grid.From(Campaign);
            _tree.Reset();
            _treeOpen = true;
            return true;
        }

        /// <summary>成長の木を閉じてメニューへ戻る。</summary>
        public void CloseTree()
        {
            _treeOpen = false;
            _tree.CancelConfirm();
            _navigator.Reset(0);
        }

        /// <summary>木の 1 フレームの操作を実行する。</summary>
        private void RunTreeCommand(GrowthTreeScreen.Command command)
        {
            StableId id = _tree.SelectedId(TreeGrid);
            switch (command)
            {
                case GrowthTreeScreen.Command.Acquire:
                    PurchaseGrowth(id);
                    _tree.Notice = Message;
                    break;
                case GrowthTreeScreen.Command.Refund:
                    RefundGrowth(id);
                    _tree.Notice = Message;
                    break;
                case GrowthTreeScreen.Command.Close:
                    CloseTree();
                    break;
            }
        }

        /// <summary>閉じる。何も変えない。</summary>
        public ShrineMenuResult Close()
        {
            if (_menuOpen)
            {
                CloseMenu();
            }

            return LastResult = ShrineMenuResult.Closed;
        }

        private void OnFastTravelCompleted(int transitionId)
        {
            if (transitionId != _travelTransitionId)
            {
                return;
            }

            _travelTransitionId = 0;

            // 到着の配置と進行適用が成功した<b>後</b>に、登録・回復・周期・保存を一括確定する（仕様 §6）。
            AreaRuntimeBundle bundle = ActiveBundle();
            RestOutcome outcome = ShrineProcedures.Rest(Session, Campaign, _travelDestination,
                bundle != null ? bundle.TransferPort : null, ActiveFields(bundle), "fast_travel");
            if (outcome.Rested)
            {
                FastTravelCompletedCount++;
                Message = _travelDestination.DisplayName + " に着いた（休息した）";
            }
        }

        private void OnFastTravelFailed(int transitionId)
        {
            if (transitionId != _travelTransitionId)
            {
                return;
            }

            _travelTransitionId = 0;
            FastTravelFailedCount++;
            Message = "旅立ちに失敗した（元の場所に居る）";
            GameLog.Warning(LogCategory.Scene, "Fast travel failed; nothing was registered or restored.");
        }

        private void OpenMenu(ShrineInfo shrine)
        {
            _openShrine = shrine;
            if (_menuOpen)
            {
                return;
            }

            _menuOpen = true;
            _navigator.Reset(0);
            IGameModeService modes = GameModeProvider.Current;
            if (modes != null && modes.Current != GameMode.Paused)
            {
                _modeBeforeMenu = modes.Current;
                modes.ChangeMode(GameMode.Paused);
            }
        }

        /// <summary>メニューを閉じたフレーム（同じ Esc の押下が別のメニューを開かないようにする）。</summary>
        public int LastClosedFrame { get; private set; } = -1;

        private void CloseMenu()
        {
            _menuOpen = false;
            _treeOpen = false;
            _tree.CancelConfirm();
            LastClosedFrame = Time.frameCount;
            IGameModeService modes = GameModeProvider.Current;
            if (modes != null && modes.Current == GameMode.Paused)
            {
                modes.ChangeMode(_modeBeforeMenu ?? GameMode.Exploration);
            }

            _modeBeforeMenu = null;
        }

        private AreaRuntimeBundle ActiveBundle()
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            if (bundle != null)
            {
                return bundle;
            }

            return AreaBundleDirectory.TryGetSingle(out bundle) ? bundle : null;
        }

        private IReadOnlyList<IFieldEnemyRebuild> ActiveFields(AreaRuntimeBundle bundle)
        {
            _fieldBuffer.Clear();
            if (bundle != null && bundle.FieldEnemies != null)
            {
                _fieldBuffer.Add(bundle.FieldEnemies);
            }

            return _fieldBuffer;
        }

        private void Update()
        {
            if (!_menuOpen)
            {
                return;
            }

            // 開いたまま状態が変わった（倒れた・移動した）なら閉じる。
            if (!CanOperate(out _))
            {
                CloseMenu();
                return;
            }

            if (_treeOpen)
            {
                RunTreeCommand(_tree.Poll(TreeGrid, Session, Campaign));
                return;
            }

            // パッド・矢印キーでの選択（試遊のフィードバック 2026-10-02）。
            List<MenuItem> items = BuildItems();
            var enabled = new bool[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                enabled[i] = items[i].Enabled;
            }

            int chosen = _navigator.Poll(items.Count, enabled, out bool cancelled);
            if (chosen >= 0)
            {
                Execute(items[chosen]);
                return;
            }

            if (cancelled)
            {
                Close();
                return;
            }

            // 既存のショートカット（キーボード）。
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                Rest();
            }
            else if (keyboard.digit2Key.wasPressedThisFrame)
            {
                if (UsesTree)
                {
                    OpenTree();
                }
                else
                {
                    PurchaseFirstAvailableGrowth();
                }
            }
            else if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
            else
            {
                Key[] travelKeys = { Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };
                List<ShrineInfo> destinations = Destinations();
                for (int i = 0; i < travelKeys.Length && i < destinations.Count; i++)
                {
                    if (keyboard[travelKeys[i]].wasPressedThisFrame)
                    {
                        FastTravel(destinations[i].ShrineId);
                        break;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 選択肢（描画と入力で同じ並び）

        private enum MenuKind
        {
            Rest,
            Growth,
            Tree,
            Travel,
            Close,
        }

        private readonly struct MenuItem
        {
            public MenuItem(MenuKind kind, StableId id, string label, bool enabled)
            {
                Kind = kind;
                Id = id;
                Label = label;
                Enabled = enabled;
            }

            public MenuKind Kind { get; }
            public StableId Id { get; }
            public string Label { get; }
            public bool Enabled { get; }
        }

        private readonly PadMenuNavigator _navigator = new PadMenuNavigator();

        private List<MenuItem> BuildItems()
        {
            var items = new List<MenuItem>
            {
                new MenuItem(MenuKind.Rest, default, "1. 休息する（全回復・きびだんご補充・敵が戻る）", true),
            };

            GameSessionState session = Session;
            CampaignCatalog campaign = Campaign;
            if (campaign != null && session != null && UsesTree)
            {
                items.Add(new MenuItem(MenuKind.Tree, default,
                    "2. 成長の木（取得・払い戻し。徳 " + session.Progress.AvailableVirtue + "／権利 "
                    + session.Progress.RefundRights + "）", true));
            }
            else if (campaign != null && session != null)
            {
                foreach (GrowthInfo growth in campaign.GrowthNodes)
                {
                    bool owned = session.Progress.HasGrowth(growth.GrowthId);
                    items.Add(new MenuItem(MenuKind.Growth, growth.GrowthId,
                        "2. 成長：" + (string.IsNullOrEmpty(growth.DisplayName) ? growth.GrowthId.Value : growth.DisplayName)
                        + "（徳 " + growth.Cost + "）" + (owned ? " 取得済み" : string.Empty), !owned));
                }
            }

            List<ShrineInfo> destinations = Destinations();
            for (int i = 0; i < destinations.Count; i++)
            {
                items.Add(new MenuItem(MenuKind.Travel, destinations[i].ShrineId,
                    (3 + i) + ". 旅立つ：" + destinations[i].DisplayName, true));
            }

            items.Add(new MenuItem(MenuKind.Close, default, "Esc. 閉じる", true));
            return items;
        }

        private void Execute(MenuItem item)
        {
            switch (item.Kind)
            {
                case MenuKind.Rest:
                    Rest();
                    break;
                case MenuKind.Growth:
                    PurchaseGrowth(item.Id);
                    break;
                case MenuKind.Tree:
                    OpenTree();
                    break;
                case MenuKind.Travel:
                    FastTravel(item.Id);
                    break;
                default:
                    Close();
                    break;
            }
        }

        /// <summary>旅立ちの行き先一覧（登録済みで、いまのお地蔵様以外）。</summary>
        public List<ShrineInfo> Destinations()
        {
            var list = new List<ShrineInfo>();
            CampaignCatalog campaign = Campaign;
            GameSessionState session = Session;
            if (campaign == null || session == null)
            {
                return list;
            }

            foreach (ShrineInfo shrine in campaign.Shrines)
            {
                if (session.IsShrineRegistered(shrine.ShrineId) && !shrine.ShrineId.Equals(_openShrine.ShrineId))
                {
                    list.Add(shrine);
                }
            }

            return list;
        }

        private void PurchaseFirstAvailableGrowth()
        {
            CampaignCatalog campaign = Campaign;
            if (campaign == null)
            {
                return;
            }

            foreach (GrowthInfo growth in campaign.GrowthNodes)
            {
                if (!Session.Progress.HasGrowth(growth.GrowthId))
                {
                    PurchaseGrowth(growth.GrowthId);
                    return;
                }
            }

            Message = "取得できる成長がありません";
        }

        private void OnDestroy()
        {
            ShrineOperationsProvider.ReleaseIfOwner(this);
            KibidangoUseProvider.ReleaseIfOwner(_kibidango);
            Bind(null);
        }

        // ---------------------------------------------------------------- 仮 UI（P6A）

        private void OnGUI()
        {
            if (!_menuOpen)
            {
                _navigator.BeginScaled();
                DrawKibidangoHud();
                if (!string.IsNullOrEmpty(Message) && Time.unscaledTime < _messageUntil)
                {
                    GUI.Label(new Rect(16f, PadMenuNavigator.VirtualHeight - 40f, 700f, 28f), Message);
                }

                _navigator.EndScaled();
                return;
            }

            _messageUntil = Time.unscaledTime + 3f;
            GameSessionState session = Session;
            CampaignCatalog campaign = Campaign;
            if (_treeOpen && session != null && campaign != null)
            {
                DrawTree(session, campaign);
                return;
            }

            List<MenuItem> items = BuildItems();
            _navigator.BeginScaled();
            const float width = 520f;
            float height = 150f + items.Count * 36f;
            var area = new Rect((PadMenuNavigator.VirtualWidth - width) * 0.5f, 40f, width, height);
            GUI.Box(area, "お地蔵様：" + _openShrine.DisplayName);
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 28f, area.width - 32f, area.height - 40f));
            GUILayout.Label("徳 " + session.Progress.AvailableVirtue + "／きびだんご " + session.Kibidango
                + "／" + campaign.KibidangoCapacityOf(session.Progress));
            for (int i = 0; i < items.Count; i++)
            {
                if (_navigator.DrawItem(i, items[i].Label, items[i].Enabled))
                {
                    Execute(items[i]);
                    break;
                }
            }

            GUILayout.Label(Message);
            GUILayout.FlexibleSpace();
            GUILayout.Label(PadMenuNavigator.Hint);
            GUILayout.EndArea();
            _navigator.EndScaled();
        }

        private float _messageUntil;

        /// <summary>
        /// きびだんごの HUD（P6B 04。仕様 §10）：残数／上限と使用操作。使用中は進み具合（1.5 秒の確定の印つき）を出す。
        /// 確定時に残数と HP が同じフレームで更新される（値は Session と Vitals をそのまま読む）。
        /// </summary>
        private void DrawKibidangoHud()
        {
            GameSessionState session = Session;
            CampaignCatalog campaign = Campaign;
            if (session == null || campaign == null || !campaign.HasKibidangoUse || string.IsNullOrEmpty(session.AdventureId))
            {
                return;
            }

            AreaRuntimeBundle bundle = ActiveBundle();
            Momotaro.Gameplay.Player.PlayerStateController player = bundle != null && bundle.TransferPort != null
                ? bundle.TransferPort.PlayerState
                : null;
            Momotaro.Gameplay.Player.PlayerVitalsHolder vitals = bundle != null && bundle.TransferPort != null
                ? bundle.TransferPort.PlayerVitals
                : null;

            var rect = new Rect(16f, PadMenuNavigator.VirtualHeight - 120f, 360f, 72f);
            GUI.Box(rect, GUIContent.none);
            string hp = vitals != null ? "　HP " + vitals.CurrentHp + "／" + vitals.MaxHp : string.Empty;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 24f),
                "きびだんご " + session.Kibidango + "／" + campaign.KibidangoCapacityOf(session.Progress) + hp);
            if (player != null && player.IsUsingItem)
            {
                float duration = Mathf.Max(0.01f, player.ItemUseDuration);
                float t = Mathf.Clamp01(player.ItemUseElapsed / duration);
                var bar = new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, 14f);
                GUI.Box(bar, GUIContent.none);
                Color prev = GUI.color;
                GUI.color = player.ItemUseCommitted ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.85f, 0.35f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * t, bar.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                float mark = player.ItemUseCommitTime / duration;
                GUI.DrawTexture(new Rect(bar.x + bar.width * mark - 1f, bar.y - 2f, 2f, bar.height + 4f), Texture2D.whiteTexture);
                GUI.color = prev;
                GUI.Label(new Rect(rect.x + 8f, rect.y + 48f, rect.width - 16f, 22f),
                    player.ItemUseCommitted ? "食べた（回復済み）" : "食べている…（被弾で中断）");
            }
            else
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, 22f), "F／LT：きびだんごを食べる");
            }
        }

        private void DrawTree(GameSessionState session, CampaignCatalog campaign)
        {
            GrowthTreeScreen.Grid grid = TreeGrid;
            AreaRuntimeBundle bundle = ActiveBundle();
            Momotaro.Gameplay.Player.PlayerVitalsHolder vitals = bundle != null && bundle.TransferPort != null
                ? bundle.TransferPort.PlayerVitals
                : null;
            GrowthEffects now = campaign.GrowthEffectsOf(session.Progress);
            int baseHp = vitals != null ? vitals.MaxHp - now.MaxHpBonus : 0;
            int baseStamina = vitals != null ? Mathf.RoundToInt(vitals.MaxStaminaValue) - now.MaxStaminaBonus : 0;

            _navigator.BeginScaled();
            float width = Mathf.Min(PadMenuNavigator.VirtualWidth - 32f, 900f);
            float height = Mathf.Min(PadMenuNavigator.VirtualHeight - 40f, 560f);
            var area = new Rect((PadMenuNavigator.VirtualWidth - width) * 0.5f, 20f, width, height);
            GUI.Box(area, "成長の木（" + _openShrine.DisplayName + "）");
            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 26f, area.width - 24f, area.height - 34f));
            GUILayout.Label("使用可能な徳 " + session.Progress.AvailableVirtue + "　払い戻し権利 "
                + session.Progress.RefundRights + "／" + campaign.RefundRightsMax);
            GUILayout.Label("現在：" + GrowthTreeScreen.Stats(now, baseHp + now.MaxHpBonus, baseStamina + now.MaxStaminaBonus,
                campaign.KibidangoBaseHeal + now.KibidangoHealBonus,
                campaign.KibidangoBaseCapacity + now.KibidangoCapacityBonus));

            string[] titles = { "体力", "攻撃", "スタミナ" };
            GUILayout.BeginHorizontal();
            for (int c = 0; c < grid.Columns; c++)
            {
                GUILayout.BeginVertical(GUILayout.Width((area.width - 40f) / grid.Columns));
                GUILayout.Label(c < titles.Length ? titles[c] : "その他");
                for (int r = 0; r < grid.Rows; r++)
                {
                    StableId id = grid.At(c, r);
                    if (id.IsEmpty)
                    {
                        GUILayout.Label(" ");
                        continue;
                    }

                    campaign.TryGetGrowth(id, out GrowthInfo g);
                    bool selected = c == _tree.Column && r == _tree.Row;
                    Color prev = GUI.backgroundColor;
                    if (selected)
                    {
                        GUI.backgroundColor = new Color(1f, 0.85f, 0.35f, 1f);
                    }

                    string label = (r > 0 ? "↓ " : string.Empty) + (selected ? "▶ " : string.Empty) + g.DisplayName
                        + "（" + g.Cost + "）［" + GrowthTreeScreen.StateLabel(session, campaign, id) + "］";
                    if (GUILayout.Button(label, GUILayout.Height(30f)))
                    {
                        _tree.Select(c, r);
                    }

                    GUI.backgroundColor = prev;
                }

                GUILayout.EndVertical();
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("以降は今後追加");

            StableId sel = _tree.SelectedId(grid);
            if (!sel.IsEmpty && campaign.TryGetGrowth(sel, out GrowthInfo info))
            {
                GUILayout.Label("選択：" + info.DisplayName + "　効果：" + info.Description + "　費用：徳 " + info.Cost
                    + "　前提：" + GrowthTreeScreen.PrerequisiteNames(campaign, info));
                bool owned = session.Progress.HasGrowth(sel);
                GrowthEffects after = owned ? MinusNode(campaign, session, sel) : now.Plus(info.Effects.MaxHpBonus,
                    info.Effects.AttackHpMultiplierBonus, info.Effects.MaxStaminaBonus, info.Effects.NormalPoiseMultiplierBonus,
                    info.Effects.KibidangoHealBonus, info.Effects.KibidangoCapacityBonus);
                GUILayout.Label((owned ? "払い戻し後：" : "取得後：") + GrowthTreeScreen.Stats(after,
                    baseHp + after.MaxHpBonus, baseStamina + after.MaxStaminaBonus,
                    campaign.KibidangoBaseHeal + after.KibidangoHealBonus,
                    campaign.KibidangoBaseCapacity + after.KibidangoCapacityBonus));

                if (_tree.Confirming != GrowthTreeScreen.Pending.None)
                {
                    string q = _tree.Confirming == GrowthTreeScreen.Pending.Acquire
                        ? "「" + info.DisplayName + "」を徳 " + info.Cost + " で取得しますか？（取得後に休息します）"
                        : "「" + info.DisplayName + "」を払い戻しますか？　失う効果：" + info.Description + "　返還：徳 "
                          + session.Progress.GrowthSpent(sel) + "　権利を 1 回使用　休息により普通敵が復活します";
                    GUILayout.Box(q);
                    GUILayout.Label((_tree.ConfirmYes ? "▶ はい　　いいえ" : "　はい　▶ いいえ")
                        + "（←→：選ぶ　決定：A／Enter　戻る：B／Esc）");
                }
            }

            GUILayout.Label(_tree.Notice);
            GUILayout.FlexibleSpace();
            GUILayout.Label("←→↑↓／十字キー：選ぶ　決定：A（×）／Enter（取得済みは払い戻し）　戻る：B（○）／Esc");
            GUILayout.EndArea();
            _navigator.EndScaled();
        }

        /// <summary>そのノードを除いた効果（払い戻し後の表示用。取得記録は変えない）。</summary>
        private static GrowthEffects MinusNode(CampaignCatalog campaign, GameSessionState session, StableId removed)
        {
            GrowthEffects sum = GrowthEffects.None;
            foreach (GrowthInfo g in campaign.GrowthNodes)
            {
                if (!g.GrowthId.Equals(removed) && session.Progress.HasGrowth(g.GrowthId))
                {
                    GrowthEffects e = g.Effects;
                    sum = sum.Plus(e.MaxHpBonus, e.AttackHpMultiplierBonus, e.MaxStaminaBonus, e.NormalPoiseMultiplierBonus,
                        e.KibidangoHealBonus, e.KibidangoCapacityBonus);
                }
            }

            return sum;
        }
    }
}
