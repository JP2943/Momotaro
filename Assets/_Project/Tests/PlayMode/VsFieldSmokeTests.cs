using System.Collections;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Player;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.SceneFlow;
using Momotaro.Presentation.Cameras;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// SCN_VS_Field の操作検証に必要な参照が揃っているかの Smoke Test（Phase1 P1-11）。
    /// Player・Camera の存在と、GameMode 切替後に入力が供給されることを確認する。
    /// Scene が未整備（Player/Camera/SceneMode 未配置）の場合は失敗し、整備漏れを検出する。
    /// </summary>
    public sealed class VsFieldSmokeTests
    {
        private sealed class NullLogSink : ILogSink
        {
            public void Write(LogLevel level, string message) { }
        }

        [SetUp]
        public void SetUp()
        {
            GameLog.SetSink(new NullLogSink());
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.SetSink(null);
        }

        [UnityTest]
        public IEnumerator VsField_HasPlayer_Camera_AndInputAvailable()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.VsField, LoadSceneMode.Single);

            // テスト実行文脈では RuntimeInitializeOnLoadMethod がこの Scene 読込では発火しないため、
            // 直開き相当の常駐（BootstrapRoot）を明示的に用意する。
            if (!BootstrapRoot.HasInstance)
            {
                new GameObject("[TestBootstrapRoot]").AddComponent<BootstrapRoot>();
            }

            // Bootstrap 初期化と GameplaySceneMode による Exploration 切替で入力が供給されるまで待つ。
            float timeout = 8f;
            while (PlayerInputProvider.Current == null && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            var player = Object.FindFirstObjectByType<PlayerStateController>();
            var camera = Object.FindFirstObjectByType<TopDownCameraFollow>();

            Assert.IsNotNull(player, "SCN_VS_Field に PlayerStateController（Player）が必要です。");
            Assert.IsNotNull(camera, "SCN_VS_Field に TopDownCameraFollow（Camera）が必要です。");
            Assert.IsNotNull(PlayerInputProvider.Current,
                "入力が供給されていません。Project-wide Actions が IA_Momotaro か、SceneMode(Exploration) が配置されているか確認してください。");
        }
    }
}
