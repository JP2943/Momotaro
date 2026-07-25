namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾側のジャストガード受付状態を表す読み取り＋成功通知の最小契約（Phase2 P2-08）。命中解決側（<see cref="IDamageable"/>
    /// 実装）が「今 JG を受け付けているか」を取得し、成立時に <see cref="NotifyJustGuardSuccess"/> で連続成功猶予付与・窓クローズを
    /// 依頼する。受付タイミングの管理は Player 側（<c>PlayerStateController</c> が <see cref="JustGuardState"/> を駆動）に閉じる。
    ///
    /// Input System / Animator / Scene には依存しない。前方 180°判定・体幹反射・スタミナ非消費は命中解決側の責務。
    /// </summary>
    public interface IJustGuardState
    {
        /// <summary>いま JG を受け付けているか（受付窓が開いているか）。</summary>
        bool CanJustGuard { get; }

        /// <summary>JG 成立を通知する（連続成功猶予の付与・受付窓のクローズ）。</summary>
        void NotifyJustGuardSuccess();
    }
}
