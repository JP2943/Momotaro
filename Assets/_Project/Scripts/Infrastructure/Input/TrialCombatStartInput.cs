using Momotaro.Gameplay.Scenes;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// P4 試遊の明示的な戦闘開始入力（P4-08R。v1.0 §13.1「P4 限定の試遊開始操作から既存の 4 Wave を開始できる」）。
    ///
    /// キーボード Enter（テンキー Enter 含む）／ゲームパッド Start の押下エッジで
    /// <see cref="TrialStageController.RequestCombatStart"/> を 1 回呼ぶ。開始後は何もしない（二重開始しない）。
    /// Retry（<see cref="CombatRetryInput"/>）も Enter を使うが、そちらは結果確定後にしか武装しないため重ならない。
    /// 本編入力（IA_Momotaro）へ裏コマンドを足さない（§13.1）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrialCombatStartInput : MonoBehaviour
    {
        [Tooltip("試遊段階（Scene 構築時に注入。Find* で探さない）。")]
        [SerializeField] private TrialStageController _stage;

        private System.Func<bool> _pressedOverride;

        /// <summary>開始を要求した回数（テスト・診断用）。</summary>
        public int StartCount { get; private set; }

        /// <summary>配線された試遊段階（Scene 検査用）。</summary>
        public TrialStageController Stage => _stage;

        /// <summary>試遊段階を注入する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(TrialStageController stage)
        {
            if (stage != null)
            {
                _stage = stage;
            }
        }

        /// <summary>押下判定を差し替える（テスト。null で実デバイスへ戻す）。</summary>
        public void SetPressedSource(System.Func<bool> pressed)
        {
            _pressedOverride = pressed;
        }

        /// <summary>1 フレームぶんの仲介（Update から呼ばれるが、テストは決定的に直接呼べる）。開始を要求したら true。</summary>
        public bool TickInput()
        {
            if (_stage == null || _stage.EncounterRequested)
            {
                return false;
            }

            bool pressed = _pressedOverride != null ? _pressedOverride() : StartPressed();
            if (!pressed)
            {
                return false;
            }

            StartCount++;
            return _stage.RequestCombatStart();
        }

        private void Update()
        {
            TickInput();
        }

        private static bool StartPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gp = Gamepad.current;
            return gp != null && gp.startButton.wasPressedThisFrame;
        }
    }
}
