using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Defense;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-10：敵ガードの純粋ロジック（§9「正面180度、HP90%軽減、Poise×1.5、最大2秒、CD3秒、背後不可、Special貫通」）。
    /// <see cref="EnemyGuardMath"/> の方向・Special 境界と、<see cref="EnemyGuardAbility"/> の時間・Cooldown を決定的に検証する。
    /// </summary>
    public sealed class EnemyGuardTests
    {
        private static readonly Vector3 Forward = Vector3.forward; // 敵は +Z を向く。

        private static HitInfo Hit(Vector3 attackDirection, float defenseIgnoreRatio = 0f)
        {
            // AttackDirection＝攻撃者→対象。前方から来る攻撃は -Z（攻撃者が +Z 側）、背後からは +Z。
            return new HitInfo(null, null, attackDirection, Vector3.zero, new HitDamage(100f, 20f, 0f),
                0f, 0f, guardable: true, justGuardable: true, isJustGuardCounter: false,
                defenseIgnoreRatio: defenseIgnoreRatio, stunHpMultiplierOverride: 0f, HitId.Single(1));
        }

        [Test]
        public void Guard_FrontAttack_Reduces90PercentHp_AndAmplifiesPoise()
        {
            HitInfo front = Hit(new Vector3(0, 0, -1)); // 前方（+Z 側）から。
            bool within = EnemyGuardMath.IsWithinFrontArc(Forward, front);
            Assert.IsTrue(within, "前方 180°以内。");

            EnemyGuardMath.Result r = EnemyGuardMath.Resolve(isGuarding: true, within, EnemyGuardMath.IsSpecialPierce(front));
            Assert.IsTrue(r.Guarded);
            Assert.AreEqual(0.1f, r.HpScale, 1e-4f, "HP 90% 軽減（×0.1）。");
            Assert.AreEqual(1.5f, r.PoiseScale, 1e-4f, "被体幹 ×1.5。");
        }

        [Test]
        public void Guard_BackAttack_Pierces()
        {
            HitInfo back = Hit(new Vector3(0, 0, 1)); // 背後（-Z 側）から。
            bool within = EnemyGuardMath.IsWithinFrontArc(Forward, back);
            Assert.IsFalse(within, "背後は前方 180°外。");

            EnemyGuardMath.Result r = EnemyGuardMath.Resolve(true, within, EnemyGuardMath.IsSpecialPierce(back));
            Assert.IsFalse(r.Guarded, "背後は貫通。");
            Assert.AreEqual(1f, r.HpScale, 1e-4f);
            Assert.AreEqual(1f, r.PoiseScale, 1e-4f);
        }

        [Test]
        public void Guard_SpecialFromFront_Pierces()
        {
            HitInfo special = Hit(new Vector3(0, 0, -1), defenseIgnoreRatio: 0.5f); // 前方だが必殺技。
            Assert.IsTrue(EnemyGuardMath.IsSpecialPierce(special), "防御一部無視＝Special。");
            EnemyGuardMath.Result r = EnemyGuardMath.Resolve(true, withinFrontArc: true, specialPierces: true);
            Assert.IsFalse(r.Guarded, "Special は正面でも貫通。");
        }

        [Test]
        public void Guard_NotGuarding_Pierces()
        {
            EnemyGuardMath.Result r = EnemyGuardMath.Resolve(isGuarding: false, withinFrontArc: true, specialPierces: false);
            Assert.IsFalse(r.Guarded);
        }

        [Test]
        public void GuardAbility_AutoReleasesAtMaxHold_ThenCooldownBlocks()
        {
            var g = new EnemyGuardAbility(cooldownSeconds: 3f, maxHoldSeconds: 2f);
            Assert.IsTrue(g.IsReady);
            Assert.IsTrue(g.TryStart());
            Assert.IsTrue(g.IsGuarding);

            g.Tick(1.9f);
            Assert.IsTrue(g.IsGuarding, "最大保持前は構え継続。");
            g.Tick(0.2f); // 合計 2.1 ≥ 2.0 で自動解除。
            Assert.IsFalse(g.IsGuarding, "最大保持で自動解除。");
            Assert.IsFalse(g.IsReady, "解除直後は Cooldown 中。");
            Assert.IsFalse(g.TryStart(), "Cooldown 中は再構え不可。");

            g.Tick(3f);
            Assert.IsTrue(g.IsReady, "Cooldown 明けで再構え可。");
            Assert.IsTrue(g.TryStart());
        }

        [Test]
        public void GuardAbility_ManualRelease_StartsCooldown()
        {
            var g = new EnemyGuardAbility(cooldownSeconds: 3f);
            g.TryStart();
            g.Release();
            Assert.IsFalse(g.IsGuarding);
            Assert.AreEqual(3f, g.CooldownRemaining, 1e-4f);
            g.Tick(2.999f);
            Assert.IsFalse(g.IsReady);
            g.Tick(0.002f);
            Assert.IsTrue(g.IsReady);
        }
    }
}
