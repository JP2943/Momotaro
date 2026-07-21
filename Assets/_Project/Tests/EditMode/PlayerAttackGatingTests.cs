using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-02/P2-03B：PlayerStateController の統合挙動を検証する。攻撃開始で Attack 状態＋向きロックへ入り、
    /// GameMode 遮断（Active=false）で攻撃・向きロックが解除され、再有効化しても押しっぱなしの攻撃入力が
    /// 残留しないことを確認する。MonoBehaviour の Update をリフレクションで駆動する決定的テスト。
    /// </summary>
    public sealed class PlayerAttackGatingTests
    {
        private static readonly MethodInfo UpdateMethod =
            typeof(PlayerStateController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            PlayerInputProvider.Current = null;
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(target, value);
        }

        private static void Tick(PlayerStateController controller)
        {
            UpdateMethod.Invoke(controller, null);
        }

        private PlayerAttackComboData MakeCombo()
        {
            var combo = ScriptableObject.CreateInstance<PlayerAttackComboData>();
            _spawned.Add(combo);
            var stages = new AttackData[1];
            var a = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(a);
            SetPrivate(a, "_startupSeconds", 0.05f);
            SetPrivate(a, "_activeSeconds", 0.10f);
            SetPrivate(a, "_recoverySeconds", 0.10f);
            stages[0] = a;
            SetPrivate(combo, "_stages", stages);
            SetPrivate(combo, "_bufferSeconds", 0.30f);
            return combo;
        }

        private (PlayerStateController controller, PlayerFacing facing) MakeController()
        {
            var go = new GameObject("AttackGatingTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var controller = go.AddComponent<PlayerStateController>();
            SetPrivate(controller, "_facing", facing);
            SetPrivate(controller, "_attackCombo", MakeCombo());
            return (controller, facing);
        }

        [Test]
        public void AttackPress_EntersAttack_AndLocksFacing()
        {
            var (controller, facing) = MakeController();
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;

            input.SetAttack(true);
            Tick(controller);

            Assert.AreEqual(PlayerState.Attack, controller.Current, "押下で攻撃状態へ。");
            Assert.IsTrue(facing.IsLocked, "攻撃中は向きロック。");
        }

        [Test]
        public void GateClose_ReleasesAttackAndFacingLock()
        {
            var (controller, facing) = MakeController();
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;

            input.SetAttack(true);
            Tick(controller);
            Assert.AreEqual(PlayerState.Attack, controller.Current);

            input.SetActive(false);
            Tick(controller);

            Assert.AreEqual(PlayerState.Idle, controller.Current, "遮断で攻撃状態が解除。");
            Assert.IsFalse(facing.IsLocked, "遮断で向きロックが解除。");
        }

        [Test]
        public void Reactivate_WithHeldButton_DoesNotResumeAttack()
        {
            var (controller, _) = MakeController();
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;

            input.SetAttack(true);
            Tick(controller);
            input.SetActive(false);
            Tick(controller);
            Assert.AreEqual(PlayerState.Idle, controller.Current);

            input.SetActive(true);
            Tick(controller);

            Assert.AreEqual(PlayerState.Idle, controller.Current, "押しっぱなしの再有効化で攻撃は再開しない。");
        }

        [Test]
        public void ReleaseAndPressAfterReactivate_TriggersAttack()
        {
            var (controller, _) = MakeController();
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;

            input.SetActive(false);
            Tick(controller);
            input.SetActive(true);

            input.SetAttack(false);
            input.SetAttack(true);
            Tick(controller);

            Assert.AreEqual(PlayerState.Attack, controller.Current, "押し直せば攻撃できる。");
        }
    }
}
