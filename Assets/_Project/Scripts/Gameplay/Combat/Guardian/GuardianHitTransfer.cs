using UnityEngine;

namespace Momotaro.Gameplay.Combat.Guardian
{
    /// <summary>
    /// 肩代わりする命中を守護者向けに再構築する純粋ロジック（P4-01）。攻撃 Snapshot 値（ダメージ・ガード可否・
    /// ステップ可否・<see cref="HitId"/>・<see cref="HitReaction"/>）は原本のまま維持し、「誰が・どこで・どちらから」
    /// だけを守護者基準へ差し替える。
    ///
    /// <list type="bullet">
    /// <item><description><see cref="HitInfo.Target"/> … 守護者へ差し替える。</description></item>
    /// <item><description><see cref="HitInfo.AttackDirection"/> … 攻撃者 → 守護者を World XZ で再計算する
    /// （主人公基準のまま渡すと、守護者が攻撃者と無関係な方向へヒットバックする）。</description></item>
    /// <item><description><see cref="HitInfo.HitPoint"/> … 守護者の位置へ差し替える（Damage VFX が主人公位置に出ない）。</description></item>
    /// </list>
    ///
    /// 攻撃者が不明、または攻撃者と守護者がほぼ同位置で方向を決められない場合は方向を <see cref="Vector3.zero"/> にする。
    /// 被弾側のヒットバックは方向 0 で押し出しを行わない実装のため、ダメージは適用しつつ不定方向へ吹き飛ばない。
    /// </summary>
    public static class GuardianHitTransfer
    {
        /// <summary>方向ベクトルを有効とみなす最小の二乗長（これ未満は方向不定として 0 を返す）。</summary>
        public const float DirectionEpsilonSqr = 1e-6f;

        /// <summary>命中を守護者向けに再構築する。守護者が null の場合は原本をそのまま返す（呼び出し側で弾く前提）。</summary>
        public static HitInfo Rebuild(in HitInfo original, IGuardianReceiver guardian)
        {
            if (guardian == null)
            {
                return original;
            }

            Vector3 position = guardian.WorldPosition;
            Vector3 direction = ResolveDirection(original.Attacker, position);

            return new HitInfo(
                original.Attacker,
                guardian,
                direction,
                position,
                original.Damage,
                original.GuardStaminaDamage,
                original.JustGuardPoiseDamage,
                original.Guardable,
                original.JustGuardable,
                original.IsJustGuardCounter,
                original.DefenseIgnoreRatio,
                original.StunHpMultiplierOverride,
                original.Steppable,
                original.HitId,
                original.Reaction);
        }

        /// <summary>攻撃者 → 守護者の方向を World XZ 平面で求める。攻撃者不明・方向不定なら <see cref="Vector3.zero"/>。</summary>
        public static Vector3 ResolveDirection(ICombatActor attacker, Vector3 guardianPosition)
        {
            if (attacker == null)
            {
                return Vector3.zero;
            }

            Vector3 delta = guardianPosition - attacker.WorldPosition;
            delta.y = 0f;
            return delta.sqrMagnitude < DirectionEpsilonSqr ? Vector3.zero : delta.normalized;
        }
    }
}
