using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat
{
    /// <summary>
    /// 射線に味方の敵がいるかを判定する契約（Phase3 P3-08。§9.2「射線に別の敵がいる場合は発射せず位置調整する」）。
    /// 遠距離攻撃の開始前に発射者→対象の直線上に別の敵（味方陣営）がいないか確認する。物理実装はテストで Fake に差し替え可能。
    /// </summary>
    public interface IEnemyFireLineProbe
    {
        /// <summary>発射者（<paramref name="selfActorId"/>）から <paramref name="from"/>→<paramref name="to"/> の直線上に、自分以外の敵がいれば true。</summary>
        bool AllyBlocksLine(Vector3 from, Vector3 to, int selfActorId);
    }

    /// <summary>
    /// 物理 Raycast による <see cref="IEnemyFireLineProbe"/> 実装（Phase3 P3-08）。Enemy レイヤーのみを対象に発射者→対象を走査し、
    /// 自分以外の敵に当たれば射線が塞がれていると判定する。主人公は Player レイヤーのため対象にならない。壁は別途 Projectile 側で消滅する。
    /// </summary>
    public sealed class PhysicsEnemyFireLineProbe : IEnemyFireLineProbe
    {
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private readonly float _height;

        /// <summary>射線の高さ（発射者原点からの +Y。敵コライダー帯に合わせる）。</summary>
        public PhysicsEnemyFireLineProbe(float height = 0.6f)
        {
            _height = height;
        }

        /// <inheritdoc />
        public bool AllyBlocksLine(Vector3 from, Vector3 to, int selfActorId)
        {
            Vector3 a = from + Vector3.up * _height;
            Vector3 b = to + Vector3.up * _height;
            Vector3 dir = b - a;
            float dist = dir.magnitude;
            if (dist < 1e-4f)
            {
                return false;
            }

            int enemyLayer = CombatLayers.EnemyLayer;
            int mask = enemyLayer >= 0 ? 1 << enemyLayer : ~0;
            Physics.SyncTransforms();
            int count = Physics.RaycastNonAlloc(a, dir / dist, _hits, dist, mask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider col = _hits[i].collider;
                if (col == null)
                {
                    continue;
                }

                var actor = col.GetComponentInParent<IDamageable>();
                if (actor != null && actor.DamageableId != selfActorId)
                {
                    return true; // 自分以外の敵が射線を塞いでいる。
                }
            }

            return false;
        }
    }
}
