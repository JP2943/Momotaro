using Momotaro.Gameplay.Enemy.Defense;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-10：敵回避の純粋ロジック（§9「短い無敵、CD 3〜5秒、連続不可」）。<see cref="EnemyEvadeAbility"/> の無敵窓・Cooldown・
    /// 連続不可を決定的に検証する。危険刺激の観測・退避移動は Controller の責務で、本テストは能力状態のみを対象とする。
    /// </summary>
    public sealed class EnemyEvadeAbilityTests
    {
        [Test]
        public void Evade_HasInvulnerableWindow_ThenCooldown()
        {
            var e = new EnemyEvadeAbility(cooldownSeconds: 4f, invulnerableSeconds: 0.3f);
            Assert.IsTrue(e.IsReady);
            Assert.IsTrue(e.TryStart());
            Assert.IsTrue(e.IsInvulnerable, "回避直後は無敵。");

            e.Tick(0.2f);
            Assert.IsTrue(e.IsInvulnerable, "無敵時間内は無敵継続。");
            e.Tick(0.15f); // 合計 0.35 ≥ 0.3 で無敵終了。
            Assert.IsFalse(e.IsInvulnerable, "無敵時間経過で無敵終了。");
            Assert.IsFalse(e.IsEvading);
            Assert.AreEqual(4f, e.CooldownRemaining, 1e-3f, "無敵終了で Cooldown 開始。");
        }

        [Test]
        public void Evade_NoConsecutive_UntilCooldownEnds()
        {
            var e = new EnemyEvadeAbility(cooldownSeconds: 4f, invulnerableSeconds: 0.3f);
            e.TryStart();
            Assert.IsFalse(e.TryStart(), "回避中は連続回避不可。");
            e.Tick(0.3f); // 無敵終了→Cooldown。
            Assert.IsFalse(e.IsReady, "Cooldown 中は再回避不可（連続不可）。");
            Assert.IsFalse(e.TryStart());

            e.Tick(3.99f);
            Assert.IsFalse(e.IsReady);
            e.Tick(0.02f);
            Assert.IsTrue(e.IsReady, "Cooldown 明けで再回避可。");
            Assert.IsTrue(e.TryStart());
        }

        [Test]
        public void Evade_Reset_ClearsState()
        {
            var e = new EnemyEvadeAbility(4f, 0.3f);
            e.TryStart();
            e.Reset();
            Assert.IsFalse(e.IsInvulnerable);
            Assert.IsFalse(e.IsEvading);
            Assert.IsTrue(e.IsReady);
        }
    }
}
