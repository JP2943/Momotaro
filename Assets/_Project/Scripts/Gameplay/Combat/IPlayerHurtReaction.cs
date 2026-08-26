namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾リアクション（Hurt 硬直・被弾後無敵）の被弾側契約（Phase3.5 P3.5-01）。IEvadeState 等と同様に、命中解決
    /// （<see cref="IDamageable"/> 実装）と状態機械（PlayerStateController）が具象へ依存せずに参照するための小さな契約。
    ///
    /// - 命中解決側：<see cref="IsPostHitInvincible"/> が true の間、通常 Damage（ガード不能・Steppable=false を含む）を無効化し、
    ///   実 HP ダメージが 1 以上入ったら <see cref="BeginHurt"/> で Hurt を起動する。
    /// - 状態機械側：<see cref="IsHurt"/> を読み、Hurt を最優先状態として全行動を中立化・凍結する。
    ///
    /// ステップ無敵（<see cref="IEvadeState"/>）とは別系統。ステップ無敵は Steppable な攻撃のみ回避するが、被弾後無敵は
    /// 通常 Damage を種別に依らず無効化する（将来の明示的 InvincibilityBypass は拡張点として未実装）。
    /// </summary>
    public interface IPlayerHurtReaction
    {
        /// <summary>Hurt 硬直（強制行動不能）中か。</summary>
        bool IsHurt { get; }

        /// <summary>被弾後無敵（通常 Damage 無効化）中か。</summary>
        bool IsPostHitInvincible { get; }

        /// <summary>実ダメージ被弾で Hurt 硬直と被弾後無敵を開始する。</summary>
        void BeginHurt();
    }
}
