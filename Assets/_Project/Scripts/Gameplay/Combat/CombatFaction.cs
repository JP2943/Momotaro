namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 陣営（P2-01 最小拡張点）。命中の敵味方判定に用いる将来の基点だが、
    /// P2-01 では列挙値と <see cref="ICombatActor.Faction"/> の公開のみを行い、
    /// 敵対関係の解決や実際の命中フィルタは後続 Phase で接続する。
    /// 既存の陣営仕様・コードは存在しないため、ここでは最小限の拡張点だけを定義する（依頼 §9）。
    /// </summary>
    public enum CombatFaction
    {
        /// <summary>主人公。</summary>
        Player = 0,

        /// <summary>仲間（犬・猿・雉など）。</summary>
        Ally = 1,

        /// <summary>敵。</summary>
        Enemy = 2,

        /// <summary>中立（環境・ギミック等）。</summary>
        Neutral = 3,
    }
}
