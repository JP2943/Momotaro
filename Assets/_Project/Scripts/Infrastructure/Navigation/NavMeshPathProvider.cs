using Momotaro.Gameplay.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Momotaro.Infrastructure.Navigation
{
    /// <summary>
    /// NavMesh で経路を答える（P5-05。仕様書 v1.1 §10.1）。
    ///
    /// <b>NavMeshAgent は使わない。</b> <see cref="NavMesh.CalculatePath"/> と
    /// <see cref="NavMeshPath"/> だけで角の列を取り、移動そのものは既存の
    /// <c>CompanionMovementArbiter</c> → <c>CompanionMotor</c> に任せる。
    /// Agent を置くと位置・速度・向きを自分で書き始めるので、書込み口が 2 つになる。
    ///
    /// <b>始点・終点は NavMesh 上へ寄せてから計算する。</b> 仲間や隊列位置は
    /// NavMesh から少し浮いた床の上にあるのが普通で、そのまま渡すと
    /// 「近いのに経路が出ない」が起きる。寄せられなければ <see cref="PathQueryStatus.Invalid"/>。
    ///
    /// <b>Partial をそのまま返す。</b> 使うかどうかを決めるのは呼び出し側（§10.1）。
    /// ここで Complete に丸めると、届かないことが呼び出し側から見えなくなる。
    /// </summary>
    public sealed class NavMeshPathProvider : IPathProvider
    {
        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly float _sampleDistance;
        private readonly int _areaMask;

        /// <param name="sampleDistance">NavMesh 上へ寄せるときの許容距離（m）。</param>
        /// <param name="areaMask">通行できる領域のマスク。既定はすべて。</param>
        public NavMeshPathProvider(float sampleDistance = 1.5f, int areaMask = NavMesh.AllAreas)
        {
            _sampleDistance = sampleDistance > 0f ? sampleDistance : 1.5f;
            _areaMask = areaMask;
        }

        /// <summary>問い合わせた回数（診断・テスト用）。</summary>
        public int QueryCount { get; private set; }

        /// <inheritdoc />
        public PathQueryResult Query(Vector3 from, Vector3 to)
        {
            QueryCount++;

            if (!TrySnap(from, out Vector3 start) || !TrySnap(to, out Vector3 goal))
            {
                return PathQueryResult.Invalid();
            }

            if (!NavMesh.CalculatePath(start, goal, _areaMask, _path))
            {
                return PathQueryResult.Invalid();
            }

            switch (_path.status)
            {
                case NavMeshPathStatus.PathComplete:
                    return PathQueryResult.Complete(_path.corners);
                case NavMeshPathStatus.PathPartial:
                    return PathQueryResult.Partial(_path.corners);
                default:
                    return PathQueryResult.Invalid();
            }
        }

        private bool TrySnap(Vector3 position, out Vector3 snapped)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, _sampleDistance, _areaMask))
            {
                snapped = hit.position;
                return true;
            }

            snapped = position;
            return false;
        }
    }
}
