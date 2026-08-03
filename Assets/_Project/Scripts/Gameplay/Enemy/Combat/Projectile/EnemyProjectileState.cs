using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>
    /// 直線 Projectile の純粋な運動・寿命計算（Phase3 P3-08。§9.2）。開始位置・正規化方向・速度・最大飛距離・寿命を保持し、
    /// <see cref="Advance"/> で 1 ステップ進める。壁・命中による消滅は MonoBehaviour 側（<see cref="EnemyProjectile"/>）の物理判定で行い、
    /// 本クラスは最大飛距離超過（<see cref="TraveledBeyondMax"/>）と寿命切れ（<see cref="LifetimeExpired"/>）だけを決定的に判定する。
    /// Unity 非依存で EditMode 再現可能（Vector3 は数学型として使用）。追尾・放物線は対象外（P3-08 は直線のみ）。
    /// </summary>
    public sealed class EnemyProjectileState
    {
        private readonly Vector3 _direction;
        private readonly float _speed;
        private readonly float _maxDistance;
        private readonly float _lifetimeSeconds;

        /// <summary>現在位置。</summary>
        public Vector3 Position { get; private set; }
        /// <summary>これまでの飛距離。</summary>
        public float Traveled { get; private set; }
        /// <summary>生存経過秒。</summary>
        public float Age { get; private set; }

        /// <summary>正規化された進行方向（XZ 平面。y は 0 化する）。</summary>
        public Vector3 Direction => _direction;

        /// <summary>開始位置・方向・速度・最大飛距離・寿命を指定して生成する。</summary>
        public EnemyProjectileState(Vector3 origin, Vector3 direction, float speed, float maxDistance, float lifetimeSeconds)
        {
            Position = origin;
            direction.y = 0f;
            _direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            _speed = Mathf.Max(0f, speed);
            _maxDistance = Mathf.Max(0f, maxDistance);
            _lifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
        }

        /// <summary>1 ステップ前進し、移動後の位置を返す。飛距離・寿命を積算する。</summary>
        public Vector3 Advance(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return Position;
            }

            float step = _speed * deltaTime;
            Position += _direction * step;
            Traveled += step;
            Age += deltaTime;
            return Position;
        }

        /// <summary>最大飛距離を超えたか。</summary>
        public bool TraveledBeyondMax => _maxDistance > 0f && Traveled >= _maxDistance;

        /// <summary>寿命が切れたか。</summary>
        public bool LifetimeExpired => _lifetimeSeconds > 0f && Age >= _lifetimeSeconds;

        /// <summary>飛距離・寿命のいずれかで消滅すべきか。</summary>
        public bool ShouldExpire => TraveledBeyondMax || LifetimeExpired;
    }
}
