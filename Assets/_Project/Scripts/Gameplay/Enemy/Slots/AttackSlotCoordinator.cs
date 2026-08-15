using System.Collections.Generic;
using Momotaro.Data.Combat;

namespace Momotaro.Gameplay.Enemy.Slots
{
    /// <summary>
    /// Encounter 単位の攻撃 Slot 調停（Phase3 P3-07。§8.1）。各攻撃分類の同時開始数を <see cref="SlotCapacities"/> で制限し、
    /// 敵は AttackPrepare へ入る直前に取得（<see cref="TryAcquire"/>）、全終了・中断経路で解放（<see cref="Release"/>）する。
    /// 純粋・決定的で Unity 非依存（EditMode 再現可能）。同一所有者の重複取得は冪等、二重解放は安全（数が壊れない）。
    /// Owner 不在（Disable／Down／破棄）の Slot は <see cref="PruneInactive"/> で回収する。Coordinator は Encounter に 1 つで、
    /// 別 Encounter の戦闘と Slot を共有しない（インスタンス分離）。
    /// </summary>
    public sealed class AttackSlotCoordinator
    {
        private readonly struct Lease
        {
            public readonly ISlotOwner Owner;
            public readonly int OwnerId;
            public readonly AttackSlotKind Kind;

            public Lease(ISlotOwner owner, int ownerId, AttackSlotKind kind)
            {
                Owner = owner;
                OwnerId = ownerId;
                Kind = kind;
            }
        }

        private readonly List<Lease> _leases = new List<Lease>(8);
        private SlotCapacities _capacities;

        /// <summary>上限を指定して生成する。</summary>
        public AttackSlotCoordinator(SlotCapacities capacities)
        {
            _capacities = capacities;
        }

        /// <summary>上限を差し替える（Inspector 変更の反映用）。保持中の Lease は維持する。</summary>
        public void Configure(SlotCapacities capacities) => _capacities = capacities;

        /// <summary>指定分類の現在の使用数（テスト／Debug 用）。</summary>
        public int ActiveCount(AttackSlotKind kind)
        {
            int c = 0;
            for (int i = 0; i < _leases.Count; i++)
            {
                if (_leases[i].Kind == kind)
                {
                    c++;
                }
            }

            return c;
        }

        /// <summary>この所有者が Slot を保持しているか。</summary>
        public bool Holds(int ownerId)
        {
            for (int i = 0; i < _leases.Count; i++)
            {
                if (_leases[i].OwnerId == ownerId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Slot 取得を試みる（AttackPrepare 直前）。<see cref="AttackSlotKind.None"/> は Slot 不要で常に成功（記録しない）。
        /// 同一所有者が既に保持していれば冪等に true。空きが無ければ false。取得できたら true。
        /// </summary>
        public bool TryAcquire(ISlotOwner owner, AttackSlotKind kind)
        {
            using var _perf = EnemyProfilerMarkers.Slot.Auto(); // P3-11：Slot 調停の負荷計測。
            if (owner == null)
            {
                return false;
            }

            if (kind == AttackSlotKind.None)
            {
                return true; // Slot を消費しない。
            }

            int ownerId = owner.SlotOwnerId;
            if (Holds(ownerId))
            {
                return true; // 冪等（既保持なら追加消費しない）。
            }

            if (ActiveCount(kind) >= _capacities.CapacityFor(kind))
            {
                return false; // 上限。
            }

            _leases.Add(new Lease(owner, ownerId, kind));
            return true;
        }

        /// <summary>所有者の Slot を解放する（全終了・中断経路）。保持していなくても安全（二重解放で数が壊れない）。</summary>
        public void Release(int ownerId)
        {
            for (int i = _leases.Count - 1; i >= 0; i--)
            {
                if (_leases[i].OwnerId == ownerId)
                {
                    _leases.RemoveAt(i);
                }
            }
        }

        /// <summary>Owner 不在（無効）の Slot を検出・回収する（§8.1）。回収した数を返す。</summary>
        public int PruneInactive()
        {
            int removed = 0;
            for (int i = _leases.Count - 1; i >= 0; i--)
            {
                ISlotOwner o = _leases[i].Owner;
                if (o == null || !o.IsSlotOwnerActive)
                {
                    _leases.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>全 Slot を解放する（Encounter 終了・再試行）。</summary>
        public void Reset() => _leases.Clear();
    }
}
