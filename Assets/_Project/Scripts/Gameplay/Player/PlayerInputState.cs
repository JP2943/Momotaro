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

        /// <inheritdoc />
        public Vector2 Move { get; private set; }

        /// <inheritdoc />
        public bool GuardHeld { get; private set; }

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
        /// ゲートの開閉を設定する。閉じる（false）と Move をゼロにし、保持中の Guard を解除する。
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
            if (GuardHeld)
            {
                GuardHeld = false;
                GuardCanceled?.Invoke();
            }
        }
    }
}
