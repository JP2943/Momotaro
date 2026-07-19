using Momotaro.Core.Logging;
using Momotaro.Gameplay.Modes;
using Momotaro.Infrastructure.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Tests.EditMode
{
    public sealed class InputServiceTests
    {
        private InputActionAsset _asset;
        private InputActionMap _gameplay;
        private InputActionMap _ui;
        private InputActionMap _dialogue;
        private InputService _service;

        [SetUp]
        public void SetUp()
        {
            GameLog.SetSink(new TestLogSink());

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _gameplay = _asset.AddActionMap("Gameplay");
            _gameplay.AddAction("Move", InputActionType.Value, "<Keyboard>/w");
            _ui = _asset.AddActionMap("UI");
            _ui.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            _dialogue = _asset.AddActionMap("Dialogue");
            _dialogue.AddAction("Advance", InputActionType.Button, "<Keyboard>/enter");

            _service = new InputService(_asset, new InMemoryRebindStore());
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
            Object.DestroyImmediate(_asset);
            GameLog.SetSink(null);
        }

        [Test]
        public void SwitchToActionMap_EnablesOnlyTarget()
        {
            bool ok = _service.SwitchToActionMap("UI");

            Assert.IsTrue(ok);
            Assert.AreEqual("UI", _service.ActiveMapName);
            Assert.IsTrue(_ui.enabled);
            Assert.IsFalse(_gameplay.enabled);
            Assert.IsFalse(_dialogue.enabled);
        }

        [Test]
        public void SwitchToActionMap_Gameplay_ThenUI_TogglesCorrectly()
        {
            _service.SwitchToActionMap("Gameplay");
            Assert.IsTrue(_gameplay.enabled);
            Assert.IsFalse(_ui.enabled);

            _service.SwitchToActionMap("UI");
            Assert.IsFalse(_gameplay.enabled);
            Assert.IsTrue(_ui.enabled);
        }

        [Test]
        public void SwitchToActionMap_Missing_ReturnsFalse()
        {
            bool ok = _service.SwitchToActionMap("DoesNotExist");
            Assert.IsFalse(ok);
        }

        [Test]
        public void OnModeChanged_Combat_ActivatesGameplayMap()
        {
            _service.OnModeChanged(new GameModeChanged(GameMode.Loading, GameMode.Combat));
            Assert.AreEqual("Gameplay", _service.ActiveMapName);
            Assert.IsTrue(_gameplay.enabled);
        }

        [Test]
        public void OnModeChanged_Dialogue_ActivatesDialogueMap()
        {
            _service.OnModeChanged(new GameModeChanged(GameMode.Exploration, GameMode.Dialogue));
            Assert.AreEqual("Dialogue", _service.ActiveMapName);
            Assert.IsTrue(_dialogue.enabled);
        }

        [Test]
        public void Rebind_SaveResetLoad_RestoresOverride()
        {
            InputAction move = _gameplay.FindAction("Move");
            move.ApplyBindingOverride(0, "<Keyboard>/upArrow");

            _service.SaveRebinds();
            _service.ResetRebinds();
            Assert.AreEqual("<Keyboard>/w", move.bindings[0].effectivePath, "Reset で既定へ戻るべき");

            _service.LoadRebinds();
            Assert.AreEqual("<Keyboard>/upArrow", move.bindings[0].effectivePath, "Load で上書きが復元されるべき");
        }
    }
}
