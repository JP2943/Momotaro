using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// 認識対象の契約（Phase3 §4）。敵が感知しうる対象（主人公・将来の仲間）が実装する。位置は毎回読み取り可能で、
    /// 敵は Find* を使わず <see cref="PerceptionTargetRegistry"/> 経由で対象を得る（§0.2）。
    /// </summary>
    public interface IPerceptionTarget
    {
        /// <summary>対象の Actor 同定 ID。</summary>
        int ActorId { get; }
        /// <summary>陣営。</summary>
        CombatFaction Faction { get; }
        /// <summary>現在位置。</summary>
        Vector3 Position { get; }
        /// <summary>感知対象として有効か。</summary>
        bool IsActive { get; }
    }

    /// <summary>
    /// 認識対象のレジストリ（Phase3 §0.2「Find* を使わず Registry を優先」）。対象が自己登録し、敵は最寄りの敵対対象を得る。
    /// 静的だがテストで <see cref="Clear"/> できる。iteration は登録数（少数）に比例し毎フレーム確保を行わない。
    /// </summary>
    public static class PerceptionTargetRegistry
    {
        private static readonly List<IPerceptionTarget> _targets = new List<IPerceptionTarget>();

        /// <summary>登録数。</summary>
        public static int Count => _targets.Count;

        /// <summary>対象を登録する（重複登録はしない）。</summary>
        public static void Register(IPerceptionTarget target)
        {
            if (target != null && !_targets.Contains(target))
            {
                _targets.Add(target);
            }
        }

        /// <summary>対象の登録を解除する。</summary>
        public static void Unregister(IPerceptionTarget target) => _targets.Remove(target);

        /// <summary>全登録を消去する（テスト用）。</summary>
        public static void Clear() => _targets.Clear();

        /// <summary>敵対関係（観測者→対象）。敵は主人公・仲間を感知し、味方同士や中立は感知しない。</summary>
        public static bool IsHostile(CombatFaction observer, CombatFaction target)
        {
            if (observer == CombatFaction.Enemy)
            {
                return target == CombatFaction.Player || target == CombatFaction.Ally;
            }

            if (observer == CombatFaction.Player || observer == CombatFaction.Ally)
            {
                return target == CombatFaction.Enemy;
            }

            return false;
        }

        /// <summary>
        /// 観測者に対する最寄りの敵対・有効対象を返す（XZ 平面距離）。存在しなければ false。
        /// </summary>
        public static bool TryGetNearestHostile(Vector3 observerPos, CombatFaction observerFaction, out IPerceptionTarget nearest)
        {
            nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < _targets.Count; i++)
            {
                IPerceptionTarget t = _targets[i];
                if (t == null || !t.IsActive || !IsHostile(observerFaction, t.Faction))
                {
                    continue;
                }

                float d = VisionCheck.PlanarDistance(observerPos, t.Position);
                if (d < best)
                {
                    best = d;
                    nearest = t;
                }
            }

            return nearest != null;
        }
    }
}
