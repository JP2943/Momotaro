using Unity.Profiling;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵 AI の主要処理に付ける Profiler Marker（Phase3 P3-11。§「Profiler Marker を Perception、Selection、Threat、Slot、Projectile へ付与」）。
    /// 集団戦の負荷を Profiler で切り分けられるようにする。プロファイル無効時は実質ゼロコストで、Gameplay 挙動を変えない（計測専用）。
    /// </summary>
    public static class EnemyProfilerMarkers
    {
        /// <summary>認識評価（視覚・聴覚・LOS）。</summary>
        public static readonly ProfilerMarker Perception = new ProfilerMarker("Momotaro.Enemy.Perception");

        /// <summary>攻撃選択（候補評価・頻度上限・Score）。</summary>
        public static readonly ProfilerMarker Selection = new ProfilerMarker("Momotaro.Enemy.Selection");

        /// <summary>ヘイト・ターゲット選択。</summary>
        public static readonly ProfilerMarker Threat = new ProfilerMarker("Momotaro.Enemy.Threat");

        /// <summary>攻撃 Slot 調停（取得・回収）。</summary>
        public static readonly ProfilerMarker Slot = new ProfilerMarker("Momotaro.Enemy.Slot");

        /// <summary>Projectile の移動・命中判定。</summary>
        public static readonly ProfilerMarker Projectile = new ProfilerMarker("Momotaro.Enemy.Projectile");
    }
}
