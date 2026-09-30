using System.Collections.Generic;
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
        public bool IsClear(Vector3 from, Vector3 to) => IsClear(from, to, null);

        /// <summary>
        /// <b>指定した Collider を障害物として数えずに</b>直線を見る（工程 P55-14b。GPT 受入 3）。
        ///
        /// スライドの表示経路検査で、<b>いま使っている接続の境界壁だけ</b>を外すために要る
        /// （<see cref="Momotaro.Gameplay.Session.AreaSeamBarrier"/>）。
        /// 境界壁は通常移動を止めるためのもので、遷移そのものは主人公を入口へ配置するので跨がない。
        /// 普通の壁として数えると、接続が必ず「表示経路を安全に作れません」で失敗する。
        ///
        /// <b>外すのは渡された Collider だけ</b>である。レイヤーで一括除外にすると、
        /// 別の接続の境界や通常の壁まで見えなくなる。
        /// </summary>
        public bool IsClear(Vector3 from, Vector3 to, IReadOnlyList<Collider> ignored)
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

            if (ignored == null || ignored.Count == 0)
            {
                if (_radius > 0f)
                {
                    return !Physics.SphereCast(a, _radius, dir / dist, out _, dist, _wallMask, QueryTriggerInteraction.Ignore);
                }

                return !Physics.Raycast(a, dir / dist, dist, _wallMask, QueryTriggerInteraction.Ignore);
            }

            // 除外がある場合は当たりを全部拾って選り分ける。
            // 1 回の遷移につき数回しか通らないので、確実さを採る。
            RaycastHit[] hits = _radius > 0f
                ? Physics.SphereCastAll(a, _radius, dir / dist, dist, _wallMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastAll(a, dir / dist, dist, _wallMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i].collider;
                if (hit == null)
                {
                    continue;
                }

                bool skip = false;
                for (int k = 0; k < ignored.Count; k++)
                {
                    if (ignored[k] != null && ignored[k] == hit)
                    {
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
