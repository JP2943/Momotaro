using Momotaro.Gameplay.Companion;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-03：仲間の攻撃進行（<see cref="CompanionAttackState"/>）を検証する。段の順序・境界、多重発動しないこと、
    /// 中断で判定が即座に消えること、そして<b>大きな deltaTime で段を飛ばさない</b>ことを固定する。
    /// 純粋 C# のため時間を外部注入でき、フレームレートに依存せず決定的に検証できる。
    /// </summary>
    public sealed class CompanionAttackStateTests
    {
        private const float Startup = 0.2f;
        private const float Active = 0.1f;
        private const float Recovery = 0.3f;

        private static CompanionAttackState Begun()
        {
            var state = new CompanionAttackState();
            Assert.IsTrue(state.Begin(Startup, Active, Recovery), "前提：攻撃を開始できる。");
            return state;
        }

        // ---- 開始 ----

        [Test]
        public void Initial_IsIdle()
        {
            var state = new CompanionAttackState();

            Assert.AreEqual(CompanionAttackPhase.Idle, state.Phase);
            Assert.IsFalse(state.IsAttacking);
            Assert.IsFalse(state.IsHitboxActive, "攻撃していない間は判定を出さない。");
            Assert.IsFalse(state.Finished);
        }

        [Test]
        public void Begin_EntersStartup()
        {
            CompanionAttackState state = Begun();

            Assert.AreEqual(CompanionAttackPhase.Startup, state.Phase);
            Assert.IsTrue(state.IsAttacking);
            Assert.IsFalse(state.IsHitboxActive, "予兆中は判定を出さない。");
            Assert.AreEqual(Startup + Active + Recovery, state.TotalSeconds, 1e-4f);
        }

        [Test]
        public void Begin_WithoutStartup_EntersActiveImmediately()
        {
            var state = new CompanionAttackState();

            Assert.IsTrue(state.Begin(0f, Active, Recovery));

            Assert.AreEqual(CompanionAttackPhase.Active, state.Phase, "予兆 0 の攻撃はその場で判定段に入る。");
            Assert.IsTrue(state.IsHitboxActive);
        }

        [Test]
        public void Begin_WhileAttacking_IsRejected()
        {
            CompanionAttackState state = Begun();

            Assert.IsFalse(state.Begin(Startup, Active, Recovery), "攻撃中は多重発動しない。");
            Assert.AreEqual(CompanionAttackPhase.Startup, state.Phase, "進行中の攻撃は巻き戻らない。");
        }

        [Test]
        public void Begin_WithZeroLength_DoesNotStart()
        {
            var state = new CompanionAttackState();

            Assert.IsFalse(state.Begin(0f, 0f, 0f), "長さゼロの攻撃は成立しない。");
            Assert.IsFalse(state.IsAttacking);
        }

        [Test]
        public void Begin_ClampsNegativeSeconds()
        {
            var state = new CompanionAttackState();

            Assert.IsTrue(state.Begin(-1f, Active, -1f), "負の秒数は 0 に丸める。");
            Assert.AreEqual(Active, state.TotalSeconds, 1e-4f);
        }

        // ---- 進行 ----

        [Test]
        public void Tick_AdvancesThroughPhasesInOrder()
        {
            CompanionAttackState state = Begun();

            state.Tick(Startup - 0.01f);
            Assert.AreEqual(CompanionAttackPhase.Startup, state.Phase);

            state.Tick(0.02f); // 予兆を越える。
            Assert.AreEqual(CompanionAttackPhase.Active, state.Phase);
            Assert.IsTrue(state.IsHitboxActive, "判定段でのみ Hitbox を出す。");

            state.Tick(Active);
            Assert.AreEqual(CompanionAttackPhase.Recovery, state.Phase);
            Assert.IsFalse(state.IsHitboxActive, "後隙では判定が消えている。");

            state.Tick(Recovery);
            Assert.AreEqual(CompanionAttackPhase.Idle, state.Phase);
            Assert.IsFalse(state.IsAttacking);
        }

        [Test]
        public void Tick_BoundaryEntersNextPhase()
        {
            CompanionAttackState state = Begun();

            state.Tick(Startup);

            Assert.AreEqual(CompanionAttackPhase.Active, state.Phase, "境界（予兆の終わり）と同時に判定段へ入る。");
        }

        [Test]
        public void Finished_IsTrueForExactlyOneTick()
        {
            CompanionAttackState state = Begun();

            state.Tick(Startup + Active + Recovery);
            Assert.IsTrue(state.Finished, "完了した Tick で 1 回だけ true。");

            state.Tick(0.1f);
            Assert.IsFalse(state.Finished, "翌 Tick では下がっている（クールダウンを二重に開始しない）。");
        }

        [Test]
        public void LargeDeltaTime_DoesNotSkipHitbox()
        {
            var state = new CompanionAttackState();
            state.Begin(Startup, Active, Recovery);

            // フレーム落ちで 1 Tick が予兆をまたぐ。累積経過から段を求めるため、余りを失って後隙へ進むことがない。
            state.Tick(Startup + Active * 0.5f);

            Assert.AreEqual(CompanionAttackPhase.Active, state.Phase,
                "予兆をまたぐ deltaTime でも、判定段の途中として正しく解決される。");
        }

        [Test]
        public void HugeDeltaTime_FinishesWithoutOverrun()
        {
            CompanionAttackState state = Begun();

            state.Tick(100f);

            Assert.AreEqual(CompanionAttackPhase.Idle, state.Phase);
            Assert.IsTrue(state.Finished, "極端な遅延でも 1 回だけ完了を通知する。");
        }

        [Test]
        public void Tick_IgnoresNegativeDeltaTime()
        {
            CompanionAttackState state = Begun();
            state.Tick(0.05f);
            float elapsed = state.Elapsed;

            state.Tick(-1f);

            Assert.AreEqual(elapsed, state.Elapsed, 1e-4f, "負の deltaTime で時間が巻き戻らない。");
        }

        [Test]
        public void Tick_WhileIdle_DoesNothing()
        {
            var state = new CompanionAttackState();

            Assert.DoesNotThrow(() => state.Tick(1f));
            Assert.AreEqual(CompanionAttackPhase.Idle, state.Phase);
            Assert.IsFalse(state.Finished);
        }

        // ---- 中断 ----

        [Test]
        public void Cancel_RemovesHitboxImmediately()
        {
            CompanionAttackState state = Begun();
            state.Tick(Startup);
            Assert.IsTrue(state.IsHitboxActive, "前提：判定中。");

            state.Cancel();

            Assert.IsFalse(state.IsHitboxActive, "中断で判定は即座に消える。");
            Assert.IsFalse(state.IsAttacking);
            Assert.IsFalse(state.Finished, "中断は完了ではない（クールダウンを開始しない）。");
            Assert.AreEqual(0f, state.Elapsed, 1e-4f);
        }

        [Test]
        public void Cancel_AllowsImmediateRestart()
        {
            CompanionAttackState state = Begun();
            state.Cancel();

            Assert.IsTrue(state.Begin(Startup, Active, Recovery), "中断後は再び攻撃を開始できる。");
        }

        [Test]
        public void Cancel_WhileIdle_IsSafe()
        {
            var state = new CompanionAttackState();

            Assert.DoesNotThrow(() => state.Cancel());
        }
    }
}
