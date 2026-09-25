using System;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Session;
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
    public sealed class PlayerInputAdapter : IGameModeListener, IInputReleaseGate, IDisposable
    {
        private const string GameplayMap = "Gameplay";
        private const string MoveAction = "Move";
        private const string GuardAction = "Guard";
        private const string AttackAction = "Attack";
        private const string StepAction = "Step";
        private const string SpecialAttackAction = "SpecialAttack";
        private const string InteractAction = "Interact";
        private const string UiMap = "UI";
        private const string SubmitAction = "Submit";

        private readonly PlayerInputState _state = new PlayerInputState();
        private readonly RespawnSubmitState _submitState = new RespawnSubmitState();
        private readonly InputAction _submit;
        private UnityEngine.InputSystem.Controls.ButtonControl _submitControl;
        private UnityEngine.InputSystem.Controls.ButtonControl _attackControl;
        private UnityEngine.InputSystem.Controls.ButtonControl _stepControl;
        private UnityEngine.InputSystem.Controls.ButtonControl _interactControl;
        private readonly InputAction _move;
        private readonly InputAction _guard;
        private readonly InputAction _attack;
        private readonly InputAction _step;
        private readonly InputAction _special;
        private readonly InputAction _interact;
        private bool _disposed;

        /// <summary>Gameplay 層へ渡す入力。</summary>
        public IPlayerInput Input => _state;

        /// <inheritdoc />
        public bool RequiresRelease => _state.RequiresRelease;

        /// <summary>覚えている再開ボタンの様子（診断・テスト用）。</summary>
        public string HeldSubmitDiagnostics =>
            _submitControl == null
                ? "(覚えていない)"
                : _submitControl.path + " pressed=" + IsPressed(_submitControl);

        /// <inheritdoc />
        public void PollHeldControls()
        {
            // Map を閉じている間に離されたボタンは<b>何も通知してこない</b>。
            // 通知だけを信じると「押しっぱなし」と記録したまま固まり、
            // 次の押下が押下エッジにならず飲み込まれる（実際に踏んだ：再開の 2 回目が効かなくなった）。
            if (!IsPressed(_attackControl))
            {
                _attackControl = null;
                _state.SetAttack(false);
            }

            if (!IsPressed(_stepControl))
            {
                _stepControl = null;
                _state.SetStep(false);
            }

            if (!IsPressed(_interactControl))
            {
                _interactControl = null;
                _state.SetInteract(false);
            }

            if (!IsPressed(_submitControl))
            {
                _submitControl = null;
                _submitState.SetSubmit(false);
                _state.ClearRequireRelease();
            }
        }

        private static bool IsPressed(UnityEngine.InputSystem.Controls.ButtonControl control)
        {
            if (control == null)
            {
                return false;
            }

            // <b>覚えたボタンのデバイスが外れていることがある。</b>
            // 仮想デバイスを足して外すテストでは実際に起きて、外れたあとに値を読むと
            // InvalidOperationException が飛ぶ。例外は入力の callback の中で起きるので、
            // そこから先の入力が丸ごと止まり、<b>無関係なテストが一斉に無反応</b>になる（実際に踏んだ）。
            InputDevice device = control.device;
            return device != null && device.added && control.isPressed;
        }

        /// <summary>再開操作（UI/Submit）の入力（P5-08。§9.1 手順 3）。アクションが無ければ押下は来ない。</summary>
        public IRespawnSubmitInput Submit => _submitState;

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

            // Interact（E／南ボタン）は任意接続（P4-07B）。押下エッジをラッチし、探索の入力仲介が 1 回消費する。
            // 既存の Step（Space／東ボタン）とは割当が重ならない（IA_Momotaro.inputactions）。
            _interact = map.FindAction(InteractAction, throwIfNotFound: false);
            if (_interact != null)
            {
                _interact.started += OnInteractStarted;
                _interact.canceled += OnInteractCanceled;
            }

            // 再開操作は UI マップの Submit（P5-08。§9.1 手順 3）。GameOver では Gameplay マップが
            // 閉じているので、Gameplay 側の入力とは別の口で読む。
            _submit = asset.FindActionMap(UiMap, throwIfNotFound: false)?.FindAction(SubmitAction, throwIfNotFound: false);
            if (_submit != null)
            {
                _submit.started += OnSubmitStarted;
                _submit.canceled += OnSubmitCanceled;
            }
        }

        private void OnSubmitStarted(InputAction.CallbackContext context)
        {
            _submitControl = context.control as UnityEngine.InputSystem.Controls.ButtonControl;
            _submitState.SetSubmit(true);
        }

        private void OnSubmitCanceled(InputAction.CallbackContext context)
        {
            if (IsStillPressed(context, _submitControl))
            {
                return;
            }

            _submitControl = null;
            _submitState.SetSubmit(false);
            _state.ClearRequireRelease();
        }

        /// <summary>
        /// この canceled が<b>物理的な離し</b>ではなく、Action Map を閉じたことによるものかを見る（P5-08。§9.1 末尾）。
        ///
        /// Map を無効化すると進行中の Action は canceled を出す。これを「離した」と記録すると、
        /// Map が戻った瞬間に押しっぱなしのボタンが<b>新しい押下</b>として立ち上がり、
        /// 再開に使ったボタンが到着先の Interact／Step へ化ける。
        /// ボタンがまだ押されているなら、離していないので生の押下状態を保つ。
        /// </summary>
        private static bool IsStillPressed(
            InputAction.CallbackContext context, UnityEngine.InputSystem.Controls.ButtonControl remembered)
        {
            // <b>取り消しの文脈だけを信じない。</b> Map を閉じたことによる canceled では
            // context.control が当てにならないことがある。押し始めに覚えておいたボタンを先に見る。
            if (IsPressed(remembered))
            {
                return true;
            }

            return context.control is UnityEngine.InputSystem.Controls.ButtonControl button && IsPressed(button);
        }

        private void OnInteractStarted(InputAction.CallbackContext context)
        {
            _interactControl = context.control as UnityEngine.InputSystem.Controls.ButtonControl;
            _state.SetInteract(true);
        }

        private void OnInteractCanceled(InputAction.CallbackContext context)
        {
            if (IsStillPressed(context, _interactControl))
            {
                return; // Map を閉じただけ。押しっぱなしを「離した」にしない（§9.1 末尾）。
            }

            _interactControl = null;
            _state.SetInteract(false);
        }

        /// <inheritdoc />
        public void OnModeChanged(GameModeChanged change)
        {
            bool isGameplay = GameModeCatalog.GetProfile(change.Current).ActionMap == GameModeCatalog.ActionMapGameplay;
            _state.SetActive(isGameplay);

            if (!isGameplay)
            {
                return;
            }

            // Gameplay の操作へ戻る瞬間に、再開へ使った物理ボタンがまだ押されているかを見る（P5-08。§9.1 末尾）。
            //
            // <b>ここで見るのは Action ではなくボタンそのもの。</b> Map を閉じている間の押下は
            // Action へ届かないので、Map を開いた瞬間に「押しっぱなし」が新しい押下として
            // 立ち上がってしまう。押されていれば離すまで無効にし、押されていなければ
            // 解放待ちを解く（離してから到着した人の最初の 1 回を飲み込まないため）。
            if (IsPressed(_submitControl))
            {
                _state.RequireRelease();
            }
            else
            {
                _state.ClearRequireRelease();
            }
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
            _attackControl = context.control as UnityEngine.InputSystem.Controls.ButtonControl;
            _state.SetAttack(true);
        }

        private void OnAttackCanceled(InputAction.CallbackContext context)
        {
            if (IsStillPressed(context, _attackControl))
            {
                return; // Map を閉じただけ。押しっぱなしを「離した」にしない（§9.1 末尾）。
            }

            _attackControl = null;
            _state.SetAttack(false);
        }

        private void OnStepStarted(InputAction.CallbackContext context)
        {
            _stepControl = context.control as UnityEngine.InputSystem.Controls.ButtonControl;
            _state.SetStep(true);
        }

        private void OnStepCanceled(InputAction.CallbackContext context)
        {
            if (IsStillPressed(context, _stepControl))
            {
                return; // Map を閉じただけ。押しっぱなしを「離した」にしない（§9.1 末尾）。
            }

            _stepControl = null;
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

            if (_interact != null)
            {
                _interact.started -= OnInteractStarted;
                _interact.canceled -= OnInteractCanceled;
            }

            if (_submit != null)
            {
                _submit.started -= OnSubmitStarted;
                _submit.canceled -= OnSubmitCanceled;
            }

            _disposed = true;
        }
    }
}
