using Momotaro.Gameplay.Player;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-02：攻撃の先行入力（Buffer）の保持・期限切れ・消費（1 押下 1 回）を検証する。
    /// </summary>
    public sealed class AttackInputBufferTests
    {
        [Test]
        public void Buffer_HoldsThenConsumesOnce()
        {
            var buffer = new AttackInputBuffer(0.30f);
            Assert.IsFalse(buffer.HasBuffered);

            buffer.Buffer();
            Assert.IsTrue(buffer.HasBuffered);
            Assert.IsTrue(buffer.TryConsume(), "保持中は消費できる。");
            Assert.IsFalse(buffer.HasBuffered, "消費後は空。");
            Assert.IsFalse(buffer.TryConsume(), "1 押下は 1 回だけ消費。");
        }

        [Test]
        public void Buffer_ExpiresAfterWindow()
        {
            var buffer = new AttackInputBuffer(0.30f);
            buffer.Buffer();

            buffer.Tick(0.20f);
            Assert.IsTrue(buffer.HasBuffered, "窓内は保持。");

            buffer.Tick(0.15f); // 累計 0.35 > 0.30
            Assert.IsFalse(buffer.HasBuffered, "窓を超えたら破棄。");
            Assert.IsFalse(buffer.TryConsume());
        }

        [Test]
        public void Buffer_ConsumableUntilExactExpiry()
        {
            var buffer = new AttackInputBuffer(0.30f);
            buffer.Buffer();
            buffer.Tick(0.30f); // ちょうど 0 へ
            Assert.IsFalse(buffer.HasBuffered, "ちょうど窓端で失効。");
        }

        [Test]
        public void Buffer_ReBufferRefreshesWindow()
        {
            var buffer = new AttackInputBuffer(0.30f);
            buffer.Buffer();
            buffer.Tick(0.25f);
            buffer.Buffer(); // 再押下で満タンへ
            buffer.Tick(0.20f);
            Assert.IsTrue(buffer.HasBuffered, "再押下で保持タイマーが更新される。");
        }

        [Test]
        public void Clear_DropsBufferedInput()
        {
            var buffer = new AttackInputBuffer(0.30f);
            buffer.Buffer();
            buffer.Clear();
            Assert.IsFalse(buffer.HasBuffered);
        }

        [Test]
        public void NegativeWindow_ClampedToZero_NeverBuffers()
        {
            var buffer = new AttackInputBuffer(-1f);
            Assert.AreEqual(0f, buffer.Window);
            buffer.Buffer();
            Assert.IsFalse(buffer.HasBuffered, "窓 0 では保持しない。");
        }
    }
}
