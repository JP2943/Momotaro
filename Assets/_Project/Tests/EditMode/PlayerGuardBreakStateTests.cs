using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-07 受入修正：ガードブレイクが独立した Gameplay 状態（<see cref="PlayerState.GuardBreak"/>）として表現され、
    /// 攻撃・ガード・移動・入力 Buffer より優先して行動不能になること、攻撃中でも中断して GuardBreak へ移り、ブレイク終了後は
    /// 入力状況へ復帰することを、実際の <see cref="PlayerStateController"/> 更新経路で検証する。
    /// </summary>
    public sealed class PlayerGuardBreakStateTests
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
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            f.SetValue(target, value);
        }

        private static void Tick(PlayerStateController controller) => UpdateMethod.Invoke(controller, null);

        private PlayerAttackComboData MakeCombo()
        {
            var combo = ScriptableObject.CreateInstance<PlayerAttackComboData>();
            _spawned.Add(combo);
            var a = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(a);
            SetPrivate(a, "_startupSeconds", 0.05f);
            SetPrivate(a, "_activeSeconds", 0.10f);
            SetPrivate(a, "_recoverySeconds", 0.10f);
            SetPrivate(combo, "_stages", new[] { a });
            SetPrivate(combo, "_bufferSeconds", 0.30f);
            return combo;
        }

        private PlayerData MakePlayerData(int maxStamina)
        {
            var d = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(d);
            SetPrivate(d, "_maxHp", 100);
            SetPrivate(d, "_defense", 0f);
            SetPrivate(d, "_maxStamina", maxStamina);
            return d;
        }

        private (PlayerStateController controller, PlayerVitalsHolder holder, PlayerFacing facing, PlayerInputState input) MakeSetup(int maxStamina = 20)
        {
            var go = new GameObject("GuardBreakTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetPrivate(holder, "_data", MakePlayerData(maxStamina));
            var controller = go.AddComponent<PlayerStateController>();
            SetPrivate(controller, "_facing", facing);
            SetPrivate(controller, "_attackCombo", MakeCombo());

            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            return (controller, holder, facing, input);
        }

        [Test]
        public void StaminaZeroWhileGuarding_EntersGuardBreak_OverAllInputs()
        {
            var (controller, holder, _, input) = MakeSetup();
            holder.ConsumeStamina(20f); // スタミナ 0 → ブレイク
            Assert.IsTrue(holder.IsGuardBroken);

            // 攻撃・ガード・移動すべて要求してもガードブレイク。
            input.SetAttack(true);
            input.SetGuard(true);
            input.SetMove(new Vector2(1f, 0f));
            Tick(controller);

            Assert.AreEqual(PlayerState.GuardBreak, controller.Current, "行動不能は全入力より優先。");
        }

        [Test]
        public void AttackInProgress_InterruptedByBreak()
        {
            var (controller, holder, facing, input) = MakeSetup();

            input.SetAttack(true);
            Tick(controller);
            Assert.AreEqual(PlayerState.Attack, controller.Current, "まず攻撃に入る。");

            holder.ConsumeStamina(20f); // ブレイク発生
            input.SetAttack(false);
            Tick(controller);

            Assert.AreEqual(PlayerState.GuardBreak, controller.Current, "攻撃を中断して GuardBreak。");
            Assert.IsFalse(facing.IsLocked, "攻撃由来の向きロックは中立化される。");
        }

        [Test]
        public void DuringBreak_InputsIgnored_NoComboBufferRetained()
        {
            var (controller, holder, _, input) = MakeSetup();
            holder.ConsumeStamina(20f); // ブレイク

            // ブレイク中に攻撃を押しても無視され、Buffer も残らない。
            input.SetAttack(true);
            Tick(controller);
            Tick(controller);
            Assert.AreEqual(PlayerState.GuardBreak, controller.Current, "ブレイク中は入力無視。");

            // ブレイク終了。押しっぱなしの攻撃は残留 Buffer で発火しない。
            holder.Tick(1.5f);
            input.SetMove(Vector2.zero);
            input.SetGuard(false);
            Tick(controller);
            Assert.AreEqual(PlayerState.Idle, controller.Current, "終了後、残留 Buffer による攻撃は起きない。");
        }

        [Test]
        public void AfterBreakEnds_ReturnsToInputState()
        {
            var (controller, holder, _, input) = MakeSetup();
            holder.ConsumeStamina(20f);
            Tick(controller);
            Assert.AreEqual(PlayerState.GuardBreak, controller.Current);

            holder.Tick(1.5f); // ブレイク終了（25% 回復）
            Assert.IsFalse(holder.IsGuardBroken);

            input.SetGuard(true);
            Tick(controller);
            Assert.AreEqual(PlayerState.GuardIdle, controller.Current, "ブレイク終了後はガード入力へ復帰。");
        }

        [Test]
        public void GateClosedDuringBreak_NoInconsistency()
        {
            var (controller, holder, facing, input) = MakeSetup();
            holder.ConsumeStamina(20f);
            Tick(controller);
            Assert.AreEqual(PlayerState.GuardBreak, controller.Current);

            // Mode 遮断（Disable 相当）。ブレイク中でも不整合なく、状態は GuardBreak を維持。
            input.SetActive(false);
            Tick(controller);
            Assert.AreEqual(PlayerState.GuardBreak, controller.Current, "遮断中もブレイクは維持され不整合が残らない。");
            Assert.IsFalse(facing.IsLocked);
        }
    }
}
