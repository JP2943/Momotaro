using Momotaro.Core.Logging;
using Momotaro.Gameplay.Modes;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Tests.EditMode
{
    public sealed class InputBootServiceTests
    {
        private InputActionAsset _asset;

        [SetUp]
        public void SetUp()
        {
            GameLog.SetSink(new TestLogSink());

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var gameplay = _asset.AddActionMap("Gameplay");
            gameplay.AddAction("Move", InputActionType.Value, "<Keyboard>/w");
            gameplay.AddAction("Guard", InputActionType.Button, "<Keyboard>/k");
            _asset.AddActionMap("UI").AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            _asset.AddActionMap("Dialogue").AddAction("Advance", InputActionType.Button, "<Keyboard>/enter");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_asset);
            GameLog.SetSink(null);
        }

        [Test]
        public void Initialize_WithAssetAndModes_CreatesInputAndAppliesInitialMap()
        {
            var modes = new GameModeService(GameMode.Loading);
            var boot = new InputBootService(_asset, modes);

            ServiceInitResult result = boot.Initialize();

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(boot.Input);
            // Loading の Map は UI（GameModeCatalog）。初期適用されているべき。
            Assert.AreEqual("UI", boot.Input.ActiveMapName);
            boot.Dispose();
        }

        [Test]
        public void ModeChange_AfterWiring_SwitchesActionMap()
        {
            var modes = new GameModeService(GameMode.Loading);
            var boot = new InputBootService(_asset, modes);
            boot.Initialize();

            modes.ChangeMode(GameMode.Combat);
            Assert.AreEqual("Gameplay", boot.Input.ActiveMapName);

            modes.ChangeMode(GameMode.Dialogue);
            Assert.AreEqual("Dialogue", boot.Input.ActiveMapName);

            boot.Dispose();
        }

        [Test]
        public void Dispose_UnsubscribesFromModeChanges()
        {
            var modes = new GameModeService(GameMode.Loading);
            var boot = new InputBootService(_asset, modes);
            boot.Initialize();
            InputService input = boot.Input;

            boot.Dispose();

            // 解除後はモード変更しても ActiveMapName が変わらない（購読解除済み）。
            string before = input.ActiveMapName;
            modes.ChangeMode(GameMode.Combat);
            Assert.AreEqual(before, input.ActiveMapName);
        }

        [Test]
        public void Initialize_WithGameplayMoveAndGuard_ExposesPlayerInput()
        {
            var modes = new GameModeService(GameMode.Loading);
            var boot = new InputBootService(_asset, modes);

            boot.Initialize();

            Assert.IsNotNull(boot.PlayerInput, "Gameplay/Move・Guard があれば PlayerInput を公開する");
            Assert.AreEqual(Vector2.zero, boot.PlayerInput.Move);
            boot.Dispose();
        }

        [Test]
        public void Initialize_WithoutAssetAndNoProjectWide_StaysIdle()
        {
            var modes = new GameModeService(GameMode.Loading);
            var boot = new InputBootService(null, modes);

            // project-wide actions が未設定の環境では Input は生成されないが、致命的失敗にはしない。
            ServiceInitResult result = boot.Initialize();

            Assert.IsTrue(result.Success);
            if (InputSystem.actions == null)
            {
                Assert.IsNull(boot.Input);
            }

            boot.Dispose();
        }
    }
}
