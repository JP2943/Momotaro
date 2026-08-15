using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-02：認識状態機 <see cref="PerceptionState"/> の完全認識蓄積(0.25s)・不審・視認喪失継続(3s)・被弾即 Alert・
    /// 音/共有による調査・Pause 非進行を検証する（§4.1/§4.3/Table 7）。純粋・再現可能。
    /// </summary>
    public sealed class PerceptionStateTests
    {
        private static PerceptionState Make()
        {
            return new PerceptionState(new PerceptionSettings(120f, 8f, 10f, 2f, 0.25f, 3f));
        }

        private static readonly Vector3 T = new Vector3(0, 0, 5f);

        [Test]
        public void SustainedSight_ReachesAlert_AtFullRecognition()
        {
            var s = Make();
            Assert.IsFalse(s.ObserveSight(true, T, 0.1f)); // 0.1 → Suspicious
            Assert.AreEqual(PerceptionPhase.Suspicious, s.Phase);
            Assert.IsFalse(s.ObserveSight(true, T, 0.1f)); // 0.2 → まだ
            bool became = s.ObserveSight(true, T, 0.1f);   // 0.3 >= 0.25 → Alert
            Assert.IsTrue(became, "完全認識で新規 Alert（警戒声契機）。");
            Assert.AreEqual(PerceptionPhase.Alert, s.Phase);
            Assert.IsTrue(s.HasLastKnownPosition);
            Assert.AreEqual(T, s.LastKnownPosition);
        }

        [Test]
        public void BriefSight_IsSuspicious_NotAlert()
        {
            var s = Make();
            s.ObserveSight(true, T, 0.1f);
            Assert.AreEqual(PerceptionPhase.Suspicious, s.Phase, "短い視認は不審止まり。");
        }

        [Test]
        public void Pause_DeltaZero_DoesNotAdvanceRecognitionTimer()
        {
            var s = Make();
            for (int i = 0; i < 100; i++)
            {
                s.ObserveSight(true, T, 0f); // Pause 相当：時間が進まない
            }

            Assert.AreEqual(0f, s.RecognitionAccum, 1e-6f, "dt=0 では認識蓄積が進まない。");
            Assert.AreNotEqual(PerceptionPhase.Alert, s.Phase, "Pause 中は Alert に至らない。");
        }

        [Test]
        public void AfterAlert_LosingSight_KeepsAlertUntilLoseSeconds()
        {
            var s = Make();
            s.ObserveSight(true, T, 0.3f); // Alert
            Assert.AreEqual(PerceptionPhase.Alert, s.Phase);

            s.ObserveSight(false, T, 2.0f); // 2s 経過：まだ喪失しない
            Assert.AreEqual(PerceptionPhase.Alert, s.Phase, "3 秒未満は Alert 維持（背後移動で即喪失しない）。");

            s.ObserveSight(false, T, 1.5f); // 累計3.5s → 喪失
            Assert.AreEqual(PerceptionPhase.Suspicious, s.Phase, "追跡継続秒を超えたら不審へ落ちる。");
        }

        [Test]
        public void Hit_TriggersImmediateAlert_RegardlessOfSight()
        {
            var s = Make();
            var atk = new Vector3(0, 0, -3f); // 背後（視線外）
            bool rising = s.NotifyHit(true, atk);
            Assert.IsTrue(rising, "被弾で新規 Alert。");
            Assert.AreEqual(PerceptionPhase.Alert, s.Phase);
            Assert.AreEqual(atk, s.LastKnownPosition, "攻撃者位置を最終確認位置に。");
            Assert.IsFalse(s.NotifyHit(true, atk), "既に Alert なら二度目は非上昇。");
        }

        [Test]
        public void NoiseHeard_FromUnaware_BecomesSuspicious()
        {
            var s = Make();
            var noise = new Vector3(4f, 0, 0);
            s.NotifyNoiseHeard(noise);
            Assert.AreEqual(PerceptionPhase.Suspicious, s.Phase);
            Assert.AreEqual(noise, s.LastKnownPosition);
        }

        [Test]
        public void AlertShared_InvestigatesButNotAlert_AndFlagsShared()
        {
            var s = Make();
            var shared = new Vector3(6f, 0, 1f);
            s.NotifyAlertShared(shared);
            Assert.AreEqual(PerceptionPhase.Suspicious, s.Phase, "共有だけでは Alert にしない（直接視認まで）。");
            Assert.IsTrue(s.AlertedByShare, "共有経由フラグ（再共有抑止）。");
            Assert.AreEqual(shared, s.LastKnownPosition);
        }

        [Test]
        public void Reset_ReturnsToUnaware()
        {
            var s = Make();
            s.NotifyHit(true, T);
            s.Reset();
            Assert.AreEqual(PerceptionPhase.Unaware, s.Phase);
            Assert.IsFalse(s.HasLastKnownPosition);
        }
    }
}
