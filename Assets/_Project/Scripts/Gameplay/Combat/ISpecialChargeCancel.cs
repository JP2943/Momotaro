namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾時に必殺技チャージを中断させるための最小契約（Phase2 P2-10。仕様書 §3.6「通常被弾で中断」）。命中解決側
    /// （<see cref="IDamageable"/> 実装）が、実ダメージを与えた命中で <see cref="CancelSpecialChargeOnHit"/> を呼ぶ。
    /// チャージ状態の管理は Player 側（<c>PlayerStateController</c>）に閉じる。
    /// </summary>
    public interface ISpecialChargeCancel
    {
        /// <summary>被弾（実ダメージ）により必殺技チャージ中なら中断する。チャージ中でなければ何もしない。</summary>
        void CancelSpecialChargeOnHit();
    }
}
