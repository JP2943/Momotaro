using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-07：スタミナ回復とガードブレイク（<see cref="StaminaState"/>）を検証する。回復待機 1.0（0 到達時 1.5）、毎秒 25 回復、
    /// ガード中は回復停止・攻撃中は回復可、スタミナ 0 でブレイク 1.5 秒（行動不能）、ブレイク終了で最大の 25% 回復、
    /// ブレイク中の被 HP ダメージ倍率、状態境界を跨いだ余剰 deltaTime の反映（大 Tick ≒ 小分け Tick）を確認する。
    /// </summary>
    public sealed class StaminaStateTests
    {
        private static StaminaState Make() => new StaminaState(100f); // 25/s, 待機1.0/1.5, Break1.5s/25%/×1.25

        [Test]
        public void Consume_ReducesAndReturnsActual()
        {
            var s = Make();
            float consumed = s.Consume(20f);
            Assert.AreEqual(20f, consumed, 1e-4f);
            Assert.AreEqual(80f, s.Current, 1e-4f);
        }

        [Test]
        public void Regen_StartsAfterDelay_At25PerSecond()
        {
            var s = Make();
            s.Consume(20f); // 80、待機1.0
            s.Tick(1.5f, regenBlocked: false); // 待機1.0消化＋余剰0.5秒回復 = 25×0.5 = 12.5 → 92.5
            Assert.AreEqual(92.5f, s.Current, 0.1f, "待機後 毎秒25回復（余剰分を同Tickで反映）。");
        }

        [Test]
        public void Regen_Blocked_PausesThenResumes()
        {
            var s = Make();
            s.Consume(20f); // 80
            s.Tick(1.5f, regenBlocked: true);  // ガード中：回復停止（待機据え置き）
            Assert.AreEqual(80f, s.Current, 1e-4f, "回復停止中は増えない。");

            s.Tick(1.5f, regenBlocked: false); // 再開：待機1.0＋0.5回復 → 92.5
            Assert.AreEqual(92.5f, s.Current, 0.1f, "再開後に回復する。");
        }

        [Test]
        public void Regen_AllowedWhileAttacking_NotBlocked()
        {
            // 攻撃中は regenBlocked=false を渡す前提。回復が進むことを確認。
            var s = Make();
            s.Consume(40f); // 60
            s.Tick(2.0f, regenBlocked: false); // 待機1.0＋1.0回復=25 → 85
            Assert.AreEqual(85f, s.Current, 0.1f);
        }

        [Test]
        public void ZeroStamina_UsesLongerWait_WhenBreakDisabled()
        {
            var s = new StaminaState(100f, breakSeconds: 0f); // ブレイク無効で 0 到達待機のみ検証
            s.Consume(100f); // 0
            Assert.IsFalse(s.IsBroken);
            s.Tick(1.4f, false); // 1.4 < 1.5 待機 → 回復なし
            Assert.AreEqual(0f, s.Current, 1e-4f, "0 到達時の待機は 1.5 秒。");
            s.Tick(0.2f, false); // 累計1.6 → 0.1秒回復 = 2.5
            Assert.AreEqual(2.5f, s.Current, 0.1f);
        }

        [Test]
        public void ZeroStamina_TriggersGuardBreak()
        {
            var s = Make();
            s.Consume(100f); // 0
            Assert.IsTrue(s.IsBroken, "スタミナ0でガードブレイク。");
            Assert.AreEqual(1.5f, s.BreakRemaining, 1e-4f);
            Assert.AreEqual(1.25f, s.BreakHpMultiplier, "ブレイク中は被HP×1.25。");
        }

        [Test]
        public void Break_NoRegenDuringBreak_ThenRestore25Percent()
        {
            var s = Make();
            s.Consume(100f); // ブレイク
            s.Tick(1.0f, false);
            Assert.IsTrue(s.IsBroken, "ブレイク中。");
            Assert.AreEqual(0f, s.Current, 1e-4f, "ブレイク中は回復しない。");

            s.Tick(0.6f, false); // 累計1.6 > 1.5 → ブレイク終了
            Assert.IsFalse(s.IsBroken);
            Assert.AreEqual(25f, s.Current, 0.1f, "ブレイク終了で最大の25%回復。");
            Assert.AreEqual(1f, s.BreakHpMultiplier, "ブレイク解除後は倍率1.0。");
        }

        [Test]
        public void Consume_IgnoredWhileBroken()
        {
            var s = Make();
            s.Consume(100f); // ブレイク
            float consumed = s.Consume(20f);
            Assert.AreEqual(0f, consumed, "行動不能中はガード消費しない。");
        }

        [Test]
        public void LargeTick_ApproximatesSmallTicks()
        {
            var big = Make();
            big.Consume(60f); // 40
            big.Tick(2.0f, false); // 待機1.0＋1.0回復=25 → 65

            var small = Make();
            small.Consume(60f);
            for (int i = 0; i < 20; i++)
            {
                small.Tick(0.1f, false); // 合計2.0
            }

            Assert.AreEqual(big.Current, small.Current, 0.5f, "大Tickと小分けTickが概ね一致。");
            Assert.AreEqual(65f, big.Current, 0.5f);
        }
    }
}
