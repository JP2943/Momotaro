using System.Reflection;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-02 受入修正：PlayerStateController の統合挙動を検証する。攻撃開始で Attack 状態＋向きロックへ入り、
    /// GameMode 遮断（Active=false）で Attack・先行入力・向きロックが解除されること、
    /// 再有効化しても押しっぱなしの攻撃入力が残留しないことを確認する。
    ///
    /// MonoBehaviour の Update をリフレクションで直接駆動し、フレーム進行に依存せず決定的に検証する。
    /// </summary>
    public sealed class PlayerAttackGatingTests
    {
        private static readonly MethodInfo UpdateMethod =
            typeof(PlayerStateController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(target, value);
        }

        private static void Tick(PlayerStateController controller)
        {
            UpdateMethod.Invoke(controller, null);
        }

        private static (GameObject go, PlayerStateController controller, PlayerFacing facing) MakeController()
        {
            var go = new GameObject("AttackGatingTest");
            var facing = go.AddComponent<PlayerFacing>();
            var controller = go.AddComponent<PlayerStateController>();
            SetPrivate(controller, "_facing", facing);
            return (go, controller, facing);
        }

        [Test]
        public void AttackPress_EntersAttack_AndLocksFacing()
        {
            var (go, controller, facing) = MakeController();
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            try
            {
                input.SetAttack(true);   // 押下エッジ
                Tick(controller);

                Assert.AreEqual(PlayerState.Attack, controller.Current, "押下で攻撃状態へ。");
                Assert.IsTrue(facing.IsLocked, "攻撃中は向きロック。");
            }
            finally
            {
                PlayerInputProvider.Current = null;
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GateClose_ReleasesAttackBufferAndFacingLock()
        {
            var (go, controller, facing) = MakeController();
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            try
            {
                input.SetAttack(true);
                Tick(controller);
                Assert.AreEqual(PlayerState.Attack, controller.Current);

                input.SetActive(false);  // GameMode 遮断
                Tick(controller);

                Assert.AreEqual(PlayerState.Idle, controller.Current, "遮断で攻撃状態が解除。");
                Assert.IsFalse(facing.IsLocked, "遮断で向きロックが解除。");
            }
            finally
            {
                PlayerInputProvider.Current = null;
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Reactivate_WithHeldButton_DoesNotResumeAttack()
        {
            var (go, controller, facing) = MakeController();
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            try
            {
                input.SetAttack(true);   // 押下（保持継続）
                Tick(controller);
                input.SetActive(false);  // 遮断
                Tick(controller);
                Assert.AreEqual(PlayerState.Idle, controller.Current);

                input.SetActive(true);   // 再有効化（ボタンは押しっぱなしのまま）
                Tick(controller);

                Assert.AreEqual(PlayerState.Idle, controller.Current, "押しっぱなしの再有効化で攻撃は再開しない。");
                Assert.IsFalse(facing.IsLocked);
            }
            finally
            {
                PlayerInputProvider.Current = null;
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ReleaseAndPressAfterReactivate_TriggersAttack()
        {
            var (go, controller, facing) = MakeController();
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            try
            {
                input.SetActive(false);
                Tick(controller);
                input.SetActive(true);

                input.SetAttack(false);  // 一度離す
                input.SetAttack(true);   // 押し直し = 新しいエッジ
                Tick(controller);

                Assert.AreEqual(PlayerState.Attack, controller.Current, "押し直せば攻撃できる。");
            }
            finally
            {
                PlayerInputProvider.Current = null;
                Object.DestroyImmediate(go);
            }
        }
    }
}
