using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Core.World;
using Momotaro.Data.World;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
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

        [Tooltip("この区画の Encounter（§8。戦闘の無い区画は未割当でよい）。")]
        [SerializeField] private AreaEncounterRunner _encounter;

        [Tooltip("本編型死亡再開の実行役（P5-08。§9.1）。")]
        [SerializeField] private CampaignRespawnRunner _respawn;

        [Header("カタログ")]
        [Tooltip("P5 のエリアカタログ。遷移サービスへ渡す。")]
        [SerializeField] private AreaCatalogData _catalog;

        /// <summary>初期化が成功したか（診断・テスト用）。</summary>
        public bool Initialized { get; private set; }

        /// <summary>失敗の理由（診断・テスト用。成功なら空）。</summary>
        public string FailureReason { get; private set; } = string.Empty;

        /// <summary>このエリアの安定 ID。</summary>
        public StableId AreaId => _areaRoot != null ? _areaRoot.AreaId : default;

        /// <summary>起動待ちが終わったか（診断・テスト用）。</summary>
        public bool WaitFinished { get; private set; }

        /// <summary>
        /// 初期化を始めた時点で捕まえた到着トークン（診断・テスト用）。
        /// 0 は「このエリア宛ての生きた到着要求が無い」。直開きとは限らない。
        /// </summary>
        public int ArrivalToken { get; private set; }

        /// <summary>
        /// 直開きの自己許可を断ったか（診断・テスト用）。
        /// 終端失敗で放棄された遷移のあとに、遅れて読み終わった Scene がここに来る。
        /// </summary>
        public bool SelfActivationBlocked { get; private set; }

        private void Start()
        {
            // すでに起動の成否が確定しているなら待たない。
            // <b>正常系に固定の待ちを足さない</b>（§6.3 末尾）。待つのは確定していないときだけ。
            if (BootstrapWait.IsReadyNow)
            {
                WaitFinished = true;
                Initialize();
                return;
            }

            StartCoroutine(InitializeWhenBootstrapReady());
        }

        /// <summary>
        /// 常駐の起動完了を待ってから初期化する（GPT レビュー R2 の指摘 2）。
        /// Bootstrap も Scene 側も <c>Start()</c> で動くので、順序に頼ると
        /// 「先に動いた側が諦めて、あとから常駐が立っても何も起きない」が起きる。
        /// </summary>
        private IEnumerator InitializeWhenBootstrapReady()
        {
            bool ok = false;
            yield return BootstrapWait.Wait(r => ok = r);
            WaitFinished = true;

            if (!ok)
            {
                Fail("常駐サービスが起動しなかったため初期化できません。");
                yield break;
            }

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

            // 初期化を「始めた時点の」到着要求を捕まえる（GPT レビュー R2 の指摘 4）。
            // 完了時に共有領域から読み直すと自分自身との比較になり、照合の意味が無くなる。
            int arrivalToken = CaptureArrivalToken(areaId);
            ArrivalToken = arrivalToken;

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

            // 5b. 死亡再開の実行役へ Session と遷移役を渡す（§9.1）。
            //
            // <b>この下で失敗しうる段より前に配線する</b>（GPT レビュー R7 の指摘 1）。
            // 以前は門の復元より後に置いていたので、門の復元で落ちると実行役が未配線のまま残り、
            // 段階は「再試行待ち」へ戻っているのに <c>RequestRespawn</c> が NotWired で断る、という
            // <b>再開画面は出るが押しても何も起きない</b>状態になっていた。
            // ここより前で落ちた場合や、そもそも実行役が Scene に居ない場合に備えて、
            // 実行役側も常駐（<see cref="GameSessionProvider"/>／
            // <see cref="CampaignRespawnTravelProvider"/>）から取り直せるようにしてある。
            // 判断（一度限り・段階）は Session 側が持ち、ここは配線だけを行う。
            if (_respawn != null)
            {
                _respawn.BindSession(() => session);
                _respawn.BindTravel(transitions);
            }

            // 6. 入口へ配置し、運ばれてきた Actor 値を復元する（§4.4〜§4.6）。
            //    値の復元は AreaReady より前。1 つでも失敗したら Ready を確定しない。
            PlaceArrivals(entryPoint, definitionFacing: ResolveFacing(entryId));

            // 死亡再開で着いたなら、運ばれてきた値ではなく<b>全回復</b>を適用する（§9.1 手順 6）。
            //
            // 「再開は Scene を作り直すのだから Actor は新品」で済ませない。P5 では実際にそうでも、
            // それは偶然で、復帰の中身がどこにも書かれていない状態になる。
            // 到着の種類で分岐を 1 つ置き、再開のときだけ全回復を通す。
            bool respawnArrival = session.Respawn.Phase == CampaignRespawnPhase.Requested;
            if (respawnArrival)
            {
                if (_transferPort == null)
                {
                    return Fail("死亡再開で到着しましたが、Actor を復帰させる窓口が未配線です。");
                }

                transitions.ClearPendingTransfer();
                _transferPort.RestoreForCampaignRespawn();

                // <b>ここで完了扱いにしない</b>（GPT レビュー R6 の指摘 1）。
                // この下にはまだ門の復元など失敗しうる段があり、さらに活動の許可は
                // 世代・到着準備を確認した遷移サービスが出す。先に Idle へ戻してしまうと、
                // そのあとに落ちても失敗通知を受理できず、「再開する」の再表示・再試行が成立しない。
                // 完了の確定は AreaTransitionService が活動を許可したあとに行う（§9.1 手順 7）。
            }
            else if (transitions.TryPeekPendingTransfer(out AreaTransferSnapshot transfer))
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

            // 6a. 門の開通を記録から復元する（§4.3／§7.3）。
            //     復元は「すでに開いていた」のであって、いま開通したのではないので通知は出さない。
            //     ここで失敗したら Ready を確定しない：見た目だけ開いて通れない状態を受入にしない。
            foreach (Momotaro.Gameplay.Interaction.AreaFlagDoor door in _areaRoot.Doors)
            {
                if (door == null || !area.IsOpen(door.FlagId))
                {
                    continue;
                }

                if (!door.TryApplyOpened(out string doorError))
                {
                    return Fail("開通済みの門を復元できませんでした: " + doorError);
                }
            }

            // 6c. Encounter へ Session の世界状態を渡し、クリア済みを復元する（§4.3／§8.4 末尾）。
            //     常駐 Session は Scene へ serialize できないので、参照ではなく取り出し口を渡す。
            //     クリア済みの区画で Trigger を踏んでも戦闘が始まらないのは、この復元が効いているため。
            if (_encounter != null)
            {
                _encounter.BindSession(() => area, () => session.RespawnCycle);
                _encounter.RestoreFromRecord();
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

            // 7. 準備できたことを報告する。<b>活動の許可はここで出さない</b>（GPT レビュー R2 の指摘 1）。
            //    許可は世代・対象・タイムアウトを確認した所有者＝遷移サービスが出す。
            _context.MarkPrepared();

            if (arrivalToken != 0)
            {
                // 遷移で来た。開始時に捕まえたトークンで報告し、所有者の許可を待つ。
                AreaPendingArrival.TryMarkPrepared(arrivalToken, areaId, entryId);
            }
            else if (AreaPendingArrival.SelfActivationAllowed)
            {
                // 直開き（遷移が 1 つも走っていない起動）。所有者が居ないので自分で許可する。
                _context.Activate();
                GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            }
            else
            {
                // <b>トークンが無い＝直開き、ではない</b>（GPT レビュー R3 の指摘 1）。
                // 遷移が終端失敗して放棄されたあと、キャンセルできないロードが遅れて
                // この Scene を読み終えた場合もここに来る。許可を出す所有者はもう居ないので、
                // 準備だけ済ませて<b>活動させない</b>。プレイヤーは Error 表示から Launcher へ戻る。
                SelfActivationBlocked = true;
                AreaPendingArrival.NoteBlockedSelfActivation();
                GameLog.Warning(LogCategory.Scene,
                    "Arrived after the transition was abandoned; not activating this area: " + areaId.Value);
            }

            Initialized = true;
            FailureReason = string.Empty;
            GameLog.Info(LogCategory.Scene, "Area ready: " + areaId.Value + " / " + entryId.Value);
            return true;
        }

        /// <summary>
        /// 初期化の<b>開始時点</b>の到着トークンを捕まえる（§6.2 末尾。GPT レビュー R2 の指摘 4）。
        ///
        /// 報告のときに <see cref="AreaPendingArrival.TransitionId"/> を読み直すと、
        /// 渡す値と比べる値が同じものになり、照合が常に成立してしまう。
        /// 初期化の途中で新しい遷移が始まっていた場合、<b>古い Scene の初期化担当が
        /// 新しい世代を「準備できた」ことにしてしまう</b>。だから開始時に捕まえて持ち回る。
        ///
        /// 自分宛てでない（別エリア宛ての）要求は 0 を返す＝直開き扱い。
        /// </summary>
        public static int CaptureArrivalToken(StableId areaId) =>
            AreaPendingArrival.HasPending && AreaPendingArrival.AreaId.Equals(areaId)
                ? AreaPendingArrival.TransitionId
                : 0;

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
