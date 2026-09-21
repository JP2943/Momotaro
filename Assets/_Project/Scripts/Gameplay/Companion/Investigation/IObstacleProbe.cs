using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// 調査移動の障害物判定（v1.0 §6.2「直線移動と障害物検査」・§12「壁を越えない」）。汎用経路探索は導入しない。
    /// テストは Fake を注入し、実機は <see cref="PhysicsObstacleProbe"/> が壁レイヤーへレイキャストする。
    /// </summary>
    public interface IObstacleProbe
    {
        /// <summary><paramref name="from"/> から <paramref name="to"/> までの直線が遮られていないか。</summary>
        bool IsClear(Vector3 from, Vector3 to);
    }

    /// <summary>壁（<see cref="CombatLayers.WallLayer"/>）に対する直線判定。索敵の視線判定と同じ方式。</summary>
    public sealed class PhysicsObstacleProbe : IObstacleProbe
    {
        private readonly int _wallMask;
        private readonly float _height;
        private readonly float _radius;

        /// <param name="height">判定の高さ（床を拾わないため少し浮かせる）。</param>
        /// <param name="radius">体の太さ（0 ならレイ、正なら球）。</param>
        public PhysicsObstacleProbe(float height = 0.5f, float radius = 0.25f)
        {
            int wallLayer = CombatLayers.WallLayer;
            _wallMask = wallLayer >= 0 ? (1 << wallLayer) : 0;
            _height = height;
            _radius = radius;
        }

        /// <inheritdoc />
        public bool IsClear(Vector3 from, Vector3 to)
        {
            Vector3 a = from; a.y += _height;
            Vector3 b = to; b.y += _height;
            Vector3 dir = b - a;
            float dist = dir.magnitude;
            if (dist < 1e-4f)
            {
                return true;
            }

            Physics.SyncTransforms();
            if (_radius > 0f)
            {
                return !Physics.SphereCast(a, _radius, dir / dist, out _, dist, _wallMask, QueryTriggerInteraction.Ignore);
            }

            return !Physics.Raycast(a, dir / dist, dist, _wallMask, QueryTriggerInteraction.Ignore);
        }
    }
}
