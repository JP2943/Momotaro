using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 敵ガード命中の解決（Phase3 P3-10。§9「Guard：正面180度、HP90%軽減、Poise被ダメージ×1.5、背後不可、Special貫通」）。
    /// プレイヤーのスタミナ式フルブロック（<see cref="GuardResolver"/>）とは別モデルで、敵ガードは HP を 90% 軽減（×0.1）しつつ
    /// 体幹被ダメージを ×1.5 に増やす「削られ受け」。前方 180°（<see cref="GuardGeometry"/> を共用）以外＝背後は貫通し、必殺技
    /// （防御一部無視＝<see cref="HitInfo.DefenseIgnoreRatio"/> &gt; 0）も貫通する。純粋・決定的で EditMode 再現可能。
    /// </summary>
    public static class EnemyGuardMath
    {
        /// <summary>ガード成功時の HP ダメージ倍率（90% 軽減）。</summary>
        public const float GuardedHpScale = 0.1f;

        /// <summary>ガード成功時の被体幹ダメージ倍率（×1.5）。</summary>
        public const float GuardedPoiseScale = 1.5f;

        /// <summary>ガード判定の結果と、適用すべき HP／体幹の倍率。</summary>
        public readonly struct Result
        {
            /// <summary>ガードで防いだ（軽減した）か。false は貫通（通常ダメージ）。</summary>
            public bool Guarded { get; }
            /// <summary>HP ダメージへ乗じる倍率（防御成功で 0.1、貫通で 1.0）。</summary>
            public float HpScale { get; }
            /// <summary>体幹ダメージへ乗じる倍率（防御成功で 1.5、貫通で 1.0）。</summary>
            public float PoiseScale { get; }

            public Result(bool guarded, float hpScale, float poiseScale)
            {
                Guarded = guarded;
                HpScale = hpScale;
                PoiseScale = poiseScale;
            }

            /// <summary>貫通（軽減なし）の結果。</summary>
            public static Result Pierce => new Result(false, 1f, 1f);
        }

        /// <summary>
        /// ガードの成否と倍率を判定する。<paramref name="isGuarding"/> かつ前方 180°以内かつ「Special 貫通でない」ときのみ防御成功。
        /// 背後（<paramref name="withinFrontArc"/>=false）・非ガード中・Special はいずれも貫通。ガード可否（Guardable）は敵ガードでは
        /// 問わない（敵ガードは分類ではなく方向と Special のみで決まる。ガード不能はプレイヤー JG/Guard を封じる仕様で、敵の削られ受け
        /// とは別軸）。
        /// </summary>
        public static Result Resolve(bool isGuarding, bool withinFrontArc, bool specialPierces)
        {
            if (!isGuarding || !withinFrontArc || specialPierces)
            {
                return Result.Pierce;
            }

            return new Result(true, GuardedHpScale, GuardedPoiseScale);
        }

        /// <summary>この命中が敵ガードを貫通する Special か（防御一部無視を持つ＝必殺技）。</summary>
        public static bool IsSpecialPierce(in HitInfo hit)
        {
            return hit.DefenseIgnoreRatio > 0f;
        }

        /// <summary>命中の前方 180°判定（ガード方向＝敵の前方。<see cref="GuardGeometry"/> を共用）。</summary>
        public static bool IsWithinFrontArc(Vector3 guardForward, in HitInfo hit)
        {
            return GuardGeometry.IsWithinGuardArc(guardForward, hit.AttackDirection);
        }
    }
}
