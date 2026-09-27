using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Session
{
    /// <summary>先読みの段階（P5.5 §4.1／§11 の E04）。</summary>
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

        /// <summary>失敗した。<b>次の要求は受け付ける</b>（詰まらせない）。</summary>
        Failed = 4,
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
        private readonly IAreaSceneHost _host;
        private readonly object _owner;

        private StableId _desiredArea;
        private string _desiredPath;

        private IAreaSceneOperation _operation;
        private AreaInstanceHandle _loading;

        /// <summary>作る。<paramref name="owner"/> は現行 Area の指定に使う所有者（§5.2 の所有者一致）。</summary>
        public AreaPreloader(AreaResidencyLedger ledger, IAreaSceneHost host, object owner)
        {
            _ledger = ledger;
            _host = host;
            _owner = owner;
        }

        /// <summary>いまの段階。</summary>
        public AreaPreloadPhase Phase { get; private set; } = AreaPreloadPhase.Idle;

        /// <summary>閉じたまま待っている Area（無ければ無効ハンドル）。</summary>
        public AreaInstanceHandle StagedArea { get; private set; }

        /// <summary>閉じたまま待っている Scene の handle（無ければ 0）。</summary>
        public int StagedSceneHandle { get; private set; }

        /// <summary>いま望んでいる先（無ければ空）。</summary>
        public StableId DesiredArea => _desiredArea;

        /// <summary>失敗の理由（成功なら空）。</summary>
        public string FailureReason { get; private set; } = string.Empty;

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

        /// <summary>最後に断った理由（診断・テスト用）。</summary>
        public AreaPreloadRejection LastRejection { get; private set; }

        /// <summary>
        /// 望む先を宣言する。<b>同じ先なら何もしない</b>（読み直さない）。
        /// 別の先なら、撤去してから読み込む（候補切替）。実際の進行は <see cref="Poll"/> が行う。
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
                return true;
            }

            if (_desiredArea.IsValid)
            {
                SwitchedCount++;
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
            }

            // Idle／Staged／Failed：望む先と実際のずれを詰める。
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
            // 読み終わった時点で目的の Area の <c>Awake</c> は走っているので、
            // 通常はゲートがすでに消費していてここは空転する。そうでない場合
            // （ゲートのない Scene・宛先違い・失敗）に残すと、<b>次の先読みが一切通らなくなる</b>し、
            // 次に直開きした Scene が閉じたまま起動する（＝何も動かないゲーム）。
            AreaStagingRequest.Clear();

            if (failed || sceneHandle == 0)
            {
                _ledger.Remove(_loading);
                _loading = AreaInstanceHandle.None;
                Fail(failed ? "先読みの読込が失敗しました。" : "読み込んだ Scene を特定できませんでした。");
                return;
            }

            StagedArea = _loading;
            StagedSceneHandle = sceneHandle;
            _loading = AreaInstanceHandle.None;
            Phase = AreaPreloadPhase.Staged;
            FailureReason = string.Empty;

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
            _ledger.Remove(StagedArea);
            StagedArea = AreaInstanceHandle.None;
            StagedSceneHandle = 0;

            if (failed)
            {
                // 撤去に失敗しても台帳からは外す。
                // 残すと在留上限を食い続けて、以降の先読みが一切通らなくなる（§9.1）。
                Fail("先読みした Scene の撤去が失敗しました。");
                return;
            }

            Phase = AreaPreloadPhase.Idle;
            FailureReason = string.Empty;
            Poll();
        }

        private void BeginLoad()
        {
            if (!_ledger.TryBeginSceneOperation())
            {
                return; // 他の Scene 操作が走っている。失敗ではない。
            }

            AreaInstanceHandle handle = _ledger.NextHandle(_desiredArea);
            if (!_ledger.TryAdmitStaged(handle))
            {
                _ledger.EndSceneOperation();
                RefusedCount++;
                LastRejection = AreaPreloadRejection.AtCapacity;
                Fail("在留上限に達しているため先読みできません（上限 "
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
                Fail("先読み要求が通りませんでした（すでに別の Area を待たせています）。");
                return;
            }

            _loading = handle;
            _operation = _host.LoadAdditive(_desiredPath);
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

            _ledger.TrySetPhase(StagedArea, AreaActivationPhase.Retiring);
            _operation = _host.Unload(StagedSceneHandle);
            ReleaseStartedCount++;
            Phase = AreaPreloadPhase.Releasing;

            if (_operation == null || _operation.HasError)
            {
                PollReleasing();
            }
        }

        private void Fail(string reason)
        {
            Phase = AreaPreloadPhase.Failed;
            FailureReason = reason;

            // <b>失敗しても望みは残す。</b> 次の Poll で読み直せる。
            // ここで望みまで消すと、呼び出し側が「先読みされている」と思い込んだまま進む。
        }
    }
}
