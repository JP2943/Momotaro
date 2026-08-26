using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-01：被弾リアクションの純粋タイマ <see cref="HitReactionState"/> の境界を検証する。硬直 0.30 秒・被弾後無敵 0.50 秒の
    /// 「直前／一致／直後」を、yield return null の偶然に依存せず決定的な deltaTime で確認する（仕様書 §12 / Table3）。
    /// </summary>
    public sealed class HitReactionStateTests
    {
        [Test]
        public void Initial_NotHurt_NotInvincible()
        {
            var s = new HitReactionState(0.30f, 0.50f);
            Assert.IsFalse(s.IsHurt);
            Assert.IsFalse(s.IsInvincible);
        }

        [Test]
        public void Begin_StartsHurtAndInvincible()
        {
            var s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            Assert.IsTrue(s.IsHurt, "被弾で硬直開始。");
            Assert.IsTrue(s.IsInvincible, "被弾で無敵開始。");
        }

        [Test]
        public void Hurt_BoundaryAt030_JustBefore_On_JustAfter()
        {
            var s = new HitReactionState(0.30f, 0.50f);

            s.Begin();
            s.Tick(0.29f);
            Assert.IsTrue(s.IsHurt, "0.30 秒直前（0.29）は硬直中。");

            s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            s.Tick(0.30f);
            Assert.IsFalse(s.IsHurt, "0.30 秒一致で硬直終了。");

            s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            s.Tick(0.31f);
            Assert.IsFalse(s.IsHurt, "0.30 秒直後（0.31）は硬直終了。");
        }

        [Test]
        public void Invincible_BoundaryAt050_JustBefore_On_JustAfter()
        {
            var s = new HitReactionState(0.30f, 0.50f);

            s.Begin();
            s.Tick(0.49f);
            Assert.IsTrue(s.IsInvincible, "0.50 秒直前（0.49）は無敵中。");

            s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            s.Tick(0.50f);
            Assert.IsFalse(s.IsInvincible, "0.50 秒一致で無敵終了。");

            s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            s.Tick(0.51f);
            Assert.IsFalse(s.IsInvincible, "0.50 秒直後（0.51）は無敵終了。");
        }

        [Test]
        public void HurtEndsBeforeInvincibility_BetweenWindow_RecoveredButStillInvincible()
        {
            var s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            s.Tick(0.40f); // 0.30 < 0.40 < 0.50
            Assert.IsFalse(s.IsHurt, "0.40 秒では硬直は解けている。");
            Assert.IsTrue(s.IsInvincible, "0.40 秒でも無敵は継続する。");
        }

        [Test]
        public void IncrementalTicks_MatchSingleTick_AtBoundary()
        {
            var inc = new HitReactionState(0.30f, 0.50f);
            inc.Begin();
            for (int i = 0; i < 3; i++)
            {
                inc.Tick(0.10f); // 合計 0.30（float 加算のため厳密には僅かな残差が出うる）
            }

            var single = new HitReactionState(0.30f, 0.50f);
            single.Begin();
            single.Tick(0.30f);

            // 小分けと単一 Tick は概ね一致し、硬直はほぼ終了する（残差は 1ms 未満で実フレームなら次 Tick で確実に 0）。
            Assert.AreEqual(single.HurtRemaining, inc.HurtRemaining, 1e-3f, "小分けと単一 Tick は概ね一致。");
            Assert.LessOrEqual(inc.HurtRemaining, 1e-3f, "小分け合計 0.30 で硬直はほぼ終了（残差 1ms 未満）。");
            Assert.IsTrue(inc.IsInvincible, "0.30 では無敵は継続（0.50 まで）。");
        }

        [Test]
        public void Begin_RefreshesTimersFromFull()
        {
            var s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            s.Tick(0.20f);
            s.Begin(); // 再被弾でリフレッシュ
            s.Tick(0.20f);
            Assert.IsTrue(s.IsHurt, "再被弾で硬直は満タンから再計時（0.20 経過では継続）。");
        }

        [Test]
        public void PausedTick_DoesNotAdvance()
        {
            var s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            s.Tick(0f);
            s.Tick(-1f);
            Assert.IsTrue(s.IsHurt, "deltaTime 0/負では進行しない（Pause 相当）。");
            Assert.IsTrue(s.IsInvincible);
        }

        [Test]
        public void Reset_ClearsBothTimers()
        {
            var s = new HitReactionState(0.30f, 0.50f);
            s.Begin();
            s.Reset();
            Assert.IsFalse(s.IsHurt);
            Assert.IsFalse(s.IsInvincible);
        }
    }
}
