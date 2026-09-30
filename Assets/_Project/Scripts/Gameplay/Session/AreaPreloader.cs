using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Session
{
    /// <summary>先読みの段階（P5.5 §4.1／§5／§11 の E04）。</summary>
    public enum AreaPreloadPhase
    {
        /// <summary>何も先読みしていない。</summary>
        Idle = 0,

        /// <summary>読込中。</summary>
        Loading = 1,

        /// <summary>読み終わり、閉じたまま待っている（§4.1 の Staged）。</summary>
        Staged = 2,

        /// <summary>撤去中。</summary>
        Releasing = 3,

        /// <summary>
        /// 読込に失敗した。<b>自動では再試行しない</b>（§5「自動で毎フレーム再試行しない。
        /// 次の新しい遷移操作で一度だけ再試行できる」）。
        /// </summary>
        Failed = 4,

        /// <summary>
        /// 撤去に失敗し、<b>実 Scene がまだ載っている</b>（§5「終端して隔離 Area を unload してから
        /// ロードを発行する」）。この間は新しいロードを出さない。
        /// </summary>
        ReleaseFailed = 5,
    }

    /// <summary>先読みを断った理由（§11 の E04）。</summary>
    public enum AreaPreloadRejection
    {
        /// <summary>断っていない。</summary>
        None = 0,

        /// <summary>ID か Scene パスが無効。</summary>
        InvalidRequest = 1,

        /// <summary>在留上限に達している（§9.1「読込済み合計は最大 2」）。</summary>
        AtCapacity = 2,

        /// <summary>読込の開始に失敗した。</summary>
        LoadStartFailed = 3,

        /// <summary>先読み要求（活動ゲートへの申し入れ）が通らなかった。</summary>
        StagingRequestRefused = 4,

        /// <summary>前の失敗を抱えているので、自動では再試行しない（§5）。</summary>
        AwaitingRetryPermission = 5,
    }

    /// <summary>
    /// 隣 Area の先読み（P5.5 §4.1／§5／§11 の E04）。
    ///
    /// <b>「望む先を宣言する」形にした。</b> <see cref="Request"/> は何度呼んでもよく、
    /// 同じ先なら読み直さず（同一先再利用）、別の先なら撤去してから読み直す（候補切替）。
    /// 「読み込め」「撤去せよ」という命令形にすると、プレイヤーが出入口の間を行き来するたびに
    /// 呼び出し側が「いま何が載っているか」を数え、撤去と読込の順序を自分で組むことになる。
    /// そこがずれると<b>二重ロード</b>か<b>撤去されない Area の積み上がり</b>になる。
    ///
    /// <b>失敗したら自分では再試行しない</b>（§5）。毎フレーム読み直すと、恒久的に失敗する先
    /// （壊れた Scene パス・容量不足）へ延々とロードを出し続ける。再試行は
    /// <see cref="ArmRetry"/> を通じて<b>外から一度だけ</b>許可する。
    ///
    /// <b>上限と直列化は持たない。</b> どちらも <see cref="AreaResidencyLedger"/> が正本で、
    /// ここは台帳に伺いを立てるだけ（§2「Scene 操作の発行は単一の管理者が直列化する」）。
    /// 両方に規則を持たせると、断られたときにどちらの規則かが実行時まで分からなくなる。
    ///
    /// UnityEngine に依存しない（Scene API は <see cref="IAreaSceneHost"/> の向こう側）ので、
    /// EditMode で決定的に検証できる。
    /// </summary>
    public sealed class AreaPreloader
    {
        private readonly AreaResidencyLedger _ledger;
        private readonly System.Func<IAreaSceneHost> _hostSource;
        private readonly object _owner;

        private StableId _desiredArea;
        private string _desiredPath;

        private IAreaSceneOperation _operation;
        private AreaInstanceHandle _loading;
        private bool _retryArmed;

        /// <summary>
        /// 作る。<paramref name="owner"/> は現行 Area の指定に使う所有者（§5.2 の所有者一致）。
        ///
        /// <b>Scene 操作の実装は「そのとき引く」</b>（工程 P55-08c）。
        ///
        /// 以前は作るときの実装を握り込んでいたので、<b>作られる時機が注入の時機を縛っていた</b>——
        /// だから「初回の要求で作る」という遅延に頼っていた。距離による先読み（§5）は
        /// 常駐が立った直後から回るので、その遅延はもう成り立たない。
        /// 毎回引く形にすれば、いつ作られても差し替えが効く。
        /// </summary>
        public AreaPreloader(
            AreaResidencyLedger ledger, System.Func<IAreaSceneHost> hostSource, object owner)
        {
            _ledger = ledger;
            _hostSource = hostSource;
            _owner = owner;
        }

        /// <summary>
        /// 作る（実装を固定する形。テストと単一 Area 構成の互換）。
        /// </summary>
        public AreaPreloader(AreaResidencyLedger ledger, IAreaSceneHost host, object owner)
            : this(ledger, () => host, owner)
        {
        }

        /// <summary>いまの Scene 操作の実装（差し替えを毎回引く）。</summary>
        private IAreaSceneHost Host => _hostSource?.Invoke();

        /// <summary>いまの段階。</summary>
        public AreaPreloadPhase Phase { get; private set; } = AreaPreloadPhase.Idle;

        /// <summary>閉じたまま待っている Area（無ければ無効ハンドル）。撤去失敗中は残したままにする。</summary>
        public AreaInstanceHandle StagedArea { get; private set; }

        /// <summary>閉じたまま待っている Scene の handle（無ければ 0）。撤去失敗中は残したままにする。</summary>
        public int StagedSceneHandle { get; private set; }

        /// <summary>いま望んでいる先（無ければ空）。</summary>
        public StableId DesiredArea => _desiredArea;

        /// <summary>失敗の理由（成功なら空）。</summary>
        public string FailureReason { get; private set; } = string.Empty;

        /// <summary>
        /// <b>終端していない Scene 操作を掴んでいる</b>か（工程 P55-07a2。GPT 追加③）。
        ///
        /// これが true の間は、ほかの Scene 操作を始めてはならない（§5「終端してから発行する」）。
        /// <b>「失敗した」という履歴とは別物である</b>——<see cref="AreaPreloadPhase.Failed"/> は
        /// 操作も Scene も手放したあとの印なので、ここは false になる。
        /// </summary>
        public bool HasLiveSceneOperation => _operation != null && !_operation.IsDone;

        /// <summary>
        /// <b>実 Scene を預かったまま</b>か（Staged／撤去失敗／読込中）。
        ///
        /// 操作は終端していても Scene が載っていることはある。
        /// <see cref="AreaPreloadPhase.Failed"/> は<b>残留物が無い</b>ので false。
        /// </summary>
        public bool HoldsStagedScene => StagedArea.IsValid || _loading.IsValid;

        /// <summary>失敗を抱えているか（<see cref="ArmRetry"/> を待っている状態）。</summary>
        public bool HasFailure =>
            Phase == AreaPreloadPhase.Failed || Phase == AreaPreloadPhase.ReleaseFailed;

        /// <summary>読込を始めた回数（診断・テスト用）。</summary>
        public int LoadStartedCount { get; private set; }

        /// <summary>読み直さずに済ませた回数（同一先再利用。診断・テスト用）。</summary>
        public int ReusedCount { get; private set; }

        /// <summary>候補を切り替えた回数（診断・テスト用）。</summary>
        public int SwitchedCount { get; private set; }

        /// <summary>撤去を始めた回数（診断・テスト用）。</summary>
        public int ReleaseStartedCount { get; private set; }

        /// <summary>断った回数（診断・テスト用）。</summary>
        public int RefusedCount { get; private set; }

        /// <summary>失敗を抱えたまま自動再試行を見送った回数（診断・テスト用）。</summary>
        public int SuppressedRetryCount { get; private set; }

        /// <summary>外から許可された再試行の回数（診断・テスト用）。</summary>
        public int ArmedRetryCount { get; private set; }

        /// <summary>最後に断った理由（診断・テスト用）。</summary>
        public AreaPreloadRejection LastRejection { get; private set; }

        /// <summary>
        /// 望む先を宣言する。<b>同じ先なら何もしない</b>（読み直さない）。
        /// 別の先なら、撤去してから読み込む（候補切替）。実際の進行は <see cref="Poll"/> が行う。
        ///
        /// <b>失敗を抱えているときに同じ先を言い直しても再試行しない</b>（§5）。
        /// 別の先を宣言した場合は新しい候補なので、読込失敗の抱え込みは解く
        /// （撤去失敗は解かない——実 Scene が残っているので先に片付ける必要がある）。
        /// </summary>
        public bool Request(StableId areaId, string scenePath)
        {
            if (!areaId.IsValid || string.IsNullOrEmpty(scenePath))
            {
                RefusedCount++;
                LastRejection = AreaPreloadRejection.InvalidRequest;
                return false;
            }

            if (_desiredArea.Equals(areaId) && _desiredPath == scenePath)
            {
                // すでにこの先を望んでいる。読込中でも Staged でも、何もしないのが正しい。
                ReusedCount++;
                Poll();
                return true;
            }

            if (_desiredArea.IsValid)
            {
                SwitchedCount++;
            }

            if (Phase == AreaPreloadPhase.Failed)
            {
                // 別の候補は「新しい先読み」なので、前の読込失敗は持ち越さない。
                Phase = AreaPreloadPhase.Idle;
                FailureReason = string.Empty;
            }

            _desiredArea = areaId;
            _desiredPath = scenePath;
            Poll();
            return true;
        }

        /// <summary>先読みをやめる（撤去まで進む）。</summary>
        public void ClearRequest()
        {
            _desiredArea = default;
            _desiredPath = null;
            Poll();
        }

        /// <summary>
        /// 閉じたまま待っている Area の<b>所有を遷移へ渡す</b>（§6.2 手順 4「対応する StagedReady を取得」）。
        ///
        /// <b>撤去しない。</b> 渡すのは「この Area の面倒を見る役」だけで、Scene も台帳の在留枠も
        /// そのまま残る。渡さないまま遷移が活動させると、先読みは<b>まだ自分の預かり物だと思っている</b>——
        /// 次に別の候補を望んだ瞬間に、いま遊んでいる Area を unload しにかかる。
        ///
        /// <b>宛先を名指しさせる。</b> 引数の Area と一致しなければ渡さない。
        /// 「いま Staged なもの」を無条件に渡す形にすると、望みが切り替わった直後の
        /// 別 Area を遷移が掴み、行き先と違う Area を活動させられる。
        /// </summary>
        /// <param name="areaId">遷移が引き取りたい Area。</param>
        /// <param name="handle">引き取った実体ハンドル（失敗時は無効）。</param>
        /// <param name="sceneHandle">引き取った Scene handle（失敗時は 0）。</param>
        public bool TryHandOffStaged(StableId areaId, out AreaInstanceHandle handle, out int sceneHandle)
        {
            handle = AreaInstanceHandle.None;
            sceneHandle = 0;

            if (Phase != AreaPreloadPhase.Staged
                || !StagedArea.IsValid || StagedSceneHandle == 0
                || !areaId.IsValid || !StagedArea.AreaId.Equals(areaId))
            {
                return false;
            }

            handle = StagedArea;
            sceneHandle = StagedSceneHandle;

            // 預かりを手放す。<b>台帳には触らない</b>——在留枠は引き続き埋まっている
            // （実 Scene は載ったままなので、空きがあると誤認させてはいけない）。
            StagedArea = AreaInstanceHandle.None;
            StagedSceneHandle = 0;
            _desiredArea = default;
            _desiredPath = null;
            Phase = AreaPreloadPhase.Idle;
            FailureReason = string.Empty;
            HandedOffCount++;
            return true;
        }

        /// <summary>遷移へ引き渡した回数（診断・テスト用）。</summary>
        public int HandedOffCount { get; private set; }

        /// <summary>
        /// <b>成功確定後の旧 Area を、非活動のまま預かる</b>（§6.2 手順 11。裁定 2。工程 P55-10c）。
        ///
        /// <see cref="TryHandOffStaged"/> の逆向きである。これまでは遷移が旧 Area を毎回
        /// unload していたが、裁定 2 で「<b>再利用可能な非活動 Area として先読み管理へ引き渡す</b>」
        /// に変わった。
        ///
        /// <b>預け先を先読みにしたのは、契約が全部そこにあるからである。</b>
        /// <list type="bullet">
        /// <item><description><b>即時の逆移動</b>：同じ先を <see cref="Request"/> されれば
        /// Staged のまま返すので、重複ロードにならない。</description></item>
        /// <item><description><b>別候補への切替</b>：<see cref="Poll"/> が
        /// <b>解放してから</b>新しい候補を読む（元からそう書いてある）。</description></item>
        /// <item><description><b>Single 遷移</b>：<c>DiscardStagedForSingleLoad</c> が
        /// 先読みの預かりを解くので、保持した Area も既存の後始末に乗る。</description></item>
        /// <item><description><b>在留上限</b>：台帳の枠を返さずに Staged へ移すだけなので、
        /// Active 1 ＋ 非活動 1 の合計 2 が保たれる。</description></item>
        /// </list>
        /// 別の場所に「保持リスト」を作ると、この 4 つを<b>全部書き直す</b>ことになる。
        ///
        /// <b>望みも一緒に立てる。</b> 立てないと直後の <see cref="Poll"/> が
        /// 「望まれていない Staged」と見て即座に撤去しにかかる——
        /// §5 の「一度読み始めた先は、境界から離れても保持する」とも一致する。
        /// </summary>
        /// <param name="handle">預かる実体ハンドル（台帳に登録済みのもの）。</param>
        /// <param name="sceneHandle">その実 Scene。</param>
        /// <param name="scenePath">読み直しに使う Scene パス（望みの宣言に要る）。</param>
        public bool TryAdoptRetained(AreaInstanceHandle handle, int sceneHandle, string scenePath)
        {
            if (!handle.IsValid || sceneHandle == 0 || string.IsNullOrEmpty(scenePath))
            {
                RefusedCount++;
                LastRejection = AreaPreloadRejection.InvalidRequest;
                return false;
            }

            // <b>すでに何か掴んでいるなら預からない。</b> 枠は 1 つしかないので、
            // 上書きすると先に預かっていた Scene を撤去する手段を失う。
            if (HasLiveSceneOperation || HoldsStagedScene
                || Phase == AreaPreloadPhase.ReleaseFailed)
            {
                RefusedCount++;
                LastRejection = AreaPreloadRejection.AtCapacity;
                return false;
            }

            StagedArea = handle;
            StagedSceneHandle = sceneHandle;
            _loading = AreaInstanceHandle.None;
            _desiredArea = handle.AreaId;
            _desiredPath = scenePath;
            Phase = AreaPreloadPhase.Staged;
            FailureReason = string.Empty;

            // 台帳の枠は<b>返さない</b>。Active から非活動へ移すだけである。
            _ledger.TrySetPhase(handle, AreaActivationPhase.Staged);
            AdoptedCount++;
            return true;
        }

        /// <summary>非活動のまま預かった回数（診断・テスト用）。</summary>
        public int AdoptedCount { get; private set; }

        /// <summary>
        /// 預かっている Scene が<b>もう載っていない</b>なら、撤去せずに手放す（P5.5 §8。工程 P55-04c）。
        ///
        /// <b>Single 読込（Fade・死亡再開・Launcher への退避）は台帳を通らない。</b>
        /// あれは載っている Scene を全部置き換えるので、こちらが預かっていた Area も
        /// 黙って消える。気付かずにいると、次の候補へ切り替えるときに
        /// <b>存在しない Scene へ撤去を発行</b>し、在留枠も埋まったままになる。
        ///
        /// <b>望みも一緒に落とす。</b> 残すと、直後の <see cref="Poll"/> が
        /// 「まだ欲しい」と解釈して読み直す——プレイヤーは Fade で別の場所へ移ったのに、
        /// 前の隣 Area が追いかけて載る。
        ///
        /// <b>解放に失敗して抱えている場合（<see cref="AreaPreloadPhase.ReleaseFailed"/>）も同じである</b>
        /// （工程 P55-13a。GPT 受入①）。以前は <c>Staged</c> だけを見ていたので、
        /// <b>解放に失敗したまま Single 読込で置き換えられた</b>とき——
        /// つまり付録 C.34 が「通してよい」と定めたまさにその経路で——
        /// 預かりの参照が<b>消えた Scene を指したまま残っていた</b>。
        /// 台帳（<c>SyncResidencyToLoadedAreas</c>）だけが片付いて、こちらが残る。
        ///
        /// 残ると起きること。
        /// <list type="bullet">
        /// <item><description><c>HoldsStagedScene</c> が立ったままなので、
        /// <b>次の出発 Area を二度と預けられない</b>（<see cref="TryAdoptRetained"/> が AtCapacity で断る）。</description></item>
        /// <item><description><c>Phase</c> が <c>ReleaseFailed</c> のままなので
        /// 「撤去し切れていない Scene がある」と言い続ける。</description></item>
        /// <item><description>次の候補へ切り替えるとき、<b>存在しない Scene へ撤去を発行</b>する。</description></item>
        /// </list>
        ///
        /// <b>ここで扱えるのは「操作が終端している」状態だけである。</b>
        /// <c>Releasing</c>（走っている最中）は対象にしない——走っている操作を横から捨てると、
        /// 終端したときに誰も引き取らない。<c>Staged</c> と <c>ReleaseFailed</c> は
        /// どちらも操作が終端したあとの状態なので、実 Scene の生死だけで決められる。
        /// </summary>
        /// <returns>手放したら true。</returns>
        public bool DropStagedIfUnloaded()
        {
            bool terminatedHold = Phase == AreaPreloadPhase.Staged
                                  || Phase == AreaPreloadPhase.ReleaseFailed;
            if (!terminatedHold || !StagedArea.IsValid || StagedSceneHandle == 0)
            {
                return false;
            }

            IAreaSceneHost loadedHost = Host;
            if (loadedHost != null && loadedHost.IsLoaded(StagedSceneHandle))
            {
                return false;
            }

            FinishRelease();
            _desiredArea = default;
            _desiredPath = null;
            Phase = AreaPreloadPhase.Idle;
            FailureReason = string.Empty;
            DroppedStaleCount++;
            return true;
        }

        /// <summary>消えていた Scene の預かりを手放した回数（診断・テスト用）。</summary>
        public int DroppedStaleCount { get; private set; }

        /// <summary>
        /// <b>新しい遷移操作に対応した再試行入口</b>（§5「次の新しい遷移操作で一度だけ再試行できる」）。
        ///
        /// 失敗を抱えていないときは何もしない。抱えているときは<b>一度だけ</b>進める——
        /// 読込失敗なら読み直し、撤去失敗なら残った Scene の撤去をやり直す。
        /// 再び失敗すれば、また外から許可されるまで止まる。
        /// </summary>
        public bool ArmRetry()
        {
            if (!HasFailure)
            {
                return false;
            }

            _retryArmed = true;
            ArmedRetryCount++;
            Poll();
            return true;
        }

        /// <summary>
        /// 状態を進める。<b>毎フレーム呼んでよい</b>（呼ばれなければ何も起きないだけ）。
        ///
        /// 台帳が Scene 操作を断っている間は何もしない。断られたことを失敗として扱わないのは、
        /// 遷移中の読込と先読みがかち合うのは<b>正常</b>で、遷移が終われば先読みは進められるため。
        /// </summary>
        public void Poll()
        {
            switch (Phase)
            {
                case AreaPreloadPhase.Loading:
                    PollLoading();
                    return;

                case AreaPreloadPhase.Releasing:
                    PollReleasing();
                    return;

                case AreaPreloadPhase.ReleaseFailed:
                    // <b>実 Scene が残っている。</b> 新しいロードは出さず、撤去のやり直しだけを狙う。
                    // 台帳からも外していないので、在留枠を食ったままになる——それが正しい
                    // （空きがあると誤認して 3 枚目を読むより、先読みが止まるほうが軽い）。
                    if (!HasRetryPermission())
                    {
                        return;
                    }

                    BeginRelease();
                    return;

                case AreaPreloadPhase.Failed:
                    if (!HasRetryPermission())
                    {
                        return;
                    }

                    break;
            }

            // Idle／Staged／（許可された）Failed：望む先と実際のずれを詰める。
            bool wantsSomething = _desiredArea.IsValid;
            bool hasStaged = Phase == AreaPreloadPhase.Staged && StagedArea.IsValid;

            if (hasStaged)
            {
                if (wantsSomething && StagedArea.AreaId.Equals(_desiredArea))
                {
                    return; // 望みどおり。
                }

                BeginRelease();
                return;
            }

            if (wantsSomething)
            {
                BeginLoad();
            }
        }

        /// <summary>再試行の許可を 1 回分消費する。許可が無ければ見送った回数を数える。</summary>
        private bool HasRetryPermission()
        {
            if (!_retryArmed)
            {
                SuppressedRetryCount++;
                LastRejection = AreaPreloadRejection.AwaitingRetryPermission;
                return false;
            }

            return true;
        }

        /// <summary>Scene 操作を掴めた時点で再試行の許可を 1 回分消費する。</summary>
        private void ConsumeRetryPermission()
        {
            _retryArmed = false;
        }

        private void PollLoading()
        {
            if (_operation == null || !_operation.IsDone)
            {
                return;
            }

            bool failed = _operation.HasError;
            int sceneHandle = _operation.SceneHandle;
            _operation = null;
            _ledger.EndSceneOperation();

            // <b>終端したら自分の申し入れは必ず引っ込む。</b>
            //
            // 読み終わった時点で目的の Area の Awake は走っているので、
            // 通常はゲートがすでに消費していてここは空転する。そうでない場合
            // （ゲートのない Scene・宛先違い・失敗）に残すと、次の先読みが一切通らなくなるし、
            // 次に直開きした Scene が閉じたまま起動する（＝何も動かないゲーム）。
            AreaStagingRequest.Clear();

            if (failed || sceneHandle == 0)
            {
                _ledger.Remove(_loading);
                _loading = AreaInstanceHandle.None;
                Fail(AreaPreloadPhase.Failed,
                    failed ? "先読みの読込が失敗しました。" : "読み込んだ Scene を特定できませんでした。");
                return;
            }

            StagedArea = _loading;
            StagedSceneHandle = sceneHandle;
            _loading = AreaInstanceHandle.None;
            Phase = AreaPreloadPhase.Staged;
            FailureReason = string.Empty;

            // <b>読み終わった実体に、いま配った世代を結び付ける</b>（工程 P55-08b）。
            //
            // これまで結び付けていたのは遷移（<c>BindInstance</c>）だけだった。
            // 先読みが<b>遷移の外で</b>Area を預かるのは距離による先読み（§5）が初めてで、
            // 結び付けないと「索引に居ない実体」に見える——常駐の合わせ直し
            // （<c>SyncResidencyToLoadedAreas</c>）が<b>載っている Scene を台帳から落とす</b>。
            // 落ちると在留枠が空いていると誤認し、上限 2 を超えて読める。
            if (AreaBundleDirectory.TryGetByScene(sceneHandle, out AreaRuntimeBundle staged)
                && staged != null)
            {
                staged.BindInstance(StagedArea);
            }

            // 望む先が読込中に変わっていたら、ここで切り替えが始まる。
            Poll();
        }

        private void PollReleasing()
        {
            if (_operation == null || !_operation.IsDone)
            {
                return;
            }

            bool failed = _operation.HasError;
            _operation = null;
            _ledger.EndSceneOperation();

            if (failed)
            {
                // <b>撤去できたと決めつけない。</b> 実際に Scene が消えたかを確かめる。
                //
                // 確かめずに台帳を空けると、A ＋ B が載ったままなのに「空きあり」と見なして
                // C を読み、実 Scene が 3 枚になる。しかも B をもう一度撤去するための
                // handle まで失う（GPT レビュー R10 の指摘 2）。
                if (Host != null && Host.IsLoaded(StagedSceneHandle))
                {
                    Fail(AreaPreloadPhase.ReleaseFailed,
                        "先読みした Scene の撤去が失敗し、まだ載っています。");
                    return;
                }

                // Scene は消えている（撤去の通知だけが失敗した）。枠は返してよい。
                FinishRelease();
                Fail(AreaPreloadPhase.Failed, "撤去の完了通知が失敗しましたが、Scene は消えています。");
                return;
            }

            FinishRelease();
            Phase = AreaPreloadPhase.Idle;
            FailureReason = string.Empty;
            Poll();
        }

        private void FinishRelease()
        {
            _ledger.Remove(StagedArea);
            StagedArea = AreaInstanceHandle.None;
            StagedSceneHandle = 0;
        }

        private void BeginLoad()
        {
            if (!_ledger.TryBeginSceneOperation())
            {
                return; // 他の Scene 操作が走っている。失敗ではない。
            }

            ConsumeRetryPermission();

            AreaInstanceHandle handle = _ledger.NextHandle(_desiredArea);
            if (!_ledger.TryAdmitStaged(handle))
            {
                _ledger.EndSceneOperation();
                RefusedCount++;
                LastRejection = AreaPreloadRejection.AtCapacity;
                Fail(AreaPreloadPhase.Failed, "在留上限に達しているため先読みできません（上限 "
                    + AreaResidencyLedger.MaxResidentAreas + "）。");
                return;
            }

            // <b>2 枚目を載せる前に現行 Area を確定させる。</b>
            // 載ってしまうと「ちょうど 1 つ」で引く互換経路が使えなくなり、
            // どちらが活動中かを言えるものが居なくなる（§4.3）。
            if (!CurrentAreaProvider.HasScope
                && AreaBundleDirectory.TryGetSingle(out AreaRuntimeBundle active))
            {
                CurrentAreaProvider.TrySetCurrent(_owner, active);
            }

            // 活動ゲートへ「この Area は閉じたまま待たせる」と申し入れる（§4.2）。
            if (!AreaStagingRequest.TryRequest(_desiredArea))
            {
                _ledger.Remove(handle);
                _ledger.EndSceneOperation();
                RefusedCount++;
                LastRejection = AreaPreloadRejection.StagingRequestRefused;
                Fail(AreaPreloadPhase.Failed,
                    "先読み要求が通りませんでした（すでに別の Area を待たせています）。");
                return;
            }

            _loading = handle;
            _operation = Host.LoadAdditive(_desiredPath);
            LoadStartedCount++;
            Phase = AreaPreloadPhase.Loading;
            FailureReason = string.Empty;

            if (_operation == null || _operation.HasError)
            {
                // 開始そのものに失敗した場合も同じ経路で終端させる（§6.3 と同じ考え方）。
                PollLoading();
                if (Phase == AreaPreloadPhase.Failed)
                {
                    RefusedCount++;
                    LastRejection = AreaPreloadRejection.LoadStartFailed;
                }
            }
        }

        private void BeginRelease()
        {
            if (!_ledger.TryBeginSceneOperation())
            {
                return;
            }

            ConsumeRetryPermission();

            _ledger.TrySetPhase(StagedArea, AreaActivationPhase.Retiring);
            _operation = Host.Unload(StagedSceneHandle);
            ReleaseStartedCount++;
            Phase = AreaPreloadPhase.Releasing;
            FailureReason = string.Empty;

            if (_operation == null || _operation.HasError)
            {
                PollReleasing();
            }
        }

        private void Fail(AreaPreloadPhase phase, string reason)
        {
            Phase = phase;
            FailureReason = reason;
            _retryArmed = false;

            // <b>失敗しても望みは残す。</b> 外から再試行を許可されたときに読み直せる。
            // ここで望みまで消すと、呼び出し側が「先読みされている」と思い込んだまま進む。
        }
    }
}
