using System;
using Momotaro.Data.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Slots
{
    /// <summary>
    /// Encounter 単位の攻撃 Slot 上限（Phase3 §8.1 / Table 12）。同時に各分類の攻撃を開始できる敵の数を制限する。
    /// Phase 3 序盤設定は 近接通常=1／強・ガード不能=1／遠距離=1。将来の中盤設定（近接通常=2 等）は Inspector で調整する。
    /// <see cref="AttackSlotKind.None"/> は Slot 不要（無制限）。コード直書きを避け直列化する。
    /// </summary>
    [Serializable]
    public struct SlotCapacities
    {
        [Tooltip("近接通常の同時攻撃上限（Table12 序盤=1）。")]
        [SerializeField] private int _meleeNormal;

        [Tooltip("強／ガード不能の同時攻撃上限（Table12 序盤=1）。")]
        [SerializeField] private int _strong;

        [Tooltip("遠距離の同時攻撃上限（Table12 序盤=1）。")]
        [SerializeField] private int _ranged;

        /// <summary>近接通常の上限。</summary>
        public int MeleeNormal => _meleeNormal;
        /// <summary>強／ガード不能の上限。</summary>
        public int Strong => _strong;
        /// <summary>遠距離の上限。</summary>
        public int Ranged => _ranged;

        /// <summary>各上限を指定して生成する。</summary>
        public SlotCapacities(int meleeNormal, int strong, int ranged)
        {
            _meleeNormal = meleeNormal;
            _strong = strong;
            _ranged = ranged;
        }

        /// <summary>Table 12 Phase 3 序盤設定（1／1／1）。</summary>
        public static SlotCapacities Default => new SlotCapacities(1, 1, 1);

        /// <summary>分類ごとの上限を返す。<see cref="AttackSlotKind.None"/> は無制限（<see cref="int.MaxValue"/>）。</summary>
        public int CapacityFor(AttackSlotKind kind)
        {
            switch (kind)
            {
                case AttackSlotKind.MeleeNormal: return _meleeNormal;
                case AttackSlotKind.Strong: return _strong;
                case AttackSlotKind.Ranged: return _ranged;
                default: return int.MaxValue; // None＝Slot 不要。
            }
        }
    }
}
