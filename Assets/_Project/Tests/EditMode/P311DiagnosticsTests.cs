using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Slots;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-11：仮 UI・Debug・性能検証の純粋ロジックを決定的に検証する。頭上バーモデル（HP／体幹の表示要否）、デバッグ行の
    /// OFF 時 非確保（Debug OFF で文字列を作らない）、性能分岐の内訳（最大 8）、8 体競合時の Slot 上限・回収の健全性。
    /// </summary>
    public sealed class P311DiagnosticsTests
    {
        // ---- 頭上バーモデル ----

        [Test]
        public void OverheadBar_HpFill_Clamped()
        {
            Assert.AreEqual(0.5f, OverheadBarModel.Resolve(20, 40, 40f, 40f, false).HpFill, 1e-4f);
            Assert.AreEqual(0f, OverheadBarModel.Resolve(-5, 40, 40f, 40f, false).HpFill, 1e-4f, "負 HP でも 0。");
            Assert.AreEqual(0f, OverheadBarModel.Resolve(10, 0, 0f, 0f, false).HpFill, 1e-4f, "MaxHp0 でも 0 除算しない。");
        }

        [Test]
        public void OverheadBar_Poise_ShownWhenDamagedOrAlways()
        {
            Assert.IsFalse(OverheadBarModel.Resolve(40, 40, 40f, 40f, false).ShowPoise, "満タン・非常時表示は体幹を出さない。");
            Assert.IsTrue(OverheadBarModel.Resolve(40, 40, 30f, 40f, false).ShowPoise, "被 Poise 中は体幹を出す。");
            Assert.IsTrue(OverheadBarModel.Resolve(40, 40, 40f, 40f, true).ShowPoise, "常時表示（強敵）は満タンでも出す。");
        }

        // ---- デバッグ行：OFF で文字列を確保しない ----

        [Test]
        public void DebugReadout_Disabled_ReturnsNull_NoStringBuilt()
        {
            string s = EnemyDebugReadout.Build(false, EnemyState.Chase, 1, 5f,
                EnemyAttackClass.Heavy, true, 12f, true, PerceptionPhase.Alert, 12f);
            Assert.IsNull(s, "Debug OFF は null（文字列を作らない）。");
        }

        [Test]
        public void DebugReadout_Enabled_ContainsAllFields()
        {
            string s = EnemyDebugReadout.Build(true, EnemyState.Chase, 7, 5.2f,
                EnemyAttackClass.Heavy, true, 12f, true, PerceptionPhase.Alert, 12f);
            Assert.IsNotNull(s);
            StringAssert.Contains("State=", s);
            StringAssert.Contains("Tgt=7", s);
            StringAssert.Contains("Thr=5.2", s);
            StringAssert.Contains("Atk=Heavy", s);
            StringAssert.Contains("Slot=1", s);
            StringAssert.Contains("LOS=Alert", s);
            StringAssert.Contains("R=12", s);
        }

        // ---- 性能分岐 ----

        [Test]
        public void PerformanceFormations_Compositions()
        {
            var m6 = EnemyTestComposition.For(EnemyTestFormation.Melee6);
            Assert.AreEqual(6, m6.Melee);
            Assert.AreEqual(0, m6.Ranged + m6.Elite);
            Assert.AreEqual(6, m6.Total);

            var mr = EnemyTestComposition.For(EnemyTestFormation.Mixed6);
            Assert.AreEqual(4, mr.Melee);
            Assert.AreEqual(2, mr.Ranged);
            Assert.AreEqual(6, mr.Total);

            var max = EnemyTestComposition.For(EnemyTestFormation.Max8);
            Assert.AreEqual(8, max.Total, "最大 8 体。");
            Assert.LessOrEqual(max.Total, 8, "総数は 8 を超えない。");
        }

        // ---- 8 体競合時の Slot 上限・回収 ----

        private sealed class FakeOwner : ISlotOwner
        {
            public int Id;
            public bool Active = true;
            public int SlotOwnerId => Id;
            public bool IsSlotOwnerActive => Active;
        }

        [Test]
        public void EightEnemies_SlotCapacityHolds_AndPruneCleansUp()
        {
            var coord = new AttackSlotCoordinator(SlotCapacities.Default); // 1/1/1。
            var owners = new FakeOwner[8];
            int acquired = 0;
            for (int i = 0; i < 8; i++)
            {
                owners[i] = new FakeOwner { Id = 100 + i };
                if (coord.TryAcquire(owners[i], AttackSlotKind.MeleeNormal))
                {
                    acquired++;
                }
            }

            Assert.AreEqual(1, acquired, "8 体競合でも近接通常 Slot は 1 体のみ取得。");
            Assert.AreEqual(1, coord.ActiveCount(AttackSlotKind.MeleeNormal), "上限を超えない。");

            // 保持者を無効化（Down/Disable 相当）→ 回収で 1 つ空く。
            for (int i = 0; i < 8; i++)
            {
                if (coord.Holds(owners[i].Id))
                {
                    owners[i].Active = false;
                }
            }

            Assert.AreEqual(1, coord.PruneInactive(), "無効 Owner の Slot を回収。");
            Assert.AreEqual(0, coord.ActiveCount(AttackSlotKind.MeleeNormal), "回収で空く（Slot 詰まりなし）。");
            Assert.IsTrue(coord.TryAcquire(owners[7], AttackSlotKind.MeleeNormal), "回収後は次の敵が取得できる。");
        }
    }
}
