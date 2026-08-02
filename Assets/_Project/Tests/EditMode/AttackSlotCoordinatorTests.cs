using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy.Slots;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-07：攻撃 Slot 調停 <see cref="AttackSlotCoordinator"/> の検証（§8.1）。同時要求・優先順（先着）・解放・二重解放・
    /// Owner 無効化の回収・別 Encounter 分離・None 無制限・冪等取得を決定的に確認する。純粋・再現可能。
    /// </summary>
    public sealed class AttackSlotCoordinatorTests
    {
        private sealed class FakeOwner : ISlotOwner
        {
            public int SlotOwnerId { get; set; }
            public bool IsSlotOwnerActive { get; set; } = true;
        }

        private static AttackSlotCoordinator Coord(int melee = 1, int strong = 1, int ranged = 1)
            => new AttackSlotCoordinator(new SlotCapacities(melee, strong, ranged));

        [Test]
        public void Acquire_UpToCapacity_ThenDenies()
        {
            var c = Coord(melee: 2);
            var a = new FakeOwner { SlotOwnerId = 1 };
            var b = new FakeOwner { SlotOwnerId = 2 };
            var d = new FakeOwner { SlotOwnerId = 3 };
            Assert.IsTrue(c.TryAcquire(a, AttackSlotKind.MeleeNormal));
            Assert.IsTrue(c.TryAcquire(b, AttackSlotKind.MeleeNormal));
            Assert.IsFalse(c.TryAcquire(d, AttackSlotKind.MeleeNormal), "上限超は拒否。");
            Assert.AreEqual(2, c.ActiveCount(AttackSlotKind.MeleeNormal));
        }

        [Test]
        public void Simultaneous_FirstComeWins()
        {
            var c = Coord(melee: 1);
            var a = new FakeOwner { SlotOwnerId = 1 };
            var b = new FakeOwner { SlotOwnerId = 2 };
            Assert.IsTrue(c.TryAcquire(a, AttackSlotKind.MeleeNormal));
            Assert.IsFalse(c.TryAcquire(b, AttackSlotKind.MeleeNormal), "先着優先で 1 体まで。");
            Assert.IsTrue(c.Holds(1));
            Assert.IsFalse(c.Holds(2));
        }

        [Test]
        public void DifferentKinds_AreIndependent()
        {
            var c = Coord(melee: 1, strong: 1, ranged: 1);
            var a = new FakeOwner { SlotOwnerId = 1 };
            var b = new FakeOwner { SlotOwnerId = 2 };
            var d = new FakeOwner { SlotOwnerId = 3 };
            Assert.IsTrue(c.TryAcquire(a, AttackSlotKind.MeleeNormal));
            Assert.IsTrue(c.TryAcquire(b, AttackSlotKind.Strong), "近接が埋まっても強は別枠。");
            Assert.IsTrue(c.TryAcquire(d, AttackSlotKind.Ranged));
        }

        [Test]
        public void NoneKind_AlwaysGrantsAndNotCounted()
        {
            var c = Coord(melee: 0);
            var a = new FakeOwner { SlotOwnerId = 1 };
            Assert.IsTrue(c.TryAcquire(a, AttackSlotKind.None), "None は Slot 不要で常に成功。");
            Assert.IsFalse(c.Holds(1), "None は記録しない。");
            Assert.AreEqual(0, c.ActiveCount(AttackSlotKind.None));
        }

        [Test]
        public void SameOwner_AcquireIsIdempotent()
        {
            var c = Coord(melee: 1);
            var a = new FakeOwner { SlotOwnerId = 1 };
            Assert.IsTrue(c.TryAcquire(a, AttackSlotKind.MeleeNormal));
            Assert.IsTrue(c.TryAcquire(a, AttackSlotKind.MeleeNormal), "同一所有者の再取得は冪等。");
            Assert.AreEqual(1, c.ActiveCount(AttackSlotKind.MeleeNormal), "追加消費しない。");
        }

        [Test]
        public void Release_FreesSlotForNext()
        {
            var c = Coord(melee: 1);
            var a = new FakeOwner { SlotOwnerId = 1 };
            var b = new FakeOwner { SlotOwnerId = 2 };
            c.TryAcquire(a, AttackSlotKind.MeleeNormal);
            Assert.IsFalse(c.TryAcquire(b, AttackSlotKind.MeleeNormal));
            c.Release(1);
            Assert.IsTrue(c.TryAcquire(b, AttackSlotKind.MeleeNormal), "解放後は次の敵が取得できる。");
        }

        [Test]
        public void DoubleRelease_IsSafe()
        {
            var c = Coord(melee: 1);
            var a = new FakeOwner { SlotOwnerId = 1 };
            c.TryAcquire(a, AttackSlotKind.MeleeNormal);
            c.Release(1);
            c.Release(1); // 二重解放
            c.Release(999); // 未保持の解放
            Assert.AreEqual(0, c.ActiveCount(AttackSlotKind.MeleeNormal), "数が壊れない。");
        }

        [Test]
        public void PruneInactive_ReclaimsDisabledOrDownOwners()
        {
            var c = Coord(melee: 1);
            var a = new FakeOwner { SlotOwnerId = 1 };
            var b = new FakeOwner { SlotOwnerId = 2 };
            c.TryAcquire(a, AttackSlotKind.MeleeNormal);
            a.IsSlotOwnerActive = false; // Disable／Down 相当
            int reclaimed = c.PruneInactive();
            Assert.AreEqual(1, reclaimed);
            Assert.AreEqual(0, c.ActiveCount(AttackSlotKind.MeleeNormal));
            Assert.IsTrue(c.TryAcquire(b, AttackSlotKind.MeleeNormal), "回収後に次の敵が取得できる。");
        }

        [Test]
        public void Encounters_AreIsolated()
        {
            var e1 = Coord(melee: 1);
            var e2 = Coord(melee: 1);
            var a = new FakeOwner { SlotOwnerId = 1 };
            var b = new FakeOwner { SlotOwnerId = 2 };
            Assert.IsTrue(e1.TryAcquire(a, AttackSlotKind.MeleeNormal));
            Assert.IsTrue(e2.TryAcquire(b, AttackSlotKind.MeleeNormal), "別 Encounter は Slot を共有しない。");
        }

        [Test]
        public void Reset_ReleasesAll()
        {
            var c = Coord(melee: 2);
            c.TryAcquire(new FakeOwner { SlotOwnerId = 1 }, AttackSlotKind.MeleeNormal);
            c.TryAcquire(new FakeOwner { SlotOwnerId = 2 }, AttackSlotKind.MeleeNormal);
            c.Reset();
            Assert.AreEqual(0, c.ActiveCount(AttackSlotKind.MeleeNormal));
        }

        [Test]
        public void NullOwner_IsRejected()
        {
            var c = Coord(melee: 1);
            Assert.IsFalse(c.TryAcquire(null, AttackSlotKind.MeleeNormal));
        }
    }
}
