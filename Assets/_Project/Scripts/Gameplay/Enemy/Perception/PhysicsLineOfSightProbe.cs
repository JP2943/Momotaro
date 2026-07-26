using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// 物理レイキャストによる視線遮蔽判定（Phase3 §4.1）。壁・地形レイヤー（Default）だけを遮蔽対象とし、主人公の正当な
    /// Hurtbox（Player レイヤー）や自身（Enemy レイヤー）で誤って遮蔽しない。<see cref="ILineOfSightProbe"/> の既定実装で、
    /// テストでは Fake を注入して物理に依存しない。目線高さのオフセットを与えて足元段差での誤遮蔽を避ける。
    /// </summary>
    public sealed class PhysicsLineOfSightProbe : ILineOfSightProbe
    {
        private readonly int _wallMask;
        private readonly float _eyeHeight;

        /// <param name="eyeHeight">レイの目線高さ（m）。</param>
        public PhysicsLineOfSightProbe(float eyeHeight = 1.0f)
        {
            int wallLayer = CombatLayers.WallLayer;
            _wallMask = wallLayer >= 0 ? (1 << wallLayer) : 0;
            _eyeHeight = eyeHeight;
        }

        /// <inheritdoc />
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 a = from; a.y += _eyeHeight;
            Vector3 b = to; b.y += _eyeHeight;
            Vector3 dir = b - a;
            float dist = dir.magnitude;
            if (dist < 1e-4f)
            {
                return true;
            }

            // 壁レイヤーのみを対象にレイキャスト。ヒットすれば遮蔽（視線不通）。Trigger は無視。
            return !Physics.Raycast(a, dir / dist, dist, _wallMask, QueryTriggerInteraction.Ignore);
        }
    }
}
