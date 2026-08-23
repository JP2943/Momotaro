using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-08A：ヒットバック／ガードバックの移動プロファイル純粋モデル（<see cref="ExternalReactionMotion"/>）を検証する。
    /// 供給速度は「距離 ÷ 時間」で一定、方向は XZ へ平坦化・正規化、Y は持たない（Motor が Y を保持すれば Y 不変）、時間で減衰、
    /// Clear／再開（上書き）が正しいこと、時間境界（直前・一致・直後）で供給が切れることを確認する。
    /// </summary>
    public sealed class ExternalReactionMotionTests
    {
        [Test]
        public void Begin_SuppliesConstantVelocity_DistanceOverTime()
        {
            var m = new ExternalReactionMotion();
            m.Begin(new Vector3(1f, 0f, 0f), 0.24f, 0.16f);

            Assert.IsTrue(m.IsActive);
            Assert.AreEqual(0.24f / 0.16f, m.CurrentVelocity.x, 1e-4f, "速度は 距離÷時間。");
            Assert.AreEqual(0f, m.CurrentVelocity.y, 1e-6f, "Y は持たない。");
            Assert.AreEqual(0f, m.CurrentVelocity.z, 1e-6f);
        }

        [Test]
        public void Direction_IsFlattenedToXZ_AndNormalized()
        {
            var m = new ExternalReactionMotion();
            // Y 成分を含む非正規化方向 → XZ へ平坦化・正規化して速度に反映（大きさ = 距離/時間）。
            m.Begin(new Vector3(0f, 5f, 3f), 0.3f, 0.1f);

            Assert.AreEqual(0f, m.CurrentVelocity.y, 1e-6f, "Y 成分は捨てる。");
            Assert.AreEqual(0.3f / 0.1f, m.CurrentVelocity.magnitude, 1e-4f, "大きさは 距離÷時間（正規化後）。");
            Assert.AreEqual(1f, new Vector3(m.CurrentVelocity.x, 0f, m.CurrentVelocity.z).normalized.z, 1e-4f, "+Z へ向く。");
        }

        [Test]
        public void IntegratedDisplacement_EqualsDistance_OverExactTime()
        {
            var m = new ExternalReactionMotion();
            m.Begin(new Vector3(1f, 0f, 0f), 0.24f, 0.16f);

            // Motor と同じ順序（速度を読む→適用→Tick）で 0.02 秒刻みに積分する。
            float dt = 0.02f;
            float moved = 0f;
            for (int i = 0; i < 8; i++) // 8 × 0.02 = 0.16
            {
                moved += m.CurrentVelocity.x * dt;
                m.Tick(dt);
            }

            Assert.AreEqual(0.24f, moved, 1e-3f, "総移動量は距離に一致（空走時）。");
            Assert.IsFalse(m.IsActive, "所要時間経過で供給が切れる。");
            Assert.AreEqual(Vector3.zero, m.CurrentVelocity, "非供給時は速度ゼロ。");
        }

        [Test]
        public void TimeBoundary_JustBefore_Exact_JustAfter()
        {
            var m = new ExternalReactionMotion();
            m.Begin(Vector3.right, 0.1f, 0.1f);

            m.Tick(0.09f);
            Assert.IsTrue(m.IsActive, "直前は供給中。");
            m.Tick(0.01f);
            Assert.IsFalse(m.IsActive, "一致で供給終了。");
            m.Tick(0.01f);
            Assert.IsFalse(m.IsActive, "直後も供給なし（負に潜らない）。");
        }

        [Test]
        public void Invalid_ZeroDistance_ZeroTime_OrZeroDir_DoesNotActivate()
        {
            var m = new ExternalReactionMotion();
            m.Begin(Vector3.right, 0f, 0.1f);
            Assert.IsFalse(m.IsActive, "距離 0 は無効。");
            m.Begin(Vector3.right, 0.1f, 0f);
            Assert.IsFalse(m.IsActive, "時間 0 は無効。");
            m.Begin(Vector3.zero, 0.1f, 0.1f);
            Assert.IsFalse(m.IsActive, "方向ゼロは無効。");
            m.Begin(new Vector3(0f, 9f, 0f), 0.1f, 0.1f);
            Assert.IsFalse(m.IsActive, "XZ 成分ゼロ（垂直のみ）は無効。");
        }

        [Test]
        public void Begin_Overwrites_PreviousPush()
        {
            var m = new ExternalReactionMotion();
            m.Begin(Vector3.right, 0.2f, 0.2f);
            m.Tick(0.1f);
            m.Begin(Vector3.forward, 0.1f, 0.1f); // 上書き（最新を優先）。

            Assert.AreEqual(0.1f, m.Remaining, 1e-4f, "残時間は新しい押し出しに置き換わる。");
            Assert.AreEqual(0.1f / 0.1f, m.CurrentVelocity.z, 1e-4f, "速度も新方向・新レートに置き換わる。");
            Assert.AreEqual(0f, m.CurrentVelocity.x, 1e-6f);
        }

        [Test]
        public void Clear_StopsImmediately()
        {
            var m = new ExternalReactionMotion();
            m.Begin(Vector3.right, 0.2f, 0.2f);
            m.Clear();
            Assert.IsFalse(m.IsActive, "Clear で即時停止。");
            Assert.AreEqual(Vector3.zero, m.CurrentVelocity);
        }
    }
}
