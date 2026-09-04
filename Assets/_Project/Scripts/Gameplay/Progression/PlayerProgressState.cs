using System.Collections.Generic;
using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Progression
{
    /// <summary>
    /// 主人公の進行データ（P4-00）。徳の累計と、GrantOnce 報酬の付与済み記録を保持する純粋 C# の Runtime State。
    /// MonoBehaviour・UnityEngine の時間／Scene API に依存しないため、EditMode テストで決定的に検証できる
    /// （<c>PlayerVitals</c> と同じ「純粋 State ＋ 保持する MonoBehaviour」の分割）。
    ///
    /// 保持スコープは 1 セッション（＝Scene 常駐の <see cref="PlayerProgressHolder"/> の寿命）。試遊 Scene の Retry は
    /// Scene 再読込であり Holder ごと破棄されるため、徳・付与済み記録はともにリセットされる（P4-00 仕様）。
    /// 完成版の永続化・チェックポイント復元は本 Task の対象外。
    /// </summary>
    public sealed class PlayerProgressState
    {
        // GrantOnce 報酬の付与済み記録。鍵は RewardData の安定 ID 文字列（Instance ID ではないので Prefab 生成でも一意）。
        private readonly HashSet<string> _grantedRewardIds = new HashSet<string>();

        /// <summary>徳の累計（0 以上）。</summary>
        public int Virtue { get; private set; }

        /// <summary>付与済みとして記録された GrantOnce 報酬の数（テスト・診断用）。</summary>
        public int GrantedRewardCount => _grantedRewardIds.Count;

        /// <summary>指定 ID の GrantOnce 報酬が付与済みか。</summary>
        public bool HasGranted(in StableId rewardId)
        {
            return !rewardId.IsEmpty && _grantedRewardIds.Contains(rewardId.Value);
        }

        /// <summary>
        /// 報酬の付与を試みる。GrantOnce の報酬は同じ <see cref="RewardSnapshot.RewardId"/> について 1 度だけ付与する
        /// （敵インスタンス単位の重複排除は上流＝<c>EnemyActor</c> の 1 回発行と <c>CombatSessionController</c> の初回受理が担う）。
        /// </summary>
        /// <param name="reward">付与要求（原本から複製済み）。</param>
        /// <param name="grantedVirtue">実際に加算された徳量（付与しなかった場合は 0）。</param>
        /// <returns>処理結果。</returns>
        public RewardGrantResult TryGrant(in RewardSnapshot reward, out int grantedVirtue)
        {
            grantedVirtue = 0;
            if (!reward.HasReward)
            {
                return RewardGrantResult.NoReward;
            }

            if (!reward.GrantOnce)
            {
                grantedVirtue = AddVirtue(reward.VirtueAmount);
                return RewardGrantResult.Granted;
            }

            // GrantOnce だが鍵が無い（Data 不備）。重複排除はできないが、付与自体は行い結果で区別できるようにする。
            if (reward.RewardId.IsEmpty)
            {
                grantedVirtue = AddVirtue(reward.VirtueAmount);
                return RewardGrantResult.GrantedWithoutId;
            }

            if (!_grantedRewardIds.Add(reward.RewardId.Value))
            {
                return RewardGrantResult.AlreadyGranted;
            }

            grantedVirtue = AddVirtue(reward.VirtueAmount);
            return RewardGrantResult.Granted;
        }

        /// <summary>徳と付与済み記録を初期化する（新規セッション・検証の再試行用）。</summary>
        public void Reset()
        {
            Virtue = 0;
            _grantedRewardIds.Clear();
        }

        /// <summary>徳を加算する（負値は 0 として無視し、int の上限で飽和させる）。実際に加算した量を返す。</summary>
        private int AddVirtue(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int room = int.MaxValue - Virtue;
            int applied = amount > room ? room : amount;
            Virtue += applied;
            return applied;
        }
    }
}
