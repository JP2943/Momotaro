namespace Momotaro.Gameplay.Enemy
{
    /// <summary>敵集団戦の性能検証分岐（Phase3 P3-11。§「性能分岐：近接6、近接4＋遠距離2、最大8体」）。</summary>
    public enum EnemyPerformanceBranch
    {
        /// <summary>近接 6 体。</summary>
        Melee6 = 0,

        /// <summary>近接 4＋遠距離 2 体。</summary>
        Melee4Ranged2 = 1,

        /// <summary>最大 8 体（近接 8）。</summary>
        Max8 = 2,
    }

    /// <summary>
    /// 性能分岐ごとの内訳（近接／遠距離／強敵の体数）を与える純粋ヘルパ（Phase3 P3-11）。Scene・Prefab に依存せず体数だけを決める
    /// （実 Spawn は <c>EnemyPerformanceHarness</c>）。EditMode で分岐と総数（最大 8）を決定的に検証できる。
    /// </summary>
    public readonly struct EnemyPerformanceComposition
    {
        /// <summary>近接体数。</summary>
        public int Melee { get; }
        /// <summary>遠距離体数。</summary>
        public int Ranged { get; }
        /// <summary>強敵体数。</summary>
        public int Elite { get; }

        /// <summary>総体数。</summary>
        public int Total => Melee + Ranged + Elite;

        public EnemyPerformanceComposition(int melee, int ranged, int elite)
        {
            Melee = melee;
            Ranged = ranged;
            Elite = elite;
        }

        /// <summary>分岐から内訳を得る。総数は最大 8 を超えない。</summary>
        public static EnemyPerformanceComposition For(EnemyPerformanceBranch branch)
        {
            switch (branch)
            {
                case EnemyPerformanceBranch.Melee4Ranged2:
                    return new EnemyPerformanceComposition(4, 2, 0);
                case EnemyPerformanceBranch.Max8:
                    return new EnemyPerformanceComposition(8, 0, 0);
                default:
                    return new EnemyPerformanceComposition(6, 0, 0);
            }
        }
    }
}
