namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 1 回の命中が運ぶ「種別の異なる 3 系統の値」（P2-01）。
    /// HP ダメージ・体幹ダメージ・ひるませ値を型として明確に分離し、取り違えを防ぐ。
    ///
    /// 仕様書との対応：
    /// - <see cref="Hp"/>        … HP ダメージ（3.9。攻撃力・防御・背後補正等の適用は後続タスク）。
    /// - <see cref="Poise"/>    … 体幹ダメージ（3.11。攻撃力・防御の影響を受けない固定系統）。
    /// - <see cref="Flinch"/>   … ひるませ値（3.12。背後・攻撃中等の状況補正の対象外）。
    ///
    /// P2-01 では値の格納と分離のみを担い、算出式（背後 1.1 倍・スタン倍率・防御下限 0.1 など）は実装しない。
    /// </summary>
    public readonly struct HitDamage
    {
        /// <summary>HP ダメージ。</summary>
        public float Hp { get; }

        /// <summary>体幹ダメージ。</summary>
        public float Poise { get; }

        /// <summary>ひるませ値。</summary>
        public float Flinch { get; }

        /// <summary>3 系統の値を指定して生成する。</summary>
        public HitDamage(float hp, float poise, float flinch)
        {
            Hp = hp;
            Poise = poise;
            Flinch = flinch;
        }

        /// <summary>すべて 0 の命中値。</summary>
        public static HitDamage None => new HitDamage(0f, 0f, 0f);
    }
}
