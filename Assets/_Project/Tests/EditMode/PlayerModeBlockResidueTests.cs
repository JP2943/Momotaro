using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-12 統合受入（項目 K）：GameMode 遮断（Active=false, 例: Dialogue/UI）で、実行中のステップ（無敵含む）と
    /// 必殺技チャージが即時解除され、予約が残らないことを PlayerStateController レベルで検証する。攻撃側の遮断は
    /// <see cref="PlayerAttackGatingTests"/> が担保するため、ここではステップ・必殺技の残留無しに絞る。
    /// MonoBehaviour の Update をリフレクションで駆動する決定的テスト。
    /// </summary>
    public sealed class PlayerModeBlockResidueTests
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

        private static void Tick(PlayerStateController controller) => UpdateMethod.Invoke(controller, null);

        private SpecialAttackData MakeSpecial()
        {
            var s = ScriptableObject.CreateInstance<SpecialAttackData>();
            _spawned.Add(s);
            // Charge を十分長く取り、1 Tick では自動発動せずチャージ継続状態を作る。
            SetPrivate(s, "_chargeSeconds", 100f);
            SetPrivate(s, "_maxHoldSeconds", 100f);
            return s;
        }

        private StepData MakeStep()
        {
            var s = ScriptableObject.CreateInstance<StepData>();
            _spawned.Add(s);
            // スタミナ 0 コストで Vitals 不要にし、移動秒・無敵窓を十分長くして 1 Tick では終了しないようにする。
            SetPrivate(s, "_staminaCost", 0f);
            SetPrivate(s, "_moveSeconds", 100f);
            SetPrivate(s, "_recoverySeconds", 1f);
            SetPrivate(s, "_invincibleStartSeconds", 0f);
            SetPrivate(s, "_invincibleEndSeconds", 100f);
            SetPrivate(s, "_chainBufferSeconds", 1f);
            return s;
        }

        private PlayerStateController MakeController(bool special, bool step)
        {
            var go = new GameObject("ModeBlockResidueTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var controller = go.AddComponent<PlayerStateController>();
            SetPrivate(controller, "_facing", facing);
            if (special)
            {
                SetPrivate(controller, "_specialData", MakeSpecial());
            }

            if (step)
            {
                SetPrivate(controller, "_stepData", MakeStep());
            }

            return controller;
        }

        [Test]
        public void GateClose_ClearsSpecialCharge_AndHeldReopenDoesNotResume()
        {
            var controller = MakeController(special: true, step: false);
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;

            input.SetSpecialAttack(true);
            Tick(controller);
            Assert.IsTrue(controller.IsSpecialCharging, "長押しでチャージ開始。");

            input.SetActive(false);
            Tick(controller);
            Assert.IsFalse(controller.IsSpecialCharging, "遮断でチャージが即時解除（残留しない）。");

            // 押しっぱなしのまま再有効化しても、要解除ロックにより自動再チャージしない。
            input.SetActive(true);
            Tick(controller);
            Assert.IsFalse(controller.IsSpecialCharging, "再有効化でチャージは自動再開しない（要解除）。");
        }

        [Test]
        public void GateClose_ClearsStepAndInvincibility_AndHeldReopenDoesNotResume()
        {
            var controller = MakeController(special: false, step: true);
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;

            input.SetMove(new Vector2(1f, 0f));
            input.SetStep(true);
            Tick(controller);
            Assert.IsTrue(controller.IsStepping, "押下でステップ開始。");
            Assert.IsTrue(controller.IsInvincible, "ステップ中は無敵。");

            input.SetActive(false);
            Tick(controller);
            Assert.IsFalse(controller.IsStepping, "遮断でステップが即時解除。");
            Assert.IsFalse(controller.IsInvincible, "遮断で無敵も解除（残留しない）。");

            // ステップは押下エッジ入力のため、遮断で予約が消費され再有効化でも自動再開しない。
            input.SetActive(true);
            Tick(controller);
            Assert.IsFalse(controller.IsStepping, "再有効化でステップは自動再開しない。");
        }
    }
}
