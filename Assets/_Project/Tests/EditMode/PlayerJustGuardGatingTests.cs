using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-08 受入修正：ジャストガード受付窓が「ガードを実際に有効化できる」ときだけ開くことを、実際の
    /// <see cref="PlayerStateController"/> 更新経路で検証する。非キャンセル時間の攻撃中は Guard 押下しても JG 不可、
    /// キャンセル可能時間では攻撃中断後に JG 受付開始、通常の Guard 遷移では JG 可、Guard Break 中は JG 不可。
    /// </summary>
    public sealed class PlayerJustGuardGatingTests
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

        private static object GetPrivate(object target, string field)
        {
            return target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
        }

        private static void Tick(PlayerStateController c) => UpdateMethod.Invoke(c, null);

        private (PlayerStateController c, PlayerInputState input) MakeController(bool withVitals = false, int maxStamina = 20)
        {
            var go = new GameObject("JgGateTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var motor = go.AddComponent<PlayerMotor>();
            if (withVitals)
            {
                var holder = go.AddComponent<PlayerVitalsHolder>();
                var data = ScriptableObject.CreateInstance<PlayerData>();
                _spawned.Add(data);
                SetPrivate(data, "_maxHp", 100);
                SetPrivate(data, "_maxStamina", maxStamina);
                SetPrivate(holder, "_data", data);
            }

            var c = go.AddComponent<PlayerStateController>();
            SetPrivate(c, "_facing", facing);
            SetPrivate(c, "_motor", motor);

            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            return (c, input);
        }

        // 攻撃を開始し（stage1）、内部コンボへ秒オーダーのタイミングを注入する（EditMode の大きめ dt でも窓を跨がない）。
        private void StartAttack(PlayerStateController c)
        {
            var timings = new[]
            {
                new StageTiming(1.0f, 2.0f, 2.0f, 0f),   // ActiveEnd 3.0, cancel@3.0, total 5.0
                new StageTiming(1.0f, 2.0f, 2.0f, 0f),
                new StageTiming(1.0f, 2.0f, 4.0f, 5.0f)
            };
            var combo = new AttackComboMachine(timings);
            SetPrivate(c, "_combo", combo);
            SetPrivate(c, "_attackBuffer", new AttackInputBuffer(0.30f));
            combo.TryStart();
            Tick(c);
            Assert.AreEqual(PlayerState.Attack, c.Current, "前提：攻撃状態で開始。");
        }

        private static void DriveTo(PlayerStateController c, float elapsedInStage1)
        {
            var m = (AttackComboMachine)GetPrivate(c, "_combo");
            m.Interrupt();
            m.TryStart();
            m.Tick(elapsedInStage1);
        }

        [Test]
        public void NonCancelableAttack_GuardInput_NoJustGuardWindow()
        {
            var (c, input) = MakeController();
            StartAttack(c);
            DriveTo(c, 2.0f);           // 判定中（1.0..3.0）。cancel 窓 3.0 未満
            input.SetGuard(true);
            Tick(c);

            Assert.AreEqual(PlayerState.Attack, c.Current, "非キャンセル時間はガードへ移行しない。");
            Assert.IsFalse(c.CanJustGuard, "攻撃中（キャンセル不可）は JG 窓を開かない。");
        }

        [Test]
        public void CancelWindow_GuardInput_InterruptsThenOpensJustGuard()
        {
            var (c, input) = MakeController();
            StartAttack(c);
            DriveTo(c, 4.0f);           // 判定終了後（>3.0）＝キャンセル可能
            input.SetGuard(true);
            Tick(c);

            Assert.AreEqual(PlayerState.GuardIdle, c.Current, "キャンセル可能時間で攻撃を中断し Guard へ。");
            Assert.IsTrue(c.CanJustGuard, "中断して Guard へ移行できるので JG 受付が開く。");
        }

        [Test]
        public void NormalGuardTransition_OpensJustGuard()
        {
            var (c, input) = MakeController();
            input.SetGuard(true);       // 非攻撃中のガード開始
            Tick(c);

            Assert.AreEqual(PlayerState.GuardIdle, c.Current);
            Assert.IsTrue(c.CanJustGuard, "通常の Guard 遷移では JG 可。");
        }

        [Test]
        public void GuardBreak_NoJustGuardWindow()
        {
            var (c, input) = MakeController(withVitals: true, maxStamina: 20);
            var holder = ((Component)c).GetComponent<PlayerVitalsHolder>();
            holder.ConsumeStamina(20f); // スタミナ 0 → ブレイク
            Assert.IsTrue(holder.IsGuardBroken);

            input.SetGuard(true);
            Tick(c);

            Assert.AreEqual(PlayerState.GuardBreak, c.Current, "行動不能。");
            Assert.IsFalse(c.CanJustGuard, "Guard Break 中は JG 不可。");
        }
    }
}
