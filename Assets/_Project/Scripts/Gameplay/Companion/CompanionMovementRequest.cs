using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>移動意図の種類。</summary>
    public enum CompanionMoveKind
    {
        /// <summary>意図なし（所有権を手放す）。</summary>
        None = 0,

        /// <summary>目標地点へ歩く。</summary>
        Move = 1,

        /// <summary>その場で止まる。</summary>
        Stop = 2,

        /// <summary>目標地点へ瞬間移動する。</summary>
        Warp = 3,
    }

    /// <summary>
    /// 移動を書く権利の持ち主。数字が大きいほど強い。
    ///
    /// 追従と戦闘が同時に Motor を触っていたころは、どちらが最後に書いたかで挙動が決まっていた。
    /// 「誰の意図か」を明示すれば、<b>古い持ち主の指示が新しい持ち主を上書きしない</b>ことを規則にできる。
    /// </summary>
    public enum CompanionMovementOwner
    {
        /// <summary>誰も持っていない。</summary>
        None = 0,

        /// <summary>追従（主人公について歩く）。</summary>
        Follow = 1,

        /// <summary>
        /// 探索（調査地点まで行って止まる。P4-07A）。追従より強い。
        /// 弱いままだと、隊列から離れた瞬間に追従が引き戻して調査地点へ着けない。
        /// </summary>
        Investigate = 2,

        /// <summary>戦闘（対象へ寄る・攻撃中は止まる）。</summary>
        Combat = 3,

        /// <summary>
        /// 防御（構え・回避）。戦闘より強い（F02c）。
        /// 構えている間に追従や戦闘が向きを書き換えると、<b>受けているはずの方向がずれてガードが素通りする</b>。
        /// ガード判定は <c>_actor.Forward</c> と命中方向の角度で決まるので、向きの所有権を防御が握る必要がある。
        /// </summary>
        Defense = 4,

        /// <summary>強制停止（ひるみ・ダウン・退場・活動停止）。その 1 フレームは何にも譲らない。</summary>
        Forced = 5,
    }

    /// <summary>
    /// 1 つの移動意図。<b>意図であって実行ではない</b>ので、これを出しただけでは Motor は動かない。
    /// 実際に何を実行するかは <see cref="CompanionMovementArbiter"/> が決める。
    /// </summary>
    public readonly struct CompanionMoveRequest
    {
        /// <summary>何をしたいか。</summary>
        public CompanionMoveKind Kind { get; }

        /// <summary>目標地点（<see cref="CompanionMoveKind.Move"/> / <see cref="CompanionMoveKind.Warp"/>）。</summary>
        public Vector3 Target { get; }

        /// <summary>移動速度（m/s）。Data 由来。</summary>
        public float Speed { get; }

        /// <summary>停止半径（m）。Data 由来。</summary>
        public float StopRadius { get; }

        /// <summary>論理的な向きを指定するか。</summary>
        public bool HasFacing { get; }

        /// <summary>向けたい方向（World。長さは問わない）。</summary>
        public Vector3 Facing { get; }

        private CompanionMoveRequest(
            CompanionMoveKind kind, Vector3 target, float speed, float stopRadius, bool hasFacing, Vector3 facing)
        {
            Kind = kind;
            Target = target;
            Speed = speed;
            StopRadius = stopRadius;
            HasFacing = hasFacing;
            Facing = facing;
        }

        /// <summary>目標地点へ歩く。</summary>
        public static CompanionMoveRequest Move(Vector3 target, float speed, float stopRadius) =>
            new CompanionMoveRequest(CompanionMoveKind.Move, target, speed, stopRadius, false, Vector3.zero);

        /// <summary>目標地点へ歩き、進行方向を向く。</summary>
        public static CompanionMoveRequest MoveFacing(
            Vector3 target, float speed, float stopRadius, Vector3 facing) =>
            new CompanionMoveRequest(CompanionMoveKind.Move, target, speed, stopRadius, true, facing);

        /// <summary>止まる。</summary>
        public static CompanionMoveRequest Stop() =>
            new CompanionMoveRequest(CompanionMoveKind.Stop, Vector3.zero, 0f, 0f, false, Vector3.zero);

        /// <summary>止まって指定方向を向く。</summary>
        public static CompanionMoveRequest StopFacing(Vector3 facing) =>
            new CompanionMoveRequest(CompanionMoveKind.Stop, Vector3.zero, 0f, 0f, true, facing);

        /// <summary>瞬間移動する。</summary>
        public static CompanionMoveRequest Warp(Vector3 target) =>
            new CompanionMoveRequest(CompanionMoveKind.Warp, target, 0f, 0f, false, Vector3.zero);

        /// <summary>向きだけ指定する（移動には触れない）。</summary>
        public static CompanionMoveRequest FaceOnly(Vector3 facing) =>
            new CompanionMoveRequest(CompanionMoveKind.None, Vector3.zero, 0f, 0f, true, facing);
    }
}
