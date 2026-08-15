using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>
    /// Projectile の表示側が発射方向を受け取る契約（Phase3 P3-08 受入修正）。直線弾は発射時に方向が確定するため、
    /// <see cref="EnemyProjectile"/> は初期化直後に本メソッドで確定方向を渡す（毎フレームの読み取りに依存せず、生成フレームの
    /// 初期化順・既定値に左右されない）。Gameplay は Presentation の具象へ依存せず、この契約越しに通知する。
    /// </summary>
    public interface IProjectileVisual
    {
        /// <summary>発射方向（XZ 正規化）を受け取り、4 方向スプライト等の表示へ反映する。</summary>
        void OnProjectileLaunched(Vector3 direction);
    }
}
