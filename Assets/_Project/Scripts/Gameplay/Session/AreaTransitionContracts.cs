using Momotaro.Core.Identification;
using Momotaro.Gameplay.Modes;

namespace Momotaro.Gameplay.Session
{
    /// <summary>遷移の段階（仕様書 v1.1 §6.2）。失敗は原因と復旧状態を別に持つ。</summary>
    public enum AreaTransitionPhase
    {
        /// <summary>遷移していない。</summary>
        Idle = 0,

        /// <summary>受理済み。停止と採取を行っている段階。</summary>
        Preparing = 1,

        /// <summary>目的地を非同期ロード中。</summary>
        Loading = 2,

        /// <summary>目的地で Bind・復元を行っている段階。</summary>
        Binding = 3,

        /// <summary>配置・カメラ・HUD の確認まで終わった段階。</summary>
        Ready = 4,

        /// <summary>復旧できずに停止している（Error 表示に留める。§6.3）。</summary>
        Failed = 5,
    }

    /// <summary>遷移を受け付けなかった理由（§6.1 の拒否条件が正本。§4.5 はその帰結。裁定 6）。</summary>
    public enum AreaTransitionRejection
    {
        None = 0,

        /// <summary>目的地の AreaId／EntryId をカタログから解決できない（§6.3 の「受理前」）。</summary>
        UnknownDestination = 1,

        /// <summary>AreaReady より前。</summary>
        NotReady = 2,

        /// <summary>Exploration 以外（Pause／Dialogue／Event／Loading／GameOver）。</summary>
        WrongMode = 3,

        /// <summary>主人公が生存していない。</summary>
        PlayerDefeated = 4,

        /// <summary>主人公が移動可能な平常状態でない（攻撃・Guard・Step・Hurt・GuardBreak）。</summary>
        PlayerBusy = 5,

        /// <summary>戦闘開始予約中・戦闘中・勝敗処理中。</summary>
        EncounterActive = 6,

        /// <summary>既に遷移中（二重要求）。</summary>
        AlreadyTransitioning = 7,
    }

    /// <summary>遷移の要求（目的地だけ。座標は到着側が解決する）。</summary>
    public readonly struct AreaTransitionRequest
    {
        public StableId AreaId { get; }
        public StableId EntryId { get; }

        public AreaTransitionRequest(StableId areaId, StableId entryId)
        {
            AreaId = areaId;
            EntryId = entryId;
        }
    }

    /// <summary>受付の結果。受理したときだけ <see cref="TransitionId"/> が有効。</summary>
    public readonly struct AreaTransitionDecision
    {
        public bool Accepted { get; }
        public AreaTransitionRejection Rejection { get; }

        /// <summary>実行世代。古い通知と新しい遷移を区別する鍵（§6.2）。</summary>
        public int TransitionId { get; }

        private AreaTransitionDecision(bool accepted, AreaTransitionRejection rejection, int transitionId)
        {
            Accepted = accepted;
            Rejection = rejection;
            TransitionId = transitionId;
        }

        public static AreaTransitionDecision Accept(int transitionId) =>
            new AreaTransitionDecision(true, AreaTransitionRejection.None, transitionId);

        public static AreaTransitionDecision Reject(AreaTransitionRejection rejection) =>
            new AreaTransitionDecision(false, rejection, 0);
    }

    /// <summary>
    /// 受付条件の供給元（§6.1）。<b>ここが受付条件の正本</b>で、Snapshot 側（§4.5）はその帰結（裁定 6）。
    /// 実装は Scene 側の状態を読むが、調停役はこの狭い契約しか見ないのでテストで Fake を差せる。
    /// </summary>
    public interface IAreaTransitionConditions
    {
        /// <summary>AreaReady が確定しているか。</summary>
        bool IsAreaReady { get; }

        /// <summary>現在のモード。</summary>
        GameMode Mode { get; }

        /// <summary>主人公が生存しているか。</summary>
        bool IsPlayerAlive { get; }

        /// <summary>
        /// 主人公が移動可能な平常状態でないか（攻撃・Guard・Step・Hurt・GuardBreak 等）。
        /// 犬丸が Down／Away／調査中でも主人公の遷移は妨げない（§6.1）ので、ここに仲間の状態は入れない。
        /// </summary>
        bool IsPlayerBusy { get; }

        /// <summary>戦闘開始予約中・戦闘中・勝敗処理中か（§8.3 は戦闘開始を優先する）。</summary>
        bool IsEncounterActive { get; }
    }

    /// <summary>
    /// 非同期ロードの観測口（§6.3）。タイムアウト後も<b>同じ操作を保持して観測し続ける</b>ため、
    /// 調停役は Unity の <c>AsyncOperation</c> を直接持たずこの契約を見る。テストでは Fake を差す。
    /// </summary>
    public interface IAreaLoadOperation
    {
        /// <summary>完了したか。</summary>
        bool IsDone { get; }

        /// <summary>失敗したか（Scene 未登録・入口不在など）。</summary>
        bool HasError { get; }
    }
}
