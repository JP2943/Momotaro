using System;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の状態機械（Phase1 P1-06）。入力の有無・移動量・ガードから状態を決定し、
    /// 同一状態への再入は抑制する。純粋クラスでありテスト可能。Motor とは責務を分離する。
    /// </summary>
    public sealed class PlayerStateMachine
    {
        private float _attackRemaining;

        /// <summary>現在の状態。</summary>
        public PlayerState Current { get; private set; } = PlayerState.Idle;

        /// <summary>攻撃状態が継続中か（Phase2 P2-02）。</summary>
        public bool IsAttacking => _attackRemaining > 0f;

        /// <summary>状態が変化した瞬間のみ発火する。</summary>
        public event Action<PlayerState> StateChanged;

        /// <summary>
        /// 入力状況から状態を更新する（Phase1 互換のオーバーロード）。攻撃は発生しない。
        /// </summary>
        /// <param name="enabled">操作が有効か（非 Gameplay/Disable 時は false）。</param>
        /// <param name="isMoving">移動入力があるか。</param>
        /// <param name="guarding">ガード保持中か。</param>
        public void Tick(bool enabled, bool isMoving, bool guarding)
        {
            Tick(enabled, isMoving, guarding, attackRequested: false, deltaTime: 0f, attackDuration: 0f);
        }

        /// <summary>
        /// 入力状況から状態を更新する（Phase2 P2-02）。攻撃は移動・ガードより優先し、開始すると
        /// <paramref name="attackDuration"/> 秒だけ <see cref="PlayerState.Attack"/> を維持する。
        /// 攻撃中は新規の攻撃要求を受け付けない（Hitbox・3 段コンボ・キャンセル窓は P2-03B）。
        /// 無効時は攻撃タイマーを含めて Idle に落とす。
        /// </summary>
        /// <param name="enabled">操作が有効か（非 Gameplay/Disable 時は false）。</param>
        /// <param name="isMoving">移動入力があるか。</param>
        /// <param name="guarding">ガード保持中か。</param>
        /// <param name="attackRequested">この Tick で攻撃実行が要求されたか（先行入力の消費結果）。</param>
        /// <param name="deltaTime">前回からの経過秒。</param>
        /// <param name="attackDuration">攻撃状態を維持する秒数（試作値・Data 化）。</param>
        public void Tick(bool enabled, bool isMoving, bool guarding, bool attackRequested, float deltaTime, float attackDuration)
        {
            PlayerState next;
            if (!enabled)
            {
                _attackRemaining = 0f;
                next = PlayerState.Idle;
            }
            else
            {
                if (_attackRemaining > 0f)
                {
                    _attackRemaining -= deltaTime;
                    if (_attackRemaining < 0f)
                    {
                        _attackRemaining = 0f;
                    }
                }

                if (_attackRemaining <= 0f && attackRequested && attackDuration > 0f)
                {
                    _attackRemaining = attackDuration;
                }

                if (_attackRemaining > 0f)
                {
                    next = PlayerState.Attack;
                }
                else if (guarding)
                {
                    next = isMoving ? PlayerState.GuardMove : PlayerState.GuardIdle;
                }
                else
                {
                    next = isMoving ? PlayerState.Move : PlayerState.Idle;
                }
            }

            if (next == Current)
            {
                return;
            }

            Current = next;
            StateChanged?.Invoke(next);
        }

        /// <summary>状態を Idle へ戻す（Disable 時のリセット用）。攻撃タイマーも解除する。変化した場合のみ通知する。</summary>
        public void Reset()
        {
            _attackRemaining = 0f;
            if (Current == PlayerState.Idle)
            {
                return;
            }

            Current = PlayerState.Idle;
            StateChanged?.Invoke(Current);
        }
    }
}
