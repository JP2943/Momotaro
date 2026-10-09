using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Progression;

namespace Momotaro.Gameplay.Session
{
    /// <summary>休息の結果（P6A-03）。</summary>
    public readonly struct RestOutcome
    {
        public RestOutcome(bool rested, int cycleAfter)
        {
            Rested = rested;
            CycleAfter = cycleAfter;
        }

        /// <summary>休息が成立したか。</summary>
        public bool Rested { get; }

        /// <summary>休息後の再出現周期。</summary>
        public int CycleAfter { get; }
    }

    /// <summary>
    /// お地蔵様の手順（P6A-03／04。仕様 §5・§7）。純粋（UnityEngine に依存しない）で、EditMode で決定的に検証できる。
    ///
    /// <b>休息は 1 か所だけ</b>：休息・成長・旅立ちの成功は、ここの <see cref="Rest"/> を<b>一度だけ</b>使う
    /// （仕様 §5「複数の通知がそれぞれ周期を増やす構造を禁止」）。周期の更新・全回復・きびだんご補充・
    /// 活動 Area の普通敵の作り直し・登録（死亡地点と中断位置）を 1 つの更新として行い、保存要求は最後に 1 件。
    ///
    /// 死亡再開は既存の手順（要求 ID につき周期 1 回・到着で全回復）を使い、完了時にきびだんご補充と復帰位置だけを足す
    /// （<c>AreaTransitionService</c>）。
    /// </summary>
    public static class ShrineProcedures
    {
        /// <summary>
        /// 休息する（仕様 §5）。<paramref name="shrine"/> を登録し直す（死亡地点・中断位置をこのお地蔵様へ）。
        /// </summary>
        public static RestOutcome Rest(GameSessionState session, CampaignCatalog campaign, ShrineInfo shrine,
            IRestTarget actors, IReadOnlyList<IFieldEnemyRebuild> activeFields, string reason)
        {
            if (session == null || campaign == null || !shrine.IsValid)
            {
                return default;
            }

            session.Changes.BeginBatch(reason);
            try
            {
                // 1. 全世界の普通敵の復活周期を 1 回だけ進める（未ロード Area は次の生成時に反映）。
                session.AdvanceRespawnCycle();

                // 2. 全回復（主人公・仲間・CD）。
                actors?.RestoreForRest();

                // 3. きびだんごを上限まで。消耗品は戻さない。
                session.RefillKibidango(campaign.KibidangoCapacityOf(session.Progress));

                // 4. 活動中の Area は普通敵をその場で作り直す。
                if (activeFields != null)
                {
                    for (int i = 0; i < activeFields.Count; i++)
                    {
                        activeFields[i]?.RebuildNow();
                    }
                }

                // 5. 登録（死亡地点・中断位置）。保存要求はここで立ち、まとめの最後に 1 件だけ出る。
                session.RegisterShrine(shrine);
                session.Changes.RequestAutosave(reason);
                return new RestOutcome(true, session.RespawnCycle);
            }
            finally
            {
                session.Changes.EndBatch();
            }
        }

        /// <summary>
        /// 成長を取得する（仕様 §7）。<b>不成立（価格不足・取得済み・未知 ID・前提不成立）は全体無変更で休息もしない</b>。
        /// 成立したら支出・取得記録 → 効果の再計算 → 休息（同じ 1 回）→ 保存。
        /// </summary>
        public static GrowthPurchaseResult PurchaseGrowth(GameSessionState session, CampaignCatalog campaign,
            ShrineInfo shrine, StableId growthId, IRestTarget actors, IReadOnlyList<IFieldEnemyRebuild> activeFields)
        {
            if (session == null || campaign == null || !shrine.IsValid)
            {
                return GrowthPurchaseResult.NotAllowedHere;
            }

            GrowthPurchaseResult check = CanPurchase(session, campaign, growthId);
            if (check != GrowthPurchaseResult.Purchased)
            {
                return check;
            }

            PlayerProgressState progress = session.Progress;
            campaign.TryGetGrowth(growthId, out GrowthInfo growth);
            session.Changes.BeginBatch("growth_purchased");
            try
            {
                GrowthPurchaseResult result = progress.TryPurchaseGrowth(growthId, growth.Cost);
                if (result != GrowthPurchaseResult.Purchased)
                {
                    return result;
                }

                // 効果は基礎値と取得 ID から置き直す（加算を繰り返さない）。休息より前——回復は新しい最大値まで。
                actors?.ApplyGrowthEffects(campaign.GrowthEffectsOf(progress));
                Rest(session, campaign, shrine, actors, activeFields, "growth_purchased");
                return GrowthPurchaseResult.Purchased;
            }
            finally
            {
                session.Changes.EndBatch();
            }
        }

