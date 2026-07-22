namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の Gameplay 状態（Phase1 P1-06/P1-07）。Animator の State ではなくこちらを正本とする。
    /// Phase 1 では Idle/Move を実動作させ、Guard 系はガード移動（P1-07）で用いる。
    /// Phase2 P2-02 で <see cref="Attack"/> を追加する（入力・状態のみ。Hitbox・ダメージ・3 段コンボは後続 Task）。
    /// </summary>
    public enum PlayerState
    {
        /// <summary>静止。</summary>
        Idle = 0,

        /// <summary>移動。</summary>
        Move = 1,

        /// <summary>ガード中の静止。</summary>
        GuardIdle = 2,

        /// <summary>ガード中の移動。</summary>
        GuardMove = 3,

        /// <summary>攻撃中（開始時に向き固定。Phase2 P2-02）。</summary>
        Attack = 4,

        /// <summary>
        /// ガードブレイク（Phase2 P2-07）。スタミナ 0 による 1.5 秒の強制行動不能。攻撃・ガード・移動・入力 Buffer を無効化する
        /// 独立状態で、優先度は Attack/Guard/Move/Idle より高い。完成 Animation の接続は対象外（状態のみ）。
        /// </summary>
        GuardBreak = 5,
    }
}
