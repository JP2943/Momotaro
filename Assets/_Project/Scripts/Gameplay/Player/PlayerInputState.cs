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
    public sealed class PlayerInputState : IPlayerInput, IInteractInput
    {
        private bool _active = true;
        private bool _attackHeldRaw;
        private bool _attackLatched;
        private bool _stepHeldRaw;
        private bool _stepLatched;
        private bool _specialHeld;
        private bool _interactHeldRaw;
        private bool _interactLatched;
        private bool _requiresRelease;

        /// <inheritdoc />
        public Vector2 Move { get; private set; }

        /// <inheritdoc />
        public bool GuardHeld { get; private set; }

        /// <inheritdoc />
        public bool Active => _active;

        /// <summary>
        /// 押しているボタンを一度離すまで、攻撃・ステップ・Interact を受け付けない状態か（P5-08。§9.1 末尾）。
        /// </summary>
        public bool RequiresRelease => _requiresRelease;

        /// <summary>
        /// 「一度離すまで使わせない」を立てる（P5-08。仕様書 v1.1 §9.1 末尾）。
        ///
        /// 再開に使った物理ボタンが、到着先の Gameplay 操作へそのまま化けるのを止める。
        /// <b>ラッチを 1 回消すだけでは足りない。</b> Action Map を閉じてから開き直すと、
        /// 押しっぱなしのボタンが「新しい押下」として立ち上がるので、押下エッジ自体を
        /// 離すまで無効にしておく必要がある（仕様書がそこまで指定している）。
        /// </summary>
        public void RequireRelease()
        {
            _requiresRelease = true;
            _attackLatched = false;
            _stepLatched = false;
            _interactLatched = false;
        }

        /// <summary>解放待ちを解く（押していないことが分かっているとき）。</summary>
        public void ClearRequireRelease()
        {
            _requiresRelease = false;
        }

        /// <summary>
        /// 解放待ちなら、生の押下だけ追って押下エッジを捨てる。
        ///
        /// <b>ここでは解かない。</b> 解く条件は「再開に使った<b>そのボタン</b>が離れたこと」で、
        /// それを知っているのは実デバイスを見られる入力ソースだけ。Gameplay 側のボタンが
        /// 押されていないことを理由に解くと、Map を開き直した直後（どのボタンも押されていないと
        /// 記録されている瞬間）に即座に解けてしまい、待ちの意味が無くなる（実際に踏んだ）。
        /// </summary>
        private bool ConsumeReleaseGate()
        {
            return _requiresRelease;
        }

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

            if (ConsumeReleaseGate())
            {
                return; // 再開に使ったボタンを離すまでは受け付けない（§9.1 末尾）。
            }

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
        /// 入力ソースからステップボタンの生の押下状態を設定する。押下エッジ（false→true）でのみラッチする（攻撃と同様）。
        /// ゲートが閉じている間はラッチしない。
        /// </summary>
        public void SetStep(bool pressed)
        {
            bool rising = pressed && !_stepHeldRaw;
            _stepHeldRaw = pressed;

            if (ConsumeReleaseGate())
            {
                return; // 再開に使ったボタンを離すまでは受け付けない（§9.1 末尾）。
            }

            if (_active && rising)
            {
                _stepLatched = true;
            }
        }

        /// <inheritdoc />
        public bool ConsumeStepPressed()
        {
            if (!_stepLatched)
            {
                return false;
            }

            _stepLatched = false;
            return true;
        }

        /// <summary>
        /// 入力ソースから Interact ボタンの生の押下状態を設定する。押下エッジ（false→true）でのみラッチする（Step と同様）。
        /// ゲートが閉じている間はラッチしない（P4-07B）。
        /// </summary>
        public void SetInteract(bool pressed)
        {
            bool rising = pressed && !_interactHeldRaw;
            _interactHeldRaw = pressed;

            if (ConsumeReleaseGate())
            {
                return; // 再開に使ったボタンを離すまでは受け付けない（§9.1 末尾）。
            }

            if (_active && rising)
            {
                _interactLatched = true;
            }
        }

        /// <inheritdoc />
        public bool InteractPressed => _interactLatched;

        /// <inheritdoc />
        public bool ConsumeInteractPressed()
        {
            if (!_interactLatched)
            {
                return false;
            }

            _interactLatched = false;
            return true;
        }

        /// <inheritdoc />
        public void DiscardInteractPressed()
        {
            _interactLatched = false;
        }

        /// <inheritdoc />
        public bool SpecialAttackHeld => _specialHeld;

        /// <summary>必殺技ボタンの保持状態を設定する。ゲートが閉じている間は解除する。</summary>
        public void SetSpecialAttack(bool held)
        {
            _specialHeld = _active && held;
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
            _stepLatched = false;
            _interactLatched = false;
            _specialHeld = false;
            if (GuardHeld)
            {
                GuardHeld = false;
                GuardCanceled?.Invoke();
            }
        }
    }
}
