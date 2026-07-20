namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の Gameplay 状態（Phase1 P1-06/P1-07）。Animator の State ではなくこちらを正本とする。
    /// Phase 1 では Idle/Move を実動作させ、Guard 系はガード移動（P1-07）で用いる。
    /// 攻撃・被弾・死亡は Phase 1 では追加しない。
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
    }
}
