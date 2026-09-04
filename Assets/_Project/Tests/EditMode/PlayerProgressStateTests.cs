using Momotaro.Core.Identification;
using Momotaro.Gameplay.Progression;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-00：進行データ（<see cref="PlayerProgressState"/>）の付与規則を検証する。徳の加算、GrantOnce（Reward 安定 ID 単位で
    /// 1 セッション 1 回）、繰り返し付与（GrantOnce=false）、ID 欠落時の扱い、リセット、負値・上限の防御を対象とする。
    /// 純粋 C# のため MonoBehaviour・Scene・時間に依存せず決定的に検証できる。
    /// </summary>
    public sealed class PlayerProgressStateTests
    {
        private static RewardSnapshot Reward(string id, int virtue, bool grantOnce, string itemId = null)
        {
            return new RewardSnapshot(new StableId(id), virtue, new StableId(itemId), grantOnce);
        }

        [Test]
        public void NoReward_DoesNotChangeAnything()
        {
            var state = new PlayerProgressState();

            RewardGrantResult result = state.TryGrant(RewardSnapshot.None, out int granted);

            Assert.AreEqual(RewardGrantResult.NoReward, result);
            Assert.AreEqual(0, granted);
            Assert.AreEqual(0, state.Virtue);
            Assert.AreEqual(0, state.GrantedRewardCount);
        }

        [Test]
        public void RepeatableReward_AccumulatesVirtue_AndIsNotRecorded()
        {
            var state = new PlayerProgressState();
            RewardSnapshot reward = Reward("reward_enemy_melee", 10, grantOnce: false);

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(RewardGrantResult.Granted, state.TryGrant(reward, out int granted));
                Assert.AreEqual(10, granted);
            }

            Assert.AreEqual(30, state.Virtue, "一般敵報酬（GrantOnce=false）は撃破ごとに累積する。");
            Assert.AreEqual(0, state.GrantedRewardCount, "GrantOnce=false は付与済み記録に残さない。");
            Assert.IsFalse(state.HasGranted(new StableId("reward_enemy_melee")));
        }

        [Test]
        public void GrantOnceReward_IsGrantedOnlyOnce()
        {
            var state = new PlayerProgressState();
            RewardSnapshot reward = Reward("reward_boss_first_clear", 40, grantOnce: true);

            Assert.AreEqual(RewardGrantResult.Granted, state.TryGrant(reward, out int first));
            Assert.AreEqual(40, first);

            Assert.AreEqual(RewardGrantResult.AlreadyGranted, state.TryGrant(reward, out int second));
            Assert.AreEqual(0, second, "重複時は徳を加算しない。");

            Assert.AreEqual(40, state.Virtue);
            Assert.AreEqual(1, state.GrantedRewardCount);
            Assert.IsTrue(state.HasGranted(new StableId("reward_boss_first_clear")));
        }

        [Test]
        public void GrantOnceRewards_WithDifferentIds_AreIndependent()
        {
            var state = new PlayerProgressState();

            Assert.AreEqual(RewardGrantResult.Granted, state.TryGrant(Reward("reward_a", 10, true), out int _));
            Assert.AreEqual(RewardGrantResult.Granted, state.TryGrant(Reward("reward_b", 5, true), out int _));

            Assert.AreEqual(15, state.Virtue);
            Assert.AreEqual(2, state.GrantedRewardCount);
        }

        [Test]
        public void GrantOnceReward_WithEmptyId_IsGrantedButReported()
        {
            var state = new PlayerProgressState();
            RewardSnapshot broken = Reward(string.Empty, 10, grantOnce: true);

            Assert.AreEqual(RewardGrantResult.GrantedWithoutId, state.TryGrant(broken, out int first));
            Assert.AreEqual(10, first);

            // 鍵が無いため重複排除できない（Data 不備であることを結果種別で示し、付与自体は止めない）。
            Assert.AreEqual(RewardGrantResult.GrantedWithoutId, state.TryGrant(broken, out int second));
            Assert.AreEqual(10, second);

            Assert.AreEqual(20, state.Virtue);
            Assert.AreEqual(0, state.GrantedRewardCount);
            Assert.IsFalse(state.HasGranted(new StableId(string.Empty)));
        }

        [Test]
        public void NegativeVirtue_IsClampedToZero()
        {
            var state = new PlayerProgressState();
            RewardSnapshot reward = Reward("reward_negative", -5, grantOnce: false);

            Assert.AreEqual(0, reward.VirtueAmount, "Snapshot 生成時に 0 へ丸める。");
            Assert.AreEqual(RewardGrantResult.Granted, state.TryGrant(reward, out int granted));
            Assert.AreEqual(0, granted);
            Assert.AreEqual(0, state.Virtue);
        }

        [Test]
        public void Virtue_SaturatesAtIntMax()
        {
            var state = new PlayerProgressState();

            Assert.AreEqual(RewardGrantResult.Granted,
                state.TryGrant(Reward("reward_huge", int.MaxValue, false), out int first));
            Assert.AreEqual(int.MaxValue, first);

            Assert.AreEqual(RewardGrantResult.Granted,
                state.TryGrant(Reward("reward_huge", 10, false), out int second));
            Assert.AreEqual(0, second, "上限に達したら加算量 0 として飽和させる（オーバーフローで負にしない）。");
            Assert.AreEqual(int.MaxValue, state.Virtue);
        }

        [Test]
        public void Reset_ClearsVirtueAndGrantedRecords()
        {
            var state = new PlayerProgressState();
            RewardSnapshot once = Reward("reward_once", 40, grantOnce: true);
            state.TryGrant(once, out int _);
            state.TryGrant(Reward("reward_melee", 10, false), out int _);

            state.Reset();

            Assert.AreEqual(0, state.Virtue);
            Assert.AreEqual(0, state.GrantedRewardCount);
            Assert.IsFalse(state.HasGranted(new StableId("reward_once")));

            // リセット後は GrantOnce 報酬を再び付与できる（新規セッション相当）。
            Assert.AreEqual(RewardGrantResult.Granted, state.TryGrant(once, out int again));
            Assert.AreEqual(40, again);
        }
    }
}
