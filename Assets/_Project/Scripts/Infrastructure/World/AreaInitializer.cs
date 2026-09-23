using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Core.World;
using Momotaro.Data.World;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Transfer;
using Momotaro.Infrastructure.Bootstrap;
using UnityEngine;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// エリア Scene の初期化担当（P5-03b。仕様書 v1.1 §5.1）。<b>薄い Infrastructure 層</b>で、
    /// 起動確認・Session 取得・注入・復元・AreaReady の確定までを決まった順に行う。
    ///
    /// 順序（§5.1）：
    /// <list type="number">
    /// <item><description>Bootstrap のサービス初期化完了を確認する。</description></item>
    /// <item><description>Session 種別を決め、既存を再利用、なければ新規生成。</description></item>
    /// <item><description>Area 定義と必須参照を検証する。</description></item>
    /// <item><description>Progress／Area 記録／活動 Context を注入する。</description></item>
    /// <item><description>入口位置・向き、Actor 値、門、クリア済み Encounter を復元する。</description></item>
    /// <item><description>報酬・Feedback・HUD・入力の購読を接続する。</description></item>
    /// <item><description>カメラを即時配置する。</description></item>
    /// <item><description>AreaReady を確定して活動・入力を許可する。</description></item>
    /// </list>
    ///
    /// <b>「1 フレーム待てば大丈夫」に依存しない。</b> 各段を明示的に順番に呼び、
    /// 途中で失敗したら Ready を確定せずに理由を残す。初期化前の Actor 更新・報酬購読は
    /// <see cref="AreaContext.IsAreaReady"/> と Gameplay 時計のゲートが止める。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaInitializer : MonoBehaviour
    {
        [Header("このエリア")]
        [Tooltip("AreaId・入口の明示参照を持つ根。")]
        [SerializeField] private AreaRoot _areaRoot;

        [Tooltip("初期化状態（AreaReady）。")]
        [SerializeField] private AreaContext _context;

        [Tooltip("遷移の受付条件を読む窓口。")]
        [SerializeField] private AreaTransitionConditionsSource _conditions;

        [Header("注入先")]
        [Tooltip("徳の窓口。Session の PlayerProgressState を Bind する。")]
        [SerializeField] private PlayerProgressHolder _progress;

        [Tooltip("調査記録の窓口。Area 記録を Bind する。")]
        [SerializeField] private InvestigationRecordHolder _record;

        [Tooltip("Actor 値の採取・復元の窓口（§4.4〜§4.6）。")]
        [SerializeField] private AreaActorTransferPort _transferPort;

        [Header("カタログ")]
        [Tooltip("P5 のエリアカタログ。遷移サービスへ渡す。")]
        [SerializeField] private AreaCatalogData _catalog;

        /// <summary>初期化が成功したか（診断・テスト用）。</summary>
        public bool Initialized { get; private set; }

        /// <summary>失敗の理由（診断・テスト用。成功なら空）。</summary>
        public string FailureReason { get; private set; } = string.Empty;

        /// <summary>このエリアの安定 ID。</summary>
        public StableId AreaId => _areaRoot != null ? _areaRoot.AreaId : default;

        private void Start()
        {
            Initialize();
        }

        /// <summary>初期化を実行する（テストから明示的に呼べるよう分離）。</summary>
        public bool Initialize()
        {
            if (Initialized)
            {
                return true;
            }

            // 1. 必須参照の検証（Scene に触る前に落とす）。
            if (_areaRoot == null || _areaRoot.Definition == null || _context == null)
            {
                return Fail("AreaRoot／AreaDefinition／AreaContext が未配線です。");
            }

            if (_catalog == null)
            {
                return Fail("Area カタログが未配線です。");
            }

            StableId areaId = _areaRoot.AreaId;

            // 2. 到着先の入口を決める。遷移で来たならその入口、直開きなら既定入口（§5.2）。
            StableId entryId = ResolveEntryId(areaId);
            if (entryId.IsEmpty)
            {
                return Fail("到着する入口を解決できません（area=" + areaId.Value + "）。");
            }

            if (!_areaRoot.TryGetEntryPoint(entryId, out AreaEntryPoint entryPoint))
            {
                return Fail("入口 '" + entryId.Value + "' が Scene にありません。");
            }

            _context.BeginInitialize(areaId, entryId);

            // 3. Session を用意する（既存があれば再利用。§5.2）。
            GameSessionBootService sessions = ResolveSessionService();
            if (sessions == null)
            {
                return Fail("Session サービスが見つかりません（Bootstrap 未起動）。");
            }

            GameSessionState session = sessions.EnsureSession();

            // 4. 注入（Actor の活動開始より前。§4.2／§4.3）。
            AreaRuntimeState area = session.GetOrCreateArea(areaId);
            session.MarkVisited(areaId);

            if (_progress != null && !_progress.Bind(session.Progress))
            {
                return Fail("進行データを注入できませんでした（使用後の差し替えの可能性）。");
            }

            if (_record != null && !_record.Bind(area.Investigation))
            {
                return Fail("調査記録を注入できませんでした。");
            }

            // 5. 遷移サービスへカタログと受付条件を渡す。
            AreaTransitionService transitions = ResolveTransitionService();
            if (transitions == null)
            {
                return Fail("遷移サービスが見つかりません（Bootstrap 未起動）。");
            }

            if (!transitions.Bind(_catalog, _conditions))
            {
                return Fail("Area カタログを構築できませんでした（Data の不整合）。");
            }

            // 6. 入口へ配置し、運ばれてきた Actor 値を復元する（§4.4〜§4.6）。
            //    値の復元は AreaReady より前。1 つでも失敗したら Ready を確定しない。
            PlaceArrivals(entryPoint, definitionFacing: ResolveFacing(entryId));

            if (transitions.TryPeekPendingTransfer(out AreaTransferSnapshot transfer))
            {
                if (_transferPort == null)
                {
                    return Fail("Actor 値が運ばれてきましたが、復元する窓口が未配線です。");
                }

                if (!_transferPort.TryApply(transfer))
                {
                    return Fail("Actor 値を復元できませんでした: " + _transferPort.LastApplyFailure);
                }
            }

            // 6b. 到着直後の跳ね返りを止める（§6.1 末尾）。
            //     入口 Trigger の中に立った状態で到着するのが普通なので、
            //     一度出るまで出入口は要求を出さない。押しっぱなしを新しい押下と解釈しない。
            foreach (AreaExitGate gate in _areaRoot.ExitGates)
            {
                if (gate != null)
                {
                    gate.DisarmOnArrival();
                }
            }

            // 7. モードを決めるのは初期化担当（§12.1「Loading 中に Exploration へ戻す自動適用をしない」）。
            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);

            // 8. Ready を確定して活動・入力を許可する。
            _context.ConfirmReady();

            // 完了は「自分が処理しているエリア・入口・世代」でだけ記録できる。
            // 無条件に完了にできると、古い Scene の初期化担当が新しい遷移を完了させてしまう。
            AreaPendingArrival.TryMarkCompleted(AreaPendingArrival.TransitionId, areaId, entryId);

            Initialized = true;
            FailureReason = string.Empty;
            GameLog.Info(LogCategory.Scene, "Area ready: " + areaId.Value + " / " + entryId.Value);
            return true;
        }

        /// <summary>
        /// 到着する入口を決める。遷移で来たならその入口、直開きなら Data の既定入口（§5.2）。
        /// 遷移の要求が<b>別のエリア宛て</b>なら自分のものではないので、既定入口を使う。
        /// </summary>
        private StableId ResolveEntryId(StableId areaId)
        {
            if (AreaPendingArrival.HasPending && AreaPendingArrival.AreaId.Equals(areaId))
            {
                return AreaPendingArrival.EntryId;
            }

            return _areaRoot.Definition.DefaultEntryId;
        }

        /// <summary>
        /// 到着位置へ置く（§3.1／§4.4）。<b>位置は運ばず、目的地の入口へ置換する。</b>
        /// 向きは Data の入口定義が正本。犬丸は入口の手前（真上に重ねない。§3.2）。
        ///
        /// 実際に動かすのは <see cref="AreaActorTransferPort"/>。明示参照で根を持っているので
        /// ここから <c>Find*</c> を使わずに済む。
        /// </summary>
        private void PlaceArrivals(AreaEntryPoint entryPoint, Vector3 definitionFacing)
        {
            if (_transferPort == null)
            {
                return;
            }

            _transferPort.PlaceAt(
                entryPoint.ArrivalPosition,
                entryPoint.ArrivalPosition - definitionFacing * 1.2f,
                definitionFacing);
        }

        /// <summary>入口定義の 4 方向を XZ のベクトルへ直す。</summary>
        private Vector3 ResolveFacing(StableId entryId)
        {
            foreach (AreaEntryDefinition e in _areaRoot.Definition.Entries)
            {
                if (e == null || !e.EntryId.Equals(entryId))
                {
                    continue;
                }

                switch (e.Facing)
                {
                    case CardinalDirection.North: return Vector3.forward;
                    case CardinalDirection.East: return Vector3.right;
                    case CardinalDirection.South: return Vector3.back;
                    case CardinalDirection.West: return Vector3.left;
                }
            }

            return Vector3.forward;
        }

        private bool Fail(string reason)
        {
            FailureReason = reason;
            Initialized = false;
            GameLog.Error(LogCategory.Scene, "Area initialization failed: " + reason);
            return false;
        }

        private static GameSessionBootService ResolveSessionService() =>
            BootstrapServices.Get<GameSessionBootService>();

        private static AreaTransitionService ResolveTransitionService() =>
            BootstrapServices.Get<AreaTransitionService>();
    }
}
