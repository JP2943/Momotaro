using Momotaro.Gameplay.Scenes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// 結果状態（Victory／Defeat）での Retry 入力を読み取る Infrastructure コンポーネント（Phase3.5 P3.5-08。仕様書 §9.2）。
    /// GameMode が非 Gameplay（GameOver）に切り替わり Gameplay Action Map が閉じても Retry を受け付けられるよう、Input System の
    /// 低レベル Device（<see cref="Keyboard"/>／<see cref="Gamepad"/>）を直接読む。<see cref="CombatOutcomeController.RetryArmed"/> が
    /// 立った後にのみ発火し、押下エッジで <see cref="CombatOutcomeController.RequestRetry"/> を 1 回呼ぶ。UI ボタンと同フレームに来ても、
    /// 再読込の二重発行は Session 状態機（Reloading）と Reloader が防ぐ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatRetryInput : MonoBehaviour
    {
        [Tooltip("結果統合コントローラ（未設定なら未解決の間だけ低頻度で探索）。")]
        [SerializeField] private CombatOutcomeController _outcome;

        [Tooltip("未 Bind の間の自動探索間隔（秒）。")]
        [SerializeField] private float _autoLocateInterval = 0.5f;

        private float _locateTimer;

        /// <summary>結果統合コントローラを注入する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(CombatOutcomeController outcome)
        {
            if (outcome != null)
            {
                _outcome = outcome;
            }
        }

        private void Update()
        {
            if (_outcome == null)
            {
                _locateTimer += Time.unscaledDeltaTime;
                if (_locateTimer >= _autoLocateInterval)
                {
                    _locateTimer = 0f;
                    _outcome = FindFirstObjectByType<CombatOutcomeController>();
                }

                if (_outcome == null)
                {
                    return;
                }
            }

            if (_outcome.RetryArmed && RetryPressed())
            {
                _outcome.RequestRetry();
            }
        }

        private static bool RetryPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame
                || kb.numpadEnterKey.wasPressedThisFrame
                || kb.rKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gp = Gamepad.current;
            if (gp != null && (gp.startButton.wasPressedThisFrame || gp.buttonSouth.wasPressedThisFrame))
            {
                return true;
            }

            return false;
        }
    }
}
