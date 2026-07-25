using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Vitals;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-06：通常ガードの成否判定とスタミナ適用（<see cref="GuardResolver"/>）を検証する。
    /// ガード中かつ Guardable かつ前方内なら防御成功、背後・ガード不能・非ガードは貫通。防御成功は固定スタミナ消費で、
    /// 残量を超える一撃もその一撃は防御し、スタミナは 0 で止まる（ブレイク自体は P2-07）。
    /// </summary>
    public sealed class GuardResolverTests
    {
        [Test]
        public void Resolve_Guarded_WhenGuardingGuardableAndWithinArc()
        {
            Assert.AreEqual(GuardOutcome.Guarded, GuardResolver.Resolve(true, true, true));
        }

        [Test]
        public void Resolve_Pierced_WhenNotGuarding()
        {
            Assert.AreEqual(GuardOutcome.Pierced, GuardResolver.Resolve(false, true, true), "非ガード中は貫通。");
        }

        [Test]
        public void Resolve_Pierced_WhenUnguardable()
        {
            Assert.AreEqual(GuardOutcome.Pierced, GuardResolver.Resolve(true, false, true), "ガード不能攻撃は貫通。");
        }

        [Test]
        public void Resolve_Pierced_WhenBehindArc()
        {
            Assert.AreEqual(GuardOutcome.Pierced, GuardResolver.Resolve(true, true, false), "背後（前方外）は貫通。");
        }

        [Test]
        public void ApplyGuardStaminaDamage_ConsumesFixedAmount()
        {
            var stamina = new Vital(100);
            int consumed = GuardResolver.ApplyGuardStaminaDamage(stamina, 20f);
            Assert.AreEqual(20, consumed);
            Assert.AreEqual(80, stamina.Current);
        }

        [Test]
        public void ApplyGuardStaminaDamage_OverStamina_GuardsAndStopsAtZero()
        {
            var stamina = new Vital(100, 5); // 残 5
            int consumed = GuardResolver.ApplyGuardStaminaDamage(stamina, 20f);
            Assert.AreEqual(5, consumed, "残量超過でも実消費は残スタミナ分（この一撃は防御）。");
            Assert.AreEqual(0, stamina.Current, "スタミナは 0 で止まる。");
        }

        [Test]
        public void ApplyGuardStaminaDamage_RoundsHalfUp()
        {
            var stamina = new Vital(100);
            int consumed = GuardResolver.ApplyGuardStaminaDamage(stamina, 9.5f);
            Assert.AreEqual(10, consumed, "小数は四捨五入（round-half-up）。");
        }

        [Test]
        public void ApplyGuardStaminaDamage_ZeroCost_NoConsume()
        {
            var stamina = new Vital(100);
            Assert.AreEqual(0, GuardResolver.ApplyGuardStaminaDamage(stamina, 0f));
            Assert.AreEqual(100, stamina.Current);
        }
    }
}
