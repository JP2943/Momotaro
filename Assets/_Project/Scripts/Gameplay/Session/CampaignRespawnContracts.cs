using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 本編型死亡再開の段階（P5-08。仕様書 v1.1 §9.1）。
    ///
    /// <b>Failed は「終わり」ではない。</b> 読込に失敗しても進行は失われていないので、
    /// 再開画面へ戻して<b>同じ再開要求のまま</b>もう一度試せるようにする（§9.1 末尾）。
    /// </summary>
    public enum CampaignRespawnPhase
    {
        /// <summary>死亡していない。</summary>
        Idle = 0,

        /// <summary>死亡を受理し、再開操作を待っている（GameOver）。</summary>
        Dead = 1,

        /// <summary>再開を受理し、読込中。</summary>
        Requested = 2,

        /// <summary>読込に失敗して再開画面へ戻った。再試行できる。</summary>
        Failed = 3,
    }

    /// <summary>再開要求を受け付けなかった理由（§9.1）。</summary>
    public enum RespawnRejection
    {
        /// <summary>拒否ではない。</summary>
        None = 0,

        /// <summary>死亡していない。</summary>
        NotDead = 1,

        /// <summary>すでに再開を受理して読込中。</summary>
        AlreadyRequested = 2,

        /// <summary>配線が足りない（Session・目的地の解決役が無い）。</summary>
        NotWired = 3,

        /// <summary>カタログから再開地点を解決できない。</summary>
        NoRespawnPoint = 4,

        /// <summary>遷移の受付に断られた。</summary>
        TravelRejected = 5,
    }

    /// <summary>再開要求の判定結果（受理なら要求 ID を持つ）。</summary>
    public readonly struct RespawnDecision
    {
        private RespawnDecision(bool accepted, RespawnRejection rejection, int requestId)
        {
            Accepted = accepted;
            Rejection = rejection;
            RequestId = requestId;
        }

        /// <summary>受理したか。</summary>
        public bool Accepted { get; }

        /// <summary>拒否の理由（受理なら <see cref="RespawnRejection.None"/>）。</summary>
        public RespawnRejection Rejection { get; }

        /// <summary>
        /// 再開要求の識別子（§9.1 末尾「再出現周期の更新は再開要求 ID につき一度」）。
        /// <b>読込失敗からの再試行では変わらない。</b> 変えてしまうと周期が二度進む。
        /// </summary>
        public int RequestId { get; }

        /// <summary>受理を作る。</summary>
        public static RespawnDecision Accept(int requestId)
        {
            return new RespawnDecision(true, RespawnRejection.None, requestId);
        }

        /// <summary>拒否を作る。</summary>
        public static RespawnDecision Reject(RespawnRejection rejection)
        {
            return new RespawnDecision(false, rejection, 0);
        }
    }

    /// <summary>
    /// 再開操作の受け口（§9.1 手順 3）。入力側（Infrastructure）はこの狭い契約だけを見る。
    /// </summary>
    public interface ICampaignRespawnRequest
    {
        /// <summary>再開操作を待っているか（死亡受理済み、または読込失敗で再試行待ち）。</summary>
        bool IsAwaitingRespawn { get; }

        /// <summary>再開を要求する。受理は 1 回だけで、連打は拒否として数える。</summary>
        RespawnDecision RequestRespawn();
    }

    /// <summary>
    /// 死亡再開のための遷移要求（§9.1 手順 4）。通常の移動と違い、<b>死亡中・GameOver でも通る</b>。
    /// Gameplay 層はこの契約だけを見て、Infrastructure の遷移サービスを直接参照しない（§12 の層規則）。
    /// </summary>
    public interface IAreaRespawnTravel
    {
        /// <summary>再開地点へ移動する。受理したら遷移の世代を返す。</summary>
        AreaTransitionDecision TryRespawnTravel(StableId areaId, StableId entryId);
    }
}
