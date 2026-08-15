using Momotaro.Gameplay.Enemy.Locomotion;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-05：攻撃後待機タイマー <see cref="EnemyPostAttackWait"/> を検証する（§9.1 / Table 6）。範囲補間・カウントダウン・
    /// 待機判定。純粋・再現可能。
    /// </summary>
    public sealed class EnemyPostAttackWaitTests
    {
        [Test]
        public void PickDuration_WithinRange()
        {
            Assert.AreEqual(0.7f, EnemyPostAttackWait.PickDuration(0.7f, 1.2f, 0f), 1e-4f);
            Assert.AreEqual(1.2f, EnemyPostAttackWait.PickDuration(0.7f, 1.2f, 1f), 1e-4f);
            float mid = EnemyPostAttackWait.PickDuration(0.7f, 1.2f, 0.5f);
            Assert.GreaterOrEqual(mid, 0.7f);
            Assert.LessOrEqual(mid, 1.2f);
        }

        [Test]
        public void PickDuration_ClampsAndOrders()
        {
            Assert.AreEqual(0.7f, EnemyPostAttackWait.PickDuration(1.2f, 0.7f, 0f), 1e-4f, "順序が逆でも小さい方から。");
            Assert.AreEqual(1.2f, EnemyPostAttackWait.PickDuration(0.7f, 1.2f, 2f), 1e-4f, "t は 0..1 にクランプ。");
        }

        [Test]
        public void Begin_Tick_CountsDown_ThenClears()
        {
            var w = new EnemyPostAttackWait();
            Assert.IsFalse(w.IsWaiting);

            w.Begin(1.0f);
            Assert.IsTrue(w.IsWaiting);
            Assert.AreEqual(1.0f, w.Remaining, 1e-4f);

            w.Tick(0.4f);
            Assert.AreEqual(0.6f, w.Remaining, 1e-4f);
            Assert.IsTrue(w.IsWaiting);

            w.Tick(1.0f);
            Assert.AreEqual(0f, w.Remaining, 1e-4f);
            Assert.IsFalse(w.IsWaiting, "0 でカウントダウン終了。");
        }

        [Test]
        public void Clear_StopsWaiting()
        {
            var w = new EnemyPostAttackWait();
            w.Begin(1.0f);
            w.Clear();
            Assert.IsFalse(w.IsWaiting);
        }
    }
}
