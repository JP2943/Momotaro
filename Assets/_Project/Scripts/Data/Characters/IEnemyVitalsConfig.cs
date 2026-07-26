namespace Momotaro.Data.Characters
{
    /// <summary>
    /// 敵 Runtime Vitals（HP・体幹・ひるみ・スタン）を構築するための数値契約（Phase3 P3-01）。
    /// 既存の <see cref="EnemyData"/>（雛形）と新しい <see cref="EnemyArchetypeData"/> の双方が実装し、
    /// Gameplay 側の共通 Runtime（EnemyVitals）が同一経路で被弾処理できるようにする（別戦闘系を作らない）。
    /// これは「数値の出所」を抽象化する Adapter であり、実処理は持たない。
    /// </summary>
    public interface IEnemyVitalsConfig
    {
        /// <summary>最大 HP。</summary>
        int MaxHp { get; }

        /// <summary>防御力（HP ダメージ補正に用いる）。</summary>
        float Defense { get; }

        /// <summary>体幹の最大値。</summary>
        float PoiseMax { get; }

        /// <summary>体幹の回復開始遅延（秒）。</summary>
        float PoiseRecoveryDelaySeconds { get; }

        /// <summary>体幹の毎秒回復割合（最大体幹比）。</summary>
        float PoiseRecoveryRatioPerSecond { get; }

        /// <summary>被体幹ダメージ倍率（対象側。1.0=等倍）。</summary>
        float PoiseDamageMultiplier { get; }

        /// <summary>スタン時間（秒）。</summary>
        float StunSeconds { get; }

        /// <summary>ひるみ耐性値（この蓄積以上でひるみ）。</summary>
        float FlinchResistance { get; }
    }
}
