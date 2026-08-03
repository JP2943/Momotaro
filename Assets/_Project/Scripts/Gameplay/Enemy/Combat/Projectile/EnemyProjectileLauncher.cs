using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>
    /// 敵 Projectile の生成器（Phase3 P3-08。§9.2）。直線弾 Prefab を発射者の銃口位置へ生成し、不変 Snapshot・攻撃力・発射者・
    /// HitId で初期化する。Pool 化は将来の最適化（プロトタイプは安全な生成／破棄）。Prefab 未設定時は Development で 1 度警告し発射しない
    /// （Gameplay を止めない）。銃口オフセットは Inspector 調整可。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyProjectileLauncher : MonoBehaviour, IEnemyProjectileLauncher
    {
        [Tooltip("直線弾 Prefab（EnemyProjectile を持つ）。")]
        [SerializeField] private EnemyProjectile _projectilePrefab;

        [Tooltip("銃口の高さ（発射者原点からの +Y。m）。")]
        [SerializeField] private float _muzzleHeight = 1.0f;

        [Tooltip("銃口の前方オフセット（狙い方向へ。m。自コライダーとの初期重なりを避ける）。")]
        [SerializeField] private float _muzzleForward = 0.6f;

        private bool _warned;

        /// <inheritdoc />
        public bool TryLaunch(in EnemyAttackSnapshot snapshot, Vector3 origin, Vector3 direction,
            ICombatActor owner, float attackPower, HitId hitId)
        {
            if (_projectilePrefab == null)
            {
                WarnOnce();
                return false;
            }

            Vector3 dir = direction;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 spawn = origin + Vector3.up * _muzzleHeight + dir * _muzzleForward;

            EnemyProjectile shot = Instantiate(_projectilePrefab, spawn, Quaternion.LookRotation(dir, Vector3.up));
            shot.Initialize(snapshot, spawn, dir, owner, attackPower, hitId);
            return true;
        }

        private void WarnOnce()
        {
            if (_warned)
            {
                return;
            }

            _warned = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("EnemyProjectileLauncher: projectile prefab 未設定のため発射しません。", this);
#endif
        }
    }
}
