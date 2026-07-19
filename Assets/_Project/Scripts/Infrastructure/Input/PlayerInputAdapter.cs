using System;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// IA_Momotaro の Gameplay/Move・Guard を読み取り、<see cref="PlayerInputState"/> へ反映する
    /// Infrastructure 側アダプタ（Phase1 P1-02）。Gameplay 層は <see cref="Input"/> を通じてのみ入力に触れる。
    ///
    /// GameMode 変更を購読し、Gameplay 以外のときはゲートを閉じて Move ゼロ・Guard 解除にする。
    /// 使用終了時は <see cref="Dispose"/> で InputAction のコールバック購読を必ず解除する。
    /// </summary>
    public sealed class PlayerInputAdapter : IGameModeListener, IDisposable
    {
        private const string GameplayMap = "Gameplay";
        private const string MoveAction = "Move";
        private const string GuardAction = "Guard";

        private readonly PlayerInputState _state = new PlayerInputState();
        private readonly InputAction _move;
        private readonly InputAction _guard;
        private bool _disposed;

        /// <summary>Gameplay 層へ渡す入力。</summary>
        public IPlayerInput Input => _state;

        /// <param name="asset">Gameplay マップに Move / Guard を持つ InputActionAsset（IA_Momotaro）。</param>
        /// <exception cref="ArgumentNullException">asset が null。</exception>
        /// <exception cref="ArgumentException">Gameplay/Move または Gameplay/Guard が見つからない。</exception>
        public PlayerInputAdapter(InputActionAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            InputActionMap map = asset.FindActionMap(GameplayMap, throwIfNotFound: false);
            _move = map?.FindAction(MoveAction, throwIfNotFound: false);
            _guard = map?.FindAction(GuardAction, throwIfNotFound: false);
            if (_move == null || _guard == null)
            {
                throw new ArgumentException("InputActionAsset must contain Gameplay/Move and Gameplay/Guard.");
            }

            _move.performed += OnMovePerformed;
            _move.canceled += OnMoveCanceled;
            _guard.started += OnGuardStarted;
            _guard.canceled += OnGuardCanceled;
        }

        /// <inheritdoc />
        public void OnModeChanged(GameModeChanged change)
        {
            bool isGameplay = GameModeCatalog.GetProfile(change.Current).ActionMap == GameModeCatalog.ActionMapGameplay;
            _state.SetActive(isGameplay);
        }

        private void OnMovePerformed(InputAction.CallbackContext context)
        {
            _state.SetMove(context.ReadValue<UnityEngine.Vector2>());
        }

        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            _state.SetMove(UnityEngine.Vector2.zero);
        }

        private void OnGuardStarted(InputAction.CallbackContext context)
        {
            _state.SetGuard(true);
        }

        private void OnGuardCanceled(InputAction.CallbackContext context)
        {
            _state.SetGuard(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _move.performed -= OnMovePerformed;
            _move.canceled -= OnMoveCanceled;
            _guard.started -= OnGuardStarted;
            _guard.canceled -= OnGuardCanceled;
            _disposed = true;
        }
    }
}
