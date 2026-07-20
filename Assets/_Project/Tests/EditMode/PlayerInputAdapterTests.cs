using System;
using Momotaro.Gameplay.Modes;
using Momotaro.Infrastructure.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Tests.EditMode
{
    public sealed class PlayerInputAdapterTests
    {
        private static InputActionAsset MakeAsset(bool includeGuard)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap gp = asset.AddActionMap("Gameplay");
            gp.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick");
            if (includeGuard)
            {
                gp.AddAction("Guard", InputActionType.Button, "<Keyboard>/k");
            }

            return asset;
        }

        private static InputActionAsset MakeAssetWithAttack()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap gp = asset.AddActionMap("Gameplay");
            gp.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick");
            gp.AddAction("Guard", InputActionType.Button, "<Keyboard>/k");
            gp.AddAction("Attack", InputActionType.Button, "<Keyboard>/j");
            return asset;
        }

        [Test]
        public void Constructor_WithAttackAction_ExposesNoPendingAttackEdge()
        {
            InputActionAsset asset = MakeAssetWithAttack();
            var adapter = new PlayerInputAdapter(asset);

            Assert.IsNotNull(adapter.Input);
            Assert.IsTrue(adapter.Input.Active, "既定でゲートは開いている。");
            Assert.IsFalse(adapter.Input.ConsumeAttackPressed(), "初期状態では攻撃エッジは無い。");

            adapter.Dispose();
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void Constructor_WithoutAttackAction_StillConstructs()
        {
            // Attack は任意接続。無くても Move/Guard があれば構築できる（P2-02）。
            InputActionAsset asset = MakeAsset(includeGuard: true);
            var adapter = new PlayerInputAdapter(asset);

            Assert.IsNotNull(adapter.Input);
            Assert.IsFalse(adapter.Input.ConsumeAttackPressed());

            adapter.Dispose();
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void Constructor_WithValidAsset_ExposesInputWithZeroMove()
        {
            InputActionAsset asset = MakeAsset(includeGuard: true);
            var adapter = new PlayerInputAdapter(asset);

            Assert.IsNotNull(adapter.Input);
            Assert.AreEqual(Vector2.zero, adapter.Input.Move);
            Assert.IsFalse(adapter.Input.GuardHeld);

            adapter.Dispose();
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void Constructor_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PlayerInputAdapter(null));
        }

        [Test]
        public void Constructor_MissingGuardAction_Throws()
        {
            InputActionAsset asset = MakeAsset(includeGuard: false);
            Assert.Throws<ArgumentException>(() => new PlayerInputAdapter(asset));
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void OnModeChanged_AndDispose_AreSafe()
        {
            InputActionAsset asset = MakeAsset(includeGuard: true);
            var adapter = new PlayerInputAdapter(asset);

            // 非 Gameplay → Gameplay の遷移で例外が出ない。
            adapter.OnModeChanged(new GameModeChanged(GameMode.Loading, GameMode.Dialogue));
            adapter.OnModeChanged(new GameModeChanged(GameMode.Dialogue, GameMode.Combat));

            adapter.Dispose();
            Assert.DoesNotThrow(() => adapter.Dispose(), "Dispose は多重呼び出しでも安全");

            UnityEngine.Object.DestroyImmediate(asset);
        }
    }
}
