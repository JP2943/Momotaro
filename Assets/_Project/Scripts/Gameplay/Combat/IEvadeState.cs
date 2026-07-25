namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾側の無敵（回避）状態を表す読み取り専用契約（Phase2 P2-09。仕様書 §10）。命中解決側（<see cref="IDamageable"/> 実装）が
    /// 「今 無敵か」を取得し、無敵中の命中を <see cref="HitResultKind.Evade"/> として解決する。無敵は Renderer の点滅ではなく
    /// この明示状態で評価する。ステップ回避の I-frame（<c>StepState.IsInvincible</c>）が Player 側で供給する。
    ///
    /// Input System / Animator / Scene には依存しない。Phase 3 の敵のステップ無敵にも同じ契約で発展できる。
    /// </summary>
    public interface IEvadeState
    {
        /// <summary>いま無敵（回避）中か。</summary>
        bool IsInvincible { get; }
    }
}
