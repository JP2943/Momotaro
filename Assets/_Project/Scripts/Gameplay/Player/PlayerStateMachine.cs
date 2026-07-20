using System;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の状態機械（Phase1 P1-06）。入力の有無・移動量・ガードから状態を決定し、
    /// 同一状態への再入は抑制する。純粋クラスでありテスト可能。Motor とは責務を分離する。
    /// </summary>
    public sealed class PlayerStateMachine
    {
        /// <summary>現在の状態。</summary>
        public PlayerState Current { get; private set; } = PlayerState.Idle;

        /// <summary>状態が変化した瞬間のみ発火する。</summary>
        public event Action<PlayerState> StateChanged;

        /// <summary>
        /// 入力状況から状態を更新する。無効時は Idle に落とす。
        /// </summary>
        /// <param name="enabled">操作が有効か（非 Gameplay/Disable 時は false）。</param>
        /// <param name="isMoving">移動入力があるか。</param>
        /// <param name="guarding">ガード保持中か。</param>
        public void Tick(bool enabled, bool isMoving, bool guarding)
        {
            PlayerState next;
            if (!enabled)
            {
                next = PlayerState.Idle;
            }
            else if (guarding)
            {
                next = isMoving ? PlayerState.GuardMove : PlayerState.GuardIdle;
            }
            else
            {
                next = isMoving ? PlayerState.Move : PlayerState.Idle;
            }

            if (next == Current)
            {
                return;
            }

            Current = next;
            StateChanged?.Invoke(next);
        }

        /// <summary>状態を Idle へ戻す（Disable 時のリセット用）。変化した場合のみ通知する。</summary>
        public void Reset()
        {
            if (Current == PlayerState.Idle)
            {
                return;
            }

            Current = PlayerState.Idle;
            StateChanged?.Invoke(Current);
        }
    }
}
