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
        private const string AttackAction = "Attack";
        private const string StepAction = "Step";
        private const string SpecialAttackAction = "SpecialAttack";

        private readonly PlayerInputState _state = new PlayerInputState();
        private readonly InputAction _move;
        private readonly InputAction _guard;
        private readonly InputAction _attack;
        private readonly InputAction _step;
        private readonly InputAction _special;
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

            // Attack / Step は任意接続（P2-02 / P2-09）。存在すれば押下エッジを供給し、無ければその入力なしで動作する。
            _attack = map.FindAction(AttackAction, throwIfNotFound: false);
            _step = map.FindAction(StepAction, throwIfNotFound: false);

            _move.performed += OnMovePerformed;
            _move.canceled += OnMoveCanceled;
            _guard.started += OnGuardStarted;
            _guard.canceled += OnGuardCanceled;
            if (_attack != null)
            {
                _attack.started += OnAttackStarted;
                _attack.canceled += OnAttackCanceled;
            }

            if (_step != null)
            {
                _step.started += OnStepStarted;
                _step.canceled += OnStepCanceled;
            }

            // SpecialAttack は任意接続（P2-10）。保持状態を供給する。
            _special = map.FindAction(SpecialAttackAction, throwIfNotFound: false);
            if (_special != null)
            {
                _special.started += OnSpecialStarted;
                _special.canceled += OnSpecialCanceled;
            }
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

        private void OnAttackStarted(InputAction.CallbackContext context)
        {
            _state.SetAttack(true);
        }

        private void OnAttackCanceled(InputAction.CallbackContext context)
        {
            _state.SetAttack(false);
        }

        private void OnStepStarted(InputAction.CallbackContext context)
        {
            _state.SetStep(true);
        }

        private void OnStepCanceled(InputAction.CallbackContext context)
        {
            _state.SetStep(false);
        }

        private void OnSpecialStarted(InputAction.CallbackContext context)
        {
            _state.SetSpecialAttack(true);
        }

        private void OnSpecialCanceled(InputAction.CallbackContext context)
        {
            _state.SetSpecialAttack(false);
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
            if (_attack != null)
            {
                _attack.started -= OnAttackStarted;
                _attack.canceled -= OnAttackCanceled;
            }

            if (_step != null)
            {
                _step.started -= OnStepStarted;
                _step.canceled -= OnStepCanceled;
            }

            if (_special != null)
            {
                _special.started -= OnSpecialStarted;
                _special.canceled -= OnSpecialCanceled;
            }

            _disposed = true;
        }
    }
}
