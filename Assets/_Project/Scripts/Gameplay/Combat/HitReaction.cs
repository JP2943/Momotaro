using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中に伴う「移動リアクション」の指定値（Phase3.5 P3.5-08A）。攻撃データ（<see cref="Momotaro.Data.Combat.AttackData"/>／
    /// <see cref="Momotaro.Data.Combat.EnemyAttackData"/>）から写し取り、<see cref="HitInfo"/> に載せて被弾側へ運ぶ表示・体感用の
    /// 値である。判定・HP・状態の正本ではなく、被弾側が反応（ヒットバック／ガードバック）を起こすためだけに参照する。
    ///
    /// ・<see cref="HitbackDistance"/>／<see cref="HitbackSeconds"/>：Damage 成立時に被弾者を <c>AttackDirection</c> へ押し出す距離・時間。
    /// ・<see cref="GuardbackDistance"/>：通常 Guard 成立時に防御者を <c>AttackDirection</c> へ押し戻す距離（時間はヒットバック時間を流用）。
    ///   ジャストガードでは呼び出し側が押し戻しを行わない（＝踏み止まり。仕様書 §7.4）。
    /// ・<see cref="IsProjectile"/>：この命中が飛び道具由来か。JG 成立時の近接攻撃者ひるみを「近接のみ」に限定するための判別
    ///   （飛び道具の JG では遠方の射手をひるませない。仕様書 §7.5）。
    /// </summary>
    public readonly struct HitReaction
    {
        /// <summary>Damage 時ヒットバック距離（m）。0 以下で無効。</summary>
        public float HitbackDistance { get; }

        /// <summary>ヒットバック／ガードバックの所要時間（秒）。0 以下で無効。</summary>
        public float HitbackSeconds { get; }

        /// <summary>通常 Guard 時ガードバック距離（m）。0 以下で無効（JG は呼び出し側が 0 とする）。</summary>
        public float GuardbackDistance { get; }

        /// <summary>飛び道具由来の命中か（JG 近接ひるみの対象外判定に用いる）。</summary>
        public bool IsProjectile { get; }

        public HitReaction(float hitbackDistance, float hitbackSeconds, float guardbackDistance, bool isProjectile)
        {
            HitbackDistance = hitbackDistance;
            HitbackSeconds = hitbackSeconds;
            GuardbackDistance = guardbackDistance;
            IsProjectile = isProjectile;
        }

        /// <summary>反応なし（既定）。</summary>
        public static HitReaction None => default;
    }
}