        /// <summary>
        /// 取得を今の状態で受け付けられるか（P6B 01。UI の表示と処理側の再検証が<b>同じ判定</b>を使う）。
        /// 既知 ID・未取得・全前提取得済み・排他なし・徳残高。成立なら <see cref="GrowthPurchaseResult.Purchased"/>。
        /// </summary>
        public static GrowthPurchaseResult CanPurchase(GameSessionState session, CampaignCatalog campaign, StableId growthId)
        {
            if (session == null || campaign == null)
            {
                return GrowthPurchaseResult.NotAllowedHere;
            }

            if (!campaign.TryGetGrowth(growthId, out GrowthInfo growth))
            {
                return GrowthPurchaseResult.UnknownGrowth;
            }

            PlayerProgressState progress = session.Progress;
            if (progress.HasGrowth(growthId))
            {
                return GrowthPurchaseResult.AlreadyAcquired;
            }

            for (int i = 0; i < growth.Prerequisites.Count; i++)
            {
                if (!progress.HasGrowth(growth.Prerequisites[i]))
                {
                    return GrowthPurchaseResult.PrerequisiteNotMet;
                }
            }

            for (int i = 0; i < growth.Exclusives.Count; i++)
            {
                if (progress.HasGrowth(growth.Exclusives[i]))
                {
                    return GrowthPurchaseResult.PrerequisiteNotMet;
                }
            }

            if (growth.Cost < 0)
            {
                return GrowthPurchaseResult.InvalidCost;
            }

            return progress.AvailableVirtue < growth.Cost
                ? GrowthPurchaseResult.InsufficientVirtue
                : GrowthPurchaseResult.Purchased;
        }

        /// <summary>
        /// 払い戻しを今の状態で受け付けられるか（P6B 02。仕様 §5）。UI の無効表示と処理側の再検証が<b>同じ判定</b>を使う。
        /// 既知 ID・取得済み・末端（取得済みの依存ノードが無い）・権利 1 以上。
        /// </summary>
        public static GrowthRefundResult CanRefund(GameSessionState session, CampaignCatalog campaign, StableId growthId)
        {
            if (session == null || campaign == null)
            {
                return GrowthRefundResult.NotAllowedHere;
            }

            if (!campaign.TryGetGrowth(growthId, out _))
            {
                return GrowthRefundResult.UnknownGrowth;
            }

            PlayerProgressState progress = session.Progress;
            if (!progress.HasGrowth(growthId))
            {
                return GrowthRefundResult.NotAcquired;
            }

            if (campaign.HasAcquiredDependent(progress, growthId))
            {
                return GrowthRefundResult.HasDependents;
            }

            if (progress.RefundRights <= 0)
            {
                return GrowthRefundResult.NoRights;
            }

            return GrowthRefundResult.Refunded;
        }

