using Momotaro.Core.Identification;
using Momotaro.Data.Progression;

namespace Momotaro.Gameplay.Progression
{
    /// <summary>
    /// 報酬データ原本（<see cref="RewardData"/>）から、付与に必要な値だけを複製した不変 Snapshot（P4-00）。
    /// <c>AttackSnapshot</c> と同じ方針で、付与要求を受け取った時点で一度生成し、以降の付与計算はこの Snapshot を正本とする。
    /// SO 原本を Runtime に書き換えないという規約（本書 §2.2）を守りつつ、原本が後から変化しても付与結果が揺れないことを保証する。
    ///
    /// P4-00 の範囲は徳（<see cref="VirtueAmount"/>）の実付与まで。<see cref="ItemId"/> は運ぶだけで付与は行わない
    /// （受け手が未実装である旨を警告する）。Inventory は Phase 4 の後続 Task。
    /// </summary>
    public readonly struct RewardSnapshot
    {
        /// <summary>報酬 Data の安定 ID。GrantOnce の重複排除の鍵に用いる。</summary>
        public StableId RewardId { get; }

        /// <summary>付与する徳量（0 以上へ丸める）。</summary>
        public int VirtueAmount { get; }

        /// <summary>付与するアイテムの安定 ID（空なら無し）。P4-00 では付与しない。</summary>
        public StableId ItemId { get; }

        /// <summary>同じ <see cref="RewardId"/> について 1 セッション 1 回だけ付与するか。</summary>
        public bool GrantOnce { get; }

        /// <summary>
        /// 報酬 Data を伴うか。既定値（<see cref="None"/>／<see cref="From"/> に null を渡した場合）は false で、
        /// 「報酬未設定の敵」を正常系として区別するための印。
        /// </summary>
        public bool HasReward { get; }

        /// <summary>各値を指定して生成する（テスト・将来の非 Asset 報酬用）。徳量は 0 未満を 0 へ丸める。</summary>
        public RewardSnapshot(StableId rewardId, int virtueAmount, StableId itemId, bool grantOnce)
        {
            RewardId = rewardId;
            VirtueAmount = virtueAmount < 0 ? 0 : virtueAmount;
            ItemId = itemId;
            GrantOnce = grantOnce;
            HasReward = true;
        }

        /// <summary>報酬なし（<see cref="HasReward"/> が false）。</summary>
        public static RewardSnapshot None => default;

        /// <summary>報酬 Data から Snapshot を生成する。null は <see cref="None"/> を返す（報酬未設定は正常系）。</summary>
        public static RewardSnapshot From(RewardData data)
        {
            if (data == null)
            {
                return None;
            }

            return new RewardSnapshot(data.Id, data.VirtueAmount, data.ItemId, data.GrantOnce);
        }
    }
}
