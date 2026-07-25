using Momotaro.Gameplay.Vitals;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>通常ガード命中の解決結果（Phase2 P2-06）。</summary>
    public enum GuardOutcome
    {
        /// <summary>通常ガードで防御した（HP ダメージ 0、固定スタミナ消費）。</summary>
        Guarded = 0,

        /// <summary>防御されず貫通した（背後・ガード不能・非ガード中など。通常ダメージ解決へ）。</summary>
        Pierced = 1,
    }

    /// <summary>
    /// 通常ガードの成否判定とスタミナ適用の純粋ロジック（Phase2 P2-06。仕様書 §3.2 / §8）。
    ///
    /// 規則：ガード中かつ攻撃が Guardable かつ前方 180°以内なら防御成功（<see cref="GuardOutcome.Guarded"/>）。
    /// 背後攻撃・ガード不能・非ガード中は貫通（<see cref="GuardOutcome.Pierced"/>）。防御成功時は HP ダメージ 0 とし、
    /// 攻撃ごとの固定スタミナダメージを消費する。残量を超える一撃もその一撃自体は防御し、スタミナは 0 で止まる
    /// （ガードブレイク＝行動不能化は P2-07 の担当。ここではスタミナ消費のみ）。JG は本 Task の対象外。
    /// </summary>
    public static class GuardResolver
    {
        /// <summary>
        /// 通常ガードの成否を判定する。
        /// </summary>
        /// <param name="isGuarding">被弾側が通常ガード中か。</param>
        /// <param name="guardable">攻撃が通常ガード可能か（<see cref="HitInfo.Guardable"/>）。</param>
        /// <param name="withinFrontArc">攻撃が前方 180°以内か（<see cref="GuardGeometry.IsWithinGuardArc"/>）。</param>
        public static GuardOutcome Resolve(bool isGuarding, bool guardable, bool withinFrontArc)
        {
            return (isGuarding && guardable && withinFrontArc) ? GuardOutcome.Guarded : GuardOutcome.Pierced;
        }

        /// <summary>
        /// 固定ガードスタミナダメージを <paramref name="stamina"/> へ減算し、実際に減った量（0..残スタミナ）を返す。
        /// 小数は四捨五入（HP と同じ round-half-up）で整数化。負の cost は 0 とみなす。残量超過でも Vital 側で 0 に Clamp。
        /// </summary>
        public static int ApplyGuardStaminaDamage(Vital stamina, float cost)
        {
            if (stamina == null || cost <= 0f)
            {
                return 0;
            }

            int amount = (int)(cost + 0.5f); // round-half-up
            int before = stamina.Current;
            stamina.Change(-amount);
            return before - stamina.Current;
        }
    }
}
