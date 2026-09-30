using System.Collections;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Data.World;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// スライド遷移の実行役（P5.5 仕様書 §6.2。工程 P55-04b）。
    ///
    /// <b>判断は <see cref="AreaSlideCoordinator"/>、実行はここ</b>——P5 の
    /// <see cref="AreaTransitionCoordinator"/>／<see cref="AreaTransitionService"/> と同じ分け方である。
    /// 受付・世代・Commit・Rollback の規則は純粋クラス側にあり EditMode で検証済み。
    /// ここがやるのは Unity への橋渡しだけ：Additive ロード、活動ゲートの開閉、
    /// カメラのスライド、表示代理、旧 Scene の撤去。
    ///
    /// <b>P5 の Single 経路を 1 行も変えない。</b> 既存の 64 件の必須テストはすべて
    /// <see cref="AreaTransitionService.TryTravel(StableId, StableId)"/> の
    /// <c>TravelRoutine</c> を通るので、そこへ分岐を足すのではなく<b>別経路</b>にした（§1.2 と同じ考え方）。
    ///
    /// <b>排他・Scene 操作の直列化は共有する</b>（§8「別サービスが同時ロードを発行しない」）。
    /// 常駐は 1 つ（<see cref="AreaTransitionService"/>）で、そこが Fade と Slide の
    /// どちらかしか走らせない。Scene 操作の直列化は <see cref="AreaResidencyLedger"/> が正本。
    ///
    /// <b>成功確定点は 1 か所だけ</b>（§6.2「成功確定点は 8〜9 の同期区間。そこに yield を挟まない」）。
    /// 訪問登録・活動許可・完了通知は、その区間を通ったあとにしか出さない。
    /// </summary>
    public sealed class AreaSlideTransitionRunner
    {
        /// <summary>
        /// 表示を進める 1 フレームの上限（unscaled 秒）。
        ///
        /// <b>巨大 delta で飛ばさない</b>（§7.1 末尾）。アプリが止まっていた・重いフレームが
        /// 挟まった場合の <c>unscaledDeltaTime</c> は数秒になりうる。そのまま渡すと
        /// 1 フレームで演出が終わり、通路を渡る絵が出ない。
        /// </summary>
        public const float MaxDisplayStepSeconds = 0.1f;

        /// <summary>
        /// 接続が所要秒を持たないときの既定（§7.1 の初期値 0.45 秒）。
        /// 正本は Data 側（<see cref="AreaConnectionDefinition"/>）で、ここは 0 が来たときの保険。
        /// </summary>
        public const float FallbackSlideSeconds = 0.45f;

        /// <summary>スライドを接続軸に載せる許容ずれ（m）。</summary>
        public const float AxisTolerance = AreaConnectionRules.AxisEpsilon;

        /// <summary>
        /// 帯の検査で刻む数（工程 P55-14d）。
        ///
        /// 直線ならば始点・終点だけで足りる（帯は凸なので間は必ず内側）。
        /// <b>刻むのは、経路が直線でなくなった日に気付くため</b>である——
        /// §7.1 の補間曲線は進み方だけを変えていて経路は線分のままだが、
        /// そこを変える改修が入ったときに、この検査が黙って空振りするのを避ける。
        /// </summary>
        private const int BandSamples = 16;

        /// <summary>戻しにかける秒の上限（§8 の 3 行目「最大 0.25 秒で戻す」）。</summary>
        public const float ReverseSeconds = 0.25f;

        /// <summary>
        /// 戻しの下限（秒）。0 にすると 1 フレームで飛ぶので、わずかでも動きを見せる。
        /// </summary>
        public const float MinReverseSeconds = 0.05f;

        private readonly AreaTransitionService _owner;
        private readonly AreaSlideCoordinator _slide = new AreaSlideCoordinator();
        private readonly AreaResidencyLedger _residency = new AreaResidencyLedger();
        private AreaPreloader _preloader;
        private AreaInstanceHandle _retireHandle;
        private int _retireSceneHandle;

        /// <summary>
        /// 撤去の<b>再試行</b>が走っている最中か（工程 P55-07c。GPT 再修正①）。
        ///
        /// <b><see cref="_retiring"/> と同じ重みで扱う。</b> どちらも「終端していない Unload を
        /// この遷移系が掴んでいる」状態で、区別しているのは<b>誰が始めたか</b>だけである。
        /// 以前はここを所有判定から落としていたので、撤去に失敗したあと再試行を始め、
        /// その Unload が走っている最中に Fade を頼むと <b>Single 読込がそのまま発行された</b>。
        /// </summary>
        private bool _retrying;


        /// <summary>
        /// いま走っている（または直前に走っていた）スライドの接続 ID（工程 P55-08d）。
        ///
        /// <b>失敗したときに抑止する相手を名指しするために持つ。</b> 距離による先読みの
        /// 「保持」から推し測ることはできない——遷移中は選定が回らないので、
        /// 手動の再試行から入った遷移では保持が空のままである。
        /// </summary>
        private StableId _slidingConnection;


        private readonly AreaTransitionWaitNoticeTimer _waitNotice = new AreaTransitionWaitNoticeTimer();

        /// <summary>Single 読込の発行権を、この遷移系が押さえている最中か（工程 P55-07c）。</summary>
        private bool _singleLoadClaimed;

        /// <summary>
        /// 旧 Area を<b>通常どおり撤去している最中</b>か（工程 P55-07a。GPT 受入②）。
        ///
        /// <see cref="HasPendingRetire"/> は<b>撤去に失敗して抱えたまま</b>の状態で、次の要求を
        /// 断る。こちらは<b>成功する見込みのある撤去が走っている</b>状態で、次の要求は
        /// <b>断らずに待たせる</b>——§8 末尾の「旧 Area の解放終了を待ってから次のロードへ進む」。
        ///
        /// この 2 つを同じ 1 つの状態で表すと、待てばよいものを断ることになる。
        /// 実際そうなっていた：到着通知（<c>ArrivalCompleted</c>）は撤去より<b>先に</b>出るので、
        /// 通知の中から次のスライドを頼むと、まだ 2 枚在留していて先読みが上限で失敗した。
        /// </summary>
        private bool _retiring;

        /// <summary>作る。<paramref name="owner"/> が常駐の遷移サービス（Provider の所有者でもある）。</summary>
        public AreaSlideTransitionRunner(AreaTransitionService owner)
        {
            _owner = owner;
        }

        /// <summary>スライド遷移の調停役（診断・テスト用）。</summary>
        public AreaSlideCoordinator Coordinator => _slide;

        /// <summary>在留台帳（診断・テスト用）。Scene 操作の直列化もここが正本。</summary>
        public AreaResidencyLedger Residency => _residency;

        /// <summary>
        /// 先読み（診断・テスト用）。<b>初回の要求で作る</b>——
        /// Scene 操作の実装（<see cref="AreaTransitionService.SlideSceneHost"/>）は
        /// テストが差し替えるので、常駐の起動時に固定してしまうと注入できない。
        /// </summary>
        public AreaPreloader Preloader => EnsurePreloader();

        /// <summary>いまスライド遷移が走っているか（§8 の排他共有に使う）。</summary>
        public bool IsTransitioning => _slide.IsTransitioning;

        /// <summary>成功を確定した回数（診断・テスト用）。</summary>
        public int CommittedCount { get; private set; }

        /// <summary>出発側へ戻した回数（診断・テスト用）。</summary>
        public int RolledBackCount { get; private set; }

        /// <summary>戻り切れなかった回数（診断・テスト用）。</summary>
        public int FailedCount { get; private set; }

        /// <summary>Commit 後に旧 Area の撤去が失敗した回数（診断・テスト用。§8 の 4 行目）。</summary>
        public int UnloadFailureCount { get; private set; }

        /// <summary>旧 Area を預けようとした回数（診断・テスト用。工程 P55-10c）。</summary>
        public int RetainAttemptCount { get; private set; }

        /// <summary>旧 Area を<b>撤去せずに預けた</b>回数（診断・テスト用）。</summary>
        public int RetainedCount { get; private set; }

        /// <summary>預けられず、従来どおり撤去した回数（診断・テスト用）。</summary>
        public int RetainDeclinedCount { get; private set; }

        /// <summary>直近に預けられなかった理由（診断・テスト用。預けられたなら空）。</summary>
        public string LastRetainDecline { get; private set; } = string.Empty;

        /// <summary>
        /// 保持していた Area へ<b>戻った</b>回数（診断・テスト用）。
        /// 読み直していないことは、Scene の読込回数と Scene handle の一致で別に見る。
        /// </summary>
        public int ReenteredCount { get; private set; }

        /// <summary>
        /// 撤去し切れなかった旧 Area を抱えているか（§8 の 4 行目）。
        /// <b>抱えている間は新しいスライドを受け付けない</b>——在留枠が埋まったままなので、
        /// 受理してから「読めません」で戻すより、受理しないほうが害が小さい。
        /// </summary>
        public bool HasPendingRetire => _retireHandle.IsValid && _retireSceneHandle != 0;

        /// <summary>
        /// <b>撤去し切れていない Scene を抱えているか</b>（工程 P55-10c）。
        ///
        /// 裁定 2 で旧 Area は先読み枠へ預けるようになったので、片付かない Scene は
        /// 2 か所に出る——預けられずに撤去して失敗した旧 Area（<see cref="HasPendingRetire"/>）と、
        /// 預けた先の解放失敗（<c>AreaPreloadPhase.ReleaseFailed</c>）。
        /// 受入と表示はこの 1 つで言う。
        /// </summary>
        public bool HasUnreleasedScene =>
            HasPendingRetire
            || (_preloader != null && _preloader.Phase == AreaPreloadPhase.ReleaseFailed);

        /// <summary>旧 Area を通常どおり撤去している最中か（診断・テスト用）。</summary>
        public bool IsRetiring => _retiring;

        /// <summary>撤去の再試行が走っている最中か（診断・テスト用。工程 P55-07c）。</summary>
        public bool IsRetryingRetire => _retrying;

        /// <summary>
        /// <b>終端していない Unload を掴んでいるか</b>（工程 P55-07c。GPT 再修正①）。
        ///
        /// 通常の撤去（<see cref="_retiring"/>）と再試行（<see cref="_retrying"/>）を
        /// <b>1 つの言い方へまとめる</b>。入口ごとにどちらか片方だけを見ると、必ず片方が抜ける。
        /// </summary>
        public bool IsRetireInFlight => _retiring || _retrying;

        /// <summary>
        /// Single 読込の発行権を押さえている最中か（工程 P55-07c。GPT 再修正①）。
        ///
        /// <see cref="DiscardStagedForSingleLoad"/> の開始から、実際の読込発行
        /// （<c>AreaTransitionService.TryStartLoad</c>）までの<b>すき間</b>を埋める。
        /// ここを空けておくと、所有を解いている数フレームのあいだに撤去の再試行が割り込み、
        /// <b>その Unload の上へ Single が重なる</b>。
        /// </summary>
        public bool SingleLoadClaimed => _singleLoadClaimed;

        /// <summary>Single 読込の発行権を手放す（発行したか、発行を諦めたとき）。</summary>
        public void ReleaseSingleLoadClaim()
        {
            _singleLoadClaimed = false;
        }

        /// <summary>Single 遷移と重なるため撤去の再試行を断った回数（診断・テスト用）。</summary>
        public int RetryBlockedCount { get; private set; }

        /// <summary>撤去の終わりを待ってから先へ進んだ回数（診断・テスト用）。</summary>
        public int RetireWaitCount { get; private set; }

        /// <summary>
        /// 先読みを終端できず、Single 読込へ進ませなかった回数（診断・テスト用）。
        /// </summary>
        public int StagedDiscardBlockedCount { get; private set; }

        /// <summary>
        /// 直前の <see cref="DiscardStagedForSingleLoad"/> が<b>所有を解けた</b>か。
        ///
        /// false のあいだ、呼び出し元は<b>次のロードを発行してはならない</b>。
        /// </summary>
        public bool StagedDiscardCompleted { get; private set; } = true;

        /// <summary>
        /// <b>終端していない Scene 操作を、この遷移系が掴んでいるか</b>（工程 P55-07a2）。
        ///
        /// <b>通常 Fade・死亡再開・Launcher 退避は、すべてここを見る。</b>
        /// 入口ごとに別々の条件を足すと、どれか 1 つが必ず抜ける——実際に
        /// Launcher 退避だけが <c>_liveOperation</c>（Single 側の監視）しか見ておらず、
        /// Additive の先読みを迂回していた（GPT 追加①）。
        ///
        /// ここが見るのは<b>終端していない操作</b>だけである。
        /// 「載ったままの Scene」は <see cref="HoldsRemainingScene"/> が別に言う——
        /// Single 読込はすべての Scene を置き換えるので、操作の衝突にはならない。
        /// </summary>
        public bool HasLiveSceneOperation =>
            (_preloader != null && _preloader.HasLiveSceneOperation) || IsRetireInFlight;

        /// <summary>
        /// <b>操作は終端したが、実 Scene を預かったまま</b>か。
        ///
        /// 先読みの Staged／撤去失敗と、撤去し切れなかった旧 Area がこれに当たる。
        /// <b>Single 読込は止めない</b>——止めると、撤去できない Scene を抱えたプレイヤーに
        /// 出口が無くなる（死亡再開も Launcher 退避もできない）。
        /// 代わりに <see cref="SingleLoadOverRemainingSceneCount"/> で数え、
        /// Single のあと <see cref="NotifySingleLoadCompleted"/> が台帳を実 Scene へ合わせ直す。
        /// </summary>
        public bool HoldsRemainingScene =>
            (_preloader != null && _preloader.HoldsStagedScene) || HasPendingRetire;

        /// <summary>載ったままの Scene を抱えたまま Single 読込へ進んだ回数（診断・テスト用）。</summary>
        public int SingleLoadOverRemainingSceneCount { get; private set; }

        /// <summary>犬丸の表示経路を選べず、運ばなかった回数（診断・テスト用。工程 P55-07c）。</summary>
        public int CompanionRouteDroppedCount { get; private set; }

        /// <summary>直近で犬丸を運ばなかった理由（診断・テスト用）。</summary>
        public string LastCompanionRouteBlocked { get; private set; } = string.Empty;

        /// <summary>実 Scene に合わせて台帳から落とした実体の数（診断・テスト用）。</summary>
        public int ForgottenResidentCount { get; private set; }

        /// <summary>撤去の再試行を始めた回数（診断・テスト用）。</summary>
        public int RetryStartedCount { get; private set; }

        /// <summary>ロード監視の上限を超えた回数（診断・テスト用。§8 の 5 行目）。</summary>
        public int TimedOutCount { get; private set; }

        /// <summary>撤去を「終端後」へ持ち越した回数（診断・テスト用）。</summary>
        public int DeferredReleaseCount { get; private set; }

        /// <summary>同じ描画経路を逆向きに戻した回数（診断・テスト用。§8 の 3 行目）。</summary>
        public int ReversedCount { get; private set; }

        /// <summary>直近の失敗理由（成功なら空）。</summary>
        public string LastFailure { get; private set; } = string.Empty;

        /// <summary>直近のスライドの始点・終点（診断・テスト用）。</summary>
        public Vector3 LastSlideFrom { get; private set; }

        /// <summary>直近のスライドの終点（診断・テスト用）。</summary>
        public Vector3 LastSlideTo { get; private set; }

        /// <summary>
        /// スライドで遷移を要求する（§6.2 手順 1〜3）。
        ///
        /// <b>受理前に State を変えない</b>（手順 1 末尾）。行き先が引けない・出発側が分からない・
        /// Fade が走っている——どれも「その場に留まる」であって、活動を閉じてはいけない。
        /// </summary>
        public AreaTransitionDecision TryTravel(in AreaConnectionSnapshot connection)
        {
            AreaCatalog catalog = _owner.Catalog;
            if (catalog == null)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
            }

            // 1. 行き先の Scene と入口を解決する（受理前の不正 Data で State を変えない）。
            if (!catalog.TryGetEntry(connection.ToAreaId, connection.EntryId, out AreaEntryInfo entry)
                || string.IsNullOrEmpty(entry.ScenePath))
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.UnknownDestination);
            }

            // <b>Fade と排他を共有する</b>（§8）。別サービスではなく同じ常駐が持っているので、
            // ここで見るだけで「同時に 2 本のロードが飛ぶ」を防げる。
            if (_owner.Coordinator != null && _owner.Coordinator.IsTransitioning)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.AlreadyTransitioning);
            }

            // <b>台帳を実 Scene に合わせ直す</b>（§8 末尾の Fade 共存）。
            // 毎フレームの後始末（<see cref="Pump"/>）でも同じことをしているが、
            // <b>ここでもう一度やる</b>——出入口の <c>Update</c> と常駐の <c>Update</c> の
            // 実行順は宣言できないので、受理の瞬間に台帳が古い可能性が残る。
            SyncResidencyToLoadedAreas();

            // 撤去し切れなかった旧 Area を抱えている間は、新しい Area ロードを出さない
            // （§8 の 4 行目「新たな Area ロードを止め、理由と再試行手段を表示」）。
            // <b>Single／Fade は止めない</b>——あちらは全部を置き換えるので、
            // 載ったままの旧 Area もろとも解決する（台帳は次の受理で実 Scene に合わせ直す）。
            if (HasPendingRetire)
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
            }

            if (!TryResolveDeparture(out AreaRuntimeBundle departure))
            {
                return AreaTransitionDecision.Reject(AreaTransitionRejection.NotReady);
            }

            AreaTransitionDecision decision = _slide.TryRequest(connection, _owner.Conditions);
            if (!decision.Accepted)
            {
                return decision;
            }

            LastFailure = string.Empty;

            // 2. 出発 Area を止める。GameMode=Loading、Gameplay 時計を凍結する。
            //    <b>凍結はここで行う</b>——P5 の調停役は自分で凍結するが、スライドの調停役は
            //    UnityEngine に触らない純粋クラスなので時計を持っていない（§9）。
            departure.Context.CloseForTransition();
            _owner.Clock.Freeze();
            GameModeProvider.Current?.ChangeMode(GameMode.Loading);

            // 3. 中断により発生する CD を含めて Snapshot を採る（手順 4→5 は Port が 1 か所で行う）。
            _owner.CaptureActorsForTransition();

            _owner.StartSlideRoutine(SlideRoutine(connection, decision.TransitionId, departure, entry));
            return decision;
        }

        /// <summary>
        /// §6.2 手順 4〜11。<b>各段の通知は必ず自分の世代を添えて出す</b>——
        /// 一致しなければ何も起きない（古い Coroutine が新しい遷移を壊さない）。
        /// </summary>
        private IEnumerator SlideRoutine(
            AreaConnectionSnapshot connection, int transitionId,
            AreaRuntimeBundle departure, AreaEntryInfo entry)
        {
            int departureSceneHandle = departure.SceneHandle;

            // <b>いまの接続を最初に控える</b>（工程 P55-08d）。
            //
            // 準備のどこで失敗しても、<c>Rollback</c> が<b>この接続を名指しで抑止できる</b>ように
            // しておく。段階が進む前に失敗する経路（撤去待ちの時間切れ・在留枠が取れない）も
            // あるので、控えるのは手順の途中ではなく<b>入口</b>である。
            _slidingConnection = connection.ConnectionId;

            if (!_slide.NotifyPreparing(transitionId))
            {
                yield break;
            }

            // <b>準備待ちはここから通算で数える</b>（§5。工程 P55-10e。GPT 追加修正）。
            //
            // 工程 P55-09a では先読みの読込待ちだけを数えていた。その前後——
            // <b>旧 Area の撤去待ち</b>と<b>到着側の初期化待ち</b>——が抜けていたので、
            // ロードが終わっていても初期化が遅れれば<b>操作不能のまま表示なしで待たされた</b>。
            //
            // 表示の条件は「ロードを待っているか」ではなく
            // <b>「操作できないまま待たされているか」</b>である。だから受理からスライド開始までを
            // 1 本の待ちとして数え、<b>段階が変わってもリセットしない</b>——
            // 区間ごとに 0 から数えると、どの区間も 0.3 秒に届かないまま合計 1 秒待つ、が起きる。
            _waitNotice.Begin();

            // <b>前の撤去が終わるのを待つ</b>（§8 末尾。GPT 受入②）。
            //
            // 到着通知は撤去より先に出るので、通知の中から次のスライドを頼むと、
            // この時点ではまだ旧 Area と到着 Area の 2 枚が在留している。
            // ここで待たずに進むと、先読みが在留上限で失敗する——
            // <b>受理できたのに進めない</b>という、いちばん分かりにくい失敗になる。
            // 撤去に失敗して抱えている場合（<see cref="HasPendingRetire"/>）は
            // そもそも受理していないので、ここへは来ない。
            if (IsRetireInFlight)
            {
                RetireWaitCount++;
                float retireWaited = 0f;
                while (IsRetireInFlight && retireWaited < _owner.TimeoutSeconds)
                {
                    // <b>段階ごとの上限は段階が数える。</b> 表示用の通算とは別に持つ
                    // （通算へ寄せると「読込は終わったが初期化が返ってこない」の切り分けが消える）。
                    retireWaited += Time.unscaledDeltaTime;
                    PumpWaitNotice();
                    yield return null;
                }

                if (IsRetireInFlight)
                {
                    yield return Rollback(transitionId, departure, AreaInstanceHandle.None,
                        "旧 Area の撤去が終わらないため、次のスライドを始められませんでした。");
                    yield break;
                }
            }

            AreaInstanceHandle departureHandle = EnsureResidency(departure);
            if (!departureHandle.IsValid)
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "出発 Area を在留台帳へ登録できませんでした。");
                yield break;
            }

            _residency.TrySetPhase(departureHandle, AreaActivationPhase.Suspended);

            // 到着側の初期化担当が「自分宛ての到着」と分かるようにする（§5.2／§6.2 手順 7）。
            // <b>これを置かないと</b>到着側は直開きと見なして自分で活動を始め、
            // Commit を待たずに遊べてしまう。
            AreaPendingArrival.Set(transitionId, connection.ToAreaId, connection.EntryId);

            // ---- 手順 4：対応する StagedReady を取得する ----
            //
            // <b>先読みを通す。</b> 距離による先読み（§5）が同じ先を望んでいれば読み直さず
            // 引き取るだけになる（<c>AreaPreloader.Request</c> の同一先再利用）。
            // ここで独自にロードを発行すると、先読みと二重に読むことになる。
            AreaPreloader preloader = EnsurePreloader();
            preloader.ArmRetry(); // 新しい遷移操作は、抱えている失敗を 1 回だけ再試行できる（§5）。

            // <b>ロードが始まるのはここだけになった</b>（工程 P55-15c。読み込み方針の裁定）。
            //
            // <b>待機表示はこの前に始まっている</b>（<c>_waitNotice.Begin()</c> は
            // <c>NotifyPreparing</c> の直後）。裁定の「ロード開始前に待機表示を描画できるようにし、
            // 『壁に引っかかったように停止してから表示が出る』順序を避ける」はこの順番のことである。
            //
            // <b>ロード済みなら読み直さない</b>（<c>Request</c> の同一先再利用）——
            // 引き継いだ（裁定 2）Area も、まだ預かっている Area もここで拾われる。
            // <b>人工的な待ちは入れない。</b>
            if (preloader.Request(connection.ToAreaId, entry.ScenePath))
            {
                PreloadRequestCount++;
            }

            // <b>待ちが長いときだけ「読み込み中」を出す</b>（§5。工程 P55-09a）。
            //
            // 先読みが間に合っている正常系では、この待ちは 1〜2 フレームで終わる。
            // そこで一瞬だけ字を出すと<b>ちらつきとして見える</b>ので、0.3 秒の壁を置く。
            // 壁の判断は <c>AreaTransitionWaitNoticeTimer</c> が持ち、
            // 表示側は「出す・消す」しか知らない。
            bool timedOut = false;
            float preloadWaited = 0f;
            while (preloader.Phase == AreaPreloadPhase.Loading
                   || preloader.Phase == AreaPreloadPhase.Releasing)
            {
                // <b>望みを毎フレーム言い直す。</b> 先読みは「望む先を宣言する」形で、
                // 同じ先なら読み直さない（§5）。言い直しておけば、距離による先読みが
                // 同じフレームに別の候補を望んでも、走っている遷移の行き先が
                // 下から差し替わらない。<b>同じ先で読み直さないこと</b>が効いている前提なので、
                // 受入（§11 の P07「重複ロードなし」）でも読込の回数を数える。
                preloader.Request(connection.ToAreaId, entry.ScenePath);
                preloader.Poll();

                // <b>この段階の上限は、この段階が数える</b>（§5。工程 P55-10e。GPT 追加修正）。
                //
                // 工程 P55-09a では表示用の通算と共通にしていた（記録 037「1 か所で数える」）。
                // 通算が受理からスライド開始までへ広がったので、共通にすると
                // <b>読込の監視が撤去待ちの時間まで含めて切れる</b>——
                // 「読込は終わったが初期化が返ってこない」の切り分けも消える。
                // <b>表示は通算・監視は段階ごと</b>で分ける。
                if (preloadWaited >= _owner.TimeoutSeconds)
                {
                    timedOut = true;
                    break;
                }

                preloadWaited += Time.unscaledDeltaTime;
                PumpWaitNotice();
                yield return null;
            }

            preloader.Poll();

            if (timedOut)
            {
                // ---- ロード監視のタイムアウト（§8 の 5 行目）----
                //
                // <b>古い操作はキャンセルできたと扱わない。</b> Unity の非同期ロードは
                // 止められないので、監視を諦めても操作は走り続ける。望みだけ取り下げて
                // 出発側へ戻り、<b>終端したあとに</b>遅れて着いた Scene を
                // Staged のまま撤去する——それを進めるのが常駐の <see cref="Pump"/>。
                //
                // 遅れて着いた Scene が自分で動き出す心配は無い：先読みの申し入れを
                // その Scene の活動ゲートが <c>Awake</c> で消費して<b>閉じたまま</b>起動し、
                // 誰も開けないので初期化担当も走らない（§4.2）。
                TimedOutCount++;
                DeferredReleaseCount++;
                yield return Rollback(transitionId, departure, departureHandle,
                    "目的地のロードが " + _owner.TimeoutSeconds.ToString("0.##")
                    + " 秒以内に完了しませんでした（操作は保持し、終端後に撤去します）。",
                    waitForPreloadRelease: false);
                yield break;
            }

            if (preloader.Phase != AreaPreloadPhase.Staged
                || !preloader.StagedArea.AreaId.Equals(connection.ToAreaId))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "目的地の先読みが完了しませんでした（" + preloader.Phase + " / "
                    + preloader.FailureReason + "）。");
                yield break;
            }

            AreaInstanceHandle destinationHandle = preloader.StagedArea;
            int destinationSceneHandle = preloader.StagedSceneHandle;

            if (!AreaBundleDirectory.TryGetByScene(destinationSceneHandle,
                    out AreaRuntimeBundle destination)
                || destination == null || destination.Context == null || destination.Root == null
                || destination.ActivityGate == null)
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "到着 Area の参照集合を引けませんでした（束・活動ゲートの配線漏れ）。");
                yield break;
            }

            // 実体ハンドルを結ぶ（§4.3）。カメラの終点計算はこのハンドルで到着 Area を引く。
            destination.BindInstance(destinationHandle);

            // ---- 手順 5：出発側を閉じ、到着側を隔離された Prepared へ ----
            //
            // <b>代理を立ててから閉じる（同じフレーム）。</b> 出発側の活動ゲートは
            // 主人公・犬丸の物体ごと非 Active にするので、先に閉じると
            // 1 フレームだけ主人公が居ない絵が出る（§7.2「一瞬の欠落を作らない」）。
            IAreaTransitionDisplay display = AreaTransitionDisplayProvider.Current;
            Vector3 playerFrom = ResolvePlayerPosition(departure);
            display?.TryBegin(departure);
            departure.ActivityGate?.Close();

            // 到着側の活動ゲートを開けて初期化を走らせる。
            //
            // <b>「開ける」は「遊べる」ではない。</b> 初期化担当（<see cref="AreaInitializer"/>）は
            // 活動ゲートの内側に居るので、閉じたままでは Snapshot の復元も入口への配置も走らない。
            // 遊べるかどうかを決めるのは <c>AreaContext.IsAreaReady</c> と Gameplay 時計で、
            // どちらもまだ閉じている。索敵・Interact は <see cref="CurrentAreaProvider"/> が
            // まだ出発側を差しているので絞り込みの外にある（§4.1「外部へ公開していない」）。
            destination.ActivityGate.Open();
            _residency.TrySetPhase(destinationHandle, AreaActivationPhase.Prepared);

            // <b>保持していた Area へ戻ったなら、入場準備を明示的に呼ぶ</b>
            // （§6.2 手順 5。裁定 2。工程 P55-10c）。
            //
            // 新しく読んだ Scene なら、ゲートを開けた時点で初期化担当の <c>Start</c> が走る。
            // <b>保持していた Area では走らない</b>——<c>Start</c> に二度目は無いからである。
            // 呼ばないと入口配置も Snapshot の復元も到着世代の報告も起きず、
            // 下の「準備できた？」の待ちが必ずタイムアウトする。
            if (destination.TryResolve(out AreaInitializer arrivalInitializer)
                && arrivalInitializer.SceneBuilt)
            {
                ReenteredCount++;
                if (!arrivalInitializer.PrepareForEntry())
                {
                    yield return Rollback(transitionId, departure, departureHandle,
                        "保持していた Area の入場準備に失敗しました: " + arrivalInitializer.FailureReason,
                        destination);
                    yield break;
                }
            }

            // <b>開けた瞬間から隠す</b>（工程 P55-07b。GPT 受入③）。
            //
            // 以前は終点を測り終えてから隠していた。ゲートを開けてから隠すまでのあいだ、
            // <b>出発側の代理と到着側の実 Actor が同時に映りうる</b>——
            // 東西配置では到着入口も出発カメラの画角に入る。
            // 実描画の検査（§11 の P15）は <c>IsSliding</c> になってから撮るので、
            // この準備区間は対象外だった。
            display?.HideArrivals(destination);

            float bindWaited = 0f;
            while (!AreaPendingArrival.IsPreparedFor(transitionId)
                   && bindWaited < _owner.BindTimeoutSeconds)
            {
                bindWaited += Time.unscaledDeltaTime;
                PumpWaitNotice();
                yield return null;

                // <b>毎フレーム隠し直す。</b> 初期化担当が Actor を置き、Animator や
                // Presenter が Renderer を有効化し直すので、一度隠すだけでは戻ってくる。
                // 既に無効な Renderer は預からないので、呼び直しても畳むときに増えない。
                display?.HideArrivals(destination);
            }

            if (!AreaPendingArrival.IsPreparedFor(transitionId))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "到着側の準備が " + _owner.BindTimeoutSeconds.ToString("0.##") + " 秒以内に完了しませんでした。",
                    destination);
                yield break;
            }

            // <b>描画準備完了後のフレームを一度通す</b>（§5「固定秒数の待機では準備確認を代用しない」）。
            // 準備完了の報告はロジックの完了で、Renderer が実際に 1 枚描かれたかは別。
            yield return null;
            display?.HideArrivals(destination);

            if (!_slide.NotifyPrepared(transitionId))
            {
                yield break;
            }

            // ---- 終点を測る（§7.1「終点は到着 Actor 位置に対して到着 Area の通常追従・clamp が算出する位置」）----
            if (!destination.Root.TryGetEntryPoint(connection.EntryId, out AreaEntryPoint arrival))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "到着入口 '" + connection.EntryId.Value + "' が到着 Scene にありません。", destination);
                yield break;
            }

            IAreaCameraOwner camera = AreaCameraOwnerProvider.Current;
            if (camera == null
                || !camera.TryGetRigPosition(out Vector3 slideFrom)
                || !camera.TryComputeArrivalPoint(destinationHandle, arrival.ArrivalPosition,
                    out Vector3 slideTo))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "スライドの終点を計算できませんでした（常駐カメラ・到着 Area のカメラ領域）。",
                    destination);
                yield break;
            }

            // <b>帯の外へ出る経路は準備失敗</b>（§7.1 改定／§7.2 末尾
            // 「表示経路を安全に作れない接続は準備失敗とする」）。
            // 無断で暗転へ切り替えて成功扱いにしない。
            if (!TryCheckSlideBand(connection, arrival.ArrivalPosition, slideFrom, slideTo,
                    out string bandFailure))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    bandFailure, destination);
                yield break;
            }

            LastSlideFrom = slideFrom;
            LastSlideTo = slideTo;

            // ---- 表示経路（§7.2。GPT 受入④）----
            //
            // <b>終点は「準備済みの到着位置」を実体から測る。</b> 入口の名目位置ではなく、
            // <c>AreaInitializer.PlaceArrivals</c> が実際に置いた場所を読む——
            // 到着位置が塞がっていれば代替配置へ回るので、名目位置とはずれる。
            // 犬丸は「入口から進行方向と逆へ 1.2m」に置かれるので、
            // <b>主人公の移動差分で運ぶと畳んだ瞬間に跳ぶ</b>。
            Vector3 playerTo = ResolvePlayerPosition(destination);
            bool hasCompanionRoute = TryResolveCompanionPositions(
                departure, destination, out Vector3 companionFrom, out Vector3 companionTo);

            // <b>出発側の当たりを検査のあいだだけ戻す</b>（工程 P55-07c。GPT 再修正②）。
            //
            // 手順 5 で出発側を閉じているので、この時点で<b>出発側の地形 Collider は無効</b>である。
            // <c>Physics.SphereCast</c> はそれを見ないので、以前の検査は
            // <b>到着側の壁しか見えていなかった</b>——犬丸が出発側の壁の向こうに居る配置で、
            // 代理が壁を突き抜ける経路を「安全」と判定できてしまう。
            //
            // 戻すのは Collider だけで、Gameplay（根・仕掛け・NavMesh）は止めたまま。
            // 到着側は既に開いているので、こちらは何もしなくても見える。
            // <b>いま使う接続の境界壁だけを障害物から外す</b>（工程 P55-14b。GPT 受入 3）。
            //
            // 接続口には<b>見えない境界</b>（<see cref="AreaSeamBarrier"/>）が立っている——
            // 出口判定が成立しないまま Area の外へ出られないようにするためである（試遊報告③）。
            // それを普通の壁として数えると、主人公の表示経路は<b>必ず塞がっている</b>ことになり、
            // 接続そのものが「表示経路を安全に作れません」で失敗する。
            //
            // <b>外すのはこの接続の境界だけ。</b> 通常の壁・閉じた門・別の接続の境界は障害物のまま。
            // レイヤーで一括除外にしないのは、壁レイヤーが Default だからでもあるが、
            // それ以前に<b>外して良いのはこの 1 枚だけ</b>だからである。
            List<Collider> ignoredForRoute = CollectSeamBarriers(
                departure, destination, connection.ConnectionId, connection.ReverseConnectionId);

            bool playerRouteClear;
            bool companionRouteClear;
            departure.ActivityGate?.BeginObstacleProbe();
            try
            {
                playerRouteClear = IsDisplayRouteClear(playerFrom, playerTo, ignoredForRoute);
                companionRouteClear =
                    !hasCompanionRoute
                    || IsDisplayRouteClear(companionFrom, companionTo, ignoredForRoute);
            }
            finally
            {
                departure.ActivityGate?.EndObstacleProbe();
            }

            // <b>安全に作れない表示経路は準備失敗</b>（§7.2 末尾）。
            // 無断で暗転へ切り替えて成功扱いにしない。
            //
            // <b>準備失敗にするのは主人公の経路だけ</b>である。§7.2 末尾が「準備失敗とする」と
            // 言っているのは<b>接続</b>——主人公が通路を渡る経路そのもので、
            // 塞がっていれば接続の作りが悪い（配置の誤り）。
            // 犬丸については同じ §7.2 が「障害物を横切らない表示経路を<b>選び</b>」と言う。
            // 犬丸の位置は遊びの結果であって接続の性質ではないので、
            // ここで遷移ごと断ると<b>犬丸を置き去りにしただけで出入口が使えなくなる</b>。
            if (!playerRouteClear)
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "表示経路を安全に作れません（主人公 " + playerFrom + "→" + playerTo + "）。",
                    destination);
                yield break;
            }

            // 到着側の実 Actor を隠す（§6.2 手順 6「両方の実 Actor の Renderer は隠し」）。
            // 隠さないと、通路を渡る代理と入口で待つ到着 Actor が二重に映る。
            display?.HideArrivals(destination);

            // <b>選べる経路が無いなら運ばない</b>（工程 P55-07c）。
            //
            // 汎用経路探索は導入しない（調査移動と同じ方針）ので、直線が塞がっていれば
            // 「横切らない経路」は選べない。運ばずに到着地点で現れてもらう——
            // 実 Renderer は <c>HideArrivals</c> が預かっているので、
            // <c>Release</c> が到着側で戻すまで二重表示にはならない。
            if (!companionRouteClear)
            {
                CompanionRouteDroppedCount++;
                LastCompanionRouteBlocked = "犬丸の表示経路が塞がっています（" + companionFrom
                    + "→" + companionTo + "）。運ばずに到着地点で現します。";
                GameLog.Info(LogCategory.Scene, LastCompanionRouteBlocked);
                display?.DropCompanionProxy();
                hasCompanionRoute = false;
                companionFrom = companionTo;
            }

            display?.SetRoute(playerFrom, playerTo, companionFrom, companionTo);
            LastPlayerRouteTo = playerTo;
            LastCompanionRouteTo = companionTo;

            // ---- 手順 6：スライド ----
            //
            // <b>ここで準備待ちは終わる</b>（§5「スライド開始・Rollback・終端失敗で消す」）。
            // 以後は操作不能でも<b>絵が動いている</b>ので、「待たされている」ではない。
            EndWaitNotice();

            if (!camera.BeginSlide(slideTo, ResolveSeconds(connection)))
            {
                yield return Rollback(transitionId, departure, departureHandle,
                    "スライドを開始できませんでした（常駐カメラが未配線）。", destination);
                yield break;
            }

            if (!_slide.NotifySlideStarted(transitionId))
            {
                yield break;
            }

            while (true)
            {
                float step = DisplayStep();
                bool running = camera.TickSlide(step);

                // カメラと<b>同じ</b>進行度を配る（§7.2）。自前の時計から作ると別の曲線になる。
                display?.SetProgress(camera.SlideEased);
                display?.TickDisplayClock(step);

                // <b>毎フレーム到着側の健全性を確かめる</b>（§8 の 3 行目）。
                // スライドは複数フレームにわたるので、その間に到着側が壊れうる
                // （Scene が外から撤去された・初期化担当が失敗して Context ごと消えた）。
                // 気付かずに Commit すると、壊れた Area を活動させてしまう。
                if (!IsDestinationHealthy(destination, destinationSceneHandle, out string midReason))
                {
                    yield return RollbackDuringSlide(transitionId, departure, departureHandle,
                        "スライド中に到着側が壊れました: " + midReason, destination,
                        camera, display, slideFrom, camera.SlideEased);
                    yield break;
                }

                if (!running)
                {
                    break;
                }

                yield return null;
            }

            camera.EndSlide();

            // ---- 手順 7：終点へ配置したあと、Commit の前に<b>もう一度</b>確かめる ----
            //
            // §6.2 手順 7 は「到着 Actor の復元値、配線、安全位置、世代を再確認する」と定めている。
            // 演出は 0.45 秒あるので、始める前に確かめたことは<b>着いた時点の保証にならない</b>。
            if (!IsDestinationHealthy(destination, destinationSceneHandle, out string arrivalReason)
                || !destination.Root.TryGetEntryPoint(connection.EntryId, out _))
            {
                yield return RollbackDuringSlide(transitionId, departure, departureHandle,
                    "到着の再確認に失敗しました: " + arrivalReason, destination,
                    camera, display, slideFrom, 1f);
                yield break;
            }

            // ---- 手順 8〜9：成功確定の同期区間。ここに yield を挟まない ----
            if (!_slide.TryCommit(transitionId))
            {
                // 世代が進んでいた。共有領域には触らず、代理だけ畳む。
                display?.Release();
                yield break;
            }

            // 先読みから所有を引き取る。<b>Commit まで引き取らない</b>のは、
            // ここより前の失敗で到着 Area を撤去する役を先読みに任せておくため。
            preloader.TryHandOffStaged(connection.ToAreaId, out _, out _);

            _residency.TrySetPhase(destinationHandle, AreaActivationPhase.Active);

            // ---- 手順 11（裁定 2）：旧 Area は撤去せず、非活動のまま先読み枠へ預ける ----
            //
            // <b>所有の移動は完了通知より前に確定させる</b>（§6.2 手順 11 の契約表）。
            // 通知の中から次の要求が来ても、そのときには「誰がこの Area を持っているか」が
            // もう決まっている必要がある。
            bool retained = TryRetainDeparture(
                preloader, departureHandle, departureSceneHandle, departure);
            if (!retained)
            {
                _residency.TrySetPhase(departureHandle, AreaActivationPhase.Retiring);
            }

            CurrentAreaProvider.TrySetCurrent(_owner, destination);
            TrySetActiveScene(destinationSceneHandle);

            // 受付条件の窓口も到着側へ（§6.2 手順 8「受付条件」）。
            // 到着側の初期化担当が Prepared の時点で差しているが、<b>明示的に差し直す</b>——
            // 暗黙に任せると、初期化の配線が変わったときに静かに古い窓口が残る。
            _owner.RebindConditions(destination.Conditions);

            // <b>ここで初めて訪問済みを記録する</b>（手順 9／§4.1「Commit 時に行う」）。
            _owner.NoteArrival(connection.ToAreaId);

            destination.Context.Activate();
            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);

            // 実 Actor の表示を戻し、代理を除去する（手順 8）。
            display?.Release();

            // 運んだ値は使い終わった。復旧のために保持する理由がもう無い（§6.2 手順 10）。
            _owner.ClearPendingTransfer();
            AreaPendingArrival.Clear();
            _owner.Clock.Thaw();
            CommittedCount++;
            LastFailure = string.Empty;

            // <b>通知より先に「撤去中」を立てる。</b> 通知の中から次の要求が来ても、
            // そこで待てるようにするため（GPT 受入②）。
            // <b>預けられたなら撤去は走らない</b>ので、立てる必要も無い。
            _retiring = !retained;
            // ---- 同期区間ここまで ----

            // 手順 10：排他と共有情報を片付けてから、一度だけ通知する。
            if (_slide.TryConsumeArrivalAnnouncement(transitionId) && _slide.Release(transitionId))
            {
                _owner.RaiseArrivalCompleted(connection.ToAreaId);
            }

            if (retained)
            {
                // 預けた。<b>毎回の unload は行わない</b>（§6.2 手順 11）。
                // 解放するのは、別の先読み候補へ切り替えるとき・Single 読込・
                // New Game／Launcher 退避——どれも既存の経路が面倒を見る。
                yield break;
            }

            // 手順 11（預けられなかった場合）：旧 Area を撤去する。
            // <b>成功は取り消さない</b>（§8 の 4 行目）。
            yield return UnloadDeparture(departureHandle, departureSceneHandle);

            // 撤去が終わった（成功でも失敗でも）。失敗なら HasPendingRetire が立っていて、
            // 次の要求は受理そのものを断る。
            _retiring = false;
        }

        /// <summary>
        /// 到着側がまだ使えるか（§6.2 手順 7 の再確認／§8 の 3 行目）。
        ///
        /// <b>参照の生死を Unity の規則で見る。</b> 破棄された <c>MonoBehaviour</c> は
        /// <c>== null</c> が true になるので、束・Context・根のどれが消えても検知できる。
        /// 実 Scene が載っているかは別に問う——束が生きていても、
        /// Scene が外から撤去されていれば到着させてはいけない。
        /// </summary>
        private bool IsDestinationHealthy(
            AreaRuntimeBundle destination, int destinationSceneHandle, out string reason)
        {
            if (destination == null)
            {
                reason = "参照集合が失われました。";
                return false;
            }

            if (destination.Context == null || destination.Root == null)
            {
                reason = "AreaContext／AreaRoot が失われました。";
                return false;
            }

            IAreaSceneHost host = _owner.SlideSceneHost;
            if (host != null && !host.IsLoaded(destinationSceneHandle))
            {
                reason = "到着 Scene が載っていません。";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// <b>スライドの途中・成功確定の前</b>に失敗したので、同じ描画経路を逆向きに戻す
        /// （§8 の 3 行目「世界は停止したまま同じ描画経路を逆向きに最大 0.25 秒で戻す」）。
        ///
        /// <b>打ち切って瞬間移動させない。</b> 見ている側には「行きかけて戻った」に見えてほしい。
        /// 戻す時間は進んだ分に比例させ、上限を <see cref="ReverseSeconds"/> にする——
        /// 1 割進んだところで 0.25 秒かけて戻すと、失敗のほうが演出として長くなる。
        ///
        /// 表示代理は<b>同じ区間を逆にたどる</b>。進行度を減らしていくだけでよく、
        /// 区間を作り直さない——作り直すと犬丸の出発位置が「いまの途中位置」になり、
        /// 戻り切ったときに元の場所へ帰らない。
        /// </summary>
        private IEnumerator RollbackDuringSlide(
            int transitionId, AreaRuntimeBundle departure, AreaInstanceHandle departureHandle,
            string reason, AreaRuntimeBundle destination,
            IAreaCameraOwner camera, IAreaTransitionDisplay display,
            Vector3 slideFrom, float progressAtFailure)
        {
            float travelled = Mathf.Clamp01(progressAtFailure);
            float seconds = Mathf.Max(MinReverseSeconds, ReverseSeconds * travelled);

            if (camera != null && camera.BeginSlide(slideFrom, seconds))
            {
                while (true)
                {
                    float step = DisplayStep();
                    bool running = camera.TickSlide(step);

                    // 逆向き：行きの進行度を 1→0 へたどり直す。
                    display?.SetProgress(travelled * (1f - camera.SlideEased));
                    display?.TickDisplayClock(step);

                    if (!running)
                    {
                        break;
                    }

                    yield return null;
                }

                // <b>留まらずに追従へ戻す。</b> 戻したあとに結び直しは起きない
                // （活動 Area は出発側のまま）ので、留まりを解く者が居ない。
                camera.EndSlideAndResumeFollow();
                ReversedCount++;
            }

            yield return Rollback(transitionId, departure, departureHandle, reason, destination);
        }

        /// <summary>
        /// <b>表示に使ってよい 1 フレームぶんの秒数</b>（§7.1。工程 P55-10f）。
        ///
        /// 2 つのことを同時に守る。
        /// <list type="bullet">
        /// <item><description><b>非フォーカス中は進めない</b>——0 を返す。
        /// スライドはその場で止まり、戻ってきたところから続く。</description></item>
        /// <item><description><b>復帰時に巨大 delta で飛ばさない</b>——
        /// <see cref="MaxDisplayStepSeconds"/> で頭を押さえる。
        /// 非フォーカスから戻った 1 フレームの <c>unscaledDeltaTime</c> は<b>止まっていた時間そのもの</b>
        /// になりうるので、そのまま渡すとスライドが一瞬で終わる。</description></item>
        /// </list>
        ///
        /// <b>ロード監視には使わない。</b> §7.1 は「ロード監視は別の unscaled／実時間で継続する」と
        /// 定める——止めると、非フォーカスのあいだに終端したロードを誰も引き取らず、
        /// 復帰した瞬間に時間切れが確定する（<b>触っていないのに失敗する</b>）。
        /// 監視の側は <c>Time.unscaledDeltaTime</c> をそのまま使い続ける。
        /// </summary>
        private static float DisplayStep() =>
            ResolveDisplayStep(Time.unscaledDeltaTime, AppFocusProvider.IsFocused);

        /// <summary>
        /// 同じ規則を<b>値だけで</b>言い直したもの（受入用。§7.1。工程 P55-10f）。
        ///
        /// <b>巨大 delta は実 Scene では作れない。</b> 背面に回しても Unity はフレームを回し続けるので、
        /// 復帰した 1 フレームの <c>unscaledDeltaTime</c> は普通のフレーム時間のままになる——
        /// 実機で起きる「止まっていた時間がそのまま届く」を、実スライドの検査では再現できない。
        /// 規則そのものはここで、<b>値を与えて</b>決定的に見る。
        /// </summary>
        public static float ResolveDisplayStep(float unscaledDeltaTime, bool focused) =>
            focused ? Mathf.Min(unscaledDeltaTime, MaxDisplayStepSeconds) : 0f;

        /// <summary>
        /// 準備待ちを 1 フレーム進め、通算が 0.3 秒を超えていれば待ち表示を出す
        /// （§5。工程 P55-10e）。
        ///
        /// <b>窓口は毎回引く。</b> 常駐は入れ替わりうるので、入口で 1 回引いて持ち回ると
        /// 入れ替わったあと誰にも届かない（記録 035 §2 と同じ形）。
        /// </summary>
        private void PumpWaitNotice()
        {
            if (_waitNotice.Tick(Time.unscaledDeltaTime))
            {
                AreaTransitionWaitNoticeProvider.Current?.Show();
            }
        }

        /// <summary>
        /// 準備待ちを終える（§5「スライド開始・Rollback・終端失敗で消す」）。
        /// 何度呼んでも安全なので、早期 return の経路からも遠慮なく呼べる。
        /// </summary>
        private void EndWaitNotice()
        {
            _waitNotice.End();
            AreaTransitionWaitNoticeProvider.Current?.Hide();
        }

        /// <summary>
        /// 旧 Area を<b>撤去せずに預ける</b>（§6.2 手順 11。裁定 2。工程 P55-10c）。
        ///
        /// 預け先は<b>先読み管理</b>である（<see cref="AreaPreloader.TryAdoptRetained"/>）。
        /// 別に「保持リスト」を作らないのは、保持に必要な契約——即時の逆移動で読み直さない、
        /// 別候補へは解放してから切り替える、Single 読込の後始末に乗る、在留上限を数える——が
        /// <b>すべて先読み側に既にある</b>ためである。
        ///
        /// <b>預けられないときは黙って進まない。</b> 理由を残したうえで従来どおり撤去する——
        /// 保持は最適化であって、成功確定を取り消す理由にはならない（§8 の 4 行目）。
        /// </summary>
        private bool TryRetainDeparture(
            AreaPreloader preloader, AreaInstanceHandle handle, int sceneHandle,
            AreaRuntimeBundle departure)
        {
            RetainAttemptCount++;

            if (!_owner.RetainDepartedArea)
            {
                return DeclineRetain("保持が無効になっています（撤去経路そのものの受入用）。");
            }

            if (preloader == null || !handle.IsValid || sceneHandle == 0 || departure == null)
            {
                return DeclineRetain("旧 Area の実体・Scene・参照集合のどれかが欠けています。");
            }

            IAreaSceneHost host = _owner.SlideSceneHost;
            if (host == null || !host.IsLoaded(sceneHandle))
            {
                // すでに載っていない（Single 起動の直後など）。預かる物が無い。
                return DeclineRetain("旧 Area の Scene がもう載っていません。");
            }

            AreaCatalog catalog = _owner.Catalog;
            if (catalog == null || !catalog.TryGetScenePath(departure.AreaId, out string scenePath))
            {
                // 読み直しのパスが引けないと、預かった先に「望み」を立てられない。
                return DeclineRetain("旧 Area の Scene パスを引けません（area=" + departure.AreaId.Value + "）。");
            }

            if (!preloader.TryAdoptRetained(handle, sceneHandle, scenePath))
            {
                return DeclineRetain("先読みが預かりを断りました（" + preloader.Phase + "）。");
            }

            RetainedCount++;
            LastRetainDecline = string.Empty;
            return true;
        }

        private bool DeclineRetain(string reason)
        {
            RetainDeclinedCount++;
            LastRetainDecline = reason;
            GameLog.Info(LogCategory.Scene, "Retaining the departed area was declined: " + reason);
            return false;
        }

        /// <summary>
        /// 旧 Area を撤去する（手順 11）。
        ///
        /// <b>失敗しても到着の成功を取り消さない</b>（§8 の 4 行目）。旧 Area は
        /// 非活動・非物理・購読解除済みで隔離されたままにし、台帳の在留枠も返さない——
        /// 返すと「空きあり」と誤認して 3 枚目を読む。
        /// </summary>
        private IEnumerator UnloadDeparture(AreaInstanceHandle handle, int sceneHandle)
        {
            IAreaSceneHost host = _owner.SlideSceneHost;
            if (sceneHandle == 0 || host == null || !host.IsLoaded(sceneHandle))
            {
                // すでに載っていない（Single 起動の直後など）。枠だけ返す。
                _residency.Remove(handle);
                yield break;
            }

            if (!_residency.TryBeginSceneOperation())
            {
                UnloadFailureCount++;
                LastFailure = "他の Scene 操作が走っているため旧 Area を撤去できませんでした。";
                yield break;
            }

            IAreaSceneOperation operation = host.Unload(sceneHandle);
            while (operation != null && !operation.IsDone)
            {
                yield return null;
            }

            _residency.EndSceneOperation();

            // <b>撤去できたと決めつけない。</b> 実際に消えたかを確かめる
            // （先読みの撤去と同じ規律。GPT レビュー R10 の指摘 2）。
            if (operation == null || host.IsLoaded(sceneHandle))
            {
                // <b>到着の成功は取り消さない</b>（§8 の 4 行目）。旧 Area は非活動・非物理・
                // 購読解除済み（受理時に活動ゲートを閉じてある）のまま隔離しておく。
                // <b>在留枠も返さない</b>——返すと「空きあり」と誤認して 3 枚目を読む。
                UnloadFailureCount++;
                _retireHandle = handle;
                _retireSceneHandle = sceneHandle;
                LastFailure = "旧 Area の撤去が失敗し、まだ載っています（到着は成立している）。"
                    + "新しいスライドは受け付けません。撤去を再試行してください。";
                GameLog.Warning(LogCategory.Scene, "Failed to unload the departed area: " + LastFailure);
                yield break;
            }

            _retireHandle = AreaInstanceHandle.None;
            _retireSceneHandle = 0;
            _residency.Remove(handle);
        }

        /// <summary>
        /// 出発側へ戻す（§8 の 2 行目「B を隔離・解放し、旧 Actor と Camera を出発位置で復帰」）。
        ///
        /// <b>Rollback は HP や徳を巻き戻す機能ではない</b>（§8 末尾）。受理後は活動を止めているので、
        /// 中断後 Snapshot と同じ値のまま旧側へ戻るだけである。探索は自動再開しない。
        /// </summary>
        private IEnumerator Rollback(
            int transitionId, AreaRuntimeBundle departure, AreaInstanceHandle departureHandle,
            string reason, AreaRuntimeBundle destination = null, bool waitForPreloadRelease = true)
        {
            LastFailure = reason;
            GameLog.Warning(LogCategory.Scene, "Slide transition rolled back: " + reason);

            // <b>失敗でも消す</b>（§5）。出したままにすると、出発側へ戻って遊べているのに
            // 「読み込み中」が residual で残る。
            EndWaitNotice();

            // <b>抑止はもう要らない</b>（工程 P55-15c。読み込み方針の裁定）。
            //
            // 抑止は「距離による先読みが、プレイヤーの再操作なしに同じ先へ向き直す」のを
            // 止めるための仕掛けだった。距離で読むのをやめたので、
            // <b>読む相手を決めるのはプレイヤーの操作だけ</b>になり、
            // 「勝手に向き直る」経路そのものが無くなった。

            if (!_slide.TryBeginRollback(transitionId))
            {
                yield break;
            }

            // 到着側を隔離・解放する。<b>放棄ではなく消す</b>——旧 Scene は生きているので、
            // 遅れて着いた Scene が自分で活動を始める心配が無い（消さないと出発側の
            // 次の遷移が「別 Area 宛ての要求」に引っかかる）。
            AreaPendingArrival.Clear();

            // 分かっているなら<b>隔離してから</b>解放する（§8 の 2 行目「B を隔離・解放し」）。
            // 開いたまま撤去すると、消えるまでの間だけ B の登録が生きている。
            destination?.ActivityGate?.Close();

            AreaPreloader preloader = EnsurePreloader();
            preloader.ClearRequest();

            // <b>タイムアウトのときは待たない。</b> 待つ相手は「止められないロード」で、
            // ここで待てば旧 Area への復帰がその分遅れる（§8「旧 Area への復帰を優先」）。
            // 撤去は終端後に <see cref="Pump"/> が進める。
            if (waitForPreloadRelease)
            {
                float waited = 0f;
                while (preloader.Phase == AreaPreloadPhase.Releasing
                       && waited < _owner.TimeoutSeconds)
                {
                    preloader.Poll();
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            preloader.Poll();

            // 代理を畳んで実 Actor の表示を戻し、出発側の活動を戻す。
            AreaTransitionDisplayProvider.Current?.Release();

            // <b>走っているときだけ打ち切る。</b> 走っていない演出を Cancel すると
            // 前回のスライドの始点が適用され、カメラが無関係な場所へ跳ぶ。
            IAreaCameraOwner camera = AreaCameraOwnerProvider.Current;
            if (camera != null && camera.IsSliding)
            {
                camera.CancelSlide();
            }

            if (departure != null)
            {
                departure.ActivityGate?.Open();
                departure.Context?.ReopenAfterFailedTransition();

                // <b>受付条件の窓口を出発側へ戻す。</b> 到着側の初期化担当が Prepared の時点で
                // 自分の窓口を差しているので、戻さないと<b>撤去済みの部品</b>を指したまま残り、
                // 以後どの遷移も断られる（実際に踏んだ：Rollback のあと Fade も受理されなかった）。
                _owner.RebindConditions(departure.Conditions);
            }

            if (departureHandle.IsValid)
            {
                _residency.TrySetPhase(departureHandle, AreaActivationPhase.Active);
            }

            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);

            // 運んでいた値は捨てる。<b>Actor はどこへも動いていない</b>ので、
            // 生きている実 Actor が持っている値がそのまま正本である。
            _owner.ClearPendingTransfer();
            _owner.Clock.Thaw();

            if (_slide.NotifyRolledBack(transitionId))
            {
                RolledBackCount++;
            }
            else
            {
                FailedCount++;
            }
        }

        /// <summary>
        /// 出発側の参照集合を引く。常駐の指定があればそれ、無ければ Area がちょうど 1 つのときだけ。
        /// <b>引けたら常駐の指定にする</b>——2 枚目が載ると「ちょうど 1 つ」の互換経路が使えなくなる。
        /// </summary>
        private bool TryResolveDeparture(out AreaRuntimeBundle bundle)
        {
            bundle = CurrentAreaProvider.Current;
            if (bundle == null && !AreaBundleDirectory.TryGetSingle(out bundle))
            {
                return false;
            }

            if (bundle == null || bundle.Context == null)
            {
                return false;
            }

            CurrentAreaProvider.TrySetCurrent(_owner, bundle);
            return true;
        }

        /// <summary>
        /// 出発 Area を在留台帳に載せる（載っていなければ）。
        ///
        /// Single 起動・直開きで載った Area は台帳を通っていないので、実体ハンドルを持たない。
        /// <b>ここで配る。</b> 持たせないと、到着側の終点計算（実体ハンドルで引く）も
        /// 撤去（どの枠を返すか）も成立しない。
        /// </summary>
        private AreaInstanceHandle EnsureResidency(AreaRuntimeBundle bundle)
        {
            if (bundle == null || !bundle.AreaId.IsValid)
            {
                return AreaInstanceHandle.None;
            }

            AreaInstanceHandle existing = bundle.Instance;
            if (existing.IsValid && _residency.PhaseOf(existing) != AreaActivationPhase.Unloaded)
            {
                return existing;
            }

            AreaInstanceHandle handle = _residency.NextHandle(bundle.AreaId);
            if (!_residency.TryAdmitStaged(handle))
            {
                return AreaInstanceHandle.None;
            }

            _residency.TrySetPhase(handle, AreaActivationPhase.Active);
            bundle.BindInstance(handle);
            return handle;
        }

        /// <summary>
        /// <b>Single 読込を出す前に先読みを捨てる</b>（§5 末尾／§8 末尾。工程 P55-04e）。
        ///
        /// §5 は「先読み中の死亡・暗転扉は先読みを不要扱いにし、<b>終端して隔離 Area を
        /// unload してから</b>ロードを発行する」と定めている。<c>LoadSceneMode.Single</c> は
        /// 載っている Scene を全部置き換えるので、先読みした Area は黙って消える——
        /// 実害は「台帳と実 Scene がずれる」ことと、止められないロードが
        /// <b>新しい世界の上へ遅れて Scene を足す</b>ことである。
        ///
        /// <b>何も載っていなければ 1 フレームも使わない。</b> P5 の構成は先読みを持たないので、
        /// ここが従来経路に待ちを足してはいけない（正常系に固定の待ちを足さない。§6.3 末尾）。
        /// </summary>
        public IEnumerator DiscardStagedForSingleLoad()
        {
            // <b>毎回ここから言い直す。</b> この値は「<b>この呼び出し</b>が所有を解けたか」であって、
            // 前回の結果ではない。前回 false のまま早期 return すると、
            // 先読みがとっくに空になっていても<b>呼び出し元が永久に進めない</b>——
            // 実際に踏んだ（再試行が一度も通らなかった）。
            StagedDiscardCompleted = true;

            // <b>ここから発行権を押さえる</b>（工程 P55-07c。GPT 再修正①）。
            //
            // 所有を解くのに数フレームかかることがある。そのあいだ表示側の「撤去をやり直す」を
            // 押せてしまうと、<b>解き終えた直後に新しい Unload が走っている</b>状態で
            // Single を撃つことになる。手放すのは、読込を発行したときと、諦めたときだけ。
            _singleLoadClaimed = true;

            // ---- 1. 撤去（通常・再試行）が走っていれば、その終端を待つ（GPT 追加②・再修正①）----
            //
            // <b>Fade も待つ。</b> 「Single は全部を置き換えるから待たせない」と書いていたが、
            // それは<b>操作の非重複という別の契約</b>を無視していた。撤去の操作が走っている
            // 最中に Single を撃てば、終端していない操作の上へ新しい操作を重ねることになる。
            if (IsRetireInFlight)
            {
                float retireWaited = 0f;
                while (IsRetireInFlight && retireWaited < _owner.TimeoutSeconds)
                {
                    retireWaited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (IsRetireInFlight)
                {
                    StagedDiscardBlockedCount++;
                    StagedDiscardCompleted = false;
                    _singleLoadClaimed = false;
                    LastFailure = "旧 Area の撤去が終わらないため、Single 読込を発行しませんでした（再試行中="
                        + _retrying + "）。";
                    GameLog.Warning(LogCategory.Scene, LastFailure);
                    yield break;
                }
            }

            if (_preloader == null)
            {
                yield break;
            }

            // ---- 2. 先読みの所有を解く ----
            //
            // <b>「失敗した」と「まだ掴んでいる」は別物である</b>（GPT 追加③）。
            // <c>Failed</c> は操作も Scene も手放したあとの<b>履歴</b>で、
            // <c>ClearRequest</c>／<c>Poll</c> では Idle へ戻らない。
            // これを未終端と同じに扱うと、<b>一度先読みに失敗しただけで
            // 死亡再開も扉移動も毎回タイムアウトする</b>。
            bool owns = _preloader.HasLiveSceneOperation
                        || _preloader.HoldsStagedScene
                        || _preloader.DesiredArea.IsValid;
            if (!owns)
            {
                yield break;
            }

            DiscardedForSingleLoadCount++;
            _preloader.ClearRequest();

            float waited = 0f;
            while (waited < _owner.TimeoutSeconds)
            {
                // 撤去失敗を抱えているなら、やり直しの許可を出しておく（§5 の 1 回だけ再試行）。
                if (_preloader.Phase == AreaPreloadPhase.ReleaseFailed)
                {
                    _preloader.ArmRetry();
                }

                _preloader.Poll();
                if (!_preloader.HasLiveSceneOperation)
                {
                    break;
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_preloader.HasLiveSceneOperation)
            {
                // <b>時間切れは「次のロードを始めてよい」ではない</b>（GPT 受入①）。
                //
                // 以前はここで警告だけ残して抜けていた。呼び出し元はそのまま Single 読込を発行し、
                // <b>Additive の先読みが走ったまま</b>次の読込が重なる。Single 側の監視は
                // 先読みの操作を持っていないので、そこでは止まらない。
                //
                // <b>所有権を手放さない。</b> 監視は <c>Pump</c> が続け、終端したら枠が戻る。
                // 呼び出し元はこの回のロードを<b>終端失敗</b>にして、プレイヤーに再操作させる。
                StagedDiscardBlockedCount++;
                StagedDiscardCompleted = false;
                _singleLoadClaimed = false;
                LastFailure = "先読みが終端していないため、Single 読込を発行しませんでした（"
                    + _preloader.Phase + "）。";
                GameLog.Warning(LogCategory.Scene, LastFailure);
                yield break;
            }

            // 操作は終端した。Scene が載ったままなら数えておく——
            // Single 読込がそれごと置き換え、<c>NotifySingleLoadCompleted</c> が台帳を合わせ直す。
            if (HoldsRemainingScene)
            {
                SingleLoadOverRemainingSceneCount++;
                GameLog.Warning(LogCategory.Scene,
                    "撤去し切れていない Scene を抱えたまま Single 読込へ進みます（"
                    + _preloader.Phase + "）。");
            }
        }

        /// <summary>Single 読込の前に先読みを捨てた回数（診断・テスト用）。</summary>
        public int DiscardedForSingleLoadCount { get; private set; }

        /// <summary>
        /// Single 読込が終わったことを知らせる（工程 P55-04e）。
        ///
        /// 載っていた Scene は全部置き換わったので、台帳と先読みの預かりをその場で合わせ直す。
        /// 受理のたびの合わせ直し（<see cref="SyncResidencyToLoadedAreas"/>）でも回復するが、
        /// <b>ずれている時間を残さない</b>ほうが、途中で数を見る検査が素直になる。
        /// </summary>
        public void NotifySingleLoadCompleted()
        {
            SyncResidencyToLoadedAreas();
        }

        /// <summary>
        /// 在留台帳を<b>実際に載っている Area</b>へ合わせ直す（§8 末尾「Fade と Slide は同じ
        /// Scene 操作管理を共有する」）。
        ///
        /// <b>Single 読込は台帳を通らない。</b> Fade の遷移・死亡再開・Launcher への退避は
        /// <c>LoadSceneMode.Single</c> で、載っている Scene を全部置き換える——台帳は
        /// それを知らないので、在留数が実際より多いまま残る。そのまま次のスライドへ入ると
        /// 上限 2 に達していると誤認し、<b>先読みが AtCapacity で断られてスライドできない</b>。
        ///
        /// 判定は<b>参照集合の生死</b>で行う。Area Scene が消えれば <c>AreaRuntimeBundle</c> の
        /// <c>OnDisable</c> が索引から外れるので、「索引に居ない実体はもう載っていない」と言える。
        ///
        /// <b>Scene 操作が走っている間は触らない。</b> 読込中の実体はまだ束を持っていないので、
        /// ここで落とすと在留枠の管理が二重になる。
        /// </summary>
        private void SyncResidencyToLoadedAreas()
        {
            if (_residency.IsSceneOperationInFlight || _slide.IsTransitioning)
            {
                return;
            }

            System.Collections.Generic.List<AreaInstanceHandle> gone = null;
            foreach (System.Collections.Generic.KeyValuePair<AreaInstanceHandle, AreaActivationPhase> kv
                     in _residency.Residents)
            {
                if (AreaBundleDirectory.TryGetByInstance(kv.Key, out AreaRuntimeBundle bundle)
                    && bundle != null)
                {
                    continue;
                }

                (gone ??= new System.Collections.Generic.List<AreaInstanceHandle>()).Add(kv.Key);
            }

            if (gone != null)
            {
                for (int i = 0; i < gone.Count; i++)
                {
                    _residency.Remove(gone[i]);
                    ForgottenResidentCount++;
                }
            }

            _preloader?.DropStagedIfUnloaded();

            // 抱えていた「撤去し切れなかった旧 Area」も、Single 読込で消えていれば解放する。
            IAreaSceneHost host = _owner.SlideSceneHost;
            if (HasPendingRetire && (host == null || !host.IsLoaded(_retireSceneHandle)))
            {
                _residency.Remove(_retireHandle);
                _retireHandle = AreaInstanceHandle.None;
                _retireSceneHandle = 0;
            }
        }

        /// <summary>
        /// 撤去し切れなかった旧 Area の撤去を<b>もう一度だけ</b>試す（§8 の 4 行目「再試行手段」）。
        ///
        /// <b>自動で再試行しない。</b> 毎フレーム撤去を撃ち続けると、恒久的に失敗する Scene へ
        /// 延々と操作を出す（先読みの再試行と同じ考え方。§5）。押すのはプレイヤー（表示側）。
        ///
        /// <b>保持した Area の解放失敗もここで扱う</b>（工程 P55-10c）。裁定 2 で旧 Area は
        /// 撤去せず先読み枠へ預けるようになったので、「撤去し切れていない Scene」は
        /// <b>2 か所に出うる</b>——預けられずに撤去して失敗した場合（<see cref="HasPendingRetire"/>）と、
        /// 預けた先が解放に失敗した場合（<c>AreaPreloadPhase.ReleaseFailed</c>）。
        /// プレイヤーから見れば同じ「片付かない」なので、押す場所を 2 つにしない。
        /// </summary>
        /// <returns>再試行を<b>始めた</b>ら true（成否は <see cref="HasUnreleasedScene"/> で見る）。</returns>
        public bool TryRetryRetiringDeparture()
        {
            if (_slide.IsTransitioning || _retrying)
            {
                return false;
            }

            if (!HasPendingRetire)
            {
                // 預けた Area の解放が失敗しているなら、そちらへ 1 回分の許可を出す。
                // 進めるのは常駐の <see cref="Pump"/>（先読みと同じ規律。§5）。
                if (_preloader == null || _preloader.Phase != AreaPreloadPhase.ReleaseFailed)
                {
                    return false;
                }

                if (_owner.IsSingleLoadInFlight)
                {
                    RetryBlockedCount++;
                    return false;
                }

                RetryStartedCount++;
                return _preloader.ArmRetry();
            }

            // <b>Single 遷移が始まっていたら割り込まない</b>（工程 P55-07c。GPT 再修正①）。
            //
            // 排他は両方向で取る。撤去側だけが「Single を待つ」形にしても、
            // Single が所有を解いているあいだに撤去を<b>始めて</b>しまえば同じ重なりになる。
            if (_owner.IsSingleLoadInFlight)
            {
                RetryBlockedCount++;
                return false;
            }

            _retrying = true;
            RetryStartedCount++;
            _owner.StartSlideRoutine(RetryRoutine(_retireHandle, _retireSceneHandle));
            return true;
        }

        private IEnumerator RetryRoutine(AreaInstanceHandle handle, int sceneHandle)
        {
            yield return UnloadDeparture(handle, sceneHandle);
            _retrying = false;
        }

        /// <summary>
        /// 遷移が走っていない間の後始末を 1 フレーム進める（常駐の <c>Update</c> から呼ばれる）。
        ///
        /// やることは 2 つ。
        /// <list type="number">
        /// <item><description><b>台帳を実 Scene へ合わせ直す。</b> Scene は常駐の外からも
        /// 置き換わる（New Game・Launcher 退避・試遊の切り替え）。毎フレーム合わせておけば、
        /// 「誰が壊したか」を数え上げずに済む（§8 末尾）。</description></item>
        /// <item><description><b>先読みを進める。</b> 止められないロードの終端はここでしか
        /// 観測できない（§8 の 5 行目）。監視を諦めた遷移は望みを取り下げて戻るだけで、
        /// 実際の撤去は「操作が終端してから」しかできない。誰も進めないと、
        /// 遅れて着いた Scene が<b>閉じたまま載り続ける</b>（在留枠も埋まったまま）。</description></item>
        /// </list>
        ///
        /// <b>遷移中は触らない。</b> 走っている遷移が自分の段で Poll しているので、
        /// 二重に進めると「読み終わった直後に撤去が始まる」順序が作れてしまう。
        /// </summary>
        public void Pump()
        {
            if (_slide.IsTransitioning)
            {
                return;
            }

            // <b>出したまま忘れない。</b> 準備待ちの表示は遷移の中で消すのが本筋だが、
            // 準備区間には早期 return の経路が十数本ある（世代が進んだ・常駐が居ない等）。
            // 遷移が走っていないのに出ているなら、それは消し忘れである（§5。工程 P55-10e）。
            if (_waitNotice.ShouldShow)
            {
                EndWaitNotice();
            }

            SyncResidencyToLoadedAreas();
            PumpResidency();
            _preloader?.Poll();
        }


        /// <summary>
        /// この常駐が読込を頼んだ回数（診断・テスト用）。
        ///
        /// <b>頼むのは遷移の受理だけになった</b>（工程 P55-15c。読み込み方針の裁定）。
        /// 歩いているだけでこれが増えるなら、距離で読む経路がどこかに残っている。
        /// </summary>
        public int PreloadRequestCount { get; private set; }

        /// <summary>待ち表示の時計（診断・テスト用。§5。工程 P55-09a）。</summary>
        public AreaTransitionWaitNoticeTimer WaitNotice => _waitNotice;

        /// <summary>
        /// <b>活動中 Area を在留台帳へ載せ続ける</b>（工程 P55-15c）。
        ///
        /// <b>ここは以前「距離で先読みを始める」場所だった。</b>
        /// 読み込み方針の裁定（オーナー提案）で、
        /// <b>接近ではロードせず、初めてそのエリアへ遷移するときにロードする</b>ことになった——
        /// 待ち時間を<b>プレイヤーが理解できる場所（エリア移動）へまとめる</b>ためである。
        /// 距離を広げる案は採らなかった：歩行中の読み込み負荷は残るし、
        /// 距離だけでは「境界が見える前に読み終わる」保証にもならない。
        ///
        /// <b>残したのは台帳の登録だけ。</b> 直開き（試遊の起動・テストの Single 読込）で載った Area は
        /// 台帳を通っていないので実体ハンドルを持たない。持たせないと、
        /// 到着側の終点計算も撤去（どの枠を返すか）も成立しない（工程 P55-08c）。
        /// <b>ロードはここからは一切起こさない。</b>
        /// </summary>
        private void PumpResidency()
        {
            // <b>活動中 Area は遷移と同じ手順で引く。</b>
            // <c>CurrentAreaProvider.Current</c> だけを見ると、直開きの Area が
            // 現行として指定されないまま残る。
            if (!TryResolveDeparture(out AreaRuntimeBundle active) || active.Root == null)
            {
                return;
            }

            // 撤去・再試行・Single の準備中は台帳へ触らない（付録 C.22.1）。
            if (IsRetireInFlight || HasPendingRetire || _singleLoadClaimed || _owner.IsSingleLoadInFlight)
            {
                return;
            }

            EnsureResidency(active);
        }

        private AreaPreloader EnsurePreloader()
        {
            // <b>実装は握り込まない。</b> 差し替えはいつ来るか分からないので、そのとき引く
            // （工程 P55-08c）。
            return _preloader ??= new AreaPreloader(
                _residency, () => _owner.SlideSceneHost, _owner);
        }

        /// <summary>直前に代理へ渡した主人公の終点（診断・テスト用）。</summary>
        public Vector3 LastPlayerRouteTo { get; private set; }

        /// <summary>直前に代理へ渡した犬丸の終点（診断・テスト用）。</summary>
        public Vector3 LastCompanionRouteTo { get; private set; }

        /// <summary>
        /// 犬丸の出発位置と<b>準備済みの到着位置</b>を測る（GPT 受入④）。
        ///
        /// <b>Away は運ばない。</b> 退場中は描かれていないので代理も立っておらず、
        /// 経路を作る相手が居ない。
        /// </summary>
        private static bool TryResolveCompanionPositions(
            AreaRuntimeBundle departure, AreaRuntimeBundle destination,
            out Vector3 from, out Vector3 to)
        {
            from = Vector3.zero;
            to = Vector3.zero;

            if (departure == null || destination == null
                || !departure.TryResolve(out Momotaro.Gameplay.Companion.CompanionActor leaving)
                || leaving == null || leaving.IsAway
                || !destination.TryResolve(out Momotaro.Gameplay.Companion.CompanionActor arriving)
                || arriving == null)
            {
                return false;
            }

            from = leaving.transform.position;
            to = arriving.transform.position;
            return true;
        }

        /// <summary>
        /// 表示経路が壁を横切らないか（§7.2 末尾「表示経路を安全に作れない接続は準備失敗」）。
        ///
        /// 索敵・Interact と<b>同じ判定</b>を使う（半径 0.25m の球）。別の物差しを作ると、
        /// 「通れるのに表示は通さない」「表示は通すのに通れない」がずれて出る。
        /// </summary>
        private static bool IsDisplayRouteClear(Vector3 from, Vector3 to) =>
            DisplayRouteProbe.IsClear(from, to);

        private static bool IsDisplayRouteClear(
            Vector3 from, Vector3 to, List<Collider> ignored) =>
            DisplayRouteProbe.IsClear(from, to, ignored);

        /// <summary>直近の表示経路検査で障害物から外した境界壁の数（診断・テスト用。工程 P55-14b）。</summary>
        public int SeamBarriersIgnoredForRoute { get; private set; }

        /// <summary>
        /// <b>その接続の境界壁</b>を両側から集める（工程 P55-14b）。
        ///
        /// 往復は同じ口を使うので、<b>順方向と逆方向のどちらの ID でも一致</b>させる
        /// （<see cref="AreaSeamBarrier.Covers"/>）。
        /// 出発側と到着側の両方を見るのは、表示経路が<b>両 Area をまたぐ</b>ためである。
        /// </summary>
        private List<Collider> CollectSeamBarriers(
            AreaRuntimeBundle departure, AreaRuntimeBundle destination,
            StableId connectionId, StableId reverseConnectionId)
        {
            var found = new List<Collider>();
            AddSeamBarriers(found, departure, connectionId, reverseConnectionId);
            AddSeamBarriers(found, destination, connectionId, reverseConnectionId);
            SeamBarriersIgnoredForRoute = found.Count;
            return found;
        }

        private static void AddSeamBarriers(
            List<Collider> into, AreaRuntimeBundle bundle,
            StableId connectionId, StableId reverseConnectionId)
        {
            if (bundle == null || !bundle.TryResolve(out AreaRoot root) || root == null)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<AreaSeamBarrier> barriers = root.SeamBarriers;
            for (int i = 0; i < barriers.Count; i++)
            {
                AreaSeamBarrier barrier = barriers[i];
                if (barrier == null || barrier.Blocker == null)
                {
                    continue;
                }

                if (barrier.Covers(connectionId) || barrier.Covers(reverseConnectionId))
                {
                    into.Add(barrier.Blocker);
                }
            }
        }

        private static readonly Momotaro.Gameplay.Companion.Investigation.PhysicsObstacleProbe
            DisplayRouteProbe = new Momotaro.Gameplay.Companion.Investigation.PhysicsObstacleProbe();

        /// <summary>出発側の主人公の足元位置（居なければ原点）。代理の出発位置になる。</summary>
        private static Vector3 ResolvePlayerPosition(AreaRuntimeBundle bundle)
        {
            return bundle != null
                   && bundle.TryResolve(out Momotaro.Gameplay.Player.PlayerRoot player)
                   && player != null
                ? player.transform.position
                : Vector3.zero;
        }

        /// <summary>接続の所要秒（0 なら既定へ落とす）。</summary>
        private static float ResolveSeconds(in AreaConnectionSnapshot connection) =>
            connection.SlideDuration > 0f ? connection.SlideDuration : FallbackSlideSeconds;

        /// <summary>
        /// スライドが<b>接続の帯の内側</b>に収まっているか（§7.1 改定。工程 P55-14d）。
        ///
        /// <b>「接続軸だけを動く」から「接続方向へ動き、軸外は通路の幅の内側」へ変わった。</b>
        /// 同一 Area 内でカメラが主人公へ連続追従するようになったので、
        /// 通路の端から出口へ入ると<b>スライドの始点は接続軸から外れる</b>——
        /// そこを不合格にすると、通路の端を歩いてきたプレイヤーは隣の Area へ行けない。
        ///
        /// <b>許容値を大きくしただけにしない</b>（GPT 裁定 (a) の注意）。見るのは 4 つで、
        /// どれも<b>配置ミスを通さない</b>ために要る：
        ///
        /// <list type="number">
        /// <item><b>到着側は接続軸に乗っている</b>（誤差許容 <c>AxisEpsilon</c> のみ）。
        /// 入口が通路の中心からずれている配置は、ここで落ちる。</item>
        /// <item><b>出発側は帯の内側</b>（通路の半幅＋誤差許容）。帯は<b>接続 Data の通路幅</b>
        /// から求める——広い通路は広く、狭い通路は狭い。</item>
        /// <item><b>補間経路も帯の内側</b>。始点・終点だけでは、経路が直線でなくなった日に気付けない。</item>
        /// <item><b>接続方向へ動いている</b>（<c>MinAlongMovement</c> 以上）。
        /// 帯の中で横へ動くだけの配置は、帯の検査だけでは捕まらない。</item>
        /// </list>
        ///
        /// <b>帯の定義は <see cref="AreaConnectionRules"/> が正本</b>で、
        /// Scene 検査（<c>Phase55WorldValidator</c>）も同じ関数を使う。
        /// </summary>
        /// <param name="connection">受理時に固定した接続。</param>
        /// <param name="arrivalPosition">到着入口の位置。<b>接続軸の座標をここから採る</b>。</param>
        /// <param name="from">スライドの始点（受理時の実 Rig 位置）。</param>
        /// <param name="to">スライドの終点（到着側の通常追従位置）。</param>
        /// <param name="failure">不合格のときの理由（値を全部埋める）。</param>
        private static bool TryCheckSlideBand(
            in AreaConnectionSnapshot connection, Vector3 arrivalPosition,
            Vector3 from, Vector3 to, out string failure)
        {
            failure = null;
            if (connection.Axis == AreaConnectionRules.Axis.None)
            {
                failure = "接続の向きが未指定なので、スライドの帯を決められません（§3.1）。";
                return false;
            }

            bool alongX = connection.Axis == AreaConnectionRules.Axis.X;
            float axis = Across(arrivalPosition, alongX);
            float band = connection.SlideAcrossHalfWidth;
            string acrossName = alongX ? "z" : "x";

            // (1) 到着側は接続軸に乗っている（誤差許容だけ。ここは緩めない）。
            float arrivalOffset = Across(to, alongX) - axis;
            if (Mathf.Abs(arrivalOffset) > AreaConnectionRules.AxisEpsilon)
            {
                failure = "到着側のカメラが接続軸に乗っていません（" + acrossName + "="
                          + Across(to, alongX) + " 軸 " + axis + " ずれ " + arrivalOffset
                          + "）。入口の配置がずれています（§7.1）。";
                return false;
            }

            // (2)(3) 始点・終点・その間が帯の内側。
            for (int i = 0; i <= BandSamples; i++)
            {
                Vector3 at = Vector3.Lerp(from, to, i / (float)BandSamples);
                float offset = Across(at, alongX) - axis;
                if (!AreaConnectionRules.IsWithinSlideBand(offset, connection.CorridorWidth))
                {
                    failure = "スライドの経路が接続の帯から外れます（" + i + "/" + BandSamples
                              + " で " + acrossName + "=" + Across(at, alongX) + " 軸 " + axis
                              + " ずれ " + offset + "／"
                              + AreaConnectionRules.DescribeSlideBand(connection.CorridorWidth)
                              + "）。from=" + from + " to=" + to + "（§7.1）。";
                    return false;
                }
            }

            // (4) 接続方向へ動いている。
            float alongDelta = Along(to, alongX) - Along(from, alongX);
            if (!AreaConnectionRules.MovesAlongConnection(alongDelta))
            {
                failure = "スライドが接続方向へ動きません（" + (alongX ? "x" : "z") + " の移動 "
                          + alongDelta + "／最低 " + AreaConnectionRules.MinAlongMovement
                          + "）。スライドする意味が無い配置です（§7.1）。";
                return false;
            }

            return true;
        }

        /// <summary>接続方向の成分。</summary>
        private static float Along(Vector3 v, bool alongX) => alongX ? v.x : v.z;

        /// <summary>接続軸と直交する成分。</summary>
        private static float Across(Vector3 v, bool alongX) => alongX ? v.z : v.x;

        /// <summary>
        /// 到着 Scene を active にする（§6.2 手順 8「Scene active 指定」）。
        ///
        /// 指定しないまま旧 Scene を撤去すると、active Scene が消える瞬間に
        /// 新しく作られる物体の行き先が決まらない。
        /// </summary>
        private static void TrySetActiveScene(int sceneHandle)
        {
            if (sceneHandle == 0)
            {
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.handle == sceneHandle && scene.isLoaded)
                {
                    SceneManager.SetActiveScene(scene);
                    return;
                }
            }
        }
    }
}