        /// <summary>
        /// 成長を 1 つ払い戻す（P6B 02。仕様 §5）。<b>最新状態で再検証</b>し、不成立（未知・未取得・非末端・権利 0・場所外）は
        /// 全体無変更で休息もしない。成立したら 取得記録削除・実支出返還・権利 -1 → 効果の再計算 → 休息（新しい上限で
        /// 全回復・補充・普通敵 1 周期）→ 保存要求 1 件を、1 つのまとめで確定する。
        /// </summary>
        public static GrowthRefundResult RefundGrowth(GameSessionState session, CampaignCatalog campaign,
            ShrineInfo shrine, StableId growthId, IRestTarget actors, IReadOnlyList<IFieldEnemyRebuild> activeFields)
        {
            if (session == null || campaign == null || !shrine.IsValid)
            {
                return GrowthRefundResult.NotAllowedHere;
            }

            GrowthRefundResult check = CanRefund(session, campaign, growthId);
            if (check != GrowthRefundResult.Refunded)
            {
                return check;
            }

            session.Changes.BeginBatch("growth_refunded");
            try
            {
                PlayerProgressState progress = session.Progress;
                GrowthRefundResult result = progress.TryRefundGrowth(growthId, out _);
                if (result != GrowthRefundResult.Refunded)
                {
                    return result;
                }

                // 効果を基礎値と残る取得 ID から置き直してから休息（新しい上限まで回復・補充し、超過を残さない）。
                actors?.ApplyGrowthEffects(campaign.GrowthEffectsOf(progress));
                Rest(session, campaign, shrine, actors, activeFields, "growth_refunded");
                return GrowthRefundResult.Refunded;
            }
            finally
            {
                session.Changes.EndBatch();
            }
        }

        /// <summary>
        /// 章クリアの払い戻し権利追加（P6B 02。仕様 §5 の接続口）。既知の章で未処理なら min(上限, 現在＋追加) とし、
        /// 同じ更新で処理済みに記録して保存を要求する。章の確定処理が自分のまとめ（BeginBatch）の中から呼べば、
        /// 章の確定と同じ保存単位に入る。本番の章進行は作らない（P6B は fixture で検証）。
        /// </summary>
        public static ChapterRightsResult GrantChapterRefundRights(GameSessionState session, CampaignCatalog campaign,
            StableId chapterId, out int added)
        {
            added = 0;
            if (session == null || campaign == null || !campaign.IsKnownChapter(chapterId))
            {
                return ChapterRightsResult.UnknownChapter;
            }

            return session.Progress.TryGrantChapterRefundRights(chapterId, campaign.RefundRightsPerChapter,
                campaign.RefundRightsMax, out added);
        }

        /// <summary>
        /// 旅立ちの行き先として選べるか（仕様 §6）。登録済み・解決可能・出発地点と別。料金・回数制限は無い。
        /// </summary>
        public static FastTravelRejection CanFastTravel(GameSessionState session, CampaignCatalog campaign,
            StableId fromShrine, StableId toShrine, out ShrineInfo destination)
        {
            destination = default;
            if (session == null || campaign == null)
            {
                return FastTravelRejection.NotWired;
            }

            if (fromShrine.Equals(toShrine))
            {
                return FastTravelRejection.SameShrine;
            }

            if (!session.IsShrineRegistered(fromShrine))
            {
                return FastTravelRejection.NotAtRegisteredShrine;
            }

            if (!session.IsShrineRegistered(toShrine))
            {
                return FastTravelRejection.NotRegistered;
            }

            if (!campaign.TryGetShrine(toShrine, out destination))
            {
                return FastTravelRejection.Unresolvable;
            }

            return FastTravelRejection.None;
        }
    }

    /// <summary>旅立ちを断った理由（P6A-04）。</summary>
    public enum FastTravelRejection
    {
        None = 0,

        /// <summary>同じお地蔵様（休息ボタンを使う）。</summary>
        SameShrine = 1,

        /// <summary>行き先が未登録。</summary>
        NotRegistered = 2,

        /// <summary>出発地点が登録済みのお地蔵様ではない（お地蔵様の外から要求した）。</summary>
        NotAtRegisteredShrine = 3,

        /// <summary>行き先をカタログで解決できない。</summary>
        Unresolvable = 4,

        /// <summary>操作できる状態ではない（戦闘・遷移・死亡の最中など）。</summary>
        NotAllowedNow = 5,

        /// <summary>遷移の受付に断られた。</summary>
        TravelRejected = 6,

        /// <summary>配線が足りない。</summary>
        NotWired = 7,
    }
}
