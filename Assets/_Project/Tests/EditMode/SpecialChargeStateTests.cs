using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-10：必殺技チャージ（<see cref="SpecialChargeState"/>）を検証する。最大 2.0 秒（1.99 は不発・2.00 で発動可）、
    /// 最大到達後 0.75 秒まで保持でき保持限界超過で自動発動、最大未満 Release は不発、キャンセルで無効化。
    /// </summary>
    public sealed class SpecialChargeStateTests
    {
        private static SpecialChargeState Make() => new SpecialChargeState(2.0f, 0.75f);

        [Test]
        public void ReleaseAt199_NotCharged()
        {
            var s = Make();
            s.Begin();
            s.Tick(1.99f);
            Assert.IsFalse(s.IsCharged, "1.99 秒は最大未満。");
            Assert.AreEqual(SpecialReleaseResult.NotCharged, s.Release(), "最大未満 Release は不発。");
            Assert.IsFalse(s.IsActive);
        }

        [Test]
        public void ReleaseAt200_Fires()
        {
            var s = Make();
            s.Begin();
            s.Tick(2.00f);
            Assert.IsTrue(s.IsCharged, "2.00 秒で最大到達。");
            Assert.AreEqual(SpecialReleaseResult.Fire, s.Release(), "最大到達で発動。");
        }

        [Test]
        public void HoldLimit_AutoFires_AfterMaxPlusHold()
        {
            var s = Make();
            s.Begin();
            s.Tick(2.00f);
            Assert.IsFalse(s.ShouldAutoFire, "最大直後は自動発動しない。");
            s.Tick(0.74f); // 2.74 < 2.75
            Assert.IsFalse(s.ShouldAutoFire, "保持限界(0.75)未満。");
            s.Tick(0.02f); // 2.76 > 2.75
            Assert.IsTrue(s.ShouldAutoFire, "保持限界超過で自動発動。");
        }

        [Test]
        public void Charging_BeforeMax()
        {
            var s = Make();
            s.Begin();
            s.Tick(1.0f);
            Assert.IsTrue(s.IsCharging);
            Assert.IsFalse(s.IsCharged);
            Assert.IsTrue(s.IsActive);
        }

        [Test]
        public void Cancel_Deactivates()
        {
            var s = Make();
            s.Begin();
            s.Tick(2.0f);
            s.Cancel();
            Assert.IsFalse(s.IsActive);
            Assert.AreEqual(SpecialReleaseResult.NotCharged, s.Release(), "キャンセル後は発動しない。");
        }

        [Test]
        public void ReleaseWithoutBegin_NotCharged()
        {
            var s = Make();
            Assert.AreEqual(SpecialReleaseResult.NotCharged, s.Release());
        }
    }
}
