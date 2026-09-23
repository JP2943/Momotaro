using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Navigation
{
    /// <summary>
    /// 経路計算の結果の種類（P5-05。仕様書 v1.1 §10.1）。
    ///
    /// <b>Partial を成功扱いにしない。</b> 途中までしか繋がっていない経路は「行けるところまで行く」ための
    /// ものであって「行ける」ではない。成功にすると、閉じた門の手前まで歩いては止まり、
    /// 止まったことを停滞と数えてワープ資格へ進む、という筋の悪い流れになる。
    /// </summary>
    public enum PathQueryStatus
    {
        /// <summary>まだ問い合わせていない（初期値）。</summary>
        Pending = 0,

        /// <summary>目的地まで繋がっている。</summary>
        Complete = 1,

        /// <summary>途中までしか繋がっていない。<b>成功ではない。</b></summary>
        Partial = 2,

        /// <summary>経路が得られない（供給元が無い・計算に失敗した）。</summary>
        Invalid = 3,
    }

    /// <summary>
    /// 経路の問い合わせ結果（P5-05。§10.1）。角の列と状態だけを持ち、実際の移動はしない。
    /// </summary>
    public readonly struct PathQueryResult
    {
        private readonly IReadOnlyList<Vector3> _corners;

        private PathQueryResult(PathQueryStatus status, IReadOnlyList<Vector3> corners)
        {
            Status = status;
            _corners = corners;
        }

        /// <summary>結果の種類。</summary>
        public PathQueryStatus Status { get; }

        /// <summary>経路の角（始点を含む供給元の並びをそのまま持つ）。無ければ空。</summary>
        public IReadOnlyList<Vector3> Corners => _corners ?? System.Array.Empty<Vector3>();

        /// <summary>角の数。</summary>
        public int CornerCount => Corners.Count;

        /// <summary>この経路をたどってよいか。<b>Complete だけが true</b>（§10.1）。</summary>
        public bool IsUsable => Status == PathQueryStatus.Complete && CornerCount > 0;

        /// <summary>目的地まで繋がった経路。</summary>
        public static PathQueryResult Complete(IReadOnlyList<Vector3> corners) =>
            new PathQueryResult(PathQueryStatus.Complete, corners);

        /// <summary>途中までの経路（成功ではない。角は診断・表示のために残す）。</summary>
        public static PathQueryResult Partial(IReadOnlyList<Vector3> corners) =>
            new PathQueryResult(PathQueryStatus.Partial, corners);

        /// <summary>経路が得られなかった。</summary>
        public static PathQueryResult Invalid() =>
            new PathQueryResult(PathQueryStatus.Invalid, null);

        /// <summary>まだ問い合わせていない。</summary>
        public static PathQueryResult Pending() =>
            new PathQueryResult(PathQueryStatus.Pending, null);
    }

    /// <summary>
    /// 経路の供給元（P5-05。§10.1 の「狭い問い合わせ契約」）。
    ///
    /// <b>Gameplay はこれしか知らない。</b> 実装は Infrastructure の NavMesh Adapter で、
    /// <c>CompanionFollowController.BindPathProvider</c> から明示注入する。
    /// <c>GetComponent</c> だけに頼らないので、テストは Fake を差して境界条件を決定的に検査できる。
    ///
    /// <b>位置・速度・向きの実書込みはここではしない。</b> それは
    /// <c>CompanionMovementArbiter</c> → <c>CompanionMotor</c> の仕事のままで、
    /// ここは「次にどこへ向かえばよいか」を答えるだけ（§10.1「NavMeshAgent を置かない」）。
    /// </summary>
    public interface IPathProvider
    {
        /// <summary><paramref name="from"/> から <paramref name="to"/> への経路を問い合わせる。</summary>
        PathQueryResult Query(Vector3 from, Vector3 to);
    }
}
