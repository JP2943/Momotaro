using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-02：追従判断（<see cref="CompanionFollowModel"/>）を検証する。停止・再開のヒステリシス、距離超過ワープ、
    /// 経路失敗ワープ（近づけない時間の蓄積）、ワープ要求が実行されなかった場合の継続要求、リセットを固定する。
    /// 時間は外部注入のため、境界は「直前・一致・直後」を明示して検証できる。
    /// </summary>
    public sealed class CompanionFollowModelTests
    {
        private static readonly Vector3 Leader = Vector3.zero;

        // 停止 0.5 / 再開 1.0 / ワープ 8.0 / 経路失敗 1.0 秒。間隔 0 にして「隊列位置＝主人公位置」で距離を直接扱う。
        private static CompanionFollowSettings Settings(float stuckSeconds = 1f) =>
            new CompanionFollowSettings(0f, 0.5f, 1.0f, 8f, stuckSeconds);

        private static CompanionFollowInput At(float distance) =>
            new CompanionFollowInput(Leader, Vector3.forward, new Vector3(0f, 0f, distance), 0);

        // ---- 停止・再開 ----

        [Test]
        public void FarFromSlot_StartsMoving()
        {
            var m = new CompanionFollowModel();

            Assert.AreEqual(CompanionFollowDecision.Move, m.Tick(At(3f), Settings(), 0.1f));
            Assert.AreEqual(3f, m.DistanceToSlot, 1e-4f);
        }

        [Test]
        public void ReachingStopDistance_Holds()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(3f), Settings(), 0.1f);

            Assert.AreEqual(CompanionFollowDecision.Move, m.Tick(At(0.6f), Settings(), 0.1f), "停止距離の直前は移動を続ける。");
            Assert.AreEqual(CompanionFollowDecision.Hold, m.Tick(At(0.5f), Settings(), 0.1f), "停止距離と一致で停止する。");
        }

        [Test]
        public void WithinResumeDistance_StaysHeld()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(3f), Settings(), 0.1f);
            m.Tick(At(0.4f), Settings(), 0.1f); // 停止。

            Assert.AreEqual(CompanionFollowDecision.Hold, m.Tick(At(0.9f), Settings(), 0.1f),
                "停止距離を超えても再開距離までは動かない（境目で往復しない）。");
        }

        [Test]
        public void BeyondResumeDistance_MovesAgain()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(3f), Settings(), 0.1f);
            m.Tick(At(0.4f), Settings(), 0.1f);

            Assert.AreEqual(CompanionFollowDecision.Move, m.Tick(At(1.0f), Settings(), 0.1f), "再開距離と一致で再び動き出す。");
        }

        // ---- 距離超過ワープ ----

        [Test]
        public void BeyondWarpDistance_RequestsWarp()
        {
            var m = new CompanionFollowModel();

            Assert.AreEqual(CompanionFollowDecision.Move, m.Tick(At(7.9f), Settings(), 0.1f), "ワープ距離の直前は移動。");
            Assert.AreEqual(CompanionFollowDecision.Warp, m.Tick(At(8f), Settings(), 0.1f), "ワープ距離と一致で要求する。");
            Assert.AreEqual(1, m.WarpRequests);
        }

        [Test]
        public void UnhandledWarp_KeepsRequesting()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(20f), Settings(), 0.1f);

            Assert.AreEqual(CompanionFollowDecision.Warp, m.Tick(At(20f), Settings(), 0.1f),
                "Motor がワープを実行しなければ、次の Tick でも要求し続ける。");
            Assert.AreEqual(2, m.WarpRequests);
        }

        [Test]
        public void AfterWarpExecuted_ReturnsToHold()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(20f), Settings(), 0.1f);

            // Motor が隊列位置へ瞬間移動させた次の Tick。
            Assert.AreEqual(CompanionFollowDecision.Hold, m.Tick(At(0f), Settings(), 0.1f));
            Assert.AreEqual(0f, m.StuckSeconds, 1e-4f);
        }

        [Test]
        public void ZeroWarpDistance_DisablesDistanceWarp()
        {
            var settings = new CompanionFollowSettings(0f, 0.5f, 1.0f, 0f, 1f);
            var m = new CompanionFollowModel();

            Assert.AreEqual(CompanionFollowDecision.Move, m.Tick(At(999f), settings, 0.1f),
                "ワープ距離 0 は距離超過判定を無効にする。");
        }

        // ---- 経路失敗ワープ ----

        [Test]
        public void StuckWhileMoving_RequestsWarp_AtThreshold()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(3f), Settings(), 0.5f); // Move へ。

            Assert.AreEqual(CompanionFollowDecision.Move, m.Tick(At(3f), Settings(), 0.5f), "停滞 0.5 秒（しきい値未満）。");
            Assert.AreEqual(0.5f, m.StuckSeconds, 1e-4f);

            Assert.AreEqual(CompanionFollowDecision.Warp, m.Tick(At(3f), Settings(), 0.5f), "停滞 1.0 秒で経路失敗とみなす。");
            Assert.AreEqual(1, m.WarpRequests);
        }

        [Test]
        public void MakingProgress_ResetsStuckTimer()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(3f), Settings(), 0.5f);
            m.Tick(At(3f), Settings(), 0.5f);
            Assert.AreEqual(0.5f, m.StuckSeconds, 1e-4f, "前提：0.5 秒ぶん停滞している。");

            m.Tick(At(2.5f), Settings(), 0.5f); // 前進した。

            Assert.AreEqual(0f, m.StuckSeconds, 1e-4f, "近づけていれば停滞時間は消える。");
        }

        [Test]
        public void HoldingDoesNotAccumulateStuck()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(3f), Settings(), 0.1f);
            m.Tick(At(0.2f), Settings(), 0.1f); // 停止。

            for (int i = 0; i < 20; i++)
            {
                m.Tick(At(0.2f), Settings(), 0.5f);
            }

            Assert.AreEqual(CompanionFollowDecision.Hold, m.Decision, "止まっているのは正常であり、経路失敗ではない。");
            Assert.AreEqual(0f, m.StuckSeconds, 1e-4f);
            Assert.AreEqual(0, m.WarpRequests);
        }

        [Test]
        public void ZeroStuckSeconds_DisablesPathFailureWarp()
        {
            var m = new CompanionFollowModel();
            CompanionFollowSettings settings = Settings(stuckSeconds: 0f);
            m.Tick(At(3f), settings, 0.5f);

            for (int i = 0; i < 20; i++)
            {
                m.Tick(At(3f), settings, 0.5f);
            }

            Assert.AreEqual(CompanionFollowDecision.Move, m.Decision, "経路失敗判定を無効にできる。");
            Assert.AreEqual(0, m.WarpRequests);
        }

        [Test]
        public void NegativeDeltaTime_IsTreatedAsZero()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(3f), Settings(), 0.5f);

            m.Tick(At(3f), Settings(), -10f);

            Assert.AreEqual(0f, m.StuckSeconds, 1e-4f, "負の deltaTime で停滞時間が巻き戻らない。");
        }

        // ---- 隊列位置・リセット ----

        [Test]
        public void SlotPosition_FollowsFormation()
        {
            var m = new CompanionFollowModel();
            var settings = new CompanionFollowSettings(2f, 0.5f, 1.0f, 8f, 1f);
            var input = new CompanionFollowInput(new Vector3(5f, 0f, 5f), Vector3.forward, Vector3.zero, 1);

            m.Tick(input, settings, 0.1f);

            Vector3 expected = FormationSlot.Resolve(input.LeaderPosition, input.LeaderForward, 1, settings.Spacing);
            Assert.AreEqual(expected.x, m.SlotPosition.x, 1e-4f);
            Assert.AreEqual(expected.z, m.SlotPosition.z, 1e-4f);
        }

        [Test]
        public void Reset_ReturnsToHold_AndClearsStuck()
        {
            var m = new CompanionFollowModel();
            m.Tick(At(3f), Settings(), 0.5f);
            m.Tick(At(3f), Settings(), 0.5f);

            m.Reset();

            Assert.AreEqual(CompanionFollowDecision.Hold, m.Decision);
            Assert.AreEqual(0f, m.StuckSeconds, 1e-4f);
        }

        [Test]
        public void Settings_FromNullData_UsesDefaults()
        {
            CompanionFollowSettings s = CompanionFollowSettings.From(null);

            Assert.AreEqual(CompanionFollowSettings.Default.Spacing, s.Spacing, 1e-4f);
            Assert.Greater(s.ResumeDistance, s.StopDistance, "既定値でも再開距離 > 停止距離。");
            Assert.Greater(s.WarpDistance, s.ResumeDistance);
        }

        [Test]
        public void Settings_ClampNegativeValues()
        {
            var s = new CompanionFollowSettings(-1f, -1f, -1f, -1f, -1f, -1f);

            Assert.AreEqual(0f, s.Spacing, 1e-4f);
            Assert.AreEqual(0f, s.StopDistance, 1e-4f);
            Assert.AreEqual(0f, s.ResumeDistance, 1e-4f);
            Assert.AreEqual(0f, s.WarpDistance, 1e-4f);
            Assert.AreEqual(0f, s.StuckSeconds, 1e-4f);
            Assert.AreEqual(0f, s.StuckProgressEpsilon, 1e-4f);
        }
    }
}
