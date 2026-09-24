using NUnit.Framework;
using UnityEngine.InputSystem;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// PlayMode の実キー検査が<b>Editor のフォーカスに左右されない</b>ようにする（P5-10）。
    ///
    /// Input System の既定は <see cref="InputSettings.EditorInputBehaviorInPlayMode.PointersAndKeyboardsRespectGameViewFocus"/>
    /// で、<b>Game View にフォーカスが無いとキーボード入力はゲームへ届かない</b>（Editor 側へ回る）。
    /// 無人で走らせる自動実行では Game View にフォーカスが無いのが普通なので、
    /// このままだと実キーのテストが<b>一斉に無反応</b>になる。
    ///
    /// 実際に 3 度踏んだ：全件 PlayMode が 140 秒・全緑から 300 秒超・10 件以上失敗へ変わり、
    /// 「Action Map は有効・ゲートも開いている・timeScale も 1 なのに押下が届かない」という読みにくい形で出た。
    /// ゲームパッドだけが通ることがあったのも、この設定がキーボードとポインタだけを対象にするため。
    ///
    /// ここでは<b>テストの実行中だけ</b>設定を変え、終わったら元へ戻す。
    /// 製品の設定（Project Settings）は変更しない。
    /// </summary>
    [SetUpFixture]
    public sealed class PlayModeInputFocusFixture
    {
        private InputSettings.EditorInputBehaviorInPlayMode _previousEditorBehavior;
        private InputSettings.BackgroundBehavior _previousBackgroundBehavior;

        [OneTimeSetUp]
        public void EnsureInputReachesTheGame()
        {
            InputSettings settings = InputSystem.settings;
            if (settings == null)
            {
                return;
            }

            _previousEditorBehavior = settings.editorInputBehaviorInPlayMode;
            _previousBackgroundBehavior = settings.backgroundBehavior;

            settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        [OneTimeTearDown]
        public void RestoreInputSettings()
        {
            InputSettings settings = InputSystem.settings;
            if (settings == null)
            {
                return;
            }

            settings.editorInputBehaviorInPlayMode = _previousEditorBehavior;
            settings.backgroundBehavior = _previousBackgroundBehavior;
        }
    }
}
