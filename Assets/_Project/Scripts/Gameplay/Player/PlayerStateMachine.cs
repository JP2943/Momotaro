using System;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の状態機械（Phase1 P1-06 / Phase2 P2-02・P2-03B）。入力の有無・移動量・ガード・攻撃中フラグから
    /// 状態を決定し、同一状態への再入は抑制する。純粋クラスでありテスト可能。
    /// 攻撃の時間・段・Hitbox は <see cref="Momotaro.Gameplay.Combat.AttackComboMachine"/>（駆動側）が担い、
    /// ここへは「攻撃中か」を bool で受け取る。
    /// 優先度: ガードブレイク ＞ 攻撃 ＞ ガード ＞ 移動/Idle（ガードブレイクは強制行動不能で最優先。Phase2 P2-07）。
    /// </summary>
    public sealed class PlayerStateMachine
    {
        /// <summary>現在の状態。</summary>
        public PlayerState Current { get; private set; } = PlayerState.Idle;

        /// <summary>状態が変化した瞬間のみ発火する。</summary>
        public event Action<PlayerState> StateChanged;

        /// <summary>
        /// 入力状況から状態を更新する（Phase1 互換のオーバーロード）。攻撃は発生しない。
        /// </summary>
        public void Tick(bool enabled, bool isMoving, bool guarding)
        {
            Tick(enabled, isMoving, guarding, attacking: false);
        }

        /// <summary>
        /// 入力状況と攻撃中フラグから状態を更新する（ガードブレイク・ステップなしのオーバーロード）。
        /// </summary>
        public void Tick(bool enabled, bool isMoving, bool guarding, bool attacking)
        {
            Tick(enabled, isMoving, guarding, attacking, guardBroken: false, stepping: false);
        }

        /// <summary>
        /// 入力状況・攻撃中・ガードブレイクから状態を更新する（ステップなしのオーバーロード）。
        /// </summary>
        public void Tick(bool enabled, bool isMoving, bool guarding, bool attacking, bool guardBroken)
        {
            Tick(enabled, isMoving, guarding, attacking, guardBroken, stepping: false);
        }

        /// <summary>
        /// 入力状況・攻撃中・ガードブレイク・ステップから状態を更新する。優先度は
        /// ガードブレイク ＞ ステップ ＞ 攻撃 ＞ ガード ＞ 移動/Idle（仕様書 §3）。無効時（Disable 等）は行動不能・ステップでなければ Idle。
        /// </summary>
        /// <param name="enabled">操作が有効か（非 Gameplay/Disable 時は false）。</param>
        /// <param name="isMoving">移動入力があるか。</param>
        /// <param name="guarding">ガード保持中か。</param>
        /// <param name="attacking">攻撃中か（コンボ状態機械が判定）。</param>
        /// <param name="guardBroken">ガードブレイク（強制行動不能）中か。</param>
        /// <param name="stepping">ステップ回避中か（I-frame 移動）。</param>
        public void Tick(bool enabled, bool isMoving, bool guarding, bool attacking, bool guardBroken, bool stepping)
        {
            Tick(enabled, isMoving, guarding, attacking, guardBroken, stepping, charging: false, specialAttacking: false);
        }

        /// <summary>
        /// 必殺技（チャージ・発動）を含めて状態を更新する。優先度は
        /// ガードブレイク ＞ ステップ ＞ 必殺技発動 ＞ 必殺技チャージ ＞ 攻撃 ＞ ガード ＞ 移動/Idle（仕様書 §3 / §3.6）。
        /// </summary>
        public void Tick(bool enabled, bool isMoving, bool guarding, bool attacking, bool guardBroken, bool stepping, bool charging, bool specialAttacking)
        {
            PlayerState next;
            if (guardBroken)
            {
                next = PlayerState.GuardBreak;
            }
            else if (stepping)
            {
                next = PlayerState.Step;
            }
            else if (specialAttacking)
            {
                next = PlayerState.Special;
            }
            else if (charging)
            {
                next = PlayerState.SpecialCharge;
            }
            else if (!enabled)
            {
                next = PlayerState.Idle;
            }
            else if (attacking)
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
