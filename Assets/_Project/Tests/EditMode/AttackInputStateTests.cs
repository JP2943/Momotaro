using Momotaro.Gameplay.Player;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-02：PlayerInputState の攻撃エッジ検出（押下 1 回 = 1 消費、Hold で連続しない）と、
    /// ゲート（Active）による遮断・再開時の押しっぱなし誤発火防止を検証する。
    /// </summary>
    public sealed class AttackInputStateTests
    {
        [Test]
        public void AttackPress_RisingEdge_LatchesOnce()
        {
            var state = new PlayerInputState();

            state.SetAttack(true);
            Assert.IsTrue(state.ConsumeAttackPressed(), "押下エッジで 1 回取り出せる。");
            Assert.IsFalse(state.ConsumeAttackPressed(), "同じ押下では 2 回取り出せない。");
        }

        [Test]
        public void AttackHold_DoesNotRepeat()
        {
            var state = new PlayerInputState();

            state.SetAttack(true);   // 押下
            Assert.IsTrue(state.ConsumeAttackPressed());

            state.SetAttack(true);   // 保持（同じ値）
            state.SetAttack(true);
            Assert.IsFalse(state.ConsumeAttackPressed(), "Hold では再ラッチしない。");
        }

        [Test]
        public void AttackReleaseThenPress_LatchesAgain()
        {
            var state = new PlayerInputState();

            state.SetAttack(true);
            state.ConsumeAttackPressed();
            state.SetAttack(false);  // 離す
            state.SetAttack(true);   // 再押下
            Assert.IsTrue(state.ConsumeAttackPressed(), "離してからの再押下は新しいエッジ。");
        }

        [Test]
        public void WhileInactive_AttackPressIsIgnored()
        {
            var state = new PlayerInputState();
            state.SetActive(false);

            state.SetAttack(true);
            Assert.IsFalse(state.ConsumeAttackPressed(), "遮断中の押下は蓄積しない。");
        }

        [Test]
        public void GateClose_DropsPendingAttackEdge()
        {
            var state = new PlayerInputState();
            state.SetAttack(true);       // ラッチ済み（未消費）
            state.SetActive(false);      // 遮断で破棄
            Assert.IsFalse(state.ConsumeAttackPressed(), "遮断で未消費エッジは破棄。");
        }

        [Test]
        public void HeldThroughGate_DoesNotAutoFireOnReactivate()
        {
            var state = new PlayerInputState();

            state.SetAttack(true);       // 押しっぱなしで
            state.ConsumeAttackPressed();
            state.SetActive(false);      // 遮断
            state.SetAttack(true);       // 遮断中も保持継続（生状態のみ追跡）
            state.SetActive(true);       // 再開
            Assert.IsFalse(state.ConsumeAttackPressed(), "押しっぱなしのまま再開しても誤発火しない。");

            state.SetAttack(false);
            state.SetAttack(true);       // 一度離して押し直す
            Assert.IsTrue(state.ConsumeAttackPressed(), "押し直せば発火する。");
        }

        [Test]
        public void Active_ReflectsGateState()
        {
            var state = new PlayerInputState();
            Assert.IsTrue(state.Active, "既定はアクティブ。");
            state.SetActive(false);
            Assert.IsFalse(state.Active);
            state.SetActive(true);
            Assert.IsTrue(state.Active);
        }
    }
}
