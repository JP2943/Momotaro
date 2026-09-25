using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Player;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 再開要求の手順（§9.1 手順 3〜5）を<b>1 か所だけ</b>に置く（GPT レビュー R8 の指摘 1）。
    ///
    /// Scene 側の <see cref="CampaignRespawnRunner"/> と、Scene が使えないときに肩代わりする
    /// 常駐側の受付が、どちらもここを呼ぶ。<b>両方に手順を書くと必ず食い違う</b>——
    /// とくに「周期の更新は再開要求 ID につき 1 回」は、写し間違えると
    /// 倒したはずの敵が再試行のたびに湧き直す形で表に出る。
    ///
    /// UnityEngine に依存しないので EditMode で決定的に検証できる。
    /// </summary>
    public static class CampaignRespawnRequestProcedure
    {
        /// <summary>再開地点を解決する（カタログの持ち主が渡す）。</summary>
        public delegate bool RespawnEntryResolver(out AreaEntryInfo entry);

        /// <summary>手順の結果。呼び出し側は診断値をここから写す。</summary>
        public readonly struct Outcome
        {
            internal Outcome(RespawnDecision decision, RespawnRejection rejection,
                AreaTransitionRejection travelRejection, int travelTransitionId, bool cycleAdvanced)
            {
                Decision = decision;
                Rejection = rejection;
                TravelRejection = travelRejection;
                TravelTransitionId = travelTransitionId;
                CycleAdvanced = cycleAdvanced;
            }

            /// <summary>呼び出し元へ返す判定。</summary>
            public RespawnDecision Decision { get; }

            /// <summary>直近の拒否理由（受理なら None）。</summary>
            public RespawnRejection Rejection { get; }

            /// <summary>遷移側の拒否理由。</summary>
            public AreaTransitionRejection TravelRejection { get; }

            /// <summary>受理された遷移の世代（受理していなければ 0）。</summary>
            public int TravelTransitionId { get; }

            /// <summary>この呼び出しで再出現周期を進めたか。</summary>
            public bool CycleAdvanced { get; }
        }

        /// <summary>
        /// 再開を要求する（§9.1 手順 3〜5）。
        ///
        /// 受理は 1 回だけ、周期の更新は再開要求 ID につき 1 回。
        /// 途中で失敗したら<b>同じ要求 ID のまま</b>再開画面へ戻す。
        /// </summary>
        public static Outcome Execute(
            GameSessionState session,
            CampaignRespawnCoordinator respawn,
            RespawnEntryResolver resolveEntry,
            IAreaRespawnTravel travel)
        {
            if (session == null || respawn == null)
            {
                return new Outcome(
                    RespawnDecision.Reject(RespawnRejection.NotWired),
                    RespawnRejection.NotWired, AreaTransitionRejection.None, 0, false);
            }

            // ---- 手順 3：再開操作を 1 回だけ受理する ----
            RespawnDecision decision = respawn.TryRequest();
            if (!decision.Accepted)
            {
                return new Outcome(decision, decision.Rejection, AreaTransitionRejection.None, 0, false);
            }

            // ---- 手順 5：再出現周期を進め、通常戦のクリア記録を初期化する ----
            //
            // 進めるのは「この再開要求で初めてのとき」だけ。読込に失敗して再試行しても、
            // 同じ要求 ID なのでもう一度は進まない（§9.1 末尾、P5-E21）。
            bool advanced = respawn.TryConsumeCycleAdvance(decision.RequestId);
            if (advanced)
            {
                session.AdvanceRespawnCycle();
            }

            AreaEntryInfo entry = default;
            if (resolveEntry == null || !resolveEntry(out entry))
            {
                respawn.NotifyFailed(decision.RequestId);
                return new Outcome(
                    RespawnDecision.Reject(RespawnRejection.NoRespawnPoint),
                    RespawnRejection.NoRespawnPoint, AreaTransitionRejection.None, 0, advanced);
            }

            // ---- 手順 4：Loading へ入り、再開地点をロードする ----
            if (travel == null)
            {
                respawn.NotifyFailed(decision.RequestId);
                return new Outcome(
                    RespawnDecision.Reject(RespawnRejection.NotWired),
                    RespawnRejection.NotWired, AreaTransitionRejection.None, 0, advanced);
            }

            AreaTransitionDecision travelDecision =
                travel.TryRespawnTravel(entry.AreaId, entry.EntryId, decision.RequestId);
            if (!travelDecision.Accepted)
            {
                respawn.NotifyFailed(decision.RequestId);
                return new Outcome(
                    RespawnDecision.Reject(RespawnRejection.TravelRejected),
                    RespawnRejection.TravelRejected, travelDecision.Rejection, 0, advanced);
            }

            // 旧 Interact のラッチを捨てる（§9.1 末尾。再開の押下が到着先の操作へ化けない）。
            (PlayerInputProvider.Current as IInteractInput)?.DiscardInteractPressed();

            return new Outcome(decision, RespawnRejection.None,
                travelDecision.Rejection, travelDecision.TransitionId, advanced);
        }
    }
}
