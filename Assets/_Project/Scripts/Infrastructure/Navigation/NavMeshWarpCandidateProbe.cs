using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Momotaro.Infrastructure.Navigation
{
    /// <summary>
    /// ワープ候補 1 点が安全かを、物理と NavMesh で答える（P5-05。仕様書 v1.1 §10.2）。
    ///
    /// 3 つを別々に見る。
    /// <list type="number">
    /// <item><description><b>床か</b>：NavMesh 上へ寄せられるか。</description></item>
    /// <item><description><b>塞がっていないか</b>：その場に体の太さぶんの球を置いて、壁・水・閉門と重ならないか。</description></item>
    /// <item><description><b>主人公と繋がっているか</b>：主人公まで <c>PathComplete</c> で届くか。</description></item>
    /// </list>
    ///
    /// 3 つ目が §10.2 の要。経路計算が失敗したからといって、
    /// <b>閉じた門の未開通側へ先回りさせない</b>。「近い」と「行ける」は違う。
    /// </summary>
    public sealed class NavMeshWarpCandidateProbe : IWarpCandidateProbe
    {
        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly float _sampleDistance;
        private readonly float _bodyRadius;
        private readonly int _blockingMask;

        /// <param name="sampleDistance">NavMesh 上へ寄せるときの許容距離（m）。</param>
        /// <param name="bodyRadius">体の太さ（重なり判定の半径）。</param>
        public NavMeshWarpCandidateProbe(float sampleDistance = 0.6f, float bodyRadius = 0.35f)
        {
            _sampleDistance = sampleDistance > 0f ? sampleDistance : 0.6f;
            _bodyRadius = bodyRadius > 0f ? bodyRadius : 0.35f;

            // 壁と同じレイヤーを塞がりとして見る。水の境界・閉じた門も同じレイヤーに置く
            // （§3.3／§7.3 で「通行止めは透明な境界で行う」としてあるため）。
            int wall = CombatLayers.WallLayer;
            _blockingMask = wall >= 0 ? 1 << wall : 0;
        }

        /// <inheritdoc />
        public bool IsOnNavigableGround(Vector3 position) =>
            NavMesh.SamplePosition(position, out _, _sampleDistance, NavMesh.AllAreas);

        /// <inheritdoc />
        public bool IsBlocked(Vector3 position)
        {
            if (_blockingMask == 0)
            {
                return false;
            }

            Physics.SyncTransforms();

            // 床を拾わないよう少し浮かせる。
            //
            // <b>Trigger は見ない。</b> このプロジェクトでは通行止めは必ず<b>実体のある Collider</b>で置く
            // （壁・水の境界・閉じた門。§3.3／§7.3）。Trigger になっているのは受付の方——
            // 出入口（<c>AreaExitGate</c>）と戦闘開始（<c>AreaEncounterTrigger</c>）——で、
            // どちらも通れる床の上にある。Trigger まで塞がりとして数えると、
            // <b>出入口と遭遇点の周りが丸ごと壁になり</b>、そこへは一歩も置けなくなる
            // （P08 で実際に踏んだ：アリーナ内へ犬丸を入れられず、戦闘が始められなかった）。
            Vector3 center = position + new Vector3(0f, _bodyRadius + 0.05f, 0f);
            return Physics.CheckSphere(center, _bodyRadius, _blockingMask, QueryTriggerInteraction.Ignore);
        }

        /// <inheritdoc />
        public bool IsConnectedToLeader(Vector3 position, Vector3 leaderPosition)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit from, _sampleDistance, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(leaderPosition, out NavMeshHit to, _sampleDistance, NavMesh.AllAreas))
            {
                return false;
            }

            // Partial では繋がっていない（途中までしか行けない＝別の通行可能側）。
            return NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _path)
                   && _path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
