namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// Phase 3 検証編成（Phase3 P3-12。§4）。固定シナリオ（近接1／遠距離1／強敵1／3体混成）と性能分岐（近接6／混成6／最大8）を
    /// 1 つの語彙へ統合し、編成管理の正本を <see cref="EnemyTestFieldController"/> の 1 箇所に集約する。
    /// </summary>
    public enum EnemyTestFormation
    {
        /// <summary>0 体（全撤収）。</summary>
        Clear = 0,

        /// <summary>近接 1 体。</summary>
        Melee1 = 1,

        /// <summary>遠距離 1 体。</summary>
        Ranged1 = 2,

        /// <summary>強敵 1 体。</summary>
        Elite1 = 3,

        /// <summary>3 体混成（近接 2＋遠距離 1）。</summary>
        Group3 = 4,

        /// <summary>近接 6 体。</summary>
        Melee6 = 5,

        /// <summary>混成 6 体（近接 4＋遠距離 2）。</summary>
        Mixed6 = 6,

        /// <summary>最大 8 体（近接 8）。</summary>
        Max8 = 7,
    }

    /// <summary>編成ごとの内訳（近接／遠距離／強敵の体数）を与える純粋ヘルパ（Phase3 P3-12）。EditMode で決定的に検証できる。</summary>
    public readonly struct EnemyTestComposition
    {
        /// <summary>近接体数。</summary>
        public int Melee { get; }
        /// <summary>遠距離体数。</summary>
        public int Ranged { get; }
        /// <summary>強敵体数。</summary>
        public int Elite { get; }

        /// <summary>総体数。</summary>
        public int Total => Melee + Ranged + Elite;

        public EnemyTestComposition(int melee, int ranged, int elite)
        {
            Melee = melee;
            Ranged = ranged;
            Elite = elite;
        }

        /// <summary>編成から内訳を得る。総数は最大 8 を超えない。</summary>
        public static EnemyTestComposition For(EnemyTestFormation formation)
        {
            switch (formation)
            {
                case EnemyTestFormation.Melee1: return new EnemyTestComposition(1, 0, 0);
                case EnemyTestFormation.Ranged1: return new EnemyTestComposition(0, 1, 0);
                case EnemyTestFormation.Elite1: return new EnemyTestComposition(0, 0, 1);
                case EnemyTestFormation.Group3: return new EnemyTestComposition(2, 1, 0);
                case EnemyTestFormation.Melee6: return new EnemyTestComposition(6, 0, 0);
                case EnemyTestFormation.Mixed6: return new EnemyTestComposition(4, 2, 0);
                case EnemyTestFormation.Max8: return new EnemyTestComposition(8, 0, 0);
                default: return new EnemyTestComposition(0, 0, 0); // Clear
            }
        }
    }
}
