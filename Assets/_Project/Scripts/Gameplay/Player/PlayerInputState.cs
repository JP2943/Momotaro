using System;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// <see cref="IPlayerInput"/> の状態を保持する純粋クラス（Phase1 P1-02）。Input System に依存せず、
    /// 入力ソース（アダプタ）から <see cref="SetMove"/> / <see cref="SetGuard"/> で更新される。
    ///
    /// <see cref="SetActive"/> が false のときはゲートが閉じ、Move はゼロ、Guard は解除される
    /// （GameMode が Gameplay でないときの挙動）。
    /// </summary>
    public sealed class PlayerInputState : IPlayerInput
    {
        private bool _active = true;
        private bool _attackHeldRaw;
        private bool _attackLatched;

        /// <inheritdoc />
        public Vector2 Move { get; private set; }

        /// <inheritdoc />
        public bool GuardHeld { get; private set; }

        /// <inheritdoc />
        public bool Active => _active;

        /// <inheritdoc />
        public event Action GuardStarted;

        /// <inheritdoc />
        public event Action GuardCanceled;

        /// <summary>入力ソースから移動値を設定する。ゲートが閉じている間はゼロに保つ。</summary>
        public void SetMove(Vector2 value)
        {
            Move = _active ? value : Vector2.zero;
        }

        /// <summary>入力ソースからガード保持状態を設定する。エッジで通知する。</summary>
        public void SetGuard(bool held)
        {
            bool target = _active && held;
            if (target == GuardHeld)
            {
                return;
            }

            GuardHeld = target;
            if (target)
            {
                GuardStarted?.Invoke();
            }
            else
            {
                GuardCanceled?.Invoke();
            }
        }

        /// <summary>
        /// 入力ソースから攻撃ボタンの生の押下状態を設定する。押下エッジ（false→true）でのみ
        /// 内部ラッチを立て、<see cref="ConsumeAttackPressed"/> で取り出すまで保持する。
        /// 保持（連続 true）では再ラッチしないため、Hold で連続実行されない。
        /// ゲートが閉じている間はラッチしない（生状態の追跡だけ続け、再開時に押しっぱなしが誤発火しないようにする）。
        /// </summary>
        public void SetAttack(bool pressed)
        {
            bool rising = pressed && !_attackHeldRaw;
            _attackHeldRaw = pressed;

            if (_active && rising)
            {
                _attackLatched = true;
            }
        }

        /// <inheritdoc />
        public bool ConsumeAttackPressed()
        {
            if (!_attackLatched)
            {
                return false;
            }

            _attackLatched = false;
            return true;
        }

        /// <summary>
        /// ゲートの開閉を設定する。閉じる（false）と Move をゼロにし、保持中の Guard を解除し、
        /// 未消費の攻撃エッジを破棄する。生の押下状態は保持し、再開時の押しっぱなし誤発火を防ぐ。
        /// </summary>
        public void SetActive(bool active)
        {
            if (_active == active)
            {
                return;
            }

            _active = active;
            if (_active)
            {
                return;
            }

            Move = Vector2.zero;
            _attackLatched = false;
            if (GuardHeld)
            {
                GuardHeld = false;
                GuardCanceled?.Invoke();
            }
        }
    }
}
