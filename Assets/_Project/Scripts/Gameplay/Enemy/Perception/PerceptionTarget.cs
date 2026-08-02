using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Threat;
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

        /// <summary>
        /// 観測者に対する在圏の敵対・有効な脅威対象（<see cref="IThreatTarget"/>）を <paramref name="buffer"/> へ収集する（Phase3 P3-06）。
        /// <paramref name="maxRange"/> 以内（XZ 平面）のみ。<paramref name="maxRange"/> ≤ 0 は距離無制限。範囲外・離脱は含めないことで
        /// 脅威テーブルの「即時無効化」の入力になる。毎フレーム確保を避けるため <paramref name="buffer"/> を再利用し、先頭で Clear する。
        /// </summary>
        public static void CollectHostileThreatTargets(
            Vector3 observerPos, CombatFaction observerFaction, float maxRange, List<IThreatTarget> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();
            bool limited = maxRange > 0f;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (!(_targets[i] is IThreatTarget t) || !t.IsActive || !IsHostile(observerFaction, t.Faction))
                {
                    continue;
                }

                if (limited && VisionCheck.PlanarDistance(observerPos, t.Position) > maxRange)
                {
                    continue; // 範囲外は候補から除外（脅威テーブルで即時切替に至る）。
                }

                buffer.Add(t);
            }
        }

        /// <summary>
        /// 攻撃者（<see cref="ICombatActor"/>）に対応する登録済み脅威対象を <b>確実に本人へ</b>解決する（Phase3 P3-06 受入修正 req6）。
        /// 位置的な近さではなく、攻撃者と脅威対象が同一 Transform ルート（＝同一エンティティ）であることで対応付ける。これにより
        /// 主人公と仲間が近接していても、実際に攻撃した本人へヘイトが加算され、Phase 4 の犬・猿・雉でも誤帰属しない。攻撃者が
        /// Component でない（＝ Transform を持たない Fake 等）場合は解決しない。脅威対象は戦闘 Actor と同一ルートに同居する想定。
        /// </summary>
        public static bool TryResolveThreatTarget(ICombatActor attacker, out IThreatTarget resolved)
        {
            resolved = null;
            if (!(attacker is Component attackerComponent))
            {
                return false; // Transform を持たない攻撃者は本人対応付け不可。
            }

            Transform attackerRoot = attackerComponent.transform.root;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (!(_targets[i] is IThreatTarget t) || !(t is Component targetComponent))
                {
                    continue;
                }

                if (targetComponent.transform.root == attackerRoot)
                {
                    resolved = t; // 同一ルート＝攻撃者本人のエンティティ。
                    return true;
                }
            }

            return false;
        }
    }
}
